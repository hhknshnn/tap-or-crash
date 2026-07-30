import bpy
import math
import os
import random
from mathutils import Vector, noise


ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
MODEL_DIR = os.path.join(ROOT, "Assets", "Models", "AlienPlanets")
SPRITE_DIR = os.path.join(ROOT, "Assets", "Sprites", "AlienPlanets")
BLEND_PATH = os.path.join(MODEL_DIR, "Alien_Planet_Collection.blend")
PALETTE_PATH = os.path.join(MODEL_DIR, "Alien_Palette.png")

# Only build this many planets (1 while art-directing, 10 for the full pack).
PLANET_COUNT = 10

# Dark chitin carrying acid and magenta glow. Every other pack is either a
# mineral or a plant world, so this one is deliberately fleshy.
PALETTE = [
    (0.150, 0.120, 0.215, 1.0),   # 0  chitin shadow
    (0.265, 0.190, 0.360, 1.0),   # 1  chitin
    (0.395, 0.275, 0.510, 1.0),   # 2  chitin lit
    (0.430, 0.190, 0.490, 1.0),   # 3  flesh
    (0.720, 0.260, 0.580, 1.0),   # 4  membrane
    (0.940, 0.180, 0.600, 1.0),   # 5  vein magenta
    (0.640, 0.860, 0.120, 1.0),   # 6  acid
    (0.290, 0.720, 0.300, 1.0),   # 7  toxic green
    (0.930, 0.940, 0.880, 1.0),   # 8  sclera
    (0.980, 0.720, 0.160, 1.0),   # 9  iris
    (0.045, 0.035, 0.070, 1.0),   # 10 pupil
    (0.840, 0.815, 0.720, 1.0),   # 11 bone
    (0.760, 1.000, 0.260, 1.0),   # 12 acid glow (emissive)
    (1.000, 0.220, 0.720, 1.0),   # 13 magenta glow (emissive)
    (0.300, 0.980, 0.940, 1.0),   # 14 cyan glow (emissive)
    (0.200, 0.470, 0.330, 1.0),   # 15 slime
]

GRID = [
    (-14.4, -3.6, 0.0), (-7.2, -3.6, 0.0), (0.0, -3.6, 0.0),
    (7.2, -3.6, 0.0), (14.4, -3.6, 0.0),
    (-14.4, 3.6, 0.0), (-7.2, 3.6, 0.0), (0.0, 3.6, 0.0),
    (7.2, 3.6, 0.0), (14.4, 3.6, 0.0),
]

# ~34 degrees off the camera axis, same as the rest of the packs.
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
    image = bpy.data.images.new("Alien_Palette", width=256, height=16, alpha=True)
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
        material = bpy.data.materials.new(f"Alien_Color_{index:02d}")
        material.diffuse_color = color
        material["palette_index"] = index
        temporary.append(material)

    material = bpy.data.materials.new("Alien_Palette_URP")
    material.use_nodes = True
    material.diffuse_color = PALETTE[1]
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = palette_image
    texture.interpolation = "Closest"
    # Wet flesh: glossy but not metal, so the chitin catches a slick highlight.
    principled.inputs["Roughness"].default_value = 0.34
    principled.inputs["Metallic"].default_value = 0.05
    links.new(texture.outputs["Color"], principled.inputs["Base Color"])

    # Veins and glands (indices 12-14) emit, keyed off the palette U coordinate.
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Strength"].default_value = 2.6
    mix = nodes.new("ShaderNodeMixShader")
    ramp = nodes.new("ShaderNodeValToRGB")
    separate = nodes.new("ShaderNodeSeparateXYZ")
    uv_node = nodes.new("ShaderNodeUVMap")
    links.new(texture.outputs["Color"], emission.inputs["Color"])
    links.new(uv_node.outputs["UV"], separate.inputs["Vector"])
    links.new(separate.outputs["X"], ramp.inputs["Fac"])
    ramp.color_ramp.interpolation = "CONSTANT"
    ramp.color_ramp.elements[0].position = 0.0
    ramp.color_ramp.elements[0].color = (0.0, 0.0, 0.0, 1.0)
    ramp.color_ramp.elements[1].position = 12.0 / 16.0
    ramp.color_ramp.elements[1].color = (1.0, 1.0, 1.0, 1.0)
    glow_end = ramp.color_ramp.elements.new(15.0 / 16.0)
    glow_end.color = (0.0, 0.0, 0.0, 1.0)
    links.new(ramp.outputs["Color"], mix.inputs["Fac"])
    links.new(principled.outputs["BSDF"], mix.inputs[1])
    links.new(emission.outputs["Emission"], mix.inputs[2])
    links.new(mix.outputs["Shader"], output.inputs["Surface"])
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
               temporary, sides=6, offset=0.5):
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
                   sides=10, offset=0.0):
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


