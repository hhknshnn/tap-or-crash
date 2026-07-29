using System.Collections.Generic;
using UnityEngine;

// Temaların paylaştığı hafif efekt kiti. Her efekt ya birkaç transform/renk günceller
// ya da elle beslenen çarpışmasız bir parçacık sistemi kullanır; hiçbiri collider,
// trigger ya da oyun tag'i üretmez (bkz. PlanetAmbience sözleşmesi).
//
// Alt sınıf Build() içinde istediği efektleri Add*() ile kurar; tick işini kit yapar.
// Yeni tema (Desert, Toxic, Crystal, Space...) yalnızca palet + efekt seçimi yazar.
public abstract class PlanetAmbienceKit : PlanetAmbience
{
    interface IAmbienceEffect
    {
        void Tick(float time, bool visible);
    }

    readonly List<IAmbienceEffect> effects = new List<IAmbienceEffect>();

    // Nested efekt sınıflarının kullandığı dar erişim yüzeyi.
    float Radius => LocalRadius;
    SpriteRenderer Body => PlanetRenderer;

    void EmitParticle(ParticleSystem particles, Vector2 position, Vector2 velocity,
        float lifetime, float size, Color color)
        => Emit(particles, position, velocity, lifetime, size, color);

    protected sealed override void Animate(float time, bool visible)
    {
        for (int i = 0; i < effects.Count; i++)
            effects[i].Tick(time, visible);

        OnAnimate(time, visible);
    }

    // Temaya özel ek animasyon gerekirse.
    protected virtual void OnAnimate(float time, bool visible) { }

    // ── Efekt kurucuları ────────────────────────────────────────────────────

    // Gezegenin arkasında nefes alan yumuşak hâle (soğuk aura, sıcak parıltı...).
    protected void AddHalo(Color color, float minAlpha, float maxAlpha, float sizeRatio, float speed)
    {
        SpriteRenderer halo = CreateSprite("AmbienceHalo", AmbienceVfxAssets.SoftDot, -1,
            Vector2.zero, LocalRadius * sizeRatio);
        halo.color = new Color(color.r, color.g, color.b, minAlpha);

        effects.Add(new PulseEffect(halo, minAlpha, maxAlpha, speed, Phase, sizeRatio * LocalRadius));
    }

    // Yüzeyde sırayla parlayıp sönen noktalar (kristal parıltısı, çiy, kar kırıntısı).
    protected void AddTwinkles(Sprite sprite, Color color, int count, float sizeRatio,
        float periodMin, float periodMax, float maxAlpha = 0.9f)
    {
        List<TwinkleEffect.Point> points = new List<TwinkleEffect.Point>(count);
        for (int i = 0; i < count; i++)
        {
            Vector2 position = Variant.InsideDisk(LocalRadius * 0.82f);
            float size = LocalRadius * sizeRatio * Variant.Range(0.75f, 1.3f);

            SpriteRenderer renderer = CreateSprite("AmbienceTwinkle", sprite, GlowSortingOffset,
                position, size);
            renderer.color = new Color(color.r, color.g, color.b, 0f);
            renderer.transform.localRotation = Quaternion.Euler(0f, 0f, Variant.Range(0f, 360f));

            points.Add(new TwinkleEffect.Point
            {
                renderer = renderer,
                baseSize = size,
                period = Variant.Range(periodMin, periodMax),
                offset = Variant.Range(0f, 10f)
            });
        }
        effects.Add(new TwinkleEffect(points, maxAlpha));
    }

