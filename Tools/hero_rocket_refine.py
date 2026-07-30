# Hero Rocket presentation pass.
#
# Input : Tools/HeroRocket/HeroRocket_source.fbx   (the untouched original ship)
# Output: Assets/Models/HeroRocket.fbx             (what Unity imports)
#
# The ship is rendered in Unity by Sprites/Default: unlit, vertex colour multiplied by
# the skin tint. Normals, smoothing and materials therefore change nothing on screen —
# every gram of shading has to live in the vertex colours. That is what this pass does:
#
#   1. replaces the flat disc engine with a real flared bell and a glowing throat
#   2. dresses the four regions the eye actually lands on — cockpit glass, nose lacquer,
#      fins and engine collar — while they are still low-poly, so the bevel inherits it
#   3. bevels every hard edge, cheaply on the small greebles and richly on the silhouette
#   4. bakes ambient occlusion, the key light the art was already lit from, a rim light
#      and a specular highlight back into the vertex colours
#
# Occlusion is sampled per VERTEX and smoothed, not per face. Per-face rays quantise into
# a different shade on every quad, which is what made the old pass read as a dirty ship
# instead of a lit one; the facets still come from the flat key light, which is where the
# low-poly identity actually lives.
#
# GAMEPLAY IS NOT TOUCHED. Proportions, silhouette, object name, material slot and the
# mesh origin are all preserved: the ship's bounds proxy, colliders, orbit radius and the
# flame anchor at the nozzle lip keep meaning exactly what they meant before.
#
# Run:  blender --background --python Tools/hero_rocket_refine.py
# (or exec() this file from an interactive Blender session)

import math
import os
import random

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

OBJECT_NAME = "HeroRocket"

# ── Bevel ───────────────────────────────────────────────────────────────────────
# Two tiers. The silhouette — hull, nose, fins, bell — is worth two segments; the six
# collar pods and the cockpit studs are a few millimetres across on a phone and were
# costing more triangles than the entire hull. They get a single chamfer, which still
# catches the key light without paying for a rounded corner nobody can resolve.
BEVEL_WIDTH = 0.014
BEVEL_SEGMENTS = 2
DETAIL_BEVEL_WIDTH = 0.010
DETAIL_BEVEL_SEGMENTS = 1
BEVEL_ANGLE = 30.0
# An island whose bounding box is smaller than this across is a greeble, not a landmark.
DETAIL_ISLAND_SIZE = 0.35

# ── Lighting ────────────────────────────────────────────────────────────────────
# The direction the original art was already shaded from, measured off its own vertex
# colours. Lighting it again from anywhere else would fight the painting.
KEY_DIRECTION = Vector((-0.6, -1.0, 0.5))
VIEW_DIRECTION = Vector((0.0, -1.0, 0.0))   # the ship is always seen from this side

KEY_AMBIENT = 0.90          # how bright an unlit face stays
KEY_STRENGTH = 0.26         # how much the key adds on top
AO_STRENGTH = 0.30          # crevice darkening; higher reads as dirt, not as form
AO_RANGE = 0.24             # world units: tight contact shadows only
AO_RAYS = 28                # per vertex, not per face: enough that the gradient is clean
AO_SMOOTH_PASSES = 2        # blurs the last of the ray noise along the surface
RIM_STRENGTH = 0.13         # space bounce along the silhouette
SPECULAR_STRENGTH = 0.17
SPECULAR_TIGHTNESS = 22.0

# Faces this hot, and down inside the engine, are light sources (throat, rim lip) and are
# left exactly as authored. The geometry gate is not optional: the ship's own red is the
# same colour as the cooler end of the bell, so a purely chromatic test hands the entire
# hull, nose and fin set a free pass and the relight never reaches the body at all.
EMISSIVE_MAX_Z = -1.03
EMISSIVE_MAX_RADIUS = 0.50
EMISSIVE_MIN_RED = 0.62
EMISSIVE_MAX_BLUE = 0.45
EMISSIVE_MIN_SPLIT = 0.35

