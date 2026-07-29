"""Bakes the Tap or Crash brand emblem.

The wordmark is real 3D: every glyph is an extruded, bevelled curve placed by an
optical letterfitting pass (ink width + per-glyph side bearings), lit by a warm
key and a cool rim that match the menu stage, and wrapped by a genuine circular
orbit ring tilted ~70 degrees so it passes *behind* the upper line and comes
round in front below the lower one. Cycles renders it straight to a transparent
PNG that Unity shows as a world-space sprite inside the menu showcase stage.

Run headless:  blender.exe -b -P bake_emblem.py
Optional job:  ... -- emblem            (only the emblem, skips nothing today)
"""

import math
import os
import sys

import bpy
from mathutils import Vector

ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
OUT = os.path.join(ROOT, "Assets", "Resources", "Menu")
FONT_PATH = os.path.join(ROOT, "Assets", "Fonts", "Fredoka-Bold.ttf")

# ── composition ──────────────────────────────────────────────────────────────
# All distances are Blender units; the camera below turns them into the 1280x480
# sprite. Keep this block in sync with MenuBrandEmblem.cs, which re-derives the
# orbit ring from the same numbers so the live spark rides the baked path.

RES_X, RES_Y = 1280, 512
ORTHO = 4.60                     # visible width
CAM_Y = -0.020                   # sprite centre, i.e. the emblem's optical centre

# "TAP OR" and "CRASH" are both five glyphs, so one measure fits both lines and
# the phrase keeps a single voice instead of splitting into title-plus-tagline.
# The upper line then has to carry a word space as well, so it is set a touch
# smaller — just enough that both lines end up with the same letter rhythm.
# That 7% difference is typography doing the work, not an arbitrary hierarchy.
LINE_WIDTH = 2.792               # both lines are justified to this exact width
TOP_CAP = 0.5357
BOT_CAP = 0.5750
TOP_BASELINE = 0.177
BOT_BASELINE = -0.490

EXTRUDE = 0.055                  # half-depth: the slab is 0.11 thick
BEVEL = 0.019
LOCKUP_TILT = math.radians(-9.0)  # tips the tops away so the extrusion reads

RING_RADIUS = 1.990
RING_TILT = math.radians(66.3)   # cos(tilt) * radius = 0.80 visible half-height
RING_CENTRE_Y = -0.020
RING_TUBE = 0.0125

WORD_SPACE = 0.26                # space between "TAP" and "OR", in cap heights

# Optical side bearings. A display wordmark is fitted by eye, not by metrics:
# round shapes and diagonals need less air than flat stems or the pairs read
# as holes. These are the multipliers that buy that.
BEARING = {
    "T": (0.40, 0.40),
    "A": (0.34, 0.34),
    "P": (0.50, 0.42),
    "O": (0.42, 0.42),
    "R": (0.46, 0.44),
    "C": (0.42, 0.44),
    "S": (0.44, 0.40),
    "H": (0.50, 0.50),
}
DEFAULT_BEARING = (0.48, 0.48)


# ── scene ────────────────────────────────────────────────────────────────────

PREVIEW = False


def fresh_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24 if PREVIEW else 220
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = 0.008
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 4
    scene.cycles.transparent_max_bounces = 8

    scene.render.resolution_x = RES_X
    scene.render.resolution_y = RES_Y
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.filter_size = 1.35
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"

    # The project is Linear colour space and the sprite is drawn unlit: anything
    # but Standard would shift the brand white before Unity ever sees it.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"

    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0, 0, 0, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
    scene.world = world
    return scene


def camera(scene):
    data = bpy.data.cameras.new("Cam")
    data.type = "ORTHO"
    data.ortho_scale = ORTHO
    cam = bpy.data.objects.new("Cam", data)
    cam.location = (0.0, CAM_Y, 6.0)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def area_light(scene, name, location, colour, energy, size, spread=None):
    data = bpy.data.lights.new(name, type="AREA")
    data.energy = energy
    data.color = colour
    data.shape = "DISK"
    data.size = size
    if spread is not None:
        data.spread = spread
    light = bpy.data.objects.new(name, data)
    light.location = location
    direction = Vector((0.0, 0.0, 0.0)) - Vector(location)
    light.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(light)
    return light


