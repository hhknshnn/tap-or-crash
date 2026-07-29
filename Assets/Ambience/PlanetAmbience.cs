using System;
using System.Collections.Generic;
using UnityEngine;

// Gezegen temalarının ortak dekoratif altyapısı.
//
// Sözleşme: bu sınıfın ürettiği hiçbir nesnenin collider'ı, trigger'ı veya oyun
// tag'i yoktur ve gezegenin kendi SpriteRenderer sınırları hiç değişmez. Yörünge
// yarıçapı, çarpışma ve hasar mantığı bu katmandan etkilenmez.
//
// Yeni tema eklemek için: PlanetAmbience'tan türeyen bir dosya ekle, içinde
// [RuntimeInitializeOnLoadMethod] ile kendini Register() et. Oyun kodunda
// (PlanetPresentation, PlanetSpawner, RocketController) hiçbir değişiklik gerekmez.
public abstract class PlanetAmbience : MonoBehaviour
{
    protected const int GlowSortingOffset     = 1;
    protected const int ParticleSortingOffset = 2;

    static readonly List<PlanetAmbienceTheme> themes = new List<PlanetAmbienceTheme>();

    // ── Tema kaydı ──────────────────────────────────────────────────────────

    public static IReadOnlyList<PlanetAmbienceTheme> Themes => themes;

    public static void Register(PlanetAmbienceTheme theme)
    {
        if (theme == null || theme.ComponentType == null) return;
        if (!typeof(PlanetAmbience).IsAssignableFrom(theme.ComponentType))
        {
            Debug.LogWarning($"PlanetAmbience: '{theme.Name}' teması PlanetAmbience'tan türemiyor, yok sayıldı.");
            return;
        }

        // Domain reload kapalıyken aynı tema tekrar kaydedilebilir; ad üzerinden tekilleştir.
        for (int i = 0; i < themes.Count; i++)
        {
            if (themes[i].Name == theme.Name) { themes[i] = theme; return; }
        }
        themes.Add(theme);
    }

    public static PlanetAmbienceTheme ResolveTheme(Transform planet)
    {
        if (planet == null || themes.Count == 0) return null;

        string objectName = planet.name;
        SpriteRenderer renderer = planet.GetComponent<SpriteRenderer>();
        string spriteName = renderer != null && renderer.sprite != null ? renderer.sprite.name : null;

        for (int i = 0; i < themes.Count; i++)
        {
            PlanetAmbienceTheme theme = themes[i];
            if (theme.Matches(objectName) || (spriteName != null && theme.Matches(spriteName)))
                return theme;
        }
        return null;
    }

    // PlanetPresentation'ın tek giriş noktası: hangi temanın uygun olduğunu bilmesi gerekmez.
    public static void Attach(Transform planet)
    {
        if (planet == null || planet.GetComponent<PlanetAmbience>() != null) return;

        PlanetAmbienceTheme theme = ResolveTheme(planet);
        if (theme == null) return;

        planet.gameObject.AddComponent(theme.ComponentType);
    }

    // Gezegenin hâle rengi de temadan gelir; eşleşme yoksa varsayılan korunur.
    public static Color AuraColorFor(Transform planet, Color fallback)
    {
        PlanetAmbienceTheme theme = ResolveTheme(planet);
        if (theme == null) return fallback;

        Color aura = theme.AuraColor;
        return new Color(aura.r, aura.g, aura.b, fallback.a);
    }

