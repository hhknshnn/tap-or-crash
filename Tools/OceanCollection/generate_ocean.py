import bpy
import math
import os
from mathutils import Vector, Matrix

ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
MODEL_DIR = os.path.join(ROOT, "Assets", "Models", "OceanPlanets")
SPRITE_DIR = os.path.join(ROOT, "Assets", "Sprites", "OceanPlanets")
os.makedirs(MODEL_DIR, exist_ok=True)
os.makedirs(SPRITE_DIR, exist_ok=True)

PALETTE = [
    (0.035, 0.30, 0.56, 1.0),  # deep ocean
    (0.03, 0.56, 0.76, 1.0),   # blue
    (0.04, 0.81, 0.83, 1.0),   # turquoise
    (0.34, 0.95, 0.92, 1.0),   # cyan highlight
    (0.92, 0.99, 1.00, 1.0),   # foam
    (0.99, 0.50, 0.55, 1.0),   # coral
    (1.00, 0.72, 0.42, 1.0),   # peach coral
    (0.98, 0.43, 0.75, 1.0),   # pink coral
    (0.10, 0.56, 0.39, 1.0),   # sea green
    (0.24, 0.81, 0.48, 1.0),   # tropical green
    (0.98, 0.84, 0.45, 1.0),   # sand
    (0.68, 0.82, 0.81, 1.0),   # pale stone
    (0.24, 0.43, 0.48, 1.0),   # dark stone
    (0.52, 0.29, 0.16, 1.0),   # driftwood
    (0.68, 0.95, 1.00, 1.0),   # crystal
    (0.72, 0.50, 0.92, 1.0),   # magic violet
]

ANIMATIONS = [
    "coral_bubbles", "lagoon_ripples", "palm_leaf_sway", "canyon_mist",
    "arch_wave_sheen", "waterfall_splash", "sea_flower_drift",
    "shell_pearl_glow", "ruin_fish_orbit", "legendary_tide_shimmer",
]

VIEW_FRONT = Vector((8.2, -11.4, 7.8)).normalized()
VIEW_RIGHT = VIEW_FRONT.cross(Vector((0, 0, 1))).normalized()
VIEW_UP = VIEW_RIGHT.cross(VIEW_FRONT).normalized()
PIECES = []
MAT = None


def view_normal(x=0.0, y=0.0, depth=1.0):
    return (VIEW_FRONT * depth + VIEW_RIGHT * x + VIEW_UP * y).normalized()


def tangent_basis(normal):
    n = Vector(normal).normalized()
    right = n.cross(Vector((0, 0, 1)))
    if right.length < 0.1:
        right = n.cross(Vector((0, 1, 0)))
    right.normalize()
    up = n.cross(right).normalized()
    return n, right, up


def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials,
                       bpy.data.cameras, bpy.data.lights):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)


def make_palette():
    path = os.path.join(MODEL_DIR, "Ocean_Palette.png")
    img = bpy.data.images.get("Ocean_Palette") or bpy.data.images.new(
        "Ocean_Palette", width=len(PALETTE), height=1, alpha=True
    )
    pixels = []
    for color in PALETTE:
        pixels.extend(color)
    img.pixels = pixels
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()

    mat = bpy.data.materials.new("Ocean_Palette_URP")
    mat.use_nodes = True
    mat.diffuse_color = PALETTE[0]
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()
    out = nodes.new("ShaderNodeOutputMaterial")
    bsdf = nodes.new("ShaderNodeBsdfPrincipled")
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = 'Closest'
    bsdf.inputs["Roughness"].default_value = 0.72
    bsdf.inputs["Metallic"].default_value = 0.0
    links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    return mat


def color_uv(obj, color_index):
    mesh = obj.data
    uv = mesh.uv_layers.active or mesh.uv_layers.new(name="UVMap")
    u = (color_index + 0.5) / len(PALETTE)
    for loop in uv.data:
        loop.uv = (u, 0.5)
    if not mesh.materials:
        mesh.materials.append(MAT)
    for poly in mesh.polygons:
        poly.use_smooth = False


