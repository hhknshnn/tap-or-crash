using UnityEngine;
using UnityEngine.EventSystems;

public sealed class UIButtonPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    float target = 1f;
    public float Press { get; private set; } = 1f;
    UIMotion motion;

    void Awake() => motion = GetComponent<UIMotion>();
    public void BindMotion(UIMotion value) => motion = value;

    void OnEnable()
    {
        Press = target = 1f;
        if (motion == null) transform.localScale = Vector3.one;
    }

    void Update()
    {
        Press = Mathf.Lerp(Press, target, 22f * Time.unscaledDeltaTime);
        if (motion == null) transform.localScale = Vector3.one * Press;
    }

    public void OnPointerDown(PointerEventData eventData) => target = 0.95f;
    public void OnPointerUp(PointerEventData eventData) => target = 1f;
    public void OnPointerExit(PointerEventData eventData) => target = 1f;
}
