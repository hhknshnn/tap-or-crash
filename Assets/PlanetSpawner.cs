using UnityEngine;

public class PlanetSpawner : MonoBehaviour
{
    public GameObject[] planetPrefabs;
    public RocketController rocket;

    [Header("Spawn Ayarları")]
    public float spawnDistance = 4f;

    // Hareketli gezegen şansı (0–1 arası). Skor 15'i geçince devreye girer.
    [SerializeField] private float movingPlanetChance = 0.25f;

    private int    lastPlanetIndex = -1;
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
        SpawnPlanet();
        SpawnPlanet();
    }

    public void SpawnPlanet()
    {
        if (rocket == null || planetPrefabs == null || planetPrefabs.Length == 0) return;

        Vector3 lastPos = rocket.planets.Count > 0
            ? rocket.planets[rocket.planets.Count - 1].position
            : Vector3.zero;

        // ── Yatay güvenli alan ──────────────────────────────────────────────
        float camHalfWidth = mainCamera.orthographicSize * mainCamera.aspect;
        float margin       = camHalfWidth * 0.4f;
        float safeWidth    = camHalfWidth - margin;
        float camCenterX   = mainCamera.transform.position.x;

        float x = camCenterX + Random.Range(-safeWidth * 0.5f, safeWidth * 0.5f);
        // B2 FIX: kameranın merkezi baz alınarak sınırlanır
        x = Mathf.Clamp(x, camCenterX - safeWidth, camCenterX + safeWidth);

        Vector3 pos = new Vector3(x, lastPos.y + spawnDistance, 0);

        // ── Aynı gezegen üst üste gelmesin ─────────────────────────────────
        int newIndex;
        do { newIndex = Random.Range(0, planetPrefabs.Length); }
        while (newIndex == lastPlanetIndex && planetPrefabs.Length > 1);
        lastPlanetIndex = newIndex;

        GameObject p = Instantiate(planetPrefabs[newIndex], pos, Quaternion.identity);
        p.tag = "Planet";

        // ── Boyut ayarı (puana göre) ────────────────────────────────────────
        SpriteRenderer sr   = p.GetComponent<SpriteRenderer>();
        int score           = GameManager.instance != null ? GameManager.instance.GetScore() : 0;

        float difficulty = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 75f, score));
        float targetSize = Mathf.Lerp(0.70f, 0.28f, difficulty);

        float naturalSize = sr != null ? Mathf.Max(sr.bounds.size.x, sr.bounds.size.y) : 1f;
        float finalScale  = naturalSize > 0 ? targetSize / naturalSize : 1f;
        p.transform.localScale = Vector3.one * finalScale;

        // ── Y4: Hareketli gezegen ───────────────────────────────────────────
        // Skor 15'i geçtikten sonra %25 ihtimalle yatay salınım ekle
        if (score >= 15)
        {
            float movingDifficulty = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(15f, 60f, score));
            float chance = Mathf.Lerp(movingPlanetChance * 0.45f, movingPlanetChance, movingDifficulty);
            if (Random.value < chance)
            {
                MovingPlanet movingPlanet = p.AddComponent<MovingPlanet>();
                movingPlanet.Configure(movingDifficulty);
            }
        }

        rocket.AddPlanet(p.transform);
    }
}
