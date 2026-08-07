using UnityEngine;

// The one place that answers "which Canvas is the scene's UI".
//
// SampleScene owns exactly one authored Canvas, but it is not the only Canvas alive
// at runtime: RocketFuelGaugeView isolates its liquid-wave rebuilds onto a nested
// Canvas, and a GraphicRaycaster added anywhere brings one with it as a required
// component. FindAnyObjectByType<Canvas>() gives no ordering guarantee, so every
// system that used it to reach StartPanel, CoinCounter or GameUI was resolving an
// arbitrary one of those — intermittently the nested gauge canvas, whose children
// are a fuel bar and nothing else.
//
// The failure is silent and total: the caller finds no child by that name and either
// rebuilds UI that already exists or reports the serialized reference as missing.
//
// Callers that legitimately want their own canvas (a nested isolation canvas, a
// component's own Canvas) still use GetComponent — this is only for the scene's
// single root UI surface.
public static class UIRootCanvas
{
    static Canvas cached;

    /// <summary>
    /// The scene's root overlay Canvas, or null if the scene has no Canvas at all.
    /// Inactive canvases are included so systems can resolve the surface before it
    /// is switched on.
    /// </summary>
    public static Canvas Resolve()
    {
        if (cached != null && cached.isRootCanvas) return cached;

        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);

        // More than one standalone root-level Canvas can exist at once (RocketFuelPopup
        // owns its own, unparented, root Canvas) — isRootCanvas alone does not single out
        // the scene's authored UI surface, and FindObjectsByType gives no ordering
        // guarantee between them. Every other caller in this codebase already assumes the
        // scene's root surface is the object literally named "Canvas" (GameObject.Find),
        // so that name is the deterministic tiebreaker here too.
        Canvas rootMatch = null;
        Canvas firstRoot = null;
        Canvas fallback = null;
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas candidate = canvases[i];
            if (candidate == null) continue;
            if (candidate.isRootCanvas)
            {
                if (firstRoot == null) firstRoot = candidate;
                if (rootMatch == null && candidate.name == "Canvas") rootMatch = candidate;
            }
            else if (fallback == null) fallback = candidate;
        }

        Canvas resolved = rootMatch != null ? rootMatch : firstRoot;
        if (resolved != null) { cached = resolved; return resolved; }

        // No root canvas at all is not a state the scene can reach, but returning the
        // only surface there is beats returning null to callers that would then build
        // their UI nowhere.
        cached = null;
        return fallback;
    }

    /// <summary>The root Canvas's RectTransform, or null when there is no canvas.</summary>
    public static RectTransform ResolveRect()
    {
        Canvas canvas = Resolve();
        return canvas != null ? canvas.GetComponent<RectTransform>() : null;
    }

    /// <summary>
    /// The cache is a plain object reference and a scene load leaves it pointing at a
    /// destroyed Canvas. The null check in Resolve already covers that; this exists so
    /// a scene load drops the reference itself rather than holding it until the next
    /// lookup.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnLoad() => cached = null;
}
