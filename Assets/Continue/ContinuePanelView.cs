using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ContinuePanelView : MonoBehaviour
{
    public enum OfferType
    {
        Normal,
        SoClose
    }

    [SerializeField, Min(0.05f)] private float fadeDuration = 0.24f;

    private GameObject root;
    private RectTransform card;
    private Image overlay;
    private Image cardImage;
    private Outline cardOutline;
    private CanvasGroup canvasGroup;
    private Button watchButton;
    private Button declineButton;
    private TextMeshProUGUI title;
    private TextMeshProUGUI subtitle;
    private TextMeshProUGUI watchLabel;
    private TextMeshProUGUI declineLabel;
    private Image facetLeft;
    private Image facetRight;
    private Coroutine fadeCoroutine;
    private Action watchAction;
    private Action declineAction;
    private OfferType currentOffer;

    public void Initialize()
    {
        if (root != null)
            return;

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("Continue Panel requires a Canvas in the scene.");
            return;
        }

        Build(canvas.transform);
    }

    public bool Show(OfferType offerType, bool rewardAvailable, Action onWatch, Action onDecline)
    {
        Initialize();
        if (root == null)
            return false;

        currentOffer = offerType;
        watchAction = onWatch;
        declineAction = onDecline;
        watchButton.interactable = rewardAvailable;
        declineButton.interactable = true;
        ApplyOfferStyle(offerType, rewardAvailable);

        root.transform.SetAsLastSibling();
        root.SetActive(true);
        PresentationGate.Acquire(PresentationGate.Kind.ContinueOffer);
        StartFade(0f, 1f, false, null, waitForCrashReveal: true);
        return true;
    }

    public void SetWaiting()
    {
        if (root == null)
            return;

        watchButton.interactable = false;
        declineButton.interactable = false;
        subtitle.text = "Preparing reward...";
    }

    public void SetUnavailable()
    {
        if (root == null)
            return;

        watchButton.interactable = false;
        declineButton.interactable = true;
        subtitle.text = currentOffer == OfferType.SoClose
            ? "Your last-chance reward is not available right now."
            : "Rewarded ad is not available.\nYou can still finish this run.";
    }

    public void Hide(Action onHidden)
    {
        if (root == null || !root.activeSelf)
        {
            onHidden?.Invoke();
            return;
        }

        watchButton.interactable = false;
        declineButton.interactable = false;
        StartFade(canvasGroup.alpha, 0f, true, onHidden);
    }

    private void StartFade(
        float from,
        float to,
        bool deactivate,
        Action onComplete,
        bool waitForCrashReveal = false)
    {
        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);
        fadeCoroutine = StartCoroutine(Fade(from, to, deactivate, onComplete, waitForCrashReveal));
    }

    private IEnumerator Fade(
        float from,
        float to,
        bool deactivate,
        Action onComplete,
        bool waitForCrashReveal)
    {
        float elapsed = 0f;
        Vector3 startScale = to > from ? Vector3.one * 0.88f : Vector3.one;
        Vector3 endScale = to > from ? Vector3.one : Vector3.one * 0.94f;
        canvasGroup.alpha = from;
        card.localScale = startScale;

        // The offer's overlay dims the whole screen, so the fade — and only the
        // fade — holds until the crash has been read. The offer is already live
        // by this point: the gate is held and the reward path is prepared.
        if (waitForCrashReveal)
        {
            int revealToken = CrashRevealDelay.Token;
            yield return CrashRevealDelay.WaitForReveal(revealToken);
            if (!CrashRevealDelay.IsCurrent(revealToken))
            {
                // Gameplay was handed back while the offer was still invisible.
                // Showing it now would drop an obsolete panel over a live run.
                fadeCoroutine = null;
                canvasGroup.alpha = 0f;
                root.SetActive(false);
                PresentationGate.Release(PresentationGate.Kind.ContinueOffer);
                yield break;
            }
        }

        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeDuration));
            canvasGroup.alpha = Mathf.Lerp(from, to, progress);
            card.localScale = Vector3.LerpUnclamped(startScale, endScale, progress);
            yield return null;
        }

        canvasGroup.alpha = to;
        card.localScale = endScale;
        fadeCoroutine = null;
        if (deactivate)
        {
            root.SetActive(false);
            PresentationGate.Release(PresentationGate.Kind.ContinueOffer);
        }
        onComplete?.Invoke();
    }

    private void Build(Transform canvas)
    {
        root = new GameObject("ContinuePanel");
        root.transform.SetParent(canvas, false);
        RectTransform rootRect = root.AddComponent<RectTransform>();
        Stretch(rootRect);

        overlay = root.AddComponent<Image>();
        overlay.color = new Color(0.08f, 0.045f, 0.025f, 0.82f);
        overlay.raycastTarget = true;

        canvasGroup = root.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        GameObject cardObject = new GameObject("LowPolyCard");
        cardObject.transform.SetParent(root.transform, false);
        card = cardObject.AddComponent<RectTransform>();
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
        card.pivot = new Vector2(0.5f, 0.5f);
        card.sizeDelta = new Vector2(620f, 520f);

        cardImage = cardObject.AddComponent<Image>();
        UIStyleKit.ApplyPanel(cardImage, new Color(1f, 0.91f, 0.72f, 1f));
        cardImage.raycastTarget = true;

        cardOutline = cardObject.AddComponent<Outline>();
        cardOutline.effectColor = new Color(0.85f, 0.27f, 0.035f, 0.9f);
        cardOutline.effectDistance = new Vector2(5f, -5f);

        facetLeft = AddFacet(cardObject.transform, "FacetLeft", new Vector2(-255f, 190f), new Vector2(92f, 92f),
            21f, new Color(1f, 0.47f, 0.08f, 0.34f));
        facetRight = AddFacet(cardObject.transform, "FacetRight", new Vector2(245f, -185f), new Vector2(112f, 112f),
            43f, new Color(0.92f, 0.25f, 0.035f, 0.25f));

        title = UIStyleKit.MakeLabel(
            cardObject.transform, "CONTINUE?", 58f, new Color(0.34f, 0.12f, 0.045f, 1f),
            new Vector2(0f, 148f), new Vector2(520f, 90f), FontStyles.Bold);
        title.characterSpacing = 2f;

        subtitle = UIStyleKit.MakeLabel(
            cardObject.transform, string.Empty, 28f, new Color(0.42f, 0.25f, 0.14f, 1f),
            new Vector2(0f, 58f), new Vector2(510f, 100f), FontStyles.Normal);

        watchButton = UIStyleKit.MakeButtonAnchored(
            cardObject.transform, "WatchAdButton", "WATCH AD", new Vector2(0f, -72f),
            new Vector2(500f, 92f), new Color(0.96f, 0.31f, 0.045f, 1f), HandleWatch, 31f);

        declineButton = UIStyleKit.MakeButtonAnchored(
            cardObject.transform, "NoThanksButton", "NO THANKS", new Vector2(0f, -184f),
            new Vector2(500f, 78f), new Color(0.76f, 0.61f, 0.40f, 1f), HandleDecline, 25f);
        watchLabel = watchButton.GetComponentInChildren<TextMeshProUGUI>();
        declineLabel = declineButton.GetComponentInChildren<TextMeshProUGUI>();

        root.SetActive(false);
    }

    private void ApplyOfferStyle(OfferType offerType, bool rewardAvailable)
    {
        bool isSoClose = offerType == OfferType.SoClose;
        title.text = isSoClose ? "\U0001F525 SO CLOSE!" : "CONTINUE?";
        title.fontSize = isSoClose ? 64f : 58f;
        title.color = isSoClose
            ? new Color(0.58f, 0.07f, 0.015f, 1f)
            : new Color(0.34f, 0.12f, 0.045f, 1f);
        subtitle.text = rewardAvailable
            ? isSoClose
                ? "You were almost there!\nWatch one last ad to finish the level."
                : "Watch a short ad to continue\nfrom your last planet."
            : "Rewarded ad is not available yet.";
        if (watchLabel != null) watchLabel.text = "WATCH AD";
        if (declineLabel != null) declineLabel.text = isSoClose ? "GIVE UP" : "NO THANKS";

        overlay.color = isSoClose
            ? new Color(0.16f, 0.015f, 0.01f, 0.88f)
            : new Color(0.08f, 0.045f, 0.025f, 0.82f);
        cardImage.color = isSoClose
            ? new Color(1f, 0.78f, 0.32f, 1f)
            : new Color(1f, 0.91f, 0.72f, 1f);
        cardOutline.effectColor = isSoClose
            ? new Color(1f, 0.12f, 0.015f, 1f)
            : new Color(0.85f, 0.27f, 0.035f, 0.9f);
        cardOutline.effectDistance = isSoClose ? new Vector2(8f, -8f) : new Vector2(5f, -5f);
        facetLeft.color = isSoClose
            ? new Color(1f, 0.17f, 0.015f, 0.62f)
            : new Color(1f, 0.47f, 0.08f, 0.34f);
        facetRight.color = isSoClose
            ? new Color(1f, 0.55f, 0.015f, 0.55f)
            : new Color(0.92f, 0.25f, 0.035f, 0.25f);
    }

    private static Image AddFacet(
        Transform parent,
        string name,
        Vector2 position,
        Vector2 size,
        float rotation,
        Color color)
    {
        GameObject facetObject = new GameObject(name);
        facetObject.transform.SetParent(parent, false);
        RectTransform rect = facetObject.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
        Image image = facetObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private void HandleWatch()
    {
        watchAction?.Invoke();
    }

    private void HandleDecline()
    {
        declineAction?.Invoke();
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
