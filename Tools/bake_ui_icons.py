"""Bakes the Tap or Crash UI icon family.

Every icon is real 3D, built from the same low-poly vocabulary as the hero
rocket and lit by the same key/rim/fill/bounce rig as the brand emblem, so a
shop bag and a sound horn read as objects from one world rather than clipart
from three. One stage, one camera, one tilt, one bevel language: that shared
setup is the whole point — it is what makes the set look designed instead of
collected.

Icons that the UI tints per world (help, sound, shop, settings, pause, close)
are baked near-white so Unity's Image.color multiply lands cleanly. Icons that
own a fixed brand colour (coin, star) are baked in that colour.

Run headless:  blender.exe -b -P bake_ui_icons.py
Only some:     blender.exe -b -P bake_ui_icons.py -- coin moon
Fast preview:  blender.exe -b -P bake_ui_icons.py -- preview
"""

import math
import os
import sys

import bpy
from mathutils import Vector

ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
OUT = os.path.join(ROOT, "Assets", "Resources", "Icons")
FONT_PATH = os.path.join(ROOT, "Assets", "Fonts", "Fredoka-Bold.ttf")

# ── stage ────────────────────────────────────────────────────────────────────
# One camera and one tilt for every icon in the family. Icons are modelled to
# live inside a 1.70-unit square so the 2.10 ortho window leaves even padding —
# that constant padding is what keeps optical weight consistent when the same
# 116px disc holds a wide shop bag and a narrow "?".

RES = 256
ORTHO = 2.10
ICON_TILT = math.radians(-9.0)   # matches the emblem lockup: makes bevels read

PREVIEW = False

NEUTRAL = (0.900, 0.930, 0.980)  # tintable icons
GOLD = (1.000, 0.700, 0.140)
MOONLIGHT = (0.870, 0.905, 1.000)


def fresh_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene

    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 24 if PREVIEW else 200
    scene.cycles.use_adaptive_sampling = True
    scene.cycles.adaptive_threshold = 0.008
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 4
    scene.cycles.transparent_max_bounces = 8

    scene.render.resolution_x = RES
    scene.render.resolution_y = RES
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.filter_size = 1.35
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"

    # Linear project, unlit sprites: anything but Standard shifts the icon set
    # away from the emblem it has to sit next to.
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"

    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0, 0, 0, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.0
    scene.world = world

    data = bpy.data.cameras.new("Cam")
    data.type = "ORTHO"
    data.ortho_scale = ORTHO
    cam = bpy.data.objects.new("Cam", data)
    cam.location = (0.0, 0.0, 6.0)
    scene.collection.objects.link(cam)
    scene.camera = cam

    lighting(scene)
    return scene


def area_light(scene, name, location, colour, energy, size):
    data = bpy.data.lights.new(name, type="AREA")
    data.energy = energy
    data.color = colour
    data.shape = "DISK"
    data.size = size
    light = bpy.data.objects.new(name, data)
    light.location = location
    light.rotation_euler = (Vector((0.0, 0.0, 0.0)) - Vector(location)) \
        .to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(light)


def lighting(scene):
    # Same four-light logic as the emblem, scaled to an icon-sized subject:
    # warm key upper-left, cool rim lower-right to carve the bevels, dim sky
    # fill so the shadow side is not black, and a warm bounce from below.
    area_light(scene, "Key", (-2.2, 1.9, 3.0), (1.00, 0.955, 0.875), 190.0, 2.6)
    area_light(scene, "Rim", (2.6, -1.7, 1.2), (0.38, 0.66, 1.00), 78.0, 1.9)
    area_light(scene, "Fill", (0.2, 3.0, 2.4), (0.62, 0.74, 1.00), 32.0, 5.0)
    area_light(scene, "Bounce", (-0.5, -2.2, 2.4), (1.00, 0.88, 0.74), 26.0, 4.2)


# ── materials ────────────────────────────────────────────────────────────────

