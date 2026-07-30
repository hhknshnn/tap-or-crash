import bpy
import math
import os
import random
from mathutils import Vector, noise


ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
MODEL_DIR = os.path.join(ROOT, "Assets", "Models", "MechanicalPlanets")
SPRITE_DIR = os.path.join(ROOT, "Assets", "Sprites", "MechanicalPlanets")
BLEND_PATH = os.path.join(MODEL_DIR, "Mechanical_Planet_Collection.blend")
PALETTE_PATH = os.path.join(MODEL_DIR, "Mechanical_Palette.png")

# Only build this many planets (1 while art-directing, 10 for the full pack).
PLANET_COUNT = 10

# Gunmetal and brass with hot signal colours. Hard-surface, so the palette is
# split by material (steel / brass / copper) rather than by light level.
PALETTE = [
    (0.175, 0.190, 0.220, 1.0),   # 0  deep shadow steel
    (0.290, 0.310, 0.350, 1.0),   # 1  hull shadow
    (0.410, 0.435, 0.480, 1.0),   # 2  hull
    (0.545, 0.575, 0.625, 1.0),   # 3  hull lit
    (0.560, 0.590, 0.640, 1.0),   # 4  polished steel
    (0.730, 0.755, 0.790, 1.0),   # 5  chrome highlight
    (0.640, 0.435, 0.185, 1.0),   # 6  brass
    (0.840, 0.615, 0.265, 1.0),   # 7  brass lit
    (0.560, 0.290, 0.155, 1.0),   # 8  copper
    (0.310, 0.220, 0.165, 1.0),   # 9  rust
    (0.930, 0.780, 0.320, 1.0),   # 10 warning yellow
    (0.850, 0.185, 0.145, 1.0),   # 11 signal red
    (1.000, 0.520, 0.120, 1.0),   # 12 hot vent orange (emissive)
    (0.250, 0.930, 1.000, 1.0),   # 13 coolant cyan (emissive)
    (0.640, 0.980, 0.400, 1.0),   # 14 status green (emissive)
    (0.080, 0.085, 0.100, 1.0),   # 15 pitch black gap
]

GRID = [
    (-14.4, -3.6, 0.0), (-7.2, -3.6, 0.0), (0.0, -3.6, 0.0),
    (7.2, -3.6, 0.0), (14.4, -3.6, 0.0),
    (-14.4, 3.6, 0.0), (-7.2, 3.6, 0.0), (0.0, 3.6, 0.0),
    (7.2, 3.6, 0.0), (14.4, 3.6, 0.0),
]

# ~34 degrees off the camera axis, same as the other packs.
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
    image = bpy.data.images.new("Mechanical_Palette", width=256, height=16, alpha=True)
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
        material = bpy.data.materials.new(f"Mechanical_Color_{index:02d}")
        material.diffuse_color = color
        material["palette_index"] = index
        temporary.append(material)

    material = bpy.data.materials.new("Mechanical_Palette_URP")
    material.use_nodes = True
    material.diffuse_color = PALETTE[2]
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = palette_image
    texture.interpolation = "Closest"
    # Machined metal: tight highlights, high metallic. The one pack that is
    # allowed to look reflective.
    principled.inputs["Roughness"].default_value = 0.28
    principled.inputs["Metallic"].default_value = 0.85
    links.new(texture.outputs["Color"], principled.inputs["Base Color"])

    # Vents, coolant and status lights (indices 12-14) emit; everything else is
    # plain metal. Keyed off the palette U coordinate, as in the Mushroom pack.
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Strength"].default_value = 2.4
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


def torus_piece(name, location, major_radius, minor_radius, color, temporary,
                rotation=(0.0, 0.0, 0.0), major_segments=14, minor_segments=4):
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


# ── Signature silhouette: gear teeth, plating and lit vents ──────────────────
def gear(name, center, direction, radius, temporary, teeth=8, color=6,
         hub_color=4, thickness=0.075, bore_color=15):
    """A toothed disc. Repeated across the globe it is the read that no other
    pack can produce — a hard, regular silhouette against soft themes."""
    direction = Vector(direction).normalized()
    tangent, bitangent = basis(direction)
    pieces = [cylinder_piece(f"{name}_Body", center, direction, radius, thickness * 2.0,
                             color, temporary, sides=10)]
    for i in range(teeth):
        angle = math.tau * i / teeth
        offset = (tangent * math.cos(angle) + bitangent * math.sin(angle)) * radius * 1.10
        tooth = box_piece(f"{name}_Tooth{i}", tuple(Vector(center) + offset),
                          (radius * 0.20, radius * 0.16, thickness), color, temporary,
                          direction=direction)
        tooth.rotation_mode = "QUATERNION"
        tooth.rotation_quaternion = (
            Vector((0.0, 0.0, 1.0)).rotation_difference(direction)
            @ __import__("mathutils").Quaternion((0.0, 0.0, 1.0), angle))
        pieces.append(tooth)
    pieces.append(cylinder_piece(f"{name}_Hub", center, direction, radius * 0.34,
                                 thickness * 2.6, hub_color, temporary, sides=8))
    pieces.append(cylinder_piece(f"{name}_Bore", center, direction, radius * 0.15,
                                 thickness * 3.0, bore_color, temporary, sides=6))
    return pieces


