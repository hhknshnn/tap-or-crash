using UnityEngine;

// High-altitude sky ambience for Cloud_01..Cloud_10.
// Decorative only: no colliders, no triggers and no gameplay-affecting forces.
public sealed class CloudPlanetAmbience : PlanetAmbienceKit
{
    // Pale periwinkle: the least saturated accent in the game, which is what
    // gives Cloud the brightest, haziest sky instead of Ice's saturated blue.
    static readonly Color AuraTint = new Color(0.80f, 0.87f, 1f, 1f);
    static readonly Color Mist = new Color(1f, 1f, 1f, 0.85f);
    static readonly Color SkyBlue = new Color(0.62f, 0.78f, 1f, 0.88f);
    static readonly Color Gold = new Color(1f, 0.84f, 0.42f, 0.86f);
    static readonly Color Sunset = new Color(1f, 0.66f, 0.66f, 0.84f);
    static readonly Color Storm = new Color(0.42f, 0.48f, 0.68f, 0.90f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<CloudPlanetAmbience>(
            "Cloud", "Cloud", AuraTint));
    }

    protected override void Build()
    {
        int index = Mathf.Abs(PlanetIndex) % 10;
        float wind = Variant.Range(0.09f, 0.18f) * Variant.Sign;

        // Every sky world is windy: mist streams across the disc constantly.
        AddDrift(MistStream(Mist, SkyBlue, wind, 16));
        AddSheen(Mist, 0.200f, 1.50f, 2.6f, 4.4f);

        switch (index)
        {
            case 1: // Vapour rings: the bands catch and lose the light.
                MultiplyTint(new Color(1f, 0.99f, 0.98f));
                AddHalo(Mist, 0.130f, 0.255f, 2.15f, 0.60f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Gold, 5, 0.195f, 1.9f, 3.6f, 0.88f);
                break;

            case 2: // Rainbow arch: bright motes crossing the colours.
                MultiplyTint(new Color(1f, 0.99f, 1f));
                AddTwinkles(AmbienceVfxAssets.Sparkle, Sunset, 7, 0.205f, 1.6f, 3.2f, 0.94f);
                AddCrossing(AmbienceVfxAssets.Bird, new Color(0.36f, 0.40f, 0.54f, 0.95f),
                    0.155f, 3.2f, 6.0f, 0.44f, true);
                break;

            case 3: // Storm cell: the one dark planet, lit from inside.
                MultiplyTint(new Color(0.95f, 0.96f, 1f));
                AddHalo(Storm, 0.165f, 0.320f, 2.20f, 0.90f);
                AddDrift(MistStream(Storm, SkyBlue, wind * 1.6f, 20));
                break;

            case 4: // Balloon dock: slow traffic drifting around the deck.
                MultiplyTint(new Color(1f, 0.98f, 0.97f));
                AddOrbiters(AmbienceVfxAssets.Butterfly, Sunset, 4, 0.155f, 0.38f, 0.68f, true);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Gold, 5, 0.190f, 2.0f, 3.8f, 0.86f);
                break;

            case 5: // Cloud spire: updraft, so the mist climbs instead of crossing.
                MultiplyTint(new Color(1f, 0.99f, 0.98f));
                AddDrift(Updraft(Mist, Gold, wind * 0.5f, 18));
                AddHalo(Gold, 0.120f, 0.240f, 2.05f, 0.66f);
                break;

            case 6: // Sun shrine: the warmest, brightest planet of the set.
                MultiplyTint(new Color(1f, 0.97f, 0.92f));
                AddHalo(Gold, 0.155f, 0.300f, 2.10f, 0.58f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Gold, 8, 0.215f, 1.5f, 3.0f, 0.98f);
                break;

            case 7: // Mist fall: a heavy curtain spilling off the ledge.
                MultiplyTint(new Color(0.98f, 0.99f, 1f));
                AddDrift(MistFall(Mist, SkyBlue, wind * 0.6f, 20));
                AddSheen(SkyBlue, 0.225f, 1.40f, 2.4f, 3.8f);
                break;

            case 8: // Floating isle: birds circling the rock.
                MultiplyTint(new Color(0.99f, 0.99f, 1f));
                AddOrbiters(AmbienceVfxAssets.Bird, new Color(0.38f, 0.42f, 0.56f, 0.95f),
                    5, 0.140f, 0.46f, 0.84f, true);
                AddHalo(SkyBlue, 0.115f, 0.230f, 2.20f, 0.54f);
                break;

            case 9: // Sky citadel: the finale — banners, glare and traffic.
                MultiplyTint(new Color(1f, 0.98f, 0.96f));
                AddHalo(Gold, 0.150f, 0.290f, 2.25f, 0.56f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Mist, 6, 0.200f, 1.7f, 3.3f, 0.92f);
                AddCrossing(AmbienceVfxAssets.Bird, new Color(0.34f, 0.38f, 0.52f, 0.95f),
                    0.150f, 3.6f, 6.4f, 0.42f, true);
                break;

            default: // Sky temple: still air, gold light on white stone.
                MultiplyTint(new Color(1f, 0.99f, 0.97f));
                AddHalo(Gold, 0.125f, 0.245f, 2.10f, 0.62f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Mist, 5, 0.190f, 1.9f, 3.7f, 0.90f);
                break;
        }
    }

    // Wind-driven: mist crosses the disc rather than falling like Sakura's
    // petals or hanging like Mushroom's spores.
    static DriftSettings MistStream(Color a, Color b, float wind, int maxParticles)
    {
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.SoftDot,
            colorA = a,
            colorB = b,
            mode = DriftMode.Float,
            sizeMin = 0.130f,
            sizeMax = 0.280f,
            speedMin = 0.110f,
            speedMax = 0.230f,
            wind = new Vector2(wind, 0.010f),
            lifetime = 4.0f,
            intervalMin = 0.20f,
            intervalMax = 0.40f,
            fieldRadius = 0.95f,
            spread = 1.10f,
            maxParticles = maxParticles
        };
    }

    static DriftSettings Updraft(Color a, Color b, float wind, int maxParticles)
    {
        DriftSettings settings = MistStream(a, b, wind, maxParticles);
        settings.wind = new Vector2(wind, 0.190f);
        settings.speedMin = 0.140f;
        settings.speedMax = 0.280f;
        return settings;
    }

    static DriftSettings MistFall(Color a, Color b, float wind, int maxParticles)
    {
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.SoftDot,
            colorA = a,
            colorB = b,
            mode = DriftMode.Fall,
            sizeMin = 0.120f,
            sizeMax = 0.250f,
            speedMin = 0.190f,
            speedMax = 0.340f,
            wind = new Vector2(wind, 0f),
            lifetime = 3.8f,
            intervalMin = 0.18f,
            intervalMax = 0.34f,
            spawnHeight = 1.05f,
            spread = 0.90f,
            maxParticles = maxParticles
        };
    }
}
