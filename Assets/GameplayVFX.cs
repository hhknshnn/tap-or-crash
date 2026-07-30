using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class GameplayVFX : MonoBehaviour
{
    public static GameplayVFX instance;

    private Canvas canvas;
    private readonly Queue<ParticleSystem> burstPool = new Queue<ParticleSystem>();
    private readonly HashSet<ParticleSystem> activeBursts = new HashSet<ParticleSystem>();
    private const int BurstPoolPrewarm = 5;
    private const int BurstPoolMaximum = 12;
    private int lastCrashFrame = -1;

    enum BurstGeometry
    {
        Soft,
        Triangle,
        Diamond
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstall() => SceneInstaller.RunOnEveryScene(() => Ensure());

    public static GameplayVFX Ensure()
    {
        if (instance != null) return instance;
        GameplayVFX existing = FindAnyObjectByType<GameplayVFX>();
        if (existing != null) return existing;

        GameObject go = new GameObject("GameplayVFX");
        return go.AddComponent<GameplayVFX>();
    }

    void Awake()
    {
        if (instance == null) instance = this;
        else { Destroy(gameObject); return; }
    }

    void Start()
    {
        canvas = FindAnyObjectByType<Canvas>();
        PrewarmBurstPool();
    }

    public void PlayLaunch(Vector3 position, Vector3 direction)
    {
        Vector3 origin = position - direction.normalized * 0.28f;
        CreateBurst(origin, new Color(1f, 0.48f, 0.08f, 1f), 6, 0.8f, 1.8f, 0.04f, 0.10f,
            BurstGeometry.Triangle, 0.18f, 0.34f, 0.12f);
    }

    public void PlayLanding(Vector3 position, RocketController.LandingQuality quality)
    {
        Color color = quality == RocketController.LandingQuality.Perfect
            ? new Color(1f, 0.82f, 0.16f, 1f)
            : quality == RocketController.LandingQuality.EdgeCatch
                ? new Color(1f, 0.38f, 0.08f, 1f)
                : new Color(0.24f, 0.85f, 1f, 1f);

        int count = quality == RocketController.LandingQuality.Normal ? 8 : 12;
        CreateBurst(position, color, count, 1.1f, 2.6f, 0.045f, 0.13f,
            BurstGeometry.Diamond, 0.20f, 0.42f, 0.16f);
    }

    public void PlayCrash(Vector3 position)
    {
        PlayCrash(position, Quaternion.identity);
    }

    public void PlayCrash(Vector3 position, Quaternion rocketRotation)
    {
        if (lastCrashFrame == Time.frameCount) return;
        lastCrashFrame = Time.frameCount;

        CrashDebrisPresentation.Spawn(position, rocketRotation);

        // Bright core, expanding fire, metal sparks and a short soft smoke tail.
        // Every layer is realtime-driven so the complete beat remains inside 0.8 s.
        CreateBurst(position, new Color(1f, 0.96f, 0.72f, 1f), 1, 0f, 0f, 0.58f, 0.68f,
            BurstGeometry.Soft, 0.12f, 0.20f, 0f);
        CreateBurst(position, new Color(1f, 0.25f, 0.055f, 0.96f), 2, 0.2f, 0.8f, 0.28f, 0.50f,
            BurstGeometry.Soft, 0.32f, 0.48f, 0.08f);
        CreateBurst(position, new Color(1f, 0.30f, 0.07f, 1f), 18, 1.9f, 4.8f, 0.065f, 0.18f,
            BurstGeometry.Triangle, 0.24f, 0.52f, 0.14f);
        CreateBurst(position, new Color(1f, 0.78f, 0.20f, 1f), 12, 1.25f, 3.8f, 0.045f, 0.12f,
            BurstGeometry.Diamond, 0.22f, 0.46f, 0.11f);
        CreateBurst(position + Vector3.up * 0.08f, new Color(0.18f, 0.20f, 0.25f, 0.62f),
            7, 0.32f, 1.15f, 0.20f, 0.38f, BurstGeometry.Soft, 0.46f, 0.72f, 0.18f);
    }

    public void PlayMilestone(int score)
    {
        if (score == 10)
            StartCoroutine(MilestoneBanner("ASTEROIDS ONLINE", "WATCH THE WARNING ARROW", new Color(1f, 0.40f, 0.12f)));
        else if (score == 15)
            StartCoroutine(MilestoneBanner("MOVING ORBITS", "TRACK THE PLANET BEFORE LAUNCH", new Color(0.30f, 0.84f, 1f)));
    }

    void CreateBurst(
        Vector3 position,
        Color color,
        int count,
        float minSpeed,
        float maxSpeed,
        float minSize,
        float maxSize,
        BurstGeometry geometry,
        float minLifetime,
        float maxLifetime,
        float shapeRadius)
    {
        ParticleSystem particles = AcquireBurst();
        if (particles == null) return;

        GameObject go = particles.gameObject;
        go.name = geometry == BurstGeometry.Soft ? "VFX_Flash" : "VFX_GeometricBurst";
        go.transform.position = position;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.duration = 0.35f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = shapeRadius > 0f;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = shapeRadius;
        shape.arc = 360f;

        ParticleSystem.ColorOverLifetimeModule colorOverLife = particles.colorOverLifetime;
        colorOverLife.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(color.a, 0.55f), new GradientAlphaKey(0f, 1f) });
        colorOverLife.color = gradient;

        ParticleSystem.SizeOverLifetimeModule sizeOverLife = particles.sizeOverLifetime;
        sizeOverLife.enabled = true;
        AnimationCurve sizeCurve = geometry == BurstGeometry.Soft && count == 1
            ? new AnimationCurve(
                new Keyframe(0f, 0.55f),
                new Keyframe(0.35f, 1.18f),
                new Keyframe(1f, 0f))
            : new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.72f, 0.82f),
                new Keyframe(1f, 0f));
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        ParticleSystem.RotationOverLifetimeModule rotationOverLife = particles.rotationOverLifetime;
        rotationOverLife.enabled = geometry != BurstGeometry.Soft;
        rotationOverLife.z = new ParticleSystem.MinMaxCurve(-3.4f, 3.4f);

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (geometry == BurstGeometry.Soft)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = VfxSpriteFactory.ParticleMaterial;
        }
        else
        {
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = geometry == BurstGeometry.Triangle
                ? VfxSpriteFactory.TriangleMesh
                : VfxSpriteFactory.DiamondMesh;
            renderer.sharedMaterial = VfxSpriteFactory.GeometricParticleMaterial;
        }
        renderer.sortingOrder = 12;

        go.SetActive(true);
        activeBursts.Add(particles);
        particles.Play();
        particles.Emit(count);
        StartCoroutine(ReleaseBurstAfterRealtime(particles, Mathf.Max(0.55f, maxLifetime + 0.28f)));
    }

    void PrewarmBurstPool()
    {
        for (int i = 0; i < BurstPoolPrewarm; i++)
        {
            ParticleSystem particles = CreateBurstParticle();
            if (particles != null) burstPool.Enqueue(particles);
        }
    }

    ParticleSystem AcquireBurst()
    {
        while (burstPool.Count > 0)
        {
            ParticleSystem particles = burstPool.Dequeue();
            if (particles != null) return particles;
        }

        if (activeBursts.Count >= BurstPoolMaximum) return null;
        return CreateBurstParticle();
    }

    ParticleSystem CreateBurstParticle()
    {
        GameObject go = new GameObject("VFX_PooledBurst");
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        ParticleSystem particles = go.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return particles;
    }

    IEnumerator ReleaseBurstAfterRealtime(ParticleSystem particles, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        if (particles == null) yield break;

        activeBursts.Remove(particles);
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        particles.gameObject.SetActive(false);
        particles.transform.SetParent(transform, false);

        if (burstPool.Count >= BurstPoolMaximum)
            Destroy(particles.gameObject);
        else
            burstPool.Enqueue(particles);
    }

    IEnumerator MilestoneBanner(string title, string subtitle, Color accent)
    {
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) yield break;

        GameObject go = new GameObject("MilestoneBanner");
        go.transform.SetParent(canvas.transform, false);
        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, 80f);
        rect.sizeDelta = new Vector2(600f, 112f);

        Image image = go.AddComponent<Image>();
        UIStyleKit.ApplyPanel(image, new Color(0.035f, 0.065f, 0.15f, 0.96f));
        image.raycastTarget = false;
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(accent.r, accent.g, accent.b, 0.65f);
        outline.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI titleText = UIStyleKit.MakeLabel(go.transform, title, 25f, accent,
            new Vector2(0f, 20f), new Vector2(560f, 40f), FontStyles.Bold);
        titleText.characterSpacing = 3f;
        TextMeshProUGUI subtitleText = UIStyleKit.MakeLabel(go.transform, subtitle, 14f, UIStyleKit.TextSub,
            new Vector2(0f, -24f), new Vector2(560f, 30f), FontStyles.Bold);
        subtitleText.characterSpacing = 2f;

        CanvasGroup group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        float canvasHeight = canvas.GetComponent<RectTransform>().rect.height;
        float safeTop = Screen.height > 0
            ? (Screen.height - Screen.safeArea.yMax) / Screen.height * canvasHeight
            : 0f;
        Vector2 hidden = new Vector2(0f, 80f - safeTop);
        Vector2 shown = new Vector2(0f, -150f - safeTop);
        float elapsed = 0f;
        while (elapsed < 0.28f)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.SmoothStep(0f, 1f, elapsed / 0.28f);
            rect.anchoredPosition = Vector2.Lerp(hidden, shown, p);
            group.alpha = p;
            yield return null;
        }

        yield return new WaitForSecondsRealtime(1.35f);

        elapsed = 0f;
        while (elapsed < 0.24f)
        {
            elapsed += Time.unscaledDeltaTime;
            float p = Mathf.Clamp01(elapsed / 0.24f);
            group.alpha = 1f - p;
            rect.anchoredPosition = Vector2.Lerp(shown, new Vector2(0f, -205f - safeTop), p);
            yield return null;
        }
        Destroy(go);
    }

}