# ── Signature silhouette: tendrils, pods and eyes ────────────────────────────
def tendril(name, base, direction, length, temporary, segments=3, radius=0.115,
            color=1, tip_color=13, curl=0.42):
    """A tapering chain that bends as it climbs. Nothing else in the game has a
    curved, asymmetric silhouette, which is what makes the pack read as alive."""
    direction = Vector(direction).normalized()
    tangent, _ = basis(direction)
    pieces = []
    cursor = Vector(base)
    heading = direction
    step = length / segments
    for i in range(segments):
        r0 = radius * (1.0 - 0.22 * i)
        r1 = radius * (1.0 - 0.22 * (i + 1))
        pieces.append(cone_piece(f"{name}_Seg{i}", cursor, heading, r0, r1, step,
                                 color, temporary, sides=6))
        cursor = cursor + heading * step
        heading = (heading + tangent * curl / segments).normalized()
    pieces.append(ico_piece(f"{name}_Tip", cursor + heading * radius * 0.4,
                            radius * 0.85, tip_color, temporary, subdivisions=1,
                            direction=heading))
    return pieces


def egg_pod(name, base, direction, radius, temporary, shell=3, glow=12):
    direction = Vector(direction).normalized()
    return [
        ico_piece(f"{name}_Shell", Vector(base) + direction * radius * 1.05, radius,
                  shell, temporary, subdivisions=1, scale=(0.86, 0.86, 1.35),
                  direction=direction),
        cone_piece(f"{name}_Mouth", Vector(base) + direction * radius * 2.05, direction,
                   radius * 0.46, radius * 0.14, radius * 0.55, glow, temporary,
                   sides=6),
    ]


def eye(name, base, direction, radius, temporary, stalk_length=0.34, stalk_color=3,
        iris_color=9):
    direction = Vector(direction).normalized()
    ball = Vector(base) + direction * (stalk_length + radius * 0.7)
    return [
        cone_piece(f"{name}_Stalk", base, direction, radius * 0.42, radius * 0.30,
                   stalk_length, stalk_color, temporary, sides=6),
        ico_piece(f"{name}_Ball", ball, radius, 8, temporary, subdivisions=2,
                  direction=direction),
        cylinder_piece(f"{name}_Iris", ball + direction * radius * 0.72, direction,
                       radius * 0.60, radius * 0.16, iris_color, temporary, sides=10),
        cylinder_piece(f"{name}_Pupil", ball + direction * radius * 0.84, direction,
                       radius * 0.26, radius * 0.16, 10, temporary, sides=8),
    ]


def maw(name, center, direction, radius, temporary, teeth=8, throat=13):
    direction = Vector(direction).normalized()
    tangent, bitangent = basis(direction)
    pieces = [cylinder_piece(f"{name}_Throat", center, direction, radius * 0.68, 0.07,
                             throat, temporary, sides=10, offset=0.03)]
    pieces.append(cylinder_piece(f"{name}_Gum", center, direction, radius, 0.09, 3,
                                 temporary, sides=10, offset=0.01))
    for i in range(teeth):
        angle = math.tau * i / teeth
        offset = (tangent * math.cos(angle) + bitangent * math.sin(angle)) * radius * 0.80
        lean = (direction - (tangent * math.cos(angle) + bitangent * math.sin(angle)) * 0.55).normalized()
        pieces.append(cone_piece(f"{name}_Tooth{i}", Vector(center) + offset, lean,
                                 radius * 0.16, 0.0, radius * 0.62, 11, temporary,
                                 sides=5))
    return pieces


def slime_pool(name, center, direction, radius, temporary):
    return [cylinder_piece(f"{name}_Pool", center, direction, radius, 0.06, 15,
                           temporary, sides=12, offset=0.02),
            cylinder_piece(f"{name}_Bloom", center, direction, radius * 0.55, 0.07, 12,
                           temporary, sides=10, offset=0.05)]


