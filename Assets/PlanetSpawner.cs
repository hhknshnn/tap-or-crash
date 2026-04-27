using UnityEngine;

public class PlanetSpawner : MonoBehaviour
{
    public GameObject[] planetPrefabs;     // Farklı gezegen tipleri
    public RocketController rocket;
    public float spawnDistance = 12f;      // Gezegenler arası yatay mesafe
    public float verticalRange = 3f;       // Dikey rastgelelik aralığı
    public float minSize = 1.5f;           // Minimum görsel boyut (Unity unit)
    public float maxSize = 2.5f;           // Maximum görsel boyut (Unity unit)
    public float targetPixelSize = 512f;   // Artık kullanılmıyor, silebilirsin
    private int lastPlanetIndex = -1; // Son seçilen gezegen indexi (-1 = henüz seçilmedi)
    void Start()
    {
        // Başlangıçta iki gezegen oluştur
        SpawnPlanet();
        SpawnPlanet();
    }

    public void SpawnPlanet()
    {
        // Son gezegenden itibaren yeni pozisyon hesapla
        Vector3 lastPos = rocket.planets.Count > 0
            ? rocket.planets[rocket.planets.Count - 1].position
            : Vector3.zero;

        float x = Random.Range(-verticalRange, verticalRange);  // Rastgele yatay konum
        Vector3 pos = lastPos + new Vector3(x, spawnDistance, 0); // Yukarıya doğru spawn

        // Rastgele gezegen tipi seç
        // Aynı gezegen arka arkaya gelmesin diye farklı bir index seç
        int newIndex;
        do {
            newIndex = Random.Range(0, planetPrefabs.Length); // Rastgele index seç
        } while (newIndex == lastPlanetIndex && planetPrefabs.Length > 1); // Aynıysa tekrar seç
        lastPlanetIndex = newIndex;                          // Seçilen indexi kaydet
        GameObject selectedPrefab = planetPrefabs[newIndex]; // Prefab'ı al
        GameObject p = Instantiate(selectedPrefab, pos, Quaternion.identity);

        // SpriteRenderer'ı al
        SpriteRenderer sr = p.GetComponent<SpriteRenderer>();

        // Skora göre hedef boyutu küçült
        int score = GameManager.instance.GetScore();
        float minS = Mathf.Max(0.8f, minSize - score * 0.005f);  // En az 0.8 unit
        float maxS = Mathf.Max(1.2f, maxSize - score * 0.005f);  // En az 1.2 unit
        float targetSize = Random.Range(minS, maxS);              // Hedef Unity unit boyutu

        // Sprite'ın bounds boyutunu al — gerçek ekran boyutu, PPU'dan bağımsız
        float naturalSize = sr != null ? Mathf.Max(sr.bounds.size.x, sr.bounds.size.y) : 1f;

        // Hedef boyuta ulaşmak için gereken scale
        float finalScale = naturalSize > 0 ? targetSize / naturalSize : 1f;

        p.transform.localScale = Vector3.one * finalScale;  // Scale uygula
        p.tag = "Planet";

        rocket.AddPlanet(p.transform);  // Rocket'e gezegeni tanıt
    }
}