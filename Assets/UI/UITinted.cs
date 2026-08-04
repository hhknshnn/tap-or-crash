using UnityEngine;
using UnityEngine.UI;

public sealed class UITinted : MonoBehaviour
{
    public enum Role { Glass, GlassDeep, Rim, Accent, Scrim }
    [SerializeField] Role role = Role.Glass;
    [SerializeField] float alphaScale = 1f;
    Graphic graphic;
    Outline outline;
    int appliedVersion = -1;

    public static UITinted Attach(GameObject target, Role role, float alphaScale = 1f)
    {
        UITinted tint = target.GetComponent<UITinted>();
        if (tint == null) tint = target.AddComponent<UITinted>();
        tint.role = role;
        tint.alphaScale = alphaScale;
        tint.appliedVersion = -1;
        return tint;
    }

    void Awake() => ResolveTargets();
    void OnEnable() => appliedVersion = -1;
    void LateUpdate() { if (appliedVersion != UIDesign.Version) { appliedVersion = UIDesign.Version; Apply(); } }

    void ResolveTargets()
    {
        if (graphic == null) graphic = GetComponent<Graphic>();
        if (outline == null) outline = GetComponent<Outline>();
    }

    void Apply() => ApplyPalette(UIDesign.CurrentPalette);

    public void ApplyPalette(UIDesign.Palette palette)
    {
        ResolveTargets();
        Color color = role switch
        {
            Role.GlassDeep => palette.GlassDeep,
            Role.Rim => palette.GlassRim,
            Role.Accent => palette.Accent,
            Role.Scrim => palette.Scrim,
            _ => palette.Glass,
        };
        color.a *= alphaScale;
        if (graphic != null) graphic.color = color;
        if (outline != null) { Color rim = palette.GlassRim; rim.a *= alphaScale; outline.effectColor = rim; }
    }
}
