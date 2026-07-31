using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Breaks the rocket apart on impact using the Blender-authored debris chunks.
///
/// The source FBX is modelled in centimetre-scale units — assembled it measures
/// roughly 150 x 46 x 113 — while the live gameplay rocket is under two world
/// units tall. Nothing here hardcodes that ratio: the chunk geometry and the
/// spread distance are both derived at spawn time from the measured prefab
/// bounds and the rocket's own world size, so re-exporting the mesh at any scale
/// needs no code change.
/// </summary>
public sealed class CrashDebrisPresentation : MonoBehaviour
{
    // ─── Break-up timing — all driven by unscaled time ───────────────────────
    //
    // The beat is: a held impact frame, a slow separation, an accelerating
    // tumble, then a short fade-out. Nothing here touches Time.timeScale; the
    // slow-motion impression comes from the shape of the travel curve alone.

    /// <summary>Frozen frame on impact, before anything separates.</summary>
    private const float ImpactHold = 0.05f;

    /// <summary>Length of the break-up itself, after the hold.</summary>
    private const float SeparationDuration = 0.73f;

    private const float SequenceDuration = ImpactHold + SeparationDuration;

    /// <summary>
    /// Largest delay handed to a chunk, so they leave in a readable order
    /// instead of all on the same frame. Chunks that depart together overlap
    /// while they are still bunched, which is what made the individual pieces
    /// hard to pick out; the delays are absorbed by each chunk's own clock, so
    /// they still finish together and the beat is unchanged. Small enough to
    /// read as one impact.
    /// </summary>
    private const float MaxPieceStagger = 0.055f;

    /// <summary>Point in the separation where chunks start fading and shrinking.</summary>
    private const float EndPhaseStart = 0.74f;

    /// <summary>Scale a chunk is left at once the end phase has fully played.</summary>
    private const float EndPhaseScale = 0.06f;

