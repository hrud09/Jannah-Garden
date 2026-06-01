# Unity Custom Shop System Prompt

This document contains a system prompt designed for **Gemini 3.5 Flash** (or similar coding assistants) to recreate this complete custom shop system from scratch in a brand-new Unity project.

---

### System Prompt for AI Assistant

```markdown
You are a Unity UI and Shader development expert. Your task is to implement a complete, premium, custom Shop System in Unity. This system features dynamic carousel scrolling, automated Y-proximity item selection, auto-snapping, scale and alpha zoom-transitions, and a slide-out navigation panel with toggle arrow indicators. 

The background assets for items are centralized inside the manager and mapped dynamically to item UI elements by categories (item types).

You must implement all the files listed below with their complete production-ready code.

---

## 1. Shop Item Type Enum (ShopItemType.cs)
Define a category enum to differentiate item rarity or type for custom layout styling.

```csharp
public enum ShopItemType
{
    Common,
    Rare,
    Epic,
    Legendary,
    Special
}
```

---

## 2. The Wobble Shader (UIClothWobble.shader)
Create a shader named `UI/Custom/ClothWobble` that enables UI elements to wobble horizontally like cloth, with a customizable Y-axis pivot point.

### Requirements:
- Properties:
  - `_Rigidity ("Stiffness (Bend Curve)", Range(0.1, 8)) = 1.5`
  - `_AnchorTop ("Use Anchor (Fixed Side)", Float) = 1` (Toggle)
  - `_AnchorPivot ("Anchor Pivot Y (0=Bottom, 1=Top)", Range(0, 1)) = 1.0`
- Vertex Shader Logic:
  - If `_AnchorTop` is enabled (> 0.5), compute the normalized vertical distance between the vertex UV (`IN.texcoord.y`) and the custom `_AnchorPivot`.
  - Normalize the distance using `maxDist = max(_AnchorPivot, 1.0 - _AnchorPivot)` so the wobble amplitude scales uniformly regardless of where the pivot is set.
  - Apply the stiffness/rigidity curve: `influence = pow(abs(distFromAnchor), _Rigidity)`.
  - Shift `IN.vertex.x` by the combined main swing and detail ripple waves scaled by the influence.

### Full Shader Code:
```hlsl
Shader "UI/Custom/ClothWobble"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Stencil)]
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Header(Use UI Alpha Clip)]
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        [Header(Main Swing)]
        [Toggle] _EnableMainSwing ("Enable Main Swing", Float) = 1
        _SpeedMain ("Swing Speed", Range(0, 30)) = 2
        _FreqMain ("Swing Frequency", Range(0, 20)) = 1
        _AmpMain ("Swing Strength", Range(0, 0.5)) = 0.02
        
        [Header(Detail Ripples)]
        [Toggle] _EnableDetailRipples ("Enable Detail Ripples", Float) = 1
        _SpeedDetail ("Ripple Speed", Range(0, 50)) = 10
        _FreqDetail ("Ripple Frequency", Range(0, 50)) = 15
        _AmpDetail ("Ripple Strength", Range(0, 0.1)) = 0.005

        [Header(Material Properties)]
        _Rigidity ("Stiffness (Bend Curve)", Range(0.1, 8)) = 1.5
        [Toggle] _AnchorTop ("Use Anchor (Fixed Side)", Float) = 1
        _AnchorPivot ("Anchor Pivot Y (0=Bottom, 1=Top)", Range(0, 1)) = 1.0

        [Header(Vertical Control)]
        _TopFade ("Top Wobble Strength", Range(0, 1)) = 1.0
        _BottomFade ("Bottom Wobble Strength", Range(0, 1)) = 1.0

        [Header(Edge Fray)]
        [Toggle] _EnableEdgeFraying ("Enable Edge Fraying", Float) = 1
        _EdgeFrayAmount ("Fray Size (0 to Disable)", Range(0, 0.2)) = 0.05
        _EdgeFrayScale ("Fray Thread Density", Range(10, 300)) = 150
        _EdgeFraySharpness ("Fray Sharpness", Range(1, 20)) = 10
        _EdgeFrayNoiseIntensity ("Fray Noise Intensity", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            
            float _EnableMainSwing;
            float _EnableDetailRipples;
            float _EnableEdgeFraying;

            float _SpeedMain;
            float _FreqMain;
            float _AmpMain;

            float _SpeedDetail;
            float _FreqDetail;
            float _AmpDetail;

            float _Rigidity;
            float _AnchorTop;
            float _AnchorPivot;

            float _TopFade;
            float _BottomFade;

            float _EdgeFrayAmount;
            float _EdgeFrayScale;
            float _EdgeFraySharpness;
            float _EdgeFrayNoiseIntensity;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPosition = IN.vertex;

                float swing = 0;
                if (_EnableMainSwing > 0.5)
                {
                    float timeMain = _Time.y * _SpeedMain;
                    swing = sin(timeMain + IN.texcoord.y * _FreqMain) * _AmpMain;
                }

                float ripple = 0;
                if (_EnableDetailRipples > 0.5)
                {
                    float timeDetail = _Time.y * _SpeedDetail;
                    ripple = cos(timeDetail + IN.texcoord.y * _FreqDetail) * _AmpDetail;
                }

                float totalWave = swing + ripple;
                float influence = 1.0;

                if (_AnchorTop > 0.5)
                {
                    float maxDist = max(_AnchorPivot, 1.0 - _AnchorPivot);
                    float distFromAnchor = abs(IN.texcoord.y - _AnchorPivot) / max(maxDist, 0.0001);
                    influence = pow(abs(distFromAnchor), _Rigidity);
                }

                float yFadeMultiplier = lerp(_BottomFade, _TopFade, IN.texcoord.y);
                influence *= yFadeMultiplier;

                IN.vertex.x += totalWave * influence * 1000; 

                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                if (_EnableEdgeFraying > 0.5 && _EdgeFrayAmount > 0.001)
                {
                    float distX = min(IN.texcoord.x, 1.0 - IN.texcoord.x);
                    float distY = min(IN.texcoord.y, 1.0 - IN.texcoord.y);
                    float distEdge = min(distX, distY);

                    float u = IN.texcoord.y * _EdgeFrayScale;
                    float threadID_X = floor(u);
                    float t_X = frac(u);
                    float randomX = frac(sin(threadID_X * 12.9898) * 43758.5453) * 2.0 - 1.0;
                    float threadX = sin(t_X * 3.14159265) * randomX;

                    float v = IN.texcoord.x * _EdgeFrayScale;
                    float threadID_Y = floor(v);
                    float t_Y = frac(v);
                    float randomY = frac(sin(threadID_Y * 78.233) * 43758.5453) * 2.0 - 1.0;
                    float threadY = sin(t_Y * 3.14159265) * randomY;

                    float weaveNoise = (distX < distY) ? threadX : threadY;

                    float edgeFactor = distEdge / _EdgeFrayAmount;
                    float jaggedEdge = edgeFactor + weaveNoise * _EdgeFrayNoiseIntensity;

                    float edgeAlpha = saturate((jaggedEdge - 0.1) * _EdgeFraySharpness);
                    color.a *= edgeAlpha;
                }

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
```

---

## 3. Shop Item Data ScriptableObject (ShopItemData.cs)
Create a ScriptableObject class that acts as the decoupled metadata container for a shop item.

### Requirements:
- Save path options via `[CreateAssetMenu(fileName = "NewShopItemData", menuName = "Shop/Item Data")]`.
- Contains:
  - `Sprite itemIcon`
  - `string itemName` (with Multi-line `[TextArea]`)
  - `string itemDescription` (with Multi-line `[TextArea]`)
  - `string itemPrice` (supports text values e.g., "$5.00", "Free", "50 Gems")
  - `ShopItemType shopItemType` to determine categories and visual templates.

```csharp
using UnityEngine;

[CreateAssetMenu(fileName = "NewShopItemData", menuName = "Shop/Item Data", order = 1)]
public class ShopItemData : ScriptableObject
{
    [Header("Item Metadata")]
    public ItemType itemType; 
    public Sprite itemIcon;
    
    [TextArea(1, 3)]
    public string itemName;
    
    [TextArea(3, 10)]
    public string itemDescription;
    
    public string itemPrice; 

    [Header("Item Visual Category")]
    public ShopItemType shopItemType;

    [Header("Asset References")]
    public GameObject itemPrefab; 
}
```

---

## 4. Shop Item UI View Component (ShopItemUI.cs)
Create a component script to attach to the Shop Item prefab to represent the visual view.

### Requirements:
- References all public UI views: `Image itemIcon`, `TMP_Text itemNameText`, `TMP_Text itemDescriptionText`, `TMP_Text itemPriceText`, `Image itemBackgroundImg`, and `Image itemIconBackgroundImg`.
- Incorporates a cached, lazy-initialized `CanvasGroup` property. If a `CanvasGroup` is missing from the item GameObject, add it dynamically in code.
- Implement an `Initialize(ShopItemData data, Sprite customBackground = null, Sprite customIconBackground = null)` method:
  - Skip setting fields if the corresponding data value in `ShopItemData` or override Sprites is null or empty, thereby retaining the default prefab visual settings.

```csharp
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopItemUI : MonoBehaviour
{
    [Header("UI Component References")]
    public Image itemIcon;
    public TMP_Text itemNameText;
    public TMP_Text itemDescriptionText;
    public TMP_Text itemPriceText;
    public Image itemBackgroundImg;
    public Image itemIconBackgroundImg;

    private CanvasGroup canvasGroup;

    /// <summary>
    /// Gets the CanvasGroup on this GameObject, adding one dynamically if missing.
    /// </summary>
    public CanvasGroup CanvasGroup
    {
        get
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }
            return canvasGroup;
        }
    }

    /// <summary>
    /// Initializes the UI components with the values from a ShopItemData asset and background overrides.
    /// If data is missing or empty, the default prefab values remain untouched.
    /// </summary>
    public void Initialize(ShopItemData data, Sprite customBackground = null, Sprite customIconBackground = null)
    {
        if (data == null) return;

        if (itemIcon != null && data.itemIcon != null)
        {
            itemIcon.sprite = data.itemIcon;
        }

        if (itemNameText != null && !string.IsNullOrEmpty(data.itemName))
        {
            itemNameText.text = data.itemName;
        }

        if (itemDescriptionText != null && !string.IsNullOrEmpty(data.itemDescription))
        {
            itemDescriptionText.text = data.itemDescription;
        }

        if (itemPriceText != null && !string.IsNullOrEmpty(data.itemPrice))
        {
            itemPriceText.text = data.itemPrice;
        }

        if (itemBackgroundImg != null && customBackground != null)
        {
            itemBackgroundImg.sprite = customBackground;
        }

        if (itemIconBackgroundImg != null && customIconBackground != null)
        {
            itemIconBackgroundImg.sprite = customIconBackground;
        }
    }
}
```

---

## 5. Shop Controller Manager (InGameShopManager.cs)
Create the central shop manager component to control the UI layout, scrolling, snapping, scale/fade zoom effects, and navigation transitions.

### Requirements:
- **Central Visual Background Mapping**:
  - Exposes a `List<ShopItemVisuals>` configured in the Inspector (serializable helper class linking `ShopItemType` to `itemBackground` and `itemIconBackground` sprites).
  - During `Start()`, loops through `shopItemUIs` and initializes them by looking up their backgrounds from the `ShopItemType` assigned to each `ShopItemData`.
- **Y-Axis Proximity Selection**:
  - Calculates the absolute Y-distance in viewport-local space between each `ShopItemUI` and a static position anchor `selectedItemUIRef`.
  - Determines the closest item, dynamically assigning it to `selectedShopItem`.
- **Carousel Scale & Fade Transition**:
  - Calculates a normalized transition ratio `0` (center reference) to `1` (at or beyond local viewport pixel distance `transitionRange`).
  - Interpolates local scale of items from `1.0` to `0.7` and `CanvasGroup.alpha` from `1.0` to `0.7`.
- **Conflict-free Auto-Snapping**:
  - Adds an `EventTrigger` dynamically to the `ScrollRect` to capture manual drag inputs.
  - Aborts auto-snapping immediately when a manual drag begins (`isDragging = true`).
  - Automatically triggers a smooth snap alignment (`FocusOnItem`) using the curve ease when velocity falls below a threshold and drag has ended.
- **Horizontal Open/Close Transitions**:
  - Saves the starting horizontal X anchored position of `shopPanel` as the open coordinate.
  - Slides the panel horizontally between the open and closed (`closedPositionX`) states using the `scrollCurve` ease.
  - Alternates visibility between two indicator GameObjects: `openArrow` (enabled when closed) and `closeArrow` (enabled when open). Default state at start is open.

```csharp
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class ShopItemVisuals
{
    public ShopItemType itemType;
    [Header("Item Visual Backgrounds")]
    public Sprite itemBackground;
    public Sprite itemIconBackground;
}

