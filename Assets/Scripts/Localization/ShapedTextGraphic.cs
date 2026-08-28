using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders text shaped by <see cref="HarfBuzzShaper"/> — used in place of <see cref="TextMeshProUGUI"/>
/// for Bengali/Arabic content, since TMP has no complex-script shaping engine of its own (see
/// <see cref="BengaliTextShaper"/>/<see cref="ArabicTextShaper"/> for the hand-rolled fallback this
/// replaces, and TMP_Text's lack of a public glyph-by-index mesh API — that's why this is a separate
/// <see cref="MaskableGraphic"/> rather than a TMP_Text subclass).
///
/// <paramref name="fontAsset"/> is used purely as a glyph-atlas data source (<see cref="TMP_FontAsset.glyphLookupTable"/>,
/// <see cref="TMP_FontAsset.atlasWidth"/>/<see cref="TMP_FontAsset.atlasHeight"/>, <see cref="TMP_FontAsset.atlasTexture"/>)
/// — HarfBuzz decides which glyph indices to draw and where; the font asset only tells us each glyph's
/// rasterized rect. It must be assigned explicitly per locale: unlike TextMeshProUGUI, this component
/// does not use TMP's automatic fallback-font chain, because HarfBuzz shapes against a single font face
/// at a time.
///
/// Rendering uses a dedicated shader (Assets/Shaders/ShapedTextSDF.shader) rather than the font asset's
/// own TMP material — that material's shader expects a richer per-vertex format (UV0 as a float4 with a
/// bold/style flag in .w, screen-derivative-based edge scaling tuned for TMP_Text's specific mesh
/// generator) that this component's simpler vertex stream doesn't populate correctly.
///
/// Mixed-script lines (e.g. a Bengali sentence with an embedded Arabic honorific like "নবী ﷺ প্রথম") are
/// split into same-script runs by <see cref="ScriptRunSplitter"/>, each shaped against its own font via
/// <see cref="HarfBuzzFontRegistry"/>. A single Graphic/CanvasRenderer can only bind one texture per
/// draw call, so a run in a script other than this component's own <see cref="locale"/> is delegated to
/// a pooled child ShapedTextGraphic (its own CanvasRenderer/material/atlas), positioned to sit exactly
/// where that run falls in the line. Word-wrap operates on the whole multi-run glyph stream before any
/// of this happens, so wrapping decisions are correct regardless of how many scripts a line mixes.
///
/// Scope (deliberately limited — see the implementation plan for what's deferred):
///  - Word-wrap breaks only at plain ASCII spaces (U+0020) — the actual word boundary in Bengali/Arabic
///    same as in Latin script — using the already-shaped glyph run's cluster indices to find break
///    points, so wrapping never re-shapes a word differently than it would on an unbroken line. A
///    single word wider than the rect hard-breaks mid-word rather than overflowing forever. No
///    auto-size: overflow when even the smallest piece can't fit is accepted, not shrunk to fit.
///  - Script-run splitting recognizes Bengali, Arabic, and Latin (see <see cref="ScriptRunSplitter"/> —
///    the last is for untranslated fallback text and plain-ASCII numeral strings, not a real "en UI
///    locale" input; see LocalizedRendering) and only reorders which FONT a run uses, not which VISUAL
///    POSITION a run appears at: runs stay in their logical left-to-right order. That's correct for a
///    Bengali-primary (LTR) line with an embedded Arabic (RTL) run — full paragraph-level bidi
///    reordering (UAX #9) would only matter for an Arabic-primary line with embedded Bengali, which
///    doesn't occur today since Arabic content still renders via the older ArabicTextShaper path.
/// </summary>
[AddComponentMenu("UI/Shaped Text (HarfBuzz)")]
[RequireComponent(typeof(CanvasRenderer))]
public class ShapedTextGraphic : MaskableGraphic
{
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static Shader s_shader;

    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private float fontSize = 36f;
    [SerializeField] private AppLocale locale = AppLocale.bn;
    [SerializeField] [TextArea] private string rawText;
    [SerializeField] private TextAlignmentOptions alignment = TextAlignmentOptions.Center;

