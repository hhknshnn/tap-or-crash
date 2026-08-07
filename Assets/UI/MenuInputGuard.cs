using UnityEngine;

// Keeps the pointer that closed a modal from also being read as a launch.
//
// The Main Menu's StartButton is a full-screen Button: every tap that is not
// consumed by something above it starts a run. A modal closing on pointer-down —
// or a modal whose blocker is torn down before the finger leaves the glass — hands
// that same press straight to the launch.
//
// EventSystem alone does not cover this: a Button raises its click on release, so
// a control that closes on pointer-up leaves the release frame with the blocker
// already gone. The guard is armed by whatever closed the modal and comes down
// once the pointer has genuinely been released *and* one further frame has passed.
// There is no timer: nothing here waits a fixed number of seconds.
//
// It is advanced by its own frame ticker rather than by whoever reads it. A guard
// that only moved when the launch path looked at it would still be up the next time
// the player deliberately pressed Launch, however many minutes later — the read
// itself would have been the first thing to notice the release.
public static class MenuInputGuard
{
    static bool armed;
    static bool pointerReleased;
    static int armedFrame;

    /// True while the press that closed a modal is still the live pointer.
    public static bool IsLaunchSuppressed => armed;

    /// Called by a modal as it closes. Arming twice is one guard.
    public static void SuppressLaunchUntilPointerReleased()
    {
        armed = true;
        pointerReleased = false;
        armedFrame = Time.frameCount;
        Ticker.Ensure();
    }

    static void Tick()
    {
        if (!armed) return;

        // The closing finger has to leave the glass first. On a device the tap that
        // pressed the close control is still down for the frames right after it.
        if (!pointerReleased)
        {
            if (!TapInput.PointerHeld) pointerReleased = true;
            return;
        }

        // One further frame, so the release itself can never be the launch.
        if (Time.frameCount <= armedFrame) return;
        armed = false;
    }

    // Statics outlive a scene load, and a guard armed by a modal that is about to be
    // destroyed would otherwise greet the next scene still up.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnLoad()
    {
        armed = false;
        pointerReleased = false;
    }

    // Exists only while a guard is up, and deletes itself the moment it comes down.
    // Nothing is left running on the menu once the player is past the modal.
    [DisallowMultipleComponent]
    sealed class Ticker : MonoBehaviour
    {
        static Ticker instance;

        public static void Ensure()
        {
            if (instance != null) return;

            GameObject host = new GameObject("MenuInputGuardTicker") { hideFlags = HideFlags.HideAndDontSave };
            instance = host.AddComponent<Ticker>();
        }

        void Update()
        {
            Tick();
            if (armed) return;

            instance = null;
            Destroy(gameObject);
        }
    }
}
