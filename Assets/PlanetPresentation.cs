using UnityEngine;

// Lightweight shared orbit halo. Its radius is also the single source of truth
// used by RocketController when deciding where the rocket first touches a planet.
public sealed class PlanetPresentation : MonoBehaviour
{
    public const float OrbitRingRadiusRatio = 1.28f;

    // Tema eşleşmeyen gezegenlerin hâle rengi.
    static readonly Color DefaultAuraColor = new Color(0.22f, 0.73f, 1f, 0.10f);

    private SpriteRenderer halo;
    private float phase;

    public static void Attach(Transform planet)
    {
        if (planet == null) return;

        // Gezegen temasına özel dekoratif katman; hangi tema olduğu burada bilinmez.
        PlanetAmbience.Attach(planet);

        if (planet.GetComponent<PlanetPresentation>() != null) return;
        planet.gameObject.AddComponent<PlanetPresentation>();
    }

    void Start()
    {
        SpriteRenderer planetRenderer = GetComponent<SpriteRenderer>();
        if (planetRenderer == null) { enabled = false; return; }

        GameObject go = new GameObject("PlanetAura");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        halo = go.AddComponent<SpriteRenderer>();
        halo.sprite = VfxSpriteFactory.SoftSprite;
        halo.color = PlanetAmbience.AuraColorFor(transform, DefaultAuraColor);
        halo.sortingLayerID = planetRenderer.sortingLayerID;
        halo.sortingOrder = planetRenderer.sortingOrder - 1;

        float worldSize = GetOrbitRingRadius(transform) * 2f;
        Vector3 scale = transform.lossyScale;
        float parentScale = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
        go.transform.localScale = Vector3.one * (worldSize / parentScale);

        GameObject triggerObject = new GameObject("OrbitCaptureTrigger");
        triggerObject.transform.SetParent(transform, false);
        triggerObject.transform.localPosition = Vector3.zero;
        CircleCollider2D captureTrigger = triggerObject.AddComponent<CircleCollider2D>();
        captureTrigger.isTrigger = true;
        captureTrigger.radius = GetOrbitRingRadius(transform) / parentScale;

        phase = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        if (halo == null) return;
        float wave = (Mathf.Sin(Time.unscaledTime * 1.7f + phase) + 1f) * 0.5f;
        Color color = halo.color;
        color.a = Mathf.Lerp(0.065f, 0.13f, wave);
        halo.color = color;
    }

    public static float GetBodyRadius(Transform planet)
    {
        if (planet == null) return 0.4f;

        SpriteRenderer renderer = planet.GetComponent<SpriteRenderer>();
        if (renderer == null) return 0.4f;

        return Mathf.Max(renderer.bounds.extents.x, renderer.bounds.extents.y);
    }

    public static float GetOrbitRingRadius(Transform planet)
    {
        return GetBodyRadius(planet) * OrbitRingRadiusRatio;
    }
}
