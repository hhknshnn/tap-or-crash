using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Lava temasi: volkanik renk tonlaması, parlayan çatlaklar, yavaş akan lav
// dereleri, periyodik patlamalar, kor parçacıkları ve sıcaklık titreşimi.
// Tamamen dekoratif; ortak sözleşme için bkz. PlanetAmbience.
public sealed class LavaPlanetAmbience : PlanetAmbience
{
    // ── Palet ───────────────────────────────────────────────────────────────
    // Sprite rengiyle çarpılır: yeşil/mavi bastırılır, koyu bazalt + kırmızı ton kalır.
    static readonly Color BasaltTint  = new Color(0.94f, 0.55f, 0.44f, 1f);
    static readonly Color MagmaBright = new Color(1f, 0.88f, 0.46f, 1f);
    static readonly Color MagmaMid    = new Color(1f, 0.45f, 0.10f, 1f);
    static readonly Color MagmaDeep   = new Color(0.74f, 0.13f, 0.03f, 1f);
    static readonly Color AshTone     = new Color(0.26f, 0.21f, 0.20f, 1f);
    static readonly Color AuraTint    = new Color(1f, 0.38f, 0.12f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void RegisterTheme()
    {
        PlanetAmbience.Register(
            PlanetAmbienceTheme.ByNamePrefix<LavaPlanetAmbience>("Lava", "Lava", AuraTint));
    }

    // ── Sprite başına kaldera (boyalı krater) konumu ─────────────────────────
    // x, y ve yarıçap sprite genişliğinin oranı olarak. Lava_07 ve Lava_08'de
    // boyalı krater yok; onlarda bacalar prosedürel yerleştirilir.
    static readonly Dictionary<string, Vector3> Calderas = new Dictionary<string, Vector3>
    {
        { "Lava_01", new Vector3( 0.062f, -0.022f, 0.132f) },
        { "Lava_02", new Vector3(-0.111f,  0.012f, 0.076f) },
        { "Lava_03", new Vector3( 0.001f,  0.150f, 0.103f) },
        { "Lava_04", new Vector3( 0.146f,  0.036f, 0.066f) },
        { "Lava_05", new Vector3( 0.003f, -0.027f, 0.083f) },
        { "Lava_06", new Vector3( 0.208f, -0.008f, 0.151f) },
        { "Lava_09", new Vector3( 0.003f,  0.207f, 0.086f) },
        { "Lava_10", new Vector3( 0.250f,  0.046f, 0.062f) },
    };

    sealed class Vent
    {
        public Vector2 position;      // yerel koordinat
        public Vector2 direction;     // püskürme yönü (yerel, normalize)
        public float radius;          // baca ağzı yarıçapı (yerel)
        public float nextEruption;
        public float nextEmberPuff;
        public SpriteRenderer mouthGlow;
        public Transform haze;
        public SpriteRenderer hazeRenderer;
        public float glowBoost;
        public float hazeBoost;
        public float phase;
    }

    sealed class LavaStream
    {
        public Vector2 start;
        public Vector2 direction;
        public float speed;
        public float lifetime;
        public float size;
        public float nextDrop;
        public float interval;
    }

    SpriteRenderer heatHalo;
    SpriteRenderer crackGlow;
    SpriteRenderer calderaGlow;
    ParticleSystem eruptionParticles;
    ParticleSystem flowParticles;

    readonly List<Vent> vents = new List<Vent>();
    readonly List<LavaStream> streams = new List<LavaStream>();

    float calderaBaseSize;

    protected override void Build()
    {
        MultiplyTint(BasaltTint);

        Vector3 caldera = ResolveCaldera();
        BuildGlowLayers(caldera);
        BuildVents(caldera);
        BuildStreams();

        eruptionParticles = CreateDecorativeParticles("LavaEruptionParticles", 48, 2.6f);
        flowParticles = CreateDecorativeParticles("LavaFlowParticles", 24, 3f);
    }

    // ── Kurulum ─────────────────────────────────────────────────────────────

    // Sprite adına göre boyalı krater; tanımlı değilse merkez dışı rastgele bir nokta.
    Vector3 ResolveCaldera()
    {
        string spriteName = SpriteName;
        if (spriteName != null && Calderas.TryGetValue(spriteName, out Vector3 normalized))
        {
            float span = LocalRadius * 2f;
            return new Vector3(normalized.x * span, normalized.y * span, normalized.z * span);
        }

        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = LocalRadius * Random.Range(0.18f, 0.44f);
        return new Vector3(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance, LocalRadius * 0.10f);
    }

    void BuildGlowLayers(Vector3 caldera)
    {
        // Gezegenin arkasında duran sıcak atmosfer parıltısı.
        heatHalo = CreateSprite("LavaHeatHalo", VfxSpriteFactory.SoftSprite, -1,
            Vector2.zero, LocalRadius * 2.7f);
        heatHalo.color = new Color(MagmaMid.r, MagmaMid.g, MagmaMid.b, 0.16f);

        // Yüzeydeki parlayan çatlak ağı; kaldera merkezli, disk içinde kalacak boyutta.
        Vector2 crackCenter = new Vector2(caldera.x, caldera.y) * 0.55f;
        float edgeRoom = Mathf.Max(0.35f * LocalRadius, LocalRadius - crackCenter.magnitude);
        float crackSpan = Mathf.Min(LocalRadius * 1.85f, edgeRoom * 1.9f);
        crackGlow = CreateSprite("LavaCrackGlow", LavaVfxAssets.CrackSprite, GlowSortingOffset,
            crackCenter, crackSpan);
        crackGlow.color = new Color(MagmaMid.r, MagmaMid.g, MagmaMid.b, 0.55f);
        crackGlow.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        // Boyalı kraterin içini akkor hâline getiren çekirdek.
        calderaBaseSize = Mathf.Max(caldera.z, LocalRadius * 0.08f) * 3.1f;
        calderaGlow = CreateSprite("LavaCalderaGlow", VfxSpriteFactory.SoftSprite, GlowSortingOffset,
            new Vector2(caldera.x, caldera.y), calderaBaseSize);
        calderaGlow.color = new Color(MagmaBright.r, MagmaBright.g, MagmaBright.b, 0.42f);
    }

    void BuildVents(Vector3 caldera)
    {
        Vector2 calderaPos = new Vector2(caldera.x, caldera.y);
        Vector2 primaryDirection = calderaPos.sqrMagnitude > (LocalRadius * 0.12f) * (LocalRadius * 0.12f)
            ? calderaPos.normalized
            : Vector2.up;
        vents.Add(CreateVent(calderaPos, primaryDirection, Mathf.Max(caldera.z, LocalRadius * 0.09f)));

        // İkinci baca: kaldera ile aynı hizaya düşmesin, kenara yakın dursun.
        float primaryAngle = Mathf.Atan2(primaryDirection.y, primaryDirection.x) * Mathf.Rad2Deg;
        float secondaryAngle = (primaryAngle + Random.Range(95f, 205f)) * Mathf.Deg2Rad;
        Vector2 secondaryDirection = new Vector2(Mathf.Cos(secondaryAngle), Mathf.Sin(secondaryAngle));
        Vector2 secondaryPos = secondaryDirection * (LocalRadius * Random.Range(0.52f, 0.72f));
        vents.Add(CreateVent(secondaryPos, secondaryDirection, LocalRadius * Random.Range(0.05f, 0.08f)));
    }

    Vent CreateVent(Vector2 position, Vector2 direction, float radius)
    {
        Vent vent = new Vent
        {
            position = position,
            direction = direction.normalized,
            radius = radius,
            phase = Random.Range(0f, Mathf.PI * 2f),
            nextEruption = Time.time + Random.Range(1.2f, 5.5f),
            nextEmberPuff = Time.time + Random.Range(0.5f, 2.2f)
        };

        vent.mouthGlow = CreateSprite("LavaVentGlow", VfxSpriteFactory.SoftSprite, GlowSortingOffset,
            position, radius * 2.8f);
        vent.mouthGlow.color = new Color(MagmaBright.r, MagmaBright.g, MagmaBright.b, 0.28f);

        // Şeffaf, hafifçe salınan bir sıcaklık perdesi (shader'sız, ucuz "heat haze").
        vent.haze = CreateSprite("LavaHeatHaze", VfxSpriteFactory.SoftSprite, ParticleSortingOffset,
            position + vent.direction * (radius * 1.6f), radius * 3.4f).transform;
        vent.hazeRenderer = vent.haze.GetComponent<SpriteRenderer>();
        vent.hazeRenderer.color = new Color(1f, 0.62f, 0.30f, 0.05f);

        return vent;
    }

    void BuildStreams()
    {
        int count = Random.value < 0.55f ? 2 : 1;
        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector2 start = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (LocalRadius * Random.Range(0.10f, 0.45f));

            // Ağırlıklı olarak aşağı, biraz da yüzeyden dışa doğru süzülür.
            Vector2 outward = start.sqrMagnitude > 0.0001f ? start.normalized : Vector2.right;
            Vector2 direction = Vector2.Lerp(Vector2.down, outward, Random.Range(0.15f, 0.4f)).normalized;

            float travel = Mathf.Min(LocalRadius * Random.Range(0.75f, 1.15f),
                DistanceToRim(start, direction) * 0.92f);
            if (travel <= LocalRadius * 0.2f) continue;

            float lifetime = Random.Range(1.9f, 2.8f);
            streams.Add(new LavaStream
            {
                start = start,
                direction = direction,
                lifetime = lifetime,
                speed = travel / lifetime,
                size = LocalRadius * Random.Range(0.10f, 0.15f),
                interval = Random.Range(0.26f, 0.42f),
                nextDrop = Time.time + Random.Range(0f, 0.4f)
            });
        }
    }

