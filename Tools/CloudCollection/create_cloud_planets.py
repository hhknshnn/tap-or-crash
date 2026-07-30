import bpy
import math
import os
import random
from mathutils import Vector, noise


ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
MODEL_DIR = os.path.join(ROOT, "Assets", "Models", "CloudPlanets")
SPRITE_DIR = os.path.join(ROOT, "Assets", "Sprites", "CloudPlanets")
BLEND_PATH = os.path.join(MODEL_DIR, "Cloud_Planet_Collection.blend")
PALETTE_PATH = os.path.join(MODEL_DIR, "Cloud_Palette.png")

# Only build this many planets (1 while art-directing, 10 for the full pack).
PLANET_COUNT = 10

# The only high-key planet in the game: every other world is a dark body on a
# dark sky, so Cloud is identified by value before hue.
PALETTE = [
    (0.585, 0.600, 0.690, 1.0),   # 0  cloud shadow
    (0.700, 0.720, 0.810, 1.0),   # 1  cloud underside
    (0.820, 0.840, 0.910, 1.0),   # 2  cloud mid
    (0.925, 0.940, 0.985, 1.0),   # 3  cloud lit
    (1.000, 1.000, 1.000, 1.0),   # 4  cloud highlight
    (0.560, 0.720, 0.970, 1.0),   # 5  sky pocket
    (1.000, 0.820, 0.360, 1.0),   # 6  gold
    (0.930, 0.600, 0.190, 1.0),   # 7  deep gold
    (1.000, 0.620, 0.620, 1.0),   # 8  sunset rose
    (0.780, 0.680, 0.960, 1.0),   # 9  lilac
    (0.975, 0.965, 0.930, 1.0),   # 10 white stone
    (0.790, 0.765, 0.720, 1.0),   # 11 stone shadow
    (0.400, 0.860, 0.860, 1.0),   # 12 aqua trim
    # Both are deliberately far darker than they look "correct" in isolation:
    # on a white body under a bright key, mid greys wash out completely.
    (0.230, 0.245, 0.330, 1.0),   # 13 storm grey
    (0.760, 0.930, 1.000, 1.0),   # 14 lightning
    (0.300, 0.265, 0.300, 1.0),   # 15 floating rock
]

GRID = [
    (-14.4, -3.6, 0.0), (-7.2, -3.6, 0.0), (0.0, -3.6, 0.0),
    (7.2, -3.6, 0.0), (14.4, -3.6, 0.0),
    (-14.4, 3.6, 0.0), (-7.2, 3.6, 0.0), (0.0, 3.6, 0.0),
    (7.2, 3.6, 0.0), (14.4, 3.6, 0.0),
]

# ~34 degrees off the camera axis, same reasoning as the Sakura and Mushroom
# packs: head-on hides every vertical, steeper leaves the 1024 frame.
HERO = Vector((0.264, -0.367, 0.891)).normalized()
CAMERA_DIR = Vector((8.2, -11.4, 7.8)).normalized()
RADIUS = 3.0


def ensure_dirs():
    os.makedirs(MODEL_DIR, exist_ok=True)
    os.makedirs(SPRITE_DIR, exist_ok=True)


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.cameras, bpy.data.lights):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)
    for material in list(bpy.data.materials):
        bpy.data.materials.remove(material)
    for image in list(bpy.data.images):
        if image.name != "Render Result":
            bpy.data.images.remove(image)


