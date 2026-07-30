using UnityEngine;

// Spring blossom ambience for Sakura_01..Sakura_10.
// Decorative only: no colliders, no triggers and no gameplay-affecting forces.
public sealed class SakuraPlanetAmbience : PlanetAmbienceKit
{
    static readonly Color AuraTint = new Color(1f, 0.62f, 0.78f, 1f);
    static readonly Color Blossom = new Color(1f, 0.52f, 0.72f, 0.92f);
    static readonly Color PaleBlossom = new Color(1f, 0.82f, 0.88f, 0.88f);
    static readonly Color Magenta = new Color(0.92f, 0.28f, 0.55f, 0.86f);
    static readonly Color Lantern = new Color(1f, 0.80f, 0.42f, 0.84f);
    static readonly Color Vermilion = new Color(0.96f, 0.34f, 0.22f, 0.80f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<SakuraPlanetAmbience>(
            "Sakura", "Sakura", AuraTint));
    }

    protected override void Build()
    {
        int index = Mathf.Abs(PlanetIndex) % 10;
        float breeze = Variant.Range(0.05f, 0.12f) * Variant.Sign;

        // Every sakura world drops petals on a soft, slow breeze.
        AddDrift(PetalFall(Blossom, PaleBlossom, breeze, 9));

        switch (index)
        {
            case 1: // Torii gate: a warm shrine glow under the crossbeam.
                MultiplyTint(new Color(1f, 0.97f, 0.97f));
                AddHalo(Vermilion, 0.070f, 0.150f, 2.10f, 0.62f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Lantern, 4, 0.150f, 2.4f, 4.6f, 0.80f);
                break;

            case 2: // Pagoda: still air, only the spire catching the light.
                MultiplyTint(new Color(1f, 0.98f, 0.98f));
                AddSheen(PaleBlossom, 0.140f, 1.75f, 4.6f, 7.4f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Lantern, 3, 0.140f, 2.6f, 5.0f, 0.76f);
                break;

            case 3: // Moon bridge: butterflies over the pond.
                MultiplyTint(new Color(0.99f, 0.99f, 1f));
                AddOrbiters(AmbienceVfxAssets.Butterfly, PaleBlossom, 3, 0.115f, 0.44f, 0.76f, true);
                AddDrift(PetalFall(PaleBlossom, Blossom, breeze * 0.7f, 7));
                break;

            case 4: // Lantern path: the warmest planet of the set.
                MultiplyTint(new Color(1f, 0.96f, 0.92f));
                AddHalo(Lantern, 0.080f, 0.165f, 2.05f, 0.70f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Lantern, 6, 0.160f, 1.9f, 3.9f, 0.90f);
                break;

            case 5: // Blossom grove: the heaviest petal fall.
                MultiplyTint(new Color(1f, 0.95f, 0.97f));
                AddDrift(PetalFall(Magenta, Blossom, breeze * 1.25f, 12));
                AddSheen(Blossom, 0.150f, 1.60f, 4.2f, 6.8f);
                break;

            case 6: // Weeping sakura: strands stirring in a gust.
                MultiplyTint(new Color(1f, 0.96f, 0.98f));
                AddDrift(PetalGust(Blossom, PaleBlossom, breeze));
                AddBreath(new Color(1f, 0.80f, 0.88f), 0.62f, 0.38f);
                break;

            case 7: // Zen garden: quiet. A single crossing bird and nothing else.
                MultiplyTint(new Color(0.99f, 0.98f, 0.98f));
                AddCrossing(AmbienceVfxAssets.Bird, new Color(0.32f, 0.24f, 0.30f, 0.85f),
                    0.115f, 6.5f, 12.0f, 0.34f, true);
                AddSheen(PaleBlossom, 0.120f, 1.90f, 5.4f, 8.6f);
                break;

            case 8: // Petal falls: a constant curtain drifting off the ledge.
                MultiplyTint(new Color(1f, 0.96f, 0.98f));
                AddDrift(PetalFall(Blossom, Magenta, breeze * 1.4f, 12));
                AddHalo(Blossom, 0.075f, 0.150f, 2.15f, 0.58f);
                break;

            case 9: // Shrine: the ceremonial finale.
                MultiplyTint(new Color(1f, 0.96f, 0.96f));
                AddHalo(Vermilion, 0.085f, 0.170f, 2.20f, 0.56f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Lantern, 5, 0.160f, 2.1f, 4.4f, 0.88f);
                AddDrift(PetalFall(PaleBlossom, Magenta, breeze * 0.8f, 10));
                break;

            default: // Great sakura: butterflies circling the crown.
                MultiplyTint(new Color(1f, 0.97f, 0.98f));
                AddOrbiters(AmbienceVfxAssets.Butterfly, Blossom, 4, 0.110f, 0.42f, 0.78f, true);
                AddSheen(PaleBlossom, 0.135f, 1.70f, 4.8f, 7.6f);
                break;
        }
    }

    static DriftSettings PetalFall(Color a, Color b, float wind, int maxParticles)
    {
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.Petal,
            colorA = a,
            colorB = b,
            mode = DriftMode.Fall,
            sizeMin = 0.060f,
            sizeMax = 0.125f,
            speedMin = 0.135f,
            speedMax = 0.260f,
            wind = new Vector2(wind, 0f),
            lifetime = 4.2f,
            intervalMin = 0.34f,
            intervalMax = 0.72f,
            spawnHeight = 1.10f,
            spread = 0.95f,
            maxParticles = maxParticles
        };
    }

    static DriftSettings PetalGust(Color a, Color b, float wind)
    {
        return new DriftSettings
        {
            sprite = AmbienceVfxAssets.Petal,
            colorA = a,
            colorB = b,
            mode = DriftMode.Gust,
            sizeMin = 0.055f,
            sizeMax = 0.115f,
            wind = new Vector2(wind >= 0f ? 0.085f : -0.085f, 0.02f),
            lifetime = 3.4f,
            spread = 1.05f,
            maxParticles = 14,
            gustCountMin = 5,
            gustCountMax = 9,
            gustIntervalMin = 4.0f,
            gustIntervalMax = 8.0f
        };
    }
}
