using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// The Main Menu's volcanic world, dressed for a single close-up shot.
//
// PRESENTATION ONLY. This is the menu's own hero planet (Resources/Menu/LavaHero) and
// this component is never attached to a gameplay planet — LavaPlanetAmbience keeps
// owning those, at the intensity a planet flying past at gameplay distance can afford.
// Here the world stands still under the camera for as long as the player looks at it,
// so it is allowed to be far more expensive: layered glow, a live eruption cycle with
// occasional major events, lava running down the rock, embers, smoke, flying debris
// and a pulsing light the ship actually flies through.
//
// Everything below is decorative: no collider, no trigger, no tag, no gameplay tag, and
// the hero's own SpriteRenderer bounds are never touched.
public sealed class MenuLavaAmbience : PlanetAmbience
{
    // ── Palette ─────────────────────────────────────────────────────────────
    // Shared with the gameplay theme so the menu reads as the same world, only hotter.
    static readonly Color BasaltTint  = new Color(0.97f, 0.63f, 0.52f, 1f);
    static readonly Color MagmaBright = new Color(1f, 0.90f, 0.52f, 1f);
    static readonly Color MagmaMid    = new Color(1f, 0.45f, 0.10f, 1f);
    static readonly Color MagmaDeep   = new Color(0.76f, 0.14f, 0.03f, 1f);
    static readonly Color AshTone     = new Color(0.24f, 0.20f, 0.19f, 1f);
    static readonly Color RockTone    = new Color(0.34f, 0.16f, 0.11f, 1f);

    // ── Cadence ─────────────────────────────────────────────────────────────
    // A vent erupts on its own rhythm; every so often the whole mountain lets go at
    // once. The major event is what the composition is built around — it has to be rare
    // enough to still feel like an event on the twentieth visit to the menu.
    const float VentEruptionMin = 2.6f;
    const float VentEruptionMax = 5.4f;
    const float MajorEruptionMin = 11f;
    const float MajorEruptionMax = 18f;

    sealed class Vent
    {
        public Vector2 position;       // local
        public Vector2 direction;      // local, normalised: the way the plume leaves
        public float radius;           // mouth radius, local
        public float nextEruption;
        public float nextEmberPuff;
        public float phase;
        public SpriteRenderer mouthGlow;
        public Transform haze;
        public SpriteRenderer hazeRenderer;
        public float glowBoost;        // 0-1, decays after every eruption
        public float hazeBoost;
    }

    // A crust channel carrying lava from a vent down the face of the planet.
    sealed class LavaRun
    {
        public Vector2 start;
        public Vector2 direction;
        public float speed;
        public float lifetime;
        public float size;
        public float interval;
        public float nextDrop;
        public SpriteRenderer glow;    // the channel itself, glowing under the drops
    }

    SpriteRenderer atmosphere;
    SpriteRenderer heatHalo;
    SpriteRenderer crackGlow;
    SpriteRenderer crackGlowSecondary;
    SpriteRenderer calderaShoulder;
    SpriteRenderer calderaCore;
    Light2D calderaLight;

    ParticleSystem fireParticles;      // eruption plume + embers
    ParticleSystem smokeParticles;     // ash columns and bursts
    ParticleSystem rockParticles;      // flying lava rocks, the only ones with weight
    ParticleSystem flowParticles;      // lava running down the crust

    readonly List<Vent> vents = new List<Vent>();
    readonly List<LavaRun> runs = new List<LavaRun>();

    float calderaCoreSize;
    float calderaShoulderSize;
    float calderaLightBase;
    float lightBoost;
    float lightScale = 1f;
    float nextMajorEruption;

    // A fader can dim a sprite but not a light, so the launch drives this instead: the
    // caldera goes out with the rest of the stage rather than switching off under it.
    public void SetLightScale(float scale)
    {
        lightScale = Mathf.Clamp01(scale);
        if (calderaLight != null) calderaLight.intensity = calderaLightBase * lightScale;
    }

    // ── Build ───────────────────────────────────────────────────────────────

