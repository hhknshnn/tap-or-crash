using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Swaps the TAP TO LAUNCH shell sprite between its idle and pressed states.
// Lives on StartButton (the full-screen tap area) so it sees the same press
// that launches, without adding a second listener or a second button.
public sealed class UILaunchShellVisual : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [SerializeField] Image shell;
    [SerializeField] Sprite normalSprite;
    [SerializeField] Sprite pressedSprite;

    void OnEnable() => SetPressed(false);
    void OnDisable() => SetPressed(false);

    public void OnPointerDown(PointerEventData eventData) => SetPressed(true);
    public void OnPointerUp(PointerEventData eventData) => SetPressed(false);
    public void OnPointerExit(PointerEventData eventData) => SetPressed(false);

    void SetPressed(bool pressed)
    {
        if (shell == null) return;
        shell.sprite = pressed ? pressedSprite : normalSprite;
    }
}
