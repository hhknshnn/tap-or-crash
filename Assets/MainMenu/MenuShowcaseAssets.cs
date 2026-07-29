using System.Collections.Generic;
using UnityEngine;

// Runtime-generated art shared by the whole menu stage. One material and one rock
// silhouette keep the backdrop inside a couple of draw calls, and nothing has to be
// imported or wired in the Inspector.
public static class MenuShowcaseAssets
{
    // Rendered in Blender and baked to PNG: a fractal nebula cloud and three lit
    // low-poly rocks. Loading is optional — every one of them falls back to procedural
    // art, so the menu never depends on an asset being present.
    const string ArtFolder = "Menu/";

    static Material unlitSprite;
    static Sprite rock;
    static Sprite nebula;
    static Sprite[] asteroids;
    static Sprite starFlare;
    static Sprite vignette;

    // Backdrop art must not react to the theme lights: a starfield that dims and tints
    // with the key light stops reading as "distant space". An explicit unlit material
    // keeps every layer constant while the planet itself stays lit.
    public static Material UnlitSpriteMaterial
    {
        get
        {
            if (unlitSprite != null) return unlitSprite;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            unlitSprite = new Material(shader) { name = "Menu Showcase Unlit" };
            return unlitSprite;
        }
    }

    // Irregular rock silhouette with a lit rim, used by the distant asteroid layer.
    // White + alpha only, so the layer tints it per renderer and still shares a batch.
    public static Sprite Rock
    {
        get
        {
            if (rock == null) rock = CreateRock();
            return rock;
        }
    }

    // Baked nebula cloud. White with a cool core, so a single texture tints to any theme.
    public static Sprite Nebula
    {
        get
        {
            if (nebula == null) nebula = Load("nebula_cloud") ?? VfxSpriteFactory.SoftSprite;
            return nebula;
        }
    }

    // Three shaded rocks, all lit from the upper left to match the stage key light.
    public static Sprite[] Asteroids
    {
        get
        {
            if (asteroids != null) return asteroids;

            List<Sprite> loaded = new List<Sprite>(3);
            for (int i = 0; i < 3; i++)
            {
                Sprite sprite = Load("asteroid_" + i);
                if (sprite != null) loaded.Add(sprite);
            }
            if (loaded.Count == 0) loaded.Add(Rock);

            asteroids = loaded.ToArray();
            return asteroids;
        }
    }

    // The handful of bright stars carry a soft four-point flare; the rest stay dots.
    public static Sprite StarFlare
    {
        get
        {
            if (starFlare == null) starFlare = CreateStarFlare();
            return starFlare;
        }
    }

    // Dark corners. Cheaper and more controllable than a post-process vignette, and it
    // is what keeps the eye on the planet once the backdrop got busier.
    public static Sprite Vignette
    {
        get
        {
            if (vignette == null) vignette = CreateVignette();
            return vignette;
        }
    }

    static Sprite Load(string name) => Resources.Load<Sprite>(ArtFolder + name);

    static Sprite CreateStarFlare()
    {
        const int size = 64;
        const float half = size * 0.5f;

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;

                float core = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                core = core * core * core;

                // Two thin crossed streaks, tapering with distance from the core.
                float horizontal = Mathf.Clamp01(1f - Mathf.Abs(dy) * 22f) * Mathf.Clamp01(1f - Mathf.Abs(dx));
                float vertical = Mathf.Clamp01(1f - Mathf.Abs(dx) * 22f) * Mathf.Clamp01(1f - Mathf.Abs(dy));

                float value = Mathf.Clamp01(core + (horizontal + vertical) * 0.45f);
                byte alpha = (byte)Mathf.RoundToInt(value * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        return BuildSprite(pixels, size, "Menu Showcase Star Flare");
    }

    static Sprite CreateVignette()
    {
        const int size = 128;
        const float half = size * 0.5f;

        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half;
                float dy = (y + 0.5f - half) / half;
                float distance = Mathf.Sqrt(dx * dx + dy * dy) * 0.72f;   // 1 at the corners

                float value = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 1f, distance));
                pixels[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(value * 255f));
            }
        }

        return BuildSprite(pixels, size, "Menu Showcase Vignette");
    }

    static Sprite BuildSprite(Color32[] pixels, int size, string name)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name + " Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.SetPixels32(pixels);
        texture.Apply(false, true);   // drop the CPU copy: mobile memory

        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
        sprite.name = name;
        return sprite;
    }

    static Sprite CreateRock()
    {
        const int size = 64;
        const int points = 11;

        float[] radii = new float[points];
        for (int i = 0; i < points; i++) radii[i] = (size * 0.5f - 3f) * Random.Range(0.62f, 1f);

        Color32[] pixels = new Color32[size * size];
        Vector2 center = new Vector2(size * 0.5f, size * 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 offset = new Vector2(x + 0.5f, y + 0.5f) - center;
                float distance = offset.magnitude;

                float angle = Mathf.Atan2(offset.y, offset.x);
                if (angle < 0f) angle += Mathf.PI * 2f;

                float t = angle / (Mathf.PI * 2f) * points;
                int index = (int)t;
                float radius = Mathf.Lerp(radii[index % points], radii[(index + 1) % points], t - index);

                if (distance >= radius)
                {
                    pixels[y * size + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                // Brighter towards the upper-left so every rock reads as lit from the
                // same direction as the stage key light.
                float lit = Mathf.Clamp01(0.5f + Vector2.Dot(offset.normalized, new Vector2(-0.6f, 0.8f)) * 0.5f);
                float shade = Mathf.Lerp(0.42f, 1f, lit * Mathf.Clamp01(1f - distance / radius * 0.55f));
                byte value = (byte)Mathf.RoundToInt(shade * 255f);
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01((radius - distance) / 1.5f) * 255f);

                pixels[y * size + x] = new Color32(value, value, value, alpha);
            }
        }

        return BuildSprite(pixels, size, "Menu Showcase Rock");
    }

    // Decorative stage sprite: no collider, no tag, unlit, batched with its siblings.
    public static SpriteRenderer CreateSprite(Transform parent, string name, Sprite sprite,
        int sortingOrder, Vector3 localPosition, float localSize, Color color)
    {
        GameObject go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = Vector3.one * Mathf.Max(0.0001f, localSize);

        SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sharedMaterial = UnlitSpriteMaterial;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }
}
