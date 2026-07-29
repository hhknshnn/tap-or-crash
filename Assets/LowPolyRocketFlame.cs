using UnityEngine;

[DisallowMultipleComponent]
public sealed class LowPolyRocketFlame : MonoBehaviour
{
    private static readonly Color OuterColor = new Color(1f, 0.25f, 0.045f, 0.94f);
    private static readonly Color InnerColor = new Color(0.25f, 0.88f, 1f, 0.98f);

    [Header("Flame Placement")]
    // Tuned for the 3D HeroRocket model: nozzle lip sits at local X -1.23.
    [SerializeField] private Vector3 flameLocalPosition = new Vector3(-1.22f, 0f, 0f);
    [SerializeField] private Vector3 flameLocalEulerAngles = Vector3.zero;
    [SerializeField] private Vector3 flameScale = new Vector3(1.45f, 0.72f, 1f);
    [SerializeField] private float modelForwardOffset;

    private RocketController rocket;
    private SpriteRenderer rocketRenderer;
    private ParticleSystem thrusterParticles;
    private Transform flameRoot;
    private SpriteRenderer outerFlame;
    private SpriteRenderer innerFlame;
    private float intensity;
    private float phase;
    private bool crashStopped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(Install);

    static void Install()
    {
        RocketController rocket = FindAnyObjectByType<RocketController>();
        if (rocket != null && rocket.GetComponent<LowPolyRocketFlame>() == null)
            rocket.gameObject.AddComponent<LowPolyRocketFlame>();
    }

    void Awake()
    {
        EnsureSetup();
    }

    void OnEnable()
    {
        EnsureSetup();
    }

    public void EnsureSetup()
    {
        rocket = GetComponent<RocketController>();
        rocketRenderer = GetComponent<SpriteRenderer>();
        thrusterParticles = rocket != null ? rocket.thrusterParticles : GetComponentInChildren<ParticleSystem>();

        ResolveOrBuildFlameLayers();
        ConfigureGeometricSparks();
    }

    void Update()
    {
        bool crashed = GameManager.isGameOver
            || rocket == null
            || !rocket.enabled
            || rocketRenderer == null
            || !rocketRenderer.enabled;
        if (crashed)
        {
            StopForCrash();
            return;
        }

        if (crashStopped && thrusterParticles != null && !thrusterParticles.isPlaying)
            thrusterParticles.Play();
        crashStopped = false;
        SetLayerVisibility(true);

        float emissionRate = GetEmissionRate();
        float targetIntensity = !GameManager.isGameStarted
            ? 0.22f
            : emissionRate > 20f ? 1f : emissionRate > 0.1f ? 0.58f : 0.34f;

        // Scaled time intentionally freezes the flame rhythm during pause.
        float delta = Time.deltaTime;
        if (delta <= 0f) return;

        intensity = Mathf.MoveTowards(intensity, targetIntensity, delta * 5.5f);
        phase += delta;

        float outerLength = 1f
            + Mathf.Sin(phase * 11.7f) * 0.10f
            + Mathf.Sin(phase * 6.1f + 1.4f) * 0.045f;
        float outerWidth = 1f + Mathf.Sin(phase * 8.3f + 0.7f) * 0.07f;
        float innerLength = 1f
            + Mathf.Sin(phase * 14.2f + 0.9f) * 0.08f
            + Mathf.Sin(phase * 5.4f) * 0.035f;

        SetLayerShape(outerFlame, intensity * outerLength, intensity * outerWidth);
        SetLayerShape(innerFlame, intensity * innerLength * 0.66f,
            intensity * (2f - outerWidth) * 0.5f);

        // Two incommensurate sines: brightness never repeats, never strobes (±5%).
        float brightness = 1f
            + Mathf.Sin(phase * 9.1f) * 0.035f
            + Mathf.Sin(phase * 4.3f + 1.2f) * 0.02f;

        Color outer = OuterColor;
        outer.a *= Mathf.Clamp01(intensity * 1.35f * brightness);
        outerFlame.color = outer;

        Color inner = InnerColor;
        inner.a *= Mathf.Clamp01(intensity * 1.5f * brightness);
        innerFlame.color = inner;
    }

