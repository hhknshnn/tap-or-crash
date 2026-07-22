using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class ThemePopupAnimator : MonoBehaviour
{
    [Header("Giriş Animasyonu")]
    public RectTransform card;       // Pop-in yapacak kart (genelde bu objenin kendisi)
    public CanvasGroup canvasGroup;  // Fade-in için (Card üzerinde)

    [Header("İkonlar (kod ile üretilir)")]
    public Image lightIcon;          // LIGHT butonundaki güneş ikonu
    public Image darkIcon;           // DARK butonundaki ay ikonu

    [Header("Hareketli Oklar")]
    public RectTransform lightArrow; // LIGHT'ı işaret eden ok
    public RectTransform darkArrow;  // DARK'ı işaret eden ok

    [Header("Butonlar (nefes efekti)")]
    public RectTransform lightButton;
    public RectTransform darkButton;

    // Okların başlangıç pozisyonları (sallanma bunun etrafında olur)
    private Vector2 lightArrowBasePos;
    private Vector2 darkArrowBasePos;
    private int selectedTheme = -1;
    private float t; // Sürekli animasyonlar için zaman sayacı

    void Awake()
    {
        // İkonları ve okları kod ile üret — harici PNG gerekmez

        // Güneş ikonu
        if (lightIcon != null)
        {
            lightIcon.sprite = CreateSunSprite();
            lightIcon.color = Color.white; // Sprite kendi rengini taşıyor
            lightIcon.raycastTarget = false;
        }

        // Ay ikonu
        if (darkIcon != null)
        {
            darkIcon.sprite = CreateMoonSprite();
            darkIcon.color = Color.white;
            darkIcon.raycastTarget = false;
        }

        // Oklar — aynı üçgen sprite, neon mavi
        Sprite arrow = CreateArrowSprite();
        AssignArrow(lightArrow, arrow);
        AssignArrow(darkArrow, arrow);
    }

    // Ok objesinin Image'ine sprite'ı atar
    void AssignArrow(RectTransform arrow, Sprite sprite)
    {
        if (arrow == null) return;
        Image img = arrow.GetComponent<Image>();
        if (img != null)
        {
            img.sprite = sprite;
            img.color = new Color(0.3f, 0.65f, 1f, 1f); // Neon mavi
            img.raycastTarget = false;
        }
    }

    void OnEnable()
    {
        // Panel her açıldığında giriş animasyonunu baştan oynat

        // Okların referans pozisyonlarını al
        RefreshLayoutBases();
        selectedTheme = -1;

        StopAllCoroutines();
        StartCoroutine(EntranceAnim());
    }

    // Kart büyüyerek + fade ile açılır (pop-in)
    IEnumerator EntranceAnim()
    {
        float dur = 0.35f;
        float e = 0f;

        if (canvasGroup != null) canvasGroup.alpha = 0f;

        while (e < dur)
        {
            // unscaledDeltaTime — oyun durmuş (timeScale 0) olsa bile çalışır
            e += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(e / dur);

            // Overshoot easing — 1.0'ı hafif aşıp geri oturur (zıplama hissi)
            float scale = OvershootEase(p);
            if (card != null) card.localScale = Vector3.one * scale;

            if (canvasGroup != null) canvasGroup.alpha = p;
            yield return null;
        }

        // Bitişte tam değerlere sabitle
        if (card != null) card.localScale = Vector3.one;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    // 0->1 arası, sonunda hafif overshoot yapan easing fonksiyonu
    float OvershootEase(float x)
    {
        float s = 1.70158f;
        x -= 1f;
        return (x * x * ((s + 1f) * x + s) + 1f);
    }

    void Update()
    {
        t += Time.unscaledDeltaTime;

        // Oklar yatayda nazikçe sallanıyor (butonu işaret eder gibi)
        float bounce = Mathf.Sin(t * 4f) * 8f;
        if (lightArrow != null)
            lightArrow.anchoredPosition = lightArrowBasePos + new Vector2(bounce, 0f);
        if (darkArrow != null)
            darkArrow.anchoredPosition = darkArrowBasePos + new Vector2(bounce, 0f);

        // Butonlar çok hafif nefes alıyor (scale pulse) — ekran canlı dursun
        if (selectedTheme < 0)
        {
            float pulse = 1f + Mathf.Sin(t * 2.5f) * 0.02f;
            if (lightButton != null) lightButton.localScale = Vector3.one * pulse;
            if (darkButton != null) darkButton.localScale = Vector3.one * pulse;
        }
        else
        {
            if (lightButton != null)
                lightButton.localScale = Vector3.one * (selectedTheme == 0 ? 1.045f : 0.97f);
            if (darkButton != null)
                darkButton.localScale = Vector3.one * (selectedTheme == 1 ? 1.045f : 0.97f);
        }
    }

    public void SetSelection(bool light)
    {
        selectedTheme = light ? 0 : 1;
    }

    public void RefreshLayoutBases()
    {
        if (lightArrow != null) lightArrowBasePos = lightArrow.anchoredPosition;
        if (darkArrow != null) darkArrowBasePos = darkArrow.anchoredPosition;
    }

    // ---------- İKON/OK ÜRETİCİLERİ ----------

    // Güneş: ortada çekirdek + çevrede ışınlar
    Sprite CreateSunSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size);
        tex.filterMode = FilterMode.Bilinear;

        Vector2 c = new Vector2(size / 2f, size / 2f);
        float coreR = size * 0.24f;   // Çekirdek yarıçapı
        float rayInner = size * 0.30f; // Işınların başladığı yarıçap
        float rayOuter = size * 0.46f; // Işınların bittiği yarıçap
        Color sun = new Color(1f, 0.74f, 0.25f, 1f); // Sıcak sarı-turuncu
        int rays = 8;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                Vector2 d = new Vector2(x, y) - c;
                float dist = d.magnitude;
                float a = Mathf.Atan2(d.y, d.x);
                if (a < 0) a += Mathf.PI * 2f;

                bool on = false;
                if (dist <= coreR)
                {
                    on = true; // Çekirdek
                }
                else if (dist >= rayInner && dist <= rayOuter)
                {
                    // Açıya göre ışın dilimleri
                    float seg = (a / (Mathf.PI * 2f)) * rays;
                    float frac = seg - Mathf.Floor(seg);
                    if (frac < 0.42f) on = true; // Işın kalınlığı
                }

                tex.SetPixel(x, y, on ? sun : Color.clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // Ay: bir diskten ikinci diski çıkararak hilal yap
    Sprite CreateMoonSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size);
        tex.filterMode = FilterMode.Bilinear;

        Vector2 c1 = new Vector2(size * 0.44f, size * 0.5f);  // Ana disk
        float r1 = size * 0.34f;
        Vector2 c2 = new Vector2(size * 0.62f, size * 0.56f); // Kesen disk (gölge)
        float r2 = size * 0.34f;
        Color moon = new Color(0.78f, 0.87f, 1f, 1f); // Soluk mavi-beyaz

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                Vector2 p = new Vector2(x, y);
                bool inMain = Vector2.Distance(p, c1) <= r1;
                bool inCut = Vector2.Distance(p, c2) <= r2;
                bool on = inMain && !inCut; // Hilal = ana disk EKSİ kesen disk

                tex.SetPixel(x, y, on ? moon : Color.clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // Sağa bakan üçgen ok
    Sprite CreateArrowSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size);
        tex.filterMode = FilterMode.Bilinear;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float fx = x / (float)size;          // 0 (sol) -> 1 (sağ)
                float halfH = (1f - fx) * 0.5f;       // Solda geniş, sağda sivri
                float cy = 0.5f;
                bool on = fx >= 0.15f && Mathf.Abs(y / (float)size - cy) <= halfH;

                tex.SetPixel(x, y, on ? Color.white : Color.clear);
            }
        }

        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