    [SerializeField] private float paddingLeft;
    [SerializeField] private float paddingRight;
    [SerializeField] private float paddingTop;
    [SerializeField] private float paddingBottom;

    private Material _instanceMaterial;
    private List<ShapedGlyph> _precomputedGlyphs;
    private readonly List<ShapedTextGraphic> _runChildren = new List<ShapedTextGraphic>();
    private readonly List<DrawBatch> _baseBatches = new List<DrawBatch>();

    /// <summary>Natural (unwrapped-by-anything-but-the-current-rect) size of the last shaped layout,
    /// padding included — mirrors what a TMP_Text's own <c>preferredWidth</c>/<c>preferredHeight</c>
    /// would report, for callers (see <see cref="LocalizedRendering"/>) that need to drive a
    /// ContentSizeFitter sitting on a sibling/parent object this component can't reach through Unity's
    /// own layout system (a ContentSizeFitter only ever looks at ILayoutElements on its own GameObject).</summary>
    public float PreferredWidth { get; private set; }
    public float PreferredHeight { get; private set; }

    /// <summary>One shaped glyph plus which script/locale (and therefore which font) it came from —
    /// only meaningful while laying out a mixed-script line; not used once split into per-run batches.</summary>
    private struct PositionedGlyph
    {
        public ShapedGlyph Glyph;
        public AppLocale Locale;
    }

    /// <summary>A run of glyphs in this component's own base <see cref="locale"/>/<see cref="fontAsset"/>,
    /// already positioned by <see cref="RebuildLayout"/> — <see cref="OnPopulateMesh"/> just draws these,
    /// it does no shaping/wrapping/child-creation of its own (that all has to happen outside of a Canvas
    /// rebuild pass — see RebuildLayout's doc comment for why).</summary>
    private struct DrawBatch
    {
        public List<ShapedGlyph> Glyphs;
        public float PenX;
        public float PenY;
    }

    public override Texture mainTexture => fontAsset != null ? fontAsset.atlasTexture : s_WhiteTexture;

    public TMP_FontAsset FontAsset
    {
        get => fontAsset;
        set { fontAsset = value; RefreshMaterial(); RebuildLayout(); }
    }

    public float FontSize
    {
        get => fontSize;
        set { fontSize = value; RebuildLayout(); }
    }

    /// <summary>Only set directly by the parent when positioning a pooled run-child (see
    /// <see cref="SetPrecomputedGlyphs"/>) — normal callers set the locale via <see cref="SetText"/>.</summary>
    public AppLocale Locale
    {
        get => locale;
        set { locale = value; RebuildLayout(); }
    }

    /// <summary>Both the horizontal (Left/Center/Right/Geometry) and vertical (Top/Middle/Bottom/Baseline/
    /// Geometry/Capline) components are honored. Justified/Flush only fall back to Left — this component
    /// doesn't stretch inter-word spacing to fill a line, see <see cref="HorizontalOffset"/>.</summary>
    public TextAlignmentOptions Alignment
    {
        get => alignment;
        set { alignment = value; RebuildLayout(); }
    }

    /// <summary>Inset applied to the RectTransform's rect before word-wrap, horizontal alignment, and
    /// vertical alignment all run — the same role TMP_Text's own "Margin" plays. Left/Right shrink the
    /// width used for wrapping and for Left/Center/Right offsetting; Top/Bottom shrink the height used
    /// for Top/Middle/Bottom offsetting. See <see cref="PaddedRect"/>, used at the top of <see cref="RebuildLayout"/>.</summary>
    public float PaddingLeft
    {
        get => paddingLeft;
        set { paddingLeft = value; RebuildLayout(); }
    }

    public float PaddingRight
    {
        get => paddingRight;
        set { paddingRight = value; RebuildLayout(); }
    }

    public float PaddingTop
    {
        get => paddingTop;
        set { paddingTop = value; RebuildLayout(); }
    }

    public float PaddingBottom
    {
        get => paddingBottom;
        set { paddingBottom = value; RebuildLayout(); }
    }

