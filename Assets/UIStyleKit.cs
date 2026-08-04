using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Tüm UI elemanları için ortak renk paleti, sprite ve yardımcı metotlar.
// MonoBehaviour gerektirmez — static kullanım.
public static class UIStyleKit
{
    // ── Renk Paleti (uzay teması) ─────────────────────────────────────────────
    public static readonly Color BgOverlay   = new Color(0.03f, 0.04f, 0.10f, 0.94f);
    public static readonly Color BgPanel     = new Color(0.07f, 0.09f, 0.18f, 0.96f);
    public static readonly Color BgCard      = new Color(0.10f, 0.13f, 0.25f, 0.98f);
    public static readonly Color BtnPrimary  = new Color(0.09f, 0.34f, 0.78f, 1f);    // Mavi
    public static readonly Color BtnAccent   = new Color(1f,    0.42f, 0.06f, 1f);    // Turuncu (thruster)
    public static readonly Color BtnSuccess  = new Color(0.09f, 0.55f, 0.30f, 1f);    // Yeşil
    public static readonly Color BtnSelected = new Color(0.08f, 0.48f, 0.26f, 1f);    // Koyu yeşil (seçili)
    public static readonly Color BtnDanger   = new Color(0.75f, 0.12f, 0.12f, 1f);    // Kırmızı
    public static readonly Color BtnNeutral  = new Color(0.15f, 0.18f, 0.30f, 1f);    // Nötr gri-mavi
    public static readonly Color BtnGold     = new Color(0.85f, 0.62f, 0.02f, 1f);    // Altın
    public static readonly Color CoinColor   = new Color(1f,    0.82f, 0.10f, 1f);    // Coin sarısı
    public static readonly Color TextMain    = new Color(0.95f, 0.96f, 1.00f, 1f);
    public static readonly Color TextSub     = new Color(0.65f, 0.70f, 0.82f, 1f);

    // ── Paylaşılan Sprite (lazy) ──────────────────────────────────────────────
    private static Sprite _roundRect;
    private static Sprite _circle;
    private static TMP_FontAsset _runtimeFont;

    // Baked assets win over generation, so surfaces drawn with them can be stored in the
    // serialized Main Menu rather than becoming null references. See MenuBakedArt.
    public static Sprite RoundedRect
    {
        get
        {
            if (_roundRect == null)
                _roundRect = MenuBakedArt.Load("UIStyleKit_RoundedRect") ?? MakeRoundedRectSprite(64, 64, 14);
            return _roundRect;
        }
    }

    public static Sprite Circle
    {
        get
        {
            if (_circle == null)
                _circle = MenuBakedArt.Load("UIStyleKit_Circle") ?? MakeCircleSprite(32);
            return _circle;
        }
    }

    // ── Sprite Fabrikası ──────────────────────────────────────────────────────