def hull_plate(name, base, direction, size, temporary, color=2, trim=10,
               height=0.10, lit=None):
    """A raised armour panel with a painted trim edge, and an optional lit strip."""
    direction = Vector(direction).normalized()
    pieces = [box_piece(f"{name}_Plate", tuple(Vector(base) + direction * height * 0.5),
                        (size, size * 0.72, height), color, temporary, direction=direction)]
    pieces.append(box_piece(f"{name}_Trim", tuple(Vector(base) + direction * height * 1.02),
                            (size * 0.92, size * 0.20, height * 0.30), trim, temporary,
                            direction=direction))
    if lit is not None:
        pieces.append(box_piece(f"{name}_Strip",
                                tuple(Vector(base) + direction * height * 1.10),
                                (size * 0.62, size * 0.085, height * 0.22), lit,
                                temporary, direction=direction))
    return pieces


def pipe_run(name, base, direction, length, temporary, color=4, joint_color=8,
             radius=0.075):
    direction = Vector(direction).normalized()
    return [
        cylinder_piece(f"{name}_Pipe", base, direction, radius, length, color,
                       temporary, sides=8, offset=length * 0.5),
        cylinder_piece(f"{name}_Collar", base, direction, radius * 1.45, radius * 0.9,
                       joint_color, temporary, sides=8, offset=length * 0.22),
        cylinder_piece(f"{name}_Cap", base, direction, radius * 1.35, radius * 0.8,
                       joint_color, temporary, sides=8, offset=length * 0.88),
    ]


def vent_stack(name, base, direction, height, temporary, glow=12):
    direction = Vector(direction).normalized()
    return [
        cone_piece(f"{name}_Stack", base, direction, 0.135, 0.105, height, 1, temporary,
                   sides=8),
        cylinder_piece(f"{name}_Ring", Vector(base) + direction * height * 0.82,
                       direction, 0.175, 0.050, 9, temporary, sides=8),
        # Wide, proud mouth: a recessed vent glow is invisible at sprite size.
        cylinder_piece(f"{name}_Mouth", Vector(base) + direction * (height + 0.05),
                       direction, 0.150, 0.075, glow, temporary, sides=8),
    ]


def antenna(name, base, direction, height, temporary, color=4, light=11):
    direction = Vector(direction).normalized()
    return [
        cone_piece(f"{name}_Mast", base, direction, 0.045, 0.022, height, color,
                   temporary, sides=6),
        ico_piece(f"{name}_Lamp", Vector(base) + direction * (height + 0.05), 0.065,
                  light, temporary, subdivisions=1, direction=direction),
    ]


# ── Terrain ───────────────────────────────────────────────────────────────────
def base_planet(name, center, temporary, seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=RADIUS, location=center)
    sphere = bpy.context.object
    sphere.name = name + "_Sphere"
    add_slots(sphere, temporary)

    # Machined, not eroded: the surface steps between flat plate levels instead
    # of rolling, which is what makes it read as built rather than grown.
    offset = Vector((seed * 0.37, seed * 0.19, seed * 0.53))
    for vertex in sphere.data.vertices:
        direction = vertex.co.normalized()
        raw = noise.noise(direction * 2.2 + offset)
        stepped = round(raw * 3.0) / 3.0
        vertex.co = direction * (RADIUS + stepped * 0.13)

    panel_offset = Vector((seed * 0.61, seed * 0.29, seed * 0.83))
    wear_offset = Vector((seed * 0.17, seed * 0.71, seed * 0.31))
    for polygon in sphere.data.polygons:
        normal = polygon.center.normalized()
        light = max(0.0, normal.z * 0.58 - normal.y * 0.28 + normal.x * 0.14)
        index = max(0, min(3, int(light * 4.6)))
        panel = noise.noise(normal * 3.4 + panel_offset)
        if panel > 0.40:
            index = 4 if light > 0.42 else 1
        elif panel < -0.44:
            index = 15
        elif noise.noise(normal * 4.6 + wear_offset) > 0.46:
            index = 9
        polygon.material_index = index
    return sphere