def make_palette():
    image = bpy.data.images.new("Cloud_Palette", width=256, height=16, alpha=True)
    pixels = []
    for _y in range(16):
        for x in range(256):
            pixels.extend(PALETTE[min(15, x // 16)])
    image.pixels = pixels
    image.filepath_raw = PALETTE_PATH
    image.file_format = "PNG"
    image.save()
    image.colorspace_settings.name = "sRGB"
    return image


def make_materials(palette_image):
    temporary = []
    for index, color in enumerate(PALETTE):
        material = bpy.data.materials.new(f"Cloud_Color_{index:02d}")
        material.diffuse_color = color
        material["palette_index"] = index
        temporary.append(material)

    material = bpy.data.materials.new("Cloud_Palette_URP")
    material.use_nodes = True
    material.diffuse_color = PALETTE[3]
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = palette_image
    texture.interpolation = "Closest"
    # Vapour, not plastic: very rough, no metal, so the white body keeps its
    # shape through shading instead of blowing out into a flat silhouette.
    principled.inputs["Roughness"].default_value = 0.88
    principled.inputs["Metallic"].default_value = 0.0
    links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    links.new(principled.outputs["BSDF"], output.inputs["Surface"])
    return temporary, material


def add_slots(obj, temporary):
    for material in temporary:
        obj.data.materials.append(material)


def paint(obj, index, temporary):
    add_slots(obj, temporary)
    for polygon in obj.data.polygons:
        polygon.material_index = index
    return obj


def point_z(obj, direction):
    obj.rotation_mode = "QUATERNION"
    obj.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(Vector(direction).normalized())


def cone_piece(name, center, direction, radius_base, radius_top, length, color,
               temporary, sides=5, offset=0.5):
    direction = Vector(direction).normalized()
    location = Vector(center) + direction * (length * offset)
    bpy.ops.mesh.primitive_cone_add(
        vertices=sides,
        radius1=radius_base,
        radius2=radius_top,
        depth=length,
        end_fill_type="NGON",
        location=location,
    )
    obj = bpy.context.object
    obj.name = name
    point_z(obj, direction)
    return paint(obj, color, temporary)


def box_piece(name, location, scale, color, temporary, direction=None):
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if direction is not None:
        point_z(obj, direction)
    return paint(obj, color, temporary)


def ico_piece(name, location, radius, color, temporary, subdivisions=1, scale=None,
              direction=None):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=radius,
                                          location=location)
    obj = bpy.context.object
    obj.name = name
    if scale is not None:
        obj.scale = scale
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if direction is not None:
        point_z(obj, direction)
    return paint(obj, color, temporary)


def cylinder_piece(name, center, direction, radius, depth, color, temporary,
                   sides=12, offset=0.0):
    direction = Vector(direction).normalized()
    bpy.ops.mesh.primitive_cylinder_add(
        vertices=sides, radius=radius, depth=depth,
        location=Vector(center) + direction * offset,
    )
    obj = bpy.context.object
    obj.name = name
    point_z(obj, direction)
    return paint(obj, color, temporary)


def torus_piece(name, location, major_radius, minor_radius, color, temporary,
                rotation=(0.0, 0.0, 0.0), major_segments=16, minor_segments=4):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius, minor_radius=minor_radius,
        major_segments=major_segments, minor_segments=minor_segments,
        location=location, rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    return paint(obj, color, temporary)


def arc_band_piece(name, center, normal, outer_radius, inner_radius, half_angle,
                   thickness, color, temporary, segments=12, tangent=None):
    normal = Vector(normal).normalized()
    tangent = Vector(tangent) if tangent is not None else Vector((1.0, 0.0, 0.0))
    bitangent = normal.cross(tangent).normalized()
    vertices = []
    faces = []
    for i in range(segments + 1):
        angle = -half_angle + (half_angle * 2.0) * i / segments
        radial = tangent * math.cos(angle) + bitangent * math.sin(angle)
        outer = Vector(center) + radial * outer_radius
        inner = Vector(center) + radial * inner_radius
        vertices += [
            tuple(outer + normal * thickness),
            tuple(outer - normal * thickness),
            tuple(inner + normal * thickness),
            tuple(inner - normal * thickness),
        ]
    for i in range(segments):
        a = i * 4
        b = (i + 1) * 4
        faces += [
            (a + 0, b + 0, b + 1, a + 1),
            (a + 2, a + 3, b + 3, b + 2),
            (a + 0, a + 2, b + 2, b + 0),
            (a + 1, b + 1, b + 3, a + 3),
        ]
    faces += [(0, 1, 3, 2)]
    end = segments * 4
    faces += [(end + 0, end + 2, end + 3, end + 1)]
    mesh = bpy.data.meshes.new(name + "_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return paint(obj, color, temporary)


def basis(direction):
    direction = Vector(direction).normalized()
    tangent = Vector((1.0, 0.0, 0.0))
    if abs(direction.dot(tangent)) > 0.94:
        tangent = Vector((0.0, 1.0, 0.0))
    bitangent = direction.cross(tangent).normalized()
    tangent = bitangent.cross(direction).normalized()
    return tangent, bitangent


# ── Signature silhouette: cumulus banks and vapour rings ─────────────────────
def cloud_bank(name, base, direction, radius, temporary, puffs=3, subdivisions=1,
               colors=(3, 4, 2), lift=0.10):
    """A bumpy cluster of flattened puffs. Repeated over the globe it is what
    turns a smooth sphere into a body made of cloud."""
    direction = Vector(direction).normalized()
    tangent, bitangent = basis(direction)
    pieces = [ico_piece(f"{name}_Core", Vector(base) + direction * (radius * lift),
                        radius, colors[0], temporary, subdivisions=subdivisions,
                        scale=(1.30, 1.30, 0.62), direction=direction)]
    for i in range(max(0, puffs - 1)):
        angle = math.tau * i / max(1, puffs - 1) + 0.45
        offset = (tangent * math.cos(angle) + bitangent * math.sin(angle)) * radius * 0.78
        pieces.append(ico_piece(
            f"{name}_Puff{i}", Vector(base) + offset + direction * (radius * (lift + 0.06)),
            radius * (0.58 + 0.10 * (i % 3)), colors[1 + i % (len(colors) - 1)],
            temporary, subdivisions=subdivisions, scale=(1.22, 1.22, 0.68),
            direction=direction))
    return pieces


def vapour_ring(name, center, normal, radius, temporary, color=4, thickness=0.075,
                segments=14, tangent=None):
    return [arc_band_piece(name, center, normal, radius, radius * 0.80,
                           math.radians(180), thickness, color, temporary,
                           segments=segments, tangent=tangent)]


def sky_column(name, base, direction, height, temporary, color=11, cap_color=6):
    direction = Vector(direction).normalized()
    return [
        cone_piece(f"{name}_Shaft", base, direction, 0.115, 0.095, height, color,
                   temporary, sides=8),
        box_piece(f"{name}_Cap", tuple(Vector(base) + direction * (height + 0.06)),
                  (0.155, 0.155, 0.055), cap_color, temporary, direction=direction),
    ]


def balloon(name, anchor, direction, radius, temporary, color=8, basket_color=7):
    direction = Vector(direction).normalized()
    body = Vector(anchor) + direction * (radius * 2.10)
    return [
        ico_piece(f"{name}_Envelope", body, radius, color, temporary, subdivisions=1,
                  scale=(1.0, 1.0, 1.22), direction=direction),
        box_piece(f"{name}_Basket", tuple(body - direction * (radius * 1.34)),
                  (radius * 0.34, radius * 0.34, radius * 0.28), basket_color,
                  temporary, direction=direction),
        cone_piece(f"{name}_Line", anchor, direction, 0.022, 0.016, radius * 1.05,
                   11, temporary, sides=4),
    ]


# ── Terrain ───────────────────────────────────────────────────────────────────
def base_planet(name, center, temporary, seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=RADIUS, location=center)
    sphere = bpy.context.object
    sphere.name = name + "_Sphere"
    add_slots(sphere, temporary)

    # Billowing, not hilly: the strongest displacement of any pack, because the
    # body itself has to read as cloud rather than as ground under cloud.
    offset = Vector((seed * 0.23, seed * 0.47, seed * 0.71))
    for vertex in sphere.data.vertices:
        direction = vertex.co.normalized()
        billow = noise.noise(direction * 1.8 + offset) * 0.62 + noise.noise(direction * 4.1 + offset) * 0.38
        vertex.co = direction * (RADIUS + billow * 0.26)

    sky_offset = Vector((seed * 0.61, seed * 0.19, seed * 0.37))
    warm_offset = Vector((seed * 0.13, seed * 0.83, seed * 0.29))
    for polygon in sphere.data.polygons:
        normal = polygon.center.normalized()
        light = max(0.0, normal.z * 0.60 - normal.y * 0.26 + normal.x * 0.14)
        index = max(0, min(4, int(light * 5.4)))
        # Large, low-frequency patches only: at sprite size, small colour patches
        # on a white body read as confetti rather than as sky and sunlight.
        if noise.noise(normal * 1.5 + sky_offset) > 0.52:
            index = 5
        elif light > 0.30 and noise.noise(normal * 1.3 + warm_offset) > 0.50:
            # Warm light only where the sun actually reaches; rose patches on the
            # shadowed underside read as stains.
            index = 6 if light > 0.45 else 8
        polygon.material_index = index
    return sphere


def surface_details(center, temporary, seed, count=12):
    """Cloud banks scattered over the camera-facing cap."""
    pieces = []
    golden = math.pi * (3.0 - math.sqrt(5.0))
    rng = random.Random(seed)
    tangent, bitangent = basis(CAMERA_DIR)
    cap = math.cos(math.radians(76.0))
    for i in range(count):
        u = (i + 0.55) / count
        polar = math.acos(1.0 - u * (1.0 - cap))
        angle = i * golden + seed * 0.37
        direction = (CAMERA_DIR * math.cos(polar)
                     + (tangent * math.cos(angle) + bitangent * math.sin(angle))
                     * math.sin(polar)).normalized()
        if direction.dot(HERO) > 0.84:
            direction = (direction - HERO * 0.55).normalized()
        base = Vector(center) + direction * (RADIUS - 0.10)
        colors = rng.choice(((3, 4, 2), (4, 3, 2), (3, 2, 4), (4, 4, 8)))
        pieces += cloud_bank(f"Bank_{i:02d}", base, direction,
                             rng.uniform(0.44, 0.62), temporary, puffs=3,
                             subdivisions=1, colors=colors)
    return pieces


# ── Hero landmarks ────────────────────────────────────────────────────────────
def hero_origin(center, lift=0.0):
    return Vector(center) + HERO * (RADIUS - 0.06 + lift)


def landmark_01(center, temporary):
    """Sky temple: white columns under a gold roof."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for side in (-1, 1):
        for depth in (-1, 1):
            base = origin + tangent * side * 0.34 + bitangent * depth * 0.22
            pieces += sky_column(f"TempleCol{side}{depth}", base, HERO, 0.62, temporary)
    pieces.append(box_piece("TempleFloor", tuple(origin + HERO * 0.04),
                            (0.56, 0.40, 0.055), 10, temporary, direction=HERO))
    pieces.append(cone_piece("TempleRoof", origin + HERO * 0.68, HERO, 0.46, 0.06,
                             0.44, 6, temporary, sides=6))
    pieces += cloud_bank("TempleBank", origin - bitangent * 0.62, HERO, 0.40, temporary)
    return pieces


def landmark_02(center, temporary):
    """Vapour rings: three tilted bands wrapping the crown."""
    origin = hero_origin(center, lift=0.10)
    tangent, bitangent = basis(HERO)
    pieces = []
    for i, (radius, tilt, color) in enumerate(((0.92, 0.0, 4), (0.74, 0.34, 3), (0.56, -0.28, 8))):
        normal = (HERO + tangent * tilt).normalized()
        pieces += vapour_ring(f"VapourRing{i}", origin + HERO * (0.10 + 0.14 * i),
                              normal, radius, temporary, color=color,
                              thickness=0.070, segments=14, tangent=bitangent)
    pieces.append(ico_piece("RingCore", origin + HERO * 0.24, 0.30, 6, temporary,
                            subdivisions=2, scale=(1.0, 1.0, 0.86), direction=HERO))
    return pieces


def landmark_03(center, temporary):
    """Rainbow arch over a bright bank."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for i, color in enumerate((8, 6, 12)):
        pieces.append(arc_band_piece(f"RainbowBand{i}", origin + HERO * 0.02,
                                     bitangent, 0.86 - i * 0.12, 0.78 - i * 0.12,
                                     math.radians(88), 0.055, color, temporary,
                                     segments=12, tangent=tangent))
    pieces += cloud_bank("ArchBankL", origin - tangent * 0.80, HERO, 0.38, temporary)
    pieces += cloud_bank("ArchBankR", origin + tangent * 0.80, HERO, 0.38, temporary)
    return pieces


def landmark_04(center, temporary):
    """Storm cell: the one dark mass in a white pack, split by lightning."""
    origin = hero_origin(center, lift=0.14)
    tangent, bitangent = basis(HERO)
    pieces = cloud_bank("StormCore", origin, HERO, 0.62, temporary, puffs=4,
                        subdivisions=2, colors=(13, 0, 13), lift=0.18)
    for i, (u, v) in enumerate(((-0.20, 0.10), (0.24, -0.14))):
        start = origin + tangent * u + bitangent * v - HERO * 0.10
        pieces.append(box_piece(f"Bolt{i}A", tuple(start - HERO * 0.16),
                                (0.075, 0.075, 0.26), 6, temporary,
                                direction=(HERO + tangent * 0.42).normalized()))
        pieces.append(box_piece(f"Bolt{i}B", tuple(start - HERO * 0.46),
                                (0.062, 0.062, 0.22), 14, temporary,
                                direction=(HERO - tangent * 0.38).normalized()))
    pieces += cloud_bank("StormSkirt", origin - HERO * 0.46, HERO, 0.44, temporary,
                         colors=(2, 3, 13))
    return pieces


def landmark_05(center, temporary):
    """Balloon dock: three tethered envelopes over a platform."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [box_piece("DockDeck", tuple(origin + HERO * 0.05),
                        (0.62, 0.34, 0.055), 10, temporary, direction=HERO)]
    for i, (u, radius, color) in enumerate(((-0.46, 0.36, 8), (0.06, 0.44, 6), (0.52, 0.34, 9))):
        anchor = origin + tangent * u + bitangent * (0.05 * (i - 1)) + HERO * 0.08
        pieces += balloon(f"DockBalloon{i}", anchor, HERO, radius, temporary,
                          color=color, basket_color=11)
    return pieces


def landmark_06(center, temporary):
    """Cloud spire: a stack of banks climbing to a gold tip."""
    origin = hero_origin(center)
    pieces = []
    for i in range(4):
        pieces += cloud_bank(f"SpireTier{i}", origin + HERO * (0.16 * i), HERO,
                             0.54 - 0.09 * i, temporary, puffs=3,
                             colors=(3, 4, 8) if i % 2 else (4, 3, 6),
                             lift=0.10 + 0.04 * i)
    pieces.append(cone_piece("SpireTip", origin + HERO * 0.70, HERO, 0.26, 0.0,
                             0.62, 6, temporary, sides=6))
    pieces.append(torus_piece("SpireCollar", tuple(origin + HERO * 0.66), 0.34, 0.06, 7,
                              temporary, rotation=(math.radians(62), 0.0, 0.0),
                              major_segments=14, minor_segments=4))
    return pieces


def landmark_07(center, temporary):
    """Sun shrine: a gold disc ringed by pale columns."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [torus_piece("SunRing", tuple(origin + HERO * 0.42), 0.62, 0.085, 6,
                          temporary, rotation=(math.radians(62), 0.0, math.radians(14)),
                          major_segments=18, minor_segments=4)]
    pieces.append(ico_piece("SunDisc", origin + HERO * 0.42, 0.34, 7, temporary,
                            subdivisions=2, scale=(1.0, 1.0, 0.5), direction=HERO))
    for i in range(4):
        angle = math.tau * i / 4 + 0.5
        offset = tangent * math.cos(angle) * 0.74 + bitangent * math.sin(angle) * 0.74
        pieces += sky_column(f"ShrineCol{i}", origin + offset,
                             (HERO + offset.normalized() * 0.18).normalized(),
                             0.44, temporary)
    return pieces


def landmark_08(center, temporary):
    """Mist fall: a bank spilling off a ledge into open sky."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    ledge = origin + HERO * 0.46 + bitangent * 0.20
    pieces = [box_piece("FallLedge", tuple(ledge), (0.54, 0.24, 0.085), 15, temporary,
                        direction=HERO)]
    pieces.append(box_piece("FallRim", tuple(ledge + HERO * 0.09),
                            (0.56, 0.26, 0.030), 6, temporary, direction=HERO))
    for i in range(3):
        offset = tangent * (i - 1) * 0.26
        # Sky-blue streams, not white: a white fall on a white body is invisible.
        pieces.append(box_piece(f"MistStream{i}", tuple(ledge + offset - HERO * 0.34),
                                (0.115, 0.075, 0.38), (5, 12, 5)[i], temporary,
                                direction=HERO))
    pieces += cloud_bank("FallPool", origin - bitangent * 0.16 - HERO * 0.04, HERO,
                         0.50, temporary, puffs=4, colors=(4, 3, 2))
    return pieces


def landmark_09(center, temporary):
    """Floating isle: a rock shard hanging under its own cloud."""
    origin = hero_origin(center, lift=0.30)
    tangent, bitangent = basis(HERO)
    pieces = [ico_piece("IsleRock", origin, 0.48, 15, temporary, subdivisions=1,
                        scale=(1.30, 1.05, 0.70), direction=HERO)]
    pieces.append(cone_piece("IsleKeel", origin - HERO * 0.16, -HERO, 0.34, 0.0,
                             0.52, 15, temporary, sides=6))
    pieces += cloud_bank("IsleCrown", origin + HERO * 0.34, HERO, 0.42, temporary,
                         colors=(4, 3, 6))
    pieces += vapour_ring("IsleRing", origin - HERO * 0.10, HERO, 0.86, temporary,
                          color=3, thickness=0.060, segments=14, tangent=tangent)
    return pieces


def landmark_10(center, temporary):
    """Sky citadel: the finale — towers, rings and gold banners."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [box_piece("CitadelDeck", tuple(origin + HERO * 0.06),
                        (0.70, 0.44, 0.06), 10, temporary, direction=HERO)]
    for i, (u, height) in enumerate(((-0.44, 0.86), (0.0, 1.06), (0.44, 0.78))):
        base = origin + tangent * u + HERO * 0.08
        pieces.append(cone_piece(f"CitadelTower{i}", base, HERO, 0.17, 0.13, height,
                                 10, temporary, sides=8))
        pieces.append(cone_piece(f"CitadelSpire{i}", base + HERO * height, HERO,
                                 0.24, 0.0, 0.30, 6, temporary, sides=6))
    pieces += vapour_ring("CitadelRing", origin + HERO * 0.30, HERO, 1.00, temporary,
                          color=4, thickness=0.070, segments=14, tangent=tangent)
    pieces += cloud_bank("CitadelBank", origin - bitangent * 0.66, HERO, 0.42, temporary)
    return pieces


