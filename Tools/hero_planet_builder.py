# Hero Planet builder — the Main-Menu "universe" planet for Tap or Crash.
#
# Run inside a live Blender session (Blender MCP):
#     exec(open(r"...\Tools\hero_planet_builder.py").read())
#
# Design contract (see Assets/Sprites/HeroPlanet/HeroPlanet.jpeg concept art):
#   * ONE mathematically perfect ico-sphere ground (no displacement, no cliffs,
#     no overhangs) so the silhouette stays a clean circle from every angle.
#   * Biome identity comes from FLAT COLOR REGIONS on that sphere plus chunky
#     low-poly props — the same language as the gameplay planet packs built by
#     Tools/blender_planet_generator.py.
#   * Composition is authored in concept-art screen space: place(u, v) takes
#     normalised image coordinates (u right, v up, unit disc = the visible
#     hemisphere) and returns the matching direction on the sphere. That is how
#     the layout stays recognisable against the reference.
#   * Everything is joined into a SINGLE mesh and baked onto ONE palette texture
#     -> 2 material slots (opaque + emissive) sharing one 256x256 PNG. Mobile:
#     2 draw calls for the whole hero asset. No ring, no satellites, no orbit.

import bpy
import bmesh
import math
import os
import random
from mathutils import Vector, noise

OUT_DIR = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new\Tools\HeroPlanet"
R_GROUND = 1.4
CLOUD_SCALE = 0.80   # polish pass: clouds trimmed ~20%
CRYSTAL_SCALE = 0.85  # polish pass: violet spires shortened ~15%
RUIN_SCALE = 1.12     # polish pass: mechanical ruins up ~12%
GRID = 16          # palette atlas is GRID x GRID cells
TEX_SIZE = 256     # 16 px per cell
COLL_NAME = "HeroPlanetGen"

random.seed(20260730)

# ── scene plumbing ────────────────────────────────────────────────────────────

scene = bpy.context.scene


def get_collection():
    coll = bpy.data.collections.get(COLL_NAME)
    if coll is None:
        coll = bpy.data.collections.new(COLL_NAME)
        scene.collection.children.link(coll)
    return coll


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)


def link_only(obj):
    coll = get_collection()
    for c in list(obj.users_collection):
        c.objects.unlink(obj)
    coll.objects.link(obj)


def shade_flat(obj):
    for p in obj.data.polygons:
        p.use_smooth = False


# ── concept-art coordinate frame ──────────────────────────────────────────────
# HERO_AXIS points at the viewer of the concept art. The composition lives on
# the hemisphere around it, tilted slightly up so the lake / tree / ruins read
# from a three-quarter menu angle instead of dead-on.

HERO_AXIS = Vector((0.0, -1.0, 0.38)).normalized()
FRAME_R = Vector((0.0, 0.0, 1.0)).cross(HERO_AXIS).normalized()
FRAME_U = HERO_AXIS.cross(FRAME_R).normalized()


def place(u, v):
    """Concept-art screen coords (unit disc) -> unit direction on the sphere."""
    r2 = u * u + v * v
    if r2 > 0.9975:
        s = math.sqrt(0.9975 / r2)
        u *= s
        v *= s
        r2 = 0.9975
    z = math.sqrt(1.0 - r2)
    return (FRAME_R * u + FRAME_U * v + HERO_AXIS * z).normalized()


def place_far(u, v):
    """Same, but on the hemisphere behind the hero face (the Cloud world)."""
    r2 = u * u + v * v
    if r2 > 0.9975:
        s = math.sqrt(0.9975 / r2)
        u *= s
        v *= s
        r2 = 0.9975
    z = math.sqrt(1.0 - r2)
    return (FRAME_R * u + FRAME_U * v - HERO_AXIS * z).normalized()


def tangent_frame(d):
    """Two unit tangents for a surface direction."""
    helper = Vector((0.0, 0.0, 1.0))
    if abs(d.dot(helper)) > 0.95:
        helper = Vector((1.0, 0.0, 0.0))
    t1 = d.cross(helper).normalized()
    return t1, d.cross(t1).normalized()


# ── palette registry ──────────────────────────────────────────────────────────
# Colours are authored once here and baked into a texture atlas at the end, so
# the whole planet ships as a single material even though it uses ~70 tints.

PALETTE = []        # index -> (r, g, b) linear
PAL_INDEX = {}      # tag -> palette index
EMISSIVE = set()    # tags that go to the glow material slot


def reg(tag, color, emissive=False):
    if tag not in PAL_INDEX:
        PAL_INDEX[tag] = len(PALETTE)
        PALETTE.append(tuple(color))
        if emissive:
            EMISSIVE.add(tag)
    return tag


def get_mat(tag):
    """Build-time preview material; one per tag, name-tagged for the bake."""
    name = "HP_" + tag
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    col = PALETTE[PAL_INDEX[tag]]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (col[0], col[1], col[2], 1.0)
    bsdf.inputs["Roughness"].default_value = 0.82
    if tag in EMISSIVE:
        try:
            bsdf.inputs["Emission Color"].default_value = (col[0], col[1], col[2], 1.0)
            bsdf.inputs["Emission Strength"].default_value = 2.5
        except KeyError:
            pass
    return m


def tints(tag, base, count=3, spread=0.12):
    """Register light/mid/dark variants of a ground colour (the mosaic look)."""
    out = []
    for i in range(count):
        f = 1.0 + spread * (i - (count - 1) * 0.5) * (2.0 / max(1, count - 1))
        c = tuple(min(1.0, max(0.0, ch * f)) for ch in base)
        out.append(reg("%s%d" % (tag, i), c))
    return out


# ── primitives (all flat shaded, all tagged) ──────────────────────────────────

