using UnityEngine;

[DisallowMultipleComponent]
public sealed class UIShadowLink : MonoBehaviour
{
    [SerializeField] GameObject shadow;
    public void Bind(GameObject target) { shadow = target; Sync(); }
    void OnEnable() => Sync();
    void OnDisable() { if (shadow != null) shadow.SetActive(false); }
    void Sync() { if (shadow != null) shadow.SetActive(isActiveAndEnabled && gameObject.activeInHierarchy); }
}
