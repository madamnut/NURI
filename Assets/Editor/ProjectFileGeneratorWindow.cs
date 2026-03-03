// Assets/Editor/ProjectFileGeneratorWindow.cs
// - 루트 폴더(복수) + 출력 폴더(단일)를 드래그&드랍(ObjectField)로 선택
// - 선택값은 EditorPrefs에 GUID로 저장되어, 창을 닫았다가 열어도 유지됨
// - 결과물(3개):
//   1) source_bundle.zip      : 지정한 루트들 아래 .cs/.json 원본만 압축
//   2) All_Files.txt          : 지정한 루트들 아래(하위 포함) 전체 파일 경로 목록(Assets/...)
//   3) source_details.txt     : zip에 포함된(.cs/.json) 파일 경로 목록(Assets/...)
//
// 메뉴: Tools/Generate Project Files

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

public sealed class ProjectFileGeneratorWindow : EditorWindow
{
    const string PREF_ROOT_GUIDS = "Stone.ProjectFileGen.RootGuids";
    const string PREF_OUTPUT_GUID = "Stone.ProjectFileGen.OutputGuid";

    [SerializeField] List<DefaultAsset> rootFolders = new List<DefaultAsset>();
    [SerializeField] DefaultAsset outputFolder;

    Vector2 rootsScroll;

    [MenuItem("Tools/Generate Project Files")]
    static void Open()
    {
        GetWindow<ProjectFileGeneratorWindow>("Project File Generator");
    }

    void OnEnable()
    {
        LoadPrefs();
    }

    void OnDisable()
    {
        SavePrefs();
    }

