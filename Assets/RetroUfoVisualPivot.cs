using UnityEngine;

// Retro UFO presentation is deliberately split into two rotation authorities.
//
// HeadingPivot (this transform) follows the gameplay/launch-guide heading only.
// StylePivot is its child and owns the small 3D beauty tilt and idle motion only.
// NoseSocket and EngineSocket remain siblings of StylePivot: their positions are
// projected from the styled model, but their rotations stay on the true heading.
// The guide and exhaust therefore cannot inherit the cosmetic pitch/yaw/roll.
[DisallowMultipleComponent]
public sealed class RetroUfoVisualPivot : MonoBehaviour
{
    [Header("Separated Hierarchy")]
    [SerializeField] private Transform stylePivot;
    [SerializeField] private Transform noseSocket;
    [SerializeField] private Transform engineSocket;

    [Header("Gameplay Style")]
    [SerializeField] private float basePitch = 18f;
    [SerializeField] private float baseYaw = -8f;
    [SerializeField] private float baseRoll;

    [Header("Gameplay Idle Motion")]
    [SerializeField] private float idlePitchAmplitude = 1.7f;
    [SerializeField] private float idleYawAmplitude = 2f;
    [SerializeField] private float idleRollAmplitude = 1.4f;
    [SerializeField] private float idleHoverAmplitude = 0.045f;
    [SerializeField] private float cycleDuration = 2.8f;

    // Richer profile used only while the Main Menu drives this pivot (see
    // SetMenuHeadingOverride/SetMenuPresentationBlend). MenuHeroRocket blends this
    // back down to the plain gameplay amplitudes above during hand-over, so the
    // approved gameplay idle motion above is never touched directly.
    [Header("Main Menu Idle Motion")]
    [SerializeField] private float menuIdlePitchAmplitude = 1.9f;
    [SerializeField] private float menuIdleYawAmplitude = 2.3f;
    [SerializeField] private float menuIdleRollAmplitude = 2.6f;
    [SerializeField] private float menuIdleHoverAmplitude = 0.055f;
    [SerializeField] private float menuIdleSwayAmplitude = 0.045f;
    [SerializeField] private float menuCycleDuration = 2.9f;

    [Header("Flight State")]
    [SerializeField, Range(0f, 1f)] private float flightHoverMultiplier = 0.2f;
    [SerializeField, Range(0f, 1f)] private float flightMotionMultiplier = 0.35f;

    [Header("Styled Anchor Positions")]
    [Tooltip("Front-center point in StylePivot space, before the presentation tilt.")]
    [SerializeField] private Vector3 noseAnchor = new Vector3(1.49f, 0f, 0f);
    [Tooltip("Rear center-thruster exit in StylePivot space, before the presentation tilt.")]
    [SerializeField] private Vector3 engineAnchor = new Vector3(-2.034f, 0f, 0.3046f);

    [Header("Shop Beauty Pose")]
    [SerializeField] private Vector3 shopStyleEuler = new Vector3(30f, -14f, 0f);

    private RocketController rocket;
    private Vector3 baseHeadingPosition;
    private Vector3 baseStylePosition;
    private float phase;
    private bool shopPreview;

    // Menu-only heading authority. When active, ApplyHeading follows the Main Menu
    // orbit tangent instead of the gameplay launch-guide direction; menuPresentationBlend
    // (1 = full menu idle profile, 0 = gameplay idle profile) is driven down to 0 by
    // MenuHeroRocket during hand-over, then ClearMenuHeadingOverride returns full
    // authority to the gameplay heading on the same frame RocketController resumes.
    private bool headingOverrideActive;
    private Vector3 headingOverrideDirection = Vector3.right;
    private float menuPresentationBlend;

    void Awake()
    {
        rocket = GetComponentInParent<RocketController>();
        ResolveHierarchy();
        CaptureBasePose();
    }

    void OnEnable()
    {
        rocket = GetComponentInParent<RocketController>();
        ResolveHierarchy();
        CaptureBasePose();
        phase = Random.value * 10f;
        shopPreview = false;
        headingOverrideActive = false;
        menuPresentationBlend = 0f;
        ApplyPose(0f, 0f, 0f, 0f, 0f);
    }

    // Called every frame by MenuHeroRocket while it owns the ship, with the analytical
    // tangent of the Main Menu orbit ellipse. Never a screen-facing or stale direction.
    public void SetMenuHeadingOverride(Vector3 worldDirection)
    {
        if (worldDirection.sqrMagnitude <= Mathf.Epsilon) return;
        headingOverrideActive = true;
        headingOverrideDirection = worldDirection;
    }

    // 1 = full Main Menu idle profile, 0 = approved gameplay idle profile. MenuHeroRocket
    // ramps this down over its own short hand-over window.
    public void SetMenuPresentationBlend(float blend)
    {
        menuPresentationBlend = Mathf.Clamp01(blend);
    }

    // Called once when RocketController takes the ship back. From this frame on,
    // ApplyHeading reads the gameplay launch-guide direction again.
    public void ClearMenuHeadingOverride()
    {
        headingOverrideActive = false;
        menuPresentationBlend = 0f;
    }