    /// <summary>Sets all four sides at once — convenience for callers that don't need per-side control.</summary>
    public void SetPadding(float left, float right, float top, float bottom)
    {
        paddingLeft = left;
        paddingRight = right;
        paddingTop = top;
        paddingBottom = bottom;
        RebuildLayout();
    }

    /// <summary>Reshapes and re-renders with a new string for the given locale.</summary>
    public void SetText(string text, AppLocale forLocale)
    {
        rawText = text;
        locale = forLocale;
        _precomputedGlyphs = null;
        RebuildLayout();
    }

    /// <summary>Used only by the parent to hand a pooled run-child its already-shaped (and, if the
    /// source line wrapped, already-line-split) glyphs — the child just draws them starting at its own
    /// top-left rather than re-shaping/re-wrapping text it was never given as a whole line.</summary>
    public void SetPrecomputedGlyphs(List<ShapedGlyph> glyphs)
    {
        rawText = null;
        _precomputedGlyphs = glyphs;
        SetVerticesDirty();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        RefreshMaterial();
    }

    private void RefreshMaterial()
    {
        if (fontAsset == null) return;

        if (s_shader == null) s_shader = Shader.Find("UI/ShapedTextSDF");
        if (_instanceMaterial == null) _instanceMaterial = new Material(s_shader) { hideFlags = HideFlags.HideAndDontSave };
        _instanceMaterial.SetTexture(MainTexId, fontAsset.atlasTexture);
        material = _instanceMaterial;
    }

    protected override void OnDestroy()
    {
        if (_instanceMaterial != null) DestroyImmediate(_instanceMaterial);
        base.OnDestroy();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Without this, editing Font Asset/Font Size/Locale/Raw Text/Alignment directly in the Inspector
    /// appears to do nothing: <see cref="Graphic.OnValidate"/>/<see cref="MaskableGraphic.OnValidate"/>
    /// only call SetAllDirty()/SetMaterialDirty(), which re-populates the mesh from whatever
    /// <see cref="_baseBatches"/> already holds — it never re-runs <see cref="RebuildLayout"/>, so the
    /// glyph positions stay stale even though the field the Inspector shows has changed. Only the public
    /// property setters (FontAsset, Alignment, etc.) call RebuildLayout, and Inspector edits write the
    /// serialized fields directly, bypassing those setters entirely.
    /// Deferred to the next editor tick rather than called inline: RebuildLayout can create pooled
    /// run-child GameObjects for a mixed-script line (see class doc), and Unity does not support
    /// creating/destroying objects from within OnValidate itself.
    /// </summary>
    protected override void OnValidate()
    {
        base.OnValidate();
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) RebuildLayout();
        };
    }
