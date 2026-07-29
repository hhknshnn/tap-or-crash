using System.Collections.Generic;
using UnityEngine;

// Ambience efektlerinin paylaştığı, çalışma zamanında bir kez üretilen küçük
// siluetler. Hepsi 64×64 beyaz + alfa maskesidir; renk parçacık/sprite üzerinden
// verilir, böylece tek doku birçok tema tarafından yeniden kullanılır.
//
// Yeni bir tema kendi şeklini eklemek isterse buraya bir Sprite özelliği eklemek
// yeterlidir; oyun kodunda değişiklik gerekmez.
public static class AmbienceVfxAssets
{
    const int Size = 64;
    const float Edge = 0.055f;   // kenar yumuşaklığı (normalize birim)

    static Sprite leaf;
    static Sprite petal;
    static Sprite butterfly;
    static Sprite bird;
    static Sprite snowflake;
    static Sprite shard;
    static Sprite sparkle;

    static readonly Dictionary<Sprite, Material> particleMaterials = new Dictionary<Sprite, Material>();

    // ── Siluetler ───────────────────────────────────────────────────────────

    public static Sprite Leaf
    {
        get
        {
            if (leaf == null) leaf = Create("Ambience Leaf", LeafShape);
            return leaf;
        }
    }

    public static Sprite Petal
    {
        get
        {
            if (petal == null) petal = Create("Ambience Petal", PetalShape);
            return petal;
        }
    }

    public static Sprite Butterfly
    {
        get
        {
            if (butterfly == null) butterfly = Create("Ambience Butterfly", ButterflyShape);
            return butterfly;
        }
    }

    public static Sprite Bird
    {
        get
        {
            if (bird == null) bird = Create("Ambience Bird", BirdShape);
            return bird;
        }
    }

    public static Sprite Snowflake
    {
        get
        {
            if (snowflake == null) snowflake = Create("Ambience Snowflake", SnowflakeShape);
            return snowflake;
        }
    }

    public static Sprite Shard
    {
        get
        {
            if (shard == null) shard = Create("Ambience Shard", ShardShape);
            return shard;
        }
    }

    public static Sprite Sparkle
    {
        get
        {
            if (sparkle == null) sparkle = Create("Ambience Sparkle", SparkleShape);
            return sparkle;
        }
    }

    // Yumuşak nokta (polen, buz tozu, sis) için zaten var olan dokuyu paylaşırız.
    public static Sprite SoftDot => VfxSpriteFactory.SoftSprite;

    // ── Parçacık malzemesi ──────────────────────────────────────────────────

    // Sprite başına tek malzeme; aynı şekli kullanan tüm gezegenler aynı malzemeyi
    // paylaşır ve tek çizim çağrısında toplanır.
    public static Material ParticleMaterialFor(Sprite sprite)
    {
        if (sprite == null) return VfxSpriteFactory.ParticleMaterial;
        if (sprite == VfxSpriteFactory.SoftSprite) return VfxSpriteFactory.ParticleMaterial;

        if (particleMaterials.TryGetValue(sprite, out Material cached) && cached != null)
            return cached;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader) { name = "Runtime " + sprite.name };
        material.mainTexture = sprite.texture;
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", sprite.texture);
        // URP's particle shaders default to Opaque, which throws the sprite's
        // alpha away and leaves a hard square. See VfxSpriteFactory.
        VfxSpriteFactory.MakeTransparent(material);