# ── Facet settling ──────────────────────────────────────────────────────────────
# The source paints every facet its own slightly different shade — 56 reds across the 96
# faces of the nose alone, scattered rather than directional. At menu size that reads as
# a soiled ship, so each facet is pulled most of the way toward its neighbours before any
# light is baked. Neighbours further away in colour than the threshold are ignored, which
# is what keeps the red/white livery edges as crisp as they were authored.
SETTLE_STRENGTH = 0.68
SETTLE_THRESHOLD = 0.22
SETTLE_PASSES = 2

# ── Cockpit glass ───────────────────────────────────────────────────────────────
# The dome was a flat blue disc with a painted dot on it. Glass reads as glass when the
# rim goes dark and saturated while the middle stays open, so the shading is driven by
# how far each facet has turned away from the camera — a Fresnel term, baked.
GLASS_DEEP = (0.050, 0.115, 0.330)      # the rim, where the dome turns away
GLASS_MID = (0.165, 0.395, 0.790)       # the body of the glass
GLASS_SKY = (0.470, 0.760, 1.000)       # the sky lying across the top of the dome
GLASS_SKY_DIRECTION = Vector((-0.55, -1.0, 0.62))
GLASS_FRESNEL_POWER = 1.7
GLASS_RIM_DEPTH = 0.88
# Broad and low: the dome already carries an authored highlight blob, and a tight sky
# term next to it reads as a chip in the glass rather than as light on it.
GLASS_SKY_TIGHTNESS = 2.6
GLASS_SKY_STRENGTH = 0.44
GLASS_GLINT_TIGHTNESS = 40.0
GLASS_GLINT_STRENGTH = 0.55
# Blue is unique to the canopy on this ship, so the region needs no coordinates.
GLASS_MIN_BLUE = 0.30
GLASS_MIN_SPLIT = 0.18

# ── Nose lacquer ────────────────────────────────────────────────────────────────
# A cone shaded flat reads as plastic. Deepening the shoulder and opening the tip gives
# the nose a form of its own before any light lands on it, which is most of what
# separates a premium mobile ship from a placeholder one.
NOSE_BASE_Z = 0.42
NOSE_SHOULDER_SHADE = 0.86
NOSE_TIP_SHADE = 1.15
NOSE_TIP_WARMTH = 0.10      # how far the tip leans toward the warm end of its own red

# ── Fins ────────────────────────────────────────────────────────────────────────
# Same idea, one axis over: dark where the fin meets the hull, bright along the outer
# edge, so the three of them stop reading as one flat shape behind the body.
FIN_MIN_REACH = 0.60        # an island reaching this far off the axis is a fin
FIN_MAX_Z = -1.20           # ...and hanging this low is a fin rather than a collar
FIN_ROOT_SHADE = 0.80
FIN_EDGE_SHADE = 1.14
FIN_LEADING_LIFT = 0.07     # the top edge catches a little more than the trailing one

# ── Engine bell ─────────────────────────────────────────────────────────────────
# (radius, z, sRGB). The outer skin runs from the collar down to the lip, then the inner
# wall climbs back up, so the bell is one closed surface of revolution with no caps.
# The lowest point stays above the fins, which is what keeps the ship's bounds unchanged.
#
# The outer skin is deliberately a lit graphite rather than the near-black it was: seen
# from the front — the only angle the menu ever shows — a dark collar between two bright
# fins reads as a hole punched in the ship.
BELL_SEGMENTS = 20
BELL_PROFILE = [
    (0.345, -1.040, (0.395, 0.395, 0.435)),   # tucked up inside the engine collar
    (0.228, -1.120, (0.300, 0.295, 0.315)),   # waist, in its own shadow
    (0.335, -1.215, (0.520, 0.420, 0.345)),   # flare, warming toward the exit
    (0.455, -1.295, (0.690, 0.485, 0.225)),   # brass rim, outer edge
    (0.418, -1.325, (0.930, 0.690, 0.300)),   # rim lip: the machined edge
    (0.370, -1.268, (1.000, 0.560, 0.110)),   # inner wall, hot at the exit
    (0.275, -1.198, (0.780, 0.250, 0.030)),
    (0.168, -1.120, (0.430, 0.100, 0.020)),
    (0.130, -1.055, (0.260, 0.055, 0.015)),   # inner wall meets the top annulus
]
CORE_RADIUS = 0.132
CORE_RIM_Z = -1.098
CORE_CENTER_Z = -1.086
CORE_CENTER_COLOR = (1.0, 0.84, 0.42)
CORE_RIM_COLOR = (1.0, 0.46, 0.08)