def finish_piece(obj, name, color_index):
    obj.name = name
    color_uv(obj, color_index)
    PIECES.append(obj)
    return obj


def orient(obj, normal):
    n = Vector(normal).normalized()
    obj.rotation_mode = 'QUATERNION'
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(n)
    return n


def ico(name, normal, scale, color, subdivisions=1, sink=0.0, shift=(0.0, 0.0)):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1.0)
    obj = bpy.context.object
    n, right, up = tangent_basis(normal)
    orient(obj, n)
    obj.location = n * (2.92 - sink) + right * shift[0] + up * shift[1]
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_piece(obj, name, color)


def cone(name, normal, radius, depth, color, vertices=7, offset=0.0,
         taper=0.65, shift=(0.0, 0.0), tilt=(0.0, 0.0)):
    bpy.ops.mesh.primitive_cone_add(
        vertices=vertices, radius1=radius, radius2=radius * taper, depth=depth
    )
    obj = bpy.context.object
    n, right, up = tangent_basis(normal)
    axis = (n + right * tilt[0] + up * tilt[1]).normalized()
    orient(obj, axis)
    obj.location = n * (2.92 + depth * 0.5 + offset) + right * shift[0] + up * shift[1]
    return finish_piece(obj, name, color)


def cylinder(name, normal, radius, depth, color, vertices=8, offset=0.0,
             shift=(0.0, 0.0), tilt=(0.0, 0.0)):
    return cone(name, normal, radius, depth, color, vertices, offset, 1.0, shift, tilt)


def panel_cube(name, normal, size, color, shift=(0.0, 0.0), offset=0.0, angle=0.0):
    n, right, up = tangent_basis(normal)
    if angle:
        ca, sa = math.cos(angle), math.sin(angle)
        right, up = right * ca + up * sa, up * ca - right * sa
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    obj = bpy.context.object
    basis = Matrix((right, up, n)).transposed().to_4x4()
    basis.translation = n * (2.92 + size[2] * 0.5 + offset) + right * shift[0] + up * shift[1]
    obj.matrix_world = basis
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_piece(obj, name, color)


def disc(name, normal, radius, depth, color, vertices=18, offset=0.0,
         scale_y=1.0, shift=(0.0, 0.0)):
    obj = cylinder(name, normal, radius, depth, color, vertices, offset, shift)
    obj.scale.x = scale_y
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def torus(name, normal, major, minor, color, major_segments=18, minor_segments=4,
          shift=(0.0, 0.0), offset=0.0, scale_y=1.0):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major, minor_radius=minor,
        major_segments=major_segments, minor_segments=minor_segments
    )
    obj = bpy.context.object
    n, right, up = tangent_basis(normal)
    basis = Matrix((right, up, n)).transposed().to_4x4()
    basis.translation = n * (2.92 + offset) + right * shift[0] + up * shift[1]
    obj.matrix_world = basis
    obj.scale.x = scale_y
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_piece(obj, name, color)


def coral(normal, size=1.0, color=5, fan=False, shift=(0.0, 0.0)):
    n, right, up = tangent_basis(normal)
    if fan:
        for i in range(7):
            x = (i - 3) * 0.075 * size
            height = (0.42 + 0.14 * (3 - abs(i - 3))) * size
            cone("CoralFan", n, 0.055 * size, height, color if i % 2 else 7,
                 6, shift=(shift[0] + x, shift[1]), tilt=(x * 0.9, 0.16))
        return
    trunk = cone("CoralTrunk", n, 0.11 * size, 0.58 * size, color, 7,
                 shift=shift, taper=0.65, tilt=(0.0, 0.08))
    for i, side in enumerate((-1, 1, -1, 1)):
        y = (0.12 + i * 0.10) * size
        branch_shift = (shift[0] + side * (0.09 + i * 0.015) * size, shift[1] + y)
        cone("CoralBranch", n, 0.055 * size, (0.28 + 0.04 * (i % 2)) * size,
             color if i % 2 else 6, 6, offset=y * 0.28, taper=0.42,
             shift=branch_shift, tilt=(side * 0.36, 0.17))
    ico("CoralTip", n, (0.09 * size,) * 3, 4, 1,
        shift=(shift[0], shift[1] + 0.34 * size))


