using UnityEngine;

// Premium crystal ambience for Crystal_01..Crystal_10.
// Decorative only: no colliders, no triggers and no gameplay-affecting forces.
public sealed class CrystalPlanetAmbience : PlanetAmbienceKit
{
    static readonly Color AuraTint = new Color(0.62f, 0.42f, 1f, 1f);
    static readonly Color Amethyst = new Color(0.68f, 0.46f, 1f, 0.90f);
    static readonly Color Cyan = new Color(0.36f, 0.92f, 1f, 0.88f);
    static readonly Color Ice = new Color(0.84f, 0.98f, 1f, 0.92f);
    static readonly Color Pink = new Color(1f, 0.48f, 0.78f, 0.86f);
    static readonly Color Gold = new Color(1f, 0.84f, 0.38f, 0.82f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<CrystalPlanetAmbience>(
            "Crystal", "Crystal", AuraTint));
    }

    protected override void Build()
    {
        int index = Mathf.Abs(PlanetIndex) % 10;
        float drift = Variant.Range(0.025f, 0.07f) * Variant.Sign;

        // Every crystal world receives a restrained refractive sweep and sparse motes.
        AddSheen(Ice, 0.145f, 1.65f, 4.2f, 7.0f);
        AddDrift(CrystalDust(Cyan, Amethyst, drift, 7));

        switch (index)
        {
            case 1: // Crown: slow orbiting splinters around the royal spire.
                MultiplyTint(new Color(0.98f, 0.96f, 1f));
                AddOrbiters(AmbienceVfxAssets.Shard, Cyan, 4, 0.105f, 0.62f, 0.98f, false);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Ice, 4, 0.165f, 2.2f, 4.4f, 0.86f);
                break;

            case 2: // Geode: internal glow breathing through the dark core.
                MultiplyTint(new Color(0.94f, 0.93f, 1f));
                AddHalo(Amethyst, 0.095f, 0.190f, 2.18f, 0.66f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Pink, 5, 0.155f, 2.1f, 4.2f, 0.90f);
                break;

            case 3: // Prism gate: broad crossing refraction.
                MultiplyTint(new Color(0.94f, 1f, 1f));
                AddSheen(Cyan, 0.185f, 1.48f, 4.8f, 7.8f);
                AddOrbiters(AmbienceVfxAssets.Shard, Ice, 3, 0.095f, 0.58f, 0.90f, false);
                break;

            case 4: // Levitating prism: satellites and a cool suspended aura.
                MultiplyTint(new Color(0.91f, 0.99f, 1f));
                AddOrbiters(AmbienceVfxAssets.Shard, Cyan, 5, 0.110f, 0.56f, 1.00f, true);
                AddBreath(new Color(0.50f, 0.94f, 1f), 0.72f, 0.44f);
                break;

            case 5: // Crescent shrine: moonlike pulse with a gold accent.
                MultiplyTint(new Color(0.96f, 0.98f, 1f));
                AddHalo(Cyan, 0.085f, 0.175f, 2.20f, 0.74f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Gold, 4, 0.160f, 2.4f, 4.8f, 0.84f);
                break;

            case 6: // Crystal bloom: petal-like sparks opening around the core.
                MultiplyTint(new Color(1f, 0.94f, 0.99f));
                AddTwinkles(AmbienceVfxAssets.Sparkle, Pink, 7, 0.175f, 1.8f, 3.8f, 0.94f);
                AddOrbiters(AmbienceVfxAssets.Shard, Amethyst, 3, 0.090f, 0.52f, 0.84f, false);
                break;

            case 7: // Twin obelisks: alternating cool pulses.
                MultiplyTint(new Color(0.94f, 0.97f, 1f));
                AddSheen(Amethyst, 0.165f, 1.55f, 4.5f, 7.2f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Cyan, 4, 0.150f, 2.4f, 4.8f, 0.82f);
                break;

            case 8: // Heart gem: warm magical sparkle kept sparse and premium.
                MultiplyTint(new Color(1f, 0.93f, 0.98f));
                AddHalo(Pink, 0.095f, 0.185f, 2.14f, 0.64f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Pink, 6, 0.175f, 1.9f, 4.0f, 0.94f);
                break;

            case 9: // Observatory: a measured orbit around the central lens.
                MultiplyTint(new Color(0.92f, 0.98f, 1f));
                AddOrbiters(AmbienceVfxAssets.Shard, Ice, 5, 0.105f, 0.58f, 0.96f, true);
                AddSheen(Cyan, 0.165f, 1.52f, 4.6f, 7.6f);
                break;

            default: // Cathedral: ceremonial halo, shimmer and restrained starlight.
                MultiplyTint(new Color(0.96f, 0.95f, 1f));
                AddHalo(Amethyst, 0.105f, 0.205f, 2.30f, 0.60f);
                AddSheen(Ice, 0.185f, 1.45f, 4.8f, 8.0f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Gold, 5, 0.165f, 2.2f, 4.6f, 0.88f);
                AddDrift(CrystalDust(Pink, Cyan, drift * 0.75f, 10));
                break;
        }
    }

    static DriftSettings CrystalDust(Color a, Color b, float wind, int maxParticles)
    {
        a.a *= 0.68f;
        b.a *= 0.68f;
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.Shard,
            colorA = a,
            colorB = b,
            mode = DriftMode.Float,
            sizeMin = 0.055f,
            sizeMax = 0.115f,
            speedMin = 0.040f,
            speedMax = 0.105f,
            wind = new Vector2(wind, 0.035f),
            lifetime = 3.8f,
            intervalMin = 0.38f,
            intervalMax = 0.78f,
            fieldRadius = 0.88f,
            spread = 1.04f,
            maxParticles = maxParticles
        };
    }
}
