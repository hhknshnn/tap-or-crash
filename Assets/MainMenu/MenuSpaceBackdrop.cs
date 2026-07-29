using System.Collections.Generic;
using UnityEngine;

// The depth layers behind the hero planet, far to near:
//   1 nebula field (a banded cloud, baked in Blender, tinted per theme)
//   2 tiny distant stars
//   3 larger stars, twinkling
//   4 bright stars with a soft flare
//   5 distant asteroids drifting across the frame
//   6 theme-coloured glow around the planet, and a vignette that darkens the corners
//
// The menu camera never moves, so parallax comes from each layer swaying on its own
// slow lissajous path: the nearer the layer, the wider and faster the sway.
//
// Everything here is deliberately quiet. The frame has more in it than before, but the
// nebula stays under 10% alpha, the glow hugs the planet instead of washing the screen,
// and the vignette pulls the corners down — so the planet is still the only thing the
// eye lands on.
//
// Cost per frame: six parent transforms, five asteroid transforms and the twinkle
// colours of one star layer. Layers share a material per texture, so the whole backdrop
// batches into a handful of draw calls and nothing is lit, shadowed or post-processed.
[DisallowMultipleComponent]
public sealed class MenuSpaceBackdrop : MonoBehaviour
{
    sealed class Layer
    {
        public Transform root;
        public Vector2 sway;        // world-unit amplitude
        public Vector2 swaySpeed;
        public float phase;
    }

    struct Twinkle
    {
        public SpriteRenderer renderer;
        public float baseAlpha;
        public float period;
        public float offset;
    }

    struct Asteroid
    {
        public Transform body;
        public Vector2 velocity;
        public float spin;
    }

    readonly List<Layer> layers = new List<Layer>(6);
    readonly List<Twinkle> twinkles = new List<Twinkle>(40);
    readonly List<Asteroid> asteroids = new List<Asteroid>(5);

    SpriteRenderer glow;
    float glowBaseAlpha;
    float halfWidth;
    float halfHeight;

    public void Build(float visibleHalfWidth, float visibleHalfHeight, Color accent)
    {
        halfWidth = visibleHalfWidth;
        halfHeight = visibleHalfHeight;

        // Sorting orders keep the backdrop strictly behind the planet (order 0) and its
        // ambience layers, whatever the prefab does with its own offsets.
        BuildNebulae(accent, -95);
        BuildFarStars(-85);
        BuildNearStars(-75);
        BuildBrightStars(-70);
        BuildAsteroids(-60);
        BuildGlow(accent, -50);
        BuildVignette(-45);
    }

    void Update()
    {
        float time = Time.unscaledTime;

        for (int i = 0; i < layers.Count; i++)
        {
            Layer layer = layers[i];
            if (layer.root == null) continue;

            layer.root.localPosition = new Vector3(
                Mathf.Sin(time * layer.swaySpeed.x + layer.phase) * layer.sway.x,
                Mathf.Sin(time * layer.swaySpeed.y + layer.phase * 1.7f) * layer.sway.y,
                layer.root.localPosition.z);
        }

        AnimateTwinkles(time);
        MoveAsteroids();

        if (glow != null)
        {
            float wave = (Mathf.Sin(time * 0.42f) + 1f) * 0.5f;
            Color color = glow.color;
            color.a = glowBaseAlpha * Mathf.Lerp(0.72f, 1.15f, wave);
            glow.color = color;
        }
    }

    // ─── Layers ──────────────────────────────────────────────────────────────

    Layer CreateLayer(string name, float depth, Vector2 sway, Vector2 swaySpeed)
    {
        GameObject go = new GameObject(name);
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, depth);

