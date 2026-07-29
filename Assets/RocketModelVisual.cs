using UnityEngine;

// Drives the 3D hero rocket model that replaced the old sprite. The gameplay
// scripts keep talking to the root SpriteRenderer (bounds proxy, enabled flag,
// skin tint); this component mirrors that state onto the mesh and adds a
// subtle idle hover/bank so the rocket always feels alive.
[DisallowMultipleComponent]
public sealed class RocketModelVisual : MonoBehaviour
{
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
    private Color lastAppliedTint = Color.clear;
    private Color themeAmbient = Color.white;
    private Color themeAmbientTarget = Color.white;
    private float nextThemeSample;
    private float phase;

    void Awake()
    {
        stateSource = GetComponent<SpriteRenderer>();
        rocketController = GetComponent<RocketController>();
        if (model == null) model = transform.Find("RocketModel3D");
        if (model == null) return;

        renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        baseLocalPosition = model.localPosition;
        baseLocalRotation = model.localRotation;
        baseLocalScale = model.localScale;
        block = new MaterialPropertyBlock();
        phase = Random.value * 10f;

        if (stateSource != null)
        {
            foreach (MeshRenderer meshRenderer in renderers)
            {
                meshRenderer.sortingLayerID = stateSource.sortingLayerID;
                meshRenderer.sortingOrder = stateSource.sortingOrder;
            }
        }
    }

    void LateUpdate()
    {
        if (model == null) return;

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
        Color skinTint = stateSource != null ? stateSource.color : Color.white;
        Color finalTint = skinTint * Color.Lerp(Color.white, themeAmbient, themeLightStrength);
        if (finalTint != lastAppliedTint)
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

        // Hover bobs across the nose axis (root-local Y), bank rolls around it (root-local X),
        // wobble is a tiny heading correction (root-local Z), breathing scales the whole model.
        model.localPosition = baseLocalPosition + Vector3.up * hover;
        model.localRotation = Quaternion.AngleAxis(wobble, Vector3.forward)
            * Quaternion.AngleAxis(bank, Vector3.right)
            * baseLocalRotation;
        model.localScale = baseLocalScale * breathing;
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
