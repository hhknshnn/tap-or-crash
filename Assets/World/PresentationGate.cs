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
        Advertisement,
    }

    static readonly HashSet<Kind> active = new HashSet<Kind>();

    public static bool IsActive(Kind kind) => active.Contains(kind);

    public static bool IsAnyFullScreenPresentationActive => active.Count > 0;

    public static bool IsAdvertisementActive => IsActive(Kind.Advertisement);

    public static bool CanBeginWorldTransition() => !IsAnyFullScreenPresentationActive;

    public static void Acquire(Kind kind) => active.Add(kind);

    public static void Release(Kind kind) => active.Remove(kind);

    public static void AcquireAdvertisement() => Acquire(Kind.Advertisement);

    public static void ReleaseAdvertisement() => Release(Kind.Advertisement);
}
