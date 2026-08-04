using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class GameplayVFX : MonoBehaviour
{
    public static GameplayVFX instance;

    private Canvas canvas;
    private readonly Queue<ParticleSystem> burstPool = new Queue<ParticleSystem>();
    private readonly HashSet<ParticleSystem> activeBursts = new HashSet<ParticleSystem>();
    private const int BurstPoolPrewarm = 5;
    private const int BurstPoolMaximum = 12;
    private int lastCrashFrame = -1;
    private GameObject milestoneRoot;
    private RectTransform milestoneRect;
    private CanvasGroup milestoneGroup;
    private Image milestoneImage;
    private Outline milestoneOutline;
    private TextMeshProUGUI milestoneTitle;
    private TextMeshProUGUI milestoneSubtitle;
    private Coroutine milestoneRoutine;
    private readonly Queue<MilestoneNotice> milestoneQueue = new Queue<MilestoneNotice>();
    private readonly HashSet<MilestoneNoticeId> shownMilestoneNotices =
        new HashSet<MilestoneNoticeId>();
    private bool noticeHoldingGameplay;
    private float timeScaleBeforeNotice = 1f;

    public enum MilestoneNoticeId
    {
        AssistanceEnded,
        AsteroidsOnline,
        MovingOrbits,
    }

    readonly struct MilestoneNotice
    {
        public readonly MilestoneNoticeId Id;
        public readonly string Title;
        public readonly string Subtitle;
        public readonly Color Accent;

        public MilestoneNotice(
            MilestoneNoticeId id,
            string title,
            string subtitle,
            Color accent)
        {
            Id = id;
            Title = title;
            Subtitle = subtitle;
            Accent = accent;
        }
    }

    enum BurstGeometry
    {
        Soft,
        Triangle,
        Diamond,

        /// <summary>Thin expanding annulus — the shock front.</summary>
        Ring,

        /// <summary>Billboard stretched along its own velocity — a spark streak.</summary>
        Spark
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(() => Ensure());

    public static GameplayVFX Ensure()
    {
        if (instance != null) return instance;
        GameplayVFX existing = FindAnyObjectByType<GameplayVFX>();
        if (existing != null)
        {
            instance = existing;
            return existing;
        }

        GameObject go = new GameObject("GameplayVFX");
        return go.AddComponent<GameplayVFX>();
    }

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }
    }

    void OnEnable()
    {
        if (instance == null) instance = this;
    }

    void OnDestroy()
    {
        CancelMilestoneNotices();
        if (instance == this) instance = null;
    }

    void Start()
    {
        PrewarmBurstPool();
        EnsureMilestoneBanner();
    }

    public void PlayLaunch(Vector3 position, Vector3 direction)
    {
        Vector3 origin = position - direction.normalized * 0.28f;
        CreateBurst(origin, new Color(1f, 0.48f, 0.08f, 1f), 6, 0.8f, 1.8f, 0.04f, 0.10f,
            BurstGeometry.Triangle, 0.18f, 0.34f, 0.12f);
    }

    public void PlayLanding(Vector3 position, RocketController.LandingQuality quality)
    {
        Color color = quality == RocketController.LandingQuality.Perfect
            ? new Color(1f, 0.82f, 0.16f, 1f)
            : quality == RocketController.LandingQuality.EdgeCatch
                ? new Color(1f, 0.38f, 0.08f, 1f)
                : new Color(0.24f, 0.85f, 1f, 1f);

        int count = quality == RocketController.LandingQuality.Normal ? 8 : 12;
        CreateBurst(position, color, count, 1.1f, 2.6f, 0.045f, 0.13f,
            BurstGeometry.Diamond, 0.20f, 0.42f, 0.16f);
    }

    public void PlayCrash(Vector3 position, Quaternion rocketRotation, float rocketWorldSize)
    {
        if (lastCrashFrame == Time.frameCount) return;
        lastCrashFrame = Time.frameCount;

        CrashDebrisPresentation.Spawn(position, rocketRotation, rocketWorldSize);

        // Compact core, a thin translucent shock ring, spark streaks, glints and
        // a short soft smoke tail. Every layer is realtime-driven so the complete
        // beat remains inside 0.8 s.
        //
        // Sizes and speeds are fractions of the visible world height rather than
        // absolute units, so the blast reads the same on any aspect ratio and
        // never grows past the screen. The core stays under a tenth of screen
        // height and the ring, the widest layer, opens to under a quarter of it.
        float height = VisibleWorldHeight();

        // Core: small, hot and gone quickly. It is deliberately narrower than the
        // fire puffs behind it, translucent, and it burns from pale gold down to
        // orange over its own life — a flash the flames grow out of rather than a
        // flat white plate laid over them.
        CreateBurst(position, new Color(1f, 0.86f, 0.46f, 0.88f), 1, 0f, 0f,
            0.070f * height, 0.085f * height,
            BurstGeometry.Soft, 0.09f, 0.15f, 0f);
        CreateBurst(position, new Color(1f, 0.58f, 0.24f, 0.15f), 1, 0f, 0f,
            0.185f * height, 0.185f * height,
            BurstGeometry.Ring, 0.19f, 0.23f, 0f);
        CreateBurst(position, new Color(1f, 0.34f, 0.07f, 0.92f), 2,
            0.02f * height, 0.06f * height, 0.085f * height, 0.115f * height,
            BurstGeometry.Soft, 0.30f, 0.46f, 0.008f * height);
        CreateBurst(position, new Color(1f, 0.44f, 0.12f, 1f), 18,
            0.095f * height, 0.235f * height, 0.006f * height, 0.013f * height,
            BurstGeometry.Spark, 0.17f, 0.36f, 0.010f * height);
        CreateBurst(position, new Color(1f, 0.78f, 0.20f, 1f), 12,
            0.075f * height, 0.160f * height, 0.004f * height, 0.009f * height,
            BurstGeometry.Diamond, 0.20f, 0.40f, 0.008f * height);
        CreateBurst(position + Vector3.up * 0.008f * height, new Color(0.18f, 0.20f, 0.25f, 0.62f),
            7, 0.020f * height, 0.055f * height, 0.022f * height, 0.045f * height,
            BurstGeometry.Soft, 0.46f, 0.72f, 0.014f * height);
    }

    /// <summary>
    /// The world height the camera currently shows. Crash presentation sizes
    /// itself against this so the blast keeps the same share of the screen
    /// whatever the camera is zoomed to.
    /// </summary>
    public static float VisibleWorldHeight()
    {
        Camera camera = Camera.main;
        if (camera == null) camera = FindAnyObjectByType<Camera>();
        if (camera == null || !camera.orthographic) return 16f;
        return camera.orthographicSize * 2f;
    }

    /// <summary>
    /// Clears every milestone notice the previous run left behind: the banner on
    /// screen, the queue waiting behind it and the record of which notices were
    /// already shown. Called only by <see cref="RunSession.Begin"/>, so a notice is
    /// eligible again in every new run without any of them being suppressed twice
    /// inside one run.
    /// </summary>
    public void ResetForNewRun()
    {
        CancelMilestoneNotices();
        shownMilestoneNotices.Clear();
    }

    public void EvaluateMilestoneNotices(int score)
    {
        QueueIfReached(score, OrbitAssistanceProgression.AssistanceEndingScore,
            new MilestoneNotice(
                MilestoneNoticeId.AssistanceEnded,
                "NOW YOU ARE ALONE",
                "NO MORE GUIDES — TRUST YOUR TIMING",
                new Color(1f, 0.82f, 0.15f)));
        QueueIfReached(score, AsteroidDifficulty.ActivationPlanet - 1,
            new MilestoneNotice(
                MilestoneNoticeId.AsteroidsOnline,
                "ASTEROIDS ONLINE",
                "WATCH THE WARNING ARROW",
                new Color(1f, 0.40f, 0.12f)));
        QueueIfReached(score, MovingOrbitProgression.ActivationScore,
            new MilestoneNotice(
                MilestoneNoticeId.MovingOrbits,
                "MOVING ORBITS",
                "TRACK THE PLANET BEFORE LAUNCH",
                new Color(0.30f, 0.84f, 1f)));
    }

    void QueueIfReached(int score, int threshold, MilestoneNotice notice)
    {
        if (score < threshold || shownMilestoneNotices.Contains(notice.Id)) return;

        EnsureMilestoneBanner();
        if (milestoneRoot == null) return;

        milestoneQueue.Enqueue(notice);
        shownMilestoneNotices.Add(notice.Id);
        if (milestoneRoutine == null)
            milestoneRoutine = StartCoroutine(ProcessMilestoneQueue(RunSession.Id));
    }

    void CreateBurst(
        Vector3 position,
        Color color,
        int count,
        float minSpeed,
        float maxSpeed,
        float minSize,
        float maxSize,
        BurstGeometry geometry,
        float minLifetime,
        float maxLifetime,
        float shapeRadius)
    {
        ParticleSystem particles = AcquireBurst();
        if (particles == null) return;

        GameObject go = particles.gameObject;
        go.name = geometry == BurstGeometry.Soft
            ? "VFX_Flash"
            : geometry == BurstGeometry.Ring
                ? "VFX_ShockRing"
                : geometry == BurstGeometry.Spark
                    ? "VFX_SparkStreaks"
                    : "VFX_GeometricBurst";
        go.transform.position = position;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.35f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = shapeRadius > 0f;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = shapeRadius;
        shape.arc = 360f;

        // A single soft particle is the explosion core; the multi-particle soft
        // bursts are fire and smoke and keep the shared curves.
        bool isExplosionCore = geometry == BurstGeometry.Soft && count == 1;

        ParticleSystem.ColorOverLifetimeModule colorOverLife = particles.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            isExplosionCore
                // Cooling towards orange as it dies is what makes the core read
                // as burning gas instead of a lit white shape.
                ? new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(new Color(1f, 0.52f, 0.16f), 1f)
                }
                : new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            isExplosionCore
                // Falls away from its very first frame. Holding alpha flat, as
                // the other layers do, is what gave the core its solid-disc edge.
                ? new[]
                {
                    new GradientAlphaKey(color.a, 0f),
                    new GradientAlphaKey(color.a * 0.45f, 0.30f),
                    new GradientAlphaKey(0f, 1f)
                }
                : new[]
                {
                    new GradientAlphaKey(color.a, 0f),
                    new GradientAlphaKey(color.a, 0.55f),
                    new GradientAlphaKey(0f, 1f)
                });
        colorOverLife.color = gradient;

        ParticleSystem.SizeOverLifetimeModule sizeOverLife = particles.sizeOverLifetime;
        sizeOverLife.enabled = true;
        AnimationCurve sizeCurve;
        if (geometry == BurstGeometry.Ring)
        {
            // Opens from a tight collar rather than appearing at full width, so
            // the shock front never lands as an oversized first frame. Snapping
            // open early also keeps it away from the steady sweep of a UI dial.
            sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.30f),
                new Keyframe(0.38f, 1.02f),
                new Keyframe(1f, 1.22f));
        }
        else if (isExplosionCore)
        {
            // Punches out fast, then holds while the alpha carries it away —
            // a shrinking core would read as a retracting shape.
            sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.42f),
                new Keyframe(0.20f, 1f),
                new Keyframe(1f, 0.55f));
        }
        else
        {
            sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.72f, 0.82f),
                new Keyframe(1f, 0f));
        }
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Spinning a velocity-aligned streak would fight its direction, and a
        // ring has no orientation to spin.
        ParticleSystem.RotationOverLifetimeModule rotationOverLife = particles.rotationOverLifetime;
        rotationOverLife.enabled =
            geometry == BurstGeometry.Triangle || geometry == BurstGeometry.Diamond;
        rotationOverLife.z = new ParticleSystem.MinMaxCurve(-3.4f, 3.4f);

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = 2f;
        renderer.velocityScale = 0f;
        switch (geometry)
        {
            case BurstGeometry.Soft:
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = VfxSpriteFactory.ParticleMaterial;
                break;

            case BurstGeometry.Spark:
                // Stretching along velocity is what makes the sparks read as
                // directional streaks instead of a symmetrical puff.
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 2.6f;
                renderer.velocityScale = 0.075f;
                renderer.sharedMaterial = VfxSpriteFactory.ParticleMaterial;
                break;

            default:
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = geometry == BurstGeometry.Triangle
                    ? VfxSpriteFactory.TriangleMesh
                    : geometry == BurstGeometry.Ring
                        ? VfxSpriteFactory.RingMesh
                        : VfxSpriteFactory.DiamondMesh;
                renderer.sharedMaterial = VfxSpriteFactory.GeometricParticleMaterial;
                break;
        }
        renderer.sortingOrder = 12;

        go.SetActive(true);
        activeBursts.Add(particles);
        particles.Play();
        particles.Emit(count);
        StartCoroutine(ReleaseBurstAfterRealtime(particles, Mathf.Max(0.55f, maxLifetime + 0.28f)));
    }

    void PrewarmBurstPool()
    {
        for (int i = 0; i < BurstPoolPrewarm; i++)
        {
            ParticleSystem particles = CreateBurstParticle();
            if (particles != null) burstPool.Enqueue(particles);
        }
    }

    ParticleSystem AcquireBurst()
    {
        while (burstPool.Count > 0)
        {
            ParticleSystem particles = burstPool.Dequeue();
            if (particles != null) return particles;
        }

        if (activeBursts.Count >= BurstPoolMaximum) return null;
        return CreateBurstParticle();
    }

    ParticleSystem CreateBurstParticle()
    {
        GameObject go = new GameObject("VFX_PooledBurst");
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        ParticleSystem particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    IEnumerator ReleaseBurstAfterRealtime(ParticleSystem particles, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (particles == null) yield break;

        // ClearActiveBursts may already have recycled it; releasing twice would
        // return the same system to the pool twice.
        if (!activeBursts.Remove(particles)) yield break;

        Recycle(particles);
    }

    /// <summary>
    /// Stops and recycles every burst still in flight. Called before Fly Again or
    /// Restart hands control back, so a resumed run starts on a clean screen and
    /// repeated crashes cannot accumulate particle objects.
    /// </summary>
    public void ClearActiveBursts()
    {
        if (activeBursts.Count > 0)
        {
            ParticleSystem[] inFlight = new ParticleSystem[activeBursts.Count];
            activeBursts.CopyTo(inFlight);
            activeBursts.Clear();

            for (int i = 0; i < inFlight.Length; i++)
            {
                if (inFlight[i] != null) Recycle(inFlight[i]);
            }
        }

        lastCrashFrame = -1;
    }

    void Recycle(ParticleSystem particles)
    {
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.gameObject.SetActive(false);
        particles.transform.SetParent(transform, false);

        if (burstPool.Count >= BurstPoolMaximum)
            Destroy(particles.gameObject);
        else
            burstPool.Enqueue(particles);
    }

    // The run that queued these notices owns them. Every wait below can outlive its
    // own run — a restart is one tap away at any point — so the run identifier is
    // captured on entry and rechecked around each wait. A queue drained on behalf
    // of a finished run would show its notices over the new one.
    IEnumerator ProcessMilestoneQueue(int runId)
    {
        EnsureMilestoneBanner();
        if (milestoneRoot == null) { milestoneRoutine = null; yield break; }

        // ScoreChanged starts a same-boundary world transition before notices are
        // evaluated. Let it claim its gate, then drain every notice in order.
        yield return null;
        while (milestoneQueue.Count > 0 && RunSession.IsCurrent(runId))
        {
            while (RunSession.IsCurrent(runId)
                && (WorldTransitionManager.IsPendingOrPlaying
                    || PresentationGate.IsAnyFullScreenPresentationActive))
                yield return null;
            yield return new WaitForSecondsRealtime(0.12f);
            while (RunSession.IsCurrent(runId)
                && (WorldTransitionManager.IsPendingOrPlaying
                    || PresentationGate.IsAnyFullScreenPresentationActive))
                yield return null;

            if (!RunSession.IsCurrent(runId)) break;

            MilestoneNotice notice = milestoneQueue.Dequeue();
            yield return ShowMilestoneBanner(notice);
        }

        milestoneRoutine = null;
    }

    IEnumerator ShowMilestoneBanner(MilestoneNotice notice)
    {
        timeScaleBeforeNotice = Time.timeScale;
        noticeHoldingGameplay = true;
        PresentationGate.Acquire(PresentationGate.Kind.GameplayNotice);
        Time.timeScale = 0f;
        try
        {
            milestoneTitle.text = notice.Title;
            milestoneTitle.color = notice.Accent;
            milestoneSubtitle.text = notice.Subtitle;
            milestoneOutline.effectColor = new Color(
                notice.Accent.r, notice.Accent.g, notice.Accent.b, 0.65f);
            milestoneRoot.SetActive(true);
            milestoneGroup.alpha = 0f;
            GameplayPresentationLayout.PlaceTopCentre(milestoneRect,
                canvas.GetComponent<RectTransform>(), GameplayPresentationLayout.Lane.MechanicNotice);
            Vector2 shown = milestoneRect.anchoredPosition;
            Vector2 hidden = shown + Vector2.up * 28f;

            float elapsed = 0f;
            while (elapsed < 0.28f)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.28f));
                milestoneRect.anchoredPosition = Vector2.Lerp(hidden, shown, p);
                milestoneGroup.alpha = p;
                yield return null;
            }

            yield return new WaitForSecondsRealtime(1.35f);

            elapsed = 0f;
            while (elapsed < 0.24f)
            {
                elapsed += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(elapsed / 0.24f);
                milestoneGroup.alpha = 1f - p;
                milestoneRect.anchoredPosition = Vector2.Lerp(shown, shown + Vector2.up * 28f, p);
                yield return null;
            }
        }
        finally
        {
            HideMilestoneBanner();
            ReleaseNoticeGameplayHold();
        }
    }

    public void CancelMilestoneNotices()
    {
        if (milestoneRoutine != null)
        {
            StopCoroutine(milestoneRoutine);
            milestoneRoutine = null;
        }
        milestoneQueue.Clear();
        HideMilestoneBanner();
        ReleaseNoticeGameplayHold();
    }

    void HideMilestoneBanner()
    {
        if (milestoneGroup != null) milestoneGroup.alpha = 0f;
        if (milestoneRoot != null) milestoneRoot.SetActive(false);
    }

    void ReleaseNoticeGameplayHold()
    {
        if (!noticeHoldingGameplay) return;
        noticeHoldingGameplay = false;
        PresentationGate.Release(PresentationGate.Kind.GameplayNotice);
        if (Mathf.Approximately(Time.timeScale, 0f))
            Time.timeScale = timeScaleBeforeNotice;
    }

    void EnsureMilestoneBanner()
    {
        if (milestoneRoot != null) return;
        if (canvas == null)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
                if (canvases[i] != null && canvases[i].isRootCanvas) { canvas = canvases[i]; break; }
            if (canvas == null && canvases.Length > 0) canvas = canvases[0];
        }
        if (canvas == null) return;
        Transform gameUi = canvas.transform.Find("GameUI");
        Transform parent = gameUi != null ? gameUi : canvas.transform;

        Transform existing = parent.Find("MechanicNoticeLane");
        milestoneRoot = existing != null
            ? existing.gameObject
            : new GameObject("MechanicNoticeLane");
        milestoneRoot.transform.SetParent(parent, false);
        milestoneRect = milestoneRoot.GetComponent<RectTransform>();
        if (milestoneRect == null) milestoneRect = milestoneRoot.AddComponent<RectTransform>();
        milestoneRect.sizeDelta = new Vector2(600f, GameplayPresentationLayout.MechanicNoticeHeight);

        milestoneImage = milestoneRoot.GetComponent<Image>();
        if (milestoneImage == null) milestoneImage = milestoneRoot.AddComponent<Image>();
        UIStyleKit.ApplyPanel(milestoneImage, new Color(0.035f, 0.065f, 0.15f, 0.96f));
        milestoneImage.raycastTarget = false;
        milestoneOutline = milestoneRoot.GetComponent<Outline>();
        if (milestoneOutline == null) milestoneOutline = milestoneRoot.AddComponent<Outline>();
        milestoneOutline.effectDistance = new Vector2(2f, -2f);

        Transform titleTransform = milestoneRoot.transform.Find("Title");
        Transform subtitleTransform = milestoneRoot.transform.Find("Subtitle");
        TextMeshProUGUI[] labels = milestoneRoot.GetComponentsInChildren<TextMeshProUGUI>(true);
        milestoneTitle = titleTransform != null ? titleTransform.GetComponent<TextMeshProUGUI>() : null;
        milestoneSubtitle = subtitleTransform != null ? subtitleTransform.GetComponent<TextMeshProUGUI>() : null;
        if (milestoneTitle == null && labels.Length > 0) milestoneTitle = labels[0];
        if (milestoneSubtitle == null && labels.Length > 1) milestoneSubtitle = labels[1];
        if (milestoneTitle == null)
            milestoneTitle = UIStyleKit.MakeLabel(milestoneRoot.transform, string.Empty, 25f, Color.white,
                new Vector2(0f, 20f), new Vector2(560f, 40f), FontStyles.Bold);
        if (milestoneSubtitle == null)
            milestoneSubtitle = UIStyleKit.MakeLabel(milestoneRoot.transform, string.Empty, 14f,
                UIStyleKit.TextSub, new Vector2(0f, -24f), new Vector2(560f, 30f), FontStyles.Bold);
        milestoneTitle.gameObject.name = "Title";
        milestoneTitle.characterSpacing = 3f;
        milestoneSubtitle.gameObject.name = "Subtitle";
        milestoneSubtitle.characterSpacing = 2f;

        milestoneGroup = milestoneRoot.GetComponent<CanvasGroup>();
        if (milestoneGroup == null) milestoneGroup = milestoneRoot.AddComponent<CanvasGroup>();
        milestoneGroup.interactable = false;
        milestoneGroup.blocksRaycasts = false;
        milestoneGroup.alpha = 0f;
        milestoneRoot.SetActive(false);
    }

}

