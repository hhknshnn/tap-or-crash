using UnityEngine;

// Biomechanical hive ambience for Alien_01..Alien_10.
// Decorative only: no colliders, no triggers and no gameplay-affecting forces.
public sealed class AlienPlanetAmbience : PlanetAmbienceKit
{
    // Radioactive magenta. Sakura holds soft rose and Crystal holds violet, but
    // both are pale next to this, and the acid green motes finish the read.
    static readonly Color AuraTint = new Color(0.94f, 0.10f, 0.78f, 1f);
    static readonly Color Acid = new Color(0.74f, 1f, 0.28f, 0.92f);
    static readonly Color Magenta = new Color(1f, 0.22f, 0.72f, 0.92f);
    static readonly Color Cyan = new Color(0.32f, 0.98f, 0.94f, 0.90f);
    static readonly Color Flesh = new Color(0.78f, 0.30f, 0.62f, 0.86f);
    static readonly Color Bone = new Color(0.86f, 0.84f, 0.74f, 0.84f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<AlienPlanetAmbience>(
            "Alien", "Alien", AuraTint));
    }

    protected override void Build()
    {
        int index = Mathf.Abs(PlanetIndex) % 10;
        float drift = Variant.Range(0.04f, 0.10f) * Variant.Sign;

        // Every hive world breathes: spores hang in the air and the hide pulses.
        AddDrift(SporeCloud(Acid, Magenta, drift, 16));
        AddBreath(new Color(1f, 0.72f, 0.92f), 0.48f, 0.55f);

        switch (index)
        {
            case 1: // Hatchery: pods pulsing in sequence.
                MultiplyTint(new Color(1f, 0.97f, 0.99f));
                AddHalo(Acid, 0.150f, 0.300f, 2.10f, 0.74f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Acid, 7, 0.200f, 1.5f, 3.0f, 0.96f);
                break;

            case 2: // Tendril nest: the busiest motion of the set.
                MultiplyTint(new Color(1f, 0.96f, 0.98f));
                AddOrbiters(AmbienceVfxAssets.Shard, Magenta, 5, 0.130f, 0.52f, 0.92f, true);
                AddDrift(SporeCloud(Magenta, Cyan, drift * 1.4f, 18));
                break;

            case 3: // Maw pit: something down there is breathing hard.
                MultiplyTint(new Color(1f, 0.95f, 0.97f));
                AddHalo(Magenta, 0.165f, 0.330f, 2.15f, 1.05f);
                AddSheen(Flesh, 0.190f, 1.45f, 2.6f, 4.2f);
                break;

            case 4: // Hive spire: gland light climbing the tower.
                MultiplyTint(new Color(0.98f, 1f, 0.98f));
                AddDrift(SporeCloud(Acid, Cyan, drift * 0.8f, 18));
                AddTwinkles(AmbienceVfxAssets.Sparkle, Magenta, 6, 0.195f, 1.6f, 3.2f, 0.94f);
                break;

            case 5: // Eye cluster: the blinks, staggered and irregular.
                MultiplyTint(new Color(1f, 0.98f, 1f));
                AddTwinkles(AmbienceVfxAssets.Sparkle, Bone, 8, 0.185f, 1.3f, 2.6f, 1f);
                AddHalo(Cyan, 0.125f, 0.255f, 2.20f, 0.62f);
                break;

            case 6: // Membrane sails: slow drag through the sail film.
                MultiplyTint(new Color(1f, 0.96f, 0.99f));
                AddSheen(Magenta, 0.235f, 1.60f, 2.8f, 4.6f);
                AddOrbiters(AmbienceVfxAssets.Shard, Flesh, 4, 0.120f, 0.44f, 0.80f, true);
                break;

            case 7: // Slime pool: heavy, wet, bubbling light.
                MultiplyTint(new Color(0.97f, 1f, 0.97f));
                AddHalo(Acid, 0.160f, 0.315f, 2.05f, 0.58f);
                AddDrift(SporeCloud(Cyan, Acid, drift * 0.6f, 20));
                break;

            case 8: // Bone arch: the dead planet. Almost nothing moves.
                MultiplyTint(new Color(0.94f, 0.95f, 0.98f));
                AddDrift(SporeCloud(Bone, Flesh, drift * 0.35f, 8));
                AddTwinkles(AmbienceVfxAssets.Sparkle, Magenta, 3, 0.170f, 2.6f, 4.8f, 0.82f);
                break;

            case 9: // Queen node: the finale — everything at once.
                MultiplyTint(new Color(1f, 0.96f, 0.98f));
                AddHalo(Magenta, 0.175f, 0.340f, 2.25f, 0.68f);
                AddOrbiters(AmbienceVfxAssets.Shard, Acid, 5, 0.130f, 0.56f, 1.00f, true);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Acid, 6, 0.200f, 1.4f, 2.8f, 0.98f);
                break;

            default: // Great eye: a slow, watchful pulse.
                MultiplyTint(new Color(1f, 0.97f, 0.99f));
                AddHalo(Cyan, 0.140f, 0.285f, 2.10f, 0.46f);
                AddSheen(Bone, 0.180f, 1.75f, 3.2f, 5.4f);
                break;
        }
    }

    // Spores hang and swirl rather than fall or stream: still, heavy hive air.
    static DriftSettings SporeCloud(Color a, Color b, float drift, int maxParticles)
    {
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.SoftDot,
            colorA = a,
            colorB = b,
            mode = DriftMode.Float,
            sizeMin = 0.070f,
            sizeMax = 0.155f,
            speedMin = 0.065f,
            speedMax = 0.150f,
            wind = new Vector2(drift, 0.045f),
            lifetime = 4.8f,
            intervalMin = 0.18f,
            intervalMax = 0.36f,
            fieldRadius = 0.94f,
            spread = 1.05f,
            maxParticles = maxParticles
        };
    }
}