def prim_ico(tag, radius, subdiv=1, scale=(1, 1, 1)):
    # NOTE: Blender counts subdivisions from 1 = base icosahedron (20 tris).
    # 1 -> 20 tris (chunky prop), 2 -> 80 tris (soft blob), 5 -> 5120 tris.
    bpy.ops.mesh.primitive_ico_sphere_add(radius=radius, subdivisions=subdiv,
                                          location=(0, 0, 0))
    o = bpy.context.active_object
    o.scale = scale
    o.data.materials.append(get_mat(tag))
    shade_flat(o)
    link_only(o)
    return o


def prim_cone(tag, r1, r2, depth, verts=6):
    bpy.ops.mesh.primitive_cone_add(radius1=r1, radius2=r2, depth=depth,
                                    vertices=verts, location=(0, 0, 0))
    o = bpy.context.active_object
    o.data.materials.append(get_mat(tag))
    shade_flat(o)
    link_only(o)
    return o


def prim_cyl(tag, radius, depth, verts=8):
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=depth,
                                        vertices=verts, location=(0, 0, 0))
    o = bpy.context.active_object
    o.data.materials.append(get_mat(tag))
    shade_flat(o)
    link_only(o)
    return o


def prim_cube(tag, dims):
    """Axis-aligned slab, 12 tris — the cheapest readable hard-edged shape."""
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0))
    o = bpy.context.active_object
    o.scale = dims
    o.data.materials.append(get_mat(tag))
    shade_flat(o)
    link_only(o)
    return o


def seat(obj, direction, height, spin=0.0, lean=0.0, lean_dir=0.0):
    """Stand an object on the sphere: +Z along the surface normal."""
    d = direction.normalized()
    obj.rotation_mode = 'QUATERNION'
    q = d.to_track_quat('Z', 'Y')
    if spin:
        q = q @ __import__('mathutils').Quaternion((0, 0, 1), spin)
    if lean:
        axis = __import__('mathutils').Quaternion((0, 0, 1), lean_dir)
        q = q @ axis @ __import__('mathutils').Quaternion((1, 0, 0), lean)
    obj.rotation_quaternion = q
    obj.location = d * (R_GROUND + height)
    return obj


# ── ground ────────────────────────────────────────────────────────────────────
# Eleven biome anchors, one per game world, laid out to match the concept art.
# Region membership is a weighted dot-product Voronoi over the sphere, so the
# borders wander along the ico facets and read as organic patches, never as
# geometry — the sphere itself is untouched.

BIOMES = [
    # tag,          concept (u, v),        pull,  colour
    ("nat",  place(-0.16, 0.52),           1.00, (0.34, 0.62, 0.26)),
    ("sak",  place(-0.52, 0.72),           1.04, (0.47, 0.66, 0.33)),
    ("mec",  place(-0.05, 0.94),           1.03, (0.48, 0.51, 0.56)),
    ("ice",  place(0.56, 0.80),            1.10, (0.84, 0.92, 0.98)),
    # crystal ground stays grey-violet STONE so the violet spires read as props
    ("cry",  place(0.86, -0.04),           1.00, (0.42, 0.38, 0.50)),
    # lava is deliberately the smallest region: at any real weight the near-black
    # basalt swallows a quarter of the silhouette and reads as a hole
    ("lav",  place(0.64, -0.44),           0.92, (0.26, 0.18, 0.15)),
    ("des",  place(0.30, -0.04),           1.04, (0.82, 0.68, 0.44)),
    ("mus",  place(0.04, -0.34),           1.05, (0.28, 0.43, 0.30)),
    ("oce",  place(-0.42, -0.56),          1.02, (0.11, 0.42, 0.58)),
    ("ali",  place(-0.88, -0.28),          1.00, (0.42, 0.31, 0.56)),
    ("clo",  -HERO_AXIS,                   0.90, (0.74, 0.87, 1.00)),
]

# Curved stone pathways, authored in concept space. Faces near these polylines
# are recoloured to path stone — the paths are paint, not cut terrain.
PATHS = [
    # wide tan stone band sweeping from the lake shore down to the lava field
    ([(0.04, 0.30), (0.26, 0.14), (0.46, 0.00), (0.62, -0.18)], 0.115, "pth"),
    # mossy path from under the sakura tree down past the mushrooms
    ([(-0.44, 0.44), (-0.32, 0.16), (-0.16, -0.10), (0.00, -0.30)], 0.085, "pmo"),
]


def catmull(pts, samples=26):
    out = []
    for i in range(len(pts) - 1):
        for s in range(samples):
            t = s / float(samples)
            p0 = pts[max(0, i - 1)]
            p1 = pts[i]
            p2 = pts[i + 1]
            p3 = pts[min(len(pts) - 1, i + 2)]
            t2, t3 = t * t, t * t * t
            x = 0.5 * ((2 * p1[0]) + (-p0[0] + p2[0]) * t +
                       (2 * p0[0] - 5 * p1[0] + 4 * p2[0] - p3[0]) * t2 +
                       (-p0[0] + 3 * p1[0] - 3 * p2[0] + p3[0]) * t3)
            y = 0.5 * ((2 * p1[1]) + (-p0[1] + p2[1]) * t +
                       (2 * p0[1] - 5 * p1[1] + 4 * p2[1] - p3[1]) * t2 +
                       (-p0[1] + 3 * p1[1] - 3 * p2[1] + p3[1]) * t3)
            out.append((x, y))
    out.append(pts[-1])
    return out