        particleMaterials[sprite] = material;
        return material;
    }

    // ── Doku üretimi ────────────────────────────────────────────────────────

    // shape: [-1,1]² içindeki bir noktadan 0..1 kapaklık döndürür.
    static Sprite Create(string name, System.Func<Vector2, float> shape)
    {
        Color32[] pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                Vector2 p = new Vector2(
                    (x + 0.5f) / Size * 2f - 1f,
                    (y + 0.5f) / Size * 2f - 1f);

                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(shape(p)) * 255f);
                pixels[y * Size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            name = name + " Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);   // CPU kopyasını bırak: mobilde bellek tasarrufu

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size),
            new Vector2(0.5f, 0.5f), Size, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        return sprite;
    }

    static float Fade(float distance) => Mathf.Clamp01(distance / Edge);

    // ── Şekiller ────────────────────────────────────────────────────────────

    // Sivri uçlu yaprak; hafif asimetrik ki dönerken canlı görünsün.
    static float LeafShape(Vector2 p)
    {
        float taper = Mathf.Clamp01(1f - Mathf.Abs(p.y) / 0.92f);
        float halfWidth = 0.33f * Mathf.Pow(taper, 0.55f) * (1f + 0.12f * p.y);
        return Fade(halfWidth - Mathf.Abs(p.x));
    }

    // Üstü yuvarlak, altı sivri taç yaprağı: yuvarlak baş + aşağı doğru daralan kama.
    static float PetalShape(Vector2 p)
    {
        float head = Fade(0.42f - (p - new Vector2(0f, 0.28f)).magnitude);
        float halfWidth = 0.42f * Mathf.Clamp01((p.y + 0.95f) / 1.25f);
        float wedge = Mathf.Min(Fade(halfWidth - Mathf.Abs(p.x)), Fade(0.28f - p.y));
        return Mathf.Max(head, wedge);
    }

    // İnce gövde + dışa doğru eğik dört kanat (simetri |x| üzerinden alınır).
    static float ButterflyShape(Vector2 p)
    {
        float x = Mathf.Abs(p.x);
        float body = Mathf.Min(Fade(0.05f - x), Fade(0.50f - Mathf.Abs(p.y)));
        float upper = Ellipse(new Vector2(x, p.y), new Vector2(0.38f, 0.30f), new Vector2(0.38f, 0.24f), 32f);
        float lower = Ellipse(new Vector2(x, p.y), new Vector2(0.30f, -0.34f), new Vector2(0.30f, 0.19f), -28f);
        return Mathf.Max(body, Mathf.Max(upper, lower));
    }

    // Uzaktan görünen klasik kuş silueti: iki kavisli kanat.
    static float BirdShape(Vector2 p)
    {
        float x = Mathf.Abs(p.x);
        if (x > 0.95f) return 0f;

        float wing = 0.40f * Mathf.Pow(x, 1.25f) - 0.10f;
        float thickness = 0.11f * (1f - x * 0.55f);
        return Mathf.Min(Fade(thickness - Mathf.Abs(p.y - wing)), Fade(0.95f - x));
    }

    // Altı kollu kar tanesi: kollar + çekirdek + kol üstü küçük düğümler.
    static float SnowflakeShape(Vector2 p)
    {
        float r = p.magnitude;
        if (r > 0.98f) return 0f;

        const float sector = Mathf.PI / 3f;
        float angle = Mathf.Atan2(p.y, p.x);
        float offset = Mathf.Abs(Mathf.Repeat(angle + sector * 0.5f, sector) - sector * 0.5f);
        float toArm = offset * r;   // kola olan dik mesafe

        float arm = Mathf.Min(Fade(0.055f - toArm), Fade(0.92f - r));
        float node = Mathf.Min(Fade(0.09f - toArm), Fade(0.075f - Mathf.Abs(r - 0.5f)));
        float core = Fade(0.13f - r);

        return Mathf.Max(core, Mathf.Max(arm, node));
    }

    // Uzun eşkenar dörtgen: buz kristali kıymığı.
    static float ShardShape(Vector2 p)
    {
        float d = Mathf.Abs(p.x) / 0.34f + Mathf.Abs(p.y) / 0.95f;
        return Fade((1f - d) * 0.34f);
    }

    // Dört uçlu yıldız parıltısı.
    static float SparkleShape(Vector2 p)
    {
        float x = Mathf.Abs(p.x);
        float y = Mathf.Abs(p.y);

        float vertical = Mathf.Min(Fade(0.075f * (1f - y) - x), Fade(0.96f - y));
        float horizontal = Mathf.Min(Fade(0.075f * (1f - x) - y), Fade(0.96f - x));
        float core = Fade(0.11f - p.magnitude);

        return Mathf.Max(core, Mathf.Max(vertical, horizontal));
    }

    static float Ellipse(Vector2 p, Vector2 center, Vector2 radii, float rotationDegrees)
    {
        Vector2 local = p - center;
        float radians = rotationDegrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        Vector2 rotated = new Vector2(local.x * cos + local.y * sin, -local.x * sin + local.y * cos);

        float d = new Vector2(rotated.x / Mathf.Max(0.0001f, radii.x),
                              rotated.y / Mathf.Max(0.0001f, radii.y)).magnitude;
        return Fade((1f - d) * Mathf.Min(radii.x, radii.y));
    }
}
