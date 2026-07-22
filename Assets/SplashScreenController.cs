using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class SplashScreenController : MonoBehaviour
{
    [Header("UI Elemanları")]
    public TextMeshProUGUI tapToStartText;
    public TextMeshProUGUI logoText;
    public Image fadeOverlay;
    public RectTransform particleParent;
    public TextMeshProUGUI bestScoreText; 


    [Header("Ayarlar")]
    public float fadeDuration = 0.6f;

    [Header("Yıldız Ayarları")]
    public int starCount = 80;

    [Header("Asteroid Ayarları")]
    public int asteroidCount = 18;

    private bool isTransitioning = false;
    private float pulseTimer = 0f;
    private float floatTimer = 0f;
    private Vector3 logoStartPos;

    // Yıldız verileri
    private List<RectTransform> starRects = new List<RectTransform>();
    private List<Image> starImages = new List<Image>();
    private List<float> starSpeeds = new List<float>();
    private List<float> starOffsets = new List<float>();
    private List<float> starLayers = new List<float>(); // Parallax katman hızı
    private List<Vector2> starBasePositions = new List<Vector2>();

    private static Sprite sharedSoftCircle;
    private static readonly List<Sprite> sharedAsteroids = new List<Sprite>();

    // Asteroid verileri
    private List<RectTransform> asteroids = new List<RectTransform>();
    private List<Vector2> asteroidDirections = new List<Vector2>();
    private List<float> asteroidSpeeds = new List<float>();
    private List<float> asteroidRotSpeeds = new List<float>(); // Dönüş hızı

    // Sahte parallax için zaman bazlı salınım
    private float parallaxTimer = 0f;

    void Start()
    {
        starCount = Mathf.Min(starCount, 55);
        asteroidCount = Mathf.Min(asteroidCount, 8);

        if (fadeOverlay != null)
        {
            Color c = fadeOverlay.color;
            c.a = 0f;
            fadeOverlay.color = c;
        }

        if (logoText != null)
            logoStartPos = logoText.transform.localPosition;

        if (particleParent != null)
        {
            SpawnStars();
            SpawnNebulas(); // Nebula lekelerini oluştur
            SpawnAsteroids();
        }
        // YENİ — Best score göster
        int highScore = PlayerPrefs.GetInt("HighScore", 0);
        if (bestScoreText != null)
        {
            if (highScore > 0)
            {
                bestScoreText.text = "Best: " + highScore;
                bestScoreText.gameObject.SetActive(true);
            }
            else
            {
                bestScoreText.gameObject.SetActive(false);
            }
        }
        }

    void Update()
    {
        if (isTransitioning) return;
        PulseTapToStart();
        FloatLogo();
        if (particleParent != null)
        {
            parallaxTimer += Time.deltaTime;
            TwinkleAndParallaxStars();
            MoveAsteroids();
        }
    }

    void SpawnStars()
    {
        Rect rect = particleParent.rect;

        for (int i = 0; i < starCount; i++)
        {
            GameObject go = new GameObject("Star_" + i);
            go.transform.SetParent(particleParent, false);

            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;

            // Yumuşak daire sprite oluştur — ParallaxBackground ile aynı yöntem
            img.sprite = CreateSoftCircleSprite();

            // 3 katman — küçük/orta/büyük
            float layerRoll = Random.value;
            float size, speed, brightness;

            if (layerRoll < 0.5f)
            {
                // Arka katman — küçük, soluk, yavaş parallax
                size = Random.Range(1.5f, 3f);
                speed = Random.Range(0.3f, 0.8f);
                brightness = Random.Range(0.3f, 0.6f);
                starLayers.Add(0.05f); // Çok yavaş salınım
            }
            else if (layerRoll < 0.85f)
            {
                // Orta katman
                size = Random.Range(3f, 5f);
                speed = Random.Range(0.8f, 1.8f);
                brightness = Random.Range(0.5f, 0.8f);
                starLayers.Add(0.12f);
            }
            else
            {
                // Ön katman — büyük, parlak, hızlı parallax
                size = Random.Range(5f, 8f);
                speed = Random.Range(1.8f, 3f);
                brightness = Random.Range(0.8f, 1f);
                starLayers.Add(0.25f);
            }

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(
                Random.Range(rect.xMin, rect.xMax),
                Random.Range(rect.yMin, rect.yMax)
            );
            starBasePositions.Add(rt.anchoredPosition);

            // Hafif renk tonu — beyaz, mavi-beyaz veya sarı-beyaz
            float colorRoll = Random.value;
            Color starColor;
            if (colorRoll < 0.6f)
                starColor = Color.white;
            else if (colorRoll < 0.8f)
                starColor = new Color(0.8f, 0.9f, 1f); // Mavi-beyaz
            else
                starColor = new Color(1f, 0.95f, 0.8f); // Sarı-beyaz

            starColor.a = brightness;
            img.color = starColor;

            starRects.Add(rt);
            starImages.Add(img);
            starSpeeds.Add(speed);
            starOffsets.Add(Random.Range(0f, Mathf.PI * 2f));
        }
    }

    void TwinkleAndParallaxStars()
    {
        float time = Time.time;

        for (int i = 0; i < starImages.Count; i++)
        {
            if (starImages[i] == null) continue;

            // Twinkle — alpha salınımı
            float alpha = Mathf.Lerp(0.1f, 1.0f, (Mathf.Sin(time * starSpeeds[i] + starOffsets[i]) + 1f) / 2f);
            Color c = starImages[i].color;
            c.a = alpha;
            starImages[i].color = c;

            // Sahte parallax — yıldızlar yavaşça yatay sallanıyor
            Vector2 pos = starBasePositions[i];
            pos.x += Mathf.Sin(parallaxTimer * starLayers[i] + starOffsets[i]) * 8f;
            starRects[i].anchoredPosition = pos;
        }
    }

    void SpawnNebulas()
    {
        Rect rect = particleParent.rect;

        // Büyük, şeffaf nebula lekeleri — arka planı hareketlendirir
        Color[] nebulaColors = new Color[]
        {
            new Color(0.4f, 0.1f, 0.7f, 0.07f), // Mor
            new Color(0.1f, 0.2f, 0.7f, 0.07f), // Lacivert
            new Color(0.6f, 0.1f, 0.5f, 0.06f), // Pembe-mor
            new Color(0.1f, 0.4f, 0.6f, 0.06f), // Mavi
        };

        for (int i = 0; i < 6; i++)
        {
            GameObject go = new GameObject("Nebula_" + i);
            go.transform.SetParent(particleParent, false);

            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.sprite = CreateSoftCircleSprite();
            img.color = nebulaColors[Random.Range(0, nebulaColors.Length)];

            RectTransform rt = go.GetComponent<RectTransform>();
            float size = Random.Range(80f, 160f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(
                Random.Range(rect.xMin, rect.xMax),
                Random.Range(rect.yMin, rect.yMax)
            );
            starBasePositions.Add(rt.anchoredPosition);

            // Nebula da yavaşça sallanacak — yıldız listesine ekle
            starRects.Add(rt);
            starImages.Add(img);
            starSpeeds.Add(Random.Range(0.1f, 0.3f));
            starOffsets.Add(Random.Range(0f, Mathf.PI * 2f));
            starLayers.Add(0.03f);
        }
    }
    void SpawnAsteroids()
    {
        Rect rect = particleParent.rect;

        for (int i = 0; i < asteroidCount; i++)
        {
            GameObject go = new GameObject("Asteroid_" + i);
            go.transform.SetParent(particleParent, false);

            Image img = go.AddComponent<Image>();
            img.raycastTarget = false;

            // Gerçekçi asteroid sprite oluştur
            img.sprite = CreateAsteroidSprite();
            // Asteroidleri daha belirgin yap — hafif sarımsı ton
            float brightness = Random.Range(0.85f, 1f);
            img.color = new Color(brightness, brightness * 0.92f, brightness * 0.8f, 1f);

            RectTransform rt = go.GetComponent<RectTransform>();
            float size = Random.Range(20f, 45f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(
                Random.Range(rect.xMin, rect.xMax),
                rect.yMax + Random.Range(50f, 400f)
            );

            // Hafif açıyla düşsün
            float angle = Random.Range(-20f, 20f);
            Vector2 dir = new Vector2(
                Mathf.Sin(angle * Mathf.Deg2Rad),
                -Mathf.Cos(angle * Mathf.Deg2Rad)
            );

            asteroids.Add(rt);
            asteroidDirections.Add(dir);
            asteroidSpeeds.Add(Random.Range(60f, 150f));
            asteroidRotSpeeds.Add(Random.Range(-90f, 90f)); // Dönüş hızı
        }
    }

    void MoveAsteroids()
    {
        Rect rect = particleParent.rect;

        for (int i = 0; i < asteroids.Count; i++)
        {
            if (asteroids[i] == null) continue;

            // İlerle
            asteroids[i].anchoredPosition += asteroidDirections[i] * asteroidSpeeds[i] * Time.deltaTime;

            // Döndür
            asteroids[i].Rotate(0, 0, asteroidRotSpeeds[i] * Time.deltaTime);

            // Ekrandan çıkınca yukarıdan tekrar gir
            Vector2 pos = asteroids[i].anchoredPosition;
            if (pos.y < rect.yMin - 50f)
            {
                asteroids[i].anchoredPosition = new Vector2(
                    Random.Range(rect.xMin, rect.xMax),
                    rect.yMax + Random.Range(20f, 150f)
                );
                asteroids[i].rotation = Quaternion.identity;
            }
        }
    }

    // Yumuşak daire sprite üretir — yıldızlar için
    Sprite CreateSoftCircleSprite()
    {
        if (sharedSoftCircle != null) return sharedSoftCircle;

        int size = 32;
        Texture2D tex = new Texture2D(size, size);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(1f - (dist / radius));
                alpha = Mathf.Pow(alpha, 1.5f); // Merkez parlak, kenar solar
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }

        tex.Apply();
        sharedSoftCircle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return sharedSoftCircle;
    }

    // Gerçekçi asteroid sprite üretir — AsteroidMover ile aynı yöntem
    Sprite CreateAsteroidSprite()
    {
        if (sharedAsteroids.Count >= 4)
            return sharedAsteroids[Random.Range(0, sharedAsteroids.Count)];

        int size = 64;
        Texture2D tex = new Texture2D(size, size);
        tex.filterMode = FilterMode.Bilinear;
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float baseRadius = size / 2f - 4f;

        // Rastgele çıkıntılar
        int points = 10;
        float[] radii = new float[points];
        for (int i = 0; i < points; i++)
            radii[i] = baseRadius * Random.Range(0.6f, 1f);

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                Vector2 pos = new Vector2(x, y);
                Vector2 dir = pos - center;
                float dist = dir.magnitude;

                float angle = Mathf.Atan2(dir.y, dir.x);
                if (angle < 0) angle += Mathf.PI * 2f;

                float t = angle / (Mathf.PI * 2f) * points;
                int i0 = (int)t % points;
                int i1 = (i0 + 1) % points;
                float blend = t - (int)t;
                float r = Mathf.Lerp(radii[i0], radii[i1], blend);

                if (dist < r)
                {
                    float edgeFactor = 1f - (dist / r);
                    Color dark = new Color(0.3f, 0.22f, 0.13f);
                    Color light = new Color(0.55f, 0.42f, 0.28f);
                    tex.SetPixel(x, y, Color.Lerp(light, dark, edgeFactor));
                }
                else
                {
                    tex.SetPixel(x, y, Color.clear);
                }
            }
        }

        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        sharedAsteroids.Add(sprite);
        return sprite;
    }

    void PulseTapToStart()
    {
        if (tapToStartText == null) return;
        pulseTimer += Time.deltaTime;
        float alpha = Mathf.Lerp(0.35f, 1.0f, (Mathf.Sin(pulseTimer * 2.5f) + 1f) / 2f);
        SetAlpha(tapToStartText, alpha);
    }

    void FloatLogo()
    {
        if (logoText == null) return;
        floatTimer += Time.deltaTime;
        float offsetY = Mathf.Sin(floatTimer * 1.2f) * 8f;
        logoText.transform.localPosition = logoStartPos + new Vector3(0, offsetY, 0);
    }

    public void StartTransition()
    {
        if (isTransitioning) return;
        StartCoroutine(FadeOutAndStart());
    }

    IEnumerator FadeOutAndStart()
    {
        isTransitioning = true;
        if (fadeOverlay != null)
        {
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                float alpha = Mathf.Clamp01(elapsed / fadeDuration);
                Color c = fadeOverlay.color;
                c.a = alpha;
                fadeOverlay.color = c;
                yield return null;
            }
        }
        TutorialManager.instance.OnTapToStart(); // ✅ Fade sonrası tutorial aç
        gameObject.SetActive(false);
    }

    void SetAlpha(TextMeshProUGUI tmp, float alpha)
    {
        if (tmp == null) return;
        Color c = tmp.color;
        c.a = alpha;
        tmp.color = c;
    }
}
