using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class AsteroidMover : MonoBehaviour
{
    public const int VisualVariantCount = 6;

    private const int TextureSize = 64;
    private const float ProceduralVisualRadius = 0.28f;
    private const string SpriteResourcePrefix = "Asteroids/asteroid_";
    private static readonly List<AsteroidMover> activeAsteroids = new List<AsteroidMover>();
    private static Sprite[] cachedSprites;

    public static IReadOnlyList<AsteroidMover> ActiveAsteroids => activeAsteroids;

    public float speed;
    public Vector2 direction;
    public GameObject explosionPrefab; // Kept for existing Inspector data. GameManager owns crash VFX.
    public float destroyY;

    [Header("Yavaşlama Ayarları")]
    public float slowRadius = 5f;
    public float slowMultiplier = 0.65f;

    [Header("Adil Çarpışma")]
    [SerializeField, Range(0.6f, 0.9f)] private float asteroidColliderVisualRatio = 0.74f;
    [SerializeField, Range(0.6f, 0.9f)] private float rocketVisualHitRatio = 0.78f;

    private AsteroidSpawner ownerPool;
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D circleCollider;
    private TrailRenderer trailRenderer;
    private TrailRenderer beamTrail;
    private SpriteRenderer atmosphereGlow;
    private Transform rocketTransform;
    private Vector3 baseScale;
    private Vector3 lastRocketPosition;
    private float originalSpeed;
    private float rotationSpeed;
    private bool baseScaleCaptured;
    private bool configured;
    private bool isExploded;
    private int spawnOrder;

    public Vector2 WorldVelocity => direction * speed;
    public int SpawnOrder => spawnOrder;
    public bool IsActiveThreat => isActiveAndEnabled && configured && !isExploded;
    public float CollisionRadius
    {
        get
        {
            if (circleCollider != null)
            {
                Vector3 scale = circleCollider.transform.lossyScale;
                return circleCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            }

            Vector3 scaleFallback = transform.lossyScale;
            return ProceduralVisualRadius * asteroidColliderVisualRatio
                * Mathf.Max(Mathf.Abs(scaleFallback.x), Mathf.Abs(scaleFallback.y));
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActiveList()
    {
        activeAsteroids.Clear();
    }

    void Awake()
    {
        CacheComponents();
    }

    void OnEnable()
    {
        if (!activeAsteroids.Contains(this)) activeAsteroids.Add(this);
        FindRocket();
        lastRocketPosition = rocketTransform != null ? rocketTransform.position : transform.position;
    }

    void OnDisable()
    {
        activeAsteroids.Remove(this);
        if (trailRenderer != null) trailRenderer.Clear();
        if (beamTrail != null) beamTrail.Clear();
    }

    void Start()
    {
        // Backward-compatible fallback for a mover placed directly in a scene.
        if (!configured)
        {
            PrepareForSpawn(
                ownerPool,
                speed,
                direction == Vector2.zero ? Vector2.down : direction,
                explosionPrefab,
                destroyY,
                0,
                1f,
                90f,
                transform.eulerAngles.z,
                0);
        }
    }

    public void SetPoolOwner(AsteroidSpawner owner)
    {
        ownerPool = owner;
        CacheComponents();
    }

    public void PrepareForSpawn(
        AsteroidSpawner owner,
        float movementSpeed,
        Vector2 movementDirection,
        GameObject crashPrefab,
        float recycleY,
        int visualVariant,
        float sizeMultiplier,
        float angularSpeed,
        float initialAngle,
        int order)
    {
        CacheComponents();
        ownerPool = owner;
        speed = Mathf.Max(0f, movementSpeed);
        originalSpeed = speed;
        direction = movementDirection.sqrMagnitude > 0.0001f
            ? movementDirection.normalized
            : Vector2.down;
        explosionPrefab = crashPrefab;
        destroyY = recycleY;
        rotationSpeed = angularSpeed;
        spawnOrder = order;
        isExploded = false;
        configured = true;

        int variant = ((visualVariant % VisualVariantCount) + VisualVariantCount) % VisualVariantCount;
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = GetAsteroidSprite(variant);
            spriteRenderer.color = Color.white;
            spriteRenderer.sortingOrder = 0;
        }

        float safeScale = Mathf.Clamp(sizeMultiplier, 0.72f, 1.35f);
        transform.localScale = new Vector3(
            baseScale.x * safeScale,
            baseScale.y * safeScale,
            baseScale.z);
        transform.rotation = Quaternion.Euler(0f, 0f, initialAngle);

        if (circleCollider != null)
        {
            circleCollider.radius = ProceduralVisualRadius * asteroidColliderVisualRatio;
            circleCollider.offset = Vector2.zero;
            circleCollider.isTrigger = true;
        }

        if (trailRenderer != null)
        {
            trailRenderer.Clear();
            trailRenderer.emitting = true;
        }
        if (beamTrail != null)
        {
            beamTrail.Clear();
            beamTrail.emitting = true;
        }
        if (atmosphereGlow != null)
        {
            atmosphereGlow.color = new Color(1f, 0.32f, 0.08f, 0.20f);
            atmosphereGlow.transform.localScale = Vector3.one * Mathf.Lerp(1.35f, 1.72f, safeScale);
        }

        FindRocket();
        lastRocketPosition = rocketTransform != null ? rocketTransform.position : transform.position;
    }

    void Update()
    {
        if (!configured || !GameManager.isGameStarted) return;

        if (GameManager.isGameOver)
        {
            RecycleOrDestroy();
            return;
        }

        FindRocket();
        HandleSlowdown();

        Vector3 previousPosition = transform.position;
        Vector3 nextPosition = previousPosition
            + (Vector3)(direction * speed * Time.deltaTime);
        transform.position = nextPosition;
        transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);

        if (nextPosition.y < destroyY)
        {
            RecycleOrDestroy();
            return;
        }

        if (!isExploded && rocketTransform != null && HasSweptCollision(previousPosition, nextPosition))
        {
            isExploded = true;

            // GameManager has the once-only guard, sound, flash, shake and explosion prefab.
            if (GameManager.instance != null)
                GameManager.instance.TriggerGameOver();

            RecycleOrDestroy();
            return;
        }

        if (rocketTransform != null) lastRocketPosition = rocketTransform.position;
    }

    public float EstimateThreatTime(
        Vector2 targetPosition,
        Vector2 targetVelocity,
        float targetRadius,
        out float closestDistance)
    {
        Vector2 relativePosition = (Vector2)transform.position - targetPosition;
        Vector2 relativeVelocity = WorldVelocity - targetVelocity;
        float velocitySquared = relativeVelocity.sqrMagnitude;

        closestDistance = relativePosition.magnitude;
        if (velocitySquared < 0.0001f) return float.PositiveInfinity;

        float timeToClosest = -Vector2.Dot(relativePosition, relativeVelocity) / velocitySquared;
        if (timeToClosest < 0f) return float.PositiveInfinity;

        Vector2 closestOffset = relativePosition + relativeVelocity * timeToClosest;
        closestDistance = closestOffset.magnitude;

        float combinedRadius = Mathf.Max(0.1f, targetRadius) + CollisionRadius;
        float a = velocitySquared;
        float b = 2f * Vector2.Dot(relativePosition, relativeVelocity);
        float c = relativePosition.sqrMagnitude - combinedRadius * combinedRadius;

        if (c <= 0f) return 0f;

        float discriminant = b * b - 4f * a * c;
        if (discriminant >= 0f)
        {
            float firstContact = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
            if (firstContact >= 0f) return firstContact;
        }

        // A near trajectory is still useful to warn about; miss distance makes it lower priority.
        return timeToClosest + closestDistance * 0.3f;
    }

    void CacheComponents()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        if (circleCollider == null) circleCollider = GetComponent<CircleCollider2D>();
        if (trailRenderer == null) trailRenderer = GetComponent<TrailRenderer>();
        EnsurePresentation();

        if (!baseScaleCaptured)
        {
            baseScale = transform.localScale;
            baseScaleCaptured = true;
        }

        FindRocket();
    }

    void EnsurePresentation()
    {
        if (trailRenderer == null)
            trailRenderer = gameObject.AddComponent<TrailRenderer>();
        ConfigureTrail(
            trailRenderer,
            0.42f,
            0.42f,
            0.02f,
            new Color(1f, 0.25f, 0.055f, 0.24f),
            new Color(0.34f, 0.10f, 0.055f, 0f),
            -3);

        Transform presentation = transform.Find("IncomingPresentation");
        if (presentation == null)
        {
            GameObject presentationObject = new GameObject("IncomingPresentation");
            presentationObject.transform.SetParent(transform, false);
            presentation = presentationObject.transform;
        }

        Transform beam = presentation.Find("LightBeam");
        if (beam == null)
        {
            GameObject beamObject = new GameObject("LightBeam");
            beamObject.transform.SetParent(presentation, false);
            beam = beamObject.transform;
        }
        beamTrail = beam.GetComponent<TrailRenderer>();
        if (beamTrail == null) beamTrail = beam.gameObject.AddComponent<TrailRenderer>();
        ConfigureTrail(
            beamTrail,
            0.24f,
            0.19f,
            0f,
            new Color(1f, 0.86f, 0.52f, 0.78f),
            new Color(1f, 0.18f, 0.04f, 0f),
            -2);

        Transform glow = presentation.Find("AtmosphericGlow");
        if (glow == null)
        {
            GameObject glowObject = new GameObject("AtmosphericGlow");
            glowObject.transform.SetParent(presentation, false);
            glow = glowObject.transform;
        }
        atmosphereGlow = glow.GetComponent<SpriteRenderer>();
        if (atmosphereGlow == null) atmosphereGlow = glow.gameObject.AddComponent<SpriteRenderer>();
        atmosphereGlow.sprite = VfxSpriteFactory.SoftSprite;
        atmosphereGlow.color = new Color(1f, 0.32f, 0.08f, 0.20f);
        atmosphereGlow.sortingOrder = -1;
        atmosphereGlow.transform.localPosition = Vector3.zero;
        atmosphereGlow.transform.localScale = Vector3.one * 1.5f;
    }

    static void ConfigureTrail(
        TrailRenderer trail,
        float lifetime,
        float startWidth,
        float endWidth,
        Color startColor,
        Color endColor,
        int sortingOrder)
    {
        trail.sharedMaterial = VfxSpriteFactory.TrailMaterial;
        trail.time = lifetime;
        trail.startWidth = startWidth;
        trail.endWidth = endWidth;
        trail.minVertexDistance = 0.035f;
        trail.numCornerVertices = 3;
        trail.numCapVertices = 2;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.sortingOrder = sortingOrder;
        trail.colorGradient = new Gradient
        {
            colorKeys = new[]
            {
                new GradientColorKey(startColor, 0f),
                new GradientColorKey(Color.Lerp(startColor, endColor, 0.55f), 0.42f),
                new GradientColorKey(endColor, 1f),
            },
            alphaKeys = new[]
            {
                new GradientAlphaKey(startColor.a, 0f),
                new GradientAlphaKey(startColor.a * 0.55f, 0.45f),
                new GradientAlphaKey(0f, 1f),
            },
        };
    }

    void FindRocket()
    {
        if (rocketTransform != null) return;
        GameObject rocket = GameObject.FindGameObjectWithTag("Player");
        if (rocket != null) rocketTransform = rocket.transform;
    }

    void HandleSlowdown()
    {
        if (rocketTransform == null || GameManager.instance == null)
        {
            speed = originalSpeed;
            return;
        }

        int score = GameManager.instance.GetScore();
        if (score >= 20 || slowRadius <= 0f)
        {
            speed = originalSpeed;
            return;
        }

        float distance = Vector2.Distance(transform.position, rocketTransform.position);
        if (distance >= slowRadius)
        {
            speed = originalSpeed;
            return;
        }

        float t = Mathf.SmoothStep(0f, 1f, distance / slowRadius);
        speed = Mathf.Lerp(originalSpeed * slowMultiplier, originalSpeed, t);
    }

    bool HasSweptCollision(Vector3 asteroidStart, Vector3 asteroidEnd)
    {
        Vector2 rocketEnd = rocketTransform.position;
        Vector2 relativeStart = (Vector2)asteroidStart - (Vector2)lastRocketPosition;
        Vector2 relativeEnd = (Vector2)asteroidEnd - rocketEnd;
        float combinedRadius = GetRocketHitRadius() + CollisionRadius;
        return DistanceFromOriginToSegment(relativeStart, relativeEnd) <= combinedRadius;
    }

    float GetRocketHitRadius()
    {
        if (rocketTransform == null) return 0.45f;

        float colliderRadius = 0f;
        CircleCollider2D rocketCollider = rocketTransform.GetComponent<CircleCollider2D>();
        if (rocketCollider != null)
            colliderRadius = Mathf.Max(rocketCollider.bounds.extents.x, rocketCollider.bounds.extents.y);

        float visualRadius = 0f;
        SpriteRenderer rocketRenderer = rocketTransform.GetComponent<SpriteRenderer>();
        if (rocketRenderer != null && rocketRenderer.sprite != null)
        {
            Vector3 scale = rocketTransform.lossyScale;
            Vector2 localHalf = rocketRenderer.sprite.bounds.extents;
            visualRadius = Mathf.Min(
                localHalf.x * Mathf.Abs(scale.x),
                localHalf.y * Mathf.Abs(scale.y)) * rocketVisualHitRatio;
        }

        return Mathf.Max(0.1f, colliderRadius, visualRadius);
    }

    static float DistanceFromOriginToSegment(Vector2 start, Vector2 end)
    {
        Vector2 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon) return start.magnitude;
        float t = Mathf.Clamp01(-Vector2.Dot(start, segment) / lengthSquared);
        return (start + segment * t).magnitude;
    }

    void RecycleOrDestroy()
    {
        configured = false;
        if (ownerPool != null)
            ownerPool.Recycle(this);
        else
            Destroy(gameObject);
    }

    static Sprite GetAsteroidSprite(int variant)
    {
        if (cachedSprites == null || cachedSprites.Length != VisualVariantCount)
        {
            cachedSprites = new Sprite[VisualVariantCount];
            for (int i = 0; i < cachedSprites.Length; i++)
            {
                cachedSprites[i] = Resources.Load<Sprite>(SpriteResourcePrefix + i);
                if (cachedSprites[i] == null)
                    cachedSprites[i] = CreateAsteroidSprite(i);
            }
        }

        return cachedSprites[variant];
    }

    static Sprite CreateAsteroidSprite(int variant)
    {
        // System.Random keeps procedural asset creation from perturbing Unity gameplay randomness.
        System.Random random = new System.Random(0x51A7 + variant * 7919);
        Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            name = $"Runtime_Asteroid_{variant + 1:00}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        int pointCount = 9 + variant % 3;
        float[] radii = new float[pointCount];
        float baseRadius = TextureSize * 0.5f - 4f;
        for (int i = 0; i < pointCount; i++)
        {
            float alternating = (i & 1) == 0 ? 0.04f : -0.035f;
            radii[i] = baseRadius * Mathf.Clamp(
                0.78f + alternating + (float)random.NextDouble() * 0.19f,
                0.72f,
                1f);
        }

        FacetCrater[] craters = new FacetCrater[2 + variant % 2];
        for (int i = 0; i < craters.Length; i++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float distance = 5f + (float)random.NextDouble() * 10f;
            craters[i] = new FacetCrater
            {
                center = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance,
                radius = 3.2f + (float)random.NextDouble() * 3.2f
            };
        }

        Color32[] palette =
        {
            new Color32(111, 91, 76, 255),
            new Color32(91, 102, 112, 255),
            new Color32(126, 91, 66, 255),
            new Color32(91, 84, 102, 255),
            new Color32(118, 107, 84, 255),
            new Color32(82, 104, 102, 255)
        };

        Color32[] pixels = new Color32[TextureSize * TextureSize];
        Vector2 center = new Vector2((TextureSize - 1) * 0.5f, (TextureSize - 1) * 0.5f);
        Color32 baseColor = palette[variant];

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                Vector2 offset = new Vector2(x, y) - center;
                float distance = offset.magnitude;
                float angle = Mathf.Atan2(offset.y, offset.x);
                if (angle < 0f) angle += Mathf.PI * 2f;

                float sector = angle / (Mathf.PI * 2f) * pointCount;
                int first = Mathf.FloorToInt(sector) % pointCount;
                int second = (first + 1) % pointCount;
                float boundary = Mathf.Lerp(radii[first], radii[second], sector - Mathf.Floor(sector));

                if (distance > boundary + 0.65f)
                {
                    pixels[y * TextureSize + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                Vector2 normal = distance > 0.001f ? offset / distance : Vector2.up;
                float light = Vector2.Dot(normal, new Vector2(-0.55f, 0.84f));
                float shade = light > 0.28f ? 1.14f : light < -0.25f ? 0.72f : 0.91f;
                shade += (((first * 37 + variant * 17) % 5) - 2) * 0.035f;

                for (int i = 0; i < craters.Length; i++)
                {
                    Vector2 craterOffset = offset - craters[i].center;
                    float diamondDistance = Mathf.Abs(craterOffset.x) + Mathf.Abs(craterOffset.y);
                    if (diamondDistance < craters[i].radius)
                        shade *= diamondDistance > craters[i].radius * 0.72f ? 0.58f : 0.73f;
                }

                if (boundary - distance < 1.35f) shade *= 0.53f;
                byte alpha = distance <= boundary - 0.45f
                    ? (byte)255
                    : (byte)Mathf.RoundToInt(Mathf.Clamp01((boundary + 0.65f - distance) / 1.1f) * 255f);
                pixels[y * TextureSize + x] = Shade(baseColor, shade, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, TextureSize, TextureSize),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = $"Asteroid_LowPoly_{variant + 1:00}";
        return sprite;
    }

    static Color32 Shade(Color32 color, float multiplier, byte alpha)
    {
        return new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * multiplier), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * multiplier), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * multiplier), 0, 255),
            alpha);
    }

    struct FacetCrater
    {
        public Vector2 center;
        public float radius;
    }
}
