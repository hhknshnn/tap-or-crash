# Blender MCP planet generator — reference/reusable pipeline
#
# This is NOT run by Unity or Blender automatically. It's the reference copy of the
# procedural low-poly planet generator used to build Assets/Sprites/NaturalPlanets/
# and Assets/Sprites/IcePlanets/. Paste (or adapt) this into a `execute_blender_code`
# call against a running Blender MCP session to build the next level pack.
#
# IMPORTANT: each `execute_blender_code` call runs in a FRESH Python namespace — only
# bpy.data / the Blender scene state persists between calls, not Python variables or
# function defs. So every real script must be one big self-contained call: paste this
# whole helper section PLUS your theme functions PLUS the render loop in one call.
#
# ── Pipeline overview ────────────────────────────────────────────────────────────
# 1. Build 10 theme functions (see NEXT LEVEL TEMPLATE at the bottom) that each call
#    build_planet(...) and land in a transparent 1024x1024 PNG under
#    Assets/Sprites/<LevelName>Planets/<LevelName>_XX.png
# 2. Generate 20 GUIDs (10 sprite + 10 prefab) via PowerShell:
#      for ($i=1; $i -le 10; $i++) { [guid]::NewGuid().ToString('N') }
# 3. Author `<Name>_XX.png.meta` for each PNG — copy the TextureImporter block from
#    an existing Natural_XX.png.meta (spriteMode: 1, spritePixelsToUnits: 100,
#    alphaIsTransparency: 1), swap in the new guid.
# 4. Author `<Name>_XX.prefab` + `.prefab.meta` — copy the structure from
#    Assets/Sprites/NaturalPlanets/Natural_01.prefab (GameObject + Transform +
#    SpriteRenderer + CircleCollider2D, tag "Planet"). Keep the SAME internal
#    fileIDs (they're only unique per-file, not project-wide) — just swap the
#    sprite guid (m_Sprite) and give the prefab file its own guid.
#      - m_LocalScale: {x: 0.3, y: 0.3, z: 0.3}  (cosmetic default; PlanetSpawner
#        always overwrites localScale at spawn time based on score)
#      - m_Size: {x: 10.24, y: 10.24}  (1024px / spritePixelsToUnits 100)
#      - CircleCollider2D m_Radius: 3.85  (matches the fixed ortho_scale=3.18 framing
#        below — ground fills ~88% of frame; recompute only if you change ortho_scale
#        or ground_radius)
#      - Material: {fileID: 2100000, guid: a97c105638bdf8b4a8650670310a4cd3, type: 2}
#        (Unity's built-in Sprites-Default material, reused project-wide)
# 5. Add a new `PlanetLevel` entry to PlanetSpawner's `levels` array in
#    Assets/Scenes/SampleScene.unity (levelName + 10 prefab refs, same GUID/fileID
#    pattern as the existing Natural/Ice entries).
# 6. PlanetSpawner.cs needs NO further code changes — the level
#    system is fully data-driven off `levels[]` (see PlanetSpawner.LevelIndexForScore
#    / LevelNames). Adding a level is purely a content + scene-wiring change.
#
# ── Design lessons from Level 1 (Natural) & Level 2 (Ice) ──────────────────────
# - Give every level pack a STRONG, immediately readable color/material identity.
#   Ice failed on the first pass because it was too grey/flat — glacier blue +
#   jagged mountain terrain (see displace_terrain) fixed it. Lava should lean into
#   near-black rock + bright emissive orange/red cracks; Desert into warm sand +
#   sharp shadow; Mushroom into saturated violet/teal + glow; etc. Don't reuse a
#   previous level's palette.
# - `displace_terrain()` (radial Perlin noise pushed along each vertex's own
#   direction from the sphere center) is the single highest-impact trick for making
#   a planet read as a specific *place* rather than a smooth ball with props glued
#   on. Strength ~0.03-0.06 = gentle rolling ground (Natural), ~0.10-0.22 = jagged
#   mountains/crags (Ice, and probably Lava/Crystal/Mechanical too).
# - Keep camera framing IDENTICAL across all packs (ortho_scale=3.18, ground_radius
#   1.4, same camera/light rig) so all planets end up the same in-game size without
#   needing per-planet collider tuning.
# - Natural planets specifically must stay green/nature-coded (trees, birds, water)
#   — don't let sub-themes drift into territory that belongs to a dedicated later
#   level (ice/lava/desert/mushroom already got promoted out of Natural once).
#
# ── Roadmap (score 1-100, 10 planets per level) ─────────────────────────────────
#   Level 1  Natural   [DONE]
#   Level 2  Ice       [DONE]
#   Level 3  Lava      [DONE]
#   Level 4  Desert     <- next
#   Level 5  Mushroom
#   Level 6  Sakura
#   Level 7  Crystal
#   Level 8  Cloud
#   Level 9  Mechanical
#   Level 10 Alien
# Build ONE level at a time and get user review before starting the next
# (explicit user preference — don't batch multiple levels unprompted).