def seaweed(normal, size=1.0, count=4, shift=(0.0, 0.0)):
    for i in range(count):
        x = (i - (count - 1) * 0.5) * 0.12 * size
        panel_cube("SeaweedBlade", normal, (0.07 * size, (0.42 + 0.09 * (i % 3)) * size,
                   0.055 * size), 9 if i % 2 else 8,
                   (shift[0] + x, shift[1] + 0.18 * size), angle=(i - 1.5) * 0.12)


def palm(normal, size=1.0, shift=(0.0, 0.0), lean=0.0):
    n, right, up = tangent_basis(normal)
    cone("PalmTrunk", n, 0.10 * size, 0.78 * size, 13, 7,
         shift=shift, taper=0.62, tilt=(lean, 0.08))
    crown_shift = (shift[0] + lean * 0.32 * size, shift[1] + 0.36 * size)
    for i in range(6):
        angle = i * math.tau / 6.0
        panel_cube("PalmLeaf", n, (0.48 * size, 0.12 * size, 0.045 * size),
                   9 if i % 2 else 8, crown_shift, offset=0.60 * size,
                   angle=angle)


def crystal_cluster(normal, size=1.0, count=5, shift=(0.0, 0.0)):
    for i in range(count):
        x = (i - (count - 1) * 0.5) * 0.13 * size
        height = (0.34 + 0.12 * ((i + 1) % 3)) * size
        cone("CoralCrystal", normal, 0.075 * size, height, 14 if i % 2 else 15,
             5, shift=(shift[0] + x, shift[1]), taper=0.0,
             tilt=(x * 0.45, 0.08))


def rock_cluster(normal, size=1.0, count=3, shift=(0.0, 0.0)):
    for i in range(count):
        x = (i - (count - 1) * 0.5) * 0.23 * size
        ico("OceanRock", normal,
            ((0.22 + 0.04 * (i % 2)) * size, 0.16 * size, (0.13 + 0.04 * i) * size),
            12 if i % 2 else 11, 1, sink=0.02, shift=(shift[0] + x, shift[1]))


def flower(normal, size=1.0, color=7, shift=(0.0, 0.0)):
    for i in range(5):
        angle = i * math.tau / 5
        ico("SeaFlowerPetal", normal, (0.10 * size, 0.055 * size, 0.035 * size),
            color, 1, shift=(shift[0] + math.cos(angle) * 0.09 * size,
                             shift[1] + math.sin(angle) * 0.09 * size))
    ico("SeaFlowerCore", normal, (0.06 * size,) * 3, 4, 1, shift=shift)


def starfish(normal, size=1.0, shift=(0.0, 0.0), color=6):
    for i in range(5):
        angle = i * math.tau / 5
        panel_cube("StarfishArm", normal, (0.055 * size, 0.25 * size, 0.035 * size),
                   color, (shift[0] + math.sin(angle) * 0.11 * size,
                           shift[1] + math.cos(angle) * 0.11 * size),
                   angle=-angle)


def shell(normal, size=1.0, shift=(0.0, 0.0), legendary=False):
    # Layered fan reads clearly at gameplay distance and stays firmly attached.
    segments = 9 if legendary else 7
    for i in range(segments):
        t = i / max(1, segments - 1)
        angle = (-0.78 + 1.56 * t)
        x = math.sin(angle) * 0.54 * size
        y = math.cos(angle) * 0.34 * size
        panel_cube("ShellRib", normal,
                   (0.11 * size, (0.62 - 0.12 * abs(t - 0.5)) * size, 0.12 * size),
                   4 if i % 2 else 6, (shift[0] + x, shift[1] + y), angle=-angle * 0.55)
    ico("ShellPearl", normal, (0.18 * size,) * 3, 14, 2,
        shift=(shift[0], shift[1] - 0.22 * size))


