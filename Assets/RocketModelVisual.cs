using UnityEngine;

// Drives the 3D hero rocket model that replaced the old sprite. The gameplay
// scripts keep talking to the root SpriteRenderer (bounds proxy, enabled flag,
// skin tint); this component mirrors that state onto the mesh and adds a
// subtle idle hover/bank so the rocket always feels alive.
[DisallowMultipleComponent]
public sealed class RocketModelVisual : MonoBehaviour
{
    // One presentation-space authority for every gameplay rocket. The gameplay
    // root keeps its authored scale because its collider and orbit calculations
    // depend on it; active model holders are compensated to this world scale.
    public const float SharedGameplayVisualScale = 0.40f;

    [Header("Model")]
    [SerializeField] private Transform model;

    [Header("Idle Motion")]
    [SerializeField] private float hoverAmplitude = 0.05f;
    [SerializeField] private float hoverFrequency = 1.4f;
    [SerializeField] private float bankAmplitude = 1.8f;
    [SerializeField] private float bankFrequency = 0.9f;
    [SerializeField] private float noseWobbleAmplitude = 1.0f;
    [SerializeField] private float breathingAmplitude = 0.006f;

    [Header("Theme Light")]
    [SerializeField, Range(0f, 0.4f)] private float themeLightStrength = 0.14f;

    private SpriteRenderer stateSource;
    private RocketController rocketController;
    private MeshRenderer[] renderers;
    private MaterialPropertyBlock block;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 baseLocalScale;
    private Vector3 authoredModelLocalScale;
    private Vector3 defaultModelAuthoredLocalScale;
    private Color lastAppliedTint = Color.clear;
    private Color themeAmbient = Color.white;
    private Color themeAmbientTarget = Color.white;
    private float nextThemeSample;
    private float phase;
    private bool runtimeInitialized;
    private Transform defaultModel;
    private GameObject replacementInstance;
    private GameObject replacementPrefab;
    private Transform activeEngineSocket;
    private Transform activeNoseSocket;
    private RetroUfoVisualPivot activePresentation;
    private LowPolyRocketFlame boundFlame;
    private bool applySkinTint = true;

    public Transform ActiveEngineSocket => activeEngineSocket;
    public Transform ActiveNoseSocket => activeNoseSocket;
    public RetroUfoVisualPivot ActiveRetroUfoPresentation => activePresentation;

    void Awake()
    {
        InitializeRuntimeState();
    }

    void OnEnable()
    {
        // Awake is not repeated when Unity recompiles scripts and continues Play
        // Mode. Rebuild transient renderer state before LateUpdate resumes.
        InitializeRuntimeState();
    }

    void InitializeRuntimeState()
    {
        stateSource = GetComponent<SpriteRenderer>();
        rocketController = GetComponent<RocketController>();
        if (model == null) model = transform.Find("RocketModel3D");
        if (defaultModel == null && model != null)
        {
            defaultModel = model;
            defaultModelAuthoredLocalScale = model.localScale;
            authoredModelLocalScale = defaultModelAuthoredLocalScale;
        }
        if (model == null) return;

        renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        baseLocalPosition = model.localPosition;
        baseLocalRotation = model.localRotation;
        UpdateSharedVisualScale();
        block = new MaterialPropertyBlock();
        if (!runtimeInitialized)
            phase = Random.value * 10f;
        runtimeInitialized = true;

        if (stateSource != null)
        {
            foreach (MeshRenderer meshRenderer in renderers)
            {
                meshRenderer.sortingLayerID = stateSource.sortingLayerID;
                meshRenderer.sortingOrder = stateSource.sortingOrder;
            }
        }

        BindFlameSocket();
    }

    public void SetReplacementModel(GameObject prefab)
    {
        InitializeRuntimeState();
        if (defaultModel == null) return;
        if (replacementPrefab == prefab
            && (prefab == null || replacementInstance != null))
        {
            BindFlameSocket();
            return;
        }

        if (replacementInstance != null)
        {
            replacementInstance.SetActive(false);
            Destroy(replacementInstance);
            replacementInstance = null;
        }

        replacementPrefab = prefab;
        activeEngineSocket = null;
        activeNoseSocket = null;
        activePresentation = null;
        if (prefab == null)
        {
            model = defaultModel;
            authoredModelLocalScale = defaultModelAuthoredLocalScale;
            applySkinTint = true;
            defaultModel.gameObject.SetActive(true);
        }
        else
        {
            defaultModel.gameObject.SetActive(false);
            replacementInstance = Instantiate(prefab, transform, false);
            replacementInstance.name = prefab.name + "_ReplacementVisual";
            Transform replacement = replacementInstance.transform;
            replacement.localPosition = Vector3.zero;
            replacement.localRotation = Quaternion.identity;
            authoredModelLocalScale = Vector3.one;
            model = replacement;
            applySkinTint = false;
            activeEngineSocket = FindDeepChild(replacement, "EngineSocket");
            activeNoseSocket = FindDeepChild(replacement, "NoseSocket");
            activePresentation = replacement.GetComponentInChildren<RetroUfoVisualPivot>(true);
            if (activeEngineSocket == null)
                Debug.LogError(prefab.name + " replacement visual has no EngineSocket.", replacementInstance);
            if (activePresentation != null && activeNoseSocket == null)
                Debug.LogError(prefab.name + " replacement visual has no NoseSocket.", replacementInstance);
        }

        renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        baseLocalPosition = model.localPosition;
        baseLocalRotation = model.localRotation;
        UpdateSharedVisualScale();
        lastAppliedTint = Color.clear;

        if (stateSource != null)
        {
            foreach (MeshRenderer meshRenderer in renderers)
            {
                meshRenderer.sortingLayerID = stateSource.sortingLayerID;
                meshRenderer.sortingOrder = stateSource.sortingOrder;
            }
        }

        BindFlameSocket();
    }