def surface_details(center, temporary, seed, count=12):
    """Plating, pipes and gears bolted over the camera-facing cap."""
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

        roll = i % 4
        if roll == 0:
            pieces += gear(f"Gear_{i:02d}", base + direction * 0.06, direction,
                           rng.uniform(0.24, 0.34), temporary, teeth=8,
                           color=rng.choice((6, 4)), hub_color=8)
        elif roll == 1:
            pieces += hull_plate(f"Plate_{i:02d}", base, direction,
                                 rng.uniform(0.30, 0.42), temporary,
                                 color=rng.choice((2, 3)), trim=rng.choice((10, 11)),
                                 lit=rng.choice((13, 14, None)))
        elif roll == 2:
            pieces += vent_stack(f"Vent_{i:02d}", base, direction,
                                 rng.uniform(0.30, 0.44), temporary)
        else:
            pieces += pipe_run(f"Pipe_{i:02d}", base, direction,
                               rng.uniform(0.34, 0.50), temporary)
    return pieces


# ── Hero landmarks ────────────────────────────────────────────────────────────
def hero_origin(center, lift=0.0):
    return Vector(center) + HERO * (RADIUS - 0.06 + lift)


def landmark_01(center, temporary):
    """Great gear: one enormous brass wheel driving smaller ones."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = gear("GreatGear", origin + HERO * 0.14, HERO, 0.68, temporary, teeth=12,
                  color=7, hub_color=8, thickness=0.095)
    for i, (u, v, radius) in enumerate(((-0.86, 0.30, 0.30), (0.84, -0.26, 0.32))):
        offset = tangent * u + bitangent * v
        pieces += gear(f"GreatIdler{i}", origin + offset + HERO * 0.10,
                       (HERO + offset * 0.22).normalized(), radius, temporary,
                       teeth=8, color=4, hub_color=6)
    pieces += vent_stack("GreatVent", origin - bitangent * 0.70, HERO, 0.42, temporary)
    return pieces


def landmark_02(center, temporary):
    """Reactor core: a caged cyan cell between coolant rings."""
    origin = hero_origin(center, lift=0.10)
    tangent, bitangent = basis(HERO)
    pieces = [ico_piece("ReactorCell", origin + HERO * 0.34, 0.36, 13, temporary,
                        subdivisions=2, direction=HERO)]
    pieces.append(torus_piece("ReactorRing", tuple(origin + HERO * 0.34), 0.56, 0.075, 4,
                              temporary, rotation=(math.radians(62), 0.0, 0.0),
                              major_segments=16, minor_segments=4))
    for i in range(4):
        angle = math.tau * i / 4 + 0.4
        offset = tangent * math.cos(angle) * 0.46 + bitangent * math.sin(angle) * 0.46
        pieces.append(cone_piece(f"ReactorStrut{i}", origin + offset, HERO, 0.070, 0.050,
                                 0.72, 2, temporary, sides=6))
    pieces += pipe_run("ReactorFeed", origin + tangent * 0.78,
                       (HERO + tangent * 0.30).normalized(), 0.52, temporary,
                       joint_color=13)
    return pieces


def landmark_03(center, temporary):
    """Piston bank: three rods at different heights, mid-stroke."""
    origin = hero_origin(center)
    tangent, _ = basis(HERO)
    pieces = [box_piece("PistonBlock", tuple(origin + HERO * 0.12),
                        (0.62, 0.30, 0.14), 1, temporary, direction=HERO)]
    for i, stroke in enumerate((0.66, 0.44, 0.80)):
        base = origin + tangent * (i - 1) * 0.40 + HERO * 0.20
        pieces.append(cylinder_piece(f"PistonSleeve{i}", base, HERO, 0.115, 0.34, 2,
                                     temporary, sides=8, offset=0.17))
        pieces.append(cylinder_piece(f"PistonRod{i}", base, HERO, 0.062, stroke, 5,
                                     temporary, sides=8, offset=stroke * 0.5 + 0.16))
        pieces.append(cylinder_piece(f"PistonHead{i}", base + HERO * (stroke + 0.18),
                                     HERO, 0.135, 0.075, 6, temporary, sides=8))
    pieces.append(box_piece("PistonRail", tuple(origin + HERO * 0.06),
                            (0.66, 0.10, 0.045), 10, temporary, direction=HERO))
    return pieces


def landmark_04(center, temporary):
    """Radar array: a dish on a mast, ringed by lit markers."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [cone_piece("RadarMast", origin, HERO, 0.115, 0.085, 0.72, 2, temporary,
                         sides=8)]
    dish = cone_piece("RadarDish", origin + HERO * 0.76,
                      (HERO + tangent * 0.36).normalized(), 0.52, 0.14, 0.24, 4,
                      temporary, sides=10)
    pieces.append(dish)
    pieces.append(ico_piece("RadarFeed", origin + HERO * 0.98 + tangent * 0.16, 0.075,
                            12, temporary, subdivisions=1, direction=HERO))
    for i in range(4):
        angle = math.tau * i / 4 + 0.6
        offset = tangent * math.cos(angle) * 0.72 + bitangent * math.sin(angle) * 0.72
        pieces += antenna(f"RadarMarker{i}", origin + offset,
                          (HERO + offset.normalized() * 0.22).normalized(), 0.30,
                          temporary, light=11 if i % 2 else 14)
    return pieces