public class InGameShopManager : MonoBehaviour
{
    public ScrollRect scrollRect; 
    public RectTransform selectedItemUIRef;
    public ShopItemUI[] shopItemUIs; 

    [Header("Scroll Behavior Settings")]
    public bool smoothScroll = true;
    public float scrollDuration = 0.3f;
    public AnimationCurve scrollCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Scale Transition Settings")]
    [Tooltip("The vertical distance threshold (in local viewport pixels) over which the scale/alpha transitions from 1.0 to 0.7.")]
    public float transitionRange = 200f;

    [Header("Selection Status")]
    public ShopItemUI selectedShopItem;

    [Header("Shop Panel Navigation")]
    public RectTransform shopPanel;
    public Button openCloseButton;
    public GameObject openArrow;
    public GameObject closeArrow;
    public float closedPositionX;
    public float panelTransitionDuration = 0.3f;

    [Header("Item Type Visual Overrides")]
    public List<ShopItemVisuals> itemTypeVisuals; 

    private float openPositionX;
    private bool isOpen = true;
    private Coroutine panelTransitionCoroutine;

    private Coroutine scrollCoroutine;
    private bool isDragging = false;
    private bool isSnapping = false;

    [Header("Shop Item Data Source")]
    public ShopItemData[] shopItemDatas; 

