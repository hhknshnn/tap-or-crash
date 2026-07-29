using UnityEngine;

// Art for the gameplay space environment.
//
// The organic pieces (nebulae, the galaxy band, cosmic dust, the rocks) are
// rendered in Blender and baked to PNG under Resources/Space. Every one of them
// is near-white with only a faint cool cast, because the environment tints each
// layer to the active planet theme at runtime — a texture with its own strong
// colour could not serve Natural, Ice and Lava from one draw call.
//
// The flat pieces (the void gradient) are cheaper to generate than to ship, so
// they are built procedurally on first use. Everything falls back to a soft dot
// if an asset is missing, so a broken import degrades the look instead of
// throwing.
public static class SpaceArt
{
    const string Folder = "Space/";

    static Sprite nebulaSoft;
    static Sprite nebulaWisp;
    static Sprite galaxyBand;
    static Sprite dustPatch;
    static Sprite[] rocks;
    static Sprite rockCluster;
    static Sprite voidGradient;

    public static Sprite NebulaSoft => nebulaSoft != null
        ? nebulaSoft
        : nebulaSoft = Load("nebula_soft") ?? VfxSpriteFactory.SoftSprite;

    public static Sprite NebulaWisp => nebulaWisp != null
        ? nebulaWisp
        : nebulaWisp = Load("nebula_wisp") ?? NebulaSoft;

    public static Sprite GalaxyBand => galaxyBand != null
        ? galaxyBand
        : galaxyBand = Load("galaxy_band") ?? NebulaSoft;

    public static Sprite DustPatch => dustPatch != null
        ? dustPatch
        : dustPatch = Load("dust_patch") ?? VfxSpriteFactory.SoftSprite;

    public static Sprite RockCluster => rockCluster != null
        ? rockCluster
        : rockCluster = Load("asteroid_cluster") ?? Rocks[0];

    // Whole patches of distant stars baked into one texture. Ninety individual
    // star quads cost fifty-odd draw calls on a phone; six of these cost six.
    public static Sprite[] StarFields
    {
        get
        {
            if (starFields != null) return starFields;

            var found = new System.Collections.Generic.List<Sprite>(3);
            for (int i = 0; i < 3; i++)
            {
                Sprite sprite = Load("starfield_" + i);
                if (sprite != null) found.Add(sprite);
            }
            if (found.Count == 0) found.Add(DustPatch);

            starFields = found.ToArray();
            return starFields;
        }
    }

    static Sprite[] starFields;

    // Six lit rocks: three baked for the menu backdrop, three more for this one.
    // Sharing the menu's set keeps the two backdrops looking like one universe.
    public static Sprite[] Rocks
    {
        get
        {
            if (rocks != null) return rocks;

            var found = new System.Collections.Generic.List<Sprite>(6);
            for (int i = 0; i < 3; i++)
            {
                Sprite sprite = Resources.Load<Sprite>("Menu/asteroid_" + i);
                if (sprite != null) found.Add(sprite);
            }
            for (int i = 3; i < 6; i++)
            {
                Sprite sprite = Load("asteroid_" + i);
                if (sprite != null) found.Add(sprite);
            }
            if (found.Count == 0) found.Add(MenuShowcaseAssets.Rock);

            rocks = found.ToArray();
            return rocks;
        }
    }

    // Opaque at the bottom, gone at the top. Stacked over the flat void colour it
    // becomes the deep-space gradient without needing a second material or a
    // vertex-coloured mesh.
    public static Sprite VoidGradient
    {
        get
        {
            if (voidGradient != null) return voidGradient;

            const int width = 4;
            const int height = 256;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float t = y / (height - 1f);
                // Eased rather than linear: a straight ramp shows a visible band
                // edge on a phone's 8-bit panel.
                float value = Mathf.SmoothStep(1f, 0f, t);
                value *= value;
                byte alpha = (byte)Mathf.RoundToInt(value * 255f);
                for (int x = 0; x < width; x++) pixels[y * width + x] = new Color32(255, 255, 255, alpha);
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Space Void Gradient",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            voidGradient = Sprite.Create(texture, new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f), height, 0, SpriteMeshType.FullRect);
            voidGradient.name = "Space Void Gradient";
            return voidGradient;
        }
    }

    // The star flare, the vignette and the unlit backdrop material are identical
    // to the menu's, so they are borrowed rather than duplicated: one more shared
    // texture is one fewer draw call and one fewer megabyte.
    public static Sprite StarFlare => MenuShowcaseAssets.StarFlare;
    public static Sprite Vignette => MenuShowcaseAssets.Vignette;
    public static Sprite Dot => VfxSpriteFactory.SoftSprite;
    public static Material UnlitMaterial => MenuShowcaseAssets.UnlitSpriteMaterial;

    // Flat opaque white. The soft dot cannot stand in for this: stretched to fill
    // the screen it becomes a blob with dark corners, not a sky.
    public static Sprite Flat
    {
        get
        {
            if (flat != null) return flat;

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
            {
                name = "Space Flat",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            flat = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f, 0,
                SpriteMeshType.FullRect);
            flat.name = "Space Flat";
            return flat;
        }
    }

    static Sprite flat;

    static Sprite Load(string name) => Resources.Load<Sprite>(Folder + name);

    // Stretches a quad to an exact world width and height, whatever the source
    // texture's aspect or pixels-per-unit happens to be.
    public static void SetWorldSize(SpriteRenderer renderer, float width, float height)
    {
        if (renderer == null || renderer.sprite == null) return;

        Vector2 natural = renderer.sprite.bounds.size;
        renderer.transform.localScale = new Vector3(
            width / Mathf.Max(0.0001f, natural.x),
            height / Mathf.Max(0.0001f, natural.y), 1f);
    }

    // Decorative quad sized in world units, so a 512px nebula and a 64px dot are
    // placed by how big they should look rather than by how big they were baked.
    public static SpriteRenderer CreateSprite(Transform parent, string name, Sprite sprite,
        int sortingOrder, Vector3 localPosition, float worldSize, Color color)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = UnlitMaterial;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;

        float natural = sprite != null ? Mathf.Max(0.0001f, sprite.bounds.size.x) : 1f;
        go.transform.localScale = Vector3.one * (worldSize / natural);
        return renderer;
    }
}