    // Gezegenin çevresinde dolanan minik canlılar (kelebek, böcek).
    protected void AddOrbiters(Sprite sprite, Color color, int count, float sizeRatio,
        float speedMin, float speedMax, bool flap)
    {
        List<OrbiterEffect.Orbiter> orbiters = new List<OrbiterEffect.Orbiter>(count);
        for (int i = 0; i < count; i++)
        {
            float size = LocalRadius * sizeRatio * Variant.Range(0.8f, 1.25f);
            SpriteRenderer renderer = CreateSprite("AmbienceOrbiter", sprite, ParticleSortingOffset,
                Vector2.zero, size);
            renderer.color = color;

            orbiters.Add(new OrbiterEffect.Orbiter
            {
                body = renderer.transform,
                renderer = renderer,
                baseSize = size,
                radiusX = LocalRadius * Variant.Range(0.42f, 0.86f),
                radiusY = LocalRadius * Variant.Range(0.30f, 0.74f),
                center = Variant.InsideDisk(LocalRadius * 0.18f),
                speed = Variant.Range(speedMin, speedMax) * Variant.Sign,
                wobbleSpeed = Variant.Range(1.6f, 3.1f),
                offset = Variant.Range(0f, 10f)
            });
        }
        effects.Add(new OrbiterEffect(orbiters, flap));
    }

    // Ara sıra gezegenin önünden geçen uçucu (kuş sürüsü, savrulan yaprak).
    protected void AddCrossing(Sprite sprite, Color color, float sizeRatio,
        float intervalMin, float intervalMax, float speed, bool flap)
    {
        SpriteRenderer renderer = CreateSprite("AmbienceCrossing", sprite, ParticleSortingOffset,
            Vector2.zero, LocalRadius * sizeRatio);
        renderer.color = color;
        renderer.enabled = false;

        effects.Add(new CrossingEffect(renderer, LocalRadius, LocalRadius * sizeRatio,
            intervalMin, intervalMax, speed, flap));
    }

    // Disk üzerinden süzülen ince ışık/rüzgâr perdesi.
    protected void AddSheen(Color color, float maxAlpha, float sweepDuration,
        float pauseMin, float pauseMax)
    {
        SpriteRenderer renderer = CreateSprite("AmbienceSheen", AmbienceVfxAssets.SoftDot,
            GlowSortingOffset, Vector2.zero, LocalRadius);
        renderer.color = new Color(color.r, color.g, color.b, 0f);
        renderer.transform.localScale = new Vector3(LocalRadius * 0.55f, LocalRadius * 1.7f, 1f);
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, Variant.Range(-22f, 22f));