    private void Start()
    {
        if (shopPanel != null)
        {
            openPositionX = shopPanel.anchoredPosition.x;
        }

        if (openCloseButton != null)
        {
            openCloseButton.onClick.AddListener(ToggleShop);
        }

        if (shopItemUIs != null && shopItemDatas != null)
        {
            for (int i = 0; i < shopItemUIs.Length; i++)
            {
                if (shopItemUIs[i] != null && i < shopItemDatas.Length && shopItemDatas[i] != null)
                {
                    ShopItemData data = shopItemDatas[i];
                    Sprite bg = null;
                    Sprite iconBg = null;

                    ShopItemVisuals visuals = GetVisualsForType(data.shopItemType);
                    if (visuals != null)
                    {
                        bg = visuals.itemBackground;
                        iconBg = visuals.itemIconBackground;
                    }

                    shopItemUIs[i].Initialize(data, bg, iconBg);
                }
            }
        }

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.AddListener(OnScrollValueChanged);

            EventTrigger trigger = scrollRect.GetComponent<EventTrigger>();
            if (trigger == null)
            {
                trigger = scrollRect.gameObject.AddComponent<EventTrigger>();
            }

            EventTrigger.Entry beginDragEntry = new EventTrigger.Entry { eventID = EventTriggerType.BeginDrag };
            beginDragEntry.callback.AddListener((data) =>
            {
                isDragging = true;
                isSnapping = false;
                if (scrollCoroutine != null)
                {
                    StopCoroutine(scrollCoroutine);
                }
            });
            trigger.triggers.Add(beginDragEntry);

            EventTrigger.Entry endDragEntry = new EventTrigger.Entry { eventID = EventTriggerType.EndDrag };
            endDragEntry.callback.AddListener((data) =>
            {
                isDragging = false;
            });
            trigger.triggers.Add(endDragEntry);
        }