LANDMARKS = [
    landmark_01, landmark_02, landmark_03, landmark_04, landmark_05,
    landmark_06, landmark_07, landmark_08, landmark_09, landmark_10,
]


def emphasize_landmark(pieces, center, index):
    planar_factors = (1.90, 1.78, 1.92, 1.80, 1.94, 1.76, 1.92, 1.96, 1.78, 1.86)
    planar = planar_factors[index]
    radial = 1.62
    anchor = Vector(center) + HERO * (RADIUS - 0.06)
    tangent, bitangent = basis(HERO)

    for obj in pieces:
        inverse = obj.matrix_world.inverted()
        for vertex in obj.data.vertices:
            world = obj.matrix_world @ vertex.co
            relative = world - anchor
            enlarged = (
                tangent * relative.dot(tangent) * planar
                + bitangent * relative.dot(bitangent) * planar
                + HERO * relative.dot(HERO) * radial
            )
            vertex.co = inverse @ (anchor + enlarged)


def join_planet(name, center, pieces, final_material, temporary):
    bpy.ops.object.select_all(action="DESELECT")
    for piece in pieces:
        piece.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.object.join()
    planet = bpy.context.object
    planet.name = name

    while planet.data.uv_layers:
        planet.data.uv_layers.remove(planet.data.uv_layers[0])
    uv = planet.data.uv_layers.new(name="CloudPaletteUV")
    for polygon in planet.data.polygons:
        slot = planet.material_slots[polygon.material_index]
        material = slot.material
        index = int(material.get("palette_index", 3)) if material else 3
        u = (index * 16 + 8) / 256.0
        for loop_index in polygon.loop_indices:
            uv.data[loop_index].uv = (u, 0.5)

    planet.data.materials.clear()
    planet.data.materials.append(final_material)
    for polygon in planet.data.polygons:
        polygon.material_index = 0
        polygon.use_smooth = False

    bpy.context.scene.cursor.location = center
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR", center="MEDIAN")
    planet["hero_landmark"] = name.replace("Cloud_", "")
    planet["mobile_optimized"] = True
    planet["single_material"] = True
    return planet


