using System.Collections.Generic;
using UnityEngine;

public class AsteroidSpawner : MonoBehaviour
{
    [Header("Prefablar")]
    public GameObject asteroidPrefab;
    public GameObject explosionPrefab;

    [Header("Spawn Ayarları")]
    public float minSpawnInterval = 2f;
    public float maxSpawnInterval = 4f;
    public float minSpeed = 3f;
    public float maxSpeed = 6f;
    public float spawnHeightOffset = 12f;

    [Header("Kontrollü Varyasyon")]
    [SerializeField, Range(0f, 0.18f)] private float movementVariation = 0.07f;
    [SerializeField, Range(0.7f, 1f)] private float minimumSizeMultiplier = 0.84f;
    [SerializeField, Range(1f, 1.35f)] private float maximumSizeMultiplier = 1.18f;
    [SerializeField] private float minimumRotationSpeed = 18f;
    [SerializeField] private float maximumRotationSpeed = 62f;
    [SerializeField] private float scoreSpeedPerPoint = 0.05f;
    [SerializeField] private float maximumScoreSpeedBonus = 3.5f;

    [Header("Asteroid Havuzu")]
    [SerializeField, Min(0)] private int prewarmCount = 6;
    [SerializeField, Min(4)] private int maximumPoolSize = 16;

    [Header("Referanslar")]
    public Camera mainCamera;

    private readonly Queue<GameObject> available = new Queue<GameObject>();
    private readonly HashSet<GameObject> pooled = new HashSet<GameObject>();
    private float spawnTimer;
    private float nextSpawnTime;
    private int createdCount;
    private int spawnSequence;
    private bool spawningArmed;

    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;

        prewarmCount = Mathf.Max(0, prewarmCount);
        maximumPoolSize = Mathf.Max(4, maximumPoolSize, prewarmCount);
        maximumScoreSpeedBonus = Mathf.Max(0f, maximumScoreSpeedBonus);
        PrewarmPool();
    }

    void Update()
    {
        if (!GameManager.isGameStarted || GameManager.isGameOver || mainCamera == null) return;
        if (GameManager.instance == null) return;

        int score = GameManager.instance.GetScore();
        if (!AsteroidDifficulty.IsActive(score)) return;

        // The very first interval is drawn the moment the run reaches the
        // activation planet, so the introduction is as sparse as the curve says.
        if (!spawningArmed)
        {
            spawningArmed = true;
            spawnTimer = 0f;
            nextSpawnTime = GetNextSpawnInterval(score);
        }

        spawnTimer += Time.deltaTime;
        if (spawnTimer < nextSpawnTime) return;

        SpawnAsteroid(score);
        spawnTimer = 0f;
        nextSpawnTime = GetNextSpawnInterval(score);
    }

    float GetNextSpawnInterval(int score)
    {
        return Random.Range(minSpawnInterval, maxSpawnInterval)
            * AsteroidDifficulty.EvaluateForScore(score).SpawnIntervalMultiplier;
    }

    void SpawnAsteroid(int score)
    {
        GameObject asteroid = Acquire();
        if (asteroid == null) return;

        float cameraHalfWidth = mainCamera.orthographicSize * mainCamera.aspect;
        float spawnX = Random.Range(-cameraHalfWidth + 1f, cameraHalfWidth - 1f);
        float spawnY = mainCamera.transform.position.y + spawnHeightOffset;
        asteroid.transform.position = new Vector3(spawnX, spawnY, 0f);

        int order = spawnSequence++;
        float sequence01 = ((order * 37) % 101) / 100f;
        float sizeMultiplier = Mathf.Lerp(minimumSizeMultiplier, maximumSizeMultiplier, sequence01);
        float rotationMagnitude = Mathf.Lerp(
            minimumRotationSpeed,
            maximumRotationSpeed,
            ((order * 53 + 17) % 97) / 96f);
        float rotationSpeed = (order & 1) == 0 ? rotationMagnitude : -rotationMagnitude;
        float initialAngle = (order * 137.50776f) % 360f;

        float scoreBonus = Mathf.Min(score * scoreSpeedPerPoint, maximumScoreSpeedBonus);
        float baseMovementSpeed = Random.Range(minSpeed, maxSpeed) + scoreBonus;
        // The progression multiplier scales the randomised speed rather than
        // replacing it, so every asteroid keeps its own variation.
        float variedMovementSpeed = baseMovementSpeed
            * Random.Range(1f - movementVariation, 1f + movementVariation)
            * AsteroidDifficulty.EvaluateForScore(score).SpeedMultiplier;
        Vector2 movementDirection = new Vector2(Random.Range(-0.3f, 0.3f), -1f).normalized;

        AsteroidMover mover = asteroid.GetComponent<AsteroidMover>();
        mover.PrepareForSpawn(
            this,
            variedMovementSpeed,
            movementDirection,
            explosionPrefab,
            mainCamera.transform.position.y - spawnHeightOffset,
            order % AsteroidMover.VisualVariantCount,
            sizeMultiplier,
            rotationSpeed,
            initialAngle,
            order);

        asteroid.SetActive(true);
    }

    void PrewarmPool()
    {
        if (asteroidPrefab == null) return;

        int count = Mathf.Min(prewarmCount, maximumPoolSize);
        for (int i = 0; i < count; i++)
        {
            GameObject asteroid = CreatePooledAsteroid();
            if (asteroid == null) break;
            pooled.Add(asteroid);
            available.Enqueue(asteroid);
        }
    }

    GameObject Acquire()
    {
        while (available.Count > 0)
        {
            GameObject asteroid = available.Dequeue();
            pooled.Remove(asteroid);
            if (asteroid != null) return asteroid;
            createdCount = Mathf.Max(0, createdCount - 1);
        }

        if (createdCount >= maximumPoolSize) return null;
        return CreatePooledAsteroid();
    }

    GameObject CreatePooledAsteroid()
    {
        if (asteroidPrefab == null) return null;

        GameObject asteroid = Instantiate(asteroidPrefab, transform);
        asteroid.SetActive(false);
        AsteroidMover mover = asteroid.GetComponent<AsteroidMover>();
        if (mover == null) mover = asteroid.AddComponent<AsteroidMover>();
        mover.SetPoolOwner(this);
        createdCount++;
        return asteroid;
    }

    public void Recycle(AsteroidMover mover)
    {
        if (mover == null) return;
        GameObject asteroid = mover.gameObject;
        if (pooled.Contains(asteroid)) return;

        asteroid.SetActive(false);
        asteroid.transform.SetParent(transform, true);

        if (available.Count >= maximumPoolSize)
        {
            createdCount = Mathf.Max(0, createdCount - 1);
            Destroy(asteroid);
            return;
        }

        pooled.Add(asteroid);
        available.Enqueue(asteroid);
    }
}