    // 9-slice uyumlu yuvarlak köşeli dikdörtgen
    public static Sprite MakeRoundedRectSprite(int w, int h, int radius)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.name = "UIStyleKit_RoundedRect";
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, InRoundRect(x, y, w, h, radius) ? Color.white : Color.clear);
        tex.Apply();
        float b = radius;
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        sprite.name = "UIStyleKit_RoundedRect";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    static bool InRoundRect(int x, int y, int w, int h, int r)
    {
        int cx = x < r ? r : (x >= w - r ? w - r - 1 : -1);
        int cy = y < r ? r : (y >= h - r ? h - r - 1 : -1);
        if (cx >= 0 && cy >= 0)
            return Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) <= r;
        return true;
    }

    public static Sprite MakeCircleSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "UIStyleKit_Circle";
        tex.hideFlags = HideFlags.HideAndDontSave;
        tex.filterMode = FilterMode.Bilinear;
        float c = size * 0.5f, r = c - 1f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                tex.SetPixel(x, y, d <= r ? Color.white : Color.clear);
            }
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.name = "UIStyleKit_Circle";
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // ── Image stilleyici ──────────────────────────────────────────────────────

    public static void ApplyPanel(Image img, Color color)
    {
        img.sprite  = RoundedRect;
        img.type    = Image.Type.Sliced;
        img.color   = color;
        img.raycastTarget = false;
    }

    // TMP'nin kendi underlay katmanı: metnin altına yumuşak bir gölge koyar.
    // Outline'ı kalınlaştırmadan başlığı arka plandan ayırır; malzeme örneği başına
    // tek ek çizim çağrısı olduğu için yalnızca büyük başlıklarda kullanılır.
    public static void ApplySoftShadow(TMP_Text text, Color color, Vector2 offset, float softness,
        float dilate = 0f)
    {
        if (text == null) return;

        Material material = text.fontMaterial;
        if (material == null) return;

        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetColor(ShaderUtilities.ID_UnderlayColor, color);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, offset.x);
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, offset.y);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, softness);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, dilate);
    }

    // ── Button fabrikası ──────────────────────────────────────────────────────

    public static Button MakeButton(Transform parent, string name, string label,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
        Color bgColor, System.Action onClick, float fontSize = 18f)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;

        Image img = go.AddComponent<Image>();
        ApplyPanel(img, bgColor);
        img.raycastTarget = true;

        Button btn = go.AddComponent<Button>();
        ColorBlock cb   = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        cb.pressedColor     = new Color(0.80f, 0.80f, 0.80f, 1f);
        cb.selectedColor    = Color.white;
        btn.colors          = cb;
        if (onClick != null) btn.onClick.AddListener(() => onClick());

        // Metin katmanı
        ConfigureButtonLabel(AddLabel(go.transform, label, fontSize, TextMain, FontStyles.Bold), fontSize);
        return btn;
    }

    // Anchor tabanlı kısayol versiyonu
    public static Button MakeButtonAnchored(Transform parent, string name, string label,
        Vector2 pos, Vector2 size, Color bgColor, System.Action onClick,
        float fontSize = 18f,
        Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin ?? new Vector2(0.5f, 0.5f);
        rt.anchorMax        = anchorMax ?? new Vector2(0.5f, 0.5f);
        rt.pivot            = pivot     ?? new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        Image img = go.AddComponent<Image>();
        ApplyPanel(img, bgColor);
        img.raycastTarget = true;

        Button btn = go.AddComponent<Button>();
        ColorBlock cb   = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor     = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.selectedColor    = Color.white;
        btn.colors          = cb;
        if (onClick != null) btn.onClick.AddListener(() => onClick());

        ConfigureButtonLabel(AddLabel(go.transform, label, fontSize, TextMain, FontStyles.Bold), fontSize);
        return btn;
    }

    // ── TextMeshPro label ─────────────────────────────────────────────────────

    public static TextMeshProUGUI AddLabel(Transform parent, string text, float fontSize,
        Color color, FontStyles style = FontStyles.Normal,
        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        GameObject go = new GameObject("Lbl");
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(6f, 0f);
        rt.offsetMax = new Vector2(-6f, 0f);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        ApplyRuntimeFont(tmp, parent);
        tmp.text          = text;
        tmp.fontSize      = fontSize;
        tmp.color         = color;
        tmp.fontStyle     = style;
        tmp.alignment     = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    // Belirli pozisyon/boyutlu label
    public static TextMeshProUGUI MakeLabel(Transform parent, string text, float fontSize,
        Color color, Vector2 pos, Vector2 size,
        FontStyles style = FontStyles.Normal,
        TextAlignmentOptions align = TextAlignmentOptions.Center,
        Vector2? anchorMin = null, Vector2? anchorMax = null)
    {
        GameObject go = new GameObject("Lbl_" + text.Substring(0, Mathf.Min(text.Length, 10)));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin ?? new Vector2(0.5f, 0.5f);
        rt.anchorMax        = anchorMax ?? new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        ApplyRuntimeFont(tmp, parent);
        tmp.text          = text;
        tmp.fontSize      = fontSize;
        tmp.color         = color;
        tmp.fontStyle     = style;
        tmp.alignment     = align;
        tmp.raycastTarget = false;
        return tmp;
    }

    public static void ConfigureButtonLabel(TextMeshProUGUI tmp, float maxSize)
    {
        if (tmp == null) return;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = Mathf.Max(14f, maxSize * 0.68f);
        tmp.fontSizeMax = maxSize;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.margin = new Vector4(10f, 2f, 10f, 2f);
    }

    public static void ApplyRuntimeFont(TMP_Text text, Transform context = null)
    {
        if (text == null) return;
        if (_runtimeFont == null) _runtimeFont = FindRuntimeFont(context);
        if (_runtimeFont != null) text.font = _runtimeFont;
    }

    static TMP_FontAsset FindRuntimeFont(Transform context)
    {
        Transform root = context != null ? context.root : null;
        TMP_Text fallback = null;

        if (root != null)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null || text.font == null) continue;
                if (fallback == null) fallback = text;
                if (text.font.name.IndexOf("Fredoka", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return text.font;
            }
        }

        if (fallback != null) return fallback.font;

        foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (text == null || text.font == null) continue;
            if (text.font.name.IndexOf("Fredoka", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return text.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    // ── Sahne butonlarını stillemek için yardımcı ─────────────────────────────

    // Varolan bir Button + Image'i modern stile dönüştürür. Özel sprite'ı olan butonlara dokunmaz.
    public static void ResyleButton(Button btn, Color bgColor)
    {
        if (btn == null) return;
        Image img = btn.GetComponent<Image>();
        if (img == null) return;

        // Özel ikon sprite'ı varsa arka plan Image'ini değil, arka plan child'ını ara
        // DayNightButton / SoundButton gibi icon-based butonlar geçilir
        bool hasCustomSprite = img.sprite != null
            && img.sprite.name != "UISprite"
            && img.sprite.name != "Background"
            && img.sprite.name != "UIMask";
        if (hasCustomSprite) return;

        ApplyPanel(img, bgColor);
        img.raycastTarget = true;

        ColorBlock cb   = btn.colors;
        cb.normalColor      = Color.white;
        cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
        cb.pressedColor     = new Color(0.82f, 0.82f, 0.82f, 1f);
        cb.selectedColor    = Color.white;
        btn.colors          = cb;
    }
}