    // ── Canlandırma ─────────────────────────────────────────────────────────

    protected override void Animate(float time, bool visible)
    {
        float wave = (Mathf.Sin(time * 1.35f + Phase) + 1f) * 0.5f;

        if (heatHalo != null)
        {
            Color color = heatHalo.color;
            color.a = Mathf.Lerp(0.11f, 0.21f, wave);
            heatHalo.color = color;
        }

        if (crackGlow != null)
        {
            float flicker = Mathf.PerlinNoise(time * 1.9f, Phase) * 0.35f;
            Color color = crackGlow.color;
            color.a = Mathf.Lerp(0.38f, 0.72f, wave * 0.65f + flicker);
            crackGlow.color = color;
        }

        if (calderaGlow != null)
        {
            float breathe = (Mathf.Sin(time * 2.1f + Phase) + 1f) * 0.5f;
            Color color = calderaGlow.color;
            color.a = Mathf.Lerp(0.32f, 0.58f, breathe);
            calderaGlow.color = color;
            calderaGlow.transform.localScale = Vector3.one * (calderaBaseSize * Mathf.Lerp(0.94f, 1.06f, breathe));
        }

        UpdateVents(time, visible);
        UpdateStreams(time, visible);
    }

    void UpdateVents(float time, bool visible)
    {
        for (int i = 0; i < vents.Count; i++)
        {
            Vent vent = vents[i];

            vent.glowBoost = Mathf.Max(0f, vent.glowBoost - Time.deltaTime * 1.4f);
            vent.hazeBoost = Mathf.Max(0f, vent.hazeBoost - Time.deltaTime * 0.8f);

            if (vent.mouthGlow != null)
            {
                float glow = Mathf.Lerp(0.22f, 0.40f, (Mathf.Sin(time * 2.6f + vent.phase) + 1f) * 0.5f);
                Color color = vent.mouthGlow.color;
                color.a = Mathf.Clamp01(glow + vent.glowBoost * 0.55f);
                vent.mouthGlow.color = color;
            }

            if (vent.haze != null)
            {
                float wobble = Mathf.Sin(time * 3.1f + vent.phase);
                float baseSize = vent.radius * 3.4f;
                vent.haze.localScale = new Vector3(
                    baseSize * (1f + wobble * 0.08f),
                    baseSize * (1.25f - wobble * 0.10f + vent.hazeBoost * 0.35f),
                    1f);
                Color color = vent.hazeRenderer.color;
                color.a = 0.035f + Mathf.Abs(wobble) * 0.02f + vent.hazeBoost * 0.07f;
                vent.hazeRenderer.color = color;
            }

            if (!visible) continue;

            if (time >= vent.nextEmberPuff)
            {
                vent.nextEmberPuff = time + Random.Range(1.1f, 2.6f);
                EmitEmbers(vent, Random.Range(2, 4), 0.7f);
            }

            if (time >= vent.nextEruption)
            {
                vent.nextEruption = time + Random.Range(5f, 9.5f);
                StartCoroutine(Erupt(vent));
            }
        }
    }

