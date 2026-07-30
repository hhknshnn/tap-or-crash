import bpy
import math
import os
import random
from mathutils import Vector, noise


ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
MODEL_DIR = os.path.join(ROOT, "Assets", "Models", "MushroomPlanets")
SPRITE_DIR = os.path.join(ROOT, "Assets", "Sprites", "MushroomPlanets")
BLEND_PATH = os.path.join(MODEL_DIR, "Mushroom_Planet_Collection.blend")
PALETTE_PATH = os.path.join(MODEL_DIR, "Mushroom_Palette.png")

# Only build this many planets (1 while art-directing, 10 for the full pack).
PLANET_COUNT = 10

PALETTE = [
    (0.170, 0.135, 0.115, 1.0),   # 0  stalk shadow / bark
    # Damp humus floor. Deliberately warm-dark: Crystal already owns violet and
    # Natural owns green, so the ground reads as forest soil, not as either.
    (0.210, 0.150, 0.115, 1.0),   # 1  humus shadow
    (0.345, 0.245, 0.165, 1.0),   # 2  humus
    (0.480, 0.345, 0.215, 1.0),   # 3  lit humus
    (0.185, 0.360, 0.290, 1.0),   # 4  dark moss (kept away from Natural's grass)
    (0.660, 0.630, 0.510, 1.0),   # 5  pale mycelium
    (0.780, 0.140, 0.230, 1.0),   # 6  crimson cap
    (0.960, 0.330, 0.130, 1.0),   # 7  orange cap
    (0.720, 0.180, 0.620, 1.0),   # 8  magenta cap
    (0.420, 0.180, 0.640, 1.0),   # 9  violet cap
    (0.940, 0.900, 0.780, 1.0),   # 10 cream stalk / cap spots
    (0.700, 1.000, 0.220, 1.0),   # 11 bioluminescent lime
    (0.300, 1.000, 0.720, 1.0),   # 12 bioluminescent aqua
    (0.860, 1.000, 0.560, 1.0),   # 13 spore glow highlight
    (0.180, 0.520, 0.420, 1.0),   # 14 damp pool
    (0.400, 0.360, 0.330, 1.0),   # 15 wet stone
]

GRID = [
    (-14.4, -3.6, 0.0), (-7.2, -3.6, 0.0), (0.0, -3.6, 0.0),
    (7.2, -3.6, 0.0), (14.4, -3.6, 0.0),
    (-14.4, 3.6, 0.0), (-7.2, 3.6, 0.0), (0.0, 3.6, 0.0),
    (7.2, 3.6, 0.0), (14.4, 3.6, 0.0),
]

# ~34 degrees off the camera axis: head-on hides every stalk, steeper pushes the
# landmark out of the 1024 frame. See the Sakura pack for the same reasoning.
HERO = Vector((0.264, -0.367, 0.891)).normalized()
CAMERA_DIR = Vector((8.2, -11.4, 7.8)).normalized()
RADIUS = 3.0

CAP_COLORS = ((6, 10), (7, 10), (8, 13), (9, 13), (6, 13), (7, 11))


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
    image = bpy.data.images.new("Mushroom_Palette", width=256, height=16, alpha=True)
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
        material = bpy.data.materials.new(f"Mushroom_Color_{index:02d}")
        material.diffuse_color = color
        material["palette_index"] = index
        temporary.append(material)

    material = bpy.data.materials.new("Mushroom_Palette_URP")
    material.use_nodes = True
    material.diffuse_color = PALETTE[7]
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = palette_image
    texture.interpolation = "Closest"
    # Damp, waxy fungus: slightly glossier than Sakura's matte petals, no metal.
    principled.inputs["Roughness"].default_value = 0.45
    principled.inputs["Metallic"].default_value = 0.0
    # The glow colours are pushed through emission so the gills and spore caps
    # still read as bioluminescent after the AgX view transform.
    emission = nodes.new("ShaderNodeEmission")
    mix = nodes.new("ShaderNodeMixShader")
    ramp = nodes.new("ShaderNodeValToRGB")
    separate = nodes.new("ShaderNodeSeparateXYZ")
    geometry = nodes.new("ShaderNodeNewGeometry")
    links.new(texture.outputs["Color"], principled.inputs["Base Color"])
    links.new(texture.outputs["Color"], emission.inputs["Color"])
    emission.inputs["Strength"].default_value = 1.9
    # Emission is keyed off the palette U coordinate: only the glow texels
    # (indices 11-13) sit in the upper band of the ramp.
    uv_node = nodes.new("ShaderNodeUVMap")
    links.new(uv_node.outputs["UV"], separate.inputs["Vector"])
    links.new(separate.outputs["X"], ramp.inputs["Fac"])
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0.0, 0.0, 0.0, 1.0)
    ramp.color_ramp.elements[1].position = 11.0 / 16.0
    ramp.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)
    glow_end = ramp.color_ramp.elements.new(14.0 / 16.0)
    glow_end.color = (0.0, 0.0, 0.0, 1.0)
    links.new(ramp.outputs["Color"], mix.inputs["Fac"])
    links.new(principled.outputs["BSDF"], mix.inputs[1])
    links.new(emission.outputs["Emission"], mix.inputs[2])
    links.new(mix.outputs["Shader"], output.inputs["Surface"])
    nodes.remove(geometry)
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


