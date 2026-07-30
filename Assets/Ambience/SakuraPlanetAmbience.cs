using UnityEngine;

// Spring blossom ambience for Sakura_01..Sakura_10.
// Decorative only: no colliders, no triggers and no gameplay-affecting forces.
public sealed class SakuraPlanetAmbience : PlanetAmbienceKit
{
    // Saturated rose, not pale blush: SpacePalette derives the whole sky from this
    // accent, and a washed-out pink came back as generic space violet.
    static readonly Color AuraTint = new Color(1f, 0.38f, 0.62f, 1f);
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
        AddDrift(PetalFall(Blossom, PaleBlossom, breeze, 16));

        switch (index)
        {
            case 1: // Torii gate: a warm shrine glow under the crossbeam.
                MultiplyTint(new Color(1f, 0.97f, 0.97f));
                AddHalo(Vermilion, 0.130f, 0.260f, 2.10f, 0.62f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Lantern, 6, 0.210f, 1.8f, 3.4f, 0.95f);
                break;

            case 2: // Pagoda: still air, only the spire catching the light.
                MultiplyTint(new Color(1f, 0.98f, 0.98f));
                AddSheen(PaleBlossom, 0.230f, 1.55f, 2.8f, 4.6f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Lantern, 5, 0.200f, 1.9f, 3.6f, 0.92f);
                break;

            case 3: // Moon bridge: butterflies over the pond.
                MultiplyTint(new Color(0.99f, 0.99f, 1f));
                AddOrbiters(AmbienceVfxAssets.Butterfly, PaleBlossom, 5, 0.170f, 0.52f, 0.90f, true);
                AddDrift(PetalFall(PaleBlossom, Blossom, breeze * 0.7f, 12));
                break;

            case 4: // Lantern path: the warmest planet of the set.
                MultiplyTint(new Color(1f, 0.96f, 0.92f));
                AddHalo(Lantern, 0.145f, 0.285f, 2.05f, 0.70f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Lantern, 8, 0.220f, 1.5f, 3.0f, 0.98f);
                break;

            case 5: // Blossom grove: the heaviest petal fall.
                MultiplyTint(new Color(1f, 0.95f, 0.97f));
                AddDrift(PetalFall(Magenta, Blossom, breeze * 1.25f, 20));
                AddSheen(Blossom, 0.240f, 1.45f, 2.6f, 4.2f);
                break;

            case 6: // Weeping sakura: strands stirring in a gust.
                MultiplyTint(new Color(1f, 0.96f, 0.98f));
                AddDrift(PetalGust(Blossom, PaleBlossom, breeze));
                AddBreath(new Color(1f, 0.72f, 0.84f), 0.62f, 0.60f);
                break;

            case 7: // Zen garden: the quietest planet, but still never static.
                MultiplyTint(new Color(0.99f, 0.98f, 0.98f));
                AddCrossing(AmbienceVfxAssets.Bird, new Color(0.30f, 0.20f, 0.28f, 0.95f),
                    0.165f, 3.4f, 6.5f, 0.40f, true);
                AddSheen(PaleBlossom, 0.200f, 1.70f, 3.2f, 5.2f);
                break;

            case 8: // Petal falls: a constant curtain drifting off the ledge.
                MultiplyTint(new Color(1f, 0.96f, 0.98f));
                AddDrift(PetalFall(Blossom, Magenta, breeze * 1.4f, 20));
                AddHalo(Blossom, 0.135f, 0.265f, 2.15f, 0.58f);
                break;

            case 9: // Shrine: the ceremonial finale.
                MultiplyTint(new Color(1f, 0.96f, 0.96f));
                AddHalo(Vermilion, 0.150f, 0.295f, 2.20f, 0.56f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Lantern, 7, 0.215f, 1.7f, 3.2f, 0.96f);
                AddDrift(PetalFall(PaleBlossom, Magenta, breeze * 0.8f, 16));
                break;

            default: // Great sakura: butterflies circling the crown.
                MultiplyTint(new Color(1f, 0.97f, 0.98f));
                AddOrbiters(AmbienceVfxAssets.Butterfly, Blossom, 6, 0.165f, 0.50f, 0.92f, true);
                AddSheen(PaleBlossom, 0.215f, 1.60f, 3.0f, 4.8f);
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
            // Petal fall is this world's signature motion, so it is emitted large
            // and often enough to be unmistakable at gameplay planet size.
            sizeMin = 0.105f,
            sizeMax = 0.190f,
            speedMin = 0.170f,
            speedMax = 0.320f,
            wind = new Vector2(wind, 0f),
            lifetime = 4.2f,
            intervalMin = 0.16f,
            intervalMax = 0.34f,
            spawnHeight = 1.10f,
            spread = 1.05f,
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
            sizeMin = 0.095f,
            sizeMax = 0.175f,
            wind = new Vector2(wind >= 0f ? 0.115f : -0.115f, 0.02f),
            lifetime = 3.4f,
            spread = 1.05f,
            maxParticles = 20,
            gustCountMin = 8,
            gustCountMax = 13,
            gustIntervalMin = 2.4f,
            gustIntervalMax = 4.6f
        };
    }
}
