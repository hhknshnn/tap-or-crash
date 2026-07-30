using UnityEngine;

public class PlanetSpawner : MonoBehaviour
{
    [System.Serializable]
    public class PlanetLevel
    {
        public string levelName;
        public GameObject[] prefabs;
    }

    // Her seviye PlanetsPerLevel kadar gezegen sürer (Level 1 = Natural, Level 2 = Ice, ...).
    // Tanımlı seviyeler tükenince aşağıdaki planetPrefabs (uzay gezegenleri) havuzuna dönülür.
    public const int PlanetsPerLevel = 10;
    private const float BenchmarkPlanetAuthoringScale = 0.30f;
    private const float DesertGameplayTargetSize = 0.66f;
    public PlanetLevel[] levels;

    public GameObject[] planetPrefabs;
    public RocketController rocket;

    [Header("Spawn Ayarları")]
    public float spawnDistance = 4f;

    // Hareketli gezegen şansı (0–1 arası). Skor 15'i geçince devreye girer.
    [SerializeField] private float movingPlanetChance = 0.25f;

    private int   lastPlanetIndex = -1;
    private int[] lastLevelIndex;
    private Camera mainCamera;

    public static string[] LevelNames { get; private set; } = new string[0];

    public static int LevelIndexForScore(int score) => score / PlanetsPerLevel;

    void Awake()
    {
        LevelNames = levels != null
            ? System.Array.ConvertAll(levels, l => l.levelName)
            : new string[0];

        lastLevelIndex = new int[levels != null ? levels.Length : 0];
        for (int i = 0; i < lastLevelIndex.Length; i++) lastLevelIndex[i] = -1;
    }

    void Start()
    {
        mainCamera = Camera.main;
        SpawnPlanet();
        SpawnPlanet();
    }

    public void SpawnPlanet()
    {
        if (rocket == null) return;

        int score = GameManager.instance != null ? GameManager.instance.GetScore() : 0;
        int levelIndex = LevelIndexForScore(score);

        bool usingLevelPool = levels != null && levelIndex < levels.Length
            && levels[levelIndex].prefabs != null && levels[levelIndex].prefabs.Length > 0;
        GameObject[] pool = usingLevelPool ? levels[levelIndex].prefabs : planetPrefabs;
        if (pool == null || pool.Length == 0) return;

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
        if (usingLevelPool)
        {
            int prevIndex = lastLevelIndex[levelIndex];
            do { newIndex = Random.Range(0, pool.Length); }
            while (newIndex == prevIndex && pool.Length > 1);
            lastLevelIndex[levelIndex] = newIndex;
        }
        else
        {
            do { newIndex = Random.Range(0, pool.Length); }
            while (newIndex == lastPlanetIndex && pool.Length > 1);
            lastPlanetIndex = newIndex;
        }

        GameObject p = Instantiate(pool[newIndex], pos, Quaternion.identity);
        p.tag = "Planet";

        // ── Boyut ayarı (puana göre) ────────────────────────────────────────
        SpriteRenderer sr = p.GetComponent<SpriteRenderer>();

        float difficulty = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 75f, score));
        float targetSize = Mathf.Lerp(0.70f, 0.28f, difficulty);

        bool usesBenchmarkPresentationSize = usingLevelPool
            && (string.Equals(levels[levelIndex].levelName, "Desert", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(levels[levelIndex].levelName, "Ocean", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(levels[levelIndex].levelName, "Crystal", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(levels[levelIndex].levelName, "Sakura", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(levels[levelIndex].levelName, "Mushroom", System.StringComparison.OrdinalIgnoreCase));

        // Collections authored from the benchmark render pipeline use scale-independent
        // sprite bounds. Keep Crystal and Sakura on the same real body diameter as
        // Desert/Ocean so their visible edge, orbit ring and capture boundary agree.
        if (usesBenchmarkPresentationSize)
            targetSize = DesertGameplayTargetSize;

        float naturalSize = sr != null
            ? (usesBenchmarkPresentationSize && sr.sprite != null
                ? Mathf.Max(sr.sprite.bounds.size.x, sr.sprite.bounds.size.y) * BenchmarkPlanetAuthoringScale
                : Mathf.Max(sr.bounds.size.x, sr.bounds.size.y))
            : 1f;
        float finalScale = naturalSize > 0 ? targetSize / naturalSize : 1f;
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
