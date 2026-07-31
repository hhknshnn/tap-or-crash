using System.Collections;
using UnityEngine;

/// <summary>
/// The single deadline every full-screen Game Over visual waits for.
///
/// The crash break-up runs for roughly 0.78 s, but the run ends — and the
/// presentation that reports it is handed over — a fraction of that in. Without
/// a shared deadline the Game Over panel and the Continue offer both take the
/// screen while the rocket is still coming apart behind them.
///
/// This holds only the *visuals*. The Game Over state, the run result, the
/// reward preparation, the ad hook, the input lock and PresentationGate
/// ownership are all committed at impact, exactly as before; a presenter simply
/// waits here before it starts fading in.
///
/// Every wait is tokened. Restart, Fly Again, Continue and a second crash all
/// invalidate the outstanding token, so a coroutine that was still waiting can
/// never open an obsolete panel over resumed gameplay.
/// </summary>
public static class CrashRevealDelay
{
    /// <summary>
    /// Crash time the player is guaranteed to see before any full-screen Game
    /// Over visual begins appearing. Two thirds of the break-up, which covers
    /// the impact hold and the readable part of the separation.
    /// </summary>
    public const float RevealDelay = 0.52f;

    /// <summary>Unscaled timestamp the visuals may start at.</summary>
    private static float revealUnscaledTime = float.NegativeInfinity;

    /// <summary>Bumped whenever an outstanding wait stops being valid.</summary>
    private static int token;

    /// <summary>
    /// The token a presenter must capture before waiting and re-check after, to
    /// prove the crash it was waiting on is still the current one.
    /// </summary>
    public static int Token => token;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        revealUnscaledTime = float.NegativeInfinity;
        token = 0;
    }

    /// <summary>Starts the window. Called the moment the rocket is destroyed.</summary>
    public static void MarkImpact()
    {
        token++;
        revealUnscaledTime = Time.unscaledTime + RevealDelay;
    }

    /// <summary>
    /// Drops the window and invalidates any wait still running on it. Called
    /// wherever gameplay is handed back — Restart, Fly Again and Continue all
    /// route through <c>GameManager.ClearCrashPresentation</c>.
    /// </summary>
    public static void Cancel()
    {
        token++;
        revealUnscaledTime = float.NegativeInfinity;
    }

    /// <summary>True while the captured token still refers to the live crash.</summary>
    public static bool IsCurrent(int waitToken) => waitToken == token;

    /// <summary>
    /// Yields until the crash has been on screen long enough, or until the wait
    /// is invalidated — whichever comes first. Unscaled throughout, so the death
    /// sequence's slow-motion beat cannot stretch it.
    /// </summary>
    public static IEnumerator WaitForReveal(int waitToken)
    {
        while (waitToken == token && Time.unscaledTime < revealUnscaledTime)
            yield return null;
    }
}
