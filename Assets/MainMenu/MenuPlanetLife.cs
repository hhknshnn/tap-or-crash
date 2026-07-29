using UnityEngine;

// Extra life for the menu hero only.
//
// The planet already wears its own theme ambience, but that ambience is authored per
// planet: one Natural world gets butterflies, the next only leaves. On the menu the
// same planet is on screen for as long as the player looks at it, so the world has to
// read as *inhabited* whichever planet the showcase happened to pick.
//
// This rides on the shared PlanetAmbienceKit, so every effect here is the same
// collider-free, batched, off-screen-free machinery the game already ships. Nothing is
// added to gameplay planets: MainMenuShowcase attaches this to the hero and nowhere else.
public sealed class MenuPlanetLife : PlanetAmbienceKit
{
    // Set by MainMenuShowcase before the first frame.
    public Color accent = new Color(0.4f, 0.9f, 0.5f, 1f);

    // A green world is a growing one. Read from the theme's own accent hue rather than
    // from a theme name, so a future Jungle or Swamp world comes alive with no edit here.
    public bool living;

    static readonly Color BirdTone = new Color(0.14f, 0.19f, 0.17f, 0.9f);

    // Hue window that reads as vegetation (roughly yellow-green through teal).
    public static bool IsLivingAccent(Color accent)
    {
        Color.RGBToHSV(accent, out float hue, out float saturation, out _);
        return saturation > 0.25f && hue > 0.16f && hue < 0.47f;
    }

    protected override void Build()
    {
        Color bloom = Color.Lerp(accent, Color.white, 0.55f);

        if (!living)
        {
            // Every world still gets the faintest dust so the frame is never dead still.
            AddMotes(Color.Lerp(accent, Color.white, 0.7f), 0.30f, 10);
            AddTwinkles(AmbienceVfxAssets.Sparkle, Color.Lerp(accent, Color.white, 0.8f),
                5, 0.055f, 3.4f, 6.2f, 0.55f);
            return;
        }

        // Blossoms catching the light: the surface itself has something happening on it.
        AddTwinkles(AmbienceVfxAssets.Sparkle, bloom, 7, 0.05f, 2.6f, 5.4f, 0.62f);

        // Grass and leaves moving in the wind — a colour breath rather than a vertex shader.
        AddBreath(new Color(0.94f, 1f, 0.90f), 0.85f, 0.42f);

        // Pollen hanging in the warm air.
        AddMotes(bloom, 0.42f, 14);

        // Petals drifting off the canopy, always with the same wind as the pollen.
        AddDrift(new DriftSettings
        {
            sprite = AmbienceVfxAssets.Petal,
            colorA = Fade(bloom, 0.80f),
            colorB = Fade(Color.Lerp(accent, Color.white, 0.25f), 0.55f),
            mode = DriftMode.Fall,
            sizeMin = 0.04f, sizeMax = 0.075f,
            speedMin = 0.14f, speedMax = 0.30f,
            wind = new Vector2(0.11f, 0f),
            lifetime = 3.8f,
            intervalMin = 0.9f, intervalMax = 2.1f,
            maxParticles = 10
        });

        // The inhabitants.
        AddOrbiters(AmbienceVfxAssets.Butterfly, Fade(bloom, 0.95f), 3, 0.20f, 0.45f, 0.9f, true);
        AddCrossing(AmbienceVfxAssets.Bird, BirdTone, 0.26f, 5.5f, 11f, 0.3f, true);
    }

    void AddMotes(Color color, float alpha, int maxParticles)
    {
        AddDrift(new DriftSettings
        {
            sprite = AmbienceVfxAssets.SoftDot,
            colorA = Fade(color, alpha),
            colorB = Fade(color, alpha * 0.5f),
            mode = DriftMode.Float,
            sizeMin = 0.025f, sizeMax = 0.05f,
            speedMin = 0.04f, speedMax = 0.13f,
            wind = new Vector2(0.04f, 0.05f),
            lifetime = 3.6f,
            intervalMin = 0.45f, intervalMax = 1f,
            fieldRadius = 0.95f,
            maxParticles = maxParticles
        });
    }

    static Color Fade(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
}
