using UnityEngine;

// Natural teması: yeşil paletin içinde kalan ama gezegen başına farklı bir ruh hâli
// (bahar yeşili, koyu orman, yosun, limon, zümrüt...) ve farklı efekt bileşimi.
// Hiçbir gezegen diğerinin aynısı değildir; tümü tamamen dekoratiftir.
public sealed class NaturalPlanetAmbience : PlanetAmbienceKit
{
    static readonly Color AuraTint = new Color(0.40f, 0.92f, 0.45f, 1f);
    static readonly Color BirdTone = new Color(0.16f, 0.21f, 0.18f, 0.92f);
    static readonly Color InsectTone = new Color(0.22f, 0.26f, 0.16f, 0.85f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        Register(PlanetAmbienceTheme.ByNamePrefix<NaturalPlanetAmbience>("Natural", "Natural", AuraTint));
    }

    [System.Flags]
    enum Effect
    {
        None        = 0,
        Butterflies = 1 << 0,
        Leaves      = 1 << 1,
        Birds       = 1 << 2,
        Pollen      = 1 << 3,
        Petals      = 1 << 4,
        Insects     = 1 << 5,
        Wind        = 1 << 6,
        Grass       = 1 << 7
    }

    struct Mood
    {
        public Color tint;      // gezegen sprite'ına çarpılır (yalnızca renk)
        public Color foliage;   // yaprak / çim tonu
        public Color bloom;     // çiçek, polen, kelebek tonu
        public Effect effects;
    }

    // Dizin = sprite numarası % 10 (Natural_01 → 1 ... Natural_10 → 0).
    // Her satır el yapımıdır: hiçbir palet ve hiçbir efekt bileşimi tekrar etmez.
    static readonly Mood[] Moods =
    {
        // Natural_10 — derin zümrüt
        new Mood
        {
            tint = new Color(0.72f, 0.94f, 0.78f), foliage = new Color(0.35f, 0.72f, 0.45f),
            bloom = new Color(0.85f, 1f, 0.85f),
            effects = Effect.Grass | Effect.Petals | Effect.Insects
        },
        // Natural_01 — taze bahar yeşili
        new Mood
        {
            tint = new Color(0.90f, 1f, 0.88f), foliage = new Color(0.55f, 0.85f, 0.45f),
            bloom = new Color(1f, 0.85f, 0.92f),
            effects = Effect.Butterflies | Effect.Pollen
        },
        // Natural_02 — parlak çayır
        new Mood
        {
            tint = new Color(0.98f, 1f, 0.78f), foliage = new Color(0.62f, 0.88f, 0.35f),
            bloom = new Color(1f, 0.93f, 0.55f),
            effects = Effect.Leaves | Effect.Wind
        },
        // Natural_03 — koyu orman
        new Mood
        {
            tint = new Color(0.70f, 0.84f, 0.70f), foliage = new Color(0.30f, 0.55f, 0.32f),
            bloom = new Color(0.85f, 0.90f, 0.75f),
            effects = Effect.Birds | Effect.Grass
        },
        // Natural_04 — sarı-yeşil bozkır
        new Mood
        {
            tint = new Color(1f, 0.97f, 0.62f), foliage = new Color(0.75f, 0.85f, 0.30f),
            bloom = new Color(1f, 0.88f, 0.40f),
            effects = Effect.Petals | Effect.Butterflies
        },
        // Natural_05 — yosun
        new Mood
        {
            tint = new Color(0.82f, 0.90f, 0.72f), foliage = new Color(0.48f, 0.62f, 0.32f),
            bloom = new Color(0.90f, 0.85f, 0.60f),
            effects = Effect.Pollen | Effect.Insects | Effect.Grass
        },
        // Natural_06 — yeşim / turkuaza çalan
        new Mood
        {
            tint = new Color(0.78f, 1f, 0.90f), foliage = new Color(0.40f, 0.80f, 0.65f),
            bloom = new Color(0.75f, 1f, 0.90f),
            effects = Effect.Leaves | Effect.Birds
        },
        // Natural_07 — sıcak yaz yeşili
        new Mood
        {
            tint = new Color(1f, 0.94f, 0.74f), foliage = new Color(0.68f, 0.80f, 0.35f),
            bloom = new Color(1f, 0.80f, 0.55f),
            effects = Effect.Wind | Effect.Petals
        },
        // Natural_08 — zeytin
        new Mood
        {
            tint = new Color(0.90f, 0.88f, 0.62f), foliage = new Color(0.60f, 0.62f, 0.28f),
            bloom = new Color(0.95f, 0.90f, 0.55f),
            effects = Effect.Insects | Effect.Pollen
        },
        // Natural_09 — limon yeşili
        new Mood
        {
            tint = new Color(0.90f, 1f, 0.70f), foliage = new Color(0.66f, 0.92f, 0.38f),
            bloom = new Color(0.92f, 1f, 0.62f),
            effects = Effect.Butterflies | Effect.Leaves | Effect.Birds
        }
    };