# Anything below this, and no wider than this, is the old flat disc engine.
OLD_ENGINE_MAX_Z = -1.03
OLD_ENGINE_MAX_RADIUS = 0.45


def project_root():
    return os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def source_path():
    return os.path.join(project_root(), "Tools", "HeroRocket", "HeroRocket_source.fbx")


def output_path():
    return os.path.join(project_root(), "Assets", "Models", "HeroRocket.fbx")


# ── Colour ──────────────────────────────────────────────────────────────────────
# Everything below reads and writes sRGB; the mesh stores linear. These two functions
# are the only place that difference exists.

def to_linear(value):
    return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4


def to_srgb(value):
    value = min(1.0, max(0.0, value))
    return value * 12.92 if value <= 0.0031308 else 1.055 * (value ** (1 / 2.4)) - 0.055


def linear_rgb(color):
    return tuple(to_linear(channel) for channel in color)


def read_srgb(loop, layer):
    color = loop[layer]
    return (to_srgb(color[0]), to_srgb(color[1]), to_srgb(color[2]))


def write_srgb(loop, layer, rgb):
    alpha = loop[layer][3]
    loop[layer] = (to_linear(rgb[0]), to_linear(rgb[1]), to_linear(rgb[2]), alpha)


def mix(a, b, t):
    t = min(1.0, max(0.0, t))
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def load_source():
    bpy.ops.wm.read_homefile(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=source_path())

    rocket = bpy.data.objects.get(OBJECT_NAME)
    if rocket is None:
        raise RuntimeError("Source FBX did not contain an object named " + OBJECT_NAME)

    bpy.context.view_layer.objects.active = rocket
    rocket.select_set(True)

    # Unity renders the ship unlit, so custom split normals carry nothing on screen.
    # Dropping them keeps the bevel predictable.
    custom_normals = rocket.data.attributes.get("custom_normal")
    if custom_normals is not None:
        rocket.data.attributes.remove(custom_normals)

    return rocket


# ── Islands ─────────────────────────────────────────────────────────────────────
# The ship is one mesh of many loose parts. Which part a vertex belongs to is the only
# region information the source carries, so both the bevel tiers and the fin dressing
# are decided from it.

def island_of(vertex, seen):
    stack = [vertex]
    island = [vertex]
    seen.add(vertex.index)
    while stack:
        current = stack.pop()
        for edge in current.link_edges:
            other = edge.other_vert(current)
            if other.index in seen:
                continue
            seen.add(other.index)
            island.append(other)
            stack.append(other)
    return island


def islands_of(bm):
    seen = set()
    found = []
    for vertex in bm.verts:
        if vertex.index in seen:
            continue
        found.append(island_of(vertex, seen))
    return found


# The engine is the one region of the ship that is a light rather than a surface: it is
# built by this pass with its own gradients, so both the settling and the relight step
# around it instead of averaging or shading it.
def in_engine(face):
    center = face.calc_center_median()
    return (center.z <= EMISSIVE_MAX_Z
            and math.hypot(center.x, center.y) <= EMISSIVE_MAX_RADIUS)