    void LateUpdate()
    {
        if (model == null) return;
        BindFlameSocket();

        // Crash/respawn: gameplay toggles the root SpriteRenderer.
        bool visible = stateSource == null || stateSource.enabled;
        if (model.gameObject.activeSelf != visible)
            model.gameObject.SetActive(visible);
        if (!visible) return;

        // Theme presentation light: a whisper of the nearest world's aura color
        // (warm for Natural, cool for Ice, orange for Lava). Sampled sparsely,
        // eased constantly, so world transitions glide instead of popping.
        UpdateThemeAmbient();
        themeAmbient = Color.Lerp(themeAmbient, themeAmbientTarget, Time.deltaTime * 1.5f);

        // Skins keep writing tints to the sprite color; theme light multiplies on top.
        Color skinTint = applySkinTint && stateSource != null ? stateSource.color : Color.white;
        Color finalTint = skinTint * Color.Lerp(Color.white, themeAmbient, themeLightStrength);
        if (applySkinTint && finalTint != lastAppliedTint)
        {
            lastAppliedTint = finalTint;
            foreach (MeshRenderer meshRenderer in renderers)
            {
                meshRenderer.GetPropertyBlock(block);
                block.SetColor("_Color", finalTint);
                meshRenderer.SetPropertyBlock(block);
            }
        }

        // Scaled time so pause freezes the idle motion with the rest of the game.
        float delta = Time.deltaTime;
        if (delta <= 0f) return;
        phase += delta;

        // All channels layer two incommensurate sines so nothing ever loops visibly.
        float hover = (Mathf.Sin(phase * hoverFrequency * Mathf.PI * 2f) * 0.7f
            + Mathf.Sin(phase * hoverFrequency * 0.37f * Mathf.PI * 2f + 2.1f) * 0.3f)
            * hoverAmplitude;
        float bank = Mathf.Sin(phase * bankFrequency * Mathf.PI * 2f) * bankAmplitude;
        // Two incommensurate sines so the nose corrections never settle into a loop.
        float wobble = (Mathf.Sin(phase * 2.3f) * 0.6f + Mathf.Sin(phase * 0.7f + 1.7f) * 0.4f)
            * noseWobbleAmplitude;
        float breathing = 1f + Mathf.Sin(phase * 0.55f * Mathf.PI * 2f) * breathingAmplitude;
        UpdateSharedVisualScale();

        // Hover bobs across the nose axis (root-local Y), bank rolls around it (root-local X),
        // wobble is a tiny heading correction (root-local Z), breathing scales the whole model.
        // Retro UFO owns a separated HeadingPivot/StylePivot hierarchy. Applying
        // this generic bank to its replacement root would rotate its sockets too
        // and recreate the exact competing-authority bug the hierarchy prevents.
        if (activePresentation != null)
        {
            model.localPosition = baseLocalPosition;
            model.localRotation = baseLocalRotation;
        }
        else
        {
            model.localPosition = baseLocalPosition + Vector3.up * hover;
            model.localRotation = Quaternion.AngleAxis(wobble, Vector3.forward)
                * Quaternion.AngleAxis(bank, Vector3.right)
                * baseLocalRotation;
        }
        model.localScale = baseLocalScale * breathing;
    }

    void UpdateSharedVisualScale()
    {
        if (model == null) return;

        // Gameplay is 2D and the authored root is uniformly scaled in X/Y. Using
        // the planar geometric mean keeps the model's effective world scale at
        // exactly 0.40 without touching the root, its collider, or orbit math.
        Vector3 parentScale = transform.lossyScale;
        float planarScale = Mathf.Sqrt(Mathf.Max(0.0001f,
            Mathf.Abs(parentScale.x * parentScale.y)));
        baseLocalScale = authoredModelLocalScale * (SharedGameplayVisualScale / planarScale);
    }

    void BindFlameSocket()
    {
        LowPolyRocketFlame flame = GetComponent<LowPolyRocketFlame>();
        if (flame == null || (flame == boundFlame && flame.ActiveEngineSocket == activeEngineSocket)) return;
        boundFlame = flame;
        boundFlame.SetEngineSocket(activeEngineSocket);
    }

    // Cat/Dog and Retro UFO place anchors at different hierarchy depths, so the
    // lookup must walk the whole replacement rather than assume a fixed parent.
    static Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeepChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    void UpdateThemeAmbient()
    {
        if (rocketController == null || Time.unscaledTime < nextThemeSample) return;
        nextThemeSample = Time.unscaledTime + 0.5f;

        Transform nearest = null;
        float bestSqr = float.MaxValue;
        foreach (Transform planet in rocketController.planets)
        {
            if (planet == null) continue;
            float sqr = (planet.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; nearest = planet; }
        }

        PlanetAmbienceTheme theme = nearest != null ? PlanetAmbience.ResolveTheme(nearest) : null;
        if (theme == null) { themeAmbientTarget = Color.white; return; }

        // Keep it a light: never darkens, only leans the white toward the aura hue.
        Color aura = theme.AuraColor;
        float max = Mathf.Max(aura.r, Mathf.Max(aura.g, aura.b));
        themeAmbientTarget = max > 0.001f
            ? new Color(aura.r / max, aura.g / max, aura.b / max, 1f)
            : Color.white;
    }
}