    void UpdateStreams(float time, bool visible)
    {
        if (!visible || flowParticles == null) return;

        for (int i = 0; i < streams.Count; i++)
        {
            LavaStream stream = streams[i];
            if (time < stream.nextDrop) continue;
            stream.nextDrop = time + stream.interval;

            Emit(flowParticles,
                stream.start + RandomOffset(LocalRadius * 0.04f),
                stream.direction * stream.speed * Random.Range(0.85f, 1.15f),
                stream.lifetime,
                stream.size * Random.Range(0.8f, 1.25f),
                Color.Lerp(MagmaBright, MagmaMid, Random.value));
        }
    }

    // ── Patlama ─────────────────────────────────────────────────────────────

    IEnumerator Erupt(Vent vent)
    {
        vent.glowBoost = 1f;
        vent.hazeBoost = 1f;

        EmitFire(vent, 7);
        EmitEmbers(vent, 4, 1f);

        yield return new WaitForSeconds(0.1f);
        EmitFire(vent, 5);
        EmitAsh(vent, 3);

        yield return new WaitForSeconds(0.14f);
        EmitEmbers(vent, 3, 1f);
        EmitAsh(vent, 3);

        yield return new WaitForSeconds(0.18f);
        EmitAsh(vent, 2);
    }

    void EmitFire(Vent vent, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Spread(vent.direction, 26f);
            Emit(eruptionParticles,
                vent.position + direction * (vent.radius * 0.4f) + RandomOffset(vent.radius * 0.35f),
                direction * (LocalRadius * Random.Range(0.85f, 1.55f)),
                Random.Range(0.45f, 0.8f),
                LocalRadius * Random.Range(0.14f, 0.26f),
                Color.Lerp(MagmaBright, MagmaDeep, Random.value * 0.75f));
        }
    }

    void EmitEmbers(Vent vent, int count, float energy)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Spread(vent.direction, 44f);
            Emit(eruptionParticles,
                vent.position + RandomOffset(vent.radius * 0.5f),
                direction * (LocalRadius * Random.Range(1.1f, 2.1f) * energy),
                Random.Range(0.7f, 1.4f),
                LocalRadius * Random.Range(0.045f, 0.085f),
                Color.Lerp(MagmaBright, MagmaMid, Random.value));
        }
    }

    void EmitAsh(Vent vent, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Spread(vent.direction, 34f);
            Emit(eruptionParticles,
                vent.position + direction * (vent.radius * 0.8f) + RandomOffset(vent.radius * 0.6f),
                direction * (LocalRadius * Random.Range(0.35f, 0.62f)),
                Random.Range(1.5f, 2.4f),
                LocalRadius * Random.Range(0.24f, 0.42f),
                new Color(AshTone.r, AshTone.g, AshTone.b, Random.Range(0.35f, 0.6f)));
        }
    }
}

