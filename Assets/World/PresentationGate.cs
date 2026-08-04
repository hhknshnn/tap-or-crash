using System.Collections.Generic;

// Single authority for every full-screen presentation in the game.
// Systems acquire on show and release on hide. Future ad networks should call
// AcquireAdvertisement / ReleaseAdvertisement around their fullscreen content.
public static class PresentationGate
{
    public enum Kind
    {
        MenuIntro,
        WorldTransition,
        Pause,
        Tutorial,
        ContinueOffer,
        GameOver,
        ThemePopup,
        Shop,
        Splash,
        FuelPopup,
        GameplayNotice,
        Advertisement,
    }

    static readonly HashSet<Kind> active = new HashSet<Kind>();

    // Gates that belong to a single run and can never legitimately still be held
    // when the next one starts. The set is static and outlives a scene load, so a
    // restart taken from inside one of these presentations — Pause Restart is the
    // one the player can reach every run — would otherwise hand the new run a gate
    // nothing is left alive to release, freezing world transitions, milestone
    // notices, asteroids and moving orbits for the rest of the session.
    //
    // Every other kind is owned by a presenter that manages its own lifetime and
    // may legitimately be open across the start of a run (Tutorial, Splash, Shop,
    // ThemePopup, FuelPopup, Advertisement), so RunSession never touches them.
    static readonly Kind[] RunScoped =
    {
        Kind.MenuIntro,
        Kind.WorldTransition,
        Kind.Pause,
        Kind.ContinueOffer,
        Kind.GameOver,
        Kind.GameplayNotice,
    };

    public static bool IsActive(Kind kind) => active.Contains(kind);

    public static bool IsAnyFullScreenPresentationActive => active.Count > 0;

    public static bool IsAdvertisementActive => IsActive(Kind.Advertisement);

    public static bool CanBeginWorldTransition() => !IsAnyFullScreenPresentationActive;

    public static void Acquire(Kind kind) => active.Add(kind);

    public static void Release(Kind kind) => active.Remove(kind);

    /// <summary>
    /// Releases every gate scoped to a single run. Called by <see cref="RunSession"/>
    /// as a new run takes ownership, and by the restart teardown so the gate is
    /// already free while the scene reloads.
    /// </summary>
    public static void ReleaseRunScoped()
    {
        for (int i = 0; i < RunScoped.Length; i++) active.Remove(RunScoped[i]);
    }

    public static void AcquireAdvertisement() => Acquire(Kind.Advertisement);

    public static void ReleaseAdvertisement() => Release(Kind.Advertisement);
}
