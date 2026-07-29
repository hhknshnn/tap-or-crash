using UnityEngine;

// Ice teması: buzul paletinin içinde kalan ama gezegen başına farklı kimlik
// (kar beyazı, mavi buz, camgöbeği kristal, turkuaz, mor donmuş gölge...) ve
// farklı efekt bileşimi. Tamamen dekoratif; bkz. PlanetAmbience sözleşmesi.
public sealed class IcePlanetAmbience : PlanetAmbienceKit
{
    static readonly Color AuraTint = new Color(0.55f, 0.86f, 1f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<IcePlanetAmbience>("Ice", "Ice", AuraTint));
    }

    [System.Flags]
    enum Effect
    {
        None     = 0,
        Snowfall = 1 << 0,
        Sparkle  = 1 << 1,
        Aura     = 1 << 2,
        Dust     = 1 << 3,
        Gust     = 1 << 4,
        Shimmer  = 1 << 5,
        Frost    = 1 << 6
    }

    struct Mood
    {
        public Color tint;      // gezegen sprite'ına çarpılır (yalnızca renk)
        public Color ice;       // kar / buz tozu tonu
        public Color crystal;   // parıltı ve kristal tonu
        public Effect effects;
    }

    // Dizin = sprite numarası % 10 (Ice_01 → 1 ... Ice_10 → 0).
    // Her satır el yapımıdır: hiçbir palet ve hiçbir efekt bileşimi tekrar etmez.
    static readonly Mood[] Moods =
    {
        // Ice_10 — mor donmuş gölge
        new Mood
        {
            tint = new Color(0.86f, 0.83f, 1f), ice = new Color(0.90f, 0.88f, 1f),
            crystal = new Color(0.78f, 0.72f, 1f),
            effects = Effect.Aura | Effect.Shimmer | Effect.Snowfall
        },
        // Ice_01 — parlak kar beyazı
        new Mood
        {
            tint = new Color(0.98f, 1f, 1f), ice = new Color(1f, 1f, 1f),
            crystal = new Color(0.92f, 0.98f, 1f),
            effects = Effect.Snowfall | Effect.Sparkle
        },
        // Ice_02 — derin mavi buz
        new Mood
        {
            tint = new Color(0.78f, 0.88f, 1f), ice = new Color(0.80f, 0.90f, 1f),
            crystal = new Color(0.55f, 0.80f, 1f),
            effects = Effect.Aura | Effect.Dust
        },
        // Ice_03 — camgöbeği kristal
        new Mood
        {
            tint = new Color(0.78f, 1f, 1f), ice = new Color(0.86f, 1f, 1f),
            crystal = new Color(0.50f, 1f, 1f),
            effects = Effect.Sparkle | Effect.Shimmer
        },
        // Ice_04 — kar fırtınası
        new Mood
        {
            tint = new Color(0.90f, 0.95f, 1f), ice = new Color(0.96f, 0.99f, 1f),
            crystal = new Color(0.72f, 0.90f, 1f),
            effects = Effect.Snowfall | Effect.Gust | Effect.Aura
        },
        // Ice_05 — turkuaz buzul
        new Mood
        {
            tint = new Color(0.74f, 1f, 0.95f), ice = new Color(0.78f, 1f, 0.96f),
            crystal = new Color(0.45f, 0.95f, 0.90f),
            effects = Effect.Dust | Effect.Frost
        },
        // Ice_06 — soluk mavi sis
        new Mood
        {
            tint = new Color(0.90f, 0.96f, 1f), ice = new Color(0.93f, 0.97f, 1f),
            crystal = new Color(0.70f, 0.86f, 1f),
            effects = Effect.Sparkle | Effect.Aura | Effect.Snowfall
        },
        // Ice_07 — çelik mavisi
        new Mood
        {
            tint = new Color(0.80f, 0.86f, 0.98f), ice = new Color(0.84f, 0.90f, 1f),
            crystal = new Color(0.60f, 0.74f, 0.98f),
            effects = Effect.Gust | Effect.Shimmer
        },
        // Ice_08 — nane buzu
        new Mood
        {
            tint = new Color(0.84f, 1f, 0.94f), ice = new Color(0.88f, 1f, 0.96f),
            crystal = new Color(0.62f, 1f, 0.88f),
            effects = Effect.Snowfall | Effect.Dust
        },
        // Ice_09 — donmuş kristal ovası
        new Mood
        {
            tint = new Color(0.72f, 0.86f, 1f), ice = new Color(0.86f, 0.94f, 1f),
            crystal = new Color(0.58f, 0.88f, 1f),
            effects = Effect.Sparkle | Effect.Frost | Effect.Gust
        }
    };

    protected override void Build()
    {
        Mood mood = Moods[Mathf.Abs(PlanetIndex) % Moods.Length];
        MultiplyTint(mood.tint);

        float wind = Variant.Range(0.08f, 0.22f) * Variant.Sign;

        if (Has(mood, Effect.Snowfall)) BuildSnowfall(mood, wind);
        if (Has(mood, Effect.Dust)) BuildIceDust(mood, wind);
        if (Has(mood, Effect.Gust)) BuildGust(mood, wind);
        if (Has(mood, Effect.Sparkle)) BuildSparkles(mood);
        if (Has(mood, Effect.Aura)) BuildAura(mood);
        if (Has(mood, Effect.Shimmer)) BuildShimmer(mood);
        if (Has(mood, Effect.Frost)) BuildFrost();
    }

    static bool Has(Mood mood, Effect effect) => (mood.effects & effect) != 0;

    // ── Efektler ────────────────────────────────────────────────────────────

    void BuildSnowfall(Mood mood, float wind)
    {
        // Yarısı kar tanesi, yarısı yumuşak nokta olsun diye tane boyutu geniş tutulur.
        AddDrift(new DriftSettings
        {
            sprite = AmbienceVfxAssets.Snowflake,
            colorA = WithAlpha(mood.ice, 0.9f),
            colorB = WithAlpha(mood.crystal, 0.6f),
            mode = DriftMode.Fall,
            sizeMin = 0.06f, sizeMax = 0.12f,
            speedMin = 0.22f, speedMax = 0.45f,
            wind = new Vector2(wind, 0f),
            lifetime = 3.2f,
            intervalMin = 0.35f, intervalMax = 0.85f,
            maxParticles = 16
        });
    }

    void BuildIceDust(Mood mood, float wind)
    {
        AddDrift(new DriftSettings
        {
            sprite = AmbienceVfxAssets.SoftDot,
            colorA = WithAlpha(mood.crystal, 0.34f),
            colorB = WithAlpha(mood.ice, 0.18f),
            mode = DriftMode.Float,
            sizeMin = 0.03f, sizeMax = 0.07f,
            speedMin = 0.05f, speedMax = 0.18f,
            wind = new Vector2(wind * 0.4f, 0.05f),
            lifetime = 3.6f,
            intervalMin = 0.4f, intervalMax = 0.95f,
            fieldRadius = 0.95f,
            maxParticles = 14
        });
    }

    void BuildGust(Mood mood, float wind)
    {
        AddDrift(new DriftSettings
        {
            sprite = AmbienceVfxAssets.Shard,
            colorA = WithAlpha(mood.ice, 0.62f),
            colorB = WithAlpha(mood.crystal, 0.34f),
            mode = DriftMode.Gust,
            sizeMin = 0.035f, sizeMax = 0.075f,
            wind = new Vector2(wind, 0.02f),
            lifetime = 1.5f,
            spread = 1.3f,
            maxParticles = 20,
            gustCountMin = 5, gustCountMax = 9,
            gustIntervalMin = 5f, gustIntervalMax = 10f
        });
    }

    void BuildSparkles(Mood mood)
    {
        AddTwinkles(AmbienceVfxAssets.Sparkle, Color.Lerp(mood.crystal, Color.white, 0.4f),
            Variant.Range(4, 7), 0.16f, 2.2f, 4.6f, 0.85f);
    }

    void BuildAura(Mood mood)
    {
        AddHalo(mood.crystal, 0.10f, 0.20f, 2.5f, 1.2f);
    }

    void BuildShimmer(Mood mood)
    {
        AddSheen(Color.Lerp(mood.crystal, Color.white, 0.7f), 0.14f, 1.4f, 2.8f, 6f);
    }

    void BuildFrost()
    {
        // Donmuş yüzeyin çok hafif soğuyup ısınması; yalnızca renk nefes alır.
        AddBreath(new Color(0.88f, 0.94f, 1f), 0.9f, 0.6f);
    }

    static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
}