def lighting(scene):
    # Key: warm, high and to the left — the same direction the menu stage's key
    # light hits the hero planet from, so the emblem belongs to that frame. Kept
    # deliberately close: the inverse-square falloff across four units of
    # wordmark is what stops a head-on ortho render from looking like flat fill.
    area_light(scene, "Key", (-2.8, 2.3, 3.0), (1.00, 0.955, 0.875), 250.0, 3.2)

    # Rim: the cool edge that makes the bevels read. Low and right, opposite the
    # key, saturated enough to tint the chamfers without touching the faces.
    area_light(scene, "Rim", (3.3, -2.2, 1.1), (0.38, 0.66, 1.00), 95.0, 2.2)

    # Sky fill: keeps the shadow side off pure black. Very dim on purpose.
    area_light(scene, "Fill", (0.3, 3.6, 2.6), (0.62, 0.74, 1.00), 40.0, 6.0)

    # Bounce: a whisper of warmth from below-front so the lower bevels are not
    # a dead line across the wordmark.
    area_light(scene, "Bounce", (-0.6, -2.6, 2.6), (1.00, 0.88, 0.74), 35.0, 5.0)


# ── materials ────────────────────────────────────────────────────────────────

def letter_material():
    material = bpy.data.materials.new("EmblemLetter")
    material.use_nodes = True
    tree = material.node_tree
    bsdf = tree.nodes["Principled BSDF"]

    bsdf.inputs["Base Color"].default_value = (0.945, 0.938, 0.925, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.0
    bsdf.inputs["Roughness"].default_value = 0.34
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.56
    if "Coat Weight" in bsdf.inputs:
        bsdf.inputs["Coat Weight"].default_value = 0.12
        bsdf.inputs["Coat Roughness"].default_value = 0.28

    # Crafted, not manufactured: a fine roughness break-up plus a bump so faint
    # it never reads as texture, only as a surface that is not perfectly flat.
    grain = tree.nodes.new("ShaderNodeTexNoise")
    grain.location = (-620, -120)
    grain.inputs["Scale"].default_value = 90.0
    grain.inputs["Detail"].default_value = 6.0
    grain.inputs["Roughness"].default_value = 0.55

    spread = tree.nodes.new("ShaderNodeMapRange")
    spread.location = (-400, -120)
    spread.inputs["From Min"].default_value = 0.35
    spread.inputs["From Max"].default_value = 0.65
    spread.inputs["To Min"].default_value = 0.285
    spread.inputs["To Max"].default_value = 0.405
    spread.clamp = True
    tree.links.new(grain.outputs["Fac"], spread.inputs["Value"])
    tree.links.new(spread.outputs["Result"], bsdf.inputs["Roughness"])

    fine = tree.nodes.new("ShaderNodeTexNoise")
    fine.location = (-620, -420)
    fine.inputs["Scale"].default_value = 260.0
    fine.inputs["Detail"].default_value = 3.0

    bump = tree.nodes.new("ShaderNodeBump")
    bump.location = (-400, -420)
    bump.inputs["Strength"].default_value = 0.05
    bump.inputs["Distance"].default_value = 0.004
    tree.links.new(fine.outputs["Fac"], bump.inputs["Height"])
    tree.links.new(bump.outputs["Normal"], bsdf.inputs["Normal"])

    return material


def ring_material():
    material = bpy.data.materials.new("EmblemRing")
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    # Pale blue metal: it should catch the key as a moving highlight along the
    # arc rather than sit there as a drawn line.
    # Part diffuse on purpose: a fully metallic ring reflects the empty black
    # world and the far arc drops out of the sprite entirely.
    bsdf.inputs["Base Color"].default_value = (0.80, 0.87, 1.00, 1.0)
    bsdf.inputs["Metallic"].default_value = 0.35
    bsdf.inputs["Roughness"].default_value = 0.26
    return material


# ── typography ───────────────────────────────────────────────────────────────

FONT = None


def make_glyph(char, size):
    curve = bpy.data.curves.new(type="FONT", name="glyph")
    curve.body = char
    curve.font = FONT
    curve.size = size
    curve.align_x = "LEFT"
    curve.align_y = "TOP_BASELINE"
    curve.extrude = EXTRUDE
    curve.bevel_depth = BEVEL
    curve.bevel_resolution = 4
    curve.resolution_u = 12

    obj = bpy.data.objects.new("G_" + char, curve)
    bpy.context.scene.collection.objects.link(obj)
    return obj


def ink(obj):
    """Local bounding box of the evaluated geometry (min x, max x, min y, max y)."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    if not mesh.vertices:
        evaluated.to_mesh_clear()
        return None
    xs = [v.co.x for v in mesh.vertices]
    ys = [v.co.y for v in mesh.vertices]
    bounds = (min(xs), max(xs), min(ys), max(ys))
    evaluated.to_mesh_clear()
    return bounds


def size_for_cap(cap_height):
    """Font size that renders a capital H exactly `cap_height` tall."""
    probe = make_glyph("H", 1.0)
    bpy.context.view_layer.update()
    bounds = ink(probe)
    natural = bounds[3] - bounds[2]
    bpy.data.objects.remove(probe, do_unlink=True)
    return cap_height / natural


def build_line(text, cap_height, baseline, material):
    """Sets one justified line and returns its glyph objects.

    Letters are fitted optically: the gap between two glyphs is their facing
    side bearings times a single spacing unit, and that unit is solved so the
    line lands on LINE_WIDTH exactly. Both lines therefore share one measure,
    which is what makes the pair read as one designed block.
    """
    size = size_for_cap(cap_height)

    glyphs = []
    for char in text:
        if char == " ":
            glyphs.append(None)
            continue
        obj = make_glyph(char, size)
        obj.data.materials.append(material)
        glyphs.append(obj)

    bpy.context.view_layer.update()

    bounds = [ink(g) if g is not None else None for g in glyphs]
    total_ink = sum(b[1] - b[0] for b in bounds if b is not None)

    factors = []
    for index in range(len(glyphs) - 1):
        if glyphs[index] is None or glyphs[index + 1] is None:
            factors.append(0.0)   # the space carries its own absolute gap
            continue
        right = BEARING.get(text[index], DEFAULT_BEARING)[1]
        left = BEARING.get(text[index + 1], DEFAULT_BEARING)[0]
        factors.append(right + left)

    # The word space is a fixed share of the cap height, not a share of whatever
    # tracking happens to be left over: solving it along with the letters is how
    # "TAP OR" collapsed into "TAPOR".
    word_space = WORD_SPACE * cap_height
    space_count = text.count(" ")
    unit = (LINE_WIDTH - total_ink - space_count * word_space) / sum(factors)

    cursor = -LINE_WIDTH * 0.5
    for index, obj in enumerate(glyphs):
        if obj is not None:
            obj.location.x = cursor - bounds[index][0]
            obj.location.y = baseline
            cursor += bounds[index][1] - bounds[index][0]
        if index < len(glyphs) - 1:
            if glyphs[index] is None or glyphs[index + 1] is None:
                cursor += word_space
            else:
                cursor += unit * factors[index]

    print("LINE %-8s cap=%.3f unit=%.4f tracking=%.0f%% of cap"
          % (text, cap_height, unit, 100.0 * unit / cap_height))
    return [g for g in glyphs if g is not None]


# ── orbit ring ───────────────────────────────────────────────────────────────

def build_ring(material, segments=256):
    curve = bpy.data.curves.new("Ring", type="CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = RING_TUBE
    curve.bevel_resolution = 6
    curve.use_fill_caps = True

    spline = curve.splines.new("POLY")
    spline.points.add(segments - 1)
    for index in range(segments):
        angle = 2.0 * math.pi * index / segments
        spline.points[index].co = (RING_RADIUS * math.cos(angle),
                                   RING_RADIUS * math.sin(angle), 0.0, 1.0)
    spline.use_cyclic_u = True

    obj = bpy.data.objects.new("OrbitRing", curve)
    obj.data.materials.append(material)
    # Negative tilt sends the far side of the orbit behind the wordmark and
    # brings the near side round in front, below it.
    obj.rotation_euler = (-RING_TILT, 0.0, 0.0)
    obj.location = (0.0, RING_CENTRE_Y, 0.0)
    bpy.context.scene.collection.objects.link(obj)
    return obj


# ── bake ─────────────────────────────────────────────────────────────────────

def bake_emblem():
    global FONT

    scene = fresh_scene()
    camera(scene)
    lighting(scene)

    FONT = bpy.data.fonts.load(FONT_PATH)
    letters = letter_material()

    lockup = bpy.data.objects.new("Lockup", None)
    scene.collection.objects.link(lockup)
    lockup.rotation_euler = (LOCKUP_TILT, 0.0, 0.0)

    glyphs = []
    glyphs += build_line("TAP OR", TOP_CAP, TOP_BASELINE, letters)
    glyphs += build_line("CRASH", BOT_CAP, BOT_BASELINE, letters)
    for glyph in glyphs:
        glyph.parent = lockup
        glyph.matrix_parent_inverse = lockup.matrix_world.inverted()

    build_ring(ring_material())

    bpy.context.view_layer.update()
    os.makedirs(OUT, exist_ok=True)
    scene.render.filepath = os.path.join(OUT, "brand_emblem.png")
    bpy.ops.render.render(write_still=True)
    print("BAKED", scene.render.filepath)


def main():
    global PREVIEW
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    PREVIEW = "preview" in only
    bake_emblem()
    print("ALL BAKES DONE")


main()