    void OnGUI()
    {
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.LabelField("Root Folders (drag folders from Project view)", EditorStyles.boldLabel);

        using (var scroll = new EditorGUILayout.ScrollViewScope(rootsScroll, GUILayout.Height(120)))
        {
            rootsScroll = scroll.scrollPosition;

            int removeIndex = -1;

            for (int i = 0; i < rootFolders.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                rootFolders[i] = (DefaultAsset)EditorGUILayout.ObjectField(rootFolders[i], typeof(DefaultAsset), false);

                if (GUILayout.Button("X", GUILayout.Width(22)))
                    removeIndex = i;

                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0 && removeIndex < rootFolders.Count)
                rootFolders.RemoveAt(removeIndex);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add Root", GUILayout.Width(100)))
            rootFolders.Add(null);

        if (GUILayout.Button("Clear", GUILayout.Width(70)))
            rootFolders.Clear();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        EditorGUILayout.LabelField("Output Folder (drag a folder from Project view)", EditorStyles.boldLabel);
        outputFolder = (DefaultAsset)EditorGUILayout.ObjectField(outputFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space(12);

        if (GUILayout.Button("Generate (zip + txt)"))
            Generate();

        if (EditorGUI.EndChangeCheck())
            SavePrefs();
    }

    void Generate()
    {
        var roots = ResolveFolders(rootFolders);
        if (roots.Count == 0)
        {
            Debug.LogError("[Generator] No valid root folders.");
            return;
        }

        string outPath = ResolveFolder(outputFolder);
        if (string.IsNullOrEmpty(outPath))
        {
            Debug.LogError("[Generator] Output folder is not set or invalid.");
            return;
        }

        EnsureAssetsFolder(outPath);

        string absOutput = ToAbsolutePath(outPath);

        var allFilesSet = new HashSet<string>(StringComparer.Ordinal);
        var includedSet = new HashSet<string>(StringComparer.Ordinal);

        for (int r = 0; r < roots.Count; r++)
        {
            string root = roots[r];
            string absRoot = ToAbsolutePath(root);

            if (!Directory.Exists(absRoot))
            {
                Debug.LogError($"[Generator] Root folder does not exist on disk: {root}");
                continue;
            }

            foreach (var absPath in Directory.GetFiles(absRoot, "*", SearchOption.AllDirectories))
            {
                if (absPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                string assetsPath = ToAssetsRelativePath(absPath);
                if (string.IsNullOrEmpty(assetsPath))
                    continue;

                allFilesSet.Add(assetsPath);

                if (assetsPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                    assetsPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    includedSet.Add(assetsPath);
                }
            }
        }

        var allFiles = new List<string>(allFilesSet);
        var includedFiles = new List<string>(includedSet);
        allFiles.Sort(StringComparer.Ordinal);
        includedFiles.Sort(StringComparer.Ordinal);

        File.WriteAllText(Path.Combine(absOutput, "All_Files.txt"), string.Join("\n", allFiles));

        var detailsLines = new List<string>(includedFiles.Count + 6)
        {
            "roots=" + string.Join(",", roots),
            "includedExtensions=.cs,.json",
            $"count={includedFiles.Count}",
            "---"
        };
        detailsLines.AddRange(includedFiles);
        File.WriteAllText(Path.Combine(absOutput, "source_details.txt"), string.Join("\n", detailsLines));

        string zipPath = Path.Combine(absOutput, "source_bundle.zip");
        if (File.Exists(zipPath))
            File.Delete(zipPath);

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            for (int i = 0; i < includedFiles.Count; i++)
            {
                string assetsPath = includedFiles[i];
                string absFile = ToAbsolutePath(assetsPath);

                if (!File.Exists(absFile))
                    continue;

                zip.CreateEntryFromFile(absFile, assetsPath, System.IO.Compression.CompressionLevel.Optimal);
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[Generator] Done.\nOutput: {outPath}\nAll Files: {allFiles.Count}\nIncluded: {includedFiles.Count}\nRoots: {roots.Count}");
    }

    // ---------- Persistence (EditorPrefs) ----------

    void SavePrefs()
    {
        // roots
        var rootGuids = new List<string>(rootFolders.Count);
        for (int i = 0; i < rootFolders.Count; i++)
        {
            string guid = AssetToGuidIfFolder(rootFolders[i]);
            if (!string.IsNullOrEmpty(guid))
                rootGuids.Add(guid);
        }
        EditorPrefs.SetString(PREF_ROOT_GUIDS, string.Join(";", rootGuids));

        // output
        string outGuid = AssetToGuidIfFolder(outputFolder);
        EditorPrefs.SetString(PREF_OUTPUT_GUID, outGuid ?? string.Empty);
    }

    void LoadPrefs()
    {
        rootFolders.Clear();

        string rootsStr = EditorPrefs.GetString(PREF_ROOT_GUIDS, string.Empty);
        if (!string.IsNullOrEmpty(rootsStr))
        {
            string[] parts = rootsStr.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var asset = GuidToFolderAsset(parts[i]);
                if (asset != null)
                    rootFolders.Add(asset);
            }
        }

        string outGuid = EditorPrefs.GetString(PREF_OUTPUT_GUID, string.Empty);
        outputFolder = GuidToFolderAsset(outGuid);
    }

    static string AssetToGuidIfFolder(DefaultAsset asset)
    {
        if (asset == null) return null;

        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path)) return null;
        if (!AssetDatabase.IsValidFolder(path)) return null;

        return AssetDatabase.AssetPathToGUID(path);
    }

    static DefaultAsset GuidToFolderAsset(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;

        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return null;
        if (!AssetDatabase.IsValidFolder(path)) return null;

        return AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
    }

    // ---------- Helpers ----------

    static List<string> ResolveFolders(List<DefaultAsset> assets)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < assets.Count; i++)
        {
            string p = ResolveFolder(assets[i]);
            if (string.IsNullOrEmpty(p))
                continue;

            if (seen.Add(p))
                list.Add(p);
        }

        return list;
    }

    static string ResolveFolder(DefaultAsset asset)
    {
        if (asset == null)
            return null;

        string path = AssetDatabase.GetAssetPath(asset);
        if (string.IsNullOrEmpty(path))
            return null;

        if (!AssetDatabase.IsValidFolder(path))
            return null;

        if (!path.StartsWith("Assets", StringComparison.Ordinal))
            return null;

        return path;
    }

    static string ToAbsolutePath(string assetsPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName.Replace("\\", "/");
        return (projectRoot + "/" + assetsPath).Replace("\\", "/");
    }

    static string ToAssetsRelativePath(string absPath)
    {
        string norm = absPath.Replace("\\", "/");
        string assetsRoot = Application.dataPath.Replace("\\", "/"); // "<project>/Assets"
        if (!norm.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        return "Assets" + norm.Substring(assetsRoot.Length);
    }

    static void EnsureAssetsFolder(string assetsFolder)
    {
        if (AssetDatabase.IsValidFolder(assetsFolder))
            return;

        string[] parts = assetsFolder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "Assets")
            return;

        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif