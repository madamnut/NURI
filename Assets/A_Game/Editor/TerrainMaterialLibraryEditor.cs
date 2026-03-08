using System;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TerrainMaterialLibrary))]
public sealed class TerrainMaterialLibraryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        TerrainMaterialLibrary library = (TerrainMaterialLibrary)target;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            library.HasBakedResources
                ? "Baked texture arrays are ready."
                : "Baked texture arrays are missing. Click Bake before entering play mode.",
            library.HasBakedResources ? MessageType.Info : MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Bake Texture Arrays"))
            {
                BakeLibrary(library);
            }

            if (GUILayout.Button("Clear Baked Arrays"))
            {
                ClearBakedArrays(library);
            }
        }
    }

    private static void BakeLibrary(TerrainMaterialLibrary library)
    {
        if (!library.TryBuildBakeInput(out TerrainMaterialLibrary.TerrainMaterialDefinition[] definitions, out Vector4[] baseColors, out string error))
        {
            Debug.LogError($"Failed to bake terrain material library: {error}", library);
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(library);
        if (string.IsNullOrEmpty(assetPath))
        {
            Debug.LogError("Terrain material library asset path is invalid.", library);
            return;
        }

        Texture2DArray baseMapArray = null;
        Texture2DArray normalMapArray = null;
        Texture2DArray roughnessMapArray = null;
        Texture2DArray aoMapArray = null;
        Texture2DArray heightMapArray = null;

        try
        {
            baseMapArray = TerrainMaterialLibrary.CreateTextureArray(definitions, definition => definition.BaseMap, false, "BaseMap");
            normalMapArray = TerrainMaterialLibrary.CreateTextureArray(definitions, definition => definition.NormalMap, true, "NormalMap");
            roughnessMapArray = TerrainMaterialLibrary.CreateTextureArray(definitions, definition => definition.RoughnessMap, true, "RoughnessMap");
            aoMapArray = TerrainMaterialLibrary.CreateTextureArray(definitions, definition => definition.AOMap, true, "AOMap");
            heightMapArray = TerrainMaterialLibrary.CreateTextureArray(definitions, definition => definition.HeightMap, true, "HeightMap");

            baseMapArray.name = $"{library.name}_BaseMapArray";
            normalMapArray.name = $"{library.name}_NormalMapArray";
            roughnessMapArray.name = $"{library.name}_RoughnessMapArray";
            aoMapArray.name = $"{library.name}_AOMapArray";
            heightMapArray.name = $"{library.name}_HeightMapArray";

            Undo.RecordObject(library, "Bake Terrain Material Library");
            DestroyOldSubAsset(library.BaseMapArray);
            DestroyOldSubAsset(library.NormalMapArray);
            DestroyOldSubAsset(library.RoughnessMapArray);
            DestroyOldSubAsset(library.AOMapArray);
            DestroyOldSubAsset(library.HeightMapArray);

            AssetDatabase.AddObjectToAsset(baseMapArray, library);
            AssetDatabase.AddObjectToAsset(normalMapArray, library);
            AssetDatabase.AddObjectToAsset(roughnessMapArray, library);
            AssetDatabase.AddObjectToAsset(aoMapArray, library);
            AssetDatabase.AddObjectToAsset(heightMapArray, library);

            library.SetBakedResources(
                baseMapArray,
                normalMapArray,
                roughnessMapArray,
                aoMapArray,
                heightMapArray,
                baseColors,
                definitions.Length);

            EditorUtility.SetDirty(library);
            AssetDatabase.ImportAsset(assetPath);
            AssetDatabase.SaveAssets();
        }
        catch (Exception exception)
        {
            DestroyIfTemporary(baseMapArray);
            DestroyIfTemporary(normalMapArray);
            DestroyIfTemporary(roughnessMapArray);
            DestroyIfTemporary(aoMapArray);
            DestroyIfTemporary(heightMapArray);
            Debug.LogError($"Failed to bake terrain material library: {exception.Message}", library);
        }
    }

    private static void ClearBakedArrays(TerrainMaterialLibrary library)
    {
        Undo.RecordObject(library, "Clear Terrain Material Library Bake");

        DestroyOldSubAsset(library.BaseMapArray);
        DestroyOldSubAsset(library.NormalMapArray);
        DestroyOldSubAsset(library.RoughnessMapArray);
        DestroyOldSubAsset(library.AOMapArray);
        DestroyOldSubAsset(library.HeightMapArray);

        library.ClearBakedResources();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
    }

    private static void DestroyOldSubAsset(UnityEngine.Object asset)
    {
        if (asset == null)
        {
            return;
        }

        UnityEngine.Object.DestroyImmediate(asset, true);
    }

    private static void DestroyIfTemporary(UnityEngine.Object asset)
    {
        if (asset == null)
        {
            return;
        }

        UnityEngine.Object.DestroyImmediate(asset);
    }
}