public static class VfxSpriteFactory
{
    private static Texture2D softTexture;
    private static Sprite softSprite;
    private static Texture2D sharpFlameTexture;
    private static Sprite sharpFlameSprite;
    private static Material particleMaterial;
    private static Material geometricParticleMaterial;
    private static Material trailMaterial;
    private static Mesh triangleMesh;
    private static Mesh diamondMesh;

    public static Sprite SoftSprite
    {
        get
        {
            EnsureTexture();
            if (softSprite == null)
                softSprite = Sprite.Create(softTexture, new Rect(0f, 0f, softTexture.width, softTexture.height),
                    new Vector2(0.5f, 0.5f), softTexture.width, 0, SpriteMeshType.FullRect);
            return softSprite;
        }
    }

    public static Material ParticleMaterial
    {
        get
        {
            EnsureTexture();
            if (particleMaterial != null) return particleMaterial;

            // Sprites/Default, not URP's particle shader.
            //
            // This project renders through the URP 2D Renderer, and that renderer does
            // not draw the 3D "Universal Render Pipeline/Particles/Unlit" pass: it
            // discards the texture's alpha and the per-particle colour and fills the whole
            // quad opaque white. Every soft round particle in the game — ash, embers, lava
            // drops, sparkles — drew as a hard white square, largest and most obvious over
            // the menu's volcano.
            //
            // Stating the blend state (MakeTransparent, below) does not fix it. The blend
            // state was already correct while the squares were still white; the shader
            // itself has to be one the 2D renderer supports. That is the same
            // Sprites/Default the geometric particles have always used, which is exactly
            // why those were the only particles rendering correctly.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            particleMaterial = new Material(shader) { name = "Runtime Soft Particle" };
            particleMaterial.mainTexture = softTexture;
            if (particleMaterial.HasProperty("_BaseMap"))
                particleMaterial.SetTexture("_BaseMap", softTexture);
            MakeTransparent(particleMaterial);
            return particleMaterial;
        }
    }