def add_camera_and_lights():
    bpy.ops.object.camera_add(location=(8.2, -11.4, 7.8))
    camera = bpy.context.object
    camera.name = "Cloud_RenderCamera"
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 7.6
    camera.rotation_euler = (Vector((0.0, 0.0, 0.0)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = camera

    def area(name, location, energy, color, size):
        bpy.ops.object.light_add(type="AREA", location=location)
        light = bpy.context.object
        light.name = name
        light.data.energy = energy
        light.data.color = color
        light.data.shape = "DISK"
        light.data.size = size
        light.rotation_euler = (Vector((0.0, 0.0, 0.0)) - light.location).to_track_quat("-Z", "Y").to_euler()
        return light

    # High-altitude sun: bright warm key, cool sky fill, strong under-bounce so
    # the white body never collapses into a flat shape.
    area("Cloud_KeyLight", (-4.4, -6.2, 10.8), 1180, (1.0, 0.95, 0.86), 6.5)
    area("Cloud_SkyFill", (7.0, -2.2, 1.4), 900, (0.62, 0.78, 1.0), 6.0)
    area("Cloud_RimLight", (0.0, 5.6, 7.2), 780, (1.0, 0.82, 0.72), 4.0)
    area("Cloud_UnderBounce", (2.0, -6.0, -6.4), 640, (0.72, 0.80, 1.0), 7.0)


def configure_render():
    scene = bpy.context.scene
    for engine in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.render.film_transparent = True
    scene.view_settings.view_transform = "AgX"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.16, 0.19, 0.26, 1.0)
    background.inputs["Strength"].default_value = 1.05


def export_and_render(planets):
    scene = bpy.context.scene
    camera = scene.camera
    original_camera_location = camera.location.copy()
    original_camera_rotation = camera.rotation_euler.copy()

    for planet in planets:
        original = planet.location.copy()
        planet.location = Vector((0.0, 0.0, 0.0))
        for other in planets:
            other.hide_render = other != planet

        camera.location = original_camera_location
        camera.rotation_euler = original_camera_rotation
        scene.render.filepath = os.path.join(SPRITE_DIR, planet.name + ".png")
        bpy.ops.render.render(write_still=True)

        bpy.ops.object.select_all(action="DESELECT")
        planet.select_set(True)
        bpy.context.view_layer.objects.active = planet
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(MODEL_DIR, planet.name + ".fbx"),
            use_selection=True,
            object_types={"MESH"},
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_ALL",
            bake_space_transform=False,
            mesh_smooth_type="FACE",
            use_mesh_modifiers=True,
            add_leaf_bones=False,
            path_mode="COPY",
            embed_textures=True,
            axis_forward="-Z",
            axis_up="Y",
        )
        planet.location = original

    for planet in planets:
        planet.hide_render = False


