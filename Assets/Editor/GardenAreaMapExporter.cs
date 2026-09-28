using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders the baked <see cref="GardenAreaMap"/> as a top-down PNG with each area's number burned
/// into it — the picture you hold next to the sketch of how the garden is meant to be divided.
///
/// <para>The Scene view overlay answers the same question in three dimensions, but a flat image is
/// what can be compared side by side with a plan, attached to a message, or diffed against the last
/// bake to see what a repainted road actually moved.</para>
/// </summary>
public static class GardenAreaMapExporter
{
    /// <summary>Pixels per grid cell. Three keeps a 512 grid readable without a 10 MB file.</summary>
    private const int Scale = 3;

    /// <summary>Pixels per unit of the 3x5 digit font, so numbers stay legible at <see cref="Scale"/>.</summary>
    private const int DigitScale = 5;

    private static readonly Color32 NoAreaColour = new Color32(56, 56, 62, 255);
    private static readonly Color32 BorderColour = new Color32(24, 24, 28, 255);
    private static readonly Color32 LabelColour = new Color32(12, 12, 14, 255);
    private static readonly Color32 LabelBackdrop = new Color32(255, 255, 255, 255);

    /// <summary>
    /// Locked areas render grey here for the same reason they do in the Scene view: the map's job is
    /// to answer "where may the player build" at a glance, and colour is the only thing on a flat
    /// image that can carry that without another legend to read.
    /// </summary>
    private static readonly Color32 LockedColour = new Color32(112, 112, 118, 255);
    private static readonly Color32 LockedLabelBackdrop = new Color32(176, 176, 182, 255);

    /// <summary>
    /// A 3x5 bitmap per digit, one bit per pixel, row 0 at the top. Hand-rolled because the
    /// alternative — rendering a real font into a texture — drags in a camera, a render target and a
    /// font asset to draw sixteen two-digit numbers.
    /// </summary>
    private static readonly byte[][] Digits =
    {
        new byte[] { 0x7, 0x5, 0x5, 0x5, 0x7 }, // 0
        new byte[] { 0x2, 0x6, 0x2, 0x2, 0x7 }, // 1
        new byte[] { 0x7, 0x1, 0x7, 0x4, 0x7 }, // 2
        new byte[] { 0x7, 0x1, 0x7, 0x1, 0x7 }, // 3
        new byte[] { 0x5, 0x5, 0x7, 0x1, 0x1 }, // 4
        new byte[] { 0x7, 0x4, 0x7, 0x1, 0x7 }, // 5
        new byte[] { 0x7, 0x4, 0x7, 0x5, 0x7 }, // 6
        new byte[] { 0x7, 0x1, 0x1, 0x1, 0x1 }, // 7
        new byte[] { 0x7, 0x5, 0x7, 0x5, 0x7 }, // 8
        new byte[] { 0x7, 0x5, 0x7, 0x1, 0x7 }, // 9
    };

    [MenuItem("Tools/Jannah Garden/Areas/Export Area Map PNG")]
    public static void Export()
    {
        GardenAreaMap map = Load();
        if (map == null) return;

        string path = EditorUtility.SaveFilePanel(
            "Export area map", Application.dataPath + "/..", "garden_areas.png", "png");
        if (string.IsNullOrEmpty(path)) return;

        System.IO.File.WriteAllBytes(path, Render(map));
        Debug.Log("[GardenAreaMapExporter] Wrote " + path);
    }

    /// <summary>Exports without a dialog. Returns the path written, or null when nothing was baked.</summary>
    public static string ExportTo(string path)
    {
        GardenAreaMap map = Load();
        if (map == null) return null;

        System.IO.File.WriteAllBytes(path, Render(map));
        return path;
    }

    private static GardenAreaMap Load()
    {
        string[] guids = AssetDatabase.FindAssets("t:GardenAreaMap");

        if (guids.Length == 0)
        {
            Debug.LogError("[GardenAreaMapExporter] No area map asset — bake one first.");
            return null;
        }

        var map = AssetDatabase.LoadAssetAtPath<GardenAreaMap>(AssetDatabase.GUIDToAssetPath(guids[0]));

        if (map == null || !map.IsBaked)
        {
            Debug.LogError("[GardenAreaMapExporter] The area map has not been baked.");
            return null;
        }

        return map;
    }