    protected override void Build()
    {
        Mood mood = Moods[Mathf.Abs(PlanetIndex) % Moods.Length];
        MultiplyTint(mood.tint);

        // Gezegen başına sabit rüzgâr yönü: yaprak, polen ve perde aynı yöne akar.
        float wind = Variant.Range(0.07f, 0.20f) * Variant.Sign;

        if (Has(mood, Effect.Leaves)) BuildLeaves(mood, wind);
        if (Has(mood, Effect.Petals)) BuildPetals(mood, wind);
        if (Has(mood, Effect.Pollen)) BuildPollen(mood, wind);
        if (Has(mood, Effect.Butterflies)) BuildButterflies(mood);
        if (Has(mood, Effect.Insects)) BuildInsects();
        if (Has(mood, Effect.Birds)) BuildBirds();
        if (Has(mood, Effect.Wind)) BuildWind(mood);
        if (Has(mood, Effect.Grass)) BuildGrass();
    }

    static bool Has(Mood mood, Effect effect) => (mood.effects & effect) != 0;

    // ── Efektler ────────────────────────────────────────────────────────────

    void BuildLeaves(Mood mood, float wind)
    {
        AddDrift(new DriftSettings
        {
            sprite = AmbienceVfxAssets.Leaf,
            colorA = Dim(mood.foliage, 1f, 0.85f),
            colorB = Dim(mood.foliage, 0.72f, 0.75f),
            mode = DriftMode.Fall,
            sizeMin = 0.09f, sizeMax = 0.16f,
            speedMin = 0.24f, speedMax = 0.46f,
            wind = new Vector2(wind, 0f),
            lifetime = 3.2f,
            intervalMin = 0.7f, intervalMax = 1.7f,
            maxParticles = 10
        });
    }

    void BuildPetals(Mood mood, float wind)
    {
        AddDrift(new DriftSettings
        {
            sprite = AmbienceVfxAssets.Petal,
            colorA = Dim(mood.bloom, 1f, 0.8f),
            colorB = Dim(mood.bloom, 0.88f, 0.6f),
            mode = DriftMode.Fall,
            sizeMin = 0.06f, sizeMax = 0.11f,
            speedMin = 0.16f, speedMax = 0.34f,
            wind = new Vector2(wind * 1.3f, 0f),
            lifetime = 3.6f,
            intervalMin = 0.8f, intervalMax = 1.9f,
            maxParticles = 10
        });
    }

    void BuildPollen(Mood mood, float wind)
    {
        AddDrift(new DriftSettings
        {
            sprite = AmbienceVfxAssets.SoftDot,
            colorA = Dim(mood.bloom, 1f, 0.42f),
            colorB = Dim(mood.bloom, 0.9f, 0.22f),
            mode = DriftMode.Float,
            sizeMin = 0.03f, sizeMax = 0.06f,
            speedMin = 0.05f, speedMax = 0.16f,
            wind = new Vector2(wind * 0.35f, 0.04f),
            lifetime = 3.4f,
            intervalMin = 0.4f, intervalMax = 0.9f,
            fieldRadius = 0.9f,
            maxParticles = 14
        });
    }

    void BuildButterflies(Mood mood)
    {
        AddOrbiters(AmbienceVfxAssets.Butterfly, Dim(mood.bloom, 1f, 0.95f),
            Variant.Range(2, 4), 0.22f, 0.5f, 0.95f, true);
    }

    void BuildInsects()
    {
        AddOrbiters(AmbienceVfxAssets.SoftDot, InsectTone, 3, 0.05f, 1.7f, 2.8f, false);
    }

    void BuildBirds()
    {
        AddCrossing(AmbienceVfxAssets.Bird, BirdTone, 0.28f, 6.5f, 13f, 0.34f, true);
    }

    void BuildWind(Mood mood)
    {
        AddSheen(Color.Lerp(mood.foliage, Color.white, 0.65f), 0.10f, 1.9f, 3.5f, 7.5f);
    }

    void BuildGrass()
    {
        // Yüzeyin rüzgârda kıpırdaması: üzerinden geçen çok hafif bir gölge nefesi.
        // Yalnızca sprite rengi değişir; sınırlar ve yarıçap sabit kalır.
        AddBreath(new Color(0.90f, 0.95f, 0.88f), 1.15f, 0.55f);
    }

    // Rengi parlaklık ve alfa olarak ayarlar; 1'i aşan kanallar kırpılır.
    static Color Dim(Color color, float brightness, float alpha)
    {
        return new Color(
            Mathf.Clamp01(color.r * brightness),
            Mathf.Clamp01(color.g * brightness),
            Mathf.Clamp01(color.b * brightness),
            alpha);
    }
}