def validate(planets):
    report = []
    for planet in planets:
        triangles = sum(len(poly.vertices) - 2 for poly in planet.data.polygons)
        if len(planet.data.uv_layers) != 1 or len(planet.data.materials) != 1:
            raise RuntimeError(f"{planet.name}: expected one UV layer and one material")
        report.append((planet.name, triangles, len(planet.data.vertices)))
    return report


def main():
    ensure_dirs()
    clear_scene()
    palette_image = make_palette()
    temporary, final_material = make_materials(palette_image)
    planets = []

    for index, center_tuple in enumerate(GRID[:PLANET_COUNT]):
        name = f"Cloud_{index + 1:02d}"
        center = Vector(center_tuple)
        pieces = [base_planet(name, center, temporary, 8100 + index)]
        pieces += surface_details(center, temporary, 9100 + index, count=12)
        landmark = LANDMARKS[index](center, temporary)
        emphasize_landmark(landmark, center, index)
        pieces += landmark
        planet = join_planet(name, center, pieces, final_material, temporary)
        planets.append(planet)

    configure_render()
    add_camera_and_lights()
    report = validate(planets)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0:
            bpy.data.meshes.remove(mesh)
    for material in temporary:
        if material.users == 0:
            bpy.data.materials.remove(material)
    export_and_render(planets)
    bpy.ops.wm.save_as_mainfile(filepath=BLEND_PATH)
    print("CLOUD_COLLECTION_COMPLETE", report)


if __name__ == "__main__":
    main()