    private static byte[] Render(GardenAreaMap map)
    {
        int res = map.resolution;
        int size = res * Scale;
        var pixels = new Color32[size * size];

        var colours = new Color32[map.areas.Count + 1];
        colours[0] = NoAreaColour;

        for (int i = 0; i < map.areas.Count; i++)
        {
            // Outside play mode the only lock state that exists is the one the map itself carries:
            // everything but the starting area.
            colours[i + 1] = map.areas[i].unlockedFromStart ? map.areas[i].editorColor : LockedColour;
        }

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                byte slot = map.cells[y * res + x];
                Color32 colour = slot < colours.Length ? colours[slot] : NoAreaColour;

                // A dark edge wherever the slot changes, so areas that happen to land on similar
                // palette colours still read as separate places.
                bool edge =
                    (x > 0 && map.cells[y * res + (x - 1)] != slot) ||
                    (x < res - 1 && map.cells[y * res + (x + 1)] != slot) ||
                    (y > 0 && map.cells[(y - 1) * res + x] != slot) ||
                    (y < res - 1 && map.cells[(y + 1) * res + x] != slot);

                if (edge) colour = BorderColour;

                for (int sy = 0; sy < Scale; sy++)
                {
                    int py = y * Scale + sy;
                    for (int sx = 0; sx < Scale; sx++)
                    {
                        pixels[py * size + (x * Scale + sx)] = colour;
                    }
                }
            }
        }

        foreach (GardenAreaDefinition area in map.areas)
        {
            // The anchor is in world metres; the image is in cells. Row 0 is minimum Z in both, which
            // is also the bottom row of a PNG as Unity encodes it, so nothing needs flipping.
            int cx = Mathf.RoundToInt((area.labelAnchor.x - map.origin.x) / map.CellSize) * Scale;
            int cy = Mathf.RoundToInt((area.labelAnchor.y - map.origin.z) / map.CellSize) * Scale;
            DrawNumber(pixels, size, cx, cy, area.number,
                area.unlockedFromStart ? LabelBackdrop : LockedLabelBackdrop);
        }

        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);
        return png;
    }

    private static void DrawNumber(Color32[] pixels, int size, int cx, int cy, int number, Color32 backdrop)
    {
        string text = Mathf.Max(0, number).ToString();

        int glyphWidth = 3 * DigitScale;
        int glyphHeight = 5 * DigitScale;
        int spacing = DigitScale;
        int totalWidth = text.Length * glyphWidth + (text.Length - 1) * spacing;

        int left = cx - totalWidth / 2;
        int bottom = cy - glyphHeight / 2;

        Fill(pixels, size, left - DigitScale, bottom - DigitScale,
            totalWidth + DigitScale * 2, glyphHeight + DigitScale * 2, backdrop);

        for (int i = 0; i < text.Length; i++)
        {
            byte[] glyph = Digits[text[i] - '0'];
            int originX = left + i * (glyphWidth + spacing);

            for (int row = 0; row < 5; row++)
            {
                // Row 0 of the glyph is its top, and the image's rows run upward, so the glyph is
                // read bottom-up here rather than flipping the whole label afterwards.
                int bits = glyph[4 - row];

                for (int col = 0; col < 3; col++)
                {
                    if ((bits & (1 << (2 - col))) == 0) continue;

                    Fill(pixels, size,
                        originX + col * DigitScale, bottom + row * DigitScale,
                        DigitScale, DigitScale, LabelColour);
                }
            }
        }
    }

    private static void Fill(Color32[] pixels, int size, int x, int y, int width, int height, Color32 colour)
    {
        for (int py = Mathf.Max(0, y); py < Mathf.Min(size, y + height); py++)
        {
            for (int px = Mathf.Max(0, x); px < Mathf.Min(size, x + width); px++)
            {
                pixels[py * size + px] = colour;
            }
        }
    }
}