# ── Signature silhouette: capped mushrooms with glowing gills ─────────────────
def mushroom(name, base, direction, stalk_length, cap_radius, temporary,
             cap_color=6, spot_color=10, glow_color=11, subdivisions=1,
             spots=0, gills=True, stalk_color=10):
    """Stalk, dome cap and a bright gill disc tucked underneath. The gill disc is
    what separates this pack from any other round-topped theme at sprite size."""
    direction = Vector(direction).normalized()
    apex = Vector(base) + direction * stalk_length
    pieces = [cone_piece(f"{name}_Stalk", base, direction, cap_radius * 0.28,
                         cap_radius * 0.19, stalk_length, stalk_color, temporary,
                         sides=6)]
    if gills:
        # Sits proud of the cap rim: a gill disc tucked underneath is invisible
        # from the render camera, which looks down on the planet.
        pieces.append(cylinder_piece(f"{name}_Gills", apex - direction * (cap_radius * 0.16),
                                     direction, cap_radius * 1.12, cap_radius * 0.09,
                                     glow_color, temporary, sides=10))
    pieces.append(ico_piece(f"{name}_Cap", apex, cap_radius, cap_color, temporary,
                            subdivisions=subdivisions, scale=(1.34, 1.34, 0.40),
                            direction=direction))
    tangent, bitangent = basis(direction)
    for i in range(spots):
        angle = math.tau * i / max(1, spots) + 0.7
        offset = (tangent * math.cos(angle) + bitangent * math.sin(angle)) * cap_radius * 0.52
        pieces.append(ico_piece(f"{name}_Spot{i}", apex + offset + direction * cap_radius * 0.38,
                                cap_radius * 0.20, spot_color, temporary,
                                subdivisions=1, scale=(1.0, 1.0, 0.55),
                                direction=direction))
    return pieces


def puffball(name, center, direction, radius, temporary, color=5, glow=13):
    direction = Vector(direction).normalized()
    pieces = [ico_piece(name, Vector(center) + direction * radius * 0.55, radius, color,
                        temporary, subdivisions=1, scale=(1.0, 1.0, 0.86),
                        direction=direction)]
    pieces.append(ico_piece(f"{name}_Vent", Vector(center) + direction * radius * 1.15,
                            radius * 0.32, glow, temporary, subdivisions=1,
                            scale=(1.0, 1.0, 0.5), direction=direction))
    return pieces


def glow_pool(name, center, direction, radius, temporary):
    return [cylinder_piece(f"{name}_Water", center, direction, radius, 0.06, 14,
                           temporary, sides=12, offset=0.02),
            cylinder_piece(f"{name}_Shine", center, direction, radius * 0.62, 0.07, 12,
                           temporary, sides=10, offset=0.05)]


def shelf_fungus(name, trunk_base, direction, height, temporary, tiers=3):
    direction = Vector(direction).normalized()
    tangent, bitangent = basis(direction)
    pieces = [cone_piece(f"{name}_Trunk", trunk_base, direction, 0.20, 0.15, height,
                         0, temporary, sides=6)]
    for i in range(tiers):
        lift = height * (0.26 + 0.23 * i)
        side = 1 if i % 2 == 0 else -1
        offset = tangent * side * 0.24 + bitangent * (0.07 * side)
        # Wide, thin, alternating shelves: the staircase silhouette is the only
        # thing that separates this landmark from a plain cap cluster.
        pieces.append(ico_piece(f"{name}_Shelf{i}", Vector(trunk_base) + direction * lift + offset,
                                0.46 - 0.06 * i, (7, 6, 8)[i % 3], temporary,
                                subdivisions=1, scale=(1.55, 1.15, 0.20),
                                direction=direction))
    pieces.append(ico_piece(f"{name}_Crown", Vector(trunk_base) + direction * height * 1.02,
                            0.22, 11, temporary, subdivisions=1, scale=(1.2, 1.2, 0.5),
                            direction=direction))
    return pieces