    /// <summary>
    /// Separation travel over the motion phase. The opening slope is roughly
    /// 60% of the plain ease-out this replaces, so the chunks drift apart
    /// before the curve accelerates through the middle and settles on exactly
    /// the same final spread.
    /// </summary>
    private static readonly AnimationCurve TravelCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 1.42f),
        new Keyframe(0.32f, 0.35f, 1.22f, 1.22f),
        new Keyframe(0.66f, 0.78f, 1.45f, 1.45f),
        new Keyframe(1f, 1f, 0.30f, 0f));

    /// <summary>
    /// Tumble over the motion phase, as a multiple of each chunk's authored
    /// spin. Held back while the chunks separate, then wound up past 1 so the
    /// rotation visibly picks up as they accelerate away.
    /// </summary>
    private static readonly AnimationCurve SpinCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 0.35f),
        new Keyframe(0.35f, 0.22f, 0.86f, 0.86f),
        new Keyframe(1f, 1.18f, 1.90f, 1.90f));

    /// <summary>
    /// Downward drop applied over the separation, as a share of the authored
    /// travel space. Chunks take one of three values so their arcs diverge and
    /// stay individually readable; the largest is the arc every chunk used to
    /// follow, so the break-up's lowest reach is unchanged.
    /// </summary>
    private static readonly float[] LiftVariants = { 0.36f, 0.31f, 0.26f };

    // ─── Presentation tuning — the only place these proportions live ─────────
    //
    // Both are expressed relative to the live rocket, never in absolute units.

    /// <summary>Largest chunk's on-screen size, as a fraction of the rocket.</summary>
    private const float LargestPieceSizeVsRocket = 0.55f;

    /// <summary>How far the outermost chunk travels, in rocket lengths.</summary>
    private const float SpreadRadiusVsRocketSize = 1f;

    /// <summary>Largest share of screen height the whole break-up may occupy.</summary>
    private const float MaxSpreadVsScreenHeight = 0.25f;

    /// <summary>
    /// Floor on how far chunks separate, in multiples of their own reach, so a
    /// very tight camera still breaks the rocket apart instead of stacking it.
    /// </summary>
    private const float MinimumSeparation = 0.5f;

    /// <summary>Grace period before the failsafe removes an interrupted sequence.</summary>
    private const float FailsafeLifetime = SequenceDuration + 0.5f;

    private static readonly List<CrashDebrisPresentation> Active =
        new List<CrashDebrisPresentation>();

    /// <summary>
    /// Transparent stand-ins for the authored chunk materials, keyed by the
    /// material they were built from. The FBX ships opaque materials, which
    /// discard the alpha the fade drives; one clone per source material is
    /// shared by every crash, so a run never accumulates material instances.
    /// </summary>
    private static readonly Dictionary<Material, Material> FadeMaterials =
        new Dictionary<Material, Material>();

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private static GameObject cachedDebrisPrefab;

    private List<Piece> pieces;

    private sealed class Piece
    {
        public Transform transform;
        public Renderer renderer;
        public MaterialPropertyBlock properties;
        public Color baseColor;
        public Vector3 startPosition;
        public Quaternion startRotation;
        public Vector3 startScale;
        public Vector3 velocity;
        public Vector3 spin;
        public float delay;
        public float lift;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActiveList()
    {
        Active.Clear();

        // Runtime materials do not survive leaving Play Mode; the cache would
        // otherwise hand out destroyed references on the next run.
        FadeMaterials.Clear();
    }

    /// <summary>
    /// Removes every live debris object immediately. Called before Fly Again or
    /// Restart hands control back, so a resumed run never inherits debris from
    /// the crash that ended the previous one.
    /// </summary>
    public static void ClearAll()
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            CrashDebrisPresentation presentation = Active[i];
            if (presentation != null) Destroy(presentation.gameObject);
        }

        Active.Clear();
    }

    public static void Spawn(Vector3 position, Quaternion rocketRotation, float rocketWorldSize)
    {
        if (cachedDebrisPrefab == null)
            cachedDebrisPrefab = Resources.Load<GameObject>("VFX/RocketCrashDebris");
        if (cachedDebrisPrefab == null) return;

        GameObject root = Instantiate(cachedDebrisPrefab, position, rocketRotation);
        root.name = "RocketCrashDebris_Presentation";

        // Scaled while inactive so no frame can ever render the raw,
        // centimetre-scale mesh at its authored size. The break-up only starts
        // once the object is live again — a coroutine cannot run on an inactive
        // GameObject, and starting one there would leave the debris frozen.
        root.SetActive(false);
        CrashDebrisPresentation presentation = root.AddComponent<CrashDebrisPresentation>();
        bool ready = presentation.Configure(Mathf.Max(0.05f, rocketWorldSize));
        root.SetActive(true);

        if (ready) presentation.Play();
        else Destroy(root);
    }

    /// <summary>
    /// Sizes the debris against the live rocket. Returns false when the prefab
    /// carries no measurable geometry to break apart.
    /// </summary>
    bool Configure(float rocketWorldSize)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        pieces = new List<Piece>(renderers.Length);

        // Pass 1: resolve each chunk's authored break-up motion, and measure the
        // three quantities the scales are derived from.
        float largestPieceExtent = 0f;
        float farthestChunkReach = 0f;
        float farthestTravel = 0f;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.sortingOrder = 18 + i;
            renderer.sharedMaterial = GetFadeMaterial(renderer.sharedMaterial);

            Transform pieceTransform = renderer.transform;
            Vector3 authoredOrigin = GetPieceOrigin(pieceTransform.name, i);
            Vector2 namedDirection = GetPieceDirection(pieceTransform.name, i);
            float speed = 1.55f + (i % 3) * 0.32f;
            Vector3 velocity =
                new Vector3(namedDirection.x, namedDirection.y, -0.12f + i * 0.045f) * speed;

            pieceTransform.localPosition = authoredOrigin;
            pieces.Add(new Piece
            {
                transform = pieceTransform,
                renderer = renderer,
                properties = new MaterialPropertyBlock(),
                baseColor = GetBaseColor(renderer.sharedMaterial),
                startPosition = authoredOrigin,
                startRotation = pieceTransform.localRotation,
                startScale = pieceTransform.localScale,
                velocity = velocity,
                spin = new Vector3(110f + i * 27f, 150f - i * 18f, (i % 2 == 0 ? 1f : -1f) * (290f + i * 31f)),
                delay = renderers.Length > 1 ? MaxPieceStagger * i / (renderers.Length - 1) : 0f,
                lift = LiftVariants[i % LiftVariants.Length],
            });

            MeasurePiece(renderer, out float extent, out float reach);
            largestPieceExtent = Mathf.Max(largestPieceExtent, extent);
            farthestChunkReach = Mathf.Max(farthestChunkReach, reach);
            farthestTravel = Mathf.Max(farthestTravel, (authoredOrigin + velocity).magnitude);
        }

        if (largestPieceExtent <= Mathf.Epsilon || farthestTravel <= Mathf.Epsilon) return false;

        // The chunks are sized off the rocket alone, so the debris always reads
        // as that rocket whatever the camera is doing.
        float largestPieceWorldSize = rocketWorldSize * LargestPieceSizeVsRocket;
        float geometryScale = largestPieceWorldSize / largestPieceExtent;

        // The break-up opens up by one rocket length, but never so far that the
        // whole scatter outgrows its share of the screen. The budget is spent on
        // the chunks first: several are modelled around an edge rather than their
        // centre — the wings reach a full span out of their own pivot — so the
        // room left over for travel is what remains after that reach is paid for.
        float budgetRadius = 0.5f * MaxSpreadVsScreenHeight * GameplayVFX.VisibleWorldHeight();
        float chunkReach = geometryScale * farthestChunkReach;
        float spreadRadius = Mathf.Max(
            chunkReach * MinimumSeparation,
            Mathf.Min(rocketWorldSize * SpreadRadiusVsRocketSize, budgetRadius - chunkReach));

        float rootScale = spreadRadius / farthestTravel;
        transform.localScale = Vector3.one * rootScale;

        // The chunks carry the geometry, corrected for the root scale they sit
        // under so the rocket's size is applied exactly once.
        float pieceScale = geometryScale / rootScale;

        // Pass 2: apply it, keeping each chunk's authored proportions.
        for (int i = 0; i < pieces.Count; i++)
        {
            Piece piece = pieces[i];
            piece.startScale *= pieceScale;
            piece.transform.localScale = piece.startScale;
        }

        Active.Add(this);

        // Guarantees removal even if the sequence is interrupted before it ends.
        Destroy(gameObject, FailsafeLifetime);
        return true;
    }

    void Play()
    {
        StartCoroutine(Animate());
    }

    void OnDestroy()
    {
        Active.Remove(this);
    }

    /// <summary>
    /// Measures one chunk in mesh-local units, before any scaling this component
    /// applies. <paramref name="extent"/> is its largest dimension in the
    /// camera-facing plane and drives how big it is drawn.
    /// <paramref name="reach"/> is how far its geometry extends from its own
    /// pivot, which is what it costs against the on-screen spread budget.
    /// </summary>
    static void MeasurePiece(Renderer renderer, out float extent, out float reach)
    {
        extent = 0f;
        reach = 0f;

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return;

        Bounds bounds = filter.sharedMesh.bounds;
        Vector3 scale = renderer.transform.localScale;
        Vector3 size = Vector3.Scale(bounds.size, Abs(scale));
        Vector3 offset = Vector3.Scale(bounds.center, Abs(scale));

        extent = Mathf.Max(size.x, size.y);
        reach = Mathf.Max(
            Mathf.Abs(offset.x) + size.x * 0.5f,
            Mathf.Abs(offset.y) + size.y * 0.5f);
    }

    static Vector3 Abs(Vector3 value)
    {
        return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    IEnumerator Animate()
    {
        float elapsed = 0f;
        while (elapsed < SequenceDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            // Before the hold is up the chunks stay exactly as Configure left
            // them, so the frame of impact reads as a held pose.
            float separationElapsed = elapsed - ImpactHold;
            if (separationElapsed > 0f) ApplySeparation(separationElapsed);

            yield return null;
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Places every chunk for one frame of the separation. Each runs on its own
    /// staggered clock but they all reach the end of the sequence together, so
    /// the stagger never lengthens the beat.
    /// </summary>
    void ApplySeparation(float separationElapsed)
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            Piece piece = pieces[i];
            if (piece.transform == null) continue;

            float progress = Mathf.Clamp01(
                (separationElapsed - piece.delay) / (SeparationDuration - piece.delay));

            float travel = TravelCurve.Evaluate(progress);
            float lift = progress * progress * piece.lift;
            float endPhase = Mathf.SmoothStep(
                0f, 1f, Mathf.InverseLerp(EndPhaseStart, 1f, progress));

            piece.transform.localPosition = piece.startPosition
                + piece.velocity * travel
                + Vector3.down * lift;
            piece.transform.localRotation = piece.startRotation
                * Quaternion.Euler(piece.spin * SpinCurve.Evaluate(progress));
            piece.transform.localScale =
                piece.startScale * Mathf.Lerp(1f, EndPhaseScale, endPhase);

            if (endPhase > 0f) ApplyFade(piece, 1f - endPhase);
        }
    }

    static void ApplyFade(Piece piece, float alpha)
    {
        if (piece.renderer == null) return;

        Color color = piece.baseColor;
        color.a *= alpha;

        piece.renderer.GetPropertyBlock(piece.properties);
        piece.properties.SetColor(BaseColorId, color);
        piece.properties.SetColor(ColorId, color);
        piece.renderer.SetPropertyBlock(piece.properties);
    }

    /// <summary>
    /// Returns the shared transparent clone of an authored chunk material,
    /// building it on first use. Assigning the clone as sharedMaterial leaves
    /// the imported material asset untouched.
    /// </summary>
    static Material GetFadeMaterial(Material source)
    {
        if (source == null) return null;
        if (FadeMaterials.TryGetValue(source, out Material cached) && cached != null)
            return cached;

        Material fadeable = new Material(source) { name = source.name + " (Crash Fade)" };
        VfxSpriteFactory.MakeTransparent(fadeable);
        FadeMaterials[source] = fadeable;
        return fadeable;
    }

    static Color GetBaseColor(Material material)
    {
        if (material == null) return Color.white;
        if (material.HasProperty(BaseColorId)) return material.GetColor(BaseColorId);
        if (material.HasProperty(ColorId)) return material.GetColor(ColorId);
        return Color.white;
    }

    static Vector2 GetPieceDirection(string pieceName, int index)
    {
        if (pieceName.Contains("WingLeft")) return new Vector2(-0.95f, 0.20f);
        if (pieceName.Contains("WingRight")) return new Vector2(0.95f, 0.24f);
        if (pieceName.Contains("Engine")) return new Vector2(-0.12f, -1f);
        if (pieceName.Contains("Nose")) return new Vector2(0.14f, 1f);

        float angle = 35f + index * 137.5f;
        return new Vector2(
            Mathf.Cos(angle * Mathf.Deg2Rad),
            Mathf.Sin(angle * Mathf.Deg2Rad)).normalized;
    }

    static Vector3 GetPieceOrigin(string pieceName, int index)
    {
        // The FBX authors its chunks in an exploded layout along its own long
        // axis, which points away from a 2D camera. These origins re-seat the
        // break-up in the plane the player actually sees. They are in the same
        // authored travel space as the velocities above, so the root scale
        // converts both to world units together.
        if (pieceName.Contains("WingLeft")) return new Vector3(-0.24f, -0.02f, 0f);
        if (pieceName.Contains("WingRight")) return new Vector3(0.24f, -0.02f, 0f);
        if (pieceName.Contains("Engine")) return new Vector3(0f, -0.26f, 0.015f);
        if (pieceName.Contains("Nose")) return new Vector3(0f, 0.28f, -0.015f);
        return new Vector3(0.10f, 0.02f, -0.03f - index * 0.008f);
    }
}