def landmark_05(center, temporary):
    """Foundry: an open furnace throwing orange light onto its stacks."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [box_piece("FoundryBody", tuple(origin + HERO * 0.24),
                        (0.56, 0.40, 0.26), 1, temporary, direction=HERO)]
    pieces.append(box_piece("FoundryMouth", tuple(origin + HERO * 0.24 - bitangent * 0.40),
                            (0.34, 0.055, 0.18), 12, temporary, direction=HERO))
    for i, u in enumerate((-0.34, 0.0, 0.34)):
        pieces += vent_stack(f"FoundryStack{i}", origin + tangent * u + HERO * 0.46,
                             HERO, 0.40 + 0.08 * (i % 2), temporary)
    pieces += pipe_run("FoundryFeed", origin + tangent * 0.72,
                       (HERO + tangent * 0.28).normalized(), 0.48, temporary)
    return pieces


def landmark_06(center, temporary):
    """Gear train: a diagonal chain of meshing wheels."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for i, (u, v, radius, color) in enumerate((
            (-0.62, -0.30, 0.34, 6), (-0.06, -0.02, 0.42, 7),
            (0.56, 0.28, 0.32, 4), (0.14, 0.62, 0.24, 8))):
        offset = tangent * u + bitangent * v
        pieces += gear(f"TrainGear{i}", origin + offset + HERO * 0.10,
                       (HERO + offset * 0.18).normalized(), radius, temporary,
                       teeth=10 if radius > 0.35 else 8, color=color, hub_color=5)
    pieces.append(box_piece("TrainRail", tuple(origin + HERO * 0.04),
                            (0.86, 0.09, 0.05), 10, temporary, direction=HERO))
    return pieces


def landmark_07(center, temporary):
    """Antenna farm: masts and dishes bristling off a plated deck."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = hull_plate("FarmDeck", origin, HERO, 0.72, temporary, color=2, trim=10,
                        height=0.13, lit=14)
    for i, (u, v, height) in enumerate(((-0.44, 0.16, 0.74), (0.10, -0.24, 0.94),
                                        (0.48, 0.20, 0.62), (-0.14, 0.46, 0.54))):
        offset = tangent * u + bitangent * v
        pieces += antenna(f"FarmMast{i}", origin + offset + HERO * 0.12,
                          (HERO + offset * 0.16).normalized(), height, temporary,
                          light=(11, 14, 13, 11)[i])
    pieces.append(cone_piece("FarmDish", origin + tangent * 0.30 + HERO * 0.52,
                             (HERO - tangent * 0.42).normalized(), 0.34, 0.09, 0.16, 5,
                             temporary, sides=10))
    return pieces


def landmark_08(center, temporary):
    """Coolant tanks: cyan cylinders strapped to a manifold."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [box_piece("TankBed", tuple(origin + HERO * 0.08),
                        (0.68, 0.34, 0.09), 1, temporary, direction=HERO)]
    for i, u in enumerate((-0.36, 0.0, 0.36)):
        base = origin + tangent * u + HERO * 0.14
        pieces.append(cylinder_piece(f"Tank{i}", base, HERO, 0.165, 0.56, 4, temporary,
                                     sides=10, offset=0.28))
        pieces.append(cylinder_piece(f"TankBand{i}", base + HERO * 0.34, HERO, 0.185,
                                     0.055, 13, temporary, sides=10))
        pieces.append(cylinder_piece(f"TankCap{i}", base + HERO * 0.58, HERO, 0.115,
                                     0.075, 8, temporary, sides=8))
    pieces += pipe_run("TankManifold", origin - bitangent * 0.52,
                       (HERO - bitangent * 0.34).normalized(), 0.46, temporary,
                       joint_color=13)
    return pieces