    protected override void Build()
    {
        if (transform.Find("MenuLavaAtmosphere") != null)
        {
            if (!BindExisting())
            {
                Debug.LogError("MenuLavaAmbience: serialized Lava presentation is incomplete. Run the Main Menu authoring command.", this);
                enabled = false;
            }
            return;
        }
        if (Application.isPlaying)
        {
            Debug.LogError("MenuLavaAmbience: serialized Lava presentation is missing. Run the Main Menu authoring command.", this);
            enabled = false;
            return;
        }

        MultiplyTint(BasaltTint);

        Vector3 caldera = ResolveCaldera();
        BuildGlowLayers(caldera);
        BuildLight(caldera);
        BuildVents(caldera);
        BuildLavaRuns(caldera);
        BuildParticles();

        nextMajorEruption = Time.time + Random.Range(4f, 7f);
    }

    bool BindExisting()
    {
        atmosphere = transform.Find("MenuLavaAtmosphere")?.GetComponent<SpriteRenderer>();
        heatHalo = transform.Find("MenuLavaHeatHalo")?.GetComponent<SpriteRenderer>();
        crackGlow = transform.Find("MenuLavaCracks")?.GetComponent<SpriteRenderer>();
        crackGlowSecondary = transform.Find("MenuLavaCracksCore")?.GetComponent<SpriteRenderer>();
        calderaShoulder = transform.Find("MenuLavaCalderaShoulder")?.GetComponent<SpriteRenderer>();
        calderaCore = transform.Find("MenuLavaCalderaCore")?.GetComponent<SpriteRenderer>();
        calderaLight = transform.Find("MenuLavaCalderaLight")?.GetComponent<Light2D>();
        if (calderaCore != null) calderaCoreSize = calderaCore.transform.localScale.x;
        if (calderaShoulder != null) calderaShoulderSize = calderaShoulder.transform.localScale.x;
        if (calderaLight != null) calderaLightBase = calderaLight.intensity;

        vents.Clear();
        runs.Clear();
        foreach (Transform child in transform)
        {
            if (child.name == "MenuLavaVentGlow")
            {
                SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
                Transform haze = null;
                for (int i = child.GetSiblingIndex() + 1; i < transform.childCount; i++)
                    if (transform.GetChild(i).name == "MenuLavaHeatHaze") { haze = transform.GetChild(i); break; }
                vents.Add(new Vent { position = child.localPosition, direction = ((Vector2)child.localPosition).normalized, radius = child.localScale.x, phase = vents.Count * 1.7f, nextEruption = Time.time + 2f + vents.Count, nextEmberPuff = Time.time + 0.5f, mouthGlow = renderer, haze = haze, hazeRenderer = haze != null ? haze.GetComponent<SpriteRenderer>() : null });
            }
            else if (child.name == "MenuLavaRun")
            {
                SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
                Vector2 direction = child.localRotation * Vector2.right;
                float travel = Mathf.Abs(child.localScale.x);
                float thickness = Mathf.Abs(child.localScale.y) / 2.2f;
                Vector2 start = (Vector2)child.localPosition - direction * (travel * 0.5f);
                const float lifetime = 2.7f;
                runs.Add(new LavaRun
                {
                    start = start,
                    direction = direction.normalized,
                    speed = travel / lifetime,
                    lifetime = lifetime,
                    size = thickness,
                    interval = 0.22f,
                    nextDrop = Time.time + runs.Count * 0.2f,
                    glow = renderer
                });
            }
            else if (child.name == "MenuLavaFire") fireParticles = child.GetComponent<ParticleSystem>();
            else if (child.name == "MenuLavaFlow") flowParticles = child.GetComponent<ParticleSystem>();
            else if (child.name == "MenuLavaSmoke") smokeParticles = child.GetComponent<ParticleSystem>();
            else if (child.name == "MenuLavaRocks") rockParticles = child.GetComponent<ParticleSystem>();
        }

        AdoptParticles(fireParticles, VfxSpriteFactory.ParticleMaterial,
            ParticleSystemRenderMode.Billboard, null);
        AdoptParticles(flowParticles, VfxSpriteFactory.ParticleMaterial,
            ParticleSystemRenderMode.Billboard, null);
        AdoptParticles(smokeParticles, VfxSpriteFactory.ParticleMaterial,
            ParticleSystemRenderMode.Billboard, null);
        AdoptParticles(rockParticles, VfxSpriteFactory.GeometricParticleMaterial,
            ParticleSystemRenderMode.Mesh, VfxSpriteFactory.TriangleMesh);

        nextMajorEruption = Time.time + Random.Range(4f, 7f);
        return atmosphere != null && heatHalo != null && crackGlow != null && calderaCore != null &&
               calderaLight != null && vents.Count == 3 && runs.Count == 3 &&
               fireParticles != null && flowParticles != null && smokeParticles != null && rockParticles != null;
    }