// Lav efektleri için çalışma zamanında üretilen, tek seferlik dokular.
public static class LavaVfxAssets
{
    private static Texture2D crackTexture;
    private static Sprite crackSprite;

    public static Sprite CrackSprite
    {
        get
        {
            EnsureCrackTexture();
            if (crackSprite == null)
            {
                crackSprite = Sprite.Create(
                    crackTexture,
                    new Rect(0f, 0f, crackTexture.width, crackTexture.height),
                    new Vector2(0.5f, 0.5f),
                    crackTexture.width,
                    0,
                    SpriteMeshType.FullRect);
                crackSprite.name = "Runtime Lava Cracks";
            }
            return crackSprite;
        }
    }

    // Merkezden dışa doğru dallanan, kenara doğru sönen akkor çatlak ağı.
    static void EnsureCrackTexture()
    {
        if (crackTexture != null) return;

        const int size = 128;
        float[] field = new float[size * size];
        uint seed = 0x9E3779B9u;   // global Random durumunu bozmamak için yerel üreteç

        const int branchCount = 7;
        for (int branch = 0; branch < branchCount; branch++)
        {
            float angle = (branch / (float)branchCount) * Mathf.PI * 2f + NextFloat(ref seed) * 0.5f;
            TraceCrack(field, size, size * 0.5f, size * 0.5f, angle, size * 0.44f, 1f, ref seed, 0);
        }

        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = y * size + x;
                float distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / (size * 0.5f);
                float mask = Mathf.Clamp01(1f - distance);
                float intensity = Mathf.Clamp01(field[index]) * mask * mask;
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(intensity) * 255f);
                // Çekirdek beyaza yakın, kenarlar turuncuya düşer.
                byte green = (byte)Mathf.RoundToInt(Mathf.Lerp(120f, 235f, Mathf.Clamp01(intensity)));
                byte blue = (byte)Mathf.RoundToInt(Mathf.Lerp(30f, 160f, Mathf.Clamp01(intensity * intensity)));
                pixels[index] = new Color32(255, green, blue, alpha);
            }
        }

        crackTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Runtime Lava Crack Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        crackTexture.SetPixels32(pixels);
        crackTexture.Apply(false, true);
    }

    static void TraceCrack(float[] field, int size, float x, float y, float angle, float length,
        float strength, ref uint seed, int depth)
    {
        int steps = Mathf.Max(4, Mathf.RoundToInt(length));
        for (int i = 0; i < steps; i++)
        {
            angle += (NextFloat(ref seed) - 0.5f) * 0.34f;
            x += Mathf.Cos(angle);
            y += Mathf.Sin(angle);
            if (x < 1f || y < 1f || x > size - 2f || y > size - 2f) return;

            float fade = strength * (1f - i / (float)steps);
            Stamp(field, size, x, y, fade);

            if (depth < 2 && i > steps * 0.25f && NextFloat(ref seed) < 0.035f)
            {
                float branchAngle = angle + (NextFloat(ref seed) < 0.5f ? -0.85f : 0.85f);
                TraceCrack(field, size, x, y, branchAngle, length * 0.45f, strength * 0.7f, ref seed, depth + 1);
            }
        }
    }

    static void Stamp(float[] field, int size, float x, float y, float strength)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(x) - 2);
        int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(x) + 2);
        int minY = Mathf.Max(0, Mathf.FloorToInt(y) - 2);
        int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(y) + 2);

        for (int py = minY; py <= maxY; py++)
        {
            for (int px = minX; px <= maxX; px++)
            {
                float dx = px - x;
                float dy = py - y;
                float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 2.4f);
                if (falloff <= 0f) continue;
                int index = py * size + px;
                field[index] = Mathf.Max(field[index], falloff * falloff * strength);
            }
        }
    }

    static float NextFloat(ref uint seed)
    {
        seed ^= seed << 13;
        seed ^= seed >> 17;
        seed ^= seed << 5;
        return (seed & 0xFFFFFF) / (float)0xFFFFFF;
    }
}