# ── Terrain ───────────────────────────────────────────────────────────────────
def base_planet(name, center, temporary, seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=RADIUS, location=center)
    sphere = bpy.context.object
    sphere.name = name + "_Sphere"
    add_slots(sphere, temporary)

    # Lumpy damp forest floor: stronger than Sakura's rolling hills so the
    # silhouette reads as something organic and overgrown.
    offset = Vector((seed * 0.29, seed * 0.61, seed * 0.13))
    for vertex in sphere.data.vertices:
        direction = vertex.co.normalized()
        lump = noise.noise(direction * 2.6 + offset)
        vertex.co = direction * (RADIUS + lump * 0.19)

    moss_offset = Vector((seed * 0.77, seed * 0.31, seed * 0.53))
    mycelium_offset = Vector((seed * 0.19, seed * 0.87, seed * 0.41))
    for polygon in sphere.data.polygons:
        normal = polygon.center.normalized()
        light = max(0.0, normal.z * 0.55 - normal.y * 0.30 + normal.x * 0.12)
        index = max(1, min(3, 1 + int(light * 3.2)))
        if noise.noise(normal * 2.9 + moss_offset) > 0.34:
            index = 4
        elif noise.noise(normal * 3.6 + mycelium_offset) > 0.36:
            index = 5
        polygon.material_index = index
    return sphere


def surface_details(center, temporary, seed, count=14):
    """The colony. Caps scattered over the camera-facing cap carry the theme."""
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
        base = Vector(center) + direction * (RADIUS - 0.05)
        cap_color, spot_color = CAP_COLORS[rng.randrange(len(CAP_COLORS))]
        pieces += mushroom(
            f"Colony_{i:02d}", base, direction,
            rng.uniform(0.44, 0.62), rng.uniform(0.32, 0.42), temporary,
            cap_color=cap_color, spot_color=spot_color,
            glow_color=11 if i % 2 else 12, subdivisions=1,
            spots=1 if rng.random() < 0.45 else 0)
    return pieces


# ── Hero landmarks ────────────────────────────────────────────────────────────
def hero_origin(center, lift=0.0):
    return Vector(center) + HERO * (RADIUS - 0.06 + lift)


def landmark_01(center, temporary):
    """Great Cap: one enormous crimson mushroom over a spore-lit clearing."""
    origin = hero_origin(center)
    pieces = mushroom("GreatCap", origin, HERO, 0.72, 0.66, temporary,
                      cap_color=6, spot_color=10, glow_color=11, subdivisions=2,
                      spots=4)
    tangent, bitangent = basis(HERO)
    pieces.append(cylinder_piece("GreatClearing", origin, HERO, 0.74, 0.05, 11,
                                 temporary, sides=12, offset=0.03))
    for i in range(3):
        angle = math.tau * i / 3 + 0.5
        offset = tangent * math.cos(angle) * 0.82 + bitangent * math.sin(angle) * 0.82
        pieces += mushroom(f"GreatSpawn{i}", origin + offset,
                           (HERO + offset.normalized() * 0.34).normalized(),
                           0.32, 0.28, temporary, cap_color=7, spot_color=10,
                           glow_color=12, spots=1)
    return pieces


