using UnityEngine;
using System.Collections.Generic;

public class ParallaxBackground : MonoBehaviour
{
    private static Sprite sharedCircleSprite;

    [System.Serializable]
    public class ParallaxLayer
    {
        public Transform layerTransform;      // Katman objesi
        public float scrollSpeed;             // Kameranın hızına oranla ne kadar kayar
        public int starCount;                 // Kaç yıldız üretilsin
        public float minStarSize;             // Minimum yıldız boyutu
        public float maxStarSize;             // Maximum yıldız boyutu
        public Color starColor;               // Yıldız rengi
        public bool hasNebula;                // Nebula lekeleri üretilsin mi
    }

    [Header("Katmanlar")]
    public ParallaxLayer[] layers;

    [Header("Referanslar")]
    public Camera mainCamera;

    // Her yıldız için pozisyon ve boyut bilgisi tutulur
    private List<List<GameObject>> layerStars = new List<List<GameObject>>();

    // Katmanın kaç birimlik yüksekliği kapsayacağı
    private float tileHeight = 30f;

    void Start()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null || layers == null) return;

        // Her katman için yıldızları üret
        for (int i = 0; i < layers.Length; i++)
        {
            layerStars.Add(new List<GameObject>());
            GenerateStars(i);
        }
    }

    void LateUpdate()
    {
        if (mainCamera == null || layers == null) return;

        float camY = mainCamera.transform.position.y;

        int layerCount = Mathf.Min(layers.Length, layerStars.Count);
        for (int i = 0; i < layerCount; i++)
        {
            // Katmanı kameraya göre yavaşlatılmış şekilde kaydır
            float newY = camY * layers[i].scrollSpeed;
            layers[i].layerTransform.position = new Vector3(0, newY, 10 - i);

            // Yıldızları döngüsel olarak yukarı/aşağı taşı (sonsuz efekti)
            RecycleStars(i, camY);
        }
    }

    void GenerateStars(int layerIndex)
    {
        ParallaxLayer layer = layers[layerIndex];
        float camHeight = mainCamera.orthographicSize * 2f;
        float camWidth = camHeight * mainCamera.aspect;

        for (int i = 0; i < layer.starCount; i++)
        {
            // Rastgele pozisyon — kameranın görüş alanının 2 katı yükseklikte dağıt
            float x = Random.Range(-camWidth / 2f, camWidth / 2f);
            float y = Random.Range(-tileHeight / 2f, tileHeight / 2f);

            CreateStar(layerIndex, new Vector3(x, y, 0), layer);

            // Nebula lekeleri ekle (sadece orta katmanda)
            if (layer.hasNebula && Random.value > 0.85f)
                CreateNebula(layerIndex, new Vector3(x, y, 0.1f));
        }
    }

    void CreateStar(int layerIndex, Vector3 localPos, ParallaxLayer layer)
    {
        GameObject star = new GameObject("Star");
        star.transform.SetParent(layers[layerIndex].layerTransform, false);
        star.transform.localPosition = localPos;

        SpriteRenderer sr = star.AddComponent<SpriteRenderer>();
        sr.sprite = CreateCircleSprite();
        sr.sortingOrder = layerIndex;

        // Yıldız boyutu ve rengi
        float size = Random.Range(layer.minStarSize, layer.maxStarSize);
        star.transform.localScale = Vector3.one * size;

        // Hafif parlaklık varyasyonu
        float brightness = Random.Range(0.6f, 1f);
        sr.color = new Color(
            layer.starColor.r * brightness,
            layer.starColor.g * brightness,
            layer.starColor.b * brightness,
            Random.Range(0.7f, 1f)
        );

        layerStars[layerIndex].Add(star);
    }

    void CreateNebula(int layerIndex, Vector3 localPos)
    {
        GameObject nebula = new GameObject("Nebula");
        nebula.transform.SetParent(layers[layerIndex].layerTransform, false);
        nebula.transform.localPosition = localPos;

        SpriteRenderer sr = nebula.AddComponent<SpriteRenderer>();
        sr.sprite = CreateCircleSprite();
        sr.sortingOrder = layerIndex - 1; // Yıldızların arkasında

        // Nebula büyük ve çok şeffaf
        float size = Random.Range(2f, 5f);
        nebula.transform.localScale = Vector3.one * size;

        // Mor veya mavi tonu rastgele seç
        Color[] nebulaColors = new Color[]
        {
            new Color(0.4f, 0.1f, 0.6f, 0.06f), // Mor
            new Color(0.1f, 0.2f, 0.6f, 0.06f), // Lacivert
            new Color(0.6f, 0.1f, 0.4f, 0.05f), // Pembe-mor
        };
        sr.color = nebulaColors[Random.Range(0, nebulaColors.Length)];

        layerStars[layerIndex].Add(nebula);
    }

    void RecycleStars(int layerIndex, float camY)
    {
        if (layerIndex < 0 || layerIndex >= layerStars.Count) return;

        // Kameranın altında veya üstünde kalan yıldızları karşı tarafa taşı
        float camHalfHeight = mainCamera.orthographicSize;

        foreach (GameObject star in layerStars[layerIndex])
        {
            if (star == null) continue;

            Vector3 worldPos = star.transform.position;

            if (worldPos.y < camY - camHalfHeight - 2f)
            {
                // Aşağıda kaldı, yukarıya taşı
                star.transform.position += new Vector3(0, tileHeight, 0);
            }
            else if (worldPos.y > camY + camHalfHeight + tileHeight)
            {
                // Çok yukarıda kaldı, aşağıya taşı
                star.transform.position -= new Vector3(0, tileHeight, 0);
            }
        }
    }

    // Kod ile daire sprite üretir — texture dosyasına gerek yok
    Sprite CreateCircleSprite()
    {
        if (sharedCircleSprite != null) return sharedCircleSprite;

        int size = 64;
        Texture2D tex = new Texture2D(size, size);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                // Kenarları yumuşat (soft circle)
                float alpha = Mathf.Clamp01(1f - (dist / radius));
                alpha = Mathf.Pow(alpha, 1.5f); // Merkez parlak, kenar solar
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }

        tex.Apply();
        sharedCircleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return sharedCircleSprite;
    }
}
