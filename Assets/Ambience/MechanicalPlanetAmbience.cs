using UnityEngine;

// Industrial machine ambience for Mechanical_01..Mechanical_10.
// Decorative only: no colliders, no triggers and no gameplay-affecting forces.
public sealed class MechanicalPlanetAmbience : PlanetAmbienceKit
{
    // Signal red. Lava owns molten orange, but its planets are black rock while
    // these are grey steel, so the two skies never get confused in play.
    static readonly Color AuraTint = new Color(0.92f, 0.16f, 0.20f, 1f);
    static readonly Color Steam = new Color(0.82f, 0.86f, 0.94f, 0.78f);
    static readonly Color Ember = new Color(1f, 0.52f, 0.14f, 0.92f);
    static readonly Color Coolant = new Color(0.28f, 0.94f, 1f, 0.90f);
    static readonly Color Warning = new Color(0.94f, 0.20f, 0.16f, 0.95f);
    static readonly Color Status = new Color(0.66f, 0.98f, 0.42f, 0.92f);
    static readonly Color Chrome = new Color(0.78f, 0.82f, 0.90f, 0.85f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<MechanicalPlanetAmbience>(
            "Mechanical", "Mechanical", AuraTint));
    }

    protected override void Build()
    {
        int index = Mathf.Abs(PlanetIndex) % 10;
        float draft = Variant.Range(0.05f, 0.11f) * Variant.Sign;

        // Every machine world vents steam and blinks. Both are the read that
        // says "running", which no static hard-surface sprite can carry alone.
        AddDrift(VentSteam(Steam, Chrome, draft, 14));
        AddTwinkles(AmbienceVfxAssets.Sparkle, Warning, 5, 0.170f, 1.2f, 2.4f, 0.98f);

        switch (index)
        {
            case 1: // Reactor core: cold cell light, coolant haze.
                MultiplyTint(new Color(0.94f, 0.98f, 1f));
                AddHalo(Coolant, 0.155f, 0.300f, 2.15f, 0.86f);
                AddDrift(VentSteam(Coolant, Steam, draft * 0.7f, 16));
                break;

            case 2: // Piston bank: hard mechanical rhythm.
                MultiplyTint(new Color(1f, 0.99f, 0.98f));
                AddBreath(new Color(1f, 0.86f, 0.72f), 1.85f, 0.34f);
                AddSheen(Chrome, 0.215f, 1.15f, 1.8f, 3.0f);
                break;

            case 3: // Radar array: a sweeping glint and marker lights.
                MultiplyTint(new Color(0.98f, 0.99f, 1f));
                AddSheen(Chrome, 0.250f, 1.05f, 1.6f, 2.6f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Status, 6, 0.185f, 1.4f, 2.8f, 0.96f);
                break;

            case 4: // Foundry: the hottest planet of the set.
                MultiplyTint(new Color(1f, 0.94f, 0.88f));
                AddHalo(Ember, 0.170f, 0.330f, 2.10f, 0.78f);
                AddDrift(SparkBurst(Ember, Warning, draft));
                break;

            case 5: // Gear train: chrome glints running down the chain.
                MultiplyTint(new Color(1f, 0.99f, 0.97f));
                AddSheen(Chrome, 0.240f, 1.10f, 1.5f, 2.5f);
                AddOrbiters(AmbienceVfxAssets.Shard, Chrome, 4, 0.105f, 0.62f, 1.05f, false);
                break;

            case 6: // Antenna farm: dense blinking traffic.
                MultiplyTint(new Color(0.99f, 1f, 0.99f));
                AddTwinkles(AmbienceVfxAssets.Sparkle, Status, 8, 0.190f, 1.1f, 2.2f, 1f);
                AddOrbiters(AmbienceVfxAssets.Shard, Coolant, 4, 0.100f, 0.58f, 0.98f, false);
                break;

            case 7: // Coolant tanks: cyan pressure haze.
                MultiplyTint(new Color(0.95f, 0.99f, 1f));
                AddDrift(VentSteam(Coolant, Steam, draft * 1.2f, 18));
                AddHalo(Coolant, 0.140f, 0.275f, 2.20f, 0.66f);
                break;

            case 8: // Wreck: dead and dark, one beacon still turning.
                MultiplyTint(new Color(0.92f, 0.92f, 0.95f));
                AddHalo(Warning, 0.100f, 0.290f, 2.05f, 1.35f);
                AddDrift(VentSteam(Steam, Steam, draft * 0.4f, 8));
                break;

            case 9: // Engine core: the finale — heat, sparks and spin.
                MultiplyTint(new Color(1f, 0.96f, 0.92f));
                AddHalo(Ember, 0.165f, 0.320f, 2.25f, 0.72f);
                AddDrift(SparkBurst(Ember, Status, draft * 1.3f));
                AddOrbiters(AmbienceVfxAssets.Shard, Chrome, 5, 0.110f, 0.66f, 1.12f, false);
                break;

            default: // Great gear: heavy machinery turning under a steam plume.
                MultiplyTint(new Color(1f, 0.98f, 0.95f));
                AddDrift(VentSteam(Steam, Ember, draft * 1.1f, 18));
                AddSheen(Chrome, 0.225f, 1.20f, 1.9f, 3.2f);
                break;
        }
    }

    // Steam rises off the stacks: slow, large and short-lived, so it reads as
    // exhaust rather than as Mushroom's hanging spores.
    static DriftSettings VentSteam(Color a, Color b, float draft, int maxParticles)
    {
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.SoftDot,
            colorA = a,
            colorB = b,
            mode = DriftMode.Float,
            sizeMin = 0.105f,
            sizeMax = 0.215f,
            speedMin = 0.110f,
            speedMax = 0.215f,
            wind = new Vector2(draft, 0.135f),
            lifetime = 3.4f,
            intervalMin = 0.17f,
            intervalMax = 0.33f,
            fieldRadius = 0.90f,
            spread = 1.05f,
            maxParticles = maxParticles
        };
    }

    static DriftSettings SparkBurst(Color a, Color b, float draft)
    {
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.Sparkle,
            colorA = a,
            colorB = b,
            mode = DriftMode.Gust,
            sizeMin = 0.055f,
            sizeMax = 0.115f,
            wind = new Vector2(draft >= 0f ? 0.145f : -0.145f, 0.085f),
            lifetime = 1.9f,
            spread = 0.95f,
            maxParticles = 24,
            gustCountMin = 10,
            gustCountMax = 16,
            gustIntervalMin = 1.8f,
            gustIntervalMax = 3.4f
        };
    }
}