        if (transitionRange <= 0.1f && shopItemUIs != null && shopItemUIs.Length > 0 && shopItemUIs[0] != null)
        {
            transitionRange = shopItemUIs[0].GetComponent<RectTransform>().rect.height;
        }

        UpdateClosestSelection();

        if (selectedShopItem != null)
        {
            FocusOnItem(selectedShopItem, smooth: false);
        }

        SetShopOpen(true, smooth: false);
        UpdateItemScales();
    }

    private void OnDestroy()
    {
        if (openCloseButton != null)
        {
            openCloseButton.onClick.RemoveListener(ToggleShop);
        }

        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
        }
    }

    private void Update()
    {
        UpdateItemScales();

        if (scrollRect == null || selectedShopItem == null || isDragging || isSnapping) return;

        float velocityThreshold = 150f;
        float speed = scrollRect.velocity.magnitude;

        if (speed < velocityThreshold && speed > 0.01f)
        {
            StartSnapToClosest();
        }
        else if (speed <= 0.01f)
        {
            float refY = selectedItemUIRef.position.y;
            float itemY = selectedShopItem.GetComponent<RectTransform>().position.y;
            float distance = Mathf.Abs(itemY - refY);

            if (distance > 0.1f)
            {
                StartSnapToClosest();
            }
        }
    }

    public void ToggleShop()
    {
        SetShopOpen(!isOpen, smooth: true);
    }

    public void SetShopOpen(bool open, bool smooth)
    {
        isOpen = open;

        if (openArrow != null) openArrow.SetActive(!isOpen);
        if (closeArrow != null) closeArrow.SetActive(isOpen);

        float targetX = isOpen ? openPositionX : closedPositionX;

        if (panelTransitionCoroutine != null)
        {
            StopCoroutine(panelTransitionCoroutine);
        }

        if (smooth && gameObject.activeInHierarchy)
        {
            panelTransitionCoroutine = StartCoroutine(TransitionPanel(targetX));
        }
        else
        {
            if (shopPanel != null)
            {
                Vector2 pos = shopPanel.anchoredPosition;
                pos.x = targetX;
                shopPanel.anchoredPosition = pos;
            }
        }
    }

    private IEnumerator TransitionPanel(float targetX)
    {
        if (shopPanel == null) yield break;

        Vector2 startPos = shopPanel.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < panelTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / panelTransitionDuration);
            float curveT = scrollCurve != null ? scrollCurve.Evaluate(t) : t;
            Vector2 currentPos = shopPanel.anchoredPosition;
            currentPos.x = Mathf.Lerp(startPos.x, targetX, curveT);
            shopPanel.anchoredPosition = currentPos;
            yield return null;
        }

        Vector2 finalPos = shopPanel.anchoredPosition;
        finalPos.x = targetX;
        shopPanel.anchoredPosition = finalPos;
    }

    private void OnScrollValueChanged(Vector2 value)
    {
        UpdateClosestSelection();
    }

    private void UpdateClosestSelection()
    {
        if (shopItemUIs == null || shopItemUIs.Length == 0 || selectedItemUIRef == null) return;

        float refY = selectedItemUIRef.position.y;
        ShopItemUI closestItem = null;
        float minDistance = float.MaxValue;

        foreach (var item in shopItemUIs)
        {
            if (item == null) continue;

            float itemY = item.GetComponent<RectTransform>().position.y;
            float distance = Mathf.Abs(itemY - refY);

            if (distance < minDistance)
            {
                minDistance = distance;
                closestItem = item;
            }
        }

        if (closestItem != null && closestItem != selectedShopItem)
        {
            selectedShopItem = closestItem;
        }
    }

    public ShopItemVisuals GetVisualsForType(ShopItemType type)
    {
        if (itemTypeVisuals != null)
        {
            foreach (var visuals in itemTypeVisuals)
            {
                if (visuals != null && visuals.itemType == type)
                {
                    return visuals;
                }
            }
        }
        return null;
    }

    private void UpdateItemScales()
    {
        if (shopItemUIs == null || selectedItemUIRef == null || scrollRect == null) return;

        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
        float refLocalY = viewport.InverseTransformPoint(selectedItemUIRef.position).y;

        foreach (var item in shopItemUIs)
        {
            if (item == null) continue;

            RectTransform itemRect = item.GetComponent<RectTransform>();
            float itemLocalY = viewport.InverseTransformPoint(itemRect.position).y;
            float distance = Mathf.Abs(itemLocalY - refLocalY);

            float normalizedDist = Mathf.Clamp01(distance / Mathf.Max(transitionRange, 0.0001f));
            float scale = Mathf.Lerp(1.0f, 0.7f, normalizedDist);
            float alpha = Mathf.Lerp(1.0f, 0.7f, normalizedDist);

            itemRect.localScale = new Vector3(scale, scale, 1f);

            if (item.CanvasGroup != null)
            {
                item.CanvasGroup.alpha = alpha;
            }
        }
    }

    private void StartSnapToClosest()
    {
        isSnapping = true;
        scrollRect.velocity = Vector2.zero;
        FocusOnItem(selectedShopItem, smoothScroll);
    }

    public void FocusOnItem(ShopItemUI targetItem, bool smooth = true)
    {
        if (scrollRect == null || selectedItemUIRef == null || targetItem == null) return;

        RectTransform content = scrollRect.content;
        if (content == null) return;

        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
        RectTransform targetRect = targetItem.GetComponent<RectTransform>();

        Vector3 targetLocalPos = viewport.InverseTransformPoint(targetRect.position);
        Vector3 refLocalPos = viewport.InverseTransformPoint(selectedItemUIRef.position);

        Vector3 localDiff = refLocalPos - targetLocalPos;
        Vector2 targetAnchoredPos = content.anchoredPosition;

        if (scrollRect.horizontal)
        {
            targetAnchoredPos.x += localDiff.x;
        }
        if (scrollRect.vertical)
        {
            targetAnchoredPos.y += localDiff.y;
        }

        targetAnchoredPos = ClampAnchoredPosition(targetAnchoredPos);

        if (scrollCoroutine != null)
        {
            StopCoroutine(scrollCoroutine);
        }

        if (smooth && gameObject.activeInHierarchy)
        {
            scrollCoroutine = StartCoroutine(SmoothScrollTo(targetAnchoredPos));
        }
        else
        {
            content.anchoredPosition = targetAnchoredPos;
            scrollRect.velocity = Vector2.zero;
            isSnapping = false;
        }
    }

    private IEnumerator SmoothScrollTo(Vector2 targetPos)
    {
        RectTransform content = scrollRect.content;
        Vector2 startPos = content.anchoredPosition;
        float elapsed = 0f;

        scrollRect.StopMovement();

        while (elapsed < scrollDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / scrollDuration);
            float curveT = scrollCurve.Evaluate(t);
            content.anchoredPosition = Vector2.Lerp(startPos, targetPos, curveT);
            yield return null;
        }

        content.anchoredPosition = targetPos;
        isSnapping = false;
    }

    private Vector2 ClampAnchoredPosition(Vector2 targetAnchoredPosition)
    {
        if (scrollRect == null || scrollRect.content == null) return targetAnchoredPosition;

        RectTransform content = scrollRect.content;
        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;

        Vector2 originalPosition = content.anchoredPosition;
        content.anchoredPosition = targetAnchoredPosition;

        Canvas.ForceUpdateCanvases();

        Vector3[] viewportCorners = new Vector3[4];
        Vector3[] contentCorners = new Vector3[4];
        viewport.GetWorldCorners(viewportCorners);
        content.GetWorldCorners(contentCorners);

        Vector2 viewportMin = viewport.InverseTransformPoint(viewportCorners[0]);
        Vector2 viewportMax = viewport.InverseTransformPoint(viewportCorners[2]);
        Vector2 contentMin = viewport.InverseTransformPoint(contentCorners[0]);
        Vector2 contentMax = viewport.InverseTransformPoint(contentCorners[2]);

        Vector2 shift = Vector2.zero;

        if (scrollRect.horizontal)
        {
            float contentWidth = contentMax.x - contentMin.x;
            float viewportWidth = viewportMax.x - viewportMin.x;

            if (contentWidth <= viewportWidth)
            {
                shift.x = (viewportMin.x + viewportWidth * 0.5f) - (contentMin.x + contentWidth * 0.5f);
            }
            else
            {
                if (contentMin.x > viewportMin.x)
                {
                    shift.x = viewportMin.x - contentMin.x;
                }
                else if (contentMax.x < viewportMax.x)
                {
                    shift.x = viewportMax.x - contentMax.x;
                }
            }
        }

        if (scrollRect.vertical)
        {
            float contentHeight = contentMax.y - contentMin.y;
            float viewportHeight = viewportMax.y - viewportMin.y;

            if (contentHeight <= viewportHeight)
            {
                shift.y = (viewportMin.y + viewportHeight * 0.5f) - (contentMin.y + contentHeight * 0.5f);
            }
            else
            {
                if (contentMin.y > viewportMin.y)
                {
                    shift.y = viewportMin.y - contentMin.y;
                }
                else if (contentMax.y < viewportMax.y)
                {
                    shift.y = viewportMax.y - contentMax.y;
                }
            }
        }

        content.anchoredPosition = originalPosition;
        return targetAnchoredPosition + shift;
    }
}
```
```