def island_size(island):
    xs = [v.co.x for v in island]
    ys = [v.co.y for v in island]
    zs = [v.co.z for v in island]
    return Vector((max(xs) - min(xs), max(ys) - min(ys), max(zs) - min(zs))).length


def is_fin(island):
    reach = max(math.hypot(v.co.x, v.co.y) for v in island)
    lowest = min(v.co.z for v in island)
    return reach >= FIN_MIN_REACH and lowest <= FIN_MAX_Z


def remove_old_engine(bm):
    doomed = []
    for island in islands_of(bm):
        top = max(v.co.z for v in island)
        widest = max(math.hypot(v.co.x, v.co.y) for v in island)
        if top <= OLD_ENGINE_MAX_Z and widest <= OLD_ENGINE_MAX_RADIUS:
            doomed.extend(island)

    if doomed:
        bmesh.ops.delete(bm, geom=doomed, context='VERTS')
    return len(doomed)


def build_bell(bm):
    colors = bm.loops.layers.color["Col"]
    uvs = bm.loops.layers.uv.get("UVMap")

    def ring(radius, z):
        return [bm.verts.new((math.cos(i / BELL_SEGMENTS * math.tau) * radius,
                              math.sin(i / BELL_SEGMENTS * math.tau) * radius, z))
                for i in range(BELL_SEGMENTS)]

    def paint(loop, color):
        loop[colors] = (color[0], color[1], color[2], 1.0)
        if uvs:
            loop[uvs].uv = (0.5, 0.5)

    def band(lower, upper, lower_color, upper_color):
        lower_linear = linear_rgb(lower_color)
        upper_linear = linear_rgb(upper_color)
        lower_set = set(lower)
        for i in range(BELL_SEGMENTS):
            j = (i + 1) % BELL_SEGMENTS
            face = bm.faces.new((lower[i], lower[j], upper[j], upper[i]))
            face.smooth = True
            for loop in face.loops:
                paint(loop, lower_linear if loop.vert in lower_set else upper_linear)

    rings = [ring(radius, z) for radius, z, _ in BELL_PROFILE]
    bm.verts.ensure_lookup_table()

    for index in range(len(rings) - 1):
        band(rings[index], rings[index + 1], BELL_PROFILE[index][2], BELL_PROFILE[index + 1][2])
    band(rings[-1], rings[0], BELL_PROFILE[-1][2], BELL_PROFILE[0][2])

    # The combustion core: a small glowing plate so the throat never reads as a hole.
    center = bm.verts.new((0.0, 0.0, CORE_CENTER_Z))
    rim = ring(CORE_RADIUS, CORE_RIM_Z)
    center_linear = linear_rgb(CORE_CENTER_COLOR)
    rim_linear = linear_rgb(CORE_RIM_COLOR)
    for i in range(BELL_SEGMENTS):
        j = (i + 1) % BELL_SEGMENTS
        face = bm.faces.new((center, rim[i], rim[j]))
        face.smooth = True
        for loop in face.loops:
            paint(loop, center_linear if loop.vert is center else rim_linear)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])


def replace_engine(rocket):
    mesh = rocket.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    removed = remove_old_engine(bm)
    build_bell(bm)
    bm.to_mesh(mesh)
    mesh.update()
    bm.free()
    return removed


# ── Region dressing ─────────────────────────────────────────────────────────────
# Albedo only, and deliberately before the bevel: the new chamfers then interpolate the
# dressed colours instead of smearing the flat originals across them. No light is added
# here — relight() owns all of that, so the two passes never fight.

def is_glass(srgb):
    return srgb[2] >= GLASS_MIN_BLUE and srgb[2] - srgb[0] >= GLASS_MIN_SPLIT