def landmark_02(center, temporary):
    """Fairy ring: a closed circle of caps around a glowing centre."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for i in range(8):
        angle = math.tau * i / 8
        offset = tangent * math.cos(angle) * 0.78 + bitangent * math.sin(angle) * 0.78
        pieces += mushroom(f"RingCap{i}", origin + offset,
                           (HERO + offset.normalized() * 0.26).normalized(),
                           0.34, 0.28, temporary,
                           cap_color=(6, 8, 7, 9)[i % 4], spot_color=10,
                           glow_color=11 if i % 2 else 12)
    pieces.append(cylinder_piece("RingGlow", origin, HERO, 0.52, 0.06, 11,
                                 temporary, sides=12, offset=0.03))
    return pieces


def landmark_03(center, temporary):
    """Spore pool: a lit pond ringed with puffballs."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = glow_pool("SporePool", origin, HERO, 0.62, temporary)
    for i in range(5):
        angle = math.tau * i / 5 + 0.4
        offset = tangent * math.cos(angle) * 0.80 + bitangent * math.sin(angle) * 0.80
        pieces += puffball(f"PoolPuff{i}", origin + offset,
                           (HERO + offset.normalized() * 0.30).normalized(),
                           0.20 + 0.04 * (i % 3), temporary)
    pieces += mushroom("PoolCap", origin + tangent * 0.86 - bitangent * 0.22,
                       (HERO + tangent * 0.28).normalized(), 0.46, 0.36, temporary,
                       cap_color=8, spot_color=13, glow_color=12, spots=2)
    return pieces


def landmark_04(center, temporary):
    """Toadstool arch: two leaning caps meeting over a lit path."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for side in (-1, 1):
        lean = (HERO - tangent * side * 0.42).normalized()
        pieces += mushroom(f"ArchCap{side}", origin + tangent * side * 0.62, lean,
                           0.72, 0.40, temporary, cap_color=7 if side < 0 else 6,
                           spot_color=10, glow_color=11, subdivisions=1, spots=2)
    pieces.append(arc_band_piece("ArchLight", origin + HERO * 0.06, bitangent,
                                 0.60, 0.50, math.radians(64), 0.055, 13, temporary,
                                 segments=10, tangent=tangent))
    return pieces


def landmark_05(center, temporary):
    """Spore tower: stacked shelves climbing a dead trunk."""
    origin = hero_origin(center)
    tangent, _ = basis(HERO)
    pieces = shelf_fungus("SporeTower", origin, HERO, 0.92, temporary, tiers=4)
    pieces += mushroom("TowerGuard", origin + tangent * 0.72,
                       (HERO + tangent * 0.26).normalized(), 0.38, 0.32, temporary,
                       cap_color=9, spot_color=13, glow_color=12, spots=1)
    return pieces


def landmark_06(center, temporary):
    """Colony bloom: a dense cluster of mismatched caps."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    layout = ((0.0, 0.0, 0.58, 0.44), (-0.60, 0.14, 0.42, 0.32),
              (0.58, 0.10, 0.44, 0.34), (-0.30, -0.50, 0.34, 0.26),
              (0.34, -0.48, 0.36, 0.28))
    for i, (u, v, stalk, radius) in enumerate(layout):
        offset = tangent * u + bitangent * v
        pieces += mushroom(f"BloomCap{i}", origin + offset,
                           (HERO + offset * 0.32).normalized(), stalk, radius,
                           temporary, cap_color=(6, 7, 8, 9, 7)[i], spot_color=10,
                           glow_color=11 if i % 2 else 12,
                           subdivisions=2 if i == 0 else 1, spots=2 if i == 0 else 0)
    return pieces


def landmark_07(center, temporary):
    """Puffball field: a bank of spore sacs venting light."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for i, (u, v, radius) in enumerate(((0.0, 0.0, 0.40), (-0.52, 0.18, 0.30),
                                        (0.50, 0.14, 0.32), (-0.24, -0.46, 0.24),
                                        (0.30, -0.44, 0.26), (0.02, 0.52, 0.22))):
        offset = tangent * u + bitangent * v
        pieces += puffball(f"FieldPuff{i}", origin + offset,
                           (HERO + offset * 0.30).normalized(), radius, temporary,
                           color=5 if i % 2 else 10, glow=13 if i % 2 else 11)
    return pieces


def landmark_08(center, temporary):
    """Mycelium web: glowing threads strung between two caps."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for side in (-1, 1):
        pieces += mushroom(f"WebCap{side}", origin + tangent * side * 0.70,
                           (HERO + tangent * side * 0.22).normalized(),
                           0.62, 0.34, temporary, cap_color=9 if side < 0 else 8,
                           spot_color=13, glow_color=12, spots=1)
    for i in range(3):
        lift = 0.34 + i * 0.16
        pieces.append(box_piece(f"WebThread{i}", tuple(origin + HERO * lift
                                                      + bitangent * (i - 1) * 0.12),
                                (0.70, 0.030, 0.030), 11, temporary, direction=HERO))
    pieces.append(ico_piece("WebNode", origin + HERO * 0.52, 0.16, 13, temporary,
                            subdivisions=1, direction=HERO))
    return pieces


