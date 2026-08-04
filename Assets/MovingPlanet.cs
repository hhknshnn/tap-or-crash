using UnityEngine;

// Moves the complete authoritative planet rig. Because the component lives on
// the planet root, its mesh, collider, capture trigger, ambience, ring target,
// tether target and RocketController coordinates remain one coherent transform.
[DisallowMultipleComponent]
public sealed class MovingPlanet : MonoBehaviour
{
    [SerializeField] private int planetNumber = MovingOrbitProgression.ActivationPlanet;
    [SerializeField] private float horizontalAmplitude;
    [SerializeField] private float verticalAmplitude;
    [SerializeField] private float period = 6f;
    [SerializeField] private float phase;
    [SerializeField] private float horizontalSign = 1f;
    [SerializeField] private float verticalSign = 1f;

    private Vector3 origin;
    private float motionClock;
    private bool configured;

    public float HorizontalAmplitude => horizontalAmplitude;
    public float VerticalAmplitude => verticalAmplitude;
    public int PlanetNumber => planetNumber;

    public void Configure(int number, Camera camera)
    {
        planetNumber = Mathf.Max(1, number);
        MovingOrbitProgression.Evaluate(planetNumber, camera,
            out horizontalAmplitude, out verticalAmplitude, out period);

        // Stable hashes: no frame-by-frame randomness and identical motion for
        // a given planet number across Continue, pause and device aspect.
        uint hash = (uint)planetNumber * 2654435761u;
        phase = (hash % 1009u) / 1009f * Mathf.PI * 2f;
        horizontalSign = (hash & 1u) == 0u ? 1f : -1f;
        verticalSign = (hash & 2u) == 0u ? 1f : -1f;
        origin = transform.position;
        motionClock = 0f;
        configured = true;
    }

    private void Start()
    {
        if (!configured)
        {
            Configure(planetNumber, Camera.main);
            origin = transform.position;
        }
    }

    public void SetSafeOrigin(Vector3 safeOrigin)
    {
        origin = safeOrigin;
        transform.position = safeOrigin;
    }

    private void Update()
    {
        if (GameManager.isGameOver || !GameManager.isGameStarted) return;
        if (GameManager.instance == null
            || !MovingOrbitProgression.IsActiveForScore(GameManager.instance.GetScore())) return;
        if (WorldTransitionManager.IsPendingOrPlaying
            || PresentationGate.IsActive(PresentationGate.Kind.GameplayNotice)) return;
        if (Time.deltaTime <= 0f) return;

        motionClock += Time.deltaTime;
        float angle = phase + motionClock / Mathf.Max(0.01f, period) * Mathf.PI * 2f;
        float x = Mathf.Sin(angle) * horizontalAmplitude * horizontalSign;
        float y = Mathf.Sin(angle * 0.73f + phase * 0.37f) * verticalAmplitude * verticalSign;
        transform.position = origin + new Vector3(x, y, 0f);
    }
}