def arch(normal, size=1.0, shift=(0.0, 0.0), coral_arch=False):
    color_a = 5 if coral_arch else 12
    color_b = 6 if coral_arch else 11
    for side in (-1, 1):
        for h in range(4):
            panel_cube("SeaArchPillar", normal, (0.26 * size, 0.30 * size, 0.22 * size),
                       color_a if h % 2 else color_b,
                       (shift[0] + side * 0.58 * size, shift[1] - 0.35 * size + h * 0.29 * size))
    for i in range(7):
        angle = i * math.pi / 6
        x = math.cos(angle) * 0.58 * size
        y = 0.48 * size + math.sin(angle) * 0.55 * size
        panel_cube("SeaArchCrown", normal, (0.27 * size, 0.25 * size, 0.23 * size),
                   color_b if i % 2 else color_a, (shift[0] + x, shift[1] + y))


def bridge(normal, size=1.0, shift=(0.0, 0.0)):
    for i in range(7):
        x = (i - 3) * 0.18 * size
        y = 0.08 * math.cos(i * math.pi / 6) * size
        panel_cube("TinyBridgePlank", normal, (0.15 * size, 0.28 * size, 0.08 * size),
                   13 if i % 2 else 10, (shift[0] + x, shift[1] + y))
    for side in (-1, 1):
        panel_cube("BridgeRope", normal, (0.035 * size, 1.22 * size, 0.035 * size),
                   13, (shift[0] + side * 0.64 * size, shift[1] + 0.02 * size),
                   angle=math.pi / 2)


def common_details(index):
    # Asymmetric low-profile reef marks preserve clean negative space.
    marks = [
        (-0.67, -0.46), (-0.38, -0.68), (0.02, -0.74), (0.43, -0.62),
        (0.70, -0.31), (-0.74, 0.02), (-0.55, 0.41), (0.47, 0.46), (0.73, 0.12),
    ]
    for i, (x, y) in enumerate(marks[:7 + index % 3]):
        n = view_normal(x, y, 0.94)
        kind = (i + index) % 4
        if kind == 0:
            seaweed(n, 0.42, 2)
        elif kind == 1:
            coral(n, 0.38, 5 if i % 2 else 7)
        elif kind == 2:
            rock_cluster(n, 0.42, 2)
        else:
            starfish(n, 0.38, color=6 if i % 2 else 7)

    # Foam bands communicate water from thumbnail distance without changing the sphere.
    for i in range(3):
        normal = view_normal(-0.42 + i * 0.41, -0.30 + 0.09 * ((index + i) % 3), 0.98)
        torus("FoamRing", normal, 0.18 + 0.025 * (i % 2), 0.025, 4,
              12, 3, scale_y=0.62)