def icon_material(name, colour, roughness=0.36, metallic=0.0, coat=0.14):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    bsdf = material.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (colour[0], colour[1], colour[2], 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in bsdf.inputs:
        bsdf.inputs["Specular IOR Level"].default_value = 0.55
    if "Coat Weight" in bsdf.inputs:
        bsdf.inputs["Coat Weight"].default_value = coat
        bsdf.inputs["Coat Roughness"].default_value = 0.26
    return material


# ── geometry helpers ─────────────────────────────────────────────────────────

def activate(obj):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def finish(obj, material, bevel=0.0, segments=3, smooth=True):
    """Bevel + shade + assign, the three things every icon part needs."""
    if bevel > 0.0:
        modifier = obj.modifiers.new("Bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = segments
        modifier.limit_method = "ANGLE"
        modifier.angle_limit = math.radians(35.0)
        modifier.harden_normals = False
        activate(obj)
        bpy.ops.object.modifier_apply(modifier=modifier.name)

    activate(obj)
    if smooth:
        # 4.1+ replaced mesh auto-smooth with an operator-driven modifier.
        try:
            bpy.ops.object.shade_auto_smooth(angle=math.radians(38.0))
        except (AttributeError, RuntimeError):
            bpy.ops.object.shade_smooth()
    else:
        bpy.ops.object.shade_flat()

    obj.data.materials.append(material)
    return obj


def box(size, location=(0, 0, 0), rotation=(0, 0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location, rotation=rotation)
    obj = bpy.context.active_object
    obj.scale = size
    activate(obj)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def cylinder(radius, depth, location=(0, 0, 0), rotation=(0, 0, 0), verts=48):
    bpy.ops.mesh.primitive_cylinder_add(radius=radius, depth=depth, vertices=verts,
                                        location=location, rotation=rotation)
    return bpy.context.active_object


def cone(radius1, radius2, depth, location=(0, 0, 0), rotation=(0, 0, 0), verts=40):
    bpy.ops.mesh.primitive_cone_add(radius1=radius1, radius2=radius2, depth=depth,
                                    vertices=verts, location=location, rotation=rotation)
    return bpy.context.active_object


def sphere(radius, location=(0, 0, 0), subdivisions=3):
    bpy.ops.mesh.primitive_ico_sphere_add(radius=radius, subdivisions=subdivisions,
                                          location=location)
    return bpy.context.active_object


def extruded_polygon(points, depth, location=(0, 0, 0)):
    """A flat n-gon in the camera plane, given thickness by Solidify.

    Stars and sparkles are drawn shapes, not assemblies of primitives: building
    them from their real outline is what keeps the points crisp and symmetrical
    instead of lumpy where blades overlap.
    """
    mesh = bpy.data.meshes.new("Poly")
    mesh.from_pydata([(x, y, 0.0) for x, y in points], [], [list(range(len(points)))])
    mesh.update()

    obj = bpy.data.objects.new("Poly", mesh)
    obj.location = location
    bpy.context.scene.collection.objects.link(obj)

    modifier = obj.modifiers.new("Solidify", "SOLIDIFY")
    modifier.thickness = depth
    modifier.offset = 0.0
    activate(obj)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    return obj


def star_points(outer, inner, count=5, phase=90.0):
    points = []
    for index in range(count * 2):
        radius = outer if index % 2 == 0 else inner
        angle = math.radians(phase + 180.0 * index / count)
        points.append((radius * math.cos(angle), radius * math.sin(angle)))
    return points


def arc(radius, tube, start_deg, end_deg, location=(0, 0, 0), rotation=(0, 0, 0),
        segments=48):
    """Open tube along a circular arc — the only honest way to draw a sound wave."""
    curve = bpy.data.curves.new("Arc", type="CURVE")
    curve.dimensions = "3D"
    curve.bevel_depth = tube
    curve.bevel_resolution = 5
    curve.use_fill_caps = True

    spline = curve.splines.new("POLY")
    spline.points.add(segments - 1)
    for index in range(segments):
        angle = math.radians(start_deg + (end_deg - start_deg) * index / (segments - 1))
        spline.points[index].co = (radius * math.cos(angle), radius * math.sin(angle), 0.0, 1.0)

    obj = bpy.data.objects.new("Arc", curve)
    obj.location = location
    obj.rotation_euler = rotation
    bpy.context.scene.collection.objects.link(obj)
    return obj


def weld(target, tool):
    """Boolean union. Parts that merely overlap get bevelled individually and
    read as separate objects stuck together; welding first gives one solid."""
    modifier = target.modifiers.new("Weld", "BOOLEAN")
    modifier.operation = "UNION"
    modifier.object = tool
    modifier.solver = "EXACT"
    activate(target)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.data.objects.remove(tool, do_unlink=True)
    return target


def cut(target, tool):
    """Boolean difference, then bin the tool. Real holes, real alpha."""
    modifier = target.modifiers.new("Cut", "BOOLEAN")
    modifier.operation = "DIFFERENCE"
    modifier.object = tool
    modifier.solver = "EXACT"
    activate(target)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.data.objects.remove(tool, do_unlink=True)
    return target


def glyph(char, cap_height, extrude=0.085, bevel=0.022):
    curve = bpy.data.curves.new(type="FONT", name="glyph")
    curve.body = char
    curve.font = bpy.data.fonts.load(FONT_PATH)
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.extrude = extrude
    curve.bevel_depth = bevel
    curve.bevel_resolution = 4
    curve.resolution_u = 12
    curve.size = 1.0

    obj = bpy.data.objects.new("Glyph_" + char, curve)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.update()

    # Scale by measured ink height so "?" and "!" get the same optical weight
    # regardless of what the font thinks their em box is.
    depsgraph = bpy.context.evaluated_depsgraph_get()
    mesh = obj.evaluated_get(depsgraph).to_mesh()
    ys = [v.co.y for v in mesh.vertices]
    natural = (max(ys) - min(ys)) if ys else 1.0
    obj.evaluated_get(depsgraph).to_mesh_clear()

    obj.data.size = cap_height / natural
    bpy.context.view_layer.update()
    return obj


# ── icons ────────────────────────────────────────────────────────────────────

def icon_coin():
    gold = icon_material("Coin", GOLD, roughness=0.24, metallic=0.55, coat=0.22)
    rim_gold = icon_material("CoinRim", (1.0, 0.80, 0.30), roughness=0.30, metallic=0.45)

    parts = []
    # Struck-coin construction: a thick blank, a proud inner field, an embossed
    # sparkle. Three depths is all it takes to stop reading as a yellow circle.
    parts.append(finish(cylinder(0.80, 0.20), gold, bevel=0.045, segments=4))
    parts.append(finish(cylinder(0.63, 0.24), rim_gold, bevel=0.030, segments=4))
    parts.append(finish(extruded_polygon(star_points(0.42, 0.115, count=4),
                                         0.09, location=(0, 0, 0.14)),
                        gold, bevel=0.022, segments=3))

    # Turned off-axis so the key light travels across the face instead of
    # flaring flat: a coin should catch light, not sit there.
    return parts, (math.radians(-8.0), math.radians(17.0), 0.0)


def icon_moon():
    pale = icon_material("Moon", MOONLIGHT, roughness=0.52, coat=0.10)
    body = sphere(0.86, subdivisions=3)
    # A cylinder bored along the view axis, not a second sphere: it cuts the
    # inner arc straight through, so the silhouette is an exact crescent and the
    # surface left facing camera is the sphere's own convex front. A sphere-cut
    # hollows the front instead and reads as a bitten ball.
    cut(body, cylinder(0.82, 4.0, location=(0.30, 0.0, 0.0), verts=64))
    # Flat shading on the icosphere: the same faceted language as the low-poly
    # planets, so the moon button belongs to the worlds it switches between.
    return [finish(body, pale, smooth=False)], (math.radians(-4.0), 0.0, math.radians(-14.0))


def _speaker(material):
    parts = []
    # Body + horn as one silhouette: a box that hides behind a cone reads as a
    # speaker at 40px, where a detailed driver reads as noise.
    parts.append(finish(box((0.34, 0.44, 0.30), location=(-0.60, 0, 0)),
                        material, bevel=0.055, segments=3))
    # +90 about Y maps the cone's local +Z (radius2) to +X, so radius2 is the
    # wide end and the horn opens toward the waves rather than into the body.
    parts.append(finish(cone(0.17, 0.60, 0.66, location=(-0.22, 0, 0),
                             rotation=(0, math.radians(90), 0)),
                        material, bevel=0.045, segments=3))
    return parts


def icon_sound_on():
    material = icon_material("SoundOn", NEUTRAL)
    parts = _speaker(material)
    # arc() already lays its points in XY, the camera plane. Rotating it would
    # stand the wave on edge and it would vanish to a sliver.
    for radius, tube in ((0.34, 0.062), (0.60, 0.058)):
        wave = arc(radius, tube, -48, 48, location=(0.24, 0, 0))
        wave.data.materials.append(material)
        parts.append(wave)
    return parts, (ICON_TILT, 0.0, 0.0)


def icon_sound_off():
    material = icon_material("SoundOff", NEUTRAL)
    parts = _speaker(material)
    for angle in (45.0, -45.0):
        parts.append(finish(box((0.66, 0.145, 0.145),
                                location=(0.44, 0, 0),
                                rotation=(0, 0, math.radians(angle))),
                            material, bevel=0.060, segments=3))
    return parts, (ICON_TILT, 0.0, 0.0)


def icon_help():
    material = icon_material("Help", NEUTRAL)
    mark = glyph("?", 1.32)
    mark.location = (0.0, 0.0, 0.0)
    mark.data.materials.append(material)
    activate(mark)
    try:
        bpy.ops.object.shade_auto_smooth(angle=math.radians(38.0))
    except (AttributeError, RuntimeError):
        pass
    return [mark], (ICON_TILT, 0.0, 0.0)


def icon_shop():
    material = icon_material("Shop", NEUTRAL)
    handle_material = icon_material("ShopHandle", (0.82, 0.87, 0.96), roughness=0.40)

    parts = []
    # A bag, not a cart: fewer strokes survive the drop to a 64px button, and
    # the soft box matches the rounded language of every other icon here.
    # Wider than tall, with a wide shallow handle. A tall body under a small
    # round handle is a padlock, which is exactly what the first pass read as.
    parts.append(finish(box((1.20, 0.88, 0.46), location=(0, -0.30, 0)),
                        material, bevel=0.115, segments=4))
    # The arc's feet have to end up *inside* the bag. At y=0.06 they stopped at
    # 0.24 while the body's top face is at 0.14, so the handle hovered a tenth of
    # a unit above the bag — at 58px on a disc that reads as two loose shapes.
    handle = arc(0.44, 0.060, 24, 156, location=(0, -0.13, 0))
    handle.data.materials.append(handle_material)
    parts.append(handle)
    # The seam under the handle is the one piece of detail worth its pixels: it
    # turns a rounded box into a bag.
    parts.append(finish(box((1.14, 0.055, 0.48), location=(0, 0.10, 0)),
                        handle_material, bevel=0.020, segments=2))
    return parts, (ICON_TILT, 0.0, 0.0)


def icon_settings():
    material = icon_material("Settings", NEUTRAL)
    # Everything stays in XY, the camera plane: the hub, the teeth and the bore
    # share one axis so the gear reads as one machined part.
    body = cylinder(0.58, 0.28, verts=48)
    for index in range(8):
        angle = math.pi * 2.0 * index / 8.0
        # Seated inside the hub radius so each tooth grows out of the disc.
        weld(body, box((0.30, 0.30, 0.28),
                       location=(math.cos(angle) * 0.55, math.sin(angle) * 0.55, 0.0),
                       rotation=(0, 0, angle)))

    cut(body, cylinder(0.235, 0.60, verts=36))
    return [finish(body, material, bevel=0.038, segments=3)], \
        (ICON_TILT, 0.0, math.radians(6.0))


def icon_pause():
    material = icon_material("Pause", NEUTRAL)
    return [finish(box((0.30, 1.06, 0.30), location=(x, 0, 0)), material,
                   bevel=0.085, segments=4)
            for x in (-0.31, 0.31)], (ICON_TILT, 0.0, 0.0)


def icon_star():
    material = icon_material("Star", GOLD, roughness=0.26, metallic=0.42, coat=0.20)
    # One extruded outline, not five overlapping blades: the points stay sharp
    # and identical, and the bevel runs cleanly around the whole silhouette.
    star = extruded_polygon(star_points(0.92, 0.40), 0.24)
    return [finish(star, material, bevel=0.055, segments=3)], \
        (math.radians(-7.0), math.radians(6.0), 0.0)


def icon_sun():
    material = icon_material("Sun", (1.000, 0.880, 0.560), roughness=0.34, coat=0.18)
    parts = [finish(sphere(0.52, subdivisions=3), material, smooth=False)]
    # Rays share the crescent's pale warmth and the same faceted read, so the two
    # states of one button feel like one object changing rather than two icons.
    for index in range(8):
        angle = math.pi * 2.0 * index / 8.0
        parts.append(finish(box((0.30, 0.115, 0.115),
                                location=(math.cos(angle) * 0.78, math.sin(angle) * 0.78, 0.0),
                                rotation=(0, 0, angle)),
                            material, bevel=0.045, segments=3))
    return parts, (ICON_TILT, 0.0, 0.0)


def icon_close():
    material = icon_material("Close", NEUTRAL)
    return [finish(box((1.10, 0.20, 0.22), rotation=(0, 0, math.radians(angle))),
                   material, bevel=0.075, segments=4)
            for angle in (45.0, -45.0)], (ICON_TILT, 0.0, 0.0)


ICONS = {
    "coin": icon_coin,
    "moon": icon_moon,
    "sound_on": icon_sound_on,
    "sound_off": icon_sound_off,
    "help": icon_help,
    "shop": icon_shop,
    "settings": icon_settings,
    "pause": icon_pause,
    "star": icon_star,
    "sun": icon_sun,
    "close": icon_close,
}


# ── bake ─────────────────────────────────────────────────────────────────────

def bake(name):
    scene = fresh_scene()
    parts, tilt = ICONS[name]()

    # Every icon is tilted as one rig, never per part: a shared tilt is what
    # makes ten separate models share a single light direction on screen.
    pivot = bpy.data.objects.new("Rig", None)
    scene.collection.objects.link(pivot)
    pivot.rotation_euler = tilt
    for part in parts:
        part.parent = pivot
        part.matrix_parent_inverse = pivot.matrix_world.inverted()

    bpy.context.view_layer.update()
    os.makedirs(OUT, exist_ok=True)
    scene.render.filepath = os.path.join(OUT, "icon_" + name + ".png")
    bpy.ops.render.render(write_still=True)
    print("BAKED", scene.render.filepath)


def main():
    global PREVIEW
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    PREVIEW = "preview" in args
    wanted = [a for a in args if a in ICONS] or list(ICONS)

    for name in wanted:
        bake(name)
    print("ALL ICON BAKES DONE")


main()
