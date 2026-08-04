using UnityEngine;

// What colour space looks like in the world the player is currently flying through.
//
// No theme is named anywhere in this file. Every colour is derived from the one
// accent the PlanetAmbience registry already publishes for a world, so adding a
// fourth world means registering its ambience and nothing else — the sky, the
// nebulae, the dust and the bounce light follow on their own.
//
// The derivation is deliberately hue-anchored: the accent's hue drives everything,
// while a constant cosmic violet is mixed back in so that no theme ever turns the
// sky monochrome. Natural lands on a warm green haze, Ice on a cold blue one, Lava
// on an ember orange — not because they are written down, but because those are
// their accents.
public struct SpacePalette
{
    public Color deepVoid;      // the flat colour the whole frame sits on
    public Color voidGlow;      // theme light pooling at the bottom of the sky
    public Color nebulaNear;    // the two nebula hues, either side of the accent
    public Color nebulaFar;
    public Color galaxy;        // the distant band: always warmer than the nebulae
    public Color star;
    public Color dust;
    public Color rock;
    public Color atmosphere;    // the halo the world casts around the planet
    public Color bounce;        // what the global 2D light is tinted towards

    // A theme-less fallback for the endless pool after the last authored world:
    // cold violet-blue, which is what "deep space" reads as with no world nearby.
    public static readonly Color NeutralAccent = new Color(0.42f, 0.62f, 1f, 1f);

    // Kept in every theme so the sky never collapses to one hue.
    static readonly Color CosmicViolet = new Color(0.34f, 0.20f, 0.62f, 1f);

    // Day-space bases: deep blue-teal-cyan, clearly brighter than night void but never
    // flat sky blue. Blended in by SpaceEnvironment when dayFactor rises.
    static readonly Color DayVoidBase = new Color(0.06f, 0.14f, 0.24f, 1f);
    static readonly Color DayGlowBase = new Color(0.10f, 0.30f, 0.40f, 1f);
    static readonly Color DayAtmoBase = new Color(0.16f, 0.44f, 0.54f, 1f);
    static readonly Color DayStarLift = new Color(0.88f, 0.94f, 1f, 1f);

    public static SpacePalette For(string themeName)
    {
        Color accent = PlanetAmbience.AccentColorFor(themeName, NeutralAccent);
        accent.a = 1f;
        return FromAccent(accent);
    }

    public static SpacePalette FromAccent(Color accent)
    {
        Color.RGBToHSV(accent, out float hue, out float saturation, out float value);
        saturation = Mathf.Clamp(saturation, 0.25f, 0.95f);
        value = Mathf.Clamp(value, 0.4f, 1f);

        var palette = new SpacePalette
        {
            // Near-black, but carrying the world's hue: a pure black sky is what
            // made the old backdrop read as an empty texture rather than as space.
            // Kept this dark on purpose — the theme has to tint the void, not fill
            // it, or the frame turns into one flat sheet of the accent colour.
            deepVoid = Color.HSVToRGB(hue, saturation * 0.40f, 0.035f),
            voidGlow = Color.HSVToRGB(hue, saturation * 0.55f, 0.16f),

            // Two hues a short walk either side of the accent, both pulled towards
            // the cosmic violet so the clouds still belong to space.
            nebulaNear = Color.Lerp(Color.HSVToRGB(Wrap(hue - 0.055f), saturation * 0.86f, 0.92f),
                                    CosmicViolet, 0.34f),
            nebulaFar = Color.Lerp(Color.HSVToRGB(Wrap(hue + 0.075f), saturation * 0.70f, 0.78f),
                                   CosmicViolet, 0.48f),

            // The galaxy core is the one warm light in the frame whatever the world,
            // which is what stops Ice from reading as a blue wash.
            galaxy = Color.Lerp(new Color(1f, 0.94f, 0.84f), accent, 0.28f),

            star = Color.Lerp(Color.white, accent, 0.16f),
            dust = Color.Lerp(Color.white, accent, 0.34f),
            rock = Color.Lerp(new Color(0.74f, 0.76f, 0.82f), accent, 0.24f),
            atmosphere = Color.HSVToRGB(hue, Mathf.Min(1f, saturation * 1.15f), Mathf.Max(value, 0.85f)),
            bounce = accent
        };

        return palette;
    }

    // Shifts the active night palette toward day-space without replacing it. Night
    // values are returned unchanged at t = 0 so night mode stays identical.
    public SpacePalette BlendDay(float t)
    {
        if (t <= 0f) return this;

        float layerLift = t * 0.38f;
        float starLift = t * 0.22f;

        return new SpacePalette
        {
            deepVoid = Color.Lerp(deepVoid, DayVoidBase, t),
            voidGlow = Color.Lerp(voidGlow, DayGlowBase, t),
            nebulaNear = Color.Lerp(nebulaNear, Lift(nebulaNear, layerLift), t),
            nebulaFar = Color.Lerp(nebulaFar, Lift(nebulaFar, layerLift * 0.85f), t),
            galaxy = Color.Lerp(galaxy, Lift(galaxy, layerLift * 0.55f), t),
            star = Color.Lerp(star, Color.Lerp(star, DayStarLift, 0.65f), t),
            dust = Color.Lerp(dust, Lift(dust, layerLift * 0.45f), t),
            rock = Color.Lerp(rock, Lift(rock, layerLift * 0.35f), t),
            atmosphere = Color.Lerp(atmosphere, DayAtmoBase, t),
            bounce = Color.Lerp(bounce, Lift(bounce, starLift), t)
        };
    }

    static Color Lift(Color color, float amount) =>
        Color.Lerp(color, Color.white, Mathf.Clamp01(amount));

    public static SpacePalette Lerp(SpacePalette a, SpacePalette b, float t)
    {
        return new SpacePalette
        {
            deepVoid = Color.Lerp(a.deepVoid, b.deepVoid, t),
            voidGlow = Color.Lerp(a.voidGlow, b.voidGlow, t),
            nebulaNear = Color.Lerp(a.nebulaNear, b.nebulaNear, t),
            nebulaFar = Color.Lerp(a.nebulaFar, b.nebulaFar, t),
            galaxy = Color.Lerp(a.galaxy, b.galaxy, t),
            star = Color.Lerp(a.star, b.star, t),
            dust = Color.Lerp(a.dust, b.dust, t),
            rock = Color.Lerp(a.rock, b.rock, t),
            atmosphere = Color.Lerp(a.atmosphere, b.atmosphere, t),
            bounce = Color.Lerp(a.bounce, b.bounce, t)
        };
    }

    static float Wrap(float hue) => hue - Mathf.Floor(hue);
}