    // Particle modules are already serialized. Adoption restores only the references that
    // cannot survive serialization (the generated triangle mesh) and the exact rendering
    // invariants; it never multiplies a saved transform or particle size.
    static void AdoptParticles(ParticleSystem particles, Material material,
        ParticleSystemRenderMode renderMode, Mesh mesh)
    {
        if (particles == null) return;

        particles.transform.localScale = Vector3.one;
        ParticleSystem.MainModule main = particles.main;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        if (renderer == null) return;
        renderer.sharedMaterial = material;
        renderer.renderMode = renderMode;
        if (renderMode == ParticleSystemRenderMode.Mesh) renderer.mesh = mesh;
    }

    // The painted crater of the hero sprite, straight from the theme's own table, so a
    // different hero sprite lands its vents correctly with no edit here.
    Vector3 ResolveCaldera()
    {
        float span = LocalRadius * 2f;
        if (LavaPlanetAmbience.TryGetCaldera(SpriteName, out Vector3 normalized))
            return new Vector3(normalized.x * span, normalized.y * span, normalized.z * span);

        return new Vector3(0f, LocalRadius * 0.18f, LocalRadius * 0.12f);
    }

    void BuildGlowLayers(Vector3 caldera)
    {
        // The world's own air: a wide, faint shell that keeps the planet from ending on
        // a hard sprite edge. Behind everything, including the planet.
        atmosphere = CreateSprite("MenuLavaAtmosphere", VfxSpriteFactory.SoftSprite, -2,
            Vector2.zero, LocalRadius * 3.4f);
        atmosphere.color = new Color(MagmaDeep.r, MagmaDeep.g, MagmaDeep.b, 0.13f);

        // Tighter and hotter: the heat the rock itself is giving off.
        heatHalo = CreateSprite("MenuLavaHeatHalo", VfxSpriteFactory.SoftSprite, -1,
            Vector2.zero, LocalRadius * 2.5f);
        heatHalo.color = new Color(MagmaMid.r, MagmaMid.g, MagmaMid.b, 0.15f);

        // Two crack networks at different scales and rotations: one wide web over the
        // whole face, one bright cluster around the caldera. Layering them is what makes
        // the glow read as molten rock rather than as a decal.
        Vector2 crackCenter = new Vector2(caldera.x, caldera.y) * 0.5f;
        crackGlow = CreateSprite("MenuLavaCracks", LavaVfxAssets.CrackSprite, GlowSortingOffset,
            crackCenter, LocalRadius * 1.9f);
        crackGlow.color = new Color(MagmaMid.r, MagmaMid.g, MagmaMid.b, 0.55f);
        crackGlow.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        crackGlowSecondary = CreateSprite("MenuLavaCracksCore", LavaVfxAssets.CrackSprite,
            GlowSortingOffset, new Vector2(caldera.x, caldera.y), LocalRadius * 0.95f);
        // Molten orange, not the near-white of MagmaBright. Four layers meet over the
        // crater — this cluster, the vent mouth, the pool and its shoulder — and at that
        // hue they add up to a white lamp the width of half the planet.
        Color cluster = Color.Lerp(MagmaBright, MagmaMid, 0.55f);
        crackGlowSecondary.color = new Color(cluster.r, cluster.g, cluster.b, 0.30f);
        crackGlowSecondary.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        // The molten pool inside the painted crater, in two parts.
        //
        // One bright disc on its own is a lamp: it ends on a hard edge, sits at one
        // brightness across its whole width and pulls the eye off the ship, which is the
        // one thing on this screen allowed to be a light source. A tight core inside a
        // wide, dim, deep-red shoulder falls off instead of ending, and reads as a pool
        // of lava seen from above rather than as a hole cut in the planet.
        float crater = Mathf.Max(caldera.z, LocalRadius * 0.09f);

        calderaShoulderSize = crater * 3.0f;
        calderaShoulder = CreateSprite("MenuLavaCalderaShoulder", VfxSpriteFactory.SoftSprite,
            GlowSortingOffset, new Vector2(caldera.x, caldera.y), calderaShoulderSize);
        Color shoulder = Color.Lerp(MagmaDeep, MagmaMid, 0.55f);
        calderaShoulder.color = new Color(shoulder.r, shoulder.g, shoulder.b, 0.20f);

        calderaCoreSize = crater * 1.5f;
        calderaCore = CreateSprite("MenuLavaCalderaCore", VfxSpriteFactory.SoftSprite,
            GlowSortingOffset, new Vector2(caldera.x, caldera.y), calderaCoreSize);
        // Molten orange rather than the near-white of the hottest magma: white reads as a
        // light source, and the ship is the only thing in this frame allowed to be one.
        Color core = Color.Lerp(MagmaBright, MagmaMid, 0.62f);
        calderaCore.color = new Color(core.r, core.g, core.b, 0.34f);
    }

