using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// material id 기반 지형 재질 정의를 모아두고, 런타임에 Texture2DArray 세트를 빌드한다.
/// </summary>
[CreateAssetMenu(fileName = "TerrainMaterialLibrary", menuName = "A_Game/Terrain Material Library")]
public sealed class TerrainMaterialLibrary : ScriptableObject
{
    private const int MaxSupportedMaterialId = 255;

    [SerializeField] private TerrainMaterialDefinition[] _materials = Array.Empty<TerrainMaterialDefinition>();

    public bool TryBuildRuntimeResources(out RuntimeResources resources, out string error)
    {
        resources = null;
        error = null;

        if (_materials == null || _materials.Length == 0)
        {
            error = "Terrain material library is empty.";
            return false;
        }

        Dictionary<int, TerrainMaterialDefinition> materialsById = new Dictionary<int, TerrainMaterialDefinition>();
        TerrainMaterialDefinition fallback = null;
        int maxMaterialId = 0;

        for (int i = 0; i < _materials.Length; i++)
        {
            TerrainMaterialDefinition definition = _materials[i];
            if (definition == null)
            {
                continue;
            }

            if (definition.Id < 1 || definition.Id > MaxSupportedMaterialId)
            {
                error = $"Material id {definition.Id} is out of range. Supported range is 1..{MaxSupportedMaterialId}.";
                return false;
            }

            if (!definition.IsComplete)
            {
                error = $"Material id {definition.Id} is missing one or more textures.";
                return false;
            }

            if (!materialsById.TryAdd(definition.Id, definition))
            {
                error = $"Material id {definition.Id} is duplicated in {name}.";
                return false;
            }

            fallback ??= definition;
            maxMaterialId = Mathf.Max(maxMaterialId, definition.Id);
        }

        if (fallback == null)
        {
            error = "Terrain material library has no valid material entries.";
            return false;
        }

        TerrainMaterialDefinition[] packedDefinitions = new TerrainMaterialDefinition[maxMaterialId];
        Vector4[] baseColors = new Vector4[maxMaterialId];

        for (int materialId = 1; materialId <= maxMaterialId; materialId++)
        {
            TerrainMaterialDefinition definition = materialsById.TryGetValue(materialId, out TerrainMaterialDefinition found)
                ? found
                : fallback;

            packedDefinitions[materialId - 1] = definition;
            baseColors[materialId - 1] = definition.BaseColor;
        }

        RuntimeResources builtResources = null;

        try
        {
            builtResources = new RuntimeResources
            {
                MaterialCount = maxMaterialId,
                BaseColors = baseColors,
                BaseMaps = CreateTextureArray(packedDefinitions, definition => definition.BaseMap, false, "BaseMap"),
                NormalMaps = CreateTextureArray(packedDefinitions, definition => definition.NormalMap, true, "NormalMap"),
                RoughnessMaps = CreateTextureArray(packedDefinitions, definition => definition.RoughnessMap, true, "RoughnessMap"),
                AOMaps = CreateTextureArray(packedDefinitions, definition => definition.AOMap, true, "AOMap"),
                HeightMaps = CreateTextureArray(packedDefinitions, definition => definition.HeightMap, true, "HeightMap")
            };

            resources = builtResources;
            return true;
        }
        catch (Exception exception)
        {
            builtResources?.Release();
            error = exception.Message;
            return false;
        }
    }

    public void ApplyToMaterial(Material targetMaterial, RuntimeResources resources)
    {
        if (targetMaterial == null || resources == null)
        {
            return;
        }

        targetMaterial.SetTexture("_BaseMapArray", resources.BaseMaps);
        targetMaterial.SetTexture("_NormalMapArray", resources.NormalMaps);
        targetMaterial.SetTexture("_RoughnessMapArray", resources.RoughnessMaps);
        targetMaterial.SetTexture("_AOMapArray", resources.AOMaps);
        targetMaterial.SetTexture("_HeightMapArray", resources.HeightMaps);
        targetMaterial.SetInt("_MaterialCount", resources.MaterialCount);
        targetMaterial.SetVectorArray("_MaterialBaseColors", resources.BaseColors);
    }

    private static Texture2DArray CreateTextureArray(
        TerrainMaterialDefinition[] definitions,
        Func<TerrainMaterialDefinition, Texture2D> selector,
        bool linear,
        string label)
    {
        Texture2D reference = selector(definitions[0]);
        if (reference == null)
        {
            throw new InvalidOperationException($"{label} reference texture is missing.");
        }

        int sliceCount = definitions.Length;
        Texture2DArray array = new Texture2DArray(reference.width, reference.height, sliceCount, TextureFormat.RGBA32, true, linear)
        {
            wrapMode = reference.wrapMode,
            filterMode = reference.filterMode,
            anisoLevel = reference.anisoLevel
        };

        for (int sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
        {
            Texture2D source = selector(definitions[sliceIndex]);
            ValidateTextureCompatibility(source, reference, label, sliceIndex + 1);
            Color[] pixels = ReadTexturePixels(source, reference.width, reference.height, linear);
            array.SetPixels(pixels, sliceIndex, 0);
        }

        array.Apply(true, true);
        return array;
    }

    private static void ValidateTextureCompatibility(Texture2D source, Texture2D reference, string label, int materialId)
    {
        if (source == null)
        {
            throw new InvalidOperationException($"{label} texture is missing for material id {materialId}.");
        }

        if (source.width != reference.width || source.height != reference.height)
        {
            throw new InvalidOperationException(
                $"{label} texture size mismatch at material id {materialId}. " +
                $"Expected {reference.width}x{reference.height}, got {source.width}x{source.height}.");
        }
    }

    private static Color[] ReadTexturePixels(Texture2D source, int width, int height, bool linear)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32,
            linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);

        RenderTexture previous = RenderTexture.active;
        Texture2D readableTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);

        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readableTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            readableTexture.Apply(false, false);
            return readableTexture.GetPixels();
        }
        finally
        {
            RenderTexture.active = previous;

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(readableTexture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(readableTexture);
            }

            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    [Serializable]
    public sealed class TerrainMaterialDefinition
    {
        [Min(1)] public int Id = 1;
        public Color BaseColor = Color.white;
        public Texture2D BaseMap;
        public Texture2D NormalMap;
        public Texture2D RoughnessMap;
        public Texture2D AOMap;
        public Texture2D HeightMap;

        public bool IsComplete =>
            BaseMap != null &&
            NormalMap != null &&
            RoughnessMap != null &&
            AOMap != null &&
            HeightMap != null;
    }

    public sealed class RuntimeResources
    {
        public int MaterialCount;
        public Vector4[] BaseColors;
        public Texture2DArray BaseMaps;
        public Texture2DArray NormalMaps;
        public Texture2DArray RoughnessMaps;
        public Texture2DArray AOMaps;
        public Texture2DArray HeightMaps;

        public void Release()
        {
            DestroyTexture(BaseMaps);
            DestroyTexture(NormalMaps);
            DestroyTexture(RoughnessMaps);
            DestroyTexture(AOMaps);
            DestroyTexture(HeightMaps);

            BaseMaps = null;
            NormalMaps = null;
            RoughnessMaps = null;
            AOMaps = null;
            HeightMaps = null;
            BaseColors = null;
            MaterialCount = 0;
        }

        private static void DestroyTexture(Texture texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(texture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