def dress_glass(face, colors):
    view = VIEW_DIRECTION.normalized()
    sky = GLASS_SKY_DIRECTION.normalized()
    normal = face.normal.normalized()

    # How far this facet has turned off the camera. The dome's rim approaches 1, the
    # patch facing the player stays near 0.
    fresnel = pow(1.0 - max(0.0, normal.dot(view)), GLASS_FRESNEL_POWER)
    color = mix(GLASS_MID, GLASS_DEEP, fresnel * GLASS_RIM_DEPTH)

    toward_sky = max(0.0, normal.dot(sky))
    color = mix(color, GLASS_SKY, pow(toward_sky, GLASS_SKY_TIGHTNESS) * GLASS_SKY_STRENGTH)

    glint = pow(toward_sky, GLASS_GLINT_TIGHTNESS) * GLASS_GLINT_STRENGTH
    color = tuple(min(1.0, channel + glint) for channel in color)

    for loop in face.loops:
        write_srgb(loop, colors, color)


def dress_nose(face, colors, tip_z):
    span = max(1e-5, tip_z - NOSE_BASE_Z)
    for loop in face.loops:
        along = min(1.0, max(0.0, (loop.vert.co.z - NOSE_BASE_Z) / span))
        shade = NOSE_SHOULDER_SHADE + (NOSE_TIP_SHADE - NOSE_SHOULDER_SHADE) * along
        red, green, blue = read_srgb(loop, colors)
        # The tip does not just get brighter, it gets warmer: the same lacquer catching
        # more of the sky than the shoulder buried against the hull.
        warm = NOSE_TIP_WARMTH * along
        write_srgb(loop, colors, (
            min(1.0, red * shade * (1.0 + warm)),
            min(1.0, green * shade),
            min(1.0, blue * shade * (1.0 - warm * 0.5)),
        ))


def dress_fin(face, colors, root_reach, edge_reach, low_z, high_z):
    span = max(1e-5, edge_reach - root_reach)
    height = max(1e-5, high_z - low_z)
    for loop in face.loops:
        position = loop.vert.co
        outward = min(1.0, max(0.0, (math.hypot(position.x, position.y) - root_reach) / span))
        leading = min(1.0, max(0.0, (position.z - low_z) / height))
        shade = (FIN_ROOT_SHADE + (FIN_EDGE_SHADE - FIN_ROOT_SHADE) * outward
                 + FIN_LEADING_LIFT * leading)
        red, green, blue = read_srgb(loop, colors)
        write_srgb(loop, colors, (min(1.0, red * shade),
                                  min(1.0, green * shade),
                                  min(1.0, blue * shade)))


def settle_facets(bm, colors):
    for _ in range(SETTLE_PASSES):
        current = {face.index: read_srgb(face.loops[0], colors) for face in bm.faces}
        settled = {}

        for face in bm.faces:
            if in_engine(face):
                continue
            own = current[face.index]
            kin = []
            for edge in face.edges:
                for neighbour in edge.link_faces:
                    if neighbour is face:
                        continue
                    other = current[neighbour.index]
                    distance = math.sqrt(sum((own[i] - other[i]) ** 2 for i in range(3)))
                    if distance <= SETTLE_THRESHOLD:
                        kin.append(other)

            if not kin:
                settled[face.index] = own
                continue

            average = tuple(sum(color[i] for color in kin) / len(kin) for i in range(3))
            settled[face.index] = mix(own, average, SETTLE_STRENGTH)

        for face in bm.faces:
            if face.index not in settled:
                continue
            for loop in face.loops:
                write_srgb(loop, colors, settled[face.index])