def build_identity(index):
    front = view_normal(-0.02, 0.10)
    if index == 1:
        # Crown reef: strong coral fan silhouette, nested color rhythm.
        disc("ReefShelf", front, 1.00, 0.10, 2, 24, scale_y=0.62)
        coral(front, 2.25, 5, True)
        coral(view_normal(-0.42, -0.10), 0.95, 7)
        coral(view_normal(0.44, -0.12), 0.90, 6)
        seaweed(view_normal(0.10, -0.43), 0.82, 5)
    elif index == 2:
        # Crystal lagoon: concentric water, white foam and prismatic crown.
        disc("LagoonSand", front, 1.17, 0.08, 10, 28, scale_y=0.64)
        disc("CrystalLagoon", front, 0.94, 0.10, 3, 28, offset=0.04, scale_y=0.62)
        torus("LagoonFoam", front, 0.78, 0.055, 4, 24, 4, offset=0.11, scale_y=0.62)
        crystal_cluster(view_normal(0.0, 0.25), 1.45, 7)
        flower(view_normal(-0.48, -0.32), 0.78, 7)
    elif index == 3:
        # Twin-palm atoll linked by a tiny bridge.
        disc("AtollSand", front, 1.18, 0.10, 10, 26, scale_y=0.52)
        disc("AtollWater", front, 0.68, 0.11, 2, 24, offset=0.05, scale_y=0.48)
        palm(view_normal(-0.32, 0.10), 1.32, lean=-0.15)
        palm(view_normal(0.34, 0.06), 1.18, lean=0.16)
        bridge(front, 0.78, (0.0, -0.30))
        starfish(view_normal(0.55, -0.40), 0.78)
    elif index == 4:
        # Coral canyon gates: paired vertical forms with a bright channel.
        disc("CanyonChannel", front, 0.73, 0.08, 3, 22, scale_y=0.46)
        for side in (-1, 1):
            for h in range(4):
                panel_cube("CoralCanyonStone", front,
                           (0.34, 0.32 + h * 0.04, 0.24),
                           12 if h % 2 else 11, (side * (0.49 + h * 0.05), -0.40 + h * 0.30))
            coral(view_normal(side * 0.40, 0.18), 0.85, 5 if side < 0 else 7)
        torus("CanyonCurrent", front, 0.48, 0.035, 4, 18, 3, shift=(0.0, -0.15), scale_y=0.50)
    elif index == 5:
        # Moon-current arch with sea flowers.
        arch(front, 1.43)
        torus("ArchCurrent", front, 0.70, 0.050, 3, 22, 4, shift=(0.0, -0.28), scale_y=0.60)
        flower(view_normal(-0.56, -0.42), 0.90, 5)
        flower(view_normal(0.58, -0.39), 0.82, 7)
        seaweed(view_normal(0.50, 0.36), 0.70, 3)
    elif index == 6:
        # Waterfall garden: one connected stone crown, bright cascade and splash basin.
        disc("UpperSpring", front, 0.62, 0.10, 3, 22, offset=0.05,
             scale_y=0.62, shift=(0.0, 0.42))
        for side in (-1, 1):
            for row in range(3):
                panel_cube("WaterfallBank", front, (0.34, 0.34, 0.25),
                           11 if row % 2 else 12,
                           (side * (0.54 - row * 0.05), 0.40 - row * 0.28))
        for i in range(5):
            panel_cube("WaterfallRibbon", front, (0.14, 1.18 - i * 0.07, 0.075),
                       3 if i % 2 else 2, ((i - 2) * 0.14, -0.04), offset=0.14)
        disc("WaterfallPool", view_normal(0.0, -0.45), 0.80, 0.08, 3, 24, scale_y=0.54)
        torus("SplashFoam", view_normal(0.0, -0.45), 0.61, 0.05, 4, 20, 4, offset=0.10, scale_y=0.54)
        coral(view_normal(-0.54, -0.32), 0.68, 5)
        coral(view_normal(0.55, -0.34), 0.64, 7)
    elif index == 7:
        # Tropical sea-flower paradise: a bold lotus readable from gameplay distance.
        disc("ParadiseShallows", front, 0.96, 0.08, 2, 24, scale_y=0.64)
        for i in range(8):
            angle = i * math.tau / 8
            panel_cube("GiantSeaLotusPetal", front, (0.24, 0.62, 0.09),
                       5 if i % 3 == 0 else (7 if i % 2 else 6),
                       (math.sin(angle) * 0.46, math.cos(angle) * 0.34),
                       offset=0.16, angle=-angle)
        ico("LotusPearl", front, (0.24, 0.24, 0.18), 14, 2, shift=(0.0, 0.02))
        seaweed(view_normal(-0.56, -0.18), 0.95, 5)
        seaweed(view_normal(0.58, -0.18), 0.92, 5)
        coral(view_normal(-0.46, 0.40), 0.66, 6)
        coral(view_normal(0.48, 0.38), 0.64, 5)
    elif index == 8:
        # Giant pearl shell, flanked by small crystal reefs.
        disc("ShellShelf", front, 1.10, 0.08, 10, 24, scale_y=0.66)
        shell(front, 1.55, legendary=True)
        crystal_cluster(view_normal(-0.52, -0.18), 0.65, 4)
        crystal_cluster(view_normal(0.54, -0.20), 0.62, 4)
    elif index == 9:
        # Ancient tide observatory and orbiting stone glyphs.
        for side in (-1, 1):
            panel_cube("TidePillar", front, (0.31, 1.30, 0.28), 11,
                       (side * 0.75, 0.02))
            panel_cube("TideCapital", front, (0.47, 0.20, 0.31), 12,
                       (side * 0.75, 0.72))
        panel_cube("TideLintel", front, (1.78, 0.25, 0.30), 12, (0.0, 0.88))
        torus("TideDial", front, 0.45, 0.08, 14, 18, 4, shift=(0.0, 0.15), offset=0.14)
        disc("TideLens", front, 0.25, 0.08, 3, 16, offset=0.22, scale_y=0.78)
        for x in (-0.52, 0.0, 0.52):
            panel_cube("RuinStep", front, (0.45, 0.18, 0.18), 11, (x, -0.66))
    else:
        # Legendary Ocean sanctuary: coral arch, crystal trident and royal lagoon.
        disc("LegendaryLagoon", front, 1.12, 0.09, 2, 28, scale_y=0.68)
        torus("LegendaryFoam", front, 0.90, 0.055, 4, 26, 4, offset=0.11, scale_y=0.68)
        arch(view_normal(-0.45, -0.10), 0.62, coral_arch=True)
        arch(view_normal(0.47, -0.08), 0.58, coral_arch=True)
        panel_cube("TridentStem", front, (0.18, 1.72, 0.20), 14, (0.0, 0.22))
        for side in (-1, 0, 1):
            panel_cube("TridentProng", front,
                       (0.15, 0.72 if side == 0 else 0.58, 0.19),
                       14 if side == 0 else 15, (side * 0.36, 0.86))
            cone("TridentTip", front, 0.14, 0.36, 4, 5,
                 shift=(side * 0.36, 1.24 if side == 0 else 1.12), taper=0.0)
        coral(view_normal(-0.62, -0.36), 0.72, 5)
        coral(view_normal(0.62, -0.34), 0.72, 7)


