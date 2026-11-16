using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace JannahGarden.EditorTools
{
    /// <summary>
    /// Applies sprite import settings to the generated UI kit.
    ///
    /// The art itself is produced by Tools/ui_kit/generate.py, which also writes
    /// borders.txt next to the PNGs listing the 9-slice border for each sprite.
    /// Re-run this after regenerating so the borders survive the reimport.
    ///
    /// The kit is drawn at 2x its layout size for crisp ornament detail. The
    /// manifest's "#ppu=" header carries the matching pixels-per-unit (200),
    /// which keeps on-screen sizes and 9-slice border widths unchanged.
    /// </summary>
    public static class JannahUIKitImporter
    {
        const string KitFolder = "Assets/2D Assets/UI/Jannah UI Kit";
        const string BorderManifest = KitFolder + "/borders.txt";

        [MenuItem("Jannah/UI Kit/Reimport Sprites")]
        public static void Reimport()
        {
            var borders = ReadBorders(out float ppu);
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { KitFolder });
            int changed = 0;

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer == null) continue;

                    var name = Path.GetFileNameWithoutExtension(path);
                    borders.TryGetValue(name, out int b);

                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = ppu;
                    importer.spriteBorder = new Vector4(b, b, b, b);
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    // Small source art with soft gradients: block compression
                    // banding is far more visible than the memory it saves.
                    importer.textureCompression = TextureImporterCompression.Uncompressed;

                    EditorUtility.SetDirty(importer);
                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[UI Kit] Reimported {changed} sprites at {ppu} PPU ({borders.Count} with 9-slice borders).");
        }

        static Dictionary<string, int> ReadBorders(out float ppu)
        {
            ppu = 100f;
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            var full = Path.Combine(Directory.GetCurrentDirectory(), BorderManifest);
            if (!File.Exists(full))
            {
                Debug.LogWarning($"[UI Kit] No border manifest at {BorderManifest}; sprites will import without 9-slice borders.");
                return map;
            }

            foreach (var line in File.ReadAllLines(full))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                if (trimmed.StartsWith("#ppu=", StringComparison.Ordinal))
                {
                    if (float.TryParse(trimmed.Substring(5), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float p) && p > 0f)
                        ppu = p;
                    continue;
                }
                if (trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                var eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                var key = trimmed.Substring(0, eq);
                if (int.TryParse(trimmed.Substring(eq + 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int value))
                {
                    map[key] = value;
                }
            }
            return map;
        }
    }
}