def landmark_09(center, temporary):
    """Sunken hollow: a rotted stump ringed by small caps and a lit pool."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = glow_pool("HollowPool", origin - bitangent * 0.08, HERO, 0.50, temporary)
    pieces.append(cylinder_piece("HollowStump", origin + tangent * 0.56, HERO,
                                 0.30, 0.44, 0, temporary, sides=8, offset=0.22))
    pieces.append(cylinder_piece("HollowRot", origin + tangent * 0.56, HERO,
                                 0.22, 0.06, 12, temporary, sides=8, offset=0.44))
    for i in range(4):
        angle = math.tau * i / 4 + 0.9
        offset = tangent * math.cos(angle) * 0.74 + bitangent * math.sin(angle) * 0.74
        pieces += mushroom(f"HollowCap{i}", origin + offset,
                           (HERO + offset.normalized() * 0.30).normalized(),
                           0.30, 0.24, temporary, cap_color=(7, 6, 8, 9)[i],
                           spot_color=10, glow_color=11)
    return pieces


def landmark_10(center, temporary):
    """Mother cap: the finale — a towering cap over a lit ring of spawn."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = mushroom("MotherCap", origin, HERO, 0.86, 0.74, temporary,
                      cap_color=8, spot_color=13, glow_color=12, subdivisions=2,
                      spots=5)
    for i in range(6):
        angle = math.tau * i / 6 + 0.3
        offset = tangent * math.cos(angle) * 0.92 + bitangent * math.sin(angle) * 0.92
        pieces += mushroom(f"MotherSpawn{i}", origin + offset,
                           (HERO + offset.normalized() * 0.32).normalized(),
                           0.30, 0.24, temporary, cap_color=(6, 9, 7)[i % 3],
                           spot_color=10, glow_color=11 if i % 2 else 13)
    pieces.append(cylinder_piece("MotherGlow", origin, HERO, 0.66, 0.05, 11,
                                 temporary, sides=12, offset=0.03))
    return pieces


LANDMARKS = [
    landmark_01, landmark_02, landmark_03, landmark_04, landmark_05,
    landmark_06, landmark_07, landmark_08, landmark_09, landmark_10,
]


def emphasize_landmark(pieces, center, index):
    planar_factors = (1.86, 2.05, 2.10, 2.00, 1.90, 1.82, 2.05, 2.00, 2.10, 1.86)
    planar = planar_factors[index]
    # Height amplified: a landmark lying flat on the surface disappears into the
    # colony at sprite size.
    radial = 1.72
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
    uv = planet.data.uv_layers.new(name="MushroomPaletteUV")
    for polygon in planet.data.polygons:
        slot = planet.material_slots[polygon.material_index]
        material = slot.material
        index = int(material.get("palette_index", 2)) if material else 2
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
    planet["hero_landmark"] = name.replace("Mushroom_", "")
    planet["mobile_optimized"] = True
    planet["single_material"] = True
    return planet


def add_camera_and_lights():
    bpy.ops.object.camera_add(location=(8.2, -11.4, 7.8))
    camera = bpy.context.object
    camera.name = "Mushroom_RenderCamera"
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

    # Damp undergrowth: cool overcast key, lime bounce from the glowing gills.
    area("Mushroom_KeyLight", (-4.6, -6.4, 10.6), 1280, (0.86, 0.90, 1.0), 6.0)
    area("Mushroom_SporeFill", (7.0, -2.4, 1.2), 1150, (0.62, 1.0, 0.52), 6.0)
    area("Mushroom_RimLight", (0.0, 5.6, 7.2), 900, (0.72, 0.86, 1.0), 4.0)
    area("Mushroom_UnderBounce", (2.0, -6.0, -6.4), 720, (0.55, 0.95, 0.80), 7.0)


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
    background.inputs["Color"].default_value = (0.075, 0.115, 0.075, 1.0)
    background.inputs["Strength"].default_value = 0.85


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
        name = f"Mushroom_{index + 1:02d}"
        center = Vector(center_tuple)
        pieces = [base_planet(name, center, temporary, 6100 + index)]
        pieces += surface_details(center, temporary, 7100 + index, count=12)
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
    print("MUSHROOM_COLLECTION_COMPLETE", report)


if __name__ == "__main__":
    main()