def build_ground():
    for tag, _d, _w, col in BIOMES:
        tints(tag, col)
    tints("pth", (0.78, 0.68, 0.50))
    tints("pmo", (0.52, 0.60, 0.34))
    # wider spread on the cloud hemisphere so it reads as stacked sky terraces
    # instead of one flat grey field
    tints("clo", (0.74, 0.87, 1.00), spread=0.13)

    bpy.ops.mesh.primitive_ico_sphere_add(radius=R_GROUND, subdivisions=5,
                                          location=(0, 0, 0))
    ground = bpy.context.active_object
    ground.name = "HeroGround"
    link_only(ground)

    slots = {}
    for tag, _d, _w, _c in BIOMES:
        for i in range(3):
            t = "%s%d" % (tag, i)
            slots[t] = len(ground.data.materials)
            ground.data.materials.append(get_mat(t))
    for tag in ("pth", "pmo"):
        for i in range(3):
            t = "%s%d" % (tag, i)
            slots[t] = len(ground.data.materials)
            ground.data.materials.append(get_mat(t))

    path_pts = [([place(u, v) for (u, v) in catmull(pl)], w, tag)
                for pl, w, tag in PATHS]

    for poly in ground.data.polygons:
        d = Vector(poly.center).normalized()
        best, best_tag = -9.9, "nat"
        for tag, bd, w, _c in BIOMES:
            s = w * d.dot(bd)
            if s > best:
                best, best_tag = s, tag
        for pts, width, ptag in path_pts:
            near = max(d.dot(p) for p in pts)
            if near > math.cos(width):
                best_tag = ptag
                break
        n = noise.noise(Vector(poly.center) * 4.3)
        tint = 0 if n < -0.08 else (2 if n > 0.08 else 1)
        poly.material_index = slots["%s%d" % (best_tag, tint)]
        poly.use_smooth = False
    return ground


# ── central lake (kidney-shaped spherical cap, matches the concept) ───────────

def build_lake(center_uv=(0.10, 0.46), alpha=0.42, rings=3, seg=26):
    reg("wat_deep", (0.07, 0.50, 0.66))
    reg("wat_shal", (0.28, 0.82, 0.86))
    reg("sand", (0.93, 0.85, 0.65))

    C = place(*center_uv)
    e1, e2 = tangent_frame(C)

    def shape(th):
        # crescent opening toward the lower right, like the reference lake
        return 1.0 + 0.30 * math.cos(th - 0.5) - 0.34 * math.cos(2.0 * (th - 0.5))

    def cap_point(a, th, lift):
        dirv = (C * math.cos(a) + (e1 * math.cos(th) + e2 * math.sin(th)) * math.sin(a))
        return dirv.normalized() * (R_GROUND + lift)

    me = bpy.data.meshes.new("HeroLake")
    bm = bmesh.new()
    lay_deep = 0

    hub = bm.verts.new(cap_point(0.0, 0.0, 0.030))
    prev = None
    for j in range(1, rings + 1):
        f = j / float(rings)
        ring = []
        for s in range(seg):
            th = (s / float(seg)) * math.tau
            a = alpha * shape(th) * f
            lift = 0.030 - 0.020 * f       # water domes up slightly in the middle
            ring.append(bm.verts.new(cap_point(a, th, lift)))
        if prev is None:
            for s in range(seg):
                bm.faces.new((hub, ring[s], ring[(s + 1) % seg]))
        else:
            for s in range(seg):
                bm.faces.new((prev[s], ring[s], ring[(s + 1) % seg],
                              prev[(s + 1) % seg]))
        prev = ring
    outer = prev
    shore = []
    for s in range(seg):
        th = (s / float(seg)) * math.tau
        a = alpha * shape(th) * 1.08
        shore.append(bm.verts.new(cap_point(a, th, 0.004)))
    for s in range(seg):
        bm.faces.new((outer[s], shore[s], shore[(s + 1) % seg],
                      outer[(s + 1) % seg]))

    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new("HeroLake", me)
    link_only(obj)
    me.materials.append(get_mat("wat_shal"))
    me.materials.append(get_mat("wat_deep"))
    me.materials.append(get_mat("sand"))
    total = len(me.polygons)
    shore_start = total - seg
    for i, poly in enumerate(me.polygons):
        if i >= shore_start:
            poly.material_index = 2
        elif i < seg * (rings - 1):
            poly.material_index = 1
        else:
            poly.material_index = 0
        poly.use_smooth = False
    return obj, C, alpha


# ── props ─────────────────────────────────────────────────────────────────────

def add_rock(d, tag, size, flat=0.7):
    o = prim_ico(tag, size, subdiv=0,
                 scale=(random.uniform(0.8, 1.3), random.uniform(0.8, 1.3), flat))
    seat(o, d, size * 0.35, spin=random.uniform(0, math.tau))
    return o


def add_path_stone(d, tag, size):
    o = prim_ico(tag, size, subdiv=0, scale=(1.25, 1.0, 0.35))
    seat(o, d, size * 0.10, spin=random.uniform(0, math.tau))
    return o


def add_sakura_tree(d, scale=1.0):
    """Hero focal point: thick leaning trunk, three limbs, blossom canopy."""
    reg("bark", (0.34, 0.22, 0.13))
    reg("bark_hi", (0.46, 0.31, 0.18))
    reg("blos_a", (0.98, 0.62, 0.78))
    reg("blos_b", (0.92, 0.44, 0.64))
    reg("blos_c", (1.00, 0.80, 0.88))
    Quat = __import__('mathutils').Quaternion
    parts = []
    t1, t2 = tangent_frame(d)

    # Tall, clearly visible trunk — the concept's tree is read trunk-first.
    trunk_h = 0.52 * scale
    trunk = prim_cone("bark", 0.075 * scale, 0.040 * scale, trunk_h, verts=7)
    seat(trunk, d, trunk_h * 0.48, lean=math.radians(11), lean_dir=0.8)
    parts.append(trunk)
    flare = prim_cone("bark", 0.115 * scale, 0.070 * scale, 0.11 * scale, verts=7)
    seat(flare, d, 0.052 * scale)
    parts.append(flare)

    base = d * (R_GROUND + trunk_h * 0.80)
    for (ang, ln, tilt) in [(0.5, 0.30, 58), (2.6, 0.26, 50), (4.6, 0.28, 54)]:
        limb = prim_cone("bark_hi", 0.032 * scale, 0.015 * scale, ln * scale, verts=6)
        limb.rotation_mode = 'QUATERNION'
        q = d.to_track_quat('Z', 'Y') @ Quat((0, 0, 1), ang) \
            @ Quat((1, 0, 0), math.radians(tilt))
        limb.rotation_quaternion = q
        limb.location = base + (t1 * math.cos(ang) + t2 * math.sin(ang)) * 0.05 * scale
        parts.append(limb)

    # Canopy = a wide flat crown of separated clusters, not one dome. Keeps the
    # silhouette lacy like the reference instead of reading as a pink cloud.
    canopy_c = d * (R_GROUND + trunk_h * 1.02)
    blobs = [(0.00, 0.02, 0.185, 2, "blos_a"), (0.23, 0.06, 0.150, 2, "blos_b"),
             (-0.24, 0.04, 0.155, 2, "blos_c"), (0.10, 0.20, 0.130, 2, "blos_a"),
             (-0.13, -0.17, 0.125, 1, "blos_b"), (0.17, -0.16, 0.118, 1, "blos_c"),
             (-0.30, -0.10, 0.105, 1, "blos_a"), (0.32, -0.05, 0.100, 1, "blos_b"),
             (-0.05, -0.27, 0.098, 1, "blos_c")]
    for (ox, oy, r, sd, tag) in blobs:
        b = prim_ico(tag, r * scale, subdiv=sd, scale=(1.30, 1.30, 0.62))
        b.rotation_mode = 'QUATERNION'
        b.rotation_quaternion = d.to_track_quat('Z', 'Y')
        b.location = canopy_c + (t1 * ox + t2 * oy) * scale + d * (0.03 * scale)
        parts.append(b)
    return parts