#endif

    /// <summary>
    /// Does all the real work — script-run splitting, shaping, word-wrap, and creating/positioning any
    /// pooled run-children a mixed-script line needs — then just marks this graphic dirty for the next
    /// normal Canvas rebuild pass to draw.
    ///
    /// This can NOT live in <see cref="OnPopulateMesh"/>: that method is invoked BY a Canvas rebuild
    /// pass, and creating/enabling a child Graphic (which happens the first time a run-child is needed)
    /// fires that child's own OnEnable → SetAllDirty(), which tries to queue the child into the SAME
    /// rebuild pass that's still iterating — Unity throws ("already inside a graphic rebuild loop")
    /// rather than support that reentrancy. Running this from <see cref="SetText"/> instead — ordinary
    /// game-code, not mid-rebuild — lets child creation/SetVerticesDirty calls work completely normally.
    /// </summary>
    private void RebuildLayout()
    {
        _baseBatches.Clear();
        int childCount = 0;

        if (fontAsset == null || string.IsNullOrEmpty(rawText))
        {
            PreferredWidth = 0f;
            PreferredHeight = 0f;
            DeactivateRunChildren(0);
            SetVerticesDirty();
            return;
        }

        // fullRect.xMin/yMax already account for the RectTransform's pivot, so text lands at the actual
        // left/top edge of the rect regardless of pivot — using local (0,0) directly would only be
        // correct for a (0,1) pivot. `rect` below insets that by the padding for all wrap/alignment math;
        // `fullRect` stays around only for positioning run-children, whose anchoredPosition is relative to
        // this component's actual RectTransform, not the padded sub-rect.
        Rect fullRect = rectTransform.rect;
        Rect rect = PaddedRect(fullRect);
        string[] sourceLines = rawText.Split('\n');

        // Word-wrap every source line up front so the vertical alignment below knows the true total
        // line count before placing a single glyph — otherwise Middle/Bottom would need a second pass.
        var visualLines = new List<List<PositionedGlyph>>();
        foreach (string sourceLine in sourceLines)
        {
            List<PositionedGlyph> glyphs = ShapeLineWithRuns(sourceLine);
            visualLines.AddRange(WrapIntoLines(glyphs, sourceLine, fontSize, rect.width));
        }

        float lineHeight = fontSize * 1.2f; // fixed line height; no auto-size, see class doc.
        float totalHeight = visualLines.Count * lineHeight;
        float penY = rect.yMax - VerticalOffset(totalHeight, rect.height);

        float maxLineWidth = 0f;

        foreach (List<PositionedGlyph> visualLine in visualLines)
        {
            float lineWidth = 0f;
            foreach (PositionedGlyph pg in visualLine) lineWidth += pg.Glyph.XAdvance * (fontSize / HarfBuzzShaper.UnitsPerEm(pg.Locale));
            if (lineWidth > maxLineWidth) maxLineWidth = lineWidth;
            float penX = rect.xMin + HorizontalOffset(lineWidth, rect.width);

            int i = 0;
            while (i < visualLine.Count)
            {
                AppLocale batchLocale = visualLine[i].Locale;
                int start = i;
                while (i < visualLine.Count && visualLine[i].Locale == batchLocale) i++;

                float batchHbScale = fontSize / HarfBuzzShaper.UnitsPerEm(batchLocale);
                var batchGlyphs = new List<ShapedGlyph>(i - start);
                float batchWidth = 0f;
                for (int k = start; k < i; k++)
                {
                    batchGlyphs.Add(visualLine[k].Glyph);
                    batchWidth += visualLine[k].Glyph.XAdvance * batchHbScale;
                }

                if (batchLocale == locale)
                {
                    _baseBatches.Add(new DrawBatch { Glyphs = batchGlyphs, PenX = penX, PenY = penY });
                }
                else
                {
                    TMP_FontAsset otherFont = HarfBuzzFontRegistry.GetFontAsset(batchLocale);
                    if (otherFont != null)
                    {
                        ShapedTextGraphic child = GetOrCreateRunChild(childCount++);

                        RectTransform childRt = child.rectTransform;
                        childRt.anchorMin = new Vector2(0f, 1f);
                        childRt.anchorMax = new Vector2(0f, 1f);
                        childRt.pivot = new Vector2(0f, 1f);
                        childRt.sizeDelta = new Vector2(Mathf.Max(batchWidth, 1f), fontSize * 1.4f);
                        childRt.anchoredPosition = new Vector2(penX - fullRect.xMin, penY - fullRect.yMax);

                        child.color = color;
                        child.FontAsset = otherFont;
                        child.FontSize = fontSize;
                        child.Locale = batchLocale;
                        child.SetPrecomputedGlyphs(batchGlyphs);
                    }
                    else
                    {
                        Debug.LogWarning($"[ShapedTextGraphic] No font registered for locale {batchLocale} in " +
                                          "HarfBuzzFontRegistry — embedded run skipped.");
                    }
                }

                penX += batchWidth;
            }

            penY -= lineHeight;
        }

        PreferredWidth = maxLineWidth + paddingLeft + paddingRight;
        PreferredHeight = totalHeight + paddingTop + paddingBottom;

        DeactivateRunChildren(childCount);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (fontAsset == null) return;

        float metricsScale = fontSize / fontAsset.faceInfo.pointSize;
        float atlasWidth = fontAsset.atlasWidth;
        float atlasHeight = fontAsset.atlasHeight;
        // TMP always samples slightly outside each glyph's tight metrics box, by this many atlas
        // pixels, because the SDF's edge falloff (where distance crosses the alpha threshold) lives
        // in that surrounding margin — the tight box is just the ink, which reads as uniformly
        // "fully inside" (see TMP_Text.cs's own UV0 computation, which does the same subtract/add).
        float padding = fontAsset.atlasPadding;

        if (_precomputedGlyphs != null)
        {
            // A pooled run-child: already shaped, already trimmed to one visual line by the parent.
            // Padded the same way the main batch path is (see RebuildLayout) so a run-child with its own
            // padding set behaves consistently — its own rect is what this draws relative to, same as
            // any other ShapedTextGraphic.
            Rect rect = PaddedRect(rectTransform.rect);
            float hbScale = fontSize / HarfBuzzShaper.UnitsPerEm(locale);
            DrawGlyphLine(vh, _precomputedGlyphs, rect.xMin, rect.yMax, metricsScale, hbScale, padding, atlasWidth, atlasHeight);
            return;
        }

        foreach (DrawBatch batch in _baseBatches)
        {
            float hbScale = fontSize / HarfBuzzShaper.UnitsPerEm(locale);
            DrawGlyphLine(vh, batch.Glyphs, batch.PenX, batch.PenY, metricsScale, hbScale, padding, atlasWidth, atlasHeight);
        }
    }

    private void DrawGlyphLine(VertexHelper vh, List<ShapedGlyph> glyphs, float startX, float startY,
        float metricsScale, float hbScale, float padding, float atlasWidth, float atlasHeight)
    {
        float penX = startX;

        foreach (ShapedGlyph g in glyphs)
        {
            if (fontAsset.glyphLookupTable.TryGetValue(g.GlyphId, out var glyph))
            {
                var m = glyph.metrics;
                var r = glyph.glyphRect;

                float x = penX + (m.horizontalBearingX - padding) * metricsScale + g.XOffset * hbScale;
                float yTop = startY + (m.horizontalBearingY + padding) * metricsScale + g.YOffset * hbScale;
                float w = (m.width + 2f * padding) * metricsScale;
                float h = (m.height + 2f * padding) * metricsScale;

                float u0 = (r.x - padding) / atlasWidth;
                float v0 = (r.y - padding) / atlasHeight;
                float u1 = (r.x + r.width + padding) / atlasWidth;
                float v1 = (r.y + r.height + padding) / atlasHeight;

                int idx = vh.currentVertCount;
                vh.AddVert(new Vector3(x, yTop - h), color, new Vector2(u0, v0));
                vh.AddVert(new Vector3(x, yTop), color, new Vector2(u0, v1));
                vh.AddVert(new Vector3(x + w, yTop), color, new Vector2(u1, v1));
                vh.AddVert(new Vector3(x + w, yTop - h), color, new Vector2(u1, v0));
                vh.AddTriangle(idx, idx + 1, idx + 2);
                vh.AddTriangle(idx + 2, idx + 3, idx);
            }
            else
            {
                Debug.LogWarning($"[ShapedTextGraphic] Glyph id {g.GlyphId} is not in '{fontAsset.name}''s glyph " +
                                  "table and could not be added dynamically — Static population mode, or the atlas " +
                                  "is out of space with Multi Atlas Textures disabled.");
            }

            penX += g.XAdvance * hbScale;
        }
    }

    private ShapedTextGraphic GetOrCreateRunChild(int index)
    {
        while (_runChildren.Count <= index)
        {
            var go = new GameObject($"Run {_runChildren.Count} (HarfBuzz)", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            ShapedTextGraphic child = go.AddComponent<ShapedTextGraphic>();
            child.raycastTarget = false;
            _runChildren.Add(child);
        }

        ShapedTextGraphic result = _runChildren[index];
        result.gameObject.SetActive(true);
        return result;
    }

    private void DeactivateRunChildren(int usedCount)
    {
        for (int i = usedCount; i < _runChildren.Count; i++) _runChildren[i].gameObject.SetActive(false);
    }

    private List<PositionedGlyph> ShapeLineWithRuns(string sourceLine)
    {
        var result = new List<PositionedGlyph>();
        foreach (ScriptRun run in ScriptRunSplitter.Split(sourceLine, locale))
        {
            // The atlas is populated dynamically (see HarfBuzzFontRegistry), but only stock TMP_Text
            // triggers that on its own — this component reads glyphLookupTable directly (see class doc),
            // so any character never rendered by a normal TMP_Text first (e.g. digits in Bengali/Arabic
            // strings) would otherwise shape to a valid glyph ID that's simply missing from the atlas,
            // silently eating its advance width as blank space instead of drawing anything.
            TMP_FontAsset runFont = run.Locale == locale ? fontAsset : HarfBuzzFontRegistry.GetFontAsset(run.Locale);
            if (runFont != null) runFont.TryAddCharacters(run.Text, out _);

            foreach (ShapedGlyph g in HarfBuzzShaper.Shape(run.Text, run.Locale))
            {
                // TryAddCharacters above only rasterizes each codepoint's nominal cmap glyph — the
                // conjunct/ligature forms HarfBuzz's GSUB pass substitutes in (Bengali "স্ব", "চ্ছ", …)
                // have no codepoint of their own, so each shaped glyph ID must also be ensured by INDEX.
                if (runFont != null) EnsureGlyphInAtlas(runFont, g.GlyphId);

                ShapedGlyph offsetGlyph = g;
                offsetGlyph.Cluster = (uint)(run.StartIndex + (int)g.Cluster);
                result.Add(new PositionedGlyph { Glyph = offsetGlyph, Locale = run.Locale });
            }
        }
        return result;
    }

    private static System.Reflection.MethodInfo s_tryAddGlyphByIndex;

    /// <summary>
    /// Rasterizes one glyph into <paramref name="font"/>'s dynamic atlas by glyph INDEX. Needed because
    /// TMP's only public population API (<see cref="TMP_FontAsset.TryAddCharacters(string, out string, bool)"/>)
    /// goes through the font's cmap, and the conjunct/ligature glyphs OpenType shaping substitutes in are
    /// not in the cmap at all — they're reachable only by index. TMP itself can do this (its runtime
    /// ligature support depends on TryAddGlyphInternal, which also handles atlas-page overflow and editor
    /// persistence) but doesn't expose it, hence the cached reflection. Static-population assets are
    /// skipped — they can't take new glyphs, and DrawGlyphLine's warning already reports that case.
    /// </summary>
    private static void EnsureGlyphInAtlas(TMP_FontAsset font, uint glyphId)
    {
        if (font.atlasPopulationMode == AtlasPopulationMode.Static || font.glyphLookupTable.ContainsKey(glyphId)) return;

        if (s_tryAddGlyphByIndex == null)
            s_tryAddGlyphByIndex = typeof(TMP_FontAsset).GetMethod("TryAddGlyphInternal",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        s_tryAddGlyphByIndex?.Invoke(font, new object[] { glyphId, null });
    }

    /// <summary>
    /// Greedily splits one already-shaped, possibly-multi-script source line into visual lines that
    /// each fit within <paramref name="maxWidth"/>, breaking only at glyphs whose cluster lands on a
    /// space character in <paramref name="sourceLine"/> — shaping happens exactly once, up front, so
    /// wrapping can never change how a word is shaped relative to it appearing on an unbroken line.
    /// </summary>
    private static List<List<PositionedGlyph>> WrapIntoLines(List<PositionedGlyph> glyphs, string sourceLine, float fontSize, float maxWidth)
    {
        var result = new List<List<PositionedGlyph>>();
        var current = new List<PositionedGlyph>();
        float currentWidth = 0f;
        int lastBreakIndex = -1; // index within `current` of the last glyph that was a space

        foreach (PositionedGlyph pg in glyphs)
        {
            float advance = pg.Glyph.XAdvance * (fontSize / HarfBuzzShaper.UnitsPerEm(pg.Locale));

            if (current.Count > 0 && currentWidth + advance > maxWidth)
            {
                if (lastBreakIndex >= 0)
                {
                    List<PositionedGlyph> line = current.GetRange(0, lastBreakIndex + 1);
                    TrimTrailingSpaces(line, sourceLine);
                    result.Add(line);

                    List<PositionedGlyph> remainder = current.GetRange(lastBreakIndex + 1, current.Count - lastBreakIndex - 1);
                    current = remainder;
                    currentWidth = 0f;
                    foreach (PositionedGlyph rg in current) currentWidth += rg.Glyph.XAdvance * (fontSize / HarfBuzzShaper.UnitsPerEm(rg.Locale));
                }
                else
                {
                    // No space seen yet in this segment (one word wider than the rect) — hard-break
                    // here rather than overflowing indefinitely.
                    result.Add(current);
                    current = new List<PositionedGlyph>();
                    currentWidth = 0f;
                }
                lastBreakIndex = -1;
            }

            current.Add(pg);
            currentWidth += advance;
            if ((int)pg.Glyph.Cluster < sourceLine.Length && sourceLine[(int)pg.Glyph.Cluster] == ' ') lastBreakIndex = current.Count - 1;
        }

        if (current.Count > 0) result.Add(current);
        return result;
    }

    private static void TrimTrailingSpaces(List<PositionedGlyph> line, string sourceLine)
    {
        while (line.Count > 0)
        {
            PositionedGlyph last = line[line.Count - 1];
            if ((int)last.Glyph.Cluster < sourceLine.Length && sourceLine[(int)last.Glyph.Cluster] == ' ') line.RemoveAt(line.Count - 1);
            else break;
        }
    }

    /// <summary>Insets <paramref name="source"/> by the four padding fields, clamped so a padding sum
    /// larger than the rect never flips xMin/xMax (or yMin/yMax) past each other into a negative-size
    /// rect that would corrupt every downstream wrap/alignment calculation.</summary>
    private Rect PaddedRect(Rect source)
    {
        float xMin = source.xMin + paddingLeft;
        float xMax = source.xMax - paddingRight;
        float yMin = source.yMin + paddingBottom;
        float yMax = source.yMax - paddingTop;

        if (xMax < xMin) xMax = xMin;
        if (yMax < yMin) yMax = yMin;

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    /// <summary>Per-visual-line horizontal placement within the rect. Justified/Flush have no stretch
    /// implementation (this component doesn't redistribute inter-word spacing), so they fall back to Left —
    /// same as stock TMP_Text would do for a single line that doesn't reach the wrap width anyway.</summary>
    private float HorizontalOffset(float lineWidth, float rectWidth)
    {
        switch (HorizontalComponent(alignment))
        {
            case HorizontalAlignmentOptions.Right:
                return rectWidth - lineWidth;
            case HorizontalAlignmentOptions.Center:
            case HorizontalAlignmentOptions.Geometry:
                return (rectWidth - lineWidth) * 0.5f;
            default: // Left, Justified, Flush
                return 0f;
        }
    }

    /// <summary>Vertical placement of the whole text block (all visual lines) within the rect, measured
    /// as a downward offset from the rect's top edge. Baseline/Capline have no distinct baseline-metrics
    /// handling here — they land on Bottom/Middle respectively, the closest approximation available
    /// without per-glyph baseline tracking across the whole block.</summary>
    private float VerticalOffset(float totalHeight, float rectHeight)
    {
        switch (VerticalComponent(alignment))
        {
            case VerticalAlignmentOptions.Bottom:
            case VerticalAlignmentOptions.Baseline:
                return rectHeight - totalHeight;
            case VerticalAlignmentOptions.Middle:
            case VerticalAlignmentOptions.Geometry:
            case VerticalAlignmentOptions.Capline:
                return (rectHeight - totalHeight) * 0.5f;
            default: // Top
                return 0f;
        }
    }

    private static HorizontalAlignmentOptions HorizontalComponent(TextAlignmentOptions alignment) =>
        (HorizontalAlignmentOptions)((int)alignment & 0xFF);

    private static VerticalAlignmentOptions VerticalComponent(TextAlignmentOptions alignment) =>
        (VerticalAlignmentOptions)((int)alignment & 0xFF00);
}