    void Update()
    {
        if (shopPreview || stylePivot == null) return;

        float delta = Time.deltaTime;
        if (delta > 0f) phase += delta;

        bool orbiting = rocket == null || rocket.IsOrbiting;
        float motionScale = orbiting ? 1f : flightMotionMultiplier;
        float hoverScale = orbiting ? 1f : flightHoverMultiplier;

        // Only ever blended in while the Main Menu holds the heading override; zero
        // blend reproduces the approved gameplay amplitudes and cycle exactly.
        float menuBlend = headingOverrideActive ? menuPresentationBlend : 0f;
        float pitchAmplitude = Mathf.Lerp(idlePitchAmplitude, menuIdlePitchAmplitude, menuBlend);
        float yawAmplitude = Mathf.Lerp(idleYawAmplitude, menuIdleYawAmplitude, menuBlend);
        float rollAmplitude = Mathf.Lerp(idleRollAmplitude, menuIdleRollAmplitude, menuBlend);
        float hoverAmplitude = Mathf.Lerp(idleHoverAmplitude, menuIdleHoverAmplitude, menuBlend);
        float swayAmplitude = menuIdleSwayAmplitude * menuBlend;
        float blendedCycleDuration = Mathf.Lerp(cycleDuration, menuCycleDuration, menuBlend);

        float frequency = 1f / Mathf.Max(0.1f, blendedCycleDuration);
        float cycle = phase * frequency * Mathf.PI * 2f;

        float pitch = Mathf.Sin(cycle) * pitchAmplitude * motionScale;
        float yaw = Mathf.Sin(cycle * 0.83f + 1.3f) * yawAmplitude * motionScale;
        float roll = Mathf.Sin(cycle * 1.21f + 2.6f) * rollAmplitude * motionScale;
        float hover = Mathf.Sin(cycle * 0.6f + 0.7f) * hoverAmplitude * hoverScale;
        float sway = Mathf.Sin(cycle * 0.71f + 4.1f) * swayAmplitude * motionScale;
        ApplyPose(pitch, yaw, roll, hover, sway);
    }

    // The preview renderer calls this immediately after instantiation, before it
    // renders its one frame. Gameplay and shop therefore do not share a forced pose.
    public void ApplyShopPreviewPose()
    {
        ResolveHierarchy();
        shopPreview = true;
        transform.localPosition = baseHeadingPosition;
        transform.localRotation = Quaternion.identity;
        if (stylePivot != null)
        {
            stylePivot.localPosition = baseStylePosition;
            stylePivot.localRotation = Quaternion.Euler(shopStyleEuler);
        }
        SyncSockets();
    }

    void ApplyPose(float pitchOffset, float yawOffset, float rollOffset, float hover, float sway)
    {
        // Only the prospective launch/flight direction owns gameplay-facing Z.
        // No screen-space clamp or 180-degree counter-rotation is allowed here.
        // Sway/hover are the pre-heading local axes (Main Menu only; sway is 0 in gameplay).
        transform.localPosition = baseHeadingPosition + Vector3.up * hover + Vector3.right * sway;
        ApplyHeading();

        stylePivot.localPosition = baseStylePosition;
        stylePivot.localRotation = Quaternion.Euler(
            basePitch + pitchOffset,
            baseYaw + yawOffset,
            baseRoll + rollOffset);
        SyncSockets();
    }

    void ApplyHeading()
    {
        Vector3 direction;
        if (headingOverrideActive)
        {
            // Main Menu tangent authority. Takes over from the gameplay launch-guide
            // direction, which stays frozen while RocketController is asleep behind
            // MenuHeroRocket and would otherwise read as a stale, near-static heading.
            direction = headingOverrideDirection;
        }
        else if (rocket != null)
        {
            direction = rocket.PresentationHeadingDirection;
            if (direction.sqrMagnitude <= Mathf.Epsilon) direction = rocket.transform.right;
        }
        else
        {
            transform.localRotation = Quaternion.identity;
            return;
        }

        float heading = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, heading);
    }

    void SyncSockets()
    {
        SyncSocket(noseSocket, noseAnchor);
        SyncSocket(engineSocket, engineAnchor);
    }

    void SyncSocket(Transform socket, Vector3 styledAnchor)
    {
        if (socket == null || stylePivot == null) return;

        // Follow the styled mesh's attachment position, while preserving the pure
        // HeadingPivot rotation. This keeps the exhaust in the 2D gameplay plane.
        socket.position = stylePivot.TransformPoint(styledAnchor);
        socket.rotation = transform.rotation;
        socket.localScale = Vector3.one;
    }

    void ResolveHierarchy()
    {
        if (stylePivot == null) stylePivot = transform.Find("StylePivot");
        if (noseSocket == null) noseSocket = transform.Find("NoseSocket");
        if (engineSocket == null) engineSocket = transform.Find("EngineSocket");
    }

    void CaptureBasePose()
    {
        baseHeadingPosition = transform.localPosition;
        if (stylePivot != null) baseStylePosition = stylePivot.localPosition;
    }
}