    void ResolveOrBuildFlameLayers()
    {
        Transform firstRoot = null;
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != "LowPolyFlame_Root") continue;
            if (firstRoot == null) firstRoot = child;
            else Destroy(child.gameObject);
        }

        if (firstRoot == null)
        {
            GameObject root = new GameObject("LowPolyFlame_Root");
            firstRoot = root.transform;
            firstRoot.SetParent(transform, false);
        }

        flameRoot = firstRoot;
        flameRoot.gameObject.SetActive(true);
        flameRoot.localPosition = flameLocalPosition;
        flameRoot.localRotation = GetFlameLocalRotation();
        flameRoot.localScale = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(flameScale.x)),
            Mathf.Max(0.01f, Mathf.Abs(flameScale.y)),
            Mathf.Max(0.01f, Mathf.Abs(flameScale.z)));

        outerFlame = ResolveOrCreateLayer("LowPolyFlame_Outer",
            rocketRenderer != null ? rocketRenderer.sortingOrder - 2 : -2, OuterColor);
        innerFlame = ResolveOrCreateLayer("LowPolyFlame_Inner",
            rocketRenderer != null ? rocketRenderer.sortingOrder - 1 : -1, InnerColor);

        intensity = GameManager.isGameStarted ? 0.58f : 0.22f;
        SetLayerShape(outerFlame, intensity, intensity);
        SetLayerShape(innerFlame, intensity * 0.66f, intensity * 0.5f);
    }

    SpriteRenderer ResolveOrCreateLayer(string objectName, int sortingOrder, Color color)
    {
        Transform existing = flameRoot.Find(objectName);
        if (existing == null) return CreateLayer(objectName, sortingOrder, color);

        SpriteRenderer renderer = existing.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = existing.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = VfxSpriteFactory.SharpFlameSprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        renderer.enabled = true;
        if (rocketRenderer != null) renderer.sortingLayerID = rocketRenderer.sortingLayerID;
        return renderer;
    }

    SpriteRenderer CreateLayer(string objectName, int sortingOrder, Color color)
    {
        GameObject layer = new GameObject(objectName);
        layer.transform.SetParent(flameRoot, false);
        layer.transform.localPosition = Vector3.zero;
        layer.transform.localRotation = Quaternion.identity;

        SpriteRenderer renderer = layer.AddComponent<SpriteRenderer>();
        renderer.sprite = VfxSpriteFactory.SharpFlameSprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        if (rocketRenderer != null) renderer.sortingLayerID = rocketRenderer.sortingLayerID;
        return renderer;
    }

    static void SetLayerShape(SpriteRenderer layer, float length, float width)
    {
        if (layer == null) return;
        length = Mathf.Max(0.01f, length);
        width = Mathf.Max(0.01f, width);

        // SharpFlameSprite has its wide base on local +X. Keeping that edge at
        // the root makes every flicker stay attached to the engine exit.
        layer.transform.localPosition = new Vector3(-length * 0.5f, 0f, 0f);
        layer.transform.localScale = new Vector3(length, width, 1f);
    }

    Quaternion GetFlameLocalRotation()
    {
        Vector3 euler = flameLocalEulerAngles;
        euler.z += modelForwardOffset;
        return Quaternion.Euler(euler);
    }

    void ConfigureGeometricSparks()
    {
        if (thrusterParticles == null) return;

        thrusterParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // The scene emitter used to sit at the baked sprite flame's tip. Move it
        // to the same engine-exit point without changing its existing parent.
        Transform particleTransform = thrusterParticles.transform;
        if (particleTransform.parent != null)
        {
            Vector3 engineWorldPosition = transform.TransformPoint(flameLocalPosition);
            particleTransform.localPosition = particleTransform.parent.InverseTransformPoint(engineWorldPosition);
        }

        ParticleSystem.MainModule main = thrusterParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.useUnscaledTime = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 14;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.43f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.32f, 0.32f);
        main.startColor = Color.white;

        ParticleSystem.EmissionModule emission = thrusterParticles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = thrusterParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 7f;
        shape.radius = 0.025f;
        // RocketController aligns rocket-local +X with travel. Apply the optional
        // model offset, then convert its opposite into this child system's space.
        Vector3 flameRearLocal = GetFlameLocalRotation() * Vector3.left;
        Vector3 worldRear = transform.TransformDirection(flameRearLocal);
        Vector3 localRear = particleTransform.InverseTransformDirection(worldRear).normalized;
        shape.rotation = Quaternion.FromToRotation(Vector3.forward, localRear).eulerAngles;

        ParticleSystem.NoiseModule noise = thrusterParticles.noise;
        noise.enabled = false;

        ParticleSystem.SizeOverLifetimeModule sizeOverLife = thrusterParticles.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(
            1f,
            new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.62f, 0.68f),
                new Keyframe(1f, 0f)));

        ParticleSystem.ColorOverLifetimeModule colorOverLife = thrusterParticles.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.45f, 0.92f, 1f), 0f),
                new GradientColorKey(new Color(1f, 0.52f, 0.08f), 0.48f),
                new GradientColorKey(new Color(1f, 0.16f, 0.04f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.92f, 0f),
                new GradientAlphaKey(0.66f, 0.62f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLife.color = gradient;

        ParticleSystemRenderer renderer = thrusterParticles.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = VfxSpriteFactory.TriangleMesh;
            renderer.sharedMaterial = VfxSpriteFactory.GeometricParticleMaterial;
            renderer.sortingOrder = rocketRenderer != null ? rocketRenderer.sortingOrder - 3 : -3;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // Keep the system alive with zero emission; RocketController's existing rates drive it later.
        thrusterParticles.Play();
    }

    float GetEmissionRate()
    {
        if (thrusterParticles == null) return 0f;
        ParticleSystem.MinMaxCurve rate = thrusterParticles.emission.rateOverTime;
        return rate.constantMax;
    }

    void StopForCrash()
    {
        SetLayerVisibility(false);
        if (crashStopped) return;
        crashStopped = true;
        if (thrusterParticles != null)
            thrusterParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void SetLayerVisibility(bool visible)
    {
        if (outerFlame != null) outerFlame.enabled = visible;
        if (innerFlame != null) innerFlame.enabled = visible;
    }
}
