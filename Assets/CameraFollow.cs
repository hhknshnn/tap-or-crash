using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public struct ContinueState
    {
        public Vector3 position;
        public Vector3 velocity;
        public float orthographicSize;
    }

    public Transform target;
    public float smoothSpeed = 5f;

    // Kameranın hedefin ne kadar önüne bakacağı (roket yukarı uçarken kamera öne baksın)
    public float lookAheadY = 2f;
    private Vector3 velocity = Vector3.zero; // SmoothDamp için dahili hız değişkeni

    [Header("Kamera Geri Bildirimi")]
    [SerializeField] private float landingKickDuration = 0.24f;
    [SerializeField] private float landingKickStrength = 0.16f;
    [SerializeField] private float precisionKickMultiplier = 1.45f;
    [SerializeField] private float crashKickDuration = 0.42f;
    [SerializeField] private float crashKickStrength = 0.32f;
    [SerializeField] private float landingZoom = 0.18f;

    private Camera controlledCamera;
    private float baseOrthographicSize;
    private float kickTimer;
    private float kickDuration;
    private float kickStrength;
    private float zoomStrength;

    void Awake()
    {
        controlledCamera = GetComponent<Camera>();
        if (controlledCamera != null)
            baseOrthographicSize = controlledCamera.orthographicSize;
    }


    void LateUpdate()
    {
        if (target == null) return;

        Vector2 kickOffset = Vector2.zero;
        float envelope = 0f;
        if (kickTimer > 0f)
        {
            kickTimer = Mathf.Max(0f, kickTimer - Time.unscaledDeltaTime);
            envelope = kickDuration > 0f ? kickTimer / kickDuration : 0f;
            float phase = Time.unscaledTime * 52f;
            kickOffset = new Vector2(Mathf.Sin(phase), Mathf.Cos(phase * 0.83f))
                * kickStrength * envelope * envelope;
        }

        Vector3 targetPos = new Vector3(kickOffset.x, target.position.y + lookAheadY + kickOffset.y, -10f);
        // SmoothDamp: Lerp'ten farklı olarak frame rate'ten bağımsız çalışır
        transform.position = Vector3.SmoothDamp(transform.position, targetPos, ref velocity, 1f / smoothSpeed);

        if (controlledCamera != null)
        {
            float targetSize = baseOrthographicSize - zoomStrength * envelope;
            controlledCamera.orthographicSize = Mathf.Lerp(
                controlledCamera.orthographicSize,
                targetSize,
                1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        }
    }

    public void PlayLandingKick(bool precisionLanding)
    {
        StartKick(
            landingKickDuration,
            landingKickStrength * (precisionLanding ? precisionKickMultiplier : 1f),
            landingZoom * (precisionLanding ? 1.15f : 1f));
    }

    public void PlayCrashKick()
    {
        StartKick(crashKickDuration, crashKickStrength, landingZoom * 1.35f);
    }

    public ContinueState CaptureContinueState()
    {
        return new ContinueState
        {
            position = transform.position,
            velocity = velocity,
            orthographicSize = controlledCamera != null
                ? controlledCamera.orthographicSize
                : baseOrthographicSize
        };
    }

    public void RestoreContinueState(ContinueState state)
    {
        transform.position = state.position;
        velocity = state.velocity;
        kickTimer = 0f;
        kickDuration = 0f;
        kickStrength = 0f;
        zoomStrength = 0f;
        if (controlledCamera != null)
            controlledCamera.orthographicSize = state.orthographicSize;
    }

    void StartKick(float duration, float strength, float zoom)
    {
        kickDuration = Mathf.Max(0.01f, duration);
        kickTimer = kickDuration;
        kickStrength = strength;
        zoomStrength = zoom;
    }
}