    /// A material built with `new Material(urpShader)` inherits the shader's defaults,
    /// and URP's particle shaders default to Opaque — which throws the texture's alpha
    /// away and leaves a hard square. This states the blend state instead.
    ///
    /// Necessary but not sufficient: under the URP 2D Renderer the blend state can be
    /// perfectly correct and the particle still draws as an opaque white quad, because
    /// the 2D renderer does not run URP's 3D particle pass at all. Picking a shader the
    /// 2D renderer supports is the other half. See ParticleMaterial above.
    public static void MakeTransparent(Material material)
    {
        if (material == null) return;

        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);   // Transparent
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);       // Alpha
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    public static Sprite SharpFlameSprite
    {
        get
        {
            EnsureSharpFlameTexture();
            if (sharpFlameSprite == null)
            {
                sharpFlameSprite = Sprite.Create(
                    sharpFlameTexture,
                    new Rect(0f, 0f, sharpFlameTexture.width, sharpFlameTexture.height),
                    new Vector2(0.5f, 0.5f),
                    sharpFlameTexture.width,
                    0,
                    SpriteMeshType.FullRect);
                sharpFlameSprite.name = "Runtime Sharp Flame";
            }
            return sharpFlameSprite;
        }
    }

    public static Material GeometricParticleMaterial
    {
        get
        {
            if (geometricParticleMaterial != null) return geometricParticleMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            geometricParticleMaterial = new Material(shader) { name = "Runtime Geometric Particle" };
            geometricParticleMaterial.mainTexture = Texture2D.whiteTexture;
            return geometricParticleMaterial;
        }
    }

    public static Mesh TriangleMesh
    {
        get
        {
            if (triangleMesh == null)
            {
                triangleMesh = new Mesh { name = "Runtime VFX Triangle" };
                triangleMesh.vertices = new[]
                {
                    new Vector3(-0.55f, -0.42f, 0f),
                    new Vector3(0.58f, -0.30f, 0f),
                    new Vector3(-0.04f, 0.66f, 0f)
                };
                triangleMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                triangleMesh.triangles = new[] { 0, 2, 1 };
                triangleMesh.RecalculateBounds();
                triangleMesh.UploadMeshData(true);
            }
            return triangleMesh;
        }
    }

    public static Mesh DiamondMesh
    {
        get
        {
            if (diamondMesh == null)
            {
                diamondMesh = new Mesh { name = "Runtime VFX Diamond" };
                diamondMesh.vertices = new[]
                {
                    new Vector3(0f, 0.68f, 0f),
                    new Vector3(0.58f, 0f, 0f),
                    new Vector3(0f, -0.68f, 0f),
                    new Vector3(-0.58f, 0f, 0f)
                };
                diamondMesh.uv = new[]
                {
                    new Vector2(0.5f, 1f),
                    new Vector2(1f, 0.5f),
                    new Vector2(0.5f, 0f),
                    new Vector2(0f, 0.5f)
                };
                diamondMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                diamondMesh.RecalculateBounds();
                diamondMesh.UploadMeshData(true);
            }
            return diamondMesh;
        }
    }

    public static Material TrailMaterial
    {
        get
        {
            if (trailMaterial != null) return trailMaterial;
            Shader shader = Shader.Find("Sprites/Default");
            trailMaterial = new Material(shader) { name = "Runtime Rocket Trail" };
            return trailMaterial;
        }
    }

    static void EnsureTexture()
    {
        if (softTexture != null) return;

        const int size = 32;
        softTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Runtime Soft Circle",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center) / radius;
                float alpha = Mathf.Pow(Mathf.Clamp01(1f - distance), 1.7f);
                softTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        softTexture.Apply(false, true);
    }

    static void EnsureSharpFlameTexture()
    {
        if (sharpFlameTexture != null) return;

        const int size = 32;
        sharpFlameTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Runtime Sharp Flame Texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color32[] pixels = new Color32[size * size];
        float centerY = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float progress = Mathf.InverseLerp(1f, size - 2f, x);
                float halfHeight = Mathf.Lerp(0.4f, 12.8f, progress);
                float distance = Mathf.Abs(y - centerY);
                byte alpha = distance <= halfHeight
                    ? (byte)255
                    : distance <= halfHeight + 1f
                        ? (byte)Mathf.RoundToInt((halfHeight + 1f - distance) * 255f)
                        : (byte)0;
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }

        sharpFlameTexture.SetPixels32(pixels);
        sharpFlameTexture.Apply(false, true);
    }
}
