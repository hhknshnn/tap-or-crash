using UnityEngine;

/// <summary>
/// The single authority for what a new run is.
///
/// Every accepted new-run entry point routes through <see cref="Begin"/>:
/// the first run after an app launch, a Main Menu launch, Fly Again, a Game Over
/// restart and a Pause restart. Begin clears every piece of state that belongs to
/// one run and stamps a new <see cref="Id"/>, so a run always starts in exactly
/// the state the player's very first run started in.
///
/// Run state is deliberately never persisted. A milestone that was shown in an
/// earlier run is eligible again in the next one, so nothing here reads or writes
/// PlayerPrefs and nothing here carries a "seen once" flag.
///
/// The identifier exists so work started by an earlier run can recognise that it
/// no longer owns the game: a coroutine captures <see cref="Id"/> when it starts
/// and stops the moment <see cref="IsCurrent"/> turns false.
/// </summary>
public static class RunSession
{
    /// <summary>Identifies the run that currently owns gameplay. Never reused.</summary>
    public static int Id { get; private set; }

    /// <summary>True while <paramref name="runId"/> still owns gameplay.</summary>
    public static bool IsCurrent(int runId) => runId == Id;

    /// <summary>
    /// Hands the game to a new run. Idempotent in effect: calling it twice simply
    /// starts from a clean state twice.
    /// </summary>
    public static void Begin()
    {
        Id++;

        // The gates come first. Every system below — and every system that reads
        // WorldTransitionManager.IsPendingOrPlaying — stays inert while a stale
        // gate is held, so releasing them is what makes the rest of this reset
        // take effect on the same frame.
        PresentationGate.ReleaseRunScoped();

        WorldTransitionManager.ResetForNewRun();
        GameplayVFX.Ensure().ResetForNewRun();

        AsteroidSpawner asteroids = Object.FindAnyObjectByType<AsteroidSpawner>();
        if (asteroids != null) asteroids.ResetForNewRun();
    }
}