    // Tema adına göre vurgu rengi. HUD (LevelProgressUI, LevelIntroUI) bunu kullanır;
    // yeni bir tema kaydedildiğinde arayüz rengi kendiliğinden doğru olur.
    public static Color AccentColorFor(string themeName, Color fallback)
    {
        if (string.IsNullOrEmpty(themeName)) return fallback;

        string trimmed = themeName.Trim();
        for (int i = 0; i < themes.Count; i++)
        {
            if (string.Equals(themes[i].Name, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                Color accent = themes[i].AuraColor;
                return new Color(accent.r, accent.g, accent.b, fallback.a);
            }
        }
        return fallback;
    }

    // ── Ortak durum ─────────────────────────────────────────────────────────

    protected SpriteRenderer PlanetRenderer { get; private set; }

    // Gezegen yarıçapı, yerel birim cinsinden (≈ sprite yarı genişliği).
    // Tüm efekt boyutları/hızları bunun katı olarak yazılırsa gezegen ölçeğinden bağımsız çalışır.
    protected float LocalRadius { get; private set; }

    // Gezegen başına sabit rastgele faz; nabız/titreşim animasyonları senkron görünmesin diye.
    protected float Phase { get; private set; }

    // Sprite adındaki sıra numarası ("Natural_03" → 3). Numara yoksa addan türetilen
    // kararlı bir sayı. Temalar buna bakarak gezegen başına el yapımı bir kimlik seçer.
    protected int PlanetIndex { get; private set; }

    // Gezegen tipine bağlı, koşudan koşuya değişmeyen rastgelelik. Yerleşim/zamanlama
    // burada üretilirse aynı gezegen her zaman aynı görünür, farklı gezegenler farklı.
    protected AmbienceRandom Variant { get; private set; }

    protected bool PlanetVisible => PlanetRenderer != null && PlanetRenderer.isVisible;

    // Alt sınıflar Start/Update tanımlamaz; Build ve Animate kullanır.
    void Start()
    {
        PlanetRenderer = GetComponent<SpriteRenderer>();
        if (PlanetRenderer == null) { enabled = false; return; }

        Vector3 scale = transform.lossyScale;
        float worldScale = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
        LocalRadius = PlanetPresentation.GetBodyRadius(transform) / worldScale;
        if (LocalRadius <= 0.0001f) { enabled = false; return; }

        string identity = SpriteName ?? name;
        PlanetIndex = ParseTrailingNumber(identity);
        Variant = new AmbienceRandom(StableHash(identity));

        Phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Build();
    }

    void Update()
    {
        Animate(Time.time, PlanetVisible);
    }

    // Katmanları bir kez kurar.
    protected abstract void Build();

    // Her karede çalışır. visible=false iken parçacık üretmeyin; ekran dışında bedava olsun.
    protected virtual void Animate(float time, bool visible) { }

    // ── Ortak yardımcılar ───────────────────────────────────────────────────

    protected string SpriteName =>
        PlanetRenderer != null && PlanetRenderer.sprite != null ? PlanetRenderer.sprite.name : null;

    // Gezegen sprite'ının rengini çarparak tonlar. Sınırlar değişmez, yalnızca renk.
    protected void MultiplyTint(Color tint)
    {
        if (PlanetRenderer == null) return;
        Color color = PlanetRenderer.color;
        PlanetRenderer.color = new Color(color.r * tint.r, color.g * tint.g, color.b * tint.b, color.a);
    }

    // Collider'sız, tag'siz dekoratif sprite katmanı.
    protected SpriteRenderer CreateSprite(string objectName, Sprite sprite, int sortingOffset,
        Vector2 localPosition, float localSize)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(localPosition.x, localPosition.y, 0f);
        go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, localSize);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingLayerID = PlanetRenderer.sortingLayerID;
        renderer.sortingOrder = PlanetRenderer.sortingOrder + sortingOffset;
        return renderer;
    }

    // "Natural_03(Clone)" → 3. Numara bulunamazsa addan türetilen kararlı bir sayı.
    protected static int ParseTrailingNumber(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        int end = -1;
        for (int i = value.Length - 1; i >= 0; i--)
        {
            if (value[i] >= '0' && value[i] <= '9') { end = i; break; }
        }
        if (end < 0) return Mathf.Abs(StableHash(value)) % 1000;

        int start = end;
        while (start > 0 && value[start - 1] >= '0' && value[start - 1] <= '9') start--;

        return int.TryParse(value.Substring(start, end - start + 1), out int parsed)
            ? parsed
            : Mathf.Abs(StableHash(value)) % 1000;
    }

    // Platformdan bağımsız FNV-1a; string.GetHashCode çalışmalar arası değişebilir.
    protected static int StableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261u;
            for (int i = 0; i < value.Length; i++)
            {
                hash ^= value[i];
                hash *= 16777619u;
            }
            return (int)hash;
        }
    }

    // Yalnızca elle Emit() ile beslenen, çarpışmasız parçacık sistemi.
    // Yerel simülasyon + Hierarchy ölçekleme sayesinde boyut/hız LocalRadius katı olarak yazılabilir.
    protected ParticleSystem CreateDecorativeParticles(string objectName, int maxParticles, float maxLifetime)
    {
        return CreateDecorativeParticles(objectName, maxParticles, maxLifetime, VfxSpriteFactory.ParticleMaterial);
    }

    // Şekilli parçacıklar (yaprak, kar tanesi, kelebek...) için malzeme verilebilir.
    // Aynı malzeme paylaşıldığı sürece tüm gezegenler tek çizim çağrısında toplanır.
    protected ParticleSystem CreateDecorativeParticles(string objectName, int maxParticles, float maxLifetime,
        Material material)
    {
        GameObject go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.zero;

        ParticleSystem particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 4f;
        main.startLifetime = maxLifetime;
        main.startSpeed = 0f;
        main.startSize = LocalRadius * 0.12f;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = maxParticles;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;

        // Dekoratif sözleşme: hiçbir şeyle çarpışmaz, hiçbir trigger'ı tetiklemez.
        ParticleSystem.CollisionModule collision = particles.collision;
        collision.enabled = false;
        ParticleSystem.TriggerModule trigger = particles.trigger;
        trigger.enabled = false;

        ParticleSystem.LimitVelocityOverLifetimeModule limit = particles.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.35f;
        limit.limit = new ParticleSystem.MinMaxCurve(LocalRadius * 3.5f);

        ParticleSystem.ColorOverLifetimeModule colorOverLife = particles.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.85f, 0.45f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLife.color = gradient;

        ParticleSystem.SizeOverLifetimeModule sizeOverLife = particles.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.55f, 0.86f),
            new Keyframe(1f, 0.25f)));

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = material != null ? material : VfxSpriteFactory.ParticleMaterial;
        renderer.sortingLayerID = PlanetRenderer.sortingLayerID;
        renderer.sortingOrder = PlanetRenderer.sortingOrder + ParticleSortingOffset;
        renderer.alignment = ParticleSystemRenderSpace.View;

        particles.Play();
        return particles;
    }

    protected static void Emit(ParticleSystem particles, Vector2 localPosition, Vector2 localVelocity,
        float lifetime, float size, Color color)
    {
        if (particles == null) return;

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams
        {
            applyShapeToPosition = false,
            position = localPosition,
            velocity = localVelocity,
            startLifetime = lifetime,
            startSize = size,
            startColor = color
        };
        particles.Emit(emitParams, 1);
    }

    // p noktasından d yönünde ilerlerken gezegen diskinin kenarına olan mesafe.
    protected float DistanceToRim(Vector2 p, Vector2 d)
    {
        float projection = Vector2.Dot(p, d);
        float discriminant = projection * projection + LocalRadius * LocalRadius - p.sqrMagnitude;
        if (discriminant <= 0f) return 0f;
        return Mathf.Max(0f, -projection + Mathf.Sqrt(discriminant));
    }

    // Bir yönü ±degrees aralığında rastgele saptırır.
    protected static Vector2 Spread(Vector2 direction, float degrees)
    {
        float angle = UnityEngine.Random.Range(-degrees, degrees) * Mathf.Deg2Rad;
        float sin = Mathf.Sin(angle);
        float cos = Mathf.Cos(angle);
        return new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
    }

    protected static Vector2 RandomOffset(float radius) => UnityEngine.Random.insideUnitCircle * radius;
}

// Bir gezegen temasının kaydı: hangi gezegenlere uyduğu, hangi bileşeni taktığı
// ve gezegenin hâlesini hangi renge boyadığı.
public sealed class PlanetAmbienceTheme
{
    public string Name { get; }
    public Type ComponentType { get; }
    public Color AuraColor { get; }

    readonly Func<string, bool> matcher;

    public PlanetAmbienceTheme(string name, Type componentType, Color auraColor, Func<string, bool> matcher)
    {
        Name = name;
        ComponentType = componentType;
        AuraColor = auraColor;
        this.matcher = matcher ?? (_ => false);
    }

    public bool Matches(string candidate) => !string.IsNullOrEmpty(candidate) && matcher(candidate);

    // Yaygın durum: prefab/sprite adı tema adıyla başlıyor (Lava_03, Ice_01, "Desert Planet"...).
    public static PlanetAmbienceTheme ByNamePrefix<T>(string name, string prefix, Color auraColor)
        where T : PlanetAmbience
    {
        return new PlanetAmbienceTheme(name, typeof(T), auraColor,
            candidate => candidate.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
