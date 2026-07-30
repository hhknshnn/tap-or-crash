using UnityEngine;
using UnityEngine.EventSystems;

// The single definition of a tap for gameplay.
//
// Android reports hardware keys — volume, menu — through the same legacy Input channel
// that also carries mouse emulation, which is how a volume press could reach the rocket
// as a launch. On a device with a touchscreen the pointer is therefore read from the
// touch API alone: a launch needs a real finger with a real screen position, and no key
// event can produce one. Mouse and Space stay the desktop and editor path.
//
// Gameplay reads this instead of UnityEngine.Input, so the rule lives in one place
// rather than being restated at every call site.
public static class TapInput
{
    // Device Simulator and phones report a touchscreen and feed real Touches; a desktop
    // editor does not, and falls through to the mouse.
    static bool UsesTouch => Input.touchSupported;

    public static bool PointerPressedThisFrame
    {
        get
        {
            if (!UsesTouch) return Input.GetMouseButtonDown(0);
            return TryGetPrimaryTouch(out Touch touch) && touch.phase == TouchPhase.Began;
        }
    }

    public static bool PointerHeld
    {
        get
        {
            if (!UsesTouch) return Input.GetMouseButton(0);
            if (!TryGetPrimaryTouch(out Touch touch)) return false;

            return touch.phase == TouchPhase.Began
                || touch.phase == TouchPhase.Moved
                || touch.phase == TouchPhase.Stationary;
        }
    }

    public static bool PointerReleasedThisFrame
    {
        get
        {
            if (!UsesTouch) return Input.GetMouseButtonUp(0);
            return TryGetPrimaryTouch(out Touch touch) && touch.phase == TouchPhase.Ended;
        }
    }

    // A cancelled touch is the system taking the finger away — the Android volume overlay
    // opening over the game is exactly that. It ends the press and never becomes a launch.
    public static bool PointerCancelledThisFrame
    {
        get
        {
            if (!UsesTouch) return false;
            return TryGetPrimaryTouch(out Touch touch) && touch.phase == TouchPhase.Canceled;
        }
    }

    // Space is a desktop convenience. It is never read on a touch device, so no hardware
    // key on a phone can reach the launch.
    public static bool KeyPressedThisFrame => !UsesTouch && Input.GetKeyDown(KeyCode.Space);

    public static bool KeyHeld => !UsesTouch && Input.GetKey(KeyCode.Space);

    public static bool KeyReleasedThisFrame => !UsesTouch && Input.GetKeyUp(KeyCode.Space);

    public static bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;

        if (UsesTouch && TryGetPrimaryTouch(out Touch touch))
            return EventSystem.current.IsPointerOverGameObject(touch.fingerId);

        return EventSystem.current.IsPointerOverGameObject();
    }

    // The first finger down owns the gameplay tap. Later fingers are ignored, which is
    // what stops a second thumb from ending an orbit the first one started.
    static bool TryGetPrimaryTouch(out Touch touch)
    {
        if (Input.touchCount > 0)
        {
            touch = Input.GetTouch(0);
            return true;
        }

        touch = default;
        return false;
    }
}
