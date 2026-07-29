using UnityEngine;

// Fantasy-desert ambience for Desert_01..Desert_10.
// All effects are decorative, collider-free, pooled by PlanetAmbienceKit and disabled off-screen.
public sealed class DesertPlanetAmbience : PlanetAmbienceKit
{
    static readonly Color AuraTint = new Color(1f, 0.70f, 0.28f, 1f);
    static readonly Color Sand = new Color(1f, 0.76f, 0.34f, 0.42f);
    static readonly Color PaleSand = new Color(1f, 0.91f, 0.66f, 0.34f);
    static readonly Color Turquoise = new Color(0.30f, 0.90f, 0.94f, 0.9f);
    static readonly Color Coral = new Color(1f, 0.42f, 0.46f, 0.82f);
    static readonly Color Green = new Color(0.36f, 0.88f, 0.48f, 0.82f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<DesertPlanetAmbience>(
            "Desert", "Desert", AuraTint));
    }

    protected override void Build()
    {
        int index = Mathf.Abs(PlanetIndex) % 10;
        float wind = Variant.Range(0.10f, 0.22f) * Variant.Sign;

        switch (index)
        {
            case 1: // Oasis ripples: cool sheen and sparse suspended moisture.
                MultiplyTint(new Color(1f, 0.98f, 0.90f));
                AddSheen(Turquoise, 0.09f, 2.7f, 4.5f, 8f);
                AddTwinkles(AmbienceVfxAssets.SoftDot, Turquoise, 3, 0.08f, 3.4f, 5.2f, 0.38f);
                break;

            case 2: // Cactus blossom drift.
                MultiplyTint(new Color(1f, 0.96f, 0.86f));
                AddDrift(Drift(AmbienceVfxAssets.Petal, Coral, Green, DriftMode.Float,
                    wind * 0.25f, 0.045f, 0.085f, 0.55f, 12));
                break;

            case 3: // Temple dust motes.
                MultiplyTint(new Color(1f, 0.94f, 0.82f));
                AddDrift(Drift(AmbienceVfxAssets.SoftDot, PaleSand, Sand, DriftMode.Float,
                    wind * 0.18f, 0.025f, 0.055f, 0.42f, 10));
                AddBreath(new Color(1f, 0.97f, 0.90f), 0.55f, 0.28f);
                break;

            case 4: // Canyon gust.
                MultiplyTint(new Color(1f, 0.91f, 0.78f));
                AddDrift(Drift(AmbienceVfxAssets.Shard, Sand, PaleSand, DriftMode.Gust,
                    wind, 0.025f, 0.060f, 0.55f, 16));
                break;

            case 5: // Arch heat shimmer.
                MultiplyTint(new Color(1f, 0.95f, 0.83f));
                AddSheen(new Color(1f, 0.84f, 0.58f, 1f), 0.075f, 1.6f, 3.8f, 7.2f);
                AddBreath(new Color(1f, 0.94f, 0.86f), 0.80f, 0.34f);
                break;

            case 6: // Crystal sparkle.
                MultiplyTint(new Color(0.98f, 0.97f, 0.92f));
                AddTwinkles(AmbienceVfxAssets.Sparkle, Turquoise, 6, 0.14f, 2.2f, 4.8f, 0.88f);
                AddHalo(new Color(0.28f, 0.82f, 0.88f, 1f), 0.035f, 0.09f, 2.25f, 0.75f);
                break;

            case 7: // Palm-leaf sway, represented without changing gameplay bounds.
                MultiplyTint(new Color(1f, 0.97f, 0.86f));
                AddDrift(Drift(AmbienceVfxAssets.Leaf, Green, PaleSand, DriftMode.Fall,
                    wind * 0.55f, 0.055f, 0.105f, 0.42f, 9));
                AddBreath(new Color(0.95f, 1f, 0.91f), 1.05f, 0.36f);
                break;

            case 8: // Wind-ripple drift.
                MultiplyTint(new Color(1f, 0.93f, 0.78f));
                AddDrift(Drift(AmbienceVfxAssets.SoftDot, PaleSand, Sand, DriftMode.Gust,
                    wind * 1.18f, 0.025f, 0.050f, 0.48f, 18));
                AddSheen(PaleSand, 0.065f, 2.0f, 2.8f, 5.8f);
                break;

            case 9: // Tiny ruin fireflies.
                MultiplyTint(new Color(1f, 0.92f, 0.80f));
                AddOrbiters(AmbienceVfxAssets.SoftDot,
                    new Color(1f, 0.88f, 0.36f, 0.82f), 3, 0.055f, 0.55f, 0.92f, false);
                AddTwinkles(AmbienceVfxAssets.Sparkle, PaleSand, 3, 0.10f, 3.0f, 5.8f, 0.52f);
                break;

            default: // Desert_10: solar dust, controlled aura and ceremonial sparkle.
                MultiplyTint(new Color(1f, 0.94f, 0.82f));
                AddHalo(new Color(1f, 0.60f, 0.20f, 1f), 0.055f, 0.12f, 2.32f, 0.62f);
                AddDrift(Drift(AmbienceVfxAssets.Sparkle, PaleSand, Turquoise, DriftMode.Float,
                    wind * 0.18f, 0.035f, 0.075f, 0.50f, 12));
                AddSheen(new Color(1f, 0.88f, 0.56f, 1f), 0.085f, 1.85f, 4.2f, 8.0f);
                break;
        }
    }

    static DriftSettings Drift(Sprite sprite, Color a, Color b, DriftMode mode,
        float wind, float minSize, float maxSize, float alphaScale, int maxParticles)
    {
        a.a *= alphaScale;
        b.a *= alphaScale;
        return new DriftSettings
        {
            sprite = sprite,
            colorA = a,
            colorB = b,
            mode = mode,
            sizeMin = minSize,
            sizeMax = maxSize,
            speedMin = mode == DriftMode.Gust ? 0.18f : 0.05f,
            speedMax = mode == DriftMode.Gust ? 0.38f : 0.14f,
            wind = new Vector2(wind, mode == DriftMode.Float ? 0.025f : 0f),
            lifetime = mode == DriftMode.Gust ? 1.5f : 3.4f,
            intervalMin = 0.48f,
            intervalMax = 1.15f,
            fieldRadius = 0.88f,
            spread = 1.15f,
            gustCountMin = 4,
            gustCountMax = 7,
            gustIntervalMin = 4.8f,
            gustIntervalMax = 8.5f,
            maxParticles = maxParticles
        };
    }
}
