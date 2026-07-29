"""Bakes the premium space-environment art for tap-or-crash.

Every sprite here is rendered by Cycles from a procedural shader or from real
low-poly geometry, then written straight into Assets/Resources/Space as a
straight-alpha PNG. White-ish and unsaturated on purpose: the Unity layers tint
each sprite per theme, so one texture has to serve Natural, Ice and Lava.

Run headless:  blender.exe -b -P bake_space.py
"""

import math
import os
import random
import sys

import bpy
from mathutils import Vector

OUT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new\Assets\Resources\Space"


# ── scene helpers ────────────────────────────────────────────────────────────

def fresh_scene(resolution, samples):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = samples
    scene.cycles.use_denoising = False
    scene.cycles.max_bounces = 2
    scene.cycles.transparent_max_bounces = 8

    scene.render.resolution_x = resolution
    scene.render.resolution_y = resolution
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.render.filter_size = 1.2

    # Authored colours must survive the render untouched: Filmic/AgX would crush
    # the bright cores and desaturate exactly the hues the themes rely on.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    return scene


def ortho_camera(scene, size, location=(0, 0, 4)):
    data = bpy.data.cameras.new("Cam")
    data.type = "ORTHO"
    data.ortho_scale = size
    cam = bpy.data.objects.new("Cam", data)
    cam.location = location
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def render(scene, name):
    path = os.path.join(OUT, name + ".png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("BAKED", path)


# ── shader helpers ───────────────────────────────────────────────────────────

def cloud_plane(name, size=2.0):
    bpy.ops.mesh.primitive_plane_add(size=size, location=(0, 0, 0))
    plane = bpy.context.object
    plane.name = name

    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.node_tree.nodes.clear()
    plane.data.materials.append(material)
    return plane, material.node_tree


def link(tree, a, sa, b, sb):
    tree.links.new(a.outputs[sa], b.inputs[sb])


def math_node(tree, op, value=None, x=0, y=0):
    node = tree.nodes.new("ShaderNodeMath")
    node.operation = op
    node.location = (x, y)
    if value is not None:
        node.inputs[1].default_value = value
    return node


def noise(tree, scale, detail, roughness, mapping_scale, x=0, y=0, seed=0.0):
    """Generated-coordinate noise with an independent per-call slice (W)."""
    coord = tree.nodes.new("ShaderNodeTexCoord")
    coord.location = (x - 600, y)

    mapping = tree.nodes.new("ShaderNodeMapping")
    mapping.location = (x - 400, y)
    mapping.inputs["Scale"].default_value = mapping_scale
    mapping.inputs["Location"].default_value = (seed * 3.1, seed * 1.7, seed * 5.3)

    tex = tree.nodes.new("ShaderNodeTexNoise")
    tex.location = (x - 200, y)
    tex.noise_dimensions = "4D"
    tex.inputs["W"].default_value = seed
    tex.inputs["Scale"].default_value = scale
    tex.inputs["Detail"].default_value = detail
    tex.inputs["Roughness"].default_value = roughness

    link(tree, coord, "Generated", mapping, "Vector")
    link(tree, mapping, "Vector", tex, "Vector")
    return tex


def radial_falloff(tree, stretch=(1.0, 1.0, 0.0), inner=0.0, outer=0.5, x=0, y=0):
    """1 in the middle of the quad, 0 past `outer` (in Generated units)."""
    coord = tree.nodes.new("ShaderNodeTexCoord")
    coord.location = (x - 800, y)

    centre = tree.nodes.new("ShaderNodeVectorMath")
    centre.operation = "SUBTRACT"
    centre.location = (x - 620, y)
    centre.inputs[1].default_value = (0.5, 0.5, 0.5)

    squash = tree.nodes.new("ShaderNodeVectorMath")
    squash.operation = "MULTIPLY"
    squash.location = (x - 440, y)
    squash.inputs[1].default_value = stretch

    length = tree.nodes.new("ShaderNodeVectorMath")
    length.operation = "LENGTH"
    length.location = (x - 260, y)

    ramp = tree.nodes.new("ShaderNodeMapRange")
    ramp.location = (x - 80, y)
    ramp.inputs["From Min"].default_value = outer
    ramp.inputs["From Max"].default_value = inner
    ramp.inputs["To Min"].default_value = 0.0
    ramp.inputs["To Max"].default_value = 1.0
    ramp.clamp = True

    link(tree, coord, "Generated", centre, "Vector")
    link(tree, centre, "Vector", squash, "Vector")
    link(tree, squash, "Vector", length, "Vector")
    link(tree, length, "Value", ramp, "Value")
    return ramp


def emit_with_alpha(tree, colour_source, colour_socket, alpha_node, alpha_socket, strength=1.0):
    emission = tree.nodes.new("ShaderNodeEmission")
    emission.location = (600, 200)
    emission.inputs["Strength"].default_value = strength

    transparent = tree.nodes.new("ShaderNodeBsdfTransparent")
    transparent.location = (600, 0)

    mix = tree.nodes.new("ShaderNodeMixShader")
    mix.location = (800, 100)

    out = tree.nodes.new("ShaderNodeOutputMaterial")
    out.location = (1000, 100)

    link(tree, colour_source, colour_socket, emission, "Color")
    link(tree, alpha_node, alpha_socket, mix, "Fac")
    link(tree, transparent, "BSDF", mix, 1)
    link(tree, emission, "Emission", mix, 2)
    link(tree, mix, "Shader", out, "Surface")


def ramp(tree, stops, x=0, y=0):
    node = tree.nodes.new("ShaderNodeValToRGB")
    node.location = (x, y)
    elements = node.color_ramp.elements
    while len(elements) > 1:
        elements.remove(elements[-1])
    elements[0].position = stops[0][0]
    elements[0].color = stops[0][1]
    for position, colour in stops[1:]:
        element = elements.new(position)
        element.color = colour
    return node


# ── 1. billowing nebula ──────────────────────────────────────────────────────

def bake_nebula_soft():
    scene = fresh_scene(512, 1)
    ortho_camera(scene, 2.0)
    _, tree = cloud_plane("NebulaSoft")

    # Two octave sets: the coarse one carves the silhouette, the fine one gives
    # the edge its fray. One alone reads either as a blob or as static.
    coarse = noise(tree, 2.6, 6.0, 0.52, (1.0, 1.0, 1.0), x=-200, y=400, seed=1.3)
    fine = noise(tree, 7.5, 12.0, 0.66, (1.0, 1.0, 1.0), x=-200, y=0, seed=4.7)

    blend = math_node(tree, "MULTIPLY", 0.62, 60, 400)
    fine_scaled = math_node(tree, "MULTIPLY", 0.38, 60, 200)
    density = math_node(tree, "ADD", None, 240, 300)

    link(tree, coarse, "Fac", blend, 0)
    link(tree, fine, "Fac", fine_scaled, 0)
    link(tree, blend, "Value", density, 0)
    link(tree, fine_scaled, "Value", density, 1)

    # Push the midtones apart so the cloud has cores and holes, not a grey haze.
    contrast = tree.nodes.new("ShaderNodeMapRange")
    contrast.location = (400, 300)
    contrast.inputs["From Min"].default_value = 0.30
    contrast.inputs["From Max"].default_value = 0.72
    contrast.clamp = True
    link(tree, density, "Value", contrast, "Value")

    falloff = radial_falloff(tree, (1.0, 1.25, 0.0), 0.06, 0.50, x=280, y=-400)

    # A bare radial falloff gives the cloud a visible oval outline. Eating into it
    # with a low-frequency mask is what breaks the silhouette back up.
    mask = noise(tree, 1.5, 3.0, 0.5, (1.0, 1.0, 1.0), x=280, y=-800, seed=8.2)
    mask_lift = tree.nodes.new("ShaderNodeMapRange")
    mask_lift.location = (400, -800)
    mask_lift.inputs["From Min"].default_value = 0.32
    mask_lift.inputs["From Max"].default_value = 0.68
    mask_lift.inputs["To Min"].default_value = 0.18
    mask_lift.inputs["To Max"].default_value = 1.0
    mask_lift.clamp = True
    link(tree, mask, "Fac", mask_lift, "Value")

    shaped = math_node(tree, "POWER", 1.8, 420, -400)
    link(tree, falloff, "Result", shaped, 0)

    masked = math_node(tree, "MULTIPLY", None, 560, -600)
    link(tree, shaped, "Value", masked, 0)
    link(tree, mask_lift, "Result", masked, 1)

    alpha = math_node(tree, "MULTIPLY", None, 700, -200)
    link(tree, contrast, "Result", alpha, 0)
    link(tree, masked, "Value", alpha, 1)

    gamma = math_node(tree, "POWER", 0.7, 840, -200)
    link(tree, alpha, "Value", gamma, 0)

    colours = ramp(tree, [
        (0.00, (0.28, 0.34, 0.62, 1.0)),   # cold rim
        (0.45, (0.52, 0.48, 0.86, 1.0)),   # body
        (0.78, (0.86, 0.84, 1.00, 1.0)),
        (1.00, (1.00, 0.98, 1.00, 1.0)),   # lit core
    ], x=400, y=600)
    link(tree, contrast, "Result", colours, "Fac")

    emit_with_alpha(tree, colours, "Color", gamma, "Value")
    render(scene, "nebula_soft")


# ── 2. wispy nebula strands ──────────────────────────────────────────────────

def bake_nebula_wisp():
    scene = fresh_scene(512, 1)
    ortho_camera(scene, 2.0)
    _, tree = cloud_plane("NebulaWisp")

    # Squashing the sample space on one axis is what turns a cloud into strands.
    strands = noise(tree, 3.2, 14.0, 0.72, (0.55, 5.2, 1.0), x=-200, y=300, seed=2.9)

    contrast = tree.nodes.new("ShaderNodeMapRange")
    contrast.location = (200, 300)
    contrast.inputs["From Min"].default_value = 0.40
    contrast.inputs["From Max"].default_value = 0.63
    contrast.clamp = True
    link(tree, strands, "Fac", contrast, "Value")

    falloff = radial_falloff(tree, (1.0, 1.6, 0.0), 0.0, 0.49, x=200, y=-400)

    alpha = math_node(tree, "MULTIPLY", None, 420, -200)
    link(tree, contrast, "Result", alpha, 0)
    link(tree, falloff, "Result", alpha, 1)

    gamma = math_node(tree, "POWER", 0.82, 560, -200)
    link(tree, alpha, "Value", gamma, 0)

    colours = ramp(tree, [
        (0.00, (0.30, 0.42, 0.70, 1.0)),
        (0.55, (0.66, 0.60, 0.92, 1.0)),
        (1.00, (0.96, 0.95, 1.00, 1.0)),
    ], x=200, y=600)
    link(tree, contrast, "Result", colours, "Fac")

    emit_with_alpha(tree, colours, "Color", gamma, "Value")
    render(scene, "nebula_wisp")


# ── 3. distant galaxy band ───────────────────────────────────────────────────

def bake_galaxy_band():
    scene = fresh_scene(512, 1)
    ortho_camera(scene, 2.0)
    _, tree = cloud_plane("GalaxyBand")

    # A hard-squashed falloff gives the disc; fine noise scattered over it reads
    # as unresolved stars rather than as cloud.
    disc = radial_falloff(tree, (1.0, 4.4, 0.0), 0.0, 0.5, x=-200, y=200)
    core = radial_falloff(tree, (1.0, 2.2, 0.0), 0.0, 0.26, x=-200, y=-300)

    grain = noise(tree, 26.0, 8.0, 0.5, (1.0, 0.6, 1.0), x=-200, y=700, seed=6.1)
    grain_lift = tree.nodes.new("ShaderNodeMapRange")
    grain_lift.location = (60, 700)
    grain_lift.inputs["From Min"].default_value = 0.34
    grain_lift.inputs["From Max"].default_value = 0.78
    grain_lift.inputs["To Min"].default_value = 0.55
    grain_lift.inputs["To Max"].default_value = 1.0
    grain_lift.clamp = True
    link(tree, grain, "Fac", grain_lift, "Value")

    dusty = math_node(tree, "MULTIPLY", None, 260, 300)
    link(tree, disc, "Result", dusty, 0)
    link(tree, grain_lift, "Result", dusty, 1)

    bulge = math_node(tree, "MULTIPLY", 0.7, 60, -300)
    link(tree, core, "Result", bulge, 0)

    total = math_node(tree, "ADD", None, 420, 0)
    link(tree, dusty, "Value", total, 0)
    link(tree, bulge, "Value", total, 1)

    clamped = math_node(tree, "MINIMUM", 1.0, 560, 0)
    link(tree, total, "Value", clamped, 0)

    alpha = math_node(tree, "POWER", 1.05, 700, -200)
    link(tree, clamped, "Value", alpha, 0)

    colours = ramp(tree, [
        (0.00, (0.34, 0.40, 0.72, 1.0)),
        (0.50, (0.74, 0.72, 0.92, 1.0)),
        (0.85, (1.00, 0.96, 0.88, 1.0)),   # warm galactic core
        (1.00, (1.00, 1.00, 0.98, 1.0)),
    ], x=560, y=600)
    link(tree, clamped, "Value", colours, "Fac")

    emit_with_alpha(tree, colours, "Color", alpha, "Value")
    render(scene, "galaxy_band")


# ── 4. cosmic dust patch ─────────────────────────────────────────────────────

def bake_dust_patch():
    scene = fresh_scene(256, 1)
    ortho_camera(scene, 2.0)
    _, tree = cloud_plane("DustPatch")

    speck = tree.nodes.new("ShaderNodeTexVoronoi")
    speck.location = (-200, 300)
    speck.feature = "F1"
    speck.inputs["Scale"].default_value = 24.0

    coord = tree.nodes.new("ShaderNodeTexCoord")
    coord.location = (-500, 300)
    link(tree, coord, "Generated", speck, "Vector")

    # Invert: Voronoi distance is 0 at each cell centre, so 1-d puts a dot there.
    dots = tree.nodes.new("ShaderNodeMapRange")
    dots.location = (40, 300)
    dots.inputs["From Min"].default_value = 0.28
    dots.inputs["From Max"].default_value = 0.0
    dots.clamp = True
    link(tree, speck, "Distance", dots, "Value")

    falloff = radial_falloff(tree, (1.0, 1.0, 0.0), 0.0, 0.5, x=40, y=-300)

    alpha = math_node(tree, "MULTIPLY", None, 300, 0)
    link(tree, dots, "Result", alpha, 0)
    link(tree, falloff, "Result", alpha, 1)

    faint = math_node(tree, "MULTIPLY", 0.9, 440, 0)
    link(tree, alpha, "Value", faint, 0)

    colours = ramp(tree, [
        (0.0, (0.70, 0.76, 0.95, 1.0)),
        (1.0, (1.00, 1.00, 1.00, 1.0)),
    ], x=300, y=500)
    link(tree, dots, "Result", colours, "Fac")

    emit_with_alpha(tree, colours, "Color", faint, "Value")
    render(scene, "dust_patch")


# ── 4b. star fields ──────────────────────────────────────────────────────────

def bake_starfields():
    """Whole patches of distant stars in one texture.

    Ninety individual star quads cost fifty-odd draw calls on a phone; six of
    these cost six. Each variant gets its own seed so the layer can overlap them
    at random rotations without the repeat ever showing.
    """
    for index, (seed, coarse_scale, fine_scale) in enumerate((
            (0.0, 13.0, 38.0),
            (7.0, 17.0, 46.0),
            (19.0, 11.0, 33.0))):
        scene = fresh_scene(512, 1)
        ortho_camera(scene, 2.0)
        _, tree = cloud_plane("StarField%d" % index)

        coord = tree.nodes.new("ShaderNodeTexCoord")
        coord.location = (-900, 300)

        offset = tree.nodes.new("ShaderNodeMapping")
        offset.location = (-720, 300)
        offset.inputs["Location"].default_value = (seed * 0.7, seed * 1.3, seed * 0.4)

        link(tree, coord, "Generated", offset, "Vector")

        # Two dot sizes. The bright sparse ones read as stars; the fine dense ones
        # are what keeps the gaps between them from looking empty.
        bright = tree.nodes.new("ShaderNodeTexVoronoi")
        bright.location = (-520, 500)
        bright.feature = "F1"
        bright.inputs["Scale"].default_value = coarse_scale

        faint = tree.nodes.new("ShaderNodeTexVoronoi")
        faint.location = (-520, 100)
        faint.feature = "F1"
        faint.inputs["Scale"].default_value = fine_scale

        link(tree, offset, "Vector", bright, "Vector")
        link(tree, offset, "Vector", faint, "Vector")

        bright_dots = tree.nodes.new("ShaderNodeMapRange")
        bright_dots.location = (-300, 500)
        bright_dots.inputs["From Min"].default_value = 0.10
        bright_dots.inputs["From Max"].default_value = 0.0
        bright_dots.clamp = True
        link(tree, bright, "Distance", bright_dots, "Value")

        faint_dots = tree.nodes.new("ShaderNodeMapRange")
        faint_dots.location = (-300, 100)
        faint_dots.inputs["From Min"].default_value = 0.05
        faint_dots.inputs["From Max"].default_value = 0.0
        faint_dots.inputs["To Max"].default_value = 0.55
        faint_dots.clamp = True
        link(tree, faint, "Distance", faint_dots, "Value")

        # Not every cell gets a star: a noise mask thins the grid out so the
        # spacing stops looking regular.
        thin = noise(tree, 9.0, 3.0, 0.5, (1.0, 1.0, 1.0), x=-300, y=-350, seed=seed + 2.5)
        thin_lift = tree.nodes.new("ShaderNodeMapRange")
        thin_lift.location = (-120, -350)
        thin_lift.inputs["From Min"].default_value = 0.42
        thin_lift.inputs["From Max"].default_value = 0.60
        thin_lift.inputs["To Min"].default_value = 0.10
        thin_lift.inputs["To Max"].default_value = 1.0
        thin_lift.clamp = True
        link(tree, thin, "Fac", thin_lift, "Value")

        combined = math_node(tree, "MAXIMUM", None, -80, 300)
        link(tree, bright_dots, "Result", combined, 0)
        link(tree, faint_dots, "Result", combined, 1)

        thinned = math_node(tree, "MULTIPLY", None, 100, 300)
        link(tree, combined, "Value", thinned, 0)
        link(tree, thin_lift, "Result", thinned, 1)

        # Soft edges so overlapping patches never show a seam.
        falloff = radial_falloff(tree, (1.0, 1.0, 0.0), 0.24, 0.5, x=100, y=-700)

        alpha = math_node(tree, "MULTIPLY", None, 320, 0)
        link(tree, thinned, "Value", alpha, 0)
        link(tree, falloff, "Result", alpha, 1)

        colours = ramp(tree, [
            (0.00, (0.72, 0.80, 1.00, 1.0)),
            (0.55, (0.94, 0.96, 1.00, 1.0)),
            (1.00, (1.00, 1.00, 1.00, 1.0)),
        ], x=320, y=500)
        link(tree, thinned, "Value", colours, "Fac")

        emit_with_alpha(tree, colours, "Color", alpha, "Value")
        render(scene, "starfield_%d" % index)


# ── 5. low-poly rocks ────────────────────────────────────────────────────────

def rock_mesh(name, seed, radius=1.0, subdivisions=2):
    """Ico-sphere pushed around per vertex, flat shaded. ~80 faces."""
    rng = random.Random(seed)

    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=radius)
    rock = bpy.context.object
    rock.name = name

    for vertex in rock.data.vertices:
        direction = vertex.co.normalized()
        # Two frequency bands: broad lobes plus a per-vertex chip.
        lobe = 1.0 + 0.26 * math.sin(direction.x * 2.3 + seed) * math.cos(direction.y * 2.7 - seed)
        chip = rng.uniform(-0.11, 0.11)
        vertex.co = direction * (radius * lobe + chip)

    for polygon in rock.data.polygons:
        polygon.use_smooth = False

    rock.scale = (rng.uniform(0.85, 1.15), rng.uniform(0.8, 1.1), rng.uniform(0.85, 1.1))
    rock.rotation_euler = (rng.uniform(0, 6.28), rng.uniform(0, 6.28), rng.uniform(0, 6.28))
    return rock


