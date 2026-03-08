using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// material id 기반 지형 재질 정의와 prebaked Texture2DArray 세트를 보관한다.
/// </summary>
[CreateAssetMenu(fileName = "TerrainMaterialLibrary", menuName = "A_Game/Terrain Material Library")]
public sealed class TerrainMaterialLibrary : ScriptableObject
{
    private const int MaxSupportedMaterialId = 255;

    [SerializeField] private TerrainMaterialDefinition[] _materials = Array.Empty<TerrainMaterialDefinition>();

    [Header("Bake Settings")]
    [SerializeField, Min(1)] private int _baseMapDownscale = 1;
    [SerializeField, Min(1)] private int _maskMapDownscale = 2;
    [SerializeField] private bool _baseMapMipChain = true;
    [SerializeField] private bool _maskMapMipChain = false;

    [Header("Baked Arrays")]
    [SerializeField, HideInInspector] private Texture2DArray _baseMapArray;
    [SerializeField, HideInInspector] private Texture2DArray _maskMapArray;
    [SerializeField, HideInInspector] private Vector4[] _materialBaseColors = Array.Empty<Vector4>();
    [SerializeField, HideInInspector] private int _materialCount;

    public bool HasBakedResources =>
        _materialCount > 0 &&
        _baseMapArray != null &&
        _maskMapArray != null &&
        _materialBaseColors != null &&
        _materialBaseColors.Length == _materialCount;

    public Texture2DArray BaseMapArray => _baseMapArray;
    public Texture2DArray MaskMapArray => _maskMapArray;
    public int BaseMapDownscale => Mathf.Max(1, _baseMapDownscale);
    public int MaskMapDownscale => Mathf.Max(1, _maskMapDownscale);
    public bool BaseMapMipChain => _baseMapMipChain;
    public bool MaskMapMipChain => _maskMapMipChain;

    public string GetDisplayName(byte materialId)
    {
        for (int i = 0; i < _materials.Length; i++)
        {
            TerrainMaterialDefinition definition = _materials[i];
            if (definition != null && definition.Id == materialId)
            {
                return string.IsNullOrWhiteSpace(definition.DisplayName)
                    ? $"Material {materialId}"
                    : definition.DisplayName;
            }
        }

        return $"Material {materialId}";
    }

    public bool TryApplyToMaterial(Material targetMaterial, out string error)
    {
        error = null;

        if (targetMaterial == null)
        {
            error = "Terrain material is not assigned.";
            return false;
        }

        if (!HasBakedResources)
        {
            error = $"{name} has no baked texture arrays. Bake the library in the editor first.";
            return false;
        }

        targetMaterial.SetTexture("_BaseMapArray", _baseMapArray);
        targetMaterial.SetTexture("_MaskMapArray", _maskMapArray);
        targetMaterial.SetInt("_MaterialCount", _materialCount);
        targetMaterial.SetVectorArray("_MaterialBaseColors", _materialBaseColors);
        return true;
    }

    public bool TryBuildBakeInput(out TerrainMaterialDefinition[] packedDefinitions, out Vector4[] baseColors, out string error)
    {
        packedDefinitions = null;
        baseColors = null;
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

        packedDefinitions = new TerrainMaterialDefinition[maxMaterialId];
        baseColors = new Vector4[maxMaterialId];

        for (int materialId = 1; materialId <= maxMaterialId; materialId++)
        {
            TerrainMaterialDefinition definition = materialsById.TryGetValue(materialId, out TerrainMaterialDefinition found)
                ? found
                : fallback;

            packedDefinitions[materialId - 1] = definition;
            baseColors[materialId - 1] = definition.BaseColor;
        }

        return true;
    }

    public void SetBakedResources(
        Texture2DArray baseMapArray,
        Texture2DArray maskMapArray,
        Vector4[] materialBaseColors,
        int materialCount)
    {
        _baseMapArray = baseMapArray;
        _maskMapArray = maskMapArray;
        _materialBaseColors = materialBaseColors ?? Array.Empty<Vector4>();
        _materialCount = materialCount;
    }