# ── Terrain ───────────────────────────────────────────────────────────────────
def base_planet(name, center, temporary, seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=RADIUS, location=center)
    sphere = bpy.context.object
    sphere.name = name + "_Sphere"
    add_slots(sphere, temporary)

    # Swollen and uneven: two noise octaves, the second one asymmetric, so the
    # body never settles into a clean sphere.
    offset = Vector((seed * 0.41, seed * 0.23, seed * 0.67))
    for vertex in sphere.data.vertices:
        direction = vertex.co.normalized()
        swell = noise.noise(direction * 1.7 + offset) * 0.66 \
            + noise.noise(direction * 3.9 + offset * 1.7) * 0.34
        vertex.co = direction * (RADIUS + swell * 0.24)

    vein_offset = Vector((seed * 0.71, seed * 0.37, seed * 0.19))
    flesh_offset = Vector((seed * 0.29, seed * 0.83, seed * 0.53))
    for polygon in sphere.data.polygons:
        normal = polygon.center.normalized()
        light = max(0.0, normal.z * 0.58 - normal.y * 0.28 + normal.x * 0.14)
        index = max(0, min(2, int(light * 3.4)))
        vein = noise.noise(normal * 2.6 + vein_offset)
        # A very narrow band around the zero crossing traces thin glowing veins.
        # Widen it even slightly and the hide turns into confetti.
        if abs(vein) < 0.016:
            index = 12 if vein >= 0.0 else 13
        elif noise.noise(normal * 2.3 + flesh_offset) > 0.54:
            index = 3
        polygon.material_index = index
    return sphere


def surface_details(center, temporary, seed, count=12):
    """The growth: pods, tendrils and watching eyes over the facing cap."""
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
        base = Vector(center) + direction * (RADIUS - 0.06)

        roll = i % 3
        if roll == 0:
            pieces += tendril(f"Tendril_{i:02d}", base, direction,
                              rng.uniform(0.62, 0.88), temporary, segments=3,
                              radius=rng.uniform(0.135, 0.180),
                              # Flesh, not chitin: a tendril in hide colour is
                              # invisible against the hide.
                              color=rng.choice((3, 4)),
                              tip_color=rng.choice((12, 13, 14)),
                              curl=rng.uniform(0.30, 0.62))
        elif roll == 1:
            pieces += egg_pod(f"Pod_{i:02d}", base, direction,
                              rng.uniform(0.23, 0.31), temporary,
                              shell=rng.choice((4, 3)),
                              glow=rng.choice((12, 13)))
        else:
            pieces += eye(f"Eye_{i:02d}", base, direction, rng.uniform(0.17, 0.23),
                          temporary, stalk_length=rng.uniform(0.26, 0.40),
                          iris_color=rng.choice((9, 6, 14)))
    return pieces


# ── Hero landmarks ────────────────────────────────────────────────────────────
def hero_origin(center, lift=0.0):
    return Vector(center) + HERO * (RADIUS - 0.06 + lift)


def landmark_01(center, temporary):
    """Great eye: one enormous eye on a fleshy mound, ringed by lashes."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [ico_piece("EyeMound", origin, 0.62, 3, temporary, subdivisions=2,
                        scale=(1.0, 1.0, 0.55), direction=HERO)]
    pieces += eye("GreatEye", origin + HERO * 0.16, HERO, 0.46, temporary,
                  stalk_length=0.24)
    for i in range(7):
        angle = math.tau * i / 7
        offset = tangent * math.cos(angle) * 0.74 + bitangent * math.sin(angle) * 0.74
        pieces += tendril(f"Lash{i}", origin + offset,
                          (HERO + offset.normalized() * 0.52).normalized(), 0.44,
                          temporary, segments=2, radius=0.070, tip_color=13, curl=0.6)
    return pieces


def landmark_02(center, temporary):
    """Hatchery: a brood of pods around a glowing vent."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [cylinder_piece("BroodVent", origin, HERO, 0.44, 0.07, 12, temporary,
                             sides=12, offset=0.03)]
    for i in range(6):
        angle = math.tau * i / 6 + 0.4
        offset = tangent * math.cos(angle) * 0.68 + bitangent * math.sin(angle) * 0.68
        pieces += egg_pod(f"BroodPod{i}", origin + offset,
                          (HERO + offset.normalized() * 0.26).normalized(),
                          0.24 + 0.03 * (i % 3), temporary,
                          glow=13 if i % 2 else 12)
    return pieces


