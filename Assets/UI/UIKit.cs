using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The button family, built from UIDesign tokens and UIGlass surfaces.
//
// Screens call these instead of styling Images by hand. That is the mechanism
// that keeps proportion, radius, rim, shadow and label treatment identical
// across a coin chip, a sound disc and the launch pill — they are literally the
// same code with different sizes.
public static class UIKit
{
    // ── surfaces ─────────────────────────────────────────────────────────────

    /// Turns an existing Image into a glass surface: rounded fill, lit rim,
    /// soft shadow. Existing handlers, names and positions are untouched.
    public static void MakeGlass(GameObject host, float radius,
        UITinted.Role role = UITinted.Role.Glass, float alphaScale = 1f,
        bool shadow = true, bool interactive = false)
    {
        Image image = host.GetComponent<Image>();
        if (image == null) image = host.AddComponent<Image>();

        image.sprite = UIGlass.Panel(radius);
        image.type = Image.Type.Sliced;
        image.raycastTarget = interactive;
        UITinted.Attach(host, role, alphaScale);

        // The legacy Outline is a flat copy nudged diagonally. A real rim
        // replaces it everywhere, so remove any left behind.
        Outline legacy = host.GetComponent<Outline>();
        if (legacy != null) Object.Destroy(legacy);

        EnsureRim(host, radius);
        if (shadow) EnsureShadow(host, radius);
    }

    public static void MakeGlassDisc(GameObject host, UITinted.Role role = UITinted.Role.Glass,
        float alphaScale = 1f, bool shadow = true, bool interactive = false)
    {
        Image image = host.GetComponent<Image>();
        if (image == null) image = host.AddComponent<Image>();

        image.sprite = UIGlass.Disc;
        image.type = Image.Type.Simple;
        image.raycastTarget = interactive;
        UITinted.Attach(host, role, alphaScale);

        Outline legacy = host.GetComponent<Outline>();
        if (legacy != null) Object.Destroy(legacy);

        EnsureChildImage(host, "Rim", UIGlass.DiscRim, Image.Type.Simple, Vector2.zero,
            UITinted.Role.Rim, 1f, 0);
        if (shadow) EnsureShadow(host, 0f);
    }

    /// The hairline that makes a surface read as glass rather than as paint.
    public static void EnsureRim(GameObject host, float radius, float alphaScale = 1f)
        => EnsureChildImage(host, "Rim", UIGlass.Rim(radius), Image.Type.Sliced, Vector2.zero,
            UITinted.Role.Rim, alphaScale, 0);

    /// Takes a surface's rim off the world palette and pins it to one colour.
    /// Reserved for the call to action — if more than one surface uses this, the
    /// palette has stopped meaning anything.
    public static void OverrideRim(GameObject host, Color color)
    {
        Transform rim = host.transform.Find("Rim");
        if (rim == null) return;

        UITinted tint = rim.GetComponent<UITinted>();
        if (tint != null)
        {
            // Disable before destroying: Destroy lands at end of frame and the
            // component would otherwise write the palette colour back once more.
            tint.enabled = false;
            Object.Destroy(tint);
        }

        Image image = rim.GetComponent<Image>();
        if (image != null) image.color = color;
    }

    /// A soft shadow sibling, drawn behind the control. It has to be a sibling:
    /// a child would draw on top of the very surface it is meant to lift.
    public static void EnsureShadow(GameObject host, float radius, float spread = 26f,
        float drop = 9f, float alpha = 0.34f)
    {
        RectTransform hostRect = host.GetComponent<RectTransform>();
        if (hostRect == null || hostRect.parent == null) return;

        string name = host.name + "Shadow";
        Transform existing = hostRect.parent.Find(name);

        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null) go.transform.SetParent(hostRect.parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null) rect = go.AddComponent<RectTransform>();
        rect.anchorMin = hostRect.anchorMin;
        rect.anchorMax = hostRect.anchorMax;
        rect.pivot = hostRect.pivot;
        rect.anchoredPosition = hostRect.anchoredPosition + new Vector2(0f, -drop);
        rect.sizeDelta = hostRect.sizeDelta + new Vector2(spread, spread);