import bpy, math, random, os, functools
import mathutils
from mathutils import Vector, Quaternion

scene = bpy.context.scene
GEN = "PlanetGen"
gen_coll = bpy.data.collections.get(GEN)
if gen_coll is None:
    gen_coll = bpy.data.collections.new(GEN)
    scene.collection.children.link(gen_coll)

def clear_gen():
    for obj in list(gen_coll.objects):
        bpy.data.objects.remove(obj, do_unlink=True)

def link_to(obj, coll=gen_coll):
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)

def shade_flat(obj):
    for p in obj.data.polygons:
        p.use_smooth = False

def mat(name, color, rough=0.85, emission_strength=0.0):
    name2 = name; i = 1
    while name2 in bpy.data.materials:
        i += 1; name2 = f"{name}_{i}"
    m = bpy.data.materials.new(name2)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (color[0], color[1], color[2], 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    if emission_strength > 0:
        try:
            bsdf.inputs["Emission Color"].default_value = (color[0], color[1], color[2], 1.0)
            bsdf.inputs["Emission Strength"].default_value = emission_strength
        except KeyError:
            pass
    return m

def displace_terrain(obj, strength, freq, seed_offset):
    """Radial Perlin displacement — the 'is this a specific place' trick. strength
    is in Blender world units relative to ground_radius=1.4 (0.03-0.06 gentle,
    0.10-0.22 dramatic/mountainous)."""
    mesh = obj.data
    off = Vector((seed_offset * 13.7, seed_offset * 7.3, seed_offset * 5.1))
    for v in mesh.vertices:
        p = v.co * freq + off
        n = mathutils.noise.noise(p)
        offset = (n - 0.5) * 2.0 * strength
        direction = v.co.normalized() if v.co.length > 0 else Vector((0, 0, 1))
        v.co = v.co + direction * offset
    mesh.update()

def add_anchor(loc, normal, spin=0.0):
    empty = bpy.data.objects.new("Anchor", None)
    link_to(empty)
    empty.location = loc
    quat = normal.to_track_quat('Z', 'Y')
    if spin:
        quat = quat @ Quaternion((0, 0, 1), spin)
    empty.rotation_mode = 'QUATERNION'
    empty.rotation_quaternion = quat
    return empty

def prim_cone(name, r1, r2, depth, z, material, verts=7):
    bpy.ops.mesh.primitive_cone_add(radius1=r1, radius2=r2, depth=depth, location=(0, 0, z), vertices=verts)
    obj = bpy.context.active_object; obj.name = name
    obj.data.materials.append(material); shade_flat(obj); link_to(obj)
    return obj

def prim_ico(name, radius, z, material, subdiv=1, scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_ico_sphere_add(radius=radius, location=(0, 0, z), subdivisions=subdiv)
    obj = bpy.context.active_object; obj.name = name; obj.scale = scale
    obj.data.materials.append(material); shade_flat(obj); link_to(obj)
    return obj

def prim_disc(name, radius, depth, z, material, verts=24):
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=depth, location=(0, 0, z), vertices=verts)
    obj = bpy.context.active_object; obj.name = name
    obj.data.materials.append(material); shade_flat(obj); link_to(obj)
    return obj

def fib_sphere_points(n):
    pts = []; ga = math.pi * (3 - math.sqrt(5))
    for i in range(n):
        y = 1 - (i / (n - 1)) * 2 if n > 1 else 0
        r = math.sqrt(max(0, 1 - y * y))
        theta = ga * i
        pts.append(Vector((math.cos(theta) * r, y, math.sin(theta) * r)).normalized())
    return pts

# ── Reusable decoration primitives (mix and match per theme) ───────────────────

def add_rock(loc, normal, rock_mat, scale=1.0):
    a = add_anchor(loc, normal, random.uniform(0, math.tau))
    s = scale * random.uniform(0.06, 0.12)
    r = prim_ico("Rock", s, s * 0.75, rock_mat, subdiv=0,
                 scale=(random.uniform(0.8, 1.2), random.uniform(0.8, 1.2), random.uniform(0.6, 1.0)))
    r.rotation_euler = (random.uniform(0, math.tau), random.uniform(0, math.tau), random.uniform(0, math.tau))
    r.parent = a
    return a

def add_glow_dot(loc, normal, glow_mat, scale=1.0, height=0.05):
    a = add_anchor(loc, normal, 0)
    d = prim_ico("Glow", 0.02 * scale, height * scale, glow_mat, subdiv=1); d.parent = a
    return a

def add_pond(loc, normal, radius, water_mat, shore_mat=None):
    a = add_anchor(loc, normal, 0)
    if shore_mat:
        shore = prim_disc("Shore", radius * 1.28, 0.006, 0.003, shore_mat, 20); shore.parent = a
    water = prim_disc("Water", radius, 0.01, 0.012, water_mat, 20); water.parent = a
    return a

def add_crystal_cluster(loc, normal, crystal_mat, scale=1.0, count=None, min_h=0.16, max_h=0.30):
    a = add_anchor(loc, normal, random.uniform(0, math.tau))
    n = count if count is not None else random.choice([1, 2, 3])
    for i in range(n):
        h = scale * random.uniform(min_h, max_h)
        r = scale * random.uniform(0.025, 0.045)
        ang = random.uniform(0, math.tau)
        lean = random.uniform(0, 14)
        offset_r = scale * 0.04 * i
        x = math.cos(ang) * offset_r; y = math.sin(ang) * offset_r
        c = prim_cone(f"Crystal{i}", r, 0.0, h, h / 2, crystal_mat, 5)
        c.location = (x, y, h / 2)
        c.rotation_euler = (math.radians(lean), math.radians(lean * 0.6), ang)
        c.parent = a
    return a

def add_pine(loc, normal, trunk_mat, foliage_mat, snow_mat=None, scale=1.0):
    a = add_anchor(loc, normal, random.uniform(0, math.tau))
    th = 0.11 * scale
    t = prim_cone("Trunk", 0.018 * scale, 0.018 * scale, th, th / 2, trunk_mat, 6); t.parent = a
    z = th; tiers = random.choice([2, 3]); r0 = 0.11 * scale
    for i in range(tiers):
        h = 0.16 * scale * (1 - i * 0.15); r = r0 * (1 - i * 0.28)
        c = prim_cone(f"Foliage{i}", r, 0.0, h, z + h / 2, foliage_mat, 7); c.parent = a
        z += h * 0.62
    if snow_mat:
        s = prim_cone("SnowCap", r0 * 0.22, 0.0, 0.05 * scale, z + 0.02, snow_mat, 6); s.parent = a
    return a

def add_bird(loc, normal, bird_mat, scale=1.0):
    a = add_anchor(loc, normal, random.uniform(0, math.tau))
    hover = 0.13 * scale
    body = prim_ico("Body", 0.014 * scale, hover, bird_mat, subdiv=0, scale=(1.5, 0.8, 0.8)); body.parent = a
    for sign in (-1, 1):
        wing = prim_ico(f"Wing{sign}", 0.032 * scale, hover, bird_mat, subdiv=0, scale=(1.0, 3.0, 0.12))
        wing.rotation_euler = (0, 0, math.radians(28 * sign))
        wing.location = (sign * 0.018 * scale, 0, hover)
        wing.parent = a
    return a

# ── Camera / lights / render settings (keep identical across ALL levels) ───────

def ensure_camera_and_lights():
    cam_data = bpy.data.cameras.get("PlanetCam") or bpy.data.cameras.new("PlanetCam")
    cam_data.type = 'ORTHO'
    cam_data.ortho_scale = 3.18
    cam_obj = bpy.data.objects.get("PlanetCamObj")
    if cam_obj is None:
        cam_obj = bpy.data.objects.new("PlanetCamObj", cam_data)
        scene.collection.objects.link(cam_obj)
    cam_obj.location = Vector((0, -4.6, 5.2))
    direction = Vector((0, 0, 0)) - cam_obj.location
    cam_obj.rotation_mode = 'QUATERNION'
    cam_obj.rotation_quaternion = direction.to_track_quat('-Z', 'Y')
    scene.camera = cam_obj
    key_data = bpy.data.lights.get("KeySun") or bpy.data.lights.new("KeySun", type='SUN')
    key_data.energy = 3.2
    key_obj = bpy.data.objects.get("KeySunObj")
    if key_obj is None:
        key_obj = bpy.data.objects.new("KeySunObj", key_data); scene.collection.objects.link(key_obj)
    key_obj.rotation_euler = (math.radians(50), 0, math.radians(-40))
    fill_data = bpy.data.lights.get("FillSun") or bpy.data.lights.new("FillSun", type='SUN')
    fill_data.energy = 1.1
    fill_obj = bpy.data.objects.get("FillSunObj")
    if fill_obj is None:
        fill_obj = bpy.data.objects.new("FillSunObj", fill_data); scene.collection.objects.link(fill_obj)
    fill_obj.rotation_euler = (math.radians(115), 0, math.radians(145))

for name in ["Cube", "Light", "Camera"]:
    o = bpy.data.objects.get(name)
    if o: bpy.data.objects.remove(o, do_unlink=True)

ensure_camera_and_lights()
scene.render.engine = 'BLENDER_EEVEE'
scene.render.film_transparent = True
scene.render.resolution_x = 1024
scene.render.resolution_y = 1024
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGBA'
try:
    scene.eevee.taa_render_samples = 32
except Exception as e:
    print("sample setting skipped", e)

def build_planet(ground_color, weighted_decor, pond=None, ground_radius=1.4, n_points=46, seed=0,
                  extra_after=None, terrain_strength=0.05, terrain_freq=2.0, ground_rough=0.85, subdiv=4):
    random.seed(seed)
    clear_gen()
    bpy.ops.mesh.primitive_ico_sphere_add(radius=ground_radius, subdivisions=subdiv, location=(0, 0, 0))
    ground = bpy.context.active_object; ground.name = "Ground"
    if terrain_strength > 0:
        displace_terrain(ground, terrain_strength, terrain_freq, seed)
    gmat = mat("GroundMat", ground_color, rough=ground_rough)
    ground.data.materials.append(gmat); shade_flat(ground); link_to(ground)
    pond_dir = None; exclude_dot = 0.0
    if pond:
        pond_dir = pond["dir"].normalized()
        exclude_dot = pond.get("exclude_dot", 0.86)
        add_pond(pond_dir * ground_radius, pond_dir, pond["radius"], pond["water_mat"], pond.get("shore_mat"))
    pts = fib_sphere_points(n_points)
    total_w = sum(w for w, _ in weighted_decor)
    for p in pts:
        if pond_dir is not None and p.dot(pond_dir) > exclude_dot:
            continue
        r = random.random() * total_w; cum = 0.0
        for w, fn in weighted_decor:
            cum += w
            if r <= cum:
                if fn is not None: fn(p * ground_radius, p)
                break
    if extra_after: extra_after(ground_radius)

def render_planet(filepath):
    scene.render.filepath = filepath
    bpy.ops.render.render(write_still=True)

# ── NEXT LEVEL TEMPLATE ─────────────────────────────────────────────────────────
# out_dir = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new\Assets\Sprites\LavaPlanets"
# os.makedirs(out_dir, exist_ok=True)
# results = []
# def run_theme(idx, fn):
#     try:
#         fn()
#         render_planet(os.path.join(out_dir, f"Lava_{idx:02d}.png"))
#         results.append(f"Lava_{idx:02d} OK")
#     except Exception as e:
#         results.append(f"Lava_{idx:02d} FAILED: {e}")
#
# def l01():
#     rock_m = mat("L1_Rock", (0.08, 0.07, 0.07))
#     glow_m = mat("L1_Glow", (1.0, 0.35, 0.05), emission_strength=4.0)
#     decor = [(0.6, functools.partial(add_rock, rock_mat=rock_m, scale=1.5)),
#              (0.4, functools.partial(add_glow_dot, glow_mat=glow_m, scale=1.2))]
#     build_planet((0.10, 0.08, 0.08), decor, n_points=40, seed=301,
#                   terrain_strength=0.16, terrain_freq=2.0, ground_rough=0.4)
# run_theme(1, l01)
# ... l02..l10 ...
# print("\n".join(results))
