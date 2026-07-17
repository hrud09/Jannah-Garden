using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

public class SceneTextureOptimizerWindow : EditorWindow
{
    private struct TextureInfo
    {
        public Texture2D texture;
        public string assetPath;
        public TextureImporter importer;
        public int currentMaxSize;
        public TextureImporterCompression currentCompression;
        public bool generateMipMaps;
        public int width;
        public int height;
        public long fileSizeBytes;
    }

    private List<TextureInfo> _detectedTextures = new List<TextureInfo>();
    private Vector2 _scrollPosition;
    private int _bulkMaxSizeIndex = 2; // Default to 1024
    private readonly string[] _maxSizes = { "128", "256", "512", "1024", "2048", "4096" };
    private readonly int[] _maxSizeValues = { 128, 256, 512, 1024, 2048, 4096 };

    [MenuItem("Tools/Scene Texture & Material Optimizer")]
    public static void ShowWindow()
    {
        GetWindow<SceneTextureOptimizerWindow>("Scene Texture Optimizer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Scene Texture & Material Optimizer", EditorStyles.boldLabel);
        GUILayout.Label("Scan textures and materials used in the current active scene to optimize loading times.", EditorStyles.miniLabel);

        GUILayout.Space(10);

        // Control buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Scan Active Scene Textures", GUILayout.Height(30)))
        {
            ScanSceneTextures();
        }
        if (_detectedTextures.Count > 0 && GUILayout.Button("Clear Scan Results", GUILayout.Height(30)))
        {
            _detectedTextures.Clear();
        }
        EditorGUILayout.EndHorizontal();

        if (_detectedTextures.Count == 0)
        {
            GUILayout.Space(20);
            EditorGUILayout.HelpBox("No scan results. Click 'Scan Active Scene Textures' above to scan active renderers and materials.", MessageType.Info);
            return;
        }

        GUILayout.Space(15);
        DrawBulkControls();
        GUILayout.Space(10);

        // Header
        DrawHeader();

        // Scroll view of textures
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        for (int i = 0; i < _detectedTextures.Count; i++)
        {
            DrawTextureRow(i);
        }
        EditorGUILayout.EndScrollView();
    }

