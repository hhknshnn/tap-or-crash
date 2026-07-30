using UnityEngine;

// Premium ocean ambience for Ocean_01..Ocean_10.
// Effects are decorative, collider-free and disabled while the planet is off-screen.
public sealed class OceanPlanetAmbience : PlanetAmbienceKit
{
    static readonly Color AuraTint = new Color(0.12f, 0.90f, 1f, 1f);
    static readonly Color Cyan = new Color(0.24f, 0.94f, 1f, 0.88f);
    static readonly Color Foam = new Color(0.92f, 1f, 1f, 0.82f);
    static readonly Color Coral = new Color(1f, 0.42f, 0.56f, 0.86f);
    static readonly Color SeaGreen = new Color(0.20f, 0.82f, 0.52f, 0.82f);
    static readonly Color Crystal = new Color(0.62f, 0.94f, 1f, 0.94f);

    readonly SpriteRenderer[] waveBands = new SpriteRenderer[3];
    readonly Vector2[] waveCenters = new Vector2[3];
    readonly float[] waveWidths = new float[3];
    readonly float[] waveAlphas = { 0.050f, 0.038f, 0.030f };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<OceanPlanetAmbience>(
            "Ocean", "Ocean", AuraTint));
    }

    protected override void Build()
    {
        int index = Mathf.Abs(PlanetIndex) % 10;
        float current = Variant.Range(0.055f, 0.14f) * Variant.Sign;

        BuildWaterSurface();
        AddSheen(Crystal, 0.052f, 3.15f, 1.2f, 2.4f);
        AddDrift(Bubbles(Cyan, Foam, current * 0.08f, 6));

        switch (index)
        {
            case 1: // Crown reef: buoyant bubbles and living coral sparkle.
                MultiplyTint(new Color(0.94f, 1f, 1f));
                AddDrift(Bubbles(Cyan, Foam, current * 0.12f, 12));
                AddTwinkles(AmbienceVfxAssets.Sparkle, Coral, 3, 0.075f, 2.8f, 5.4f, 0.48f);
                break;

            case 2: // Crystal lagoon: broad ripple sheen.
                MultiplyTint(new Color(0.90f, 1f, 1f));
                AddSheen(Crystal, 0.12f, 2.45f, 4.2f, 7.6f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Crystal, 5, 0.12f, 2.2f, 4.8f, 0.82f);
                break;

            case 3: // Palm atoll: leaf sway and soft tropical breath.
                MultiplyTint(new Color(0.96f, 1f, 0.94f));
                AddDrift(Floaters(AmbienceVfxAssets.Leaf, SeaGreen, Foam,
                    DriftMode.Fall, current * 0.45f, 0.055f, 0.10f, 8));
                AddBreath(new Color(0.76f, 1f, 0.94f), 1.0f, 0.34f);
                break;

            case 4: // Coral canyon: cool channel mist.
                MultiplyTint(new Color(0.91f, 0.98f, 1f));
                AddDrift(Floaters(AmbienceVfxAssets.SoftDot, Cyan, Foam,
                    DriftMode.Float, current * 0.28f, 0.025f, 0.06f, 12));
                AddHalo(new Color(0.08f, 0.78f, 0.98f), 0.035f, 0.075f, 2.20f, 0.70f);
                break;

            case 5: // Moon-current arch: passing wave highlight.
                MultiplyTint(new Color(0.94f, 0.99f, 1f));
                AddSheen(Foam, 0.095f, 1.72f, 3.8f, 7.0f);
                AddBreath(new Color(0.62f, 0.94f, 1f), 0.78f, 0.30f);
                break;

            case 6: // Cascade garden: splash spray and shimmer.
                MultiplyTint(new Color(0.91f, 1f, 1f));
                AddDrift(Floaters(AmbienceVfxAssets.SoftDot, Foam, Cyan,
                    DriftMode.Gust, current * 0.22f, 0.028f, 0.065f, 15));
                AddSheen(Cyan, 0.075f, 2.2f, 3.4f, 6.2f);
                break;

            case 7: // Sea-flower paradise: petals ride the current.
                MultiplyTint(new Color(0.96f, 1f, 0.96f));
                AddDrift(Floaters(AmbienceVfxAssets.Petal, Coral, SeaGreen,
                    DriftMode.Float, current * 0.34f, 0.05f, 0.095f, 10));
                AddTwinkles(AmbienceVfxAssets.SoftDot, Cyan, 3, 0.065f, 3.0f, 5.8f, 0.36f);
                break;

            case 8: // Pearl shell: slow pearl glow.
                MultiplyTint(new Color(0.97f, 0.96f, 1f));
                AddHalo(Crystal, 0.055f, 0.12f, 2.14f, 0.62f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Foam, 5, 0.105f, 2.6f, 5.5f, 0.78f);
                break;

            case 9: // Tide observatory: tiny fish-like orbiters.
                MultiplyTint(new Color(0.90f, 0.98f, 1f));
                AddOrbiters(AmbienceVfxAssets.Shard, Cyan, 4, 0.055f, 0.56f, 0.94f, false);
                AddSheen(new Color(0.48f, 0.86f, 1f), 0.065f, 2.0f, 4.4f, 8.0f);
                break;

            default: // Ocean_10: ceremonial tide shimmer.
                MultiplyTint(new Color(0.92f, 0.99f, 1f));
                AddHalo(new Color(0.10f, 0.82f, 1f), 0.06f, 0.13f, 2.32f, 0.62f);
                AddDrift(Bubbles(Crystal, Foam, current * 0.10f, 12));
                AddSheen(Foam, 0.10f, 1.85f, 4.0f, 7.8f);
                AddTwinkles(AmbienceVfxAssets.Sparkle, Coral, 3, 0.09f, 3.0f, 6.0f, 0.50f);
                break;
        }
    }

    void BuildWaterSurface()
    {
        float[] heights = { -0.24f, 0.02f, 0.27f };
        float[] widths = { 1.18f, 1.42f, 0.96f };

        for (int i = 0; i < waveBands.Length; i++)
        {
            float x = (i - 1) * LocalRadius * 0.10f;
            waveCenters[i] = new Vector2(x, heights[i] * LocalRadius);
            waveWidths[i] = widths[i] * LocalRadius;

            SpriteRenderer wave = CreateSprite(
                "OceanWaveBand_" + (i + 1),
                AmbienceVfxAssets.SoftDot,
                GlowSortingOffset,
                waveCenters[i],
                LocalRadius);

            wave.color = new Color(Foam.r, Crystal.g, Crystal.b, waveAlphas[i]);
            wave.transform.localScale = new Vector3(
                waveWidths[i],
                LocalRadius * (0.070f + i * 0.012f),
                1f);
            waveBands[i] = wave;
        }
    }

    protected override void OnAnimate(float time, bool visible)
    {
        if (!visible) return;

        for (int i = 0; i < waveBands.Length; i++)
        {
            SpriteRenderer wave = waveBands[i];
            if (wave == null) continue;

            float phase = Phase + i * 2.15f;
            float flow = time * (0.46f + i * 0.075f) + phase;
            float ripple = Mathf.Sin(flow);
            float counterRipple = Mathf.Sin(flow * 0.61f + 1.4f);

            wave.transform.localPosition = new Vector3(
                waveCenters[i].x + counterRipple * LocalRadius * 0.075f,
                waveCenters[i].y + ripple * LocalRadius * 0.025f,
                0f);
            wave.transform.localScale = new Vector3(
                waveWidths[i] * (1f + counterRipple * 0.035f),
                LocalRadius * (0.070f + i * 0.012f) * (1f + ripple * 0.08f),
                1f);
            wave.transform.localRotation = Quaternion.Euler(0f, 0f, ripple * 1.8f);

            Color color = wave.color;
            color.a = waveAlphas[i] * (0.78f + (ripple + 1f) * 0.11f);
            wave.color = color;
        }
    }

    static DriftSettings Bubbles(Color a, Color b, float wind, int maxParticles)
    {
        return Floaters(AmbienceVfxAssets.SoftDot, a, b, DriftMode.Float,
            wind, 0.025f, 0.065f, maxParticles, 0.82f);
    }

    static DriftSettings Floaters(Sprite sprite, Color a, Color b, DriftMode mode,
        float wind, float minSize, float maxSize, int maxParticles, float alphaScale = 0.52f)
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
            speedMin = mode == DriftMode.Gust ? 0.16f : 0.035f,
            speedMax = mode == DriftMode.Gust ? 0.30f : 0.105f,
            wind = new Vector2(wind, mode == DriftMode.Float ? 0.045f : 0f),
            lifetime = mode == DriftMode.Gust ? 1.5f : 3.6f,
            intervalMin = 0.48f,
            intervalMax = 1.12f,
            fieldRadius = 0.86f,
            spread = 1.08f,
            gustCountMin = 3,
            gustCountMax = 6,
            gustIntervalMin = 5.0f,
            gustIntervalMax = 8.5f,
            maxParticles = maxParticles
        };
    }
}
