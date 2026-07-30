using UnityEngine;

// Bioluminescent undergrowth ambience for Mushroom_01..Mushroom_10.
// Decorative only: no colliders, no triggers and no gameplay-affecting forces.
public sealed class MushroomPlanetAmbience : PlanetAmbienceKit
{
    // Acid lime: the one hue no other world claims (Natural is grass green,
    // Ocean cyan, Crystal violet), and SpacePalette builds the whole sky from it.
    static readonly Color AuraTint = new Color(0.58f, 1f, 0.08f, 1f);
    static readonly Color Spore = new Color(0.80f, 1f, 0.45f, 0.90f);
    static readonly Color Aqua = new Color(0.30f, 1f, 0.72f, 0.88f);
    static readonly Color Lime = new Color(0.68f, 1f, 0.22f, 0.92f);
    static readonly Color Crimson = new Color(0.86f, 0.20f, 0.30f, 0.86f);
    static readonly Color Magenta = new Color(0.80f, 0.24f, 0.72f, 0.86f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<MushroomPlanetAmbience>(
            "Mushroom", "Mushroom", AuraTint));
    }

    protected override void Build()
    {
        int index = Mathf.Abs(PlanetIndex) % 10;
        float draft = Variant.Range(0.04f, 0.10f) * Variant.Sign;

        // Spores rise off every colony — the signature motion of the world.
        AddDrift(SporeRise(Spore, Lime, draft, 16));

        switch (index)
        {
            case 1: // Fairy ring: the lit circle pulses like a slow heartbeat.
                MultiplyTint(new Color(0.97f, 1f, 0.96f));
                AddHalo(Lime, 0.150f, 0.300f, 2.10f, 0.66f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Spore, 7, 0.205f, 1.6f, 3.2f, 0.96f);
                break;

            case 2: // Spore pool: aqua light breathing off the water.
                MultiplyTint(new Color(0.95f, 1f, 0.99f));
                AddHalo(Aqua, 0.145f, 0.290f, 2.15f, 0.58f);
                AddBreath(new Color(0.72f, 1f, 0.92f), 0.58f, 0.52f);
                break;

            case 3: // Toadstool arch: light strung under the caps.
                MultiplyTint(new Color(1f, 0.99f, 0.96f));
                AddSheen(Lime, 0.225f, 1.55f, 2.8f, 4.6f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Spore, 6, 0.195f, 1.8f, 3.6f, 0.92f);
                break;

            case 4: // Spore tower: a steady drift climbing the shelves.
                MultiplyTint(new Color(0.98f, 1f, 0.96f));
                AddDrift(SporeRise(Lime, Aqua, draft * 1.3f, 20));
                AddSheen(Spore, 0.210f, 1.70f, 3.0f, 4.8f);
                break;

            case 5: // Colony bloom: insects working the caps.
                MultiplyTint(new Color(1f, 0.98f, 0.97f));
                AddOrbiters(AmbienceVfxAssets.Butterfly, Spore, 5, 0.160f, 0.48f, 0.88f, true);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Aqua, 6, 0.190f, 1.7f, 3.4f, 0.90f);
                break;

            case 6: // Puffball field: bursts of spore, then quiet.
                MultiplyTint(new Color(0.99f, 1f, 0.96f));
                AddDrift(SporeBurst(Spore, Lime, draft));
                AddHalo(Spore, 0.130f, 0.265f, 2.05f, 0.72f);
                break;

            case 7: // Mycelium web: the threads carry light between the caps.
                MultiplyTint(new Color(0.97f, 1f, 0.98f));
                AddSheen(Aqua, 0.240f, 1.40f, 2.4f, 3.9f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Lime, 8, 0.215f, 1.4f, 2.9f, 0.98f);
                break;

            case 8: // Sunken hollow: damp, still, one drifting insect.
                MultiplyTint(new Color(0.96f, 1f, 0.98f));
                AddHalo(Aqua, 0.140f, 0.275f, 2.20f, 0.54f);
                AddOrbiters(AmbienceVfxAssets.Butterfly, Aqua, 4, 0.150f, 0.42f, 0.76f, true);
                break;

            case 9: // Mother cap: the finale, lit from underneath.
                MultiplyTint(new Color(1f, 0.98f, 0.99f));
                AddHalo(Magenta, 0.155f, 0.300f, 2.25f, 0.56f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Spore, 7, 0.210f, 1.6f, 3.1f, 0.96f);
                AddDrift(SporeRise(Magenta, Spore, draft * 0.8f, 18));
                break;

            default: // Great cap: a heavy spore fall from one enormous crown.
                MultiplyTint(new Color(1f, 0.98f, 0.96f));
                AddDrift(SporeRise(Crimson, Spore, draft * 1.2f, 20));
                AddHalo(Lime, 0.135f, 0.270f, 2.10f, 0.62f);
                break;
        }
    }

    // Spores hang and rise rather than fall — the opposite of Sakura's petals,
    // which is what sells the damp, still air of the undergrowth.
    static DriftSettings SporeRise(Color a, Color b, float draft, int maxParticles)
    {
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.SoftDot,
            colorA = a,
            colorB = b,
            mode = DriftMode.Float,
            sizeMin = 0.075f,
            sizeMax = 0.150f,
            speedMin = 0.075f,
            speedMax = 0.165f,
            wind = new Vector2(draft, 0.075f),
            lifetime = 4.6f,
            intervalMin = 0.18f,
            intervalMax = 0.36f,
            fieldRadius = 0.92f,
            spread = 1.05f,
            maxParticles = maxParticles
        };
    }

    static DriftSettings SporeBurst(Color a, Color b, float draft)
    {
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.SoftDot,
            colorA = a,
            colorB = b,
            mode = DriftMode.Gust,
            sizeMin = 0.085f,
            sizeMax = 0.175f,
            wind = new Vector2(draft >= 0f ? 0.105f : -0.105f, 0.045f),
            lifetime = 3.6f,
            spread = 1.05f,
            maxParticles = 22,
            gustCountMin = 9,
            gustCountMax = 14,
            gustIntervalMin = 2.2f,
            gustIntervalMax = 4.2f
        };
    }
}