def add_ice_shard(d, scale=1.0):
    reg("ice_a", (0.62, 0.86, 0.98))
    reg("ice_b", (0.86, 0.96, 1.00))
    out = []
    n = random.choice([2, 3])
    t1, t2 = tangent_frame(d)
    for i in range(n):
        h = scale * random.uniform(0.20, 0.34)
        w = scale * random.uniform(0.09, 0.15)
        tag = "ice_a" if i % 2 == 0 else "ice_b"
        s = prim_ico(tag, w, subdiv=0, scale=(1.0, 0.85, h / w))
        ang = random.uniform(0, math.tau)
        seat(s, d, h * 0.42, spin=ang, lean=math.radians(random.uniform(4, 16)),
             lean_dir=ang)
        s.location = s.location + (t1 * math.cos(ang) + t2 * math.sin(ang)) * \
            (0.07 * i * scale)
        out.append(s)
    return out


def add_crystals(d, tag_a, tag_b, scale=1.0, count=None, max_lean=17.0):
    out = []
    n = count if count else random.choice([3, 4])
    t1, t2 = tangent_frame(d)
    for i in range(n):
        h = scale * random.uniform(0.20, 0.38)
        r = scale * random.uniform(0.032, 0.055)
        tag = tag_a if i % 2 == 0 else tag_b
        c = prim_cone(tag, r, 0.0, h, verts=5)
        ang = random.uniform(0, math.tau)
        seat(c, d, h * 0.5, spin=ang,
             lean=math.radians(random.uniform(2, max_lean)), lean_dir=ang)
        c.location = c.location + (t1 * math.cos(ang) + t2 * math.sin(ang)) * \
            (0.055 * i * scale)
        out.append(c)
    return out


def add_lava_crack(d, length, width, angle, tag="lava_glow"):
    """Emissive tangent quad — cheap (2 tris) and reads as a molten fissure."""
    t1, t2 = tangent_frame(d)
    a = t1 * math.cos(angle) + t2 * math.sin(angle)
    b = t1 * math.cos(angle + math.pi / 2) + t2 * math.sin(angle + math.pi / 2)
    base = d * (R_GROUND + 0.010)
    me = bpy.data.meshes.new("Crack")
    bm = bmesh.new()
    vs = [bm.verts.new(base + a * length + b * width),
          bm.verts.new(base - a * length + b * width * 0.5),
          bm.verts.new(base - a * length - b * width * 0.5),
          bm.verts.new(base + a * length - b * width)]
    bm.faces.new(vs)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new("Crack", me)
    link_only(o)
    me.materials.append(get_mat(tag))
    for p in me.polygons:
        p.use_smooth = False
    return o


def add_mushroom(d, cap_tag, scale=1.0, dots=2):
    reg("mus_stem", (0.94, 0.90, 0.78))
    reg("mus_dot", (1.00, 0.98, 0.94))
    out = []
    stem_h = 0.16 * scale
    stem = prim_cyl("mus_stem", 0.040 * scale, stem_h, verts=8)
    seat(stem, d, stem_h * 0.5)
    out.append(stem)
    cap_r = 0.125 * scale
    cap = prim_ico(cap_tag, cap_r, subdiv=1, scale=(1.0, 1.0, 0.66))
    seat(cap, d, stem_h * 0.94)
    out.append(cap)
    t1, t2 = tangent_frame(d)
    for i in range(dots):
        ang = random.uniform(0, math.tau)
        off = cap_r * random.uniform(0.28, 0.52)
        dot = prim_ico("mus_dot", cap_r * 0.24, subdiv=0, scale=(1.0, 1.0, 0.35))
        seat(dot, d, stem_h * 0.94 + cap_r * 0.52)
        dot.location = dot.location + (t1 * math.cos(ang) + t2 * math.sin(ang)) * off
        out.append(dot)
    return out


def add_cloud(d, scale=1.0, hover=0.16, puffs=3):
    reg("cloud_a", (1.00, 1.00, 1.00))
    reg("cloud_b", (0.93, 0.96, 1.00))
    out = []
    scale *= CLOUD_SCALE
    t1, t2 = tangent_frame(d)
    base = d * (R_GROUND + hover)
    spec = [(0.00, 0.00, 0.155, "cloud_a"), (0.17, -0.02, 0.115, "cloud_b"),
            (-0.16, 0.01, 0.108, "cloud_a"), (0.05, 0.09, 0.090, "cloud_b")]
    for i in range(min(puffs, len(spec))):
        ox, oy, r, tag = spec[i]
        # lead puff gets the soft 80-tri shell, satellites stay chunky
        p = prim_ico(tag, r * scale, subdiv=2 if i == 0 else 1,
                     scale=(1.35, 1.0, 0.78))
        p.rotation_mode = 'QUATERNION'
        p.rotation_quaternion = d.to_track_quat('Z', 'Y')
        p.location = base + (t1 * ox + t2 * oy) * scale
        out.append(p)
    return out


