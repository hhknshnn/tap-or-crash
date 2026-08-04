using System.Collections.Generic;
using UnityEngine;

// One glass material language, generated once at runtime.
//
// Two things separate this from the flat rounded rectangles it replaces:
//
//   Antialiasing. Every shape is coverage-sampled against a signed distance
//   field, so corners are smooth at any size instead of stair-stepped.
//
//   A real rim. Glass reads as glass because its top edge catches light and its
//   bottom edge does not. Rim() bakes that falloff into the stroke, which is
//   what Unity's Outline component — a flat copy nudged two pixels diagonally —
//   can never do.
//
// Every sprite is 9-slice safe: the border equals the corner radius, so the
// stretched middle band is a single constant row and nothing smears.
public static class UIGlass
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // Rim brightness top to bottom. Light comes from above in this game's world;
    // the UI obeys the same rule as the hero rocket's key light.
    const float RimTop = 1.00f;
    const float RimBottom = 0.26f;

    public static Sprite Panel(float radius)
        => Get("panel" + radius, PanelName(radius), () => BuildPanel(radius));

    public static Sprite Rim(float radius, float stroke = 2.5f)
        => Get("rim" + radius + "_" + stroke, RimName(radius), () => BuildRim(radius, stroke));

    public static Sprite Disc => Get("disc", "UIGlass_Disc", BuildDisc);

    public static Sprite DiscRim => Get("discRim", "UIGlass_DiscRim", BuildDiscRim);

    /// Soft radial falloff: halos behind a call to action, shadows beneath a card.
    public static Sprite Glow => Get("glow", "UIGlass_Glow", BuildGlow);

    // The radius is rounded the same way the builders round it, so the name a sprite is
    // looked up by is the name it was baked under.
    static string PanelName(float radius) => "UIGlass_Panel" + Mathf.Max(2, Mathf.RoundToInt(radius));

    static string RimName(float radius) => "UIGlass_Rim" + Mathf.Max(2, Mathf.RoundToInt(radius));

    // Baked assets win over generation. The pixels are the same either way; the difference
    // is that a baked sprite has an asset path, so a surface drawn with it can be stored in
    // the serialized menu instead of turning into a null reference. Radii the menu never
    // uses are not baked and are still generated on demand — see MenuBakedArt.
    static Sprite Get(string key, string assetName, System.Func<Sprite> build)
    {
        if (cache.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;

        sprite = MenuBakedArt.Load(assetName) ?? build();
        cache[key] = sprite;
        return sprite;
    }

    // ── shape sampling ───────────────────────────────────────────────────────

    // Signed distance to a rounded rectangle: negative inside, zero on the edge.
    static float RoundRectDistance(float x, float y, float w, float h, float r)
    {
        float dx = Mathf.Max(Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r), 0f);
        float dy = Mathf.Max(Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r), 0f);
        return Mathf.Sqrt(dx * dx + dy * dy) - r;
    }

    // One pixel of coverage either side of the edge. Wider looks blurry, narrower
    // re-introduces the stepping this exists to remove.
    static float Coverage(float distance) => Mathf.Clamp01(0.5f - distance);

    static Texture2D NewTexture(int size, string name) => NewTexture(size, size, name);

    static Texture2D NewTexture(int width, int height, string name)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = name,
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        return texture;
    }

    static Sprite Finish(Texture2D texture, Color32[] pixels, Vector4 border, string name)
    {
        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        Sprite sprite = Sprite.Create(texture,
            new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f),
            100f, 0, SpriteMeshType.FullRect, border);
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // ── builders ─────────────────────────────────────────────────────────────

    static Sprite BuildPanel(float radius)
    {
        int r = Mathf.Max(2, Mathf.RoundToInt(radius));
        int size = r * 2 + 4;
        Texture2D texture = NewTexture(size, "UIGlass_Panel" + r);
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float coverage = Coverage(RoundRectDistance(x + 0.5f, y + 0.5f, size, size, r));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * 255f));
            }

        return Finish(texture, pixels, new Vector4(r, r, r, r), "UIGlass_Panel" + r);
    }

    static Sprite BuildRim(float radius, float stroke)
    {
        int r = Mathf.Max(2, Mathf.RoundToInt(radius));
        int size = r * 2 + 4;
        float half = stroke * 0.5f;

        Texture2D texture = NewTexture(size, "UIGlass_Rim" + r);
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            // Smoothstep rather than a straight ramp: the highlight should pool
            // along the top edge and fade away, not slide evenly down the side.
            float height = (y + 0.5f) / size;
            float lit = Mathf.Lerp(RimBottom, RimTop, height * height * (3f - 2f * height));

            for (int x = 0; x < size; x++)
            {
                float distance = RoundRectDistance(x + 0.5f, y + 0.5f, size, size, r);
                float coverage = Coverage(Mathf.Abs(distance + half) - half);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * lit * 255f));
            }
        }

        return Finish(texture, pixels, new Vector4(r, r, r, r), "UIGlass_Rim" + r);
    }

    static Sprite BuildDisc()
    {
        const int size = 128;
        const float radius = size * 0.5f - 1f;
        Texture2D texture = NewTexture(size, "UIGlass_Disc");
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                    new Vector2(size * 0.5f, size * 0.5f)) - radius;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Coverage(distance) * 255f));
            }

        return Finish(texture, pixels, Vector4.zero, "UIGlass_Disc");
    }

    static Sprite BuildDiscRim()
    {
        const int size = 128;
        const float radius = size * 0.5f - 2f;
        const float half = 1.6f;

        Texture2D texture = NewTexture(size, "UIGlass_DiscRim");
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float height = (y + 0.5f) / size;
            float lit = Mathf.Lerp(RimBottom, RimTop, height * height * (3f - 2f * height));

            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f),
                    new Vector2(size * 0.5f, size * 0.5f)) - radius;
                float coverage = Coverage(Mathf.Abs(distance + half) - half);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(coverage * lit * 255f));
            }
        }

        return Finish(texture, pixels, Vector4.zero, "UIGlass_DiscRim");
    }

    static Sprite BuildGlow()
    {
        const int size = 128;
        const float radius = size * 0.5f;
        Texture2D texture = NewTexture(size, "UIGlass_Glow");
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float t = Mathf.Clamp01(1f - Vector2.Distance(
                    new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius)) / radius);
                // Squared falloff: a linear halo has a visible outer ring.
                float alpha = t * t * t * (3f - 2f * t) * 0.5f + t * t * 0.5f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
            }

        return Finish(texture, pixels, Vector4.zero, "UIGlass_Glow");
    }
}