def rock_material():
    material = bpy.data.materials.new("Rock")
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    # Near-white and matte: the Unity layer tints it per theme, and any baked
    # colour of its own would fight that tint.
    bsdf.inputs["Base Color"].default_value = (0.80, 0.80, 0.84, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.92
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.16
    return material


def rock_lighting(scene):
    # Key from the upper left, matching the menu stage light, plus a cool fill so
    # the shadow side stays readable instead of going to pure black.
    key_data = bpy.data.lights.new("Key", type="SUN")
    key_data.energy = 1.55
    key_data.angle = 0.35
    key = bpy.data.objects.new("Key", key_data)
    key.rotation_euler = (math.radians(52), math.radians(-16), math.radians(-38))
    scene.collection.objects.link(key)

    fill_data = bpy.data.lights.new("Fill", type="SUN")
    fill_data.energy = 0.55
    fill_data.color = (0.62, 0.72, 1.0)
    fill = bpy.data.objects.new("Fill", fill_data)
    fill.rotation_euler = (math.radians(-38), math.radians(24), math.radians(140))
    scene.collection.objects.link(fill)


def bake_rocks():
    for index, seed in enumerate((11, 27, 43)):
        scene = fresh_scene(256, 48)
        ortho_camera(scene, 2.4)
        rock_lighting(scene)

        rock = rock_mesh("Rock", seed)
        rock.data.materials.append(rock_material())

        render(scene, "asteroid_%d" % (index + 3))


def bake_rock_cluster():
    """One sprite that already contains a clump of rocks: a whole asteroid field
    for the price of a single quad."""
    scene = fresh_scene(384, 48)
    ortho_camera(scene, 2.9)
    rock_lighting(scene)

    material = rock_material()
    rng = random.Random(97)
    placements = [
        (0.0, 0.0, 0.60),
        (-0.85, 0.42, 0.34),
        (0.78, 0.30, 0.30),
        (0.34, -0.72, 0.26),
        (-0.55, -0.66, 0.22),
        (1.05, -0.28, 0.16),
        (-1.12, -0.10, 0.14),
    ]
    for index, (x, y, scale) in enumerate(placements):
        rock = rock_mesh("Rock%d" % index, 200 + index * 13, radius=scale, subdivisions=1)
        rock.location = (x + rng.uniform(-0.06, 0.06), y + rng.uniform(-0.06, 0.06), rng.uniform(-0.2, 0.2))
        rock.data.materials.append(material)

    render(scene, "asteroid_cluster")


# ── entry point ──────────────────────────────────────────────────────────────

def main():
    os.makedirs(OUT, exist_ok=True)
    only = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

    jobs = {
        "nebula_soft": bake_nebula_soft,
        "nebula_wisp": bake_nebula_wisp,
        "galaxy_band": bake_galaxy_band,
        "dust_patch": bake_dust_patch,
        "starfields": bake_starfields,
        "rocks": bake_rocks,
        "cluster": bake_rock_cluster,
    }
    for name, job in jobs.items():
        if not only or name in only:
            job()

    print("ALL BAKES DONE")


main()