def dress_surfaces(rocket):
    mesh = rocket.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.faces.ensure_lookup_table()
    bm.normal_update()
    colors = bm.loops.layers.color["Col"]

    settle_facets(bm, colors)

    fin_faces = {}
    for island in islands_of(bm):
        if not is_fin(island):
            continue
        root_reach = min(math.hypot(v.co.x, v.co.y) for v in island)
        edge_reach = max(math.hypot(v.co.x, v.co.y) for v in island)
        low_z = min(v.co.z for v in island)
        high_z = max(v.co.z for v in island)
        for vertex in island:
            for face in vertex.link_faces:
                fin_faces[face.index] = (root_reach, edge_reach, low_z, high_z)

    tip_z = max(v.co.z for v in bm.verts)
    glass = nose = fins = 0

    for face in bm.faces:
        base = read_srgb(face.loops[0], colors)

        if is_glass(base):
            dress_glass(face, colors)
            glass += 1
            continue

        bounds = fin_faces.get(face.index)
        if bounds is not None:
            dress_fin(face, colors, *bounds)
            fins += 1
            continue

        if face.calc_center_median().z >= NOSE_BASE_Z:
            dress_nose(face, colors, tip_z)
            nose += 1

    bm.to_mesh(mesh)
    mesh.update()
    bm.free()
    return glass, nose, fins


# ── Bevel ───────────────────────────────────────────────────────────────────────

def bevelled_edges(bm, want_detail):
    detail = set()
    for island in islands_of(bm):
        if island_size(island) < DETAIL_ISLAND_SIZE:
            detail.update(v.index for v in island)

    chosen = []
    for edge in bm.edges:
        if len(edge.link_faces) != 2 or not edge.is_manifold:
            continue
        if edge.calc_face_angle(0.0) < math.radians(BEVEL_ANGLE):
            continue
        is_detail = edge.verts[0].index in detail and edge.verts[1].index in detail
        if is_detail == want_detail:
            chosen.append(edge)
    return chosen


def bevel_edges(rocket):
    mesh = rocket.data
    bm = bmesh.new()
    bm.from_mesh(mesh)

    for width, segments, want_detail in (
            (BEVEL_WIDTH, BEVEL_SEGMENTS, False),
            (DETAIL_BEVEL_WIDTH, DETAIL_BEVEL_SEGMENTS, True)):
        bm.edges.ensure_lookup_table()
        edges = bevelled_edges(bm, want_detail)
        if not edges:
            continue
        bmesh.ops.bevel(bm, geom=edges, offset=width, offset_type='OFFSET',
                        segments=segments, profile=0.5, affect='EDGES',
                        clamp_overlap=True, miter_outer='ARC', harden_normals=False,
                        loop_slide=True)

    bm.to_mesh(mesh)
    mesh.update()
    bm.free()


# ── Relight ─────────────────────────────────────────────────────────────────────

def hemisphere_directions(count):
    # Deterministic, so re-running the pass never shifts the shading.
    generator = random.Random(7)
    directions = []
    while len(directions) < count:
        candidate = Vector((generator.uniform(-1, 1),
                            generator.uniform(-1, 1),
                            generator.uniform(-1, 1)))
        if 0.25 < candidate.length <= 1.0:
            directions.append(candidate.normalized())
    return directions


def is_emissive(face, srgb):
    return (in_engine(face)
            and srgb[0] > EMISSIVE_MIN_RED
            and srgb[2] < EMISSIVE_MAX_BLUE
            and srgb[0] - srgb[2] > EMISSIVE_MIN_SPLIT)


# Occlusion measured at the vertices and blurred along the edges. Sampling it per face
# instead — as this pass used to — turns twelve rays into twelve visible shades and
# scatters them over the hull; interpolating a smooth vertex value across the same flat
# facets keeps the low-poly read and loses the noise.
def vertex_occlusion(bm, tree):
    hemisphere = hemisphere_directions(AO_RAYS)
    occlusion = [0.0] * len(bm.verts)

    for vertex in bm.verts:
        normal = vertex.normal.copy()
        if normal.length < 1e-6:
            continue
        normal.normalize()

        origin = vertex.co + normal * 0.005
        blocked = 0
        for direction in hemisphere:
            ray = (direction + normal * 0.6).normalized()
            if tree.ray_cast(origin, ray, AO_RANGE)[0] is not None:
                blocked += 1
        occlusion[vertex.index] = blocked / len(hemisphere)

    for _ in range(AO_SMOOTH_PASSES):
        blurred = list(occlusion)
        for vertex in bm.verts:
            neighbours = [edge.other_vert(vertex).index for edge in vertex.link_edges]
            if not neighbours:
                continue
            average = sum(occlusion[index] for index in neighbours) / len(neighbours)
            blurred[vertex.index] = occlusion[vertex.index] * 0.5 + average * 0.5
        occlusion = blurred

    return occlusion