def add_ruin_tower(d, height, radius, tag, cap=True):
    reg("chrome", (0.74, 0.78, 0.84))
    reg("chrome_dk", (0.42, 0.46, 0.53))
    reg("chrome_hi", (0.90, 0.93, 0.97))
    out = []
    body = prim_cyl(tag, radius, height, verts=8)
    seat(body, d, height * 0.5)
    out.append(body)
    if cap:
        tip = prim_cone("chrome_hi", radius * 0.98, radius * 0.32, height * 0.22,
                        verts=8)
        seat(tip, d, height + height * 0.10)
        out.append(tip)
    return out


def add_alien_monolith(d, scale=1.0):
    """Three leaning dark slabs around a hovering emissive core.

    Replaces the earlier stalk+bulb pods, which read as lollipops. Standing
    stones plus a floating light say 'alien site' without adding organic shapes
    that would compete with the mushroom biome.
    """
    reg("ali_stone", (0.15, 0.13, 0.23))
    reg("ali_stone_hi", (0.27, 0.23, 0.38))
    reg("ali_glow", (0.44, 1.00, 0.52), emissive=True)
    out = []
    t1, t2 = tangent_frame(d)
    ring_r = 0.078 * scale
    for i in range(3):
        ang = i * (math.tau / 3.0) + 0.45
        h = (0.25 + 0.055 * (i % 2)) * scale
        slab = prim_cube("ali_stone" if i % 2 == 0 else "ali_stone_hi",
                         (0.030 * scale, 0.086 * scale, h))
        seat(slab, d, h * 0.46, spin=ang, lean=math.radians(11), lean_dir=ang)
        slab.location = slab.location + \
            (t1 * math.cos(ang) + t2 * math.sin(ang)) * ring_r
        out.append(slab)
    core = prim_ico("ali_glow", 0.046 * scale, subdiv=1)
    seat(core, d, 0.33 * scale)
    out.append(core)
    # faint emissive seam on the ground between the stones
    out.append(add_lava_crack(d, 0.052 * scale, 0.011 * scale, 0.0,
                              tag="ali_glow"))
    return out


def add_bush(d, tag, size):
    o = prim_ico(tag, size, subdiv=0, scale=(1.2, 1.2, 0.85))
    seat(o, d, size * 0.55, spin=random.uniform(0, math.tau))
    return o


# ── composition ───────────────────────────────────────────────────────────────