public static class VfxSpriteFactory
{
    // The soft radial dot behind most of the game's glows. Named so it can be baked to an
    // asset and referenced by the serialized Main Menu.
    public const string SoftSpriteName = "Runtime Soft Sprite";
    public const string SoftParticleMaterialName = "Runtime Soft Particle";
    public const string GeometricParticleMaterialName = "Runtime Geometric Particle";

    private static Texture2D softTexture;
    private static Sprite softSprite;
    private static Texture2D sharpFlameTexture;
    private static Sprite sharpFlameSprite;
    private static Material particleMaterial;
    private static Material geometricParticleMaterial;
    private static Material trailMaterial;
    private static Mesh triangleMesh;
    private static Mesh diamondMesh;
    private static Mesh ringMesh;

    public static Sprite SoftSprite
    {
        get
        {
            if (softSprite != null) return softSprite;

            // Baked first, so the menu layers drawn with it can be serialized. Identical
            // pixels either way — see MenuBakedArt.
            softSprite = MenuBakedArt.Load(SoftSpriteName);
            if (softSprite != null) return softSprite;

            EnsureTexture();
            softSprite = Sprite.Create(softTexture, new Rect(0f, 0f, softTexture.width, softTexture.height),
                new Vector2(0.5f, 0.5f), softTexture.width, 0, SpriteMeshType.FullRect);
            softSprite.name = SoftSpriteName;
            return softSprite;
        }
    }