        effects.Add(new SheenEffect(renderer, LocalRadius, maxAlpha, sweepDuration, pauseMin, pauseMax));
    }

    // Gezegen renginin çok hafif nefes alması: rüzgârda kıpırdayan çimen, buz parıltısı.
    // Yalnızca SpriteRenderer.color değişir; sınırlar ve yarıçap sabit kalır.
    protected void AddBreath(Color tint, float speed, float amount)
    {
        if (PlanetRenderer == null) return;
        effects.Add(new BreathEffect(Body, tint, speed, amount, Phase));
    }

    // Sürüklenen parçacık alanı: düşen yaprak/kar, süzülen polen, ani rüzgâr.
    protected void AddDrift(DriftSettings settings)
    {
        if (settings == null || settings.sprite == null) return;

        ParticleSystem particles = CreateDecorativeParticles(
            "AmbienceDrift_" + settings.sprite.name,
            settings.maxParticles,
            settings.lifetime * 1.3f,
            AmbienceVfxAssets.ParticleMaterialFor(settings.sprite));

        effects.Add(new DriftEffect(this, particles, settings));
    }

    // ── Sürükleme ayarları ──────────────────────────────────────────────────

    protected enum DriftMode
    {
        Fall,    // üstten aşağı süzülür (yaprak, kar)
        Float,   // disk üzerinde havada asılı gezinir (polen, buz tozu)
        Gust     // aralıklı olarak yandan savrulan öbek (rüzgâr, kar fırtınası)
    }

    // Tüm ölçüler LocalRadius katıdır; gezegen boyutundan bağımsız çalışır.
    protected sealed class DriftSettings
    {
        public Sprite sprite;
        public Color colorA = Color.white;
        public Color colorB = Color.white;
        public DriftMode mode = DriftMode.Fall;

        public float sizeMin = 0.06f;
        public float sizeMax = 0.11f;
        public float speedMin = 0.22f;
        public float speedMax = 0.48f;
        public Vector2 wind = new Vector2(-0.12f, 0f);
        public float lifetime = 3f;

        public float intervalMin = 0.45f;
        public float intervalMax = 1.1f;
        public int maxParticles = 14;

        public float spawnHeight = 1.15f;   // Fall: diskin ne kadar üstünde doğar
        public float spread = 1.0f;         // Fall/Gust: yatay yayılım
        public float fieldRadius = 0.85f;   // Float: hangi yarıçap içinde gezinir

        public int gustCountMin = 4;
        public int gustCountMax = 8;
        public float gustIntervalMin = 4.5f;
        public float gustIntervalMax = 9f;
    }

    // ── Efektler ────────────────────────────────────────────────────────────

    sealed class PulseEffect : IAmbienceEffect
    {
        readonly SpriteRenderer renderer;
        readonly float minAlpha;
        readonly float maxAlpha;
        readonly float speed;
        readonly float phase;
        readonly float baseSize;

        public PulseEffect(SpriteRenderer renderer, float minAlpha, float maxAlpha, float speed,
            float phase, float baseSize)
        {
            this.renderer = renderer;
            this.minAlpha = minAlpha;
            this.maxAlpha = maxAlpha;
            this.speed = speed;
            this.phase = phase;
            this.baseSize = baseSize;
        }

        public void Tick(float time, bool visible)
        {
            if (renderer == null || !visible) return;

            float wave = (Mathf.Sin(time * speed + phase) + 1f) * 0.5f;
            Color color = renderer.color;
            color.a = Mathf.Lerp(minAlpha, maxAlpha, wave);
            renderer.color = color;
            renderer.transform.localScale = Vector3.one * (baseSize * Mathf.Lerp(0.97f, 1.05f, wave));
        }
    }

    sealed class TwinkleEffect : IAmbienceEffect
    {
        public struct Point
        {
            public SpriteRenderer renderer;
            public float baseSize;
            public float period;
            public float offset;
        }

        readonly List<Point> points;
        readonly float maxAlpha;

        public TwinkleEffect(List<Point> points, float maxAlpha)
        {
            this.points = points;
            this.maxAlpha = maxAlpha;
        }

        public void Tick(float time, bool visible)
        {
            if (!visible) return;

            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                if (point.renderer == null) continue;

                // Kısa parlama, uzun bekleme: döngünün yalnızca ilk %35'i görünür.
                float cycle = Mathf.Repeat((time + point.offset) / point.period, 1f);
                float flash = cycle < 0.35f ? Mathf.Sin(cycle / 0.35f * Mathf.PI) : 0f;

                Color color = point.renderer.color;
                color.a = flash * maxAlpha;
                point.renderer.color = color;
                point.renderer.transform.localScale = Vector3.one * (point.baseSize * (0.72f + flash * 0.5f));
            }
        }
    }

    sealed class OrbiterEffect : IAmbienceEffect
    {
        public struct Orbiter
        {
            public Transform body;
            public SpriteRenderer renderer;
            public float baseSize;
            public float radiusX;
            public float radiusY;
            public Vector2 center;
            public float speed;
            public float wobbleSpeed;
            public float offset;
        }

        readonly List<Orbiter> orbiters;
        readonly bool flap;

        public OrbiterEffect(List<Orbiter> orbiters, bool flap)
        {
            this.orbiters = orbiters;
            this.flap = flap;
        }

        public void Tick(float time, bool visible)
        {
            if (!visible) return;

            for (int i = 0; i < orbiters.Count; i++)
            {
                Orbiter orbiter = orbiters[i];
                if (orbiter.body == null) continue;

                float t = time * orbiter.speed + orbiter.offset;
                // Lissajous: dairesel değil, gezinen bir yol çizer.
                float x = orbiter.center.x + Mathf.Sin(t) * orbiter.radiusX;
                float y = orbiter.center.y + Mathf.Sin(t * 1.37f + 0.9f) * orbiter.radiusY;
                orbiter.body.localPosition = new Vector3(x, y, 0f);

                if (!flap) continue;

                // Kanat çırpma: yatay ölçek daralıp açılır, yön değişince siluet döner.
                float wing = Mathf.Abs(Mathf.Sin(time * orbiter.wobbleSpeed + orbiter.offset));
                float facing = Mathf.Cos(t) >= 0f ? 1f : -1f;
                orbiter.body.localScale = new Vector3(
                    orbiter.baseSize * facing * Mathf.Lerp(0.45f, 1f, wing),
                    orbiter.baseSize,
                    1f);
            }
        }
    }

    sealed class CrossingEffect : IAmbienceEffect
    {
        readonly SpriteRenderer renderer;
        readonly Transform body;
        readonly float radius;
        readonly float size;
        readonly float intervalMin;
        readonly float intervalMax;
        readonly float speed;
        readonly bool flap;

        float nextStart;
        float progress = -1f;
        float direction = 1f;
        float height;
        float arc;

        public CrossingEffect(SpriteRenderer renderer, float radius, float size,
            float intervalMin, float intervalMax, float speed, bool flap)
        {
            this.renderer = renderer;
            body = renderer.transform;
            this.radius = radius;
            this.size = size;
            this.intervalMin = intervalMin;
            this.intervalMax = intervalMax;
            this.speed = Mathf.Max(0.05f, speed);
            this.flap = flap;

            nextStart = Time.time + Random.Range(intervalMin * 0.35f, intervalMax);
        }

        public void Tick(float time, bool visible)
        {
            if (renderer == null) return;

            if (progress < 0f)
            {
                // Ekran dışındayken hiç başlatma: görünmeyen gezegen bedava olsun.
                if (!visible || time < nextStart) return;

                progress = 0f;
                direction = Random.value < 0.5f ? -1f : 1f;
                height = Random.Range(-0.35f, 0.95f) * radius;
                arc = Random.Range(0.08f, 0.28f) * radius;
                renderer.enabled = true;
                return;
            }

            progress += Time.deltaTime * speed;
            if (progress >= 1f)
            {
                progress = -1f;
                renderer.enabled = false;
                nextStart = time + Random.Range(intervalMin, intervalMax);
                return;
            }

            float x = Mathf.Lerp(-1.45f, 1.45f, progress) * radius * direction;
            float y = height + Mathf.Sin(progress * Mathf.PI) * arc;
            body.localPosition = new Vector3(x, y, 0f);

            float wing = flap ? Mathf.Lerp(0.55f, 1f, Mathf.Abs(Mathf.Sin(time * 7.5f))) : 1f;
            body.localScale = new Vector3(size * direction, size * wing, 1f);
        }
    }

    sealed class SheenEffect : IAmbienceEffect
    {
        readonly SpriteRenderer renderer;
        readonly Transform body;
        readonly float radius;
        readonly float maxAlpha;
        readonly float sweepDuration;
        readonly float pauseMin;
        readonly float pauseMax;

        float progress = -1f;
        float nextStart;

        public SheenEffect(SpriteRenderer renderer, float radius, float maxAlpha,
            float sweepDuration, float pauseMin, float pauseMax)
        {
            this.renderer = renderer;
            body = renderer.transform;
            this.radius = radius;
            this.maxAlpha = maxAlpha;
            this.sweepDuration = Mathf.Max(0.2f, sweepDuration);
            this.pauseMin = pauseMin;
            this.pauseMax = pauseMax;

            nextStart = Time.time + Random.Range(0f, pauseMax);
        }

        public void Tick(float time, bool visible)
        {
            if (renderer == null) return;

            if (progress < 0f)
            {
                if (!visible || time < nextStart) return;
                progress = 0f;
                return;
            }

            progress += Time.deltaTime / sweepDuration;
            if (progress >= 1f)
            {
                progress = -1f;
                nextStart = time + Random.Range(pauseMin, pauseMax);
                Color faded = renderer.color;
                faded.a = 0f;
                renderer.color = faded;
                return;
            }

            body.localPosition = new Vector3(Mathf.Lerp(-0.85f, 0.85f, progress) * radius, 0f, 0f);

            Color color = renderer.color;
            color.a = Mathf.Sin(progress * Mathf.PI) * maxAlpha;
            renderer.color = color;
        }
    }

    sealed class BreathEffect : IAmbienceEffect
    {
        readonly SpriteRenderer body;
        readonly Color baseColor;
        readonly Color tintedColor;
        readonly float speed;
        readonly float amount;
        readonly float phase;

        public BreathEffect(SpriteRenderer body, Color tint, float speed, float amount, float phase)
        {
            this.body = body;
            baseColor = body.color;
            tintedColor = new Color(baseColor.r * tint.r, baseColor.g * tint.g, baseColor.b * tint.b,
                baseColor.a);
            this.speed = speed;
            this.amount = Mathf.Clamp01(amount);
            this.phase = phase;
        }

        public void Tick(float time, bool visible)
        {
            if (body == null || !visible) return;

            float wave = (Mathf.Sin(time * speed + phase) + 1f) * 0.5f;
            body.color = Color.Lerp(baseColor, tintedColor, wave * amount);
        }
    }

    sealed class DriftEffect : IAmbienceEffect
    {
        readonly PlanetAmbienceKit owner;
        readonly ParticleSystem particles;
        readonly DriftSettings settings;

        float nextEmit;

        public DriftEffect(PlanetAmbienceKit owner, ParticleSystem particles, DriftSettings settings)
        {
            this.owner = owner;
            this.particles = particles;
            this.settings = settings;

            nextEmit = Time.time + Random.Range(0f, settings.mode == DriftMode.Gust
                ? settings.gustIntervalMax
                : settings.intervalMax);
        }

        public void Tick(float time, bool visible)
        {
            // Ekran dışında hiç parçacık üretilmez.
            if (!visible || particles == null || time < nextEmit) return;

            if (settings.mode == DriftMode.Gust)
            {
                nextEmit = time + Random.Range(settings.gustIntervalMin, settings.gustIntervalMax);
                int count = Random.Range(settings.gustCountMin, settings.gustCountMax + 1);
                for (int i = 0; i < count; i++) EmitOne();
                return;
            }

            nextEmit = time + Random.Range(settings.intervalMin, settings.intervalMax);
            EmitOne();
        }

        void EmitOne()
        {
            float radius = owner.Radius;
            Vector2 position;
            Vector2 velocity;

            switch (settings.mode)
            {
                case DriftMode.Float:
                    position = Random.insideUnitCircle * (radius * settings.fieldRadius);
                    velocity = (new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.15f, 0.75f)).normalized
                                * Random.Range(settings.speedMin, settings.speedMax) + settings.wind) * radius;
                    break;

                case DriftMode.Gust:
                    float side = settings.wind.x >= 0f ? -1f : 1f;
                    position = new Vector2(side * settings.spread * radius,
                        Random.Range(-0.75f, 0.95f) * radius);
                    velocity = (settings.wind * Random.Range(2.2f, 3.4f)
                                + new Vector2(0f, Random.Range(-0.2f, 0.2f))) * radius;
                    break;

                default:
                    position = new Vector2(Random.Range(-settings.spread, settings.spread) * radius,
                        settings.spawnHeight * radius);
                    velocity = (settings.wind + Vector2.down * Random.Range(settings.speedMin, settings.speedMax))
                               * radius;
                    break;
            }

            owner.EmitParticle(particles, position, velocity,
                settings.lifetime * Random.Range(0.85f, 1.15f),
                radius * Random.Range(settings.sizeMin, settings.sizeMax),
                Color.Lerp(settings.colorA, settings.colorB, Random.value));
        }
    }
}