    private void ScanSceneTextures()
    {
        _detectedTextures.Clear();
        HashSet<Texture2D> uniqueTextures = new HashSet<Texture2D>();
        HashSet<Material> uniqueMaterials = new HashSet<Material>();

        // Find all active renderers in the scene
        Renderer[] renderers = FindObjectsOfType<Renderer>(true);

        foreach (Renderer r in renderers)
        {
            // Scan both sharedMaterials and materials to ensure we grab everything
            Material[] mats = r.sharedMaterials;
            if (mats == null) continue;

            foreach (Material m in mats)
            {
                if (m == null || uniqueMaterials.Contains(m)) continue;
                uniqueMaterials.Add(m);

                // Get all textures referenced by this material
                Shader shader = m.shader;
                if (shader == null) continue;

                int propertyCount = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        string propName = ShaderUtil.GetPropertyName(shader, i);
                        Texture tex = m.GetTexture(propName);
                        if (tex is Texture2D tex2D)
                        {
                            uniqueTextures.Add(tex2D);
                        }
                    }
                }
            }
        }

        // Get details of all textures
        foreach (Texture2D tex in uniqueTextures)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            if (string.IsNullOrEmpty(path) || path.StartsWith("Library/")) continue; // Skip built-ins

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            TextureInfo info = new TextureInfo
            {
                texture = tex,
                assetPath = path,
                importer = importer,
                currentMaxSize = importer.maxTextureSize,
                currentCompression = importer.textureCompression,
                generateMipMaps = importer.mipmapEnabled,
                width = tex.width,
                height = tex.height
            };

            // Estimate file size on disk
            try
            {
                FileInfo fInfo = new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), path));
                if (fInfo.Exists)
                {
                    info.fileSizeBytes = fInfo.Length;
                }
            }
            catch {}

            _detectedTextures.Add(info);
        }

        Debug.Log($"[TextureOptimizer] Found {_detectedTextures.Count} unique textures across {uniqueMaterials.Count} materials in scene.");
    }

    private void DrawBulkControls()
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Bulk Operations", EditorStyles.boldLabel);
        
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Cap Max Size to:", GUILayout.Width(120));
        _bulkMaxSizeIndex = EditorGUILayout.Popup(_bulkMaxSizeIndex, _maxSizes, GUILayout.Width(100));

        if (GUILayout.Button("Apply Bulk Max Resolution Cap", GUILayout.Width(220)))
        {
            int targetSize = _maxSizeValues[_bulkMaxSizeIndex];
            if (EditorUtility.DisplayDialog("Bulk Update", $"Cap all {_detectedTextures.Count} textures to {targetSize}px max?", "Yes", "Cancel"))
            {
                BulkApplySettings(targetSize, null, null);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Enable Mip Maps on All", GUILayout.Width(200)))
        {
            BulkApplySettings(null, true, null);
        }
        if (GUILayout.Button("Disable Mip Maps on All", GUILayout.Width(200)))
        {
            BulkApplySettings(null, false, null);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label("Texture Asset", EditorStyles.boldLabel, GUILayout.Width(180));
        GUILayout.Label("Source Res", EditorStyles.boldLabel, GUILayout.Width(100));
        GUILayout.Label("File Size", EditorStyles.boldLabel, GUILayout.Width(80));
        GUILayout.Label("Max Size Cap", EditorStyles.boldLabel, GUILayout.Width(100));
        GUILayout.Label("Mip Maps", EditorStyles.boldLabel, GUILayout.Width(80));
        GUILayout.Label("Actions", EditorStyles.boldLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawTextureRow(int index)
    {
        TextureInfo info = _detectedTextures[index];
        if (info.texture == null) return;

        EditorGUILayout.BeginHorizontal();

        // 1. Clickable Object Reference
        EditorGUILayout.ObjectField(info.texture, typeof(Texture2D), false, GUILayout.Width(180));

        // 2. Original Dimensions
        GUILayout.Label($"{info.width}x{info.height}", GUILayout.Width(100));

        // 3. File Size on Disk
        string sizeString = FormatBytes(info.fileSizeBytes);
        GUILayout.Label(sizeString, GUILayout.Width(80));

        // 4. Max Size Cap Dropdown
        int curSizeIndex = System.Array.IndexOf(_maxSizeValues, info.currentMaxSize);
        if (curSizeIndex < 0) curSizeIndex = 3; // Fallback to 1024
        int newSizeIndex = EditorGUILayout.Popup(curSizeIndex, _maxSizes, GUILayout.Width(100));
        if (newSizeIndex != curSizeIndex)
        {
            info.currentMaxSize = _maxSizeValues[newSizeIndex];
            _detectedTextures[index] = info;
        }

        // 5. Mip Map Toggle
        bool newMipMap = EditorGUILayout.Toggle(info.generateMipMaps, GUILayout.Width(80));
        if (newMipMap != info.generateMipMaps)
        {
            info.generateMipMaps = newMipMap;
            _detectedTextures[index] = info;
        }

        // 6. Action: Save / Apply Settings
        GUI.backgroundColor = new Color(0.18f, 0.49f, 0.72f); // Accent Blue
        if (GUILayout.Button("Apply Settings", GUILayout.Width(110)))
        {
            ApplySettings(info);
            // Re-read details
            info.importer = AssetImporter.GetAtPath(info.assetPath) as TextureImporter;
            if (info.importer != null)
            {
                info.currentMaxSize = info.importer.maxTextureSize;
                info.currentCompression = info.importer.textureCompression;
                info.generateMipMaps = info.importer.mipmapEnabled;
            }
            _detectedTextures[index] = info;
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
    }

    private void ApplySettings(TextureInfo info)
    {
        if (info.importer == null) return;

        info.importer.maxTextureSize = info.currentMaxSize;
        info.importer.mipmapEnabled = info.generateMipMaps;

        AssetDatabase.StartAssetEditing();
        info.importer.SaveAndReimport();
        AssetDatabase.StopAssetEditing();
    }

    private void BulkApplySettings(int? targetSize, bool? targetMipMap, TextureImporterCompression? targetCompression)
    {
        AssetDatabase.StartAssetEditing();

        for (int i = 0; i < _detectedTextures.Count; i++)
        {
            TextureInfo info = _detectedTextures[i];
            if (info.importer == null) continue;

            bool changed = false;

            if (targetSize.HasValue && info.importer.maxTextureSize != targetSize.Value)
            {
                info.importer.maxTextureSize = targetSize.Value;
                info.currentMaxSize = targetSize.Value;
                changed = true;
            }

            if (targetMipMap.HasValue && info.importer.mipmapEnabled != targetMipMap.Value)
            {
                info.importer.mipmapEnabled = targetMipMap.Value;
                info.generateMipMaps = targetMipMap.Value;
                changed = true;
            }

            if (targetCompression.HasValue && info.importer.textureCompression != targetCompression.Value)
            {
                info.importer.textureCompression = targetCompression.Value;
                info.currentCompression = targetCompression.Value;
                changed = true;
            }

            if (changed)
            {
                info.importer.SaveAndReimport();
                _detectedTextures[i] = info;
            }
        }

        AssetDatabase.StopAssetEditing();
        AssetDatabase.Refresh();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024f):F1} KB";
        return $"{(bytes / (1024f * 1024f)):F1} MB";
    }
}
