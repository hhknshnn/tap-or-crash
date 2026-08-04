using UnityEngine;
using UnityEngine.UI;

// The quiet layer. Nothing in this UI is perfectly still, and nothing in it is
// animated enough to notice.
//
// Two rules make the difference between premium and cheap:
//
//   Amplitudes are tiny. A breathing button moves about one percent. The player
//   should feel that the screen is alive without ever watching it move.
//
//   Every instance carries its own phase, derived from its instance id. Motion
//   that starts in lockstep reads as a screensaver; motion that drifts reads as
//   a room full of objects.
//
// Unscaled time throughout, so a paused game still breathes.
[DisallowMultipleComponent]
public sealed class UIMotion : MonoBehaviour
{
    public enum Mode
    {
        Breathe,   // buttons: a shallow scale swell
        Float,     // logos and emblems: a slow vertical drift
        Hover,     // the two together, for the largest showcase pieces
        Pulse,     // halos: alpha only
        Shine,     // coins: a rare, quick specular tick
    }

    [SerializeField] private Mode mode = Mode.Breathe;
    [SerializeField] private float amplitude = 1f;
    [SerializeField] private float period = 3.4f;

    private RectTransform rect;
    private Graphic graphic;
    private UIButtonPressFeedback press;

    private Vector2 basePosition;
    private float baseAlpha;
    private float phase;
    private bool baselined;

    public static UIMotion Attach(GameObject target, Mode mode, float amplitude = 1f,
        float period = 3.4f)
    {
        UIMotion motion = target.GetComponent<UIMotion>();
        if (motion == null) motion = target.AddComponent<UIMotion>();
        motion.mode = mode;
        motion.amplitude = amplitude;
        motion.period = period;
        motion.baselined = false;
        return motion;
    }

    /// Call after moving the element by hand; the drift is relative to wherever
    /// the layout finally put it.
    public void Rebaseline() => baselined = false;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        graphic = GetComponent<Graphic>();
        press = GetComponent<UIButtonPressFeedback>();
        // Hand ourselves over so only one of us writes localScale.
        if (press != null) press.BindMotion(this);

        // Every instance gets its own phase. Motion that starts in lockstep
        // reads as a screensaver; motion that drifts reads as a living screen.
        phase = Random.value * Mathf.PI * 2f;
    }

    void OnEnable() => baselined = false;

    void LateUpdate()
    {
        if (!baselined)
        {
            if (rect != null) basePosition = rect.anchoredPosition;
            if (graphic != null) baseAlpha = graphic.color.a;
            baselined = true;
        }

        float wave = Mathf.Sin(Time.unscaledTime * (Mathf.PI * 2f / period) + phase);

        switch (mode)
        {
            case Mode.Breathe:
                ApplyScale(1f + wave * 0.012f * amplitude);
                break;

            case Mode.Float:
                if (rect != null)
                    rect.anchoredPosition = basePosition + new Vector2(0f, wave * 5.5f * amplitude);
                break;

            case Mode.Hover:
                if (rect != null)
                    rect.anchoredPosition = basePosition + new Vector2(0f, wave * 4.5f * amplitude);
                ApplyScale(1f + wave * 0.010f * amplitude);
                break;

            case Mode.Pulse:
                if (graphic != null)
                {
                    Color color = graphic.color;
                    color.a = baseAlpha * Mathf.Lerp(0.55f, 1f, (wave + 1f) * 0.5f);
                    graphic.color = color;
                }
                break;

            case Mode.Shine:
                ApplyShine();
                break;
        }
    }

    void ApplyScale(float scale)
    {
        // The press feedback owns the same property. Multiplying rather than
        // overwriting lets a button be pressed while it breathes.
        if (press != null) scale *= press.Press;
        transform.localScale = Vector3.one * scale;
    }

    // Mostly dark, with a short bright tick: a coin catching the light as it
    // turns, not a blinking indicator.
    void ApplyShine()
    {
        if (graphic == null) return;

        float cycle = (Time.unscaledTime / period + phase) % 1f;
        const float activeWindow = 0.12f; // ~0.50s at 4.2s period
        float sweep = cycle <= activeWindow
            ? Mathf.Clamp01(cycle / activeWindow)
            : 0f;
        float eased = Mathf.SmoothStep(0f, 1f, sweep);
        float glint = sweep > 0f ? Mathf.Sin(sweep * Mathf.PI) : 0f;

        Color color = graphic.color;
        color.a = baseAlpha * Mathf.SmoothStep(0f, 1f, glint);
        graphic.color = color;

        if (rect != null)
            rect.anchoredPosition = basePosition + new Vector2(Mathf.Lerp(-48f, 48f, eased), 0f);

        transform.localScale = Vector3.one;
    }
}