def relight(rocket):
    mesh = rocket.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    bm.faces.ensure_lookup_table()
    bm.normal_update()
    tree = BVHTree.FromBMesh(bm)

    key = KEY_DIRECTION.normalized()
    view = VIEW_DIRECTION.normalized()
    half = (key + view).normalized()
    colors = bm.loops.layers.color["Col"]
    occlusion = vertex_occlusion(bm, tree)
    emissive_faces = 0

    for face in bm.faces:
        normal = face.normal.copy()
        if normal.length < 1e-6:
            continue
        normal.normalize()

        if is_emissive(face, read_srgb(face.loops[0], colors)):
            emissive_faces += 1
            continue

        # Flat per facet: this is the term the low-poly look is made of.
        lit = KEY_AMBIENT + KEY_STRENGTH * max(0.0, normal.dot(key))
        rim = pow(1.0 - max(0.0, normal.dot(view)), 3.0) * RIM_STRENGTH
        specular = pow(max(0.0, normal.dot(half)), SPECULAR_TIGHTNESS) * SPECULAR_STRENGTH

        for loop in face.loops:
            # Smooth per vertex: this is the term that makes the facets sit on a solid.
            shade = lit * (1.0 - AO_STRENGTH * occlusion[loop.vert.index])
            red, green, blue = read_srgb(loop, colors)
            write_srgb(loop, colors, (
                red * shade + rim * 0.78 + specular,
                green * shade + rim * 0.84 + specular,
                blue * shade + rim + specular,
            ))

    bm.to_mesh(mesh)
    mesh.update()
    bm.free()
    return emissive_faces


def export(rocket):
    bpy.ops.object.select_all(action='DESELECT')
    rocket.select_set(True)
    bpy.context.view_layer.objects.active = rocket

    with bpy.context.temp_override(object=rocket, active_object=rocket,
                                   selected_objects=[rocket],
                                   selected_editable_objects=[rocket]):
        # Scale must round-trip exactly: Unity imports this with useFileScale, and the
        # scene's rocket carries its own non-uniform override on top. Anything that bakes
        # a unit conversion into the file arrives as a 100x root scale in the prefab.
        bpy.ops.export_scene.fbx(
            filepath=output_path(),
            use_selection=True,
            apply_unit_scale=False,
            apply_scale_options='FBX_SCALE_NONE',
            global_scale=1.0,
            axis_forward='-Z',
            axis_up='Y',
            object_types={'MESH'},
            use_mesh_modifiers=True,
            # The project renders in Linear colour space and Sprites/Default hands vertex
            # colours to the GPU untouched, so sRGB bytes arrive as linear values and the
            # ship washes out to salmon. Exporting linear bytes is what makes the red on
            # screen the red that was painted.
            colors_type='LINEAR',
            bake_space_transform=False,
            add_leaf_bones=False,
            path_mode='COPY',
            embed_textures=False,
        )


def main():
    rocket = load_source()
    removed = replace_engine(rocket)
    glass, nose, fins = dress_surfaces(rocket)
    bevel_edges(rocket)
    emissive = relight(rocket)
    export(rocket)

    mesh = rocket.data
    triangles = sum(len(polygon.vertices) - 2 for polygon in mesh.polygons)
    print("HeroRocket refined: removed {0} old engine verts, dressed {1} glass / {2} nose "
          "/ {3} fin faces, kept {4} emissive faces, {5} verts / {6} tris, bounds {7}".format(
              removed, glass, nose, fins, emissive,
              len(mesh.vertices), triangles,
              tuple(round(value, 4) for value in rocket.dimensions)))


if __name__ == "__main__":
    main()