def landmark_09(center, temporary):
    """Wrecked hull: torn plating, exposed rust and one dead red beacon."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [box_piece("WreckHull", tuple(origin + HERO * 0.18),
                        (0.66, 0.36, 0.20), 9, temporary, direction=HERO)]
    for i, (u, v, tilt) in enumerate(((-0.40, 0.10, 0.55), (0.36, -0.14, -0.62),
                                      (0.06, 0.34, 0.30))):
        offset = tangent * u + bitangent * v
        pieces.append(box_piece(f"WreckShard{i}", tuple(origin + offset + HERO * 0.34),
                                (0.26, 0.20, 0.045), 2, temporary,
                                direction=(HERO + tangent * tilt).normalized()))
    pieces.append(box_piece("WreckGap", tuple(origin + HERO * 0.30 - tangent * 0.06),
                            (0.28, 0.16, 0.10), 15, temporary, direction=HERO))
    pieces += antenna("WreckBeacon", origin + tangent * 0.56 + HERO * 0.16,
                      (HERO + tangent * 0.30).normalized(), 0.46, temporary, light=11)
    return pieces


def landmark_10(center, temporary):
    """Engine core: the finale — a driven gear stack over a hot reactor throat."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [cylinder_piece("CoreThroat", origin, HERO, 0.44, 0.30, 1, temporary,
                             sides=10, offset=0.15)]
    pieces.append(cylinder_piece("CoreGlow", origin + HERO * 0.30, HERO, 0.34, 0.09, 12,
                                 temporary, sides=10))
    pieces += gear("CoreGear", origin + HERO * 0.48, HERO, 0.62, temporary, teeth=12,
                   color=7, hub_color=8, thickness=0.085)
    pieces.append(torus_piece("CoreRing", tuple(origin + HERO * 0.62), 0.86, 0.070, 5,
                              temporary, rotation=(math.radians(62), 0.0, math.radians(16)),
                              major_segments=18, minor_segments=4))
    for i in range(3):
        angle = math.tau * i / 3 + 0.5
        offset = tangent * math.cos(angle) * 0.92 + bitangent * math.sin(angle) * 0.92
        pieces += vent_stack(f"CoreVent{i}", origin + offset,
                             (HERO + offset.normalized() * 0.24).normalized(), 0.40,
                             temporary)
    return pieces


LANDMARKS = [
    landmark_01, landmark_02, landmark_03, landmark_04, landmark_05,
    landmark_06, landmark_07, landmark_08, landmark_09, landmark_10,
]


def emphasize_landmark(pieces, center, index):
    planar_factors = (1.72, 1.80, 1.86, 1.78, 1.80, 1.70, 1.84, 1.86, 1.84, 1.68)
    planar = planar_factors[index]
    radial = 1.58
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
    uv = planet.data.uv_layers.new(name="MechanicalPaletteUV")
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
    planet["hero_landmark"] = name.replace("Mechanical_", "")
    planet["mobile_optimized"] = True
    planet["single_material"] = True
    return planet


def add_camera_and_lights():
    bpy.ops.object.camera_add(location=(8.2, -11.4, 7.8))
    camera = bpy.context.object
    camera.name = "Mechanical_RenderCamera"
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

    # Hard workshop light: a tight bright key for metal highlights, a cold fill
    # and a hot orange bounce standing in for the furnaces.
    area("Mechanical_KeyLight", (-4.4, -6.2, 10.6), 1650, (1.0, 0.97, 0.92), 4.5)
    area("Mechanical_ColdFill", (7.0, -2.4, 1.4), 1250, (0.58, 0.72, 1.0), 6.5)
    area("Mechanical_RimLight", (0.0, 5.6, 7.2), 1000, (0.80, 0.88, 1.0), 4.0)
    area("Mechanical_FurnaceBounce", (2.0, -6.0, -6.4), 780, (1.0, 0.56, 0.24), 7.0)


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
    background.inputs["Color"].default_value = (0.085, 0.095, 0.115, 1.0)
    background.inputs["Strength"].default_value = 0.95


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
        name = f"Mechanical_{index + 1:02d}"
        center = Vector(center_tuple)
        pieces = [base_planet(name, center, temporary, 10100 + index)]
        pieces += surface_details(center, temporary, 11100 + index, count=12)
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
    print("MECHANICAL_COLLECTION_COMPLETE", report)


if __name__ == "__main__":
    main()
