using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RocketController : MonoBehaviour
{
    public struct ContinueState
    {
        public Transform planet;
        public Vector3 planetRelativePosition;
        public Quaternion rotation;
        public float angle;
        public float orbitRadius;
        public int orbitDirection;
    }

    public enum LandingQuality
    {
        Normal,
        EdgeCatch,
        Perfect
    }

    [Header("Trajectory")]
    public LineRenderer trajectoryLine;    // Yön gösterge çizgisi (şu an devre dışı)
    public int trajectoryPoints = 10;      // Kaç nokta gösterilsin (şu an kullanılmıyor)
    public float trajectoryLength = 5f;    // Çizgi uzunluğu (şu an kullanılmıyor)

    [Header("Roket Efekti")]
    public ParticleSystem thrusterParticles;          // Raketin altındaki ateş/alev particle sistemi
    [SerializeField] private float orbitEmissionRate = 15f;  // Orbit dönerken saniyede kaç partikül çıksın
    [SerializeField] private float flightEmissionRate = 45f; // Uçuş sırasında saniyede kaç partikül çıksın

    [Header("Orbit Ayarları")]
    private float orbitSpeed = 120f;       // Şu anki dönüş hızı (runtime'da değişir)
    public float orbitRadius = 3f;         // Gezegene olan mesafe (yörünge yarıçapı)
    [SerializeField, Min(0.05f)] private float reverseHoldThreshold = 0.2f;
    [SerializeField, Range(0.04f, 0.16f)] private float captureSettleDuration = 0.08f;

    [Header("Uçuş Ayarları")]
    public float flightSpeed = 8f;         // Fırlatınca raketin düz uçuş hızı
    public float captureRadius = 2.5f;     // Sonraki gezegene bu kadar yaklaşınca yakalanma tetiklenir

    [Header("Hassasiyet ve Zorluk")]
    [SerializeField] private float minimumCaptureRadius = 0.75f;
    [SerializeField] private float maximumFlightSpeed = 13f;
    [SerializeField] private int maximumDifficultyScore = 75;
    [SerializeField, Range(0.1f, 0.7f)] private float perfectRatio = 0.42f;
    [SerializeField, Range(0.55f, 0.95f)] private float edgeCatchRatio = 0.78f;
    [SerializeField] private float almostMissMultiplier = 1.35f;

    [Header("Okunabilirlik")]
    [SerializeField] private bool showTrajectoryHint = true;
    [SerializeField] private bool showTargetRing = true;
    [SerializeField, Min(1)] private int trajectoryHintScoreLimit = 10;
    [SerializeField] private Color trajectoryColor = new Color(0.25f, 0.85f, 1f, 0.72f);
    [SerializeField] private Color targetColor = new Color(0.25f, 0.85f, 1f, 0.85f);

    public const float OrbitRadiusMultiplier = 1.25f;

    [Header("Zorluk Ayarları")]
    private float baseOrbitSpeed = 120f;   // Oyun başındaki temel dönüş hızı
    public float speedIncreaseRate = 3f;   // Her skor artışında orbit hızına eklenen miktar
    private int persistentOrbitDirection = 1; // HOLD ile değişir ve bırakınca korunur
    private PlanetSpawner planetSpawner;   // PlanetSpawner referansını önbelleğe alır

    private float closestApproach = float.MaxValue;
    private float pointerDownTime = 0f;
    private bool pointerStartedOverUI = false;
    private bool isPointerDown = false;
    private bool holdTriggered = false;
    public List<Transform> planets = new List<Transform>(); // Sahnedeki tüm gezegenlerin listesi
    private int currentIndex = 0;          // Şu an hangi gezegenin etrafında dönülüyor

    private bool isOrbiting = true;        // true = orbit, false = uçuş
    public bool IsOrbiting => isOrbiting;

    // Presentation-only view of the direction already used by the launch guide
    // and DoLaunch. It exposes existing math without changing orbit, launch, or
    // physics authority, allowing replacement visuals to agree with the guide.
    public Vector3 PresentationHeadingDirection
    {
        get
        {
            if (!isOrbiting && flyDir.sqrMagnitude > Mathf.Epsilon)
                return flyDir.normalized;
            if (planets != null && planets.Count > 0 && currentIndex >= 0)
                return CalculateLaunchDirection();
            return transform.right;
        }
    }
    private float angle = 0f;             // Şu anki orbit açısı (derece)
    private Vector3 flyDir;               // Fırlatma anındaki uçuş yönü
    private LineRenderer targetRing;
    private Transform launchGuideAnchor;
    private GameObject dashedGuideRoot;
    private readonly List<LineRenderer> dashedGuideSegments = new List<LineRenderer>();
    private TrailRenderer flightTrail;
    private SpriteRenderer rocketSpriteRenderer;
    private MeshRenderer[] rocketModelRenderers;
    private Collider2D rocketHitbox;
    private Transform trackedFlightTarget;
    private Vector3 previousFlightTargetPosition;
    private Quaternion captureStartRotation;
    private float captureStartOrbitRadius;
    private float captureSettleElapsed;
    private bool isSettlingCapture;
    void Awake()
    {
        // Sahneyi sadece bir kez tarar ve referansı saklar
        planetSpawner = FindAnyObjectByType<PlanetSpawner>();
        rocketSpriteRenderer = GetComponent<SpriteRenderer>();
        rocketModelRenderers = GetComponentsInChildren<MeshRenderer>(true);
        rocketHitbox = GetComponent<Collider2D>();
        EnsureLaunchGuideAnchor();
        ConfigureTrajectoryLine();
        CreateDashedGuide();
        CreateTargetRing();
        ConfigureFlightTrail();
        GameplayVFX.Ensure();

        foreach (Transform planet in planets)
            PlanetPresentation.Attach(planet);
    }

    void Start()
    {
        EnsureRocketFlame();
    }

    void OnEnable()
    {
        rocketModelRenderers = GetComponentsInChildren<MeshRenderer>(true);

        Transform anchor = transform.Find("LaunchGuideAnchor");
        if (anchor != null) launchGuideAnchor = anchor;

        Transform ring = transform.Find("TargetRing");
        if (ring != null) targetRing = ring.GetComponent<LineRenderer>();

        Transform dashRoot = transform.Find("LaunchGuideDashes");
        if (dashRoot != null) dashedGuideRoot = dashRoot.gameObject;
        RebindDashedGuideSegments();
    }

    void OnDestroy()
    {
        if (targetRing != null) Destroy(targetRing.gameObject);
    }

    void OnDisable()
    {
        CancelHoldInput();
        if (targetRing != null) targetRing.enabled = false;
        if (trajectoryLine != null) trajectoryLine.positionCount = 0;
        SetDashedGuideVisible(false);
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) CancelHoldInput();
    }

    void Update()
    {
        // Runs before the gameplay guard on purpose: the states this catches are the
        // ones where the rest of Update either never runs or cannot reach DoFlight's
        // own miss check, which is what would otherwise leave a run alive forever with
        // no ship on screen and no Game Over.
        WatchForLostRocket();

        if (!CanRunGameplay())
        {
            CancelHoldInput();
            if (targetRing != null) targetRing.enabled = false;
            if (trajectoryLine != null)
            {
                trajectoryLine.positionCount = 0;
                trajectoryLine.enabled = false;
            }
            SetDashedGuideVisible(false);
            return;
        }

        if (planets.Count == 0) return;     // Gezegen yoksa hiçbir şey yapma
        if (isOrbiting)
        {
            // Process the threshold before moving so a HOLD toggle takes effect on the
            // exact frame it is detected and cannot turn into a launch on release.
            if (HandleOrbitInput())
            {
                DoLaunch();
                return;
            }

            DoOrbit();       // Gezegen etrafında dön
        }
        else
        {
            CancelHoldInput();
            DoFlight(); // Düz uç
        }
    }

    void LateUpdate()
    {
        if (isOrbiting && CanRunGameplay()) DrawTrajectory();
    }

    bool HandleOrbitInput()
    {
        bool pointerDown = TapInput.PointerPressedThisFrame;
        bool keyDown = TapInput.KeyPressedThisFrame;
        if (pointerDown || keyDown)
        {
            CancelHoldInput();
            pointerStartedOverUI = pointerDown && TapInput.IsPointerOverUI();
            if (!pointerStartedOverUI)
            {
                isPointerDown = true;
                pointerDownTime = Time.unscaledTime;
            }
        }

        if (isPointerDown)
        {
            // The system took the finger away — a volume overlay, a notification shade.
            // The press ends where it stands and never becomes a launch.
            if (TapInput.PointerCancelledThisFrame)
            {
                CancelHoldInput();
                return false;
            }

            if (TapInput.PointerHeld && TapInput.IsPointerOverUI())
            {
                CancelHoldInput();
                return false;
            }

            bool isStillHeld = TapInput.PointerHeld || TapInput.KeyHeld;
            if (isStillHeld
                && !holdTriggered
                && Time.unscaledTime - pointerDownTime >= reverseHoldThreshold)
            {
                ToggleOrbitDirection();
            }
        }

        if (!TapInput.PointerReleasedThisFrame && !TapInput.KeyReleasedThisFrame) return false;

        if (pointerStartedOverUI)
        {
            CancelHoldInput();
            return false;
        }

        if (!isPointerDown) return false;

        // A press can cross the threshold between two rendered frames. Treat that as a
        // HOLD on pointer-up as well, toggling once and suppressing the launch.
        if (!holdTriggered && Time.unscaledTime - pointerDownTime >= reverseHoldThreshold)
            ToggleOrbitDirection();

        bool wasHoldTriggered = holdTriggered;
        CancelHoldInput();

        return !wasHoldTriggered && isOrbiting && CanRunGameplay();
    }

    void ToggleOrbitDirection()
    {
        holdTriggered = true;
        persistentOrbitDirection *= -1;
    }

    public void CancelHoldInput()
    {
        isPointerDown = false;
        pointerStartedOverUI = false;
        holdTriggered = false;
    }

    public void ResetForNewRun()
    {
        CancelHoldInput();
        persistentOrbitDirection = 1;
        EnsureRocketFlame();
    }

    /// <summary>
    /// The orbit invariant every new run starts from, guaranteed before gameplay takes
    /// authority. Called by <see cref="RunSession.Begin"/>, which is the single entry
    /// point for the first run, a Main Menu launch, Fly Again and both restarts.
    ///
    /// A run must never begin mid-flight, without an orbit target or carrying the
    /// previous run's held direction. The Main Menu hand-over runs after this and
    /// writes the exact pose the ship is already in (see RestoreContinueState), so
    /// guaranteeing the invariant here costs the launch nothing.
    /// </summary>
    public void BeginRun()
    {
        ResetForNewRun();

        isOrbiting = true;
        isSettlingCapture = false;
        captureSettleElapsed = 0f;
        flyDir = Vector3.zero;
        trackedFlightTarget = null;
        closestApproach = float.MaxValue;
        lostRocketElapsed = 0f;

        if (flightTrail != null)
        {
            flightTrail.emitting = false;
            flightTrail.Clear();
        }
        if (trajectoryLine != null) trajectoryLine.positionCount = 0;

        if (planets == null || planets.Count == 0) return;

        // A list that was rebuilt, or an index left over from a previous run, must not
        // become an orbit around nothing.
        currentIndex = Mathf.Clamp(currentIndex, 0, planets.Count - 1);
        if (planets[currentIndex] == null) currentIndex = 0;

        Transform planet = planets[currentIndex];
        if (planet == null) return;

        orbitRadius = CalculateOrbitRadius(planet);

        // Keep the angle the ship already stands at when that is meaningful, so the
        // hand-over and a restart both resume from a readable pose rather than snapping
        // to a fixed side of the planet.
        Vector3 outward = transform.position - planet.position;
        angle = outward.sqrMagnitude > 0.0001f
            ? Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg
            : angle;

        float rad = angle * Mathf.Deg2Rad;
        Vector3 orbitOutward = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
        transform.position = planet.position + orbitOutward * orbitRadius;
        transform.rotation = CalculateOrbitRotation(orbitOutward, persistentOrbitDirection);
    }

    // ─── Lost-rocket fail-safe ───────────────────────────────────────────────

    // A run is only ever ended by this after every legitimate explanation has been
    // ruled out and the invalid state has held continuously. It exists for the state
    // no legitimate path produces: an active run whose ship has no orbit target at all,
    // where DoOrbit cannot place it and DoFlight's own miss check never runs.
    const float LostRocketConfirmSeconds = 1.25f;

    float lostRocketElapsed;

    void WatchForLostRocket()
    {
        if (!IsRocketLost())
        {
            lostRocketElapsed = 0f;
            return;
        }

        lostRocketElapsed += Time.unscaledDeltaTime;
        if (lostRocketElapsed < LostRocketConfirmSeconds) return;

        lostRocketElapsed = 0f;
        Debug.LogError("RocketController: the run has no valid orbit target and no crash, " +
                       "landing or transition is in progress. Ending the run through the normal " +
                       "Game Over path rather than leaving it stuck.", this);

        // The normal path, once. TriggerGameOver refuses a second call while a Game
        // Over is already running, so this can never stack.
        GameManager.isNearMiss = false;
        if (GameManager.instance != null) GameManager.instance.TriggerGameOver();
    }

    bool IsRocketLost()
    {
        // Only an active, un-paused, un-presented run can be stuck. Every one of these
        // is a legitimate reason for the ship to be standing still.
        if (!GameManager.isGameStarted || GameManager.isGameOver) return false;
        if (GameManager.isIntroPlaying) return false;
        if (WorldTransitionManager.IsPlaying) return false;
        if (PauseManager.instance != null && PauseManager.instance.IsPaused) return false;
        if (PresentationGate.IsAnyFullScreenPresentationActive) return false;
        if (Mathf.Approximately(Time.timeScale, 0f)) return false;

        // A missed launch is not this: it is still flying toward a real planet and
        // DoFlight ends it on its own distance check. This is the ship having nothing
        // to orbit or fly towards at all.
        return planets == null
            || planets.Count == 0
            || currentIndex < 0
            || currentIndex >= planets.Count
            || planets[currentIndex] == null;
    }

    public bool TryCaptureContinueState(out ContinueState state)
    {
        state = default;
        if (!isOrbiting || currentIndex < 0 || currentIndex >= planets.Count)
            return false;

        Transform currentPlanet = planets[currentIndex];
        if (currentPlanet == null)
            return false;

        state = new ContinueState
        {
            planet = currentPlanet,
            planetRelativePosition = transform.position - currentPlanet.position,
            rotation = transform.rotation,
            angle = angle,
            orbitRadius = orbitRadius,
            orbitDirection = persistentOrbitDirection
        };
        return true;
    }

    public bool RestoreContinueState(ContinueState state)
    {
        int restoredIndex = state.planet != null ? planets.IndexOf(state.planet) : -1;
        if (restoredIndex < 0)
            return false;

        currentIndex = restoredIndex;
        transform.position = state.planet.position + state.planetRelativePosition;
        transform.rotation = state.rotation;
        angle = state.angle;
        orbitRadius = state.orbitRadius;
        persistentOrbitDirection = state.orbitDirection == 0 ? 1 : state.orbitDirection;
        isOrbiting = true;
        isSettlingCapture = false;
        captureSettleElapsed = 0f;
        closestApproach = float.MaxValue;
        trackedFlightTarget = null;
        flyDir = Vector3.zero;
        CancelHoldInput();

        if (flightTrail != null)
        {
            flightTrail.emitting = false;
            flightTrail.Clear();
        }
        if (trajectoryLine != null)
            trajectoryLine.positionCount = 0;
        if (rocketSpriteRenderer != null)
            rocketSpriteRenderer.enabled = true;
        if (rocketHitbox != null)
            rocketHitbox.enabled = true;

        enabled = true;
        EnsureRocketFlame();
        if (thrusterParticles != null)
            thrusterParticles.Play();
        SetEmissionRate(orbitEmissionRate);
        return true;
    }

    void EnsureRocketFlame()
    {
        LowPolyRocketFlame flame = GetComponent<LowPolyRocketFlame>();
        if (flame == null) flame = gameObject.AddComponent<LowPolyRocketFlame>();
        flame.EnsureSetup();
    }

    bool CanRunGameplay()
    {
        if (!GameManager.isGameStarted || GameManager.isGameOver) return false;
        // The menu still owns the ship while the camera pulls back into the game.
        if (GameManager.isIntroPlaying) return false;
        if (WorldTransitionManager.IsPlaying) return false;
        if (PauseManager.instance != null && PauseManager.instance.IsPaused) return false;
        return !Mathf.Approximately(Time.timeScale, 0f);
    }

    public Transform GetUpcomingPlanetTransform()
    {
        if (planets == null || planets.Count == 0) return null;
        if (currentIndex + 1 < planets.Count) return planets[currentIndex + 1];
        if (currentIndex >= 0 && currentIndex < planets.Count) return planets[currentIndex];
        return null;
    }

    int CurrentOrbitDirection => persistentOrbitDirection;

    void DoOrbit()
    {
        int score = GameManager.instance.GetScore();
        ApplyDifficulty(score);
        orbitSpeed = baseOrbitSpeed + (score * speedIncreaseRate);
        int direction = CurrentOrbitDirection;
        angle += orbitSpeed * direction * Time.deltaTime;

        Vector3 center = planets[currentIndex].position;
        float rad = angle * Mathf.Deg2Rad;
        Vector3 outward = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
        Quaternion targetRotation = CalculateOrbitRotation(outward, direction);

        if (isSettlingCapture)
        {
            captureSettleElapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(captureSettleElapsed / Mathf.Max(0.01f, captureSettleDuration));
            transform.rotation = Quaternion.Slerp(
                captureStartRotation,
                targetRotation,
                Mathf.SmoothStep(0f, 1f, progress));
            orbitRadius = Mathf.Lerp(
                captureStartOrbitRadius,
                CalculateOrbitRadius(planets[currentIndex]),
                Mathf.SmoothStep(0f, 1f, progress));
            if (progress >= 1f)
            {
                transform.rotation = targetRotation;
                orbitRadius = CalculateOrbitRadius(planets[currentIndex]);
                isSettlingCapture = false;
            }
        }
        else
        {
            transform.rotation = targetRotation;
            orbitRadius = CalculateOrbitRadius(planets[currentIndex]);
        }

        transform.position = center + outward * orbitRadius;

        SetEmissionRate(orbitEmissionRate);
    }

    void DrawTrajectory()
    {
        UpdateTargetRing();

        int score = GameManager.instance != null
            ? GameManager.instance.GetScore()
            : 0;
        int globalLevel = score + 1;

        bool canShowGuide = showTrajectoryHint
            && OrbitAssistanceProgression.IsAvailableForScore(score)
            && currentIndex >= 0
            && currentIndex + 1 < planets.Count;

        if (trajectoryLine == null || !canShowGuide)
        {
            if (trajectoryLine != null)
            {
                trajectoryLine.positionCount = 0;
                trajectoryLine.enabled = false;
            }
            SetDashedGuideVisible(false);
            return;
        }

        UpdateLaunchGuideAnchor();
        Vector3 start = launchGuideAnchor != null ? launchGuideAnchor.position : transform.position;
        Vector3 launchDirection = CalculateLaunchDirection();
        float fullLength = Mathf.Max(0.5f, trajectoryLength);
        if (OrbitAssistanceProgression.IsFullForPlanet(globalLevel))
        {
            SetDashedGuideVisible(false);
            trajectoryLine.enabled = true;
            trajectoryLine.positionCount = 2;
            trajectoryLine.startColor = trajectoryColor;
            trajectoryLine.endColor = trajectoryColor;
            trajectoryLine.SetPosition(0, start);
            trajectoryLine.SetPosition(1, start + launchDirection * fullLength);
            return;
        }

        trajectoryLine.positionCount = 0;
        trajectoryLine.enabled = false;
        DrawDashedGuide(start, start + launchDirection * (fullLength * 0.48f), globalLevel);
    }

    void DoLaunch()
    {
        if (trajectoryLine != null) trajectoryLine.positionCount = 0;
        if (targetRing != null) targetRing.enabled = false;
        SetDashedGuideVisible(false);
        if (AudioManager.instance != null) AudioManager.instance.PlayLaunch();
        SetEmissionRate(flightEmissionRate);

        flyDir = CalculateLaunchDirection();
        closestApproach = float.MaxValue;
        isSettlingCapture = false;
        trackedFlightTarget = currentIndex + 1 < planets.Count ? planets[currentIndex + 1] : null;
        previousFlightTargetPosition = trackedFlightTarget != null
            ? trackedFlightTarget.position
            : Vector3.zero;

        if (flightTrail != null)
        {
            flightTrail.Clear();
            flightTrail.emitting = true;
        }
        GameplayVFX.Ensure().PlayLaunch(transform.position, flyDir);

        isOrbiting = false;
    }

    void DoFlight()
    {
        int score = GameManager.instance.GetScore();
        ApplyDifficulty(score);

        Vector3 previousPosition = transform.position;
        Vector3 nextPosition = previousPosition + flyDir * flightSpeed * Time.deltaTime;
        transform.position = nextPosition;

        // Uçuş yönüne döndür
        float rotAngle = Mathf.Atan2(flyDir.y, flyDir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, rotAngle);

        // Sonraki gezegen var mı ve yaklaştık mı?
        if (currentIndex + 1 < planets.Count)
        {
            Transform next = planets[currentIndex + 1];
            if (trackedFlightTarget != next)
            {
                trackedFlightTarget = next;
                previousFlightTargetPosition = next.position;
            }

            Vector3 targetStart = previousFlightTargetPosition;
            Vector3 targetEnd = next.position;
            Vector3 relativeStart = previousPosition - targetStart;
            Vector3 relativeEnd = nextPosition - targetEnd;
            float ringRadius = PlanetPresentation.GetOrbitRingRadius(next);

            float dist = DistanceToSegment(Vector3.zero, relativeStart, relativeEnd);
            closestApproach = Mathf.Min(closestApproach, dist);

            // Solve in target-relative space so a moving planet cannot pass through the
            // rocket between frames. The rocket's oriented sprite support is part of the
            // boundary, therefore its visible edge is what first touches the ring.
            if (TryGetFirstRingContact(
                relativeStart,
                relativeEnd,
                ringRadius,
                out Vector3 relativeContact,
                out float attachmentCenterRadius))
            {
                Vector3 relativeDirection = (nextPosition - previousPosition)
                    - (targetEnd - targetStart);
                float predictedClosestApproach = DistanceToRay(
                    Vector3.zero,
                    relativeStart,
                    relativeDirection);
                LandingQuality quality = EvaluateLandingQuality(predictedClosestApproach, attachmentCenterRadius);

                Vector3 outward = relativeContact;
                if (outward.sqrMagnitude <= Mathf.Epsilon)
                    outward = relativeStart.sqrMagnitude > Mathf.Epsilon
                        ? relativeStart
                        : Vector3.right;
                outward.Normalize();

                orbitRadius = attachmentCenterRadius;
                captureStartOrbitRadius = attachmentCenterRadius;
                captureStartRotation = transform.rotation;
                captureSettleElapsed = 0f;
                isSettlingCapture = true;
                transform.position = next.position + outward * orbitRadius;
                trackedFlightTarget = null;
                closestApproach = float.MaxValue;
                currentIndex++;        // Bir sonraki gezegene geç
                isOrbiting = true;     // Orbit moduna dön
                CancelHoldInput();
                if (flightTrail != null) flightTrail.emitting = false;
                if (AudioManager.instance != null) AudioManager.instance.StopLaunch();

                // currentIndex - 2'deki gezegen artık kesinlikle geride kaldı, temizle
                int removeIndex = currentIndex - 2;
                if (removeIndex >= 0 && removeIndex < planets.Count)
                {
                    Destroy(planets[removeIndex].gameObject); // Sahnadeki objeyi sil
                    planets.RemoveAt(removeIndex);            // Listeden çıkar
                    currentIndex--;                           // Silinen eleman indexi kaydırdı, düzelt
                }
                SetEmissionRate(orbitEmissionRate); // Partikül yoğunluğunu düşür

                // İlk temas açısını aynen koru; takip eden orbit frame'i aynı çemberden devam eder.
                angle = Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg;

                GameManager.instance.RegisterLanding(quality);
                PlayLandingFeedback(next.position, quality);

                GameManager.instance.AddScore();
                if (planetSpawner != null) planetSpawner.SpawnPlanet();

                if (CoinManager.instance != null)
                {
                    CoinManager.instance.AwardLanding(
                        transform.position,
                        GameManager.instance.GetScore(),
                        GameManager.instance.GetCombo());
                }
                if (AudioManager.instance != null)
                {
                    float landingPitch = quality == LandingQuality.Perfect
                        ? 1.14f
                        : quality == LandingQuality.EdgeCatch ? 1.07f : 1f;
                    AudioManager.instance.PlayLand(landingPitch);
                }
                GameManager.instance.CaptureContinueCheckpoint();
                return;
            }

            previousFlightTargetPosition = targetEnd;
        }

        // Mevcut gezegenden çok uzaklaştıysa oyun biter
        float distToCurrent = Vector3.Distance(transform.position, planets[currentIndex].position);
        if (distToCurrent > 12f)
        {
            // Compare against the same capture envelope used during flight contact tests,
            // not the post-landing orbit radius (which includes OrbitRadiusMultiplier).
            float missRadius = currentIndex + 1 < planets.Count
                ? PlanetPresentation.GetOrbitRingRadius(planets[currentIndex + 1]) + GetRocketOrbitHalfSize()
                : captureRadius;
            GameManager.isNearMiss = closestApproach > missRadius
                && closestApproach <= missRadius * almostMissMultiplier;
            GameManager.instance.TriggerGameOver();
        }
    }

    Vector3 CalculateLaunchDirection()
    {
        float rad = angle * Mathf.Deg2Rad;
        int direction = CurrentOrbitDirection;
        Vector3 tangent = new Vector3(
            -Mathf.Sin(rad) * direction,
            Mathf.Cos(rad) * direction,
            0f).normalized;

        if (currentIndex + 1 >= planets.Count) return tangent;

        Vector3 toNext = (planets[currentIndex + 1].position - transform.position).normalized;
        return (tangent * 0.7f + toNext * 0.3f).normalized;
    }

    void ApplyDifficulty(int score)
    {
        float difficulty = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(0f, maximumDifficultyScore, score));
        captureRadius = Mathf.Lerp(2.1f, minimumCaptureRadius, difficulty);
        flightSpeed = Mathf.Lerp(8f, maximumFlightSpeed, difficulty);
    }

    LandingQuality EvaluateLandingQuality(float distance, float referenceRadius)
    {
        float ratio = referenceRadius > 0f ? distance / referenceRadius : 1f;
        if (ratio <= perfectRatio) return LandingQuality.Perfect;
        if (ratio >= edgeCatchRatio) return LandingQuality.EdgeCatch;
        return LandingQuality.Normal;
    }

    float CalculateOrbitRadius(Transform planet)
    {
        return (PlanetPresentation.GetOrbitRingRadius(planet) + GetRocketOrbitHalfSize())
            * OrbitRadiusMultiplier;
    }

    public float GetOrbitEnvelopeRadius(Transform planet) => CalculateOrbitRadius(planet);

    /// <summary>
    /// The rocket's longest on-screen dimension in world units, with every parent
    /// transform already folded in. Crash presentation sizes itself against this
    /// so the debris always inherits the live rocket's effective world scale.
    /// </summary>
    public float VisualWorldSize
    {
        get
        {
            float modelSize = 0f;
            for (int i = 0; rocketModelRenderers != null && i < rocketModelRenderers.Length; i++)
            {
                Bounds bounds = rocketModelRenderers[i].bounds;
                modelSize = Mathf.Max(modelSize, bounds.size.x, bounds.size.y);
            }

            SpriteRenderer renderer = rocketSpriteRenderer;
            if (renderer != null && renderer.sprite != null)
            {
                Vector3 scale = transform.lossyScale;
                Vector3 size = renderer.sprite.bounds.size;
                return Mathf.Max(
                    0.05f,
                    modelSize,
                    size.x * Mathf.Abs(scale.x),
                    size.y * Mathf.Abs(scale.y));
            }

            Collider2D hitbox = rocketHitbox;
            return hitbox != null
                ? Mathf.Max(0.05f, modelSize, hitbox.bounds.size.x, hitbox.bounds.size.y)
                : Mathf.Max(0.5f, modelSize);
        }
    }

    float GetRocketOrbitHalfSize()
    {
        SpriteRenderer renderer = rocketSpriteRenderer;
        if (renderer != null && renderer.sprite != null)
        {
            float localHalfHeight = renderer.sprite.bounds.extents.y;
            return Mathf.Max(0.05f, localHalfHeight * Mathf.Abs(transform.lossyScale.y));
        }

        Collider2D hitbox = rocketHitbox;
        return hitbox != null ? Mathf.Max(0.05f, hitbox.bounds.extents.y) : 0.25f;
    }

    float GetRocketSupport(Vector3 radialDirection)
    {
        Vector3 radial = radialDirection.sqrMagnitude > Mathf.Epsilon
            ? radialDirection.normalized
            : Vector3.right;

        SpriteRenderer renderer = rocketSpriteRenderer;
        if (renderer != null && renderer.sprite != null)
        {
            Vector3 scale = transform.lossyScale;
            float halfX = renderer.sprite.bounds.extents.x * Mathf.Abs(scale.x);
            float halfY = renderer.sprite.bounds.extents.y * Mathf.Abs(scale.y);
            float projected = Mathf.Abs(Vector3.Dot(radial, transform.right)) * halfX
                + Mathf.Abs(Vector3.Dot(radial, transform.up)) * halfY;
            return Mathf.Max(0.05f, projected);
        }

        Collider2D hitbox = rocketHitbox;
        if (hitbox != null)
        {
            Vector3 extents = hitbox.bounds.extents;
            return Mathf.Max(0.05f,
                Mathf.Abs(radial.x) * extents.x + Mathf.Abs(radial.y) * extents.y);
        }

        return 0.25f;
    }

    static Quaternion CalculateOrbitRotation(Vector3 outward, int direction)
    {
        Vector3 toCenter = outward.sqrMagnitude > Mathf.Epsilon
            ? -outward.normalized
            : Vector3.left;
        float rotAngle = Mathf.Atan2(toCenter.y, toCenter.x) * Mathf.Rad2Deg;
        float orbitOffset = direction == 1 ? -90f : 90f;
        return Quaternion.Euler(0f, 0f, rotAngle + orbitOffset);
    }

    static float DistanceToRay(Vector3 point, Vector3 origin, Vector3 direction)
    {
        Vector3 normalizedDirection = direction.sqrMagnitude > Mathf.Epsilon
            ? direction.normalized
            : Vector3.right;
        float alongRay = Mathf.Max(0f, Vector3.Dot(point - origin, normalizedDirection));
        return Vector3.Distance(point, origin + normalizedDirection * alongRay);
    }

    bool TryGetFirstRingContact(
        Vector3 relativeStart,
        Vector3 relativeEnd,
        float ringRadius,
        out Vector3 relativeContact,
        out float attachmentCenterRadius)
    {
        ringRadius = Mathf.Max(0.001f, ringRadius);
        Vector3 relativeSegment = relativeEnd - relativeStart;
        Vector3 fallbackRadial = relativeStart.sqrMagnitude > Mathf.Epsilon
            ? relativeStart.normalized
            : relativeSegment.sqrMagnitude > Mathf.Epsilon
                ? -relativeSegment.normalized
                : Vector3.right;

        float startClearance = GetRingClearance(relativeStart, fallbackRadial, ringRadius);
        if (startClearance <= 0f)
        {
            Vector3 outward = relativeStart.sqrMagnitude > Mathf.Epsilon
                ? relativeStart.normalized
                : fallbackRadial;
            attachmentCenterRadius = ringRadius + GetRocketSupport(outward);
            relativeContact = outward * attachmentCenterRadius;
            return true;
        }

        float lengthSquared = relativeSegment.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
        {
            relativeContact = relativeEnd;
            attachmentCenterRadius = ringRadius + GetRocketSupport(fallbackRadial);
            return false;
        }

        const int SweepSamples = 12;
        float low = 0f;
        float high = 0f;
        bool foundInside = false;
        for (int i = 1; i <= SweepSamples; i++)
        {
            float sampleT = i / (float)SweepSamples;
            Vector3 sample = relativeStart + relativeSegment * sampleT;
            if (GetRingClearance(sample, fallbackRadial, ringRadius) > 0f) continue;

            low = (i - 1) / (float)SweepSamples;
            high = sampleT;
            foundInside = true;
            break;
        }

        if (!foundInside)
        {
            // Preserve the exact closest-point test for very shallow, sub-sample grazes.
            float closestT = Mathf.Clamp01(-Vector3.Dot(relativeStart, relativeSegment) / lengthSquared);
            Vector3 closest = relativeStart + relativeSegment * closestT;
            if (GetRingClearance(closest, fallbackRadial, ringRadius) <= 0f)
            {
                low = 0f;
                high = closestT;
                foundInside = true;
            }
        }

        if (!foundInside)
        {
            relativeContact = relativeEnd;
            attachmentCenterRadius = ringRadius + GetRocketSupport(fallbackRadial);
            return false;
        }

        for (int i = 0; i < 12; i++)
        {
            float mid = (low + high) * 0.5f;
            Vector3 candidate = relativeStart + relativeSegment * mid;
            if (GetRingClearance(candidate, fallbackRadial, ringRadius) > 0f)
                low = mid;
            else
                high = mid;
        }

        Vector3 firstContact = relativeStart + relativeSegment * high;
        Vector3 contactOutward = firstContact.sqrMagnitude > Mathf.Epsilon
            ? firstContact.normalized
            : fallbackRadial;
        attachmentCenterRadius = ringRadius + GetRocketSupport(contactOutward);
        relativeContact = contactOutward * attachmentCenterRadius;
        return true;
    }

    float GetRingClearance(Vector3 relativePosition, Vector3 fallbackRadial, float ringRadius)
    {
        Vector3 outward = relativePosition.sqrMagnitude > Mathf.Epsilon
            ? relativePosition.normalized
            : fallbackRadial;
        return relativePosition.magnitude - ringRadius - GetRocketSupport(outward);
    }

    static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        return Vector3.Distance(point, ClosestPointOnSegment(point, start, end));
    }

    static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon) return start;
        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSquared);
        return start + segment * t;
    }

    void ConfigureFlightTrail()
    {
        flightTrail = GetComponent<TrailRenderer>();
        if (flightTrail == null) flightTrail = gameObject.AddComponent<TrailRenderer>();

        flightTrail.time = 0.20f;
        flightTrail.minVertexDistance = 0.08f;
        flightTrail.startWidth = 0.14f;
        flightTrail.endWidth = 0.015f;
        flightTrail.numCornerVertices = 2;
        flightTrail.numCapVertices = 2;
        flightTrail.alignment = LineAlignment.View;
        flightTrail.textureMode = LineTextureMode.Stretch;
        flightTrail.sortingOrder = -1;
        flightTrail.sharedMaterial = VfxSpriteFactory.TrailMaterial;
        flightTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flightTrail.receiveShadows = false;
        flightTrail.emitting = false;

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.92f, 0.42f), 0f),
                new GradientColorKey(new Color(1f, 0.34f, 0.06f), 0.55f),
                new GradientColorKey(new Color(0.25f, 0.75f, 1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.88f, 0f),
                new GradientAlphaKey(0.58f, 0.55f),
                new GradientAlphaKey(0f, 1f)
            });
        flightTrail.colorGradient = gradient;
    }

    void ConfigureTrajectoryLine()
    {
        if (trajectoryLine == null) return;
        trajectoryLine.useWorldSpace = true;
        trajectoryLine.loop = false;
        trajectoryLine.startWidth = 0.055f;
        trajectoryLine.endWidth = 0.055f;
        trajectoryLine.startColor = trajectoryColor;
        trajectoryLine.endColor = trajectoryColor;
        trajectoryLine.positionCount = 0;
    }

    void EnsureLaunchGuideAnchor()
    {
        Transform existing = transform.Find("LaunchGuideAnchor");
        if (existing != null) launchGuideAnchor = existing;
        else
        {
            GameObject anchor = new GameObject("LaunchGuideAnchor");
            anchor.transform.SetParent(transform, false);
            launchGuideAnchor = anchor.transform;
        }
        launchGuideAnchor.localPosition = Vector3.zero;
        launchGuideAnchor.localRotation = Quaternion.identity;
        launchGuideAnchor.localScale = Vector3.one;
    }

    void UpdateLaunchGuideAnchor()
    {
        if (launchGuideAnchor == null) EnsureLaunchGuideAnchor();

        RocketModelVisual modelVisual = GetComponent<RocketModelVisual>();
        Transform explicitNose = modelVisual != null ? modelVisual.ActiveNoseSocket : null;
        if (explicitNose != null)
        {
            launchGuideAnchor.position = explicitNose.position;
            return;
        }

        Vector3 nose = transform.position;
        Vector3 noseAxis = transform.right;
        float farthest = 0f;
        for (int i = 0; rocketModelRenderers != null && i < rocketModelRenderers.Length; i++)
        {
            MeshRenderer renderer = rocketModelRenderers[i];
            Bounds bounds = renderer.localBounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 localCorner = bounds.center + new Vector3(
                    bounds.extents.x * x,
                    bounds.extents.y * y,
                    bounds.extents.z * z);
                Vector3 worldCorner = renderer.transform.TransformPoint(localCorner);
                farthest = Mathf.Max(farthest,
                    Vector3.Dot(worldCorner - transform.position, noseAxis));
            }
        }

        if (farthest <= 0.001f && rocketSpriteRenderer != null && rocketSpriteRenderer.sprite != null)
            farthest = rocketSpriteRenderer.sprite.bounds.extents.x * Mathf.Abs(transform.lossyScale.x);

        launchGuideAnchor.position = nose + noseAxis * farthest;
    }

    void CreateDashedGuide()
    {
        if (dashedGuideRoot == null)
        {
            Transform existing = transform.Find("LaunchGuideDashes");
            dashedGuideRoot = existing != null ? existing.gameObject : null;
        }
        if (dashedGuideRoot == null)
        {
            dashedGuideRoot = new GameObject("LaunchGuideDashes");
            dashedGuideRoot.transform.SetParent(transform, false);
        }

        RebindDashedGuideSegments();
        const int segmentCount = 6;
        for (int i = dashedGuideSegments.Count; i < segmentCount; i++)
        {
            GameObject go = new GameObject("Dash" + (i + 1).ToString("00"));
            go.transform.SetParent(dashedGuideRoot.transform, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = false;
            line.positionCount = 0;
            line.startWidth = line.endWidth = 0.045f;
            line.sortingOrder = trajectoryLine != null ? trajectoryLine.sortingOrder : 1;
            if (trajectoryLine != null) line.sharedMaterial = trajectoryLine.sharedMaterial;
            dashedGuideSegments.Add(line);
        }
        dashedGuideRoot.SetActive(false);
    }

    void RebindDashedGuideSegments()
    {
        dashedGuideSegments.Clear();
        if (dashedGuideRoot == null) return;

        foreach (Transform child in dashedGuideRoot.transform)
        {
            LineRenderer line = child.GetComponent<LineRenderer>();
            if (line != null) dashedGuideSegments.Add(line);
        }
    }

    void DrawDashedGuide(Vector3 start, Vector3 end, int globalLevel)
    {
        if (dashedGuideRoot == null || dashedGuideSegments.Count == 0) CreateDashedGuide();
        dashedGuideRoot.SetActive(true);
        Vector3 delta = end - start;
        int count = dashedGuideSegments.Count;
        float stage = Mathf.InverseLerp(
            OrbitAssistanceProgression.FullAssistanceLastPlanet + 1f,
            OrbitAssistanceProgression.LastAssistedPlanet,
            globalLevel);
        float baseAlpha = Mathf.Lerp(0.58f, 0.34f, stage);
        for (int i = 0; i < count; i++)
        {
            float segmentStart = i / (float)count;
            float segmentEnd = Mathf.Min(1f, segmentStart + 0.58f / count);
            LineRenderer line = dashedGuideSegments[i];
            line.enabled = true;
            line.positionCount = 2;
            line.SetPosition(0, start + delta * segmentStart);
            line.SetPosition(1, start + delta * segmentEnd);
            Color color = trajectoryColor;
            color.a = baseAlpha * Mathf.Lerp(1f, 0.28f, i / (float)Mathf.Max(1, count - 1));
            line.startColor = line.endColor = color;
        }
    }

    void SetDashedGuideVisible(bool visible)
    {
        if (dashedGuideRoot != null) dashedGuideRoot.SetActive(visible);
    }

    void CreateTargetRing()
    {
        GameObject go = new GameObject("TargetRing");
        go.transform.SetParent(transform, false);
        targetRing = go.AddComponent<LineRenderer>();
        targetRing.useWorldSpace = true;
        targetRing.loop = true;
        targetRing.positionCount = 48;
        targetRing.startWidth = targetRing.endWidth = 0.045f;
        targetRing.startColor = targetRing.endColor = targetColor;
        targetRing.sortingOrder = 1;
        if (trajectoryLine != null)
            targetRing.sharedMaterial = trajectoryLine.sharedMaterial;
        targetRing.enabled = false;
    }

    void UpdateTargetRing()
    {
        if (targetRing == null) return;
        int score = GameManager.instance != null ? GameManager.instance.GetScore() : 0;
        int globalLevel = score + 1;
        bool canShow = showTargetRing
            && OrbitAssistanceProgression.IsAvailableForScore(score)
            && isOrbiting
            && currentIndex >= 0
            && currentIndex < planets.Count;
        targetRing.enabled = canShow;
        if (!canShow) return;

        Transform target = planets[currentIndex];
        Vector3 center = target.position;
        float radius = PlanetPresentation.GetOrbitRingRadius(target);
        Color pulsedColor = targetColor;
        if (OrbitAssistanceProgression.IsFullForPlanet(globalLevel))
        {
            pulsedColor.a = targetColor.a;
        }
        else
        {
            float alphaRatio = globalLevel >= OrbitAssistanceProgression.LastAssistedPlanet
                ? 0.20f
                : 0.48f - (globalLevel
                    - (OrbitAssistanceProgression.FullAssistanceLastPlanet + 1)) * 0.03f;
            pulsedColor.a = targetColor.a * alphaRatio;
        }
        targetRing.startColor = targetRing.endColor = pulsedColor;
        for (int i = 0; i < targetRing.positionCount; i++)
        {
            float a = i / (float)targetRing.positionCount * Mathf.PI * 2f;
            targetRing.SetPosition(i, center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
        }
    }

    void PlayLandingFeedback(Vector3 center, LandingQuality quality)
    {
        Color color = quality == LandingQuality.Perfect
            ? new Color(1f, 0.82f, 0.15f, 1f)
            : quality == LandingQuality.EdgeCatch
                ? new Color(1f, 0.38f, 0.08f, 1f)
                : targetColor;

        StartCoroutine(AnimateLandingPulse(center, color));
        GameplayVFX.Ensure().PlayLanding(center, quality);

        CameraFollow cameraFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cameraFollow != null)
            cameraFollow.PlayLandingKick(quality != LandingQuality.Normal);

        if (quality == LandingQuality.EdgeCatch && NearMissEffect.instance != null)
            NearMissEffect.instance.Trigger();
    }

    IEnumerator AnimateLandingPulse(Vector3 center, Color color)
    {
        GameObject go = new GameObject("LandingPulse");
        LineRenderer pulse = go.AddComponent<LineRenderer>();
        pulse.useWorldSpace = true;
        pulse.loop = true;
        pulse.positionCount = 40;
        pulse.startWidth = pulse.endWidth = 0.07f;
        pulse.startColor = pulse.endColor = color;
        pulse.sortingOrder = 2;
        if (trajectoryLine != null) pulse.sharedMaterial = trajectoryLine.sharedMaterial;

        float elapsed = 0f;
        const float duration = 0.42f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(elapsed / duration);
            float radius = Mathf.Lerp(0.35f, 1.35f, Mathf.SmoothStep(0f, 1f, p));
            Color faded = color;
            faded.a = 1f - p;
            pulse.startColor = pulse.endColor = faded;

            for (int i = 0; i < pulse.positionCount; i++)
            {
                float a = i / (float)pulse.positionCount * Mathf.PI * 2f;
                pulse.SetPosition(i, center + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius);
            }
            yield return null;
        }
        Destroy(go);
    }

    // Dışarıdan yeni gezegen eklemek için (PlanetSpawner tarafından çağrılır)
    public void AddPlanet(Transform t)
    {
        PlanetPresentation.Attach(t);
        planets.Add(t);

        if (planets.Count == 1 && currentIndex == 0 && isOrbiting)
        {
            orbitRadius = CalculateOrbitRadius(t);
            Vector3 outward = transform.position - t.position;
            if (outward.sqrMagnitude <= Mathf.Epsilon) outward = Vector3.right;
            angle = Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg;
        }
    }

    // Particle System'in saniyedeki partikül sayısını ayarla
    void SetEmissionRate(float rate)
    {
        if (thrusterParticles == null) return; // Particle System atanmamışsa sessizce geç, hata verme
        var emission = thrusterParticles.emission; // Emission modülünü al (struct olduğu için var ile alıyoruz)
        emission.rateOverTime = rate;              // Saniyedeki partikül sayısını güncelle
    }
}