def build_props(lake_center, lake_alpha):
    reg("cry_a", (0.72, 0.46, 0.92))
    reg("cry_b", (0.95, 0.62, 0.86))
    reg("gold_a", (1.00, 0.82, 0.24))
    reg("gold_b", (0.94, 0.64, 0.16))
    reg("lava_glow", (1.00, 0.42, 0.06), emissive=True)
    reg("basalt", (0.09, 0.08, 0.08))
    reg("rock_grey", (0.46, 0.45, 0.44))
    reg("rock_tan", (0.72, 0.62, 0.46))
    reg("moss", (0.30, 0.55, 0.26))
    reg("moss_dk", (0.20, 0.40, 0.20))
    reg("mus_red", (0.88, 0.16, 0.16))
    reg("mus_blue", (0.20, 0.52, 0.92))

    # 1. Sakura tree — upper left, the focal point. Pulled in off the limb so
    # the trunk stays readable instead of hanging over the silhouette.
    add_sakura_tree(place(-0.46, 0.60), scale=1.15)
    for (u, v, s) in [(-0.64, 0.50, 0.055), (-0.36, 0.46, 0.045),
                      (-0.60, 0.70, 0.048), (-0.28, 0.64, 0.040)]:
        add_bush(place(u, v), "blos_b", s)
    # petal-strewn ground under the canopy
    for i in range(6):
        u = -0.46 + random.uniform(-0.22, 0.22)
        v = 0.60 + random.uniform(-0.20, 0.16)
        f = prim_ico("blos_c" if i % 2 else "blos_b", random.uniform(0.026, 0.042),
                     subdiv=1, scale=(1.4, 1.4, 0.22))
        seat(f, place(u, v), 0.006, spin=random.uniform(0, math.tau))
    for (u, v) in [(-0.24, 0.40), (-0.60, 0.34), (-0.14, 0.62)]:
        add_bush(place(u, v), "moss", 0.055)

    # 2. Lake shore rocks, following the crescent
    for i in range(11):
        th = (i / 11.0) * math.tau
        e1, e2 = tangent_frame(lake_center)
        a = lake_alpha * 1.22
        d = (lake_center * math.cos(a) +
             (e1 * math.cos(th) + e2 * math.sin(th)) * math.sin(a)).normalized()
        add_rock(d, "rock_grey" if i % 2 else "rock_tan",
                 random.uniform(0.055, 0.085))

    # 3. Mechanical ruins — crowning the north limb
    for (u, v, h, r, tag) in [(-0.06, 0.95, 0.42, 0.055, "chrome"),
                              (0.03, 0.99, 0.34, 0.045, "chrome_dk"),
                              (-0.16, 0.99, 0.28, 0.040, "chrome"),
                              (0.13, 0.93, 0.24, 0.048, "chrome_dk")]:
        add_ruin_tower(place(u, v), h * RUIN_SCALE, r * RUIN_SCALE, tag)
    dome = prim_cyl("chrome_dk", 0.13 * RUIN_SCALE, 0.05 * RUIN_SCALE, verts=10)
    seat(dome, place(0.20, 0.90), 0.025 * RUIN_SCALE)
    dome2 = prim_ico("chrome_hi", 0.10 * RUIN_SCALE, subdiv=1,
                     scale=(1.0, 1.0, 0.42))
    seat(dome2, place(-0.26, 0.92), 0.02 * RUIN_SCALE)

    # 4. Ice biome — upper right limb, big angular blocks
    for (u, v, s) in [(0.54, 0.80, 1.25), (0.66, 0.70, 1.0), (0.44, 0.86, 0.85)]:
        add_ice_shard(place(u, v), scale=s)

    # 5. Crystal biome — right flank, violet/pink spires
    for (u, v, s) in [(0.84, -0.02, 1.30), (0.74, 0.14, 0.95), (0.80, -0.20, 1.05),
                      (0.66, -0.02, 0.80), (0.90, 0.14, 0.85),
                      (0.94, -0.18, 0.90), (0.72, -0.34, 0.80)]:
        add_crystals(place(u, v), "cry_a", "cry_b", scale=s * CRYSTAL_SCALE)
    # scattered stone so the crystal flank is not a bare violet field in profile
    for i in range(6):
        add_rock(place(random.uniform(0.62, 0.96), random.uniform(-0.30, 0.24)),
                 "rock_grey", random.uniform(0.050, 0.075))

    # 6. Desert gold crystals — lower left, warm counterweight to the violet
    for (u, v, s) in [(-0.46, -0.26, 1.25), (-0.34, -0.12, 0.90)]:
        add_crystals(place(u, v), "gold_a", "gold_b", scale=s, count=4, max_lean=8.0)

    # 7. Lava field — lower right, black basalt with emissive fissures
    lava_c = place(0.62, -0.42)
    e1, e2 = tangent_frame(lava_c)
    # Fissures are drawn as short chained segments so they read as branching
    # veins in the rock rather than as loose orange sticks.
    for trunk in range(9):
        th0 = random.uniform(0, math.tau)
        a0 = random.uniform(0.04, 0.30)
        ang = random.uniform(0, math.tau)
        th, a = th0, a0
        for seg in range(random.choice([3, 4])):
            d = (lava_c * math.cos(a) +
                 (e1 * math.cos(th) + e2 * math.sin(th)) * math.sin(a)).normalized()
            ln = random.uniform(0.045, 0.075)
            add_lava_crack(d, ln, random.uniform(0.005, 0.009), ang)
            ang += random.uniform(-0.7, 0.7)
            th += math.cos(ang) * 0.10
            a = max(0.02, min(0.34, a + math.sin(ang) * 0.055))
    for i in range(7):
        th = random.uniform(0, math.tau)
        a = random.uniform(0.10, 0.36)
        d = (lava_c * math.cos(a) +
             (e1 * math.cos(th) + e2 * math.sin(th)) * math.sin(a)).normalized()
        add_rock(d, "basalt", random.uniform(0.06, 0.10), flat=0.85)

    # 8. Mushroom grove — lower centre, the concept's red/blue trio
    add_mushroom(place(0.02, -0.30), "mus_red", scale=1.30, dots=2)
    add_mushroom(place(-0.10, -0.36), "mus_red", scale=0.85, dots=2)
    add_mushroom(place(0.14, -0.36), "mus_blue", scale=1.00, dots=2)
    for (u, v) in [(-0.04, -0.22), (0.08, -0.44)]:
        add_bush(place(u, v), "moss", 0.05)

    # 9. Alien biome — far left, standing stones around a hovering core
    for (u, v, s) in [(-0.86, -0.24, 1.20), (-0.92, -0.10, 0.95),
                      (-0.80, -0.42, 1.05)]:
        add_alien_monolith(place(u, v), scale=s)

    # 10. Curved pathways — flat stones riding the painted bands
    for pl, width, tag in PATHS:
        pts = catmull(pl, samples=6)
        stone_tag = "rock_tan" if tag == "pth" else "moss_dk"
        for i, (u, v) in enumerate(pts):
            if i % 3:
                continue
            add_path_stone(place(u, v), stone_tag, random.uniform(0.055, 0.080))

    # 11. Clouds — hovering ring, heaviest on the left limb like the concept
    for (u, v, s, hov, pf) in [(-0.72, 0.06, 1.25, 0.20, 4),
                               (-0.30, -0.52, 1.00, 0.17, 3),
                               (0.92, 0.36, 1.00, 0.19, 3),
                               (-0.88, 0.34, 0.85, 0.16, 3),
                               (0.30, 0.94, 0.80, 0.18, 2)]:
        add_cloud(place(u, v), scale=s, hover=hov, puffs=pf)

    # 12. Cloud world — the far hemisphere. Without this the back of the planet
    # reads as a blank ball, which kills it as a rotating menu piece.
    reg("sky_pool", (0.46, 0.80, 0.94))
    Quat = __import__('mathutils').Quaternion
    for (u, v, s, hov) in [(0.00, 0.10, 1.50, 0.30), (-0.42, 0.34, 1.15, 0.34),
                           (0.46, -0.14, 1.25, 0.26), (-0.34, -0.40, 1.05, 0.29),
                           (0.30, 0.54, 0.95, 0.33)]:
        d = place_far(u, v)
        deck = prim_ico("cloud_a" if s > 1.10 else "cloud_b",
                        0.17 * s * CLOUD_SCALE, subdiv=2,
                        scale=(1.45, 1.05, 0.62))
        deck.rotation_mode = 'QUATERNION'
        deck.rotation_quaternion = d.to_track_quat('Z', 'Y') \
            @ Quat((0, 0, 1), random.uniform(0, math.tau))
        deck.location = d * (R_GROUND + hov)
    for (u, v, r) in [(-0.16, -0.12, 0.16), (0.30, 0.26, 0.12),
                      (-0.52, 0.06, 0.10), (0.14, -0.44, 0.13),
                      (-0.60, -0.34, 0.09)]:
        pool = prim_cyl("sky_pool", r, 0.012, verts=12)
        seat(pool, place_far(u, v), 0.008)