    public void ClearBakedResources()
    {
        _baseMapArray = null;
        _maskMapArray = null;
        _materialBaseColors = Array.Empty<Vector4>();
        _materialCount = 0;
    }

    public static Texture2DArray CreateTextureArray(
        TerrainMaterialDefinition[] definitions,
        Func<TerrainMaterialDefinition, Texture2D> selector,
        int downscale,
        bool mipChain,
        bool linear,
        string label)
    {
        Texture2D reference = selector(definitions[0]);
        if (reference == null)
        {
            throw new InvalidOperationException($"{label} reference texture is missing.");
        }

        int sliceCount = definitions.Length;
        int width = Mathf.Max(1, reference.width / Mathf.Max(1, downscale));
        int height = Mathf.Max(1, reference.height / Mathf.Max(1, downscale));
        Texture2DArray array = new Texture2DArray(width, height, sliceCount, TextureFormat.RGB24, mipChain, linear)
        {
            wrapMode = reference.wrapMode,
            filterMode = reference.filterMode,
            anisoLevel = reference.anisoLevel
        };

        for (int sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
        {
            Texture2D source = selector(definitions[sliceIndex]);
            ValidateTextureCompatibility(source, reference, label, sliceIndex + 1);
            Color[] pixels = ReadTexturePixels(source, width, height, linear);
            array.SetPixels(pixels, sliceIndex, 0);
        }

        array.Apply(mipChain, false);
        return array;
    }

    public static Texture2DArray CreatePackedMaskArray(
        TerrainMaterialDefinition[] definitions,
        int downscale,
        bool mipChain)
    {
        Texture2D reference = definitions[0].RoughnessMap;
        if (reference == null)
        {
            throw new InvalidOperationException("Mask reference texture is missing.");
        }

        int sliceCount = definitions.Length;
        int width = Mathf.Max(1, reference.width / Mathf.Max(1, downscale));
        int height = Mathf.Max(1, reference.height / Mathf.Max(1, downscale));
        Texture2DArray array = new Texture2DArray(width, height, sliceCount, TextureFormat.RGB24, mipChain, true)
        {
            wrapMode = reference.wrapMode,
            filterMode = reference.filterMode,
            anisoLevel = reference.anisoLevel
        };

        for (int sliceIndex = 0; sliceIndex < sliceCount; sliceIndex++)
        {
            TerrainMaterialDefinition definition = definitions[sliceIndex];
            ValidateTextureCompatibility(definition.RoughnessMap, reference, "RoughnessMap", sliceIndex + 1);
            ValidateTextureCompatibility(definition.AOMap, reference, "AOMap", sliceIndex + 1);
            ValidateTextureCompatibility(definition.HeightMap, reference, "HeightMap", sliceIndex + 1);

            Color[] roughnessPixels = ReadTexturePixels(definition.RoughnessMap, width, height, true);
            Color[] aoPixels = ReadTexturePixels(definition.AOMap, width, height, true);
            Color[] heightPixels = ReadTexturePixels(definition.HeightMap, width, height, true);
            Color[] packedPixels = new Color[roughnessPixels.Length];

            for (int pixelIndex = 0; pixelIndex < packedPixels.Length; pixelIndex++)
            {
                packedPixels[pixelIndex] = new Color(
                    aoPixels[pixelIndex].r,
                    roughnessPixels[pixelIndex].r,
                    heightPixels[pixelIndex].r,
                    1f);
            }

            array.SetPixels(packedPixels, sliceIndex, 0);
        }

        array.Apply(mipChain, false);
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
        public string DisplayName = "Material";
        public Color BaseColor = Color.white;
        public Texture2D BaseMap;
        public Texture2D RoughnessMap;
        public Texture2D AOMap;
        public Texture2D HeightMap;

        public bool IsComplete =>
            BaseMap != null &&
            RoughnessMap != null &&
            AOMap != null &&
            HeightMap != null;
    }
}