def landmark_03(center, temporary):
    """Tendril nest: a writhing mass with nothing to anchor the eye."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [ico_piece("NestRoot", origin, 0.44, 1, temporary, subdivisions=2,
                        scale=(1.10, 1.10, 0.62), direction=HERO)]
    for i in range(8):
        angle = math.tau * i / 8 + 0.3
        offset = tangent * math.cos(angle) * 0.40 + bitangent * math.sin(angle) * 0.40
        pieces += tendril(f"NestArm{i}", origin + offset + HERO * 0.10,
                          (HERO + offset.normalized() * 0.34).normalized(),
                          0.72 + 0.10 * (i % 3), temporary, segments=3,
                          radius=0.105, tip_color=(12, 13, 14)[i % 3],
                          curl=0.55 + 0.12 * (i % 2))
    return pieces


def landmark_04(center, temporary):
    """Maw pit: a toothed mouth open in the hide."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = maw("HeroMaw", origin, HERO, 0.66, temporary, teeth=10)
    for i in range(3):
        angle = math.tau * i / 3 + 0.7
        offset = tangent * math.cos(angle) * 0.90 + bitangent * math.sin(angle) * 0.90
        pieces += tendril(f"MawFeeler{i}", origin + offset,
                          (HERO + offset.normalized() * 0.46).normalized(), 0.52,
                          temporary, segments=2, radius=0.085, tip_color=13)
    return pieces


def landmark_05(center, temporary):
    """Hive spire: a segmented tower with pods growing off it."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for i in range(4):
        lift = 0.22 * i
        pieces.append(ico_piece(f"SpireSeg{i}", origin + HERO * lift, 0.34 - 0.055 * i,
                                (1, 2, 3, 2)[i], temporary, subdivisions=1,
                                scale=(1.0, 1.0, 0.80), direction=HERO))
    pieces.append(cone_piece("SpireCrown", origin + HERO * 0.86, HERO, 0.20, 0.03,
                             0.40, 3, temporary, sides=6))
    pieces.append(ico_piece("SpireGland", origin + HERO * 1.24, 0.14, 13, temporary,
                            subdivisions=1, direction=HERO))
    for i, (angle, lift) in enumerate(((0.4, 0.24), (2.5, 0.46), (4.6, 0.16))):
        offset = tangent * math.cos(angle) * 0.36 + bitangent * math.sin(angle) * 0.36
        pieces += egg_pod(f"SpirePod{i}", origin + offset + HERO * lift,
                          (HERO + offset.normalized() * 0.60).normalized(), 0.19,
                          temporary, glow=12)
    return pieces


def landmark_06(center, temporary):
    """Eye cluster: a colony of stalks all turned the same way."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [ico_piece("ClusterBed", origin, 0.50, 3, temporary, subdivisions=2,
                        scale=(1.20, 1.20, 0.50), direction=HERO)]
    layout = ((0.0, 0.0, 0.30), (-0.44, 0.16, 0.22), (0.42, 0.12, 0.24),
              (-0.20, -0.42, 0.19), (0.26, -0.38, 0.20))
    for i, (u, v, radius) in enumerate(layout):
        offset = tangent * u + bitangent * v
        pieces += eye(f"ClusterEye{i}", origin + offset + HERO * 0.10,
                      (HERO + offset * 0.30).normalized(), radius, temporary,
                      stalk_length=0.26 + 0.08 * (i % 3),
                      iris_color=(9, 6, 14, 9, 6)[i])
    return pieces


def landmark_07(center, temporary):
    """Membrane sails: thin fins stretched between bone ribs."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for i, (u, tilt, color) in enumerate(((-0.42, 0.30, 4), (0.02, 0.0, 5), (0.46, -0.28, 4))):
        base = origin + tangent * u
        normal = (bitangent + tangent * tilt).normalized()
        pieces.append(arc_band_piece(f"Sail{i}", base + HERO * 0.06, normal, 0.62, 0.16,
                                     math.radians(74), 0.022, color, temporary,
                                     segments=10, tangent=HERO))
        pieces.append(cone_piece(f"SailRib{i}", base, (HERO + tangent * tilt).normalized(),
                                 0.075, 0.030, 0.78, 11, temporary, sides=6))
    pieces += slime_pool("SailRoot", origin - bitangent * 0.42, HERO, 0.42, temporary)
    return pieces


def landmark_08(center, temporary):
    """Slime pool: a lit basin with pods breaking the surface."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = slime_pool("HeroPool", origin, HERO, 0.72, temporary)
    for i, (u, v, radius) in enumerate(((-0.30, 0.12, 0.20), (0.26, -0.10, 0.17),
                                        (0.06, 0.34, 0.15))):
        offset = tangent * u + bitangent * v
        pieces += egg_pod(f"PoolPod{i}", origin + offset,
                          (HERO + offset * 0.34).normalized(), radius, temporary,
                          glow=14)
    pieces += tendril("PoolArm", origin + tangent * 0.84,
                      (HERO + tangent * 0.42).normalized(), 0.62, temporary,
                      segments=3, radius=0.100, tip_color=13)
    return pieces