    public static Material ParticleMaterial
    {
        get
        {
            if (particleMaterial != null) return particleMaterial;

            // Baked first — see MenuBakedArt. The baked copy carries the same shader and
            // blend state the block below establishes.
            particleMaterial = MenuBakedArt.LoadMaterial(SoftParticleMaterialName);
            if (particleMaterial != null) return particleMaterial;

            EnsureTexture();

            // Sprites/Default, not URP's particle shader.
            //
            // This project renders through the URP 2D Renderer, and that renderer does
            // not draw the 3D "Universal Render Pipeline/Particles/Unlit" pass: it
            // discards the texture's alpha and the per-particle colour and fills the whole
            // quad opaque white. Every soft round particle in the game — ash, embers, lava
            // drops, sparkles — drew as a hard white square, largest and most obvious over
            // the menu's volcano.
            //
            // Stating the blend state (MakeTransparent, below) does not fix it. The blend
            // state was already correct while the squares were still white; the shader
            // itself has to be one the 2D renderer supports. That is the same
            // Sprites/Default the geometric particles have always used, which is exactly
            // why those were the only particles rendering correctly.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            particleMaterial = new Material(shader) { name = SoftParticleMaterialName };
            particleMaterial.mainTexture = softTexture;
            if (particleMaterial.HasProperty("_BaseMap"))
                particleMaterial.SetTexture("_BaseMap", softTexture);
            MakeTransparent(particleMaterial);
            return particleMaterial;
        }
    }