def triangle_count(objects):
    return sum(sum(max(1, len(poly.vertices) - 2) for poly in obj.data.polygons)
               for obj in objects)


def ensure_minimum_budget(index):
    # Small pearl/bubble clusters fill the mobile triangle floor without visual clutter.
    cursor = 0
    spots = [
        (-0.72, 0.28), (-0.54, -0.12), (-0.30, 0.58), (0.18, 0.62),
        (0.52, 0.36), (0.70, -0.08), (0.38, -0.56), (-0.12, -0.66)
    ]
    while triangle_count(PIECES) < 1850:
        x, y = spots[cursor % len(spots)]
        n = view_normal(x + 0.025 * (cursor // len(spots)), y, 0.92)
        ico("SeaPearl", n, (0.055, 0.055, 0.045), 4 if cursor % 3 else 14, 1,
            sink=0.01)
        cursor += 1
        if cursor > 50:
            break


def join_and_optimize(name, index):
    ensure_minimum_budget(index)
    bpy.ops.object.select_all(action='DESELECT')
    for piece in PIECES:
        piece.select_set(True)
    bpy.context.view_layer.objects.active = PIECES[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + "_Mesh"
    obj.location = (0, 0, 0)
    obj.rotation_euler = (0, 0, 0)
    obj.scale = (1, 1, 1)
    obj["biome"] = "Ocean"
    obj["planet_index"] = index
    obj["hero_landmark"] = [
        "Crown Reef", "Crystal Lagoon", "Twin Palm Atoll", "Coral Canyon",
        "Moon Current Arch", "Cascade Garden", "Sea Flower Paradise",
        "Pearl Shell", "Tide Observatory", "Legendary Tide Sanctuary"
    ][index - 1]
    obj["environment_animation"] = ANIMATIONS[index - 1]
    obj["gameplay_clearance_radius"] = 3.0
    obj["mobile_optimized"] = True

    tri = obj.modifiers.new("ExportTriangulation", "TRIANGULATE")
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=tri.name)
    tris = len(obj.data.polygons)
    if tris > 2450:
        decimate = obj.modifiers.new("MobileTriangleBudget", "DECIMATE")
        decimate.ratio = 2380.0 / tris
        bpy.ops.object.modifier_apply(modifier=decimate.name)

    obj.data.validate(clean_customdata=False)
    for poly in obj.data.polygons:
        poly.use_smooth = False
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj


def setup_render():
    world = bpy.context.scene.world or bpy.data.worlds.new("OceanWorld")
    bpy.context.scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.010, 0.040, 0.085, 1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.50

    bpy.ops.object.camera_add(location=(8.2, -11.4, 7.8))
    camera = bpy.context.object
    camera.name = "Ocean_RenderCamera"
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 6.90
    camera.rotation_euler = (Vector((0, 0, 0)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.scene.camera = camera

    bpy.ops.object.light_add(type='AREA', location=(-4.8, -6.2, 10.8))
    key = bpy.context.object
    key.name = "Ocean_KeyLight"
    key.data.energy = 1260
    key.data.color = (0.72, 0.97, 1.0)
    key.data.shape = 'DISK'
    key.data.size = 5.5
    key.rotation_euler = (Vector((0, 0, 0)) - key.location).to_track_quat('-Z', 'Y').to_euler()

    bpy.ops.object.light_add(type='AREA', location=(6.0, 1.4, 3.5))
    fill = bpy.context.object
    fill.name = "Ocean_CoralFill"
    fill.data.energy = 680
    fill.data.color = (1.0, 0.48, 0.58)
    fill.data.size = 6.5
    fill.rotation_euler = (Vector((0, 0, 0)) - fill.location).to_track_quat('-Z', 'Y').to_euler()

    scene = bpy.context.scene
    if "BLENDER_EEVEE_NEXT" in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items.keys():
        scene.render.engine = 'BLENDER_EEVEE_NEXT'
    else:
        scene.render.engine = 'BLENDER_EEVEE'
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.image_settings.color_depth = '8'
    scene.render.film_transparent = True
    try:
        scene.view_settings.look = 'AgX - Medium High Contrast'
    except Exception:
        pass
    return camera, key, fill


def export_and_render(obj, index, all_planets):
    name = f"Ocean_{index:02d}"
    for planet in all_planets:
        planet.hide_render = planet != obj
    bpy.context.scene.render.filepath = os.path.join(SPRITE_DIR, name + ".png")
    bpy.ops.render.render(write_still=True)

    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(MODEL_DIR, name + ".fbx"),
        use_selection=True,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        object_types={'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        use_tspace=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode='COPY',
        embed_textures=False,
        use_custom_props=True,
        axis_forward='-Z',
        axis_up='Y'
    )


clear_scene()
MAT = make_palette()
PLANETS = []
for planet_index in range(1, 11):
    PIECES = []
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=3, radius=3.0, location=(0, 0, 0))
    base = bpy.context.object
    finish_piece(base, "PerfectSphere", 0 if planet_index % 3 else 1)
    build_identity(planet_index)
    common_details(planet_index)
    PLANETS.append(join_and_optimize(f"Ocean_{planet_index:02d}", planet_index))

CAMERA, KEY_LIGHT, FILL_LIGHT = setup_render()
for planet_index, planet in enumerate(PLANETS, 1):
    export_and_render(planet, planet_index, PLANETS)

for i, planet in enumerate(PLANETS):
    planet.hide_render = False
    planet.location = ((i % 5) * 7.2 - 14.4, (i // 5) * 7.2 - 3.6, 0)

bpy.ops.wm.save_as_mainfile(
    filepath=os.path.join(MODEL_DIR, "Ocean_Planet_Collection.blend")
)

report = []
for planet in PLANETS:
    report.append(
        f"{planet.name}: tris={len(planet.data.polygons)}, verts={len(planet.data.vertices)}, "
        f"uv={bool(planet.data.uv_layers)}, mats={len(planet.data.materials)}, pivot={tuple(planet.location)}"
    )
print("OCEAN_BUILD_COMPLETE\n" + "\n".join(report))
