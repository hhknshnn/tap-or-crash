using UnityEngine;
using UnityEngine.UI;
using System.Collections;

// Başarılı near-miss anında ekrana turuncu flash + kısa slow-motion uygular.
// Game Over sırasındaki slow-motion ile çakışmaz (isGameOver kontrolü var).
// Sahneye boş bir GameObject ekleyip bu scripti bağla — gerisini kod halleder.
public class NearMissEffect : MonoBehaviour
{
    public static NearMissEffect instance;

    [SerializeField] private float slowTimeScale    = 0.88f;
    [SerializeField] private float slowDuration     = 0.025f;
    [SerializeField] private float flashMaxAlpha    = 0.10f;

    private Image     overlay;
    private Coroutine activeEffect;

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }

        // Keep legacy scene values from reintroducing an aggressive slowdown.
        slowTimeScale = Mathf.Max(slowTimeScale, 0.88f);
        slowDuration  = Mathf.Min(slowDuration, 0.025f);
        flashMaxAlpha = Mathf.Min(flashMaxAlpha, 0.10f);

        CreateOverlay();
    }

    // Canvas'ta fullscreen turuncu bir overlay Image oluşturur
    void CreateOverlay()
    {
        Canvas canvas = UIRootCanvas.Resolve();
        if (canvas == null) return;

        GameObject go = new GameObject("NearMissOverlay");
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsFirstSibling(); // Diğer UI'ın arkasında kalır

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        overlay               = go.AddComponent<Image>();
        overlay.color         = new Color(1f, 0.45f, 0f, 0f); // Turuncu, başlangıçta şeffaf
        overlay.raycastTarget = false; // Dokunuşları engelleme
    }

    // RocketController tarafından başarılı near-miss'te çağrılır
    public void Trigger()
    {
        if (GameManager.isGameOver) return; // Death sequence kendi slow-mo'sunu yönetir

        if (activeEffect != null) StopCoroutine(activeEffect);
        activeEffect = StartCoroutine(PlayEffect());
    }

    IEnumerator PlayEffect()
    {
        Time.timeScale = slowTimeScale;

        // Flash in
        float t = 0f;
        while (t < 0.035f)
        {
            t += Time.unscaledDeltaTime;
            SetOverlayAlpha(Mathf.Lerp(0f, flashMaxAlpha, t / 0.035f));
            yield return null;
        }

        yield return new WaitForSecondsRealtime(slowDuration);

        // Zaman ölçeğini yalnızca oyun hâlâ aktifse geri al
        if (!GameManager.isGameOver) Time.timeScale = 1f;

        // Flash out
        t = 0f;
        while (t < 0.10f)
        {
            t += Time.unscaledDeltaTime;
            SetOverlayAlpha(Mathf.Lerp(flashMaxAlpha, 0f, t / 0.10f));
            yield return null;
        }
        SetOverlayAlpha(0f);
        activeEffect = null;
    }

    void SetOverlayAlpha(float a)
    {
        if (overlay == null) return;
        Color c = overlay.color;
        c.a         = a;
        overlay.color = c;
    }
}