        Image image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.sprite = UIGlass.Glow;
        image.type = Image.Type.Simple;
        image.raycastTarget = false;
        // Deliberately not world-tinted: a shadow is an absence of light, and
        // tinting it toward the world accent makes it read as a coloured glow.
        image.color = new Color(0.004f, 0.008f, 0.024f, alpha);

        go.transform.SetSiblingIndex(hostRect.GetSiblingIndex());

        // A shadow has to be a sibling to draw behind its host, which means it
        // does not inherit the host being switched off. Panels like Game Over
        // spend most of their life inactive and would otherwise leave a dark
        // smudge floating on the screen.
        UIShadowLink link = host.GetComponent<UIShadowLink>();
        if (link == null) link = host.AddComponent<UIShadowLink>();
        link.Bind(go);
    }

    /// Pass fixedColor for a child that must stay off the world palette — the
    /// gold star, the coin. Everything else inherits.
    public static Image EnsureChildImage(GameObject host, string name, Sprite sprite,
        Image.Type type, Vector2 inset, UITinted.Role role, float alphaScale, int siblingIndex,
        Color? fixedColor = null)
    {
        Transform existing = host.transform.Find(name);
        GameObject go = existing != null ? existing.gameObject : new GameObject(name);
        if (existing == null) go.transform.SetParent(host.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null) rect = go.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = inset;
        rect.offsetMax = -inset;

        Image image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = type;
        image.raycastTarget = false;

        if (fixedColor.HasValue)
        {
            UITinted tint = go.GetComponent<UITinted>();
            if (tint != null)
            {
                // Disabled first: Destroy lands at end of frame, and the
                // component would write the palette colour back once more.
                tint.enabled = false;
                Object.Destroy(tint);
            }
            image.color = fixedColor.Value;
        }
        else
        {
            UITinted.Attach(go, role, alphaScale);
        }

        go.transform.SetSiblingIndex(siblingIndex);
        return image;
    }

    // ── buttons ──────────────────────────────────────────────────────────────

    /// The shared icon disc: sound, help, day/night, pause. One silhouette, one
    /// glyph size, one rim, whatever sprite the managers swap in.
    public static void StyleIconButton(Transform button, string iconName = null,
        float size = UIDesign.IconButtonSize)
    {
        if (button == null) return;

        RectTransform rect = button.GetComponent<RectTransform>();
        if (rect != null) rect.sizeDelta = new Vector2(size, size);

        // The button's own Image is the disc; the glyph moves to a child so the
        // managers that swap sprites keep working on a known target.
        MakeGlassDisc(button.gameObject, UITinted.Role.Glass, 1f, true, true);

        Image glyph = EnsureGlyph(button.gameObject, size);
        if (iconName != null) glyph.sprite = UIIcons.Get(iconName);

        AddPressFeedback(button.gameObject);
        UIMotion.Attach(button.gameObject, UIMotion.Mode.Breathe, 0.75f, 4.6f);
    }

    /// The glyph layer of an icon button, created on demand.
    public static Image EnsureGlyph(GameObject host, float discSize)
    {
        Transform existing = host.transform.Find("Glyph");
        GameObject go = existing != null ? existing.gameObject : new GameObject("Glyph");
        if (existing == null) go.transform.SetParent(host.transform, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null) rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.one * (discSize * UIDesign.IconGlyphRatio);

        Image image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = UIDesign.TextMain;

        go.transform.SetAsLastSibling();
        return image;
    }

    /// A pill: glass surface, optional leading icon, one label. Used for the
    /// shop, the launch call and every major panel button.
    public static void StylePill(Transform target, string label, float radius,
        UITinted.Role role = UITinted.Role.Glass, string iconName = null,
        float fontSize = UIDesign.TypeButton, Color? labelColor = null)
    {
        if (target == null) return;

        MakeGlass(target.gameObject, radius, role, 1f, true, true);

        float iconInset = 0f;
        if (iconName != null)
        {
            RectTransform rect = target.GetComponent<RectTransform>();
            float height = rect != null ? rect.sizeDelta.y : UIDesign.ButtonHeightPill;
            Image icon = EnsureLeadingIcon(target.gameObject, iconName, height);
            iconInset = icon.rectTransform.sizeDelta.x + 34f;
        }

        TextMeshProUGUI text = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
        {
            text.text = label;
            StyleText(text, fontSize, UIDesign.TrackButton,
                labelColor ?? UIDesign.TextMain, FontStyles.Bold);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(iconInset, 0f);
            rect.offsetMax = new Vector2(-18f, 0f);
            text.transform.SetAsLastSibling();
        }

        AddPressFeedback(target.gameObject);
    }

    static Image EnsureLeadingIcon(GameObject host, string iconName, float height)
    {
        Transform existing = host.transform.Find("LeadingIcon");
        GameObject go = existing != null ? existing.gameObject : new GameObject("LeadingIcon");
        if (existing == null) go.transform.SetParent(host.transform, false);

        float glyph = height * 0.56f;

        RectTransform rect = go.GetComponent<RectTransform>();
        if (rect == null) rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(height * 0.30f, 0f);
        rect.sizeDelta = new Vector2(glyph, glyph);

        Image image = go.GetComponent<Image>();
        if (image == null) image = go.AddComponent<Image>();
        image.sprite = UIIcons.Get(iconName);
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = UIDesign.TextMain;
        return image;
    }

    public static void AddPressFeedback(GameObject host)
    {
        if (host.GetComponent<UIButtonPressFeedback>() == null)
            host.AddComponent<UIButtonPressFeedback>();

        // Colour tinting fights the glass tint; the press is carried by scale.
        Button button = host.GetComponent<Button>();
        if (button == null) return;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.86f, 0.90f, 1f, 1f);
        colors.selectedColor = Color.white;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
    }

    // ── typography ───────────────────────────────────────────────────────────

    /// One place that decides what "a label" means: size, tracking, colour,
    /// weight, wrapping. Every screen goes through it.
    public static void StyleText(TMP_Text text, float size, float tracking, Color color,
        FontStyles style = FontStyles.Normal,
        TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        if (text == null) return;

        UIStyleKit.ApplyRuntimeFont(text, text.transform);
        text.fontSize = size;
        text.characterSpacing = tracking;
        text.color = color;
        text.fontStyle = style;
        text.alignment = align;
        text.raycastTarget = false;

        // Autosizing only ever shrinks, and only as far as legibility allows.
        text.enableAutoSizing = true;
        text.fontSizeMin = size * 0.74f;
        text.fontSizeMax = size;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.margin = new Vector4(8f, 2f, 8f, 2f);

        // Tracking adds space after the last glyph too, which visually shifts a
        // centred line left. Half the tracking back is the standard correction.
        if (align == TextAlignmentOptions.Center && tracking > 0f)
            text.margin = new Vector4(8f + tracking * 0.5f, 2f, 8f, 2f);
    }

    /// Display type sits over artwork, so it gets a shadow rather than a heavier
    /// outline: it separates from the background without thickening the letters.
    public static void StyleDisplay(TMP_Text text, float size, float tracking, Color color)
    {
        StyleText(text, size, tracking, color, FontStyles.Bold);
        UIStyleKit.ApplySoftShadow(text, new Color(0.004f, 0.012f, 0.045f, 0.75f),
            new Vector2(0.6f, -0.9f), 0.36f);
    }
}

// Ties a shadow sibling's visibility to its host.
//
// Event-driven, not polled: a disabled GameObject cannot run its own Update to
// switch itself back on, so the host has to drive it.
[DisallowMultipleComponent]
public sealed class UIShadowLink : MonoBehaviour
{
    [SerializeField] private GameObject shadow;

    public void Bind(GameObject target)
    {
        shadow = target;
        Sync();
    }

    void OnEnable() => Sync();

    void OnDisable()
    {
        if (shadow != null) shadow.SetActive(false);
    }

    void Sync()
    {
        if (shadow != null) shadow.SetActive(isActiveAndEnabled && gameObject.activeInHierarchy);
    }
}