# NOTE: an earlier revision added a golden orbital ring as a second mesh. It was
# removed on the polish pass — the planet carries itself, and a surrounding
# element only competed with the silhouette. The asset is now a SINGLE mesh.


# ── palette bake + join ───────────────────────────────────────────────────────

def cell_uv(index):
    cx = ((index % GRID) + 0.5) / GRID
    cy = ((index // GRID) + 0.5) / GRID
    return (cx, cy)


def write_palette_png(path):
    img = bpy.data.images.get("HeroPlanet_Palette")
    if img is not None:
        bpy.data.images.remove(img)
    img = bpy.data.images.new("HeroPlanet_Palette", TEX_SIZE, TEX_SIZE, alpha=False)
    # Set colorspace/format BEFORE writing pixels: changing colorspace on a
    # generated image re-creates its buffer and silently wipes it to black.
    img.colorspace_settings.name = 'sRGB'
    img.file_format = 'PNG'
    img.filepath_raw = path
    cell = TEX_SIZE // GRID
    px = []
    for y in range(TEX_SIZE):
        row_cell = y // cell
        for x in range(TEX_SIZE):
            idx = row_cell * GRID + (x // cell)
            c = PALETTE[idx] if idx < len(PALETTE) else (0.0, 0.0, 0.0)
            px.extend((c[0], c[1], c[2], 1.0))
    img.pixels.foreach_set(px)
    img.update()
    img.save()
    return img


def make_atlas_material(name, img, emissive):
    m = bpy.data.materials.get(name)
    if m is not None:
        bpy.data.materials.remove(m)
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = 'Closest'
    tex.location = (-360, 200)
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.80
    if emissive:
        try:
            nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
            bsdf.inputs["Emission Strength"].default_value = 2.6
        except KeyError:
            pass
    return m


def bake_to_atlas():
    """Join every planet part, rewrite UVs to palette cells, 2 material slots."""
    coll = get_collection()
    parts = [o for o in coll.objects if o.type == 'MESH']

    # remember (object, polygon) -> tag before the join destroys slot layout
    tag_map = {}
    for o in parts:
        names = [ms.material.name[3:] if ms.material else "nat1"
                 for ms in o.material_slots] or ["nat1"]
        tag_map[o.name] = [names[min(p.material_index, len(names) - 1)]
                           for p in o.data.polygons]

    ground = bpy.data.objects["HeroGround"]
    order = [ground] + [o for o in parts if o is not ground]
    flat_tags = []
    for o in order:
        flat_tags.extend(tag_map[o.name])

    bpy.ops.object.select_all(action='DESELECT')
    for o in order:
        o.select_set(True)
    bpy.context.view_layer.objects.active = ground
    bpy.ops.object.join()
    planet = bpy.context.active_object
    planet.name = "HeroPlanet"
    planet.data.name = "HeroPlanetMesh"

    if len(planet.data.polygons) != len(flat_tags):
        raise RuntimeError("poly/tag mismatch: %d vs %d" %
                           (len(planet.data.polygons), len(flat_tags)))

    img = write_palette_png(os.path.join(OUT_DIR, "HeroPlanet_Palette.png"))
    surf = make_atlas_material("HeroPlanet_Surface", img, False)
    glow = make_atlas_material("HeroPlanet_Glow", img, True)
    planet.data.materials.clear()
    planet.data.materials.append(surf)
    planet.data.materials.append(glow)

    me = planet.data
    uv = me.uv_layers.get("UVMap") or me.uv_layers.new(name="UVMap")
    for poly, tag in zip(me.polygons, flat_tags):
        idx = PAL_INDEX.get(tag, PAL_INDEX.get("nat1", 0))
        u, v = cell_uv(idx)
        poly.material_index = 1 if tag in EMISSIVE else 0
        poly.use_smooth = False
        for li in poly.loop_indices:
            uv.data[li].uv = (u, v)

    planet.location = (0, 0, 0)
    planet.rotation_euler = (0, 0, 0)
    planet.scale = (1, 1, 1)
    return planet


def bake_vertex_shading(obj):
    """Bake the review rig's lighting into a per-corner colour attribute.

    The game runs URP's 2D Renderer, which has no 3D lighting pass — URP/Lit and
    URP/Unlit render byte-identically there (verified in-engine). So the planet
    has to carry its own shading. Baking it per face corner keeps the flat-shaded
    facets crisp, and the in-game shader just multiplies palette x vertex colour.

    Values are LINEAR (exported with colors_type='LINEAR') so the multiply lands
    in the same space Blender did it in.
    """
    mesh = obj.data
    for existing in list(mesh.color_attributes):
        mesh.color_attributes.remove(existing)
    attr = mesh.color_attributes.new(name="Shade", type='FLOAT_COLOR',
                                     domain='CORNER')

    # Same three-sun neutral rig used for the approved renders.
    def sun_dir(rx, ry, rz):
        e = __import__('mathutils').Euler(
            (math.radians(rx), math.radians(ry), math.radians(rz)), 'XYZ')
        return (e.to_quaternion() @ Vector((0.0, 0.0, -1.0))).normalized()

    key = sun_dir(58, 0, -34)
    fill = sun_dir(108, 0, 150)
    rim = sun_dir(74, 0, 96)

    AMBIENT, K_KEY, K_FILL, K_RIM, FLOOR = 0.30, 0.62, 0.34, 0.16, 0.20

    for poly in mesh.polygons:
        n = poly.normal
        shade = (AMBIENT
                 + K_KEY * max(0.0, n.dot(-key))
                 + K_FILL * max(0.0, n.dot(-fill))
                 + K_RIM * max(0.0, n.dot(-rim)))
        shade = min(1.0, max(FLOOR, shade))
        for li in poly.loop_indices:
            attr.data[li].color = (shade, shade, shade, 1.0)
    return attr


def orient_for_unity(obj):
    """Write the mesh out already in Unity's coordinate space.

    This export path passes Blender coordinates through unconverted (verified
    in-engine: the model arrived mirrored, sakura on the wrong side). So do the
    conversion here, explicitly:

      1. rotate the composition basis onto Blender (+X, +Z, -Y), then
      2. swap Y/Z, which is the odd permutation that takes right-handed Blender
         into left-handed Unity.

    Result in Unity: FRAME_R -> +X (right), FRAME_U -> +Y (up),
    HERO_AXIS -> -Z, i.e. the hero face looks straight at a camera sitting on -Z.

    Step 2 has a negative determinant, so triangle winding is flipped afterwards
    to keep the faces pointing outwards. Vertex colours are uniform per face, so
    the loop reordering that comes with the flip is harmless.
    """
    Matrix = __import__('mathutils').Matrix
    # M v = t1*(v.R) + t2*(v.U) + t3*(v.A)  ->  M = T @ S^T
    t1 = Vector((1.0, 0.0, 0.0))     # FRAME_R   -> +X
    t2 = Vector((0.0, 0.0, 1.0))     # FRAME_U   -> +Z (becomes +Y after the swap)
    t3 = Vector((0.0, -1.0, 0.0))    # HERO_AXIS -> -Y (becomes -Z after the swap)
    T = Matrix((t1, t2, t3)).transposed()               # columns = targets
    S_inv = Matrix((FRAME_R, FRAME_U, HERO_AXIS))       # rows = source basis
    M = T @ S_inv

    # Y/Z swap puts the hero face on -Z and the composition's up on +Y.
    swap = Matrix(((1.0, 0.0, 0.0), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0)))
    # ...but the swap is a mirror, and this export path applies one of its own, so
    # the model arrived flipped left-to-right twice over. Negating X makes the
    # number of mirrors even, which is what actually lands the layout the right
    # way round in-engine (sakura upper LEFT, ice upper RIGHT). Verified visually.
    neg_x = Matrix(((-1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)))
    full = neg_x @ swap @ M
    # det(full) == +1, so winding is already correct — no normal flip here.
    for v in obj.data.vertices:
        v.co = full @ v.co
    obj.data.update()
    return full


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def preview_render(path, view=None, ortho=4.1, res=900):
    """Turntable-style check render. Not part of the asset; art review only.
    `view` is the direction the camera sits in (defaults to the hero axis)."""
    d = (view or HERO_AXIS).normalized()
    cam_d = bpy.data.cameras.get("HeroCam") or bpy.data.cameras.new("HeroCam")
    cam_d.type = 'ORTHO'
    cam_d.ortho_scale = ortho
    cam = bpy.data.objects.get("HeroCamObj")
    if cam is None:
        cam = bpy.data.objects.new("HeroCamObj", cam_d)
        scene.collection.objects.link(cam)
    cam.data = cam_d
    cam.location = d * 9.0
    cam.rotation_mode = 'QUATERNION'
    cam.rotation_quaternion = (-d).to_track_quat('-Z', 'Y')
    scene.camera = cam

    for nm, energy, rot in [("HeroKey", 3.0, (52, 0, -38)),
                            ("HeroFill", 2.0, (112, 0, 148))]:
        ld = bpy.data.lights.get(nm) or bpy.data.lights.new(nm, type='SUN')
        ld.energy = energy
        lo = bpy.data.objects.get(nm + "Obj")
        if lo is None:
            lo = bpy.data.objects.new(nm + "Obj", ld)
            scene.collection.objects.link(lo)
        lo.data = ld
        lo.rotation_euler = tuple(math.radians(a) for a in rot)

    # soft ambient so the review render is not crushed on the shadow side
    world = scene.world or bpy.data.worlds.new("HeroWorld")
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs["Color"].default_value = (0.55, 0.62, 0.74, 1.0)
        bg.inputs["Strength"].default_value = 0.55

    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            scene.render.engine = eng
            break
        except TypeError:
            continue
    scene.render.film_transparent = True
    scene.render.resolution_x = res
    scene.render.resolution_y = res
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    return path


# ── entry point ───────────────────────────────────────────────────────────────

def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    clear_scene()
    ground = build_ground()
    lake, lake_c, lake_a = build_lake()
    build_props(lake_c, lake_a)
    planet = bake_to_atlas()
    # Bake BEFORE reorienting: the shading maths has to run against the same
    # normals the approved review renders were lit with.
    bake_vertex_shading(planet)
    orient_for_unity(planet)
    planet.data.validate()

    # drop the per-tag build materials and the meshes the join orphaned, so the
    # saved .blend carries only the two shipping materials
    bpy.ops.outliner.orphans_purge(do_local_ids=True, do_linked_ids=True,
                                   do_recursive=True)

    report = {
        "planet_tris": tri_count(planet),
        "planet_verts": len(planet.data.vertices),
        "palette_entries": len(PALETTE),
        "emissive_tags": sorted(EMISSIVE),
    }
    report["total_tris"] = report["planet_tris"]

    blend_path = os.path.join(OUT_DIR, "HeroPlanet.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)

    fbx_path = os.path.join(OUT_DIR, "HeroPlanet.fbx")
    bpy.ops.object.select_all(action='DESELECT')
    planet.select_set(True)
    bpy.context.view_layer.objects.active = planet
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        apply_unit_scale=True,
        global_scale=1.0,
        apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z',
        axis_up='Y',
        object_types={'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        use_triangles=True,
        use_tspace=False,
        colors_type='LINEAR',   # baked shading must not be sRGB-encoded on the way out
        path_mode='COPY',
        embed_textures=False,
        bake_space_transform=False,
    )
    report["blend"] = blend_path
    report["fbx"] = fbx_path
    for k in sorted(report):
        print("%-16s %s" % (k, report[k]))
    return report


HERO_REPORT = main()
