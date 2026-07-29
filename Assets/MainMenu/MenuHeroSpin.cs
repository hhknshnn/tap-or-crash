using System.Collections.Generic;
using UnityEngine;

// The hero planet turns forever, slowly enough that you never catch it moving: one
// revolution takes minutes. It is what separates a living world from a screenshot.
//
// Only the body and the things painted on it turn. Anything that lives in the air
// around the planet — butterflies, birds, falling leaves, the wind sheen — is a child
// of the same transform, so it is counter-rotated back to its original orientation.
// Without that, leaves would slowly start falling sideways and the flock would fly
// upside down after a couple of minutes.
[DisallowMultipleComponent]
public sealed class MenuHeroSpin : MonoBehaviour
{
    // A full turn every ~4 minutes: alive, never distracting.
    const float DegreesPerSecond = 1.5f;

    // Ambience children that belong to the sky rather than to the surface.
    static readonly string[] AirbornePrefixes =
    {
        "AmbienceOrbiter", "AmbienceCrossing", "AmbienceDrift", "AmbienceSheen", "MenuLife"
    };

    struct Upright
    {
        public Transform body;
        public Quaternion original;
    }

    readonly List<Upright> upright = new List<Upright>(16);

    float direction = 1f;
    float angle;
    float rescanTimer;
    int rescans = 4;

    // Direction is picked once per menu so the two worlds of a session never feel identical.
    public void Configure(bool clockwise)
    {
        direction = clockwise ? -1f : 1f;
    }

    void Start()
    {
        Rescan();
    }

    void LateUpdate()
    {
        // Ambience layers build themselves over the first frames; pick up late arrivals.
        if (rescans > 0)
        {
            rescanTimer -= Time.unscaledDeltaTime;
            if (rescanTimer <= 0f)
            {
                rescanTimer = 0.35f;
                rescans--;
                Rescan();
            }
        }

        angle = Mathf.Repeat(angle + DegreesPerSecond * direction * Time.unscaledDeltaTime, 360f);
        transform.localRotation = Quaternion.Euler(0f, 0f, angle);

        Quaternion counter = Quaternion.Euler(0f, 0f, -angle);
        for (int i = 0; i < upright.Count; i++)
        {
            Transform body = upright[i].body;
            if (body != null) body.localRotation = counter * upright[i].original;
        }
    }

    // Newly built effects are recorded with the orientation their author gave them;
    // already tracked ones keep theirs, which the counter-rotation has since overwritten.
    void Rescan()
    {
        foreach (Transform child in transform)
        {
            if (!IsAirborne(child.name) || Tracked(child)) continue;
            upright.Add(new Upright { body = child, original = child.localRotation });
        }
    }

    bool Tracked(Transform child)
    {
        for (int i = 0; i < upright.Count; i++)
            if (upright[i].body == child) return true;
        return false;
    }

    static bool IsAirborne(string name)
    {
        for (int i = 0; i < AirbornePrefixes.Length; i++)
            if (name.StartsWith(AirbornePrefixes[i], System.StringComparison.Ordinal)) return true;
        return false;
    }
}