    /// A material built with `new Material(urpShader)` inherits the shader's defaults,
    /// and URP's particle shaders default to Opaque — which throws the texture's alpha
    /// away and leaves a hard square. This states the blend state instead.
    ///
    /// Necessary but not sufficient: under the URP 2D Renderer the blend state can be
    /// perfectly correct and the particle still draws as an opaque white quad, because
    /// the 2D renderer does not run URP's 3D particle pass at all. Picking a shader the
    /// 2D renderer supports is the other half. See ParticleMaterial above.
    public static void MakeTransparent(Material material)
    {
        if (material == null) return;

        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);   // Transparent
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);       // Alpha
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    public static Sprite SharpFlameSprite
    {
        get
        {
            EnsureSharpFlameTexture();
            if (sharpFlameSprite == null)
            {
                sharpFlameSprite = Sprite.Create(
                    sharpFlameTexture,
                    new Rect(0f, 0f, sharpFlameTexture.width, sharpFlameTexture.height),
                    new Vector2(0.5f, 0.5f),
                    sharpFlameTexture.width,
                    0,
                    SpriteMeshType.FullRect);
                sharpFlameSprite.name = "Runtime Sharp Flame";
            }
            return sharpFlameSprite;
        }
    }

    public static Material GeometricParticleMaterial
    {
        get
        {
            if (geometricParticleMaterial != null) return geometricParticleMaterial;

            // Baked first, so particle renderers in the serialized menu point at a real
            // material asset instead of one that only exists in Play Mode.
            geometricParticleMaterial = MenuBakedArt.LoadMaterial(GeometricParticleMaterialName);
            if (geometricParticleMaterial != null) return geometricParticleMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            geometricParticleMaterial = new Material(shader) { name = GeometricParticleMaterialName };
            geometricParticleMaterial.mainTexture = Texture2D.whiteTexture;
            return geometricParticleMaterial;
        }
    }

    public static Mesh TriangleMesh
    {
        get
        {
            if (triangleMesh == null)
            {
                triangleMesh = new Mesh { name = "Runtime VFX Triangle" };
                triangleMesh.vertices = new[]
                {
                    new Vector3(-0.55f, -0.42f, 0f),
                    new Vector3(0.58f, -0.30f, 0f),
                    new Vector3(-0.04f, 0.66f, 0f)
                };
                triangleMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                triangleMesh.triangles = new[] { 0, 2, 1 };
                triangleMesh.RecalculateBounds();
                triangleMesh.UploadMeshData(true);
            }
            return triangleMesh;
        }
    }

    public static Mesh DiamondMesh
    {
        get
        {
            if (diamondMesh == null)
            {
                diamondMesh = new Mesh { name = "Runtime VFX Diamond" };
                diamondMesh.vertices = new[]
                {
                    new Vector3(0f, 0.68f, 0f),
                    new Vector3(0.58f, 0f, 0f),
                    new Vector3(0f, -0.68f, 0f),
                    new Vector3(-0.58f, 0f, 0f)
                };
                diamondMesh.uv = new[]
                {
                    new Vector2(0.5f, 1f),
                    new Vector2(1f, 0.5f),
                    new Vector2(0.5f, 0f),
                    new Vector2(0f, 0.5f)
                };
                diamondMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                diamondMesh.RecalculateBounds();
                diamondMesh.UploadMeshData(true);
            }
            return diamondMesh;
        }
    }

    /// <summary>
    /// A soft-edged annulus of outer radius 0.5, so a particle of size S draws a
    /// ring exactly S across. Used for the crash shock front.
    ///
    /// The band is built from three concentric vertex rings — rim, centre, rim —
    /// with the vertex alpha falling to zero at both rims. An evenly lit band of
    /// constant width and hard edges is exactly what a targeting reticle looks
    /// like; tapering it leaves a pressure line that thins into the background
    /// instead. The taper costs 120 vertices and no extra draw call.
    /// </summary>
    public static Mesh RingMesh
    {
        get
        {
            if (ringMesh != null) return ringMesh;

            const int segments = 40;
            const float outerRadius = 0.5f;
            const float centreRadius = 0.479f;
            const float innerRadius = 0.458f;

            Vector3[] vertices = new Vector3[segments * 3];
            Vector2[] uv = new Vector2[segments * 3];
            Color[] colors = new Color[segments * 3];
            int[] triangles = new int[segments * 12];

            Color rim = new Color(1f, 1f, 1f, 0f);

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                int band = i * 3;
                vertices[band] = new Vector3(cos * innerRadius, sin * innerRadius, 0f);
                vertices[band + 1] = new Vector3(cos * centreRadius, sin * centreRadius, 0f);
                vertices[band + 2] = new Vector3(cos * outerRadius, sin * outerRadius, 0f);

                float u = i / (float)segments;
                uv[band] = new Vector2(u, 0f);
                uv[band + 1] = new Vector2(u, 0.5f);
                uv[band + 2] = new Vector2(u, 1f);

                colors[band] = rim;
                colors[band + 1] = Color.white;
                colors[band + 2] = rim;

                int next = (i + 1) % segments * 3;
                int t = i * 12;

                // Inner half of the band.
                triangles[t] = band;
                triangles[t + 1] = band + 1;
                triangles[t + 2] = next + 1;
                triangles[t + 3] = band;
                triangles[t + 4] = next + 1;
                triangles[t + 5] = next;

                // Outer half.
                triangles[t + 6] = band + 1;
                triangles[t + 7] = band + 2;
                triangles[t + 8] = next + 2;
                triangles[t + 9] = band + 1;
                triangles[t + 10] = next + 2;
                triangles[t + 11] = next + 1;
            }

            ringMesh = new Mesh { name = "Runtime VFX Ring" };
            ringMesh.vertices = vertices;
            ringMesh.uv = uv;
            ringMesh.colors = colors;
            ringMesh.triangles = triangles;
            ringMesh.RecalculateBounds();
            ringMesh.UploadMeshData(true);
            return ringMesh;
        }
    }

    public static Material TrailMaterial
    {
        get
        {
            if (trailMaterial != null) return trailMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            trailMaterial = new Material(shader) { name = "Runtime Rocket Trail" };
            return trailMaterial;
        }
    }

    static void EnsureTexture()
    {
        if (softTexture != null) return;

        const int size = 32;
        softTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Runtime Soft Circle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center) / radius;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 1.7f);
                softTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        softTexture.Apply(false, true);
    }

    static void EnsureSharpFlameTexture()
    {
        if (sharpFlameTexture != null) return;

        const int size = 32;
        sharpFlameTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Runtime Sharp Flame Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color32[] pixels = new Color32[size * size];
        float centerY = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float progress = Mathf.InverseLerp(1f, size - 2f, x);
                float halfHeight = Mathf.Lerp(0.4f, 12.8f, progress);
                float distance = Mathf.Abs(y - centerY);
                byte alpha = distance <= halfHeight
                    ? (byte)255
                    : distance <= halfHeight + 1f
                        ? (byte)Mathf.RoundToInt((halfHeight + 1f - distance) * 255f)
                        : (byte)0;
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        sharpFlameTexture.SetPixels32(pixels);
        sharpFlameTexture.Apply(false, true);
    }
}
