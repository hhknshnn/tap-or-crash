using System.Collections.Generic;
using UnityEngine;

// Groups the sprites under a root so they can be dimmed as one.
//
// Every alpha is captured once at construction, so a fade is always relative to the
// look the artist authored: a star that was 40% opaque never becomes brighter than
// 40% when the group is faded back in.
//
// Some menu layers animate their own alpha every frame (twinkles, halos, glows). A
// fader cannot win that fight, so Capture also collects the behaviours that write
// alpha and Freeze turns them off for the duration of the fade. A frozen twinkle over
// a one-second cross-fade is invisible; a twinkle fighting the fade is not.
public sealed class MenuFader
{
    readonly List<SpriteRenderer> renderers = new List<SpriteRenderer>();
    readonly List<float> baseAlpha = new List<float>();
    readonly List<MonoBehaviour> animators = new List<MonoBehaviour>();

    public static MenuFader Capture(Transform root, params System.Type[] animatorTypes)
    {
        MenuFader fader = new MenuFader();
        fader.Add(root, animatorTypes);
        return fader;
    }

    public void Add(Transform root, params System.Type[] animatorTypes)
    {
        if (root == null) return;

        foreach (SpriteRenderer renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
        {
            renderers.Add(renderer);
            baseAlpha.Add(renderer.color.a);
        }

        if (animatorTypes == null) return;
        for (int i = 0; i < animatorTypes.Length; i++)
        {
            foreach (Component component in root.GetComponentsInChildren(animatorTypes[i], true))
            {
                if (component is MonoBehaviour behaviour) animators.Add(behaviour);
            }
        }
    }

    // Stops the captured behaviours from writing alpha while a fade is in flight.
    public void Freeze()
    {
        for (int i = 0; i < animators.Count; i++)
            if (animators[i] != null) animators[i].enabled = false;
    }

    public void Thaw()
    {
        for (int i = 0; i < animators.Count; i++)
            if (animators[i] != null) animators[i].enabled = true;
    }

    public void SetAlpha(float multiplier)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null) continue;

            Color color = renderer.color;
            color.a = baseAlpha[i] * multiplier;
            renderer.color = color;
        }
    }
}