        Layer layer = new Layer
        {
            root = go.transform,
            sway = sway,
            swaySpeed = swaySpeed,
            phase = Random.Range(0f, Mathf.PI * 2f)
        };
        layers.Add(layer);
        return layer;
    }

    // Layer 1 — the deep field. Clouds are laid along one diagonal band rather than
    // scattered, which is what makes it read as a galaxy edge instead of coloured fog.
    // Two of them are pulled towards the theme colour so the void belongs to this world.
    void BuildNebulae(Color accent, int sortingOrder)
    {
        Layer layer = CreateLayer("Layer1_Nebulae", 7f, new Vector2(0.07f, 0.045f), new Vector2(0.041f, 0.031f));

        Color[] palette =
        {
            new Color(0.36f, 0.16f, 0.68f),                             // violet
            new Color(0.10f, 0.22f, 0.66f),                             // deep blue
            Color.Lerp(accent, new Color(0.24f, 0.30f, 0.86f), 0.5f),   // theme-stained
            new Color(0.58f, 0.14f, 0.44f),                             // magenta
            Color.Lerp(accent, new Color(0.16f, 0.34f, 0.78f), 0.72f)
        };

        // The band runs lower-left to upper-right, behind and past the planet.
        Vector2 direction = new Vector2(0.82f, 0.57f).normalized;

        for (int i = 0; i < 5; i++)
        {
            float along = Mathf.Lerp(-1.25f, 1.25f, i / 4f) + Random.Range(-0.12f, 0.12f);
            float across = Random.Range(-0.34f, 0.34f);

            Vector3 position = new Vector3(
                (direction.x * along - direction.y * across) * halfWidth * 1.35f,
                (direction.y * along + direction.x * across) * halfHeight * 0.95f, 0f);

            Color tint = palette[i % palette.Length];
            tint.a = Random.Range(0.10f, 0.16f);

            // Alternating cloud and strand: one shape repeated five times is what
            // made the old band read as a single smear.
            Sprite cloud = i % 2 == 0 ? SpaceArt.NebulaSoft : SpaceArt.NebulaWisp;

            SpriteRenderer renderer = MenuShowcaseAssets.CreateSprite(layer.root, "Nebula",
                cloud, sortingOrder, position,
                Random.Range(halfWidth * 1.25f, halfWidth * 2.3f), tint);

            // Stretched along the band, so the clouds flow into one another.
            float roll = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + Random.Range(-14f, 14f);
            renderer.transform.localRotation = Quaternion.Euler(0f, 0f, roll);
            renderer.transform.localScale = new Vector3(
                renderer.transform.localScale.x,
                renderer.transform.localScale.y * Random.Range(0.45f, 0.72f),
                1f);
        }

        // One distant galaxy, pushed to a corner. It gives the menu's void the same
        // far horizon the gameplay sky has, and it is the only warm light in the
        // frame when the theme is a cold one.
        // SpaceArt.CreateSprite sizes in world units rather than in raw scale, which
        // is what lets these two share the gameplay bakes without being retuned.
        SpriteRenderer galaxy = SpaceArt.CreateSprite(layer.root, "GalaxyBand",
            SpaceArt.GalaxyBand, sortingOrder + 1,
            new Vector3(halfWidth * Random.Range(0.55f, 0.9f) * (Random.value < 0.5f ? -1f : 1f),
                        halfHeight * Random.Range(0.45f, 0.8f), 0f),
            halfWidth * Random.Range(0.9f, 1.4f),
            new Color(1f, 0.95f, 0.86f, Random.Range(0.16f, 0.24f)));
        galaxy.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-30f, 30f));

        // Two dust patches fill the gaps between stars, so the void is never empty.
        for (int i = 0; i < 2; i++)
        {
            SpriteRenderer dust = SpaceArt.CreateSprite(layer.root, "DustPatch",
                SpaceArt.DustPatch, sortingOrder + 2,
                new Vector3(Random.Range(-halfWidth, halfWidth), Random.Range(-halfHeight, halfHeight), 0f),
                Random.Range(halfWidth * 0.9f, halfWidth * 1.6f),
                new Color(0.86f, 0.9f, 1f, Random.Range(0.10f, 0.16f)));
            dust.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        }
    }

    // Layer 2 — the star dust. Small, dim, plentiful and never animated individually.
    void BuildFarStars(int sortingOrder)
    {
        Layer layer = CreateLayer("Layer2_FarStars", 5f, new Vector2(0.04f, 0.026f), new Vector2(0.085f, 0.055f));

        // Kept deliberately modest: every star is its own quad, and the layer has to pay
        // for itself in draw calls on a phone.
        for (int i = 0; i < 78; i++)
        {
            Color color = StarColor(Random.value);
            color.a = Random.Range(0.16f, 0.46f);

            MenuShowcaseAssets.CreateSprite(layer.root, "FarStar", VfxSpriteFactory.SoftSprite, sortingOrder,
                new Vector3(Random.Range(-halfWidth, halfWidth) * 1.1f,
                            Random.Range(-halfHeight, halfHeight) * 1.05f, 0f),
                Random.Range(0.02f, 0.046f), color);
        }
    }

    // Layer 3 — the readable stars. Only these twinkle, which is what sells the depth.
    void BuildNearStars(int sortingOrder)
    {
        Layer layer = CreateLayer("Layer3_NearStars", 4f, new Vector2(0.15f, 0.095f), new Vector2(0.125f, 0.096f));

        for (int i = 0; i < 24; i++)
        {
            Color color = StarColor(Random.value);
            float alpha = Random.Range(0.5f, 0.9f);
            color.a = alpha;

            SpriteRenderer renderer = MenuShowcaseAssets.CreateSprite(layer.root, "NearStar",
                VfxSpriteFactory.SoftSprite, sortingOrder,
                new Vector3(Random.Range(-halfWidth, halfWidth) * 1.05f,
                            Random.Range(-halfHeight, halfHeight), 0f),
                Random.Range(0.06f, 0.13f), color);

            twinkles.Add(new Twinkle
            {
                renderer = renderer,
                baseAlpha = alpha,
                period = Random.Range(1.8f, 5.2f),
                offset = Random.Range(0f, 10f)
            });
        }
    }

    // Layer 4 — a few landmark stars with a flare. Kept away from the middle of the
    // frame so nothing bright ever sits next to the planet.
    void BuildBrightStars(int sortingOrder)
    {
        Layer layer = CreateLayer("Layer4_BrightStars", 3.5f, new Vector2(0.2f, 0.13f), new Vector2(0.15f, 0.115f));

        for (int i = 0; i < 5; i++)
        {
            Color color = StarColor(Random.value);
            float alpha = Random.Range(0.62f, 0.85f);
            color.a = alpha;

            float x = Random.Range(0.45f, 1.0f) * halfWidth * (Random.value < 0.5f ? -1f : 1f);
            float y = Random.Range(-1f, 1f) * halfHeight * 0.95f;

            SpriteRenderer renderer = MenuShowcaseAssets.CreateSprite(layer.root, "BrightStar",
                MenuShowcaseAssets.StarFlare, sortingOrder, new Vector3(x, y, 0f),
                Random.Range(0.3f, 0.52f), color);

            twinkles.Add(new Twinkle
            {
                renderer = renderer,
                baseAlpha = alpha,
                period = Random.Range(3.4f, 7f),
                offset = Random.Range(0f, 10f)
            });
        }
    }

    // Layer 5 — rocks crossing the frame, lit from the same side as the planet.
    // Recycled in place: no runtime allocation, no particle system, five transform
    // writes per frame.
    void BuildAsteroids(int sortingOrder)
    {
        Layer layer = CreateLayer("Layer5_Asteroids", 3f, new Vector2(0.045f, 0.035f), new Vector2(0.16f, 0.12f));
        Sprite[] rocks = MenuShowcaseAssets.Asteroids;

        for (int i = 0; i < 5; i++)
        {
            float shade = Random.Range(0.42f, 0.66f);
            SpriteRenderer renderer = MenuShowcaseAssets.CreateSprite(layer.root, "Asteroid",
                rocks[i % rocks.Length], sortingOrder,
                new Vector3(Random.Range(-halfWidth, halfWidth),
                            Random.Range(-halfHeight, halfHeight), 0f),
                Random.Range(0.13f, 0.28f),
                new Color(shade, shade * 0.95f, shade * 0.88f, Random.Range(0.34f, 0.55f)));

            renderer.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            asteroids.Add(new Asteroid
            {
                body = renderer.transform,
                velocity = new Vector2(Random.Range(-0.085f, -0.02f), Random.Range(-0.045f, -0.012f)),
                spin = Random.Range(-5.5f, 5.5f)
            });
        }
    }

    // Layer 6 — the theme's own light bleeding into the void behind the planet. Tight
    // and faint: the old full-screen wash was what made the background read as flat.
    void BuildGlow(Color accent, int sortingOrder)
    {
        Layer layer = CreateLayer("Layer6_AtmosphericGlow", 2f, new Vector2(0.028f, 0.02f), new Vector2(0.07f, 0.05f));

        // Saturated rather than pale: over near-black, a washed accent reads as grey fog.
        Color tint = Saturate(accent, 1.35f);

        glowBaseAlpha = 0.032f;
        glow = MenuShowcaseAssets.CreateSprite(layer.root, "AtmosphericGlow", VfxSpriteFactory.SoftSprite,
            sortingOrder, Vector3.zero, halfWidth * 1.8f,
            new Color(tint.r, tint.g, tint.b, glowBaseAlpha));

        // A tighter pool of colour hugging the planet itself.
        MenuShowcaseAssets.CreateSprite(layer.root, "AtmosphericCore", VfxSpriteFactory.SoftSprite,
            sortingOrder, Vector3.zero, halfWidth * 1.05f,
            new Color(tint.r, tint.g, tint.b, 0.04f));
    }

    // Corners pulled down towards black. One unlit quad, no post-processing. Sized to
    // the visible frame — any larger and the dark ring falls outside the screen.
    void BuildVignette(int sortingOrder)
    {
        SpriteRenderer renderer = MenuShowcaseAssets.CreateSprite(transform, "Vignette",
            MenuShowcaseAssets.Vignette, sortingOrder, new Vector3(0f, 0f, 1.5f), 1f,
            new Color(0f, 0f, 0f, 0.62f));
        renderer.transform.localScale = new Vector3(halfWidth * 2.15f, halfHeight * 2.15f, 1f);
    }

    // ─── Animation ───────────────────────────────────────────────────────────

    void AnimateTwinkles(float time)
    {
        for (int i = 0; i < twinkles.Count; i++)
        {
            Twinkle twinkle = twinkles[i];
            if (twinkle.renderer == null) continue;

            float wave = (Mathf.Sin((time + twinkle.offset) * (Mathf.PI * 2f / twinkle.period)) + 1f) * 0.5f;
            Color color = twinkle.renderer.color;
            color.a = twinkle.baseAlpha * Mathf.Lerp(0.35f, 1f, wave);
            twinkle.renderer.color = color;
        }
    }

    void MoveAsteroids()
    {
        float delta = Time.unscaledDeltaTime;
        float boundX = halfWidth * 1.35f;
        float boundY = halfHeight * 1.25f;

        for (int i = 0; i < asteroids.Count; i++)
        {
            Asteroid asteroid = asteroids[i];
            if (asteroid.body == null) continue;

            Vector3 position = asteroid.body.localPosition;
            position.x += asteroid.velocity.x * delta;
            position.y += asteroid.velocity.y * delta;

            // Wrap to the opposite edge instead of destroying and respawning.
            if (position.x < -boundX) position.x = boundX;
            if (position.y < -boundY) position.y = boundY;

            asteroid.body.localPosition = position;
            asteroid.body.Rotate(0f, 0f, asteroid.spin * delta);
        }
    }

    // Pushes a colour away from grey without touching its brightness.
    static Color Saturate(Color color, float amount)
    {
        float luminance = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
        return new Color(
            Mathf.Clamp01(luminance + (color.r - luminance) * amount),
            Mathf.Clamp01(luminance + (color.g - luminance) * amount),
            Mathf.Clamp01(luminance + (color.b - luminance) * amount),
            color.a);
    }

    static Color StarColor(float roll)
    {
        if (roll < 0.62f) return Color.white;
        if (roll < 0.84f) return new Color(0.78f, 0.88f, 1f);
        return new Color(1f, 0.94f, 0.8f);
    }
}
