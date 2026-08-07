using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Subtle idle pulse + press squash for the Shop Watch Ad button only.
[DisallowMultipleComponent]
public sealed class WatchAdButtonPulse : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    const float IdleScale = 1f;
    const float PulseScale = 1.04f;
    const float PressScale = 0.94f;
    const float HalfDuration = 0.65f;

    Coroutine pulseRoutine;
    float pulseScale = IdleScale;
    float pressTarget = 1f;
    float pressScale = 1f;
    bool pulsing;
    bool pressFeedbackEnabled = true;
    Button button;

    public void SetPulsing(bool enabled)
    {
        if (pulsing == enabled) return;
        pulsing = enabled;
        if (!isActiveAndEnabled) return;
        RestartPulse();
    }

    public void SetPressFeedbackEnabled(bool enabled)
    {
        if (pressFeedbackEnabled == enabled) return;
        pressFeedbackEnabled = enabled;
        if (!pressFeedbackEnabled)
        {
            pressTarget = 1f;
            pressScale = 1f;
        }
        ApplyVisualScale();
    }

    void Awake() => button = GetComponent<Button>();

    void OnEnable()
    {
        pressScale = pressTarget = 1f;
        RestartPulse();
    }

    void OnDisable()
    {
        StopPulse();
        pulseScale = IdleScale;
        pressScale = pressTarget = 1f;
        transform.localScale = Vector3.one;
    }

    void LateUpdate() => ApplyVisualScale();

    void ApplyVisualScale()
    {
        if (pressFeedbackEnabled)
            pressScale = Mathf.Lerp(pressScale, pressTarget, 22f * Time.unscaledDeltaTime);
        else
            pressScale = 1f;

        transform.localScale = Vector3.one * pulseScale * pressScale;
    }

    void RestartPulse()
    {
        StopPulse();
        if (!pulsing)
        {
            pulseScale = IdleScale;
            ApplyVisualScale();
            return;
        }

        pulseRoutine = StartCoroutine(PulseLoop());
    }

    void StopPulse()
    {
        if (pulseRoutine == null) return;
        StopCoroutine(pulseRoutine);
        pulseRoutine = null;
    }

    IEnumerator PulseLoop()
    {
        while (pulsing)
        {
            yield return AnimatePulse(IdleScale, PulseScale);
            if (!pulsing) yield break;
            yield return AnimatePulse(PulseScale, IdleScale);
        }
    }

    IEnumerator AnimatePulse(float from, float to)
    {
        float elapsed = 0f;
        while (elapsed < HalfDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / HalfDuration);
            float eased = t * t * (3f - 2f * t);
            pulseScale = Mathf.Lerp(from, to, eased);
            ApplyVisualScale();
            yield return null;
        }

        pulseScale = to;
        ApplyVisualScale();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!CanPress()) return;
        pressTarget = PressScale;
        ApplyVisualScale();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressTarget = 1f;
        ApplyVisualScale();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pressTarget = 1f;
        ApplyVisualScale();
    }

    bool CanPress()
    {
        if (!pressFeedbackEnabled) return false;
        return button == null || button.interactable;
    }
}