def landmark_09(center, temporary):
    """Bone arch: a picked-clean ribcage over a dark hollow."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [cylinder_piece("ArchHollow", origin, HERO, 0.52, 0.06, 0, temporary,
                             sides=12, offset=0.02)]
    for i, scale in enumerate((0.92, 0.76, 0.60)):
        pieces.append(arc_band_piece(f"Rib{i}", origin + HERO * 0.04,
                                     bitangent + tangent * (i - 1) * 0.18,
                                     scale, scale - 0.09, math.radians(84), 0.045, 11,
                                     temporary, segments=10, tangent=tangent))
    pieces.append(ico_piece("ArchSkull", origin + tangent * 0.66 + HERO * 0.18, 0.28,
                            11, temporary, subdivisions=1, scale=(1.25, 0.95, 0.85),
                            direction=HERO))
    pieces.append(cylinder_piece("ArchSocket", origin + tangent * 0.66 + HERO * 0.30,
                                 (HERO + tangent * 0.30).normalized(), 0.10, 0.06, 13,
                                 temporary, sides=8))
    return pieces


def landmark_10(center, temporary):
    """Queen node: the finale — a crowned eye over a maw and a ring of pods."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = maw("QueenMaw", origin, HERO, 0.52, temporary, teeth=8)
    pieces += eye("QueenEye", origin + HERO * 0.36, HERO, 0.38, temporary,
                  stalk_length=0.42, iris_color=6)
    for i in range(6):
        angle = math.tau * i / 6 + 0.5
        offset = tangent * math.cos(angle) * 0.94 + bitangent * math.sin(angle) * 0.94
        if i % 2 == 0:
            pieces += egg_pod(f"QueenPod{i}", origin + offset,
                              (HERO + offset.normalized() * 0.30).normalized(), 0.21,
                              temporary, glow=13)
        else:
            pieces += tendril(f"QueenArm{i}", origin + offset,
                              (HERO + offset.normalized() * 0.44).normalized(), 0.66,
                              temporary, segments=3, radius=0.095, tip_color=12,
                              curl=0.58)
    return pieces


LANDMARKS = [
    landmark_01, landmark_02, landmark_03, landmark_04, landmark_05,
    landmark_06, landmark_07, landmark_08, landmark_09, landmark_10,
]


def emphasize_landmark(pieces, center, index):
    planar_factors = (1.74, 1.86, 1.72, 1.84, 1.70, 1.82, 1.86, 1.88, 1.84, 1.76)
    planar = planar_factors[index]
    radial = 1.60
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
    uv = planet.data.uv_layers.new(name="AlienPaletteUV")
    for polygon in planet.data.polygons:
        slot = planet.material_slots[polygon.material_index]
        material = slot.material
        index = int(material.get("palette_index", 1)) if material else 1
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
    planet["hero_landmark"] = name.replace("Alien_", "")
    planet["mobile_optimized"] = True
    planet["single_material"] = True
    return planet


def add_camera_and_lights():
    bpy.ops.object.camera_add(location=(8.2, -11.4, 7.8))
    camera = bpy.context.object
    camera.name = "Alien_RenderCamera"
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

    # Sickly light: a cold key, acid green bounce and a magenta rim, so nothing
    # on the planet reads as sunlit.
    area("Alien_KeyLight", (-4.6, -6.4, 10.6), 1240, (0.78, 0.86, 1.0), 5.5)
    area("Alien_AcidFill", (7.0, -2.4, 1.2), 1080, (0.62, 1.0, 0.38), 6.0)
    area("Alien_MagentaRim", (0.0, 5.6, 7.2), 1020, (1.0, 0.32, 0.78), 4.5)
    area("Alien_UnderBounce", (2.0, -6.0, -6.4), 700, (0.72, 0.40, 1.0), 7.0)


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
    background.inputs["Color"].default_value = (0.115, 0.065, 0.140, 1.0)
    background.inputs["Strength"].default_value = 0.90


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
        name = f"Alien_{index + 1:02d}"
        center = Vector(center_tuple)
        pieces = [base_planet(name, center, temporary, 12100 + index)]
        pieces += surface_details(center, temporary, 13100 + index, count=12)
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
    print("ALIEN_COLLECTION_COMPLETE", report)


if __name__ == "__main__":
    main()
