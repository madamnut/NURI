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
        Texture2DArray maskMapArray = null;

        try
        {
            baseMapArray = TerrainMaterialLibrary.CreateTextureArray(
                definitions,
                definition => definition.BaseMap,
                library.BaseMapDownscale,
                library.BaseMapMipChain,
                false,
                "BaseMap");
            maskMapArray = TerrainMaterialLibrary.CreatePackedMaskArray(
                definitions,
                library.MaskMapDownscale,
                library.MaskMapMipChain);

            baseMapArray.name = $"{library.name}_BaseMapArray";
            maskMapArray.name = $"{library.name}_MaskMapArray";

            Undo.RecordObject(library, "Bake Terrain Material Library");
            DestroyTextureArraySubAssets(assetPath);

            AssetDatabase.AddObjectToAsset(baseMapArray, library);
            AssetDatabase.AddObjectToAsset(maskMapArray, library);

            library.SetBakedResources(
                baseMapArray,
                maskMapArray,
                baseColors,
                definitions.Length);

            EditorUtility.SetDirty(library);
            AssetDatabase.ImportAsset(assetPath);
            AssetDatabase.SaveAssets();
        }
        catch (Exception exception)
        {
            DestroyIfTemporary(baseMapArray);
            DestroyIfTemporary(maskMapArray);
            Debug.LogError($"Failed to bake terrain material library: {exception.Message}", library);
        }
    }

    private static void ClearBakedArrays(TerrainMaterialLibrary library)
    {
        Undo.RecordObject(library, "Clear Terrain Material Library Bake");

        string assetPath = AssetDatabase.GetAssetPath(library);
        DestroyTextureArraySubAssets(assetPath);

        library.ClearBakedResources();
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
    }

    private static void DestroyTextureArraySubAssets(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return;
        }

        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Texture2DArray textureArray)
            {
                UnityEngine.Object.DestroyImmediate(textureArray, true);
            }
        }
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
