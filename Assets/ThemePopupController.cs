using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ThemePopupController : MonoBehaviour
{
    [Header("Connections")]
    public GameObject themePopupPanel;

    private const string ThemeChosenKey = "ThemeChosen";
    private bool choosing;

    void Start()
    {
        StylePopup();

        bool alreadyChosen = PlayerPrefs.GetInt(ThemeChosenKey, 0) == 1;
        if (themePopupPanel != null)
        {
            themePopupPanel.SetActive(!alreadyChosen);
            if (!alreadyChosen)
                PresentationGate.Acquire(PresentationGate.Kind.ThemePopup);
        }
    }

    public void OnLightSelected() => SelectTheme(true);
    public void OnDarkSelected() => SelectTheme(false);

    void SelectTheme(bool light)
    {
        if (choosing) return;
        choosing = true;

        if (DayNightManager.instance != null)
            DayNightManager.instance.SetMode(light);

        PlayerPrefs.SetInt(ThemeChosenKey, 1);
        PlayerPrefs.Save();

        ApplySelectionVisual(light);
        StartCoroutine(CloseAfterSelection());
    }

    IEnumerator CloseAfterSelection()
    {
        yield return new WaitForSecondsRealtime(0.32f);
        if (themePopupPanel != null) themePopupPanel.SetActive(false);
        PresentationGate.Release(PresentationGate.Kind.ThemePopup);
        choosing = false;
    }

    void StylePopup()
    {
        if (themePopupPanel == null) return;

        Image overlay = themePopupPanel.GetComponent<Image>();
        if (overlay != null)
        {
            overlay.color = new Color(0.005f, 0.012f, 0.04f, 0.94f);
            overlay.raycastTarget = true;
        }

        Transform card = FindDeep(themePopupPanel.transform, "Card");
        if (card == null) return;

        RectTransform cardRect = card.GetComponent<RectTransform>();
        if (cardRect != null)
        {
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.anchoredPosition = Vector2.zero;
            cardRect.sizeDelta = new Vector2(820f, 1060f);
        }

        Image cardImage = card.GetComponent<Image>();
        if (cardImage != null)
        {
            UIStyleKit.ApplyPanel(cardImage, UIStyleKit.BgPanel);
            cardImage.raycastTarget = true;
        }

        TextMeshProUGUI title = FindText(card, "TitleText");
        if (title != null)
        {
            title.text = "CHOOSE YOUR THEME";
            title.fontSize = 52f;
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 3f;
            title.color = UIStyleKit.TextMain;
            SetRect(title.rectTransform, new Vector2(0f, 426f), new Vector2(740f, 90f));
        }

        TextMeshProUGUI hint = EnsureLabel(card, "ThemeHint",
            "Pick the look you want for this flight.", 25f, UIStyleKit.TextSub,
            new Vector2(0f, 352f), new Vector2(700f, 52f));
        hint.enableAutoSizing = true;
        hint.fontSizeMin = 20f;
        hint.fontSizeMax = 25f;

        StyleThemeButton(card, "LightButton", "LIGHT", "Bright sky, warm colors and crisp contrast.",
            new Vector2(0f, 145f), new Color(0.13f, 0.40f, 0.72f, 1f));
        StyleThemeButton(card, "DarkButton", "DARK", "Deep space, cool glow and classic contrast.",
            new Vector2(0f, -185f), new Color(0.09f, 0.14f, 0.31f, 1f));

        RectTransform lightArrow = GetRect(card, "LightArrow");
        RectTransform darkArrow = GetRect(card, "DarkArrow");
        if (lightArrow != null) SetRect(lightArrow, new Vector2(-374f, 145f), new Vector2(58f, 58f));
        if (darkArrow != null) SetRect(darkArrow, new Vector2(-374f, -185f), new Vector2(58f, 58f));

        ThemePopupAnimator animator = card.GetComponent<ThemePopupAnimator>();
        if (animator != null) animator.RefreshLayoutBases();
    }

    void StyleThemeButton(Transform card, string buttonName, string label, string description,
        Vector2 position, Color backgroundColor)
    {
        Transform target = FindDeep(card, buttonName);
        if (target == null) return;

        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect != null) SetRect(rect, position, new Vector2(680f, 260f));

        Image background = target.GetComponent<Image>();
        if (background != null)
        {
            UIStyleKit.ApplyPanel(background, backgroundColor);
            background.raycastTarget = true;
        }

        Button button = target.GetComponent<Button>();
        if (button != null)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            colors.pressedColor = new Color(0.84f, 0.84f, 0.84f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;
        }

        TextMeshProUGUI buttonText = target.GetComponentInChildren<TextMeshProUGUI>(true);
        if (buttonText != null)
        {
            buttonText.text = label;
            buttonText.fontSize = 40f;
            buttonText.fontStyle = FontStyles.Bold;
            buttonText.characterSpacing = 3f;
            buttonText.color = UIStyleKit.TextMain;
            RectTransform textRect = buttonText.rectTransform;
            textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = new Vector2(78f, 48f);
            textRect.sizeDelta = new Vector2(430f, 62f);
        }

        RectTransform icon = GetRect(target, "Icon");
        if (icon != null) SetRect(icon, new Vector2(-235f, 12f), new Vector2(112f, 112f));

        TextMeshProUGUI desc = EnsureLabel(target, "Description", description, 24f,
            new Color(0.83f, 0.88f, 1f, 0.95f), new Vector2(78f, -28f), new Vector2(430f, 78f));
        desc.textWrappingMode = TextWrappingModes.Normal;
        desc.enableAutoSizing = true;
        desc.fontSizeMin = 19f;
        desc.fontSizeMax = 24f;

        TextMeshProUGUI status = EnsureLabel(target, "SelectionStatus", "TAP TO SELECT", 19f,
            UIStyleKit.CoinColor, new Vector2(78f, -88f), new Vector2(430f, 34f));
        status.fontStyle = FontStyles.Bold;
        status.characterSpacing = 2f;
    }

    void ApplySelectionVisual(bool light)
    {
        if (themePopupPanel == null) return;
        Transform card = FindDeep(themePopupPanel.transform, "Card");
        if (card == null) return;

        SetSelectedState(FindDeep(card, "LightButton"), light);
        SetSelectedState(FindDeep(card, "DarkButton"), !light);

        ThemePopupAnimator animator = card.GetComponent<ThemePopupAnimator>();
        if (animator != null) animator.SetSelection(light);
    }

    static void SetSelectedState(Transform button, bool selected)
    {
        if (button == null) return;
        TextMeshProUGUI status = FindText(button, "SelectionStatus");
        if (status != null)
        {
            status.text = selected ? "SELECTED" : "NOT SELECTED";
            status.color = selected ? new Color(0.42f, 1f, 0.66f, 1f) : UIStyleKit.TextSub;
        }

        Image image = button.GetComponent<Image>();
        if (image != null)
            image.color = selected ? UIStyleKit.BtnSelected : new Color(image.color.r * 0.72f,
                image.color.g * 0.72f, image.color.b * 0.72f, 1f);

        Outline outline = button.GetComponent<Outline>();
        if (outline == null) outline = button.gameObject.AddComponent<Outline>();
        outline.effectColor = selected
            ? new Color(0.36f, 1f, 0.66f, 0.95f)
            : new Color(0.22f, 0.42f, 0.7f, 0.35f);
        outline.effectDistance = selected ? new Vector2(4f, -4f) : new Vector2(2f, -2f);
        outline.useGraphicAlpha = true;
    }

    static TextMeshProUGUI EnsureLabel(Transform parent, string name, string text, float fontSize,
        Color color, Vector2 position, Vector2 size)
    {
        TextMeshProUGUI existing = FindText(parent, name);
        if (existing != null)
        {
            existing.text = text;
            existing.fontSize = fontSize;
            existing.color = color;
            SetRect(existing.rectTransform, position, size);
            return existing;
        }

        TextMeshProUGUI label = UIStyleKit.MakeLabel(parent, text, fontSize, color, position, size);
        label.gameObject.name = name;
        return label;
    }

    static TextMeshProUGUI FindText(Transform root, string name)
    {
        Transform target = FindDeep(root, name);
        TextMeshProUGUI text = target != null ? target.GetComponent<TextMeshProUGUI>() : null;
        if (text != null) UIStyleKit.ApplyRuntimeFont(text, root);
        return text;
    }

    static RectTransform GetRect(Transform root, string name)
    {
        Transform target = FindDeep(root, name);
        return target != null ? target.GetComponent<RectTransform>() : null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name.Trim() == name) return root;
        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static void SetRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