    // A real light, not another sprite: the caldera has to throw its pulse onto the ship
    // as it passes and onto the rock around it. The menu's own rig already owns the
    // frame, so this is deliberately a short-reach point light that only ever adds.
    void BuildLight(Vector3 caldera)
    {
        Vector3 lossy = transform.lossyScale;
        float worldScale = Mathf.Max(0.0001f, Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y)));

        GameObject go = new GameObject("MenuLavaCalderaLight") { layer = gameObject.layer };
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(caldera.x, caldera.y, 0f);
        // Light2D radii are world units; undoing the hero's scale here keeps the reach
        // written below meaningful whatever size the hero is authored at.
        go.transform.localScale = Vector3.one / worldScale;

        calderaLight = go.AddComponent<Light2D>();
        calderaLight.lightType = Light2D.LightType.Point;
        calderaLight.shadowsEnabled = false;
        calderaLight.color = Color.Lerp(MagmaMid, MagmaBright, 0.35f);

        float reach = LocalRadius * worldScale;
        calderaLight.pointLightInnerRadius = reach * 0.30f;
        calderaLight.pointLightOuterRadius = reach * 2.15f;
        calderaLightBase = 0.85f;
        calderaLight.intensity = calderaLightBase;

        SortingLayer[] layers = SortingLayer.layers;
        for (int i = 0; i < layers.Length; i++) calderaLight.AddTargetSortingLayer(layers[i].id);
    }

    // Three vents: the caldera itself, a flank vent and a small rim fissure. Three is
    // what lets a major eruption read as a chain reaction instead of one puff.
    void BuildVents(Vector3 caldera)
    {
        Vector2 calderaPos = new Vector2(caldera.x, caldera.y);
        Vector2 primaryDirection = calderaPos.sqrMagnitude > (LocalRadius * 0.12f) * (LocalRadius * 0.12f)
            ? calderaPos.normalized
            : Vector2.up;

        vents.Add(CreateVent(calderaPos, primaryDirection, Mathf.Max(caldera.z, LocalRadius * 0.10f)));

        float primaryAngle = Mathf.Atan2(primaryDirection.y, primaryDirection.x) * Mathf.Rad2Deg;
        AddFlankVent(primaryAngle + Random.Range(105f, 145f), 0.58f, 0.075f);
        AddFlankVent(primaryAngle - Random.Range(105f, 155f), 0.74f, 0.052f);
    }

    void AddFlankVent(float angleDegrees, float distanceRatio, float radiusRatio)
    {
        float radians = angleDegrees * Mathf.Deg2Rad;
        Vector2 direction = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        vents.Add(CreateVent(direction * (LocalRadius * distanceRatio), direction,
            LocalRadius * radiusRatio));
    }

    Vent CreateVent(Vector2 position, Vector2 direction, float radius)
    {
        Vent vent = new Vent
        {
            position = position,
            direction = direction.normalized,
            radius = radius,
            phase = Random.Range(0f, Mathf.PI * 2f),
            nextEruption = Time.time + Random.Range(0.8f, VentEruptionMax),
            nextEmberPuff = Time.time + Random.Range(0.3f, 1.4f)
        };

        // Tighter and warmer than it was. The caldera vent's mouth is as wide as the
        // crater, so at three times that radius in near-white it was the single brightest
        // thing on the screen — brighter than the ship, which is the only object in this
        // frame allowed to be a light source.
        vent.mouthGlow = CreateSprite("MenuLavaVentGlow", VfxSpriteFactory.SoftSprite,
            GlowSortingOffset, position, radius * 2.2f);
        Color mouth = Color.Lerp(MagmaBright, MagmaMid, 0.5f);
        vent.mouthGlow.color = new Color(mouth.r, mouth.g, mouth.b, 0.26f);

        // Heat distortion without a shader: a wide, nearly transparent curtain standing
        // over the vent, breathing on two axes at once. On a 2D renderer this costs one
        // transparent quad and reads as rising heat; a real refraction pass does not
        // belong in a mobile main menu.
        vent.haze = CreateSprite("MenuLavaHeatHaze", VfxSpriteFactory.SoftSprite,
            ParticleSortingOffset, position + vent.direction * (radius * 1.9f),
            radius * 4.2f).transform;
        vent.hazeRenderer = vent.haze.GetComponent<SpriteRenderer>();
        vent.hazeRenderer.color = new Color(1f, 0.66f, 0.34f, 0.07f);

        return vent;
    }

    // Lava leaving the caldera and running downhill across the face of the planet. Each
    // run is a glowing channel plus a stream of drops travelling along it.
    void BuildLavaRuns(Vector3 caldera)
    {
        Vector2 source = new Vector2(caldera.x, caldera.y);

        for (int i = 0; i < 3; i++)
        {
            Vector2 start = source + RandomOffset(caldera.z * 0.8f);
            Vector2 outward = start.sqrMagnitude > 0.0001f ? start.normalized : Vector2.right;
            // Mostly downhill, partly straight out from the crater: gravity wins, but the
            // crust still steers the flow.
            Vector2 direction = Vector2.Lerp(Vector2.down, outward, Random.Range(0.20f, 0.55f)).normalized;

            float travel = Mathf.Min(LocalRadius * Random.Range(0.85f, 1.25f),
                DistanceToRim(start, direction) * 0.94f);
            if (travel <= LocalRadius * 0.25f) continue;

            float lifetime = Random.Range(2.2f, 3.2f);
            LavaRun run = new LavaRun
            {
                start = start,
                direction = direction,
                lifetime = lifetime,
                speed = travel / lifetime,
                size = LocalRadius * Random.Range(0.09f, 0.14f),
                interval = Random.Range(0.16f, 0.26f),
                nextDrop = Time.time + Random.Range(0f, 0.4f)
            };

            // The channel the drops run in, so the flow still reads between drops.
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            run.glow = CreateSprite("MenuLavaRun", VfxSpriteFactory.SoftSprite,
                GlowSortingOffset, start + direction * (travel * 0.5f), travel);
            run.glow.color = new Color(MagmaMid.r, MagmaMid.g, MagmaMid.b, 0.26f);
            run.glow.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            run.glow.transform.localScale = new Vector3(travel, run.size * 2.2f, 1f);

            runs.Add(run);
        }
    }

    void BuildParticles()
    {
        fireParticles = CreateDecorativeParticles("MenuLavaFire", 220, 3.2f);
        flowParticles = CreateDecorativeParticles("MenuLavaFlow", 60, 3.4f);

        smokeParticles = CreateDecorativeParticles("MenuLavaSmoke", 90, 6f);
        // Smoke is the one layer that has to grow instead of shrink as it dies.
        ParticleSystem.SizeOverLifetimeModule smokeSize = smokeParticles.sizeOverLifetime;
        smokeSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.55f),
            new Keyframe(0.5f, 1.25f),
            new Keyframe(1f, 1.75f)));

        // Debris: real chunks of crust, thrown clear and pulled back down. The only
        // system here with weight, and the reason a big eruption reads as violent.
        rockParticles = CreateDecorativeParticles("MenuLavaRocks", 40, 3f,
            VfxSpriteFactory.GeometricParticleMaterial);
        ParticleSystem.MainModule rockMain = rockParticles.main;
        rockMain.gravityModifier = 0.55f;
        rockMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystemRenderer rockRenderer = rockParticles.GetComponent<ParticleSystemRenderer>();
        rockRenderer.renderMode = ParticleSystemRenderMode.Mesh;
        rockRenderer.mesh = VfxSpriteFactory.TriangleMesh;
    }

    // ── Per frame ───────────────────────────────────────────────────────────

    protected override void Animate(float time, bool visible)
    {
        float slow = (Mathf.Sin(time * 0.9f + Phase) + 1f) * 0.5f;
        float pulse = (Mathf.Sin(time * 2.3f + Phase) + 1f) * 0.5f;
        lightBoost = Mathf.Max(0f, lightBoost - Time.deltaTime * 1.15f);

        if (atmosphere != null)
        {
            Color color = atmosphere.color;
            color.a = Mathf.Lerp(0.10f, 0.17f, slow) + lightBoost * 0.06f;
            atmosphere.color = color;
        }

        if (heatHalo != null)
        {
            Color color = heatHalo.color;
            color.a = Mathf.Lerp(0.10f, 0.18f, slow) + lightBoost * 0.08f;
            heatHalo.color = color;
        }

        if (crackGlow != null)
        {
            // Perlin over a sine: the crust brightens in patches, never in one beat.
            float flicker = Mathf.PerlinNoise(time * 1.7f, Phase) * 0.32f;
            Color color = crackGlow.color;
            color.a = Mathf.Clamp01(Mathf.Lerp(0.38f, 0.80f, slow * 0.6f + flicker) + lightBoost * 0.22f);
            crackGlow.color = color;
        }

        if (crackGlowSecondary != null)
        {
            float flicker = Mathf.PerlinNoise(Phase, time * 2.4f) * 0.30f;
            Color color = crackGlowSecondary.color;
            // Deliberately dimmer than the wide web: this cluster sits on top of the pool,
            // and the two of them at full strength is what made the crater read as a lamp.
            color.a = Mathf.Clamp01(Mathf.Lerp(0.18f, 0.38f, pulse * 0.5f + flicker) + lightBoost * 0.26f);
            crackGlowSecondary.color = color;
        }

        // The shoulder breathes on the slow channel and the core on the fast one, so the
        // pool never brightens as one flat shape.
        if (calderaShoulder != null)
        {
            Color color = calderaShoulder.color;
            color.a = Mathf.Clamp01(Mathf.Lerp(0.15f, 0.26f, slow) + lightBoost * 0.16f);
            calderaShoulder.color = color;
            float scale = calderaShoulderSize * (Mathf.Lerp(0.96f, 1.05f, slow) + lightBoost * 0.14f);
            calderaShoulder.transform.localScale = Vector3.one * scale;
        }

        if (calderaCore != null)
        {
            Color color = calderaCore.color;
            color.a = Mathf.Clamp01(Mathf.Lerp(0.24f, 0.40f, pulse) + lightBoost * 0.22f);
            calderaCore.color = color;
            float scale = calderaCoreSize * (Mathf.Lerp(0.92f, 1.09f, pulse) + lightBoost * 0.20f);
            calderaCore.transform.localScale = Vector3.one * scale;
        }

        // The pulse the player feels rather than sees: the light under the crust
        // breathing, and flaring with every eruption.
        if (calderaLight != null)
            calderaLight.intensity =
                (calderaLightBase * Mathf.Lerp(0.82f, 1.12f, pulse) + lightBoost * 1.35f) * lightScale;

        UpdateVents(time, visible);
        UpdateLavaRuns(time, visible, pulse);

        if (!visible) return;

        if (time >= nextMajorEruption)
        {
            nextMajorEruption = time + Random.Range(MajorEruptionMin, MajorEruptionMax);
            StartCoroutine(MajorEruption());
        }
    }

    void UpdateVents(float time, bool visible)
    {
        for (int i = 0; i < vents.Count; i++)
        {
            Vent vent = vents[i];

            vent.glowBoost = Mathf.Max(0f, vent.glowBoost - Time.deltaTime * 1.2f);
            vent.hazeBoost = Mathf.Max(0f, vent.hazeBoost - Time.deltaTime * 0.7f);

            if (vent.mouthGlow != null)
            {
                float glow = Mathf.Lerp(0.18f, 0.32f, (Mathf.Sin(time * 2.8f + vent.phase) + 1f) * 0.5f);
                Color color = vent.mouthGlow.color;
                color.a = Mathf.Clamp01(glow + vent.glowBoost * 0.30f);
                vent.mouthGlow.color = color;
            }

            if (vent.haze != null)
            {
                float wobble = Mathf.Sin(time * 3.4f + vent.phase);
                float drift = Mathf.Sin(time * 1.9f + vent.phase * 1.7f);
                float baseSize = vent.radius * 4.2f;
                vent.haze.localScale = new Vector3(
                    baseSize * (1f + wobble * 0.10f + drift * 0.05f),
                    baseSize * (1.35f - wobble * 0.12f + vent.hazeBoost * 0.45f),
                    1f);
                Color color = vent.hazeRenderer.color;
                color.a = 0.05f + Mathf.Abs(wobble) * 0.025f + vent.hazeBoost * 0.09f;
                vent.hazeRenderer.color = color;
            }

            if (!visible) continue;

            if (time >= vent.nextEmberPuff)
            {
                vent.nextEmberPuff = time + Random.Range(0.5f, 1.5f);
                EmitEmbers(vent, Random.Range(3, 6), 0.75f);
            }

            if (time >= vent.nextEruption)
            {
                vent.nextEruption = time + Random.Range(VentEruptionMin, VentEruptionMax);
                StartCoroutine(Erupt(vent, 1f));
            }
        }
    }

    void UpdateLavaRuns(float time, bool visible, float pulse)
    {
        for (int i = 0; i < runs.Count; i++)
        {
            LavaRun run = runs[i];

            if (run.glow != null)
            {
                Color color = run.glow.color;
                color.a = Mathf.Lerp(0.18f, 0.34f, pulse) + lightBoost * 0.18f;
                run.glow.color = color;
            }

            if (!visible || flowParticles == null || time < run.nextDrop) continue;
            run.nextDrop = time + run.interval;

            Emit(flowParticles,
                run.start + RandomOffset(LocalRadius * 0.035f),
                run.direction * run.speed * Random.Range(0.85f, 1.2f),
                run.lifetime,
                run.size * Random.Range(0.75f, 1.3f),
                Translucent(Color.Lerp(MagmaBright, MagmaMid, Random.value), 0.8f));
        }
    }

    // ── Eruptions ───────────────────────────────────────────────────────────

    // One vent letting go. energy scales the whole event, so the same routine covers
    // the idle rhythm and the major event without a second code path.
    IEnumerator Erupt(Vent vent, float energy)
    {
        vent.glowBoost = Mathf.Min(1f, 0.8f * energy);
        vent.hazeBoost = Mathf.Min(1f, 0.9f * energy);
        lightBoost = Mathf.Max(lightBoost, 0.55f * energy);

        EmitFire(vent, Mathf.RoundToInt(9f * energy), energy);
        EmitEmbers(vent, Mathf.RoundToInt(6f * energy), energy);
        EmitRocks(vent, Mathf.RoundToInt(3f * energy), energy);

        yield return new WaitForSeconds(0.09f);
        EmitFire(vent, Mathf.RoundToInt(7f * energy), energy * 0.9f);
        EmitSmoke(vent, Mathf.RoundToInt(4f * energy), energy);

        yield return new WaitForSeconds(0.13f);
        EmitEmbers(vent, Mathf.RoundToInt(5f * energy), energy);
        EmitRocks(vent, Mathf.RoundToInt(2f * energy), energy * 0.8f);
        EmitSmoke(vent, Mathf.RoundToInt(4f * energy), energy);

        yield return new WaitForSeconds(0.2f);
        EmitSmoke(vent, Mathf.RoundToInt(3f * energy), energy * 0.85f);
    }

    // The whole mountain going at once, vent by vent. The caldera leads, the flanks
    // answer, and the light flares over the top of all of it.
    IEnumerator MajorEruption()
    {
        lightBoost = 1.15f;

        for (int i = 0; i < vents.Count; i++)
        {
            StartCoroutine(Erupt(vents[i], i == 0 ? 2.5f : 1.8f));
            // Pushing each vent's own next eruption out keeps the seconds after a major
            // event quiet, which is what makes the event read as one.
            vents[i].nextEruption = Time.time + Random.Range(3.5f, 6f);
            yield return new WaitForSeconds(Random.Range(0.12f, 0.26f));
        }
    }

    // Flame is drawn as a stack of soft blobs, and opaque blobs do not stack: they
    // saturate. Thirty of them over a vent, each a seventh of the planet across and fully
    // opaque, added up to a solid pale plate sitting on the crater — the plume read as a
    // sheet of paper rather than as fire. Smaller and see-through is what makes it read as
    // one plume made of many pieces.
    void EmitFire(Vent vent, int count, float energy)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Spread(vent.direction, 24f);
            Emit(fireParticles,
                vent.position + direction * (vent.radius * 0.4f) + RandomOffset(vent.radius * 0.35f),
                direction * (LocalRadius * Random.Range(0.9f, 1.7f) * energy),
                Random.Range(0.5f, 0.95f),
                LocalRadius * Random.Range(0.075f, 0.145f) * Mathf.Lerp(1f, 1.3f, energy - 1f),
                Translucent(Color.Lerp(MagmaBright, MagmaDeep, Random.value * 0.7f),
                    Random.Range(0.45f, 0.72f)));
        }
    }

    static Color Translucent(Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, alpha);
    }

    void EmitEmbers(Vent vent, int count, float energy)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Spread(vent.direction, 48f);
            Emit(fireParticles,
                vent.position + RandomOffset(vent.radius * 0.55f),
                direction * (LocalRadius * Random.Range(1.2f, 2.4f) * energy),
                Random.Range(0.9f, 1.9f),
                LocalRadius * Random.Range(0.04f, 0.08f),
                Translucent(Color.Lerp(MagmaBright, MagmaMid, Random.value), 0.85f));
        }
    }

    void EmitSmoke(Vent vent, int count, float energy)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Spread(vent.direction, 30f);
            Emit(smokeParticles,
                vent.position + direction * (vent.radius * 0.9f) + RandomOffset(vent.radius * 0.7f),
                direction * (LocalRadius * Random.Range(0.30f, 0.60f) * energy),
                Random.Range(2.2f, 4.2f),
                // The energy term is capped: smoke also grows to nearly twice this over
                // its life, and a major eruption was otherwise throwing ash clouds wider
                // than the planet they came from.
                LocalRadius * Random.Range(0.18f, 0.32f) * Mathf.Lerp(1f, 1.5f, Mathf.Clamp01(energy - 1f)),
                new Color(AshTone.r, AshTone.g, AshTone.b, Random.Range(0.30f, 0.52f)));
        }
    }

    void EmitRocks(Vent vent, int count, float energy)
    {
        for (int i = 0; i < count; i++)
        {
            Vector2 direction = Spread(vent.direction, 38f);
            Emit(rockParticles,
                vent.position + direction * (vent.radius * 0.6f) + RandomOffset(vent.radius * 0.5f),
                direction * (LocalRadius * Random.Range(1.3f, 2.2f) * energy),
                Random.Range(1.4f, 2.4f),
                LocalRadius * Random.Range(0.05f, 0.10f),
                Color.Lerp(RockTone, MagmaDeep, Random.value * 0.6f));
        }
    }

    // The launch fades the stage out and freezes the components that animate alpha.
    // Particles cannot be faded that way, so they are cleared the moment this stops:
    // no embers left hanging in the frame the game starts on. The light keeps whatever
    // the launch last set it to and is dimmed from there by SetLightScale.
    void OnDisable()
    {
        ClearParticles(fireParticles);
        ClearParticles(smokeParticles);
        ClearParticles(rockParticles);
        ClearParticles(flowParticles);
    }

    static void ClearParticles(ParticleSystem particles)
    {
        if (particles == null) return;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
