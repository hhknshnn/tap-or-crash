import bpy
import math
import os
import random
from mathutils import Vector


ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
MODEL_DIR = os.path.join(ROOT, "Assets", "Models", "CrystalPlanets")
SPRITE_DIR = os.path.join(ROOT, "Assets", "Sprites", "CrystalPlanets")
BLEND_PATH = os.path.join(MODEL_DIR, "Crystal_Planet_Collection.blend")
PALETTE_PATH = os.path.join(MODEL_DIR, "Crystal_Palette.png")

PALETTE = [
    (0.120, 0.080, 0.280, 1.0),
    (0.220, 0.160, 0.450, 1.0),
    (0.400, 0.280, 0.720, 1.0),
    (0.580, 0.380, 0.920, 1.0),
    (0.750, 0.500, 1.000, 1.0),
    (0.870, 0.750, 1.000, 1.0),
    (0.260, 0.750, 1.000, 1.0),
    (0.300, 0.940, 1.000, 1.0),
    (0.760, 1.000, 1.000, 1.0),
    (0.930, 1.000, 1.000, 1.0),
    (0.900, 0.390, 0.780, 1.0),
    (1.000, 0.580, 0.790, 1.0),
    (0.320, 0.840, 0.700, 1.0),
    (0.980, 0.820, 0.360, 1.0),
    (0.270, 0.480, 0.820, 1.0),
    (0.580, 0.800, 1.000, 1.0),
]

GRID = [
    (-14.4, -3.6, 0.0), (-7.2, -3.6, 0.0), (0.0, -3.6, 0.0),
    (7.2, -3.6, 0.0), (14.4, -3.6, 0.0),
    (-14.4, 3.6, 0.0), (-7.2, 3.6, 0.0), (0.0, 3.6, 0.0),
    (7.2, 3.6, 0.0), (14.4, 3.6, 0.0),
]

HERO = Vector((0.0, -0.45, 0.89)).normalized()


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
    image = bpy.data.images.new("Crystal_Palette", width=256, height=16, alpha=True)
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
        material = bpy.data.materials.new(f"Crystal_Color_{index:02d}")
        material.diffuse_color = color
        material["palette_index"] = index
        temporary.append(material)

    material = bpy.data.materials.new("Crystal_Palette_URP")
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
    principled.inputs["Roughness"].default_value = 0.22
    principled.inputs["Metallic"].default_value = 0.08
    if "Coat Weight" in principled.inputs:
        principled.inputs["Coat Weight"].default_value = 0.28
        principled.inputs["Coat Roughness"].default_value = 0.12
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


def crystal_piece(name, center, direction, radius, length, color, temporary, sides=5):
    direction = Vector(direction).normalized()
    location = Vector(center) + direction * (length * 0.48)
    bpy.ops.mesh.primitive_cone_add(
        vertices=sides,
        radius1=radius,
        radius2=max(radius * 0.16, 0.006),
        depth=length * 0.72,
        end_fill_type="NGON",
        location=location,
    )
    body = bpy.context.object
    body.name = name + "_Body"
    point_z(body, direction)
    paint(body, color, temporary)

    bpy.ops.mesh.primitive_cone_add(
        vertices=sides,
        radius1=max(radius * 0.16, 0.006),
        radius2=0.0,
        depth=length * 0.28,
        end_fill_type="NGON",
        location=Vector(center) + direction * (length * 0.86),
    )
    tip = bpy.context.object
    tip.name = name + "_Tip"
    point_z(tip, direction)
    paint(tip, min(15, color + 2), temporary)
    return [body, tip]


def box_piece(name, location, scale, color, temporary, rotation=(0.0, 0.0, 0.0)):
    bpy.ops.mesh.primitive_cube_add(location=location, rotation=rotation)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return paint(obj, color, temporary)


def ico_piece(name, location, radius, color, temporary, subdivisions=1, scale=None):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=radius, location=location)
    obj = bpy.context.object
    obj.name = name
    if scale is not None:
        obj.scale = scale
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return paint(obj, color, temporary)


def torus_piece(name, location, major_radius, minor_radius, color, temporary,
                rotation=(0.0, 0.0, 0.0), major_segments=16, minor_segments=4):
    bpy.ops.mesh.primitive_torus_add(
        major_radius=major_radius,
        minor_radius=minor_radius,
        major_segments=major_segments,
        minor_segments=minor_segments,
        location=location,
        rotation=rotation,
    )
    obj = bpy.context.object
    obj.name = name
    return paint(obj, color, temporary)


def arc_band_piece(name, center, normal, outer_radius, inner_radius, half_angle,
                   thickness, color, temporary, segments=16):
    normal = Vector(normal).normalized()
    tangent = Vector((1.0, 0.0, 0.0))
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


def base_planet(name, center, temporary, seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=3.0, location=center)
    sphere = bpy.context.object
    sphere.name = name + "_Sphere"
    add_slots(sphere, temporary)
    rng = random.Random(seed)
    for polygon in sphere.data.polygons:
        normal = polygon.center.normalized()
        light = max(0.0, normal.z * 0.55 - normal.y * 0.30 + normal.x * 0.12)
        index = 1 + int(light * 4.2)
        if rng.random() < 0.12:
            index += rng.choice((-1, 1))
        # Terrain remains a clean violet family so every hero crystal reads
        # immediately in cyan, ice, pink or gold at gameplay distance.
        polygon.material_index = max(1, min(5, index))
    return sphere


def surface_details(center, temporary, seed, count=30):
    pieces = []
    golden = math.pi * (3.0 - math.sqrt(5.0))
    rng = random.Random(seed)
    for i in range(count):
        y = 1.0 - (i + 0.5) * 2.0 / count
        radial = math.sqrt(max(0.0, 1.0 - y * y))
        angle = i * golden + seed * 0.37
        direction = Vector((math.cos(angle) * radial, math.sin(angle) * radial, y))
        # Keep the front hero zone clean so the landmark reads immediately.
        if direction.dot(HERO) > 0.76:
            direction = Vector((-direction.x, direction.y, direction.z)).normalized()
        length = rng.uniform(0.13, 0.27)
        radius = rng.uniform(0.035, 0.065)
        color = rng.choice((4, 5, 6, 7, 10, 14, 15))
        pieces += crystal_piece(
            f"SurfaceShard_{i:02d}", Vector(center) + direction * 2.96,
            direction, radius, length, color, temporary, sides=4,
        )
    return pieces


def radial_landmark(center, temporary, count, radius, length, colors, phase=0.0,
                    tilt=0.0, origin=None):
    pieces = []
    origin = Vector(origin) if origin is not None else Vector(center) + HERO * 2.88
    tangent = Vector((1.0, 0.0, 0.0))
    bitangent = HERO.cross(tangent).normalized()
    for i in range(count):
        angle = phase + math.tau * i / count
        offset = tangent * math.cos(angle) * radius + bitangent * math.sin(angle) * radius
        direction = (HERO + offset.normalized() * tilt).normalized()
        pieces += crystal_piece(
            f"HeroRay_{i:02d}", origin + offset, direction,
            0.09 + 0.018 * (i % 3), length * (0.82 + 0.11 * (i % 4)),
            colors[i % len(colors)], temporary, sides=5,
        )
    return pieces


def landmark_01(center, temporary):
    pieces = radial_landmark(center, temporary, 10, 0.58, 1.15, (4, 6, 7, 10), tilt=0.22)
    pieces += crystal_piece("CrownPrime", Vector(center) + HERO * 2.84, HERO,
                            0.34, 1.82, 4, temporary, sides=6)
    return pieces


def landmark_02(center, temporary):
    pieces = []
    origin = Vector(center) + HERO * 2.72
    tangent = Vector((1.0, 0.0, 0.0))
    bitangent = HERO.cross(tangent).normalized()
    for i in range(14):
        angle = math.tau * i / 14
        offset = tangent * math.cos(angle) * 0.68 + bitangent * math.sin(angle) * 0.54
        direction = (HERO + offset.normalized() * 0.34).normalized()
        pieces += crystal_piece("GeodeRim", origin + offset, direction, 0.11, 0.58,
                                (6, 7, 10, 11)[i % 4], temporary, sides=5)
    pieces.append(ico_piece("GeodeCore", origin + HERO * 0.12, 0.43, 0, temporary,
                            subdivisions=2, scale=(1.0, 0.42, 0.82)))
    return pieces


def landmark_03(center, temporary):
    pieces = []
    origin = Vector(center) + HERO * 2.75
    tangent = Vector((1.0, 0.0, 0.0))
    for side in (-1, 1):
        base = origin + tangent * side * 0.48
        pieces += crystal_piece("PrismTower", base, HERO, 0.24, 1.35,
                                4 if side < 0 else 6, temporary, sides=6)
    lintel = box_piece("PrismLintel", origin + HERO * 0.82, (0.72, 0.12, 0.13),
                       7, temporary, rotation=(0.0, 0.0, 0.0))
    lintel.rotation_mode = "QUATERNION"
    lintel.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(HERO)
    pieces.append(lintel)
    pieces += radial_landmark(center, temporary, 6, 0.90, 0.48, (10, 7, 5),
                              phase=math.pi / 6, tilt=0.45, origin=origin + HERO * 0.42)
    return pieces


def landmark_04(center, temporary):
    pieces = []
    origin = Vector(center) + HERO * 3.20
    tangent = Vector((1.0, 0.0, 0.0))
    bitangent = HERO.cross(tangent).normalized()
    core = ico_piece("LevitatingPrismCore", origin, 0.62, 7, temporary,
                     subdivisions=2, scale=(1.28, 0.72, 0.82))
    core.rotation_euler = (0.18, -0.22, 0.30)
    pieces.append(core)
    for i in range(8):
        angle = math.tau * i / 8 + math.pi / 8
        offset = tangent * math.cos(angle) * 0.94 + bitangent * math.sin(angle) * 0.72
        direction = (HERO + offset.normalized() * 0.32).normalized()
        pieces += crystal_piece("PrismSatellite", origin + offset, direction, 0.105,
                                0.62 + 0.12 * (i % 3),
                                (4, 6, 10, 15)[i % 4], temporary, sides=5)
    for i in range(3):
        pieces += crystal_piece("PrismKeel", origin - bitangent * (0.30 + i * 0.22),
                                -bitangent + HERO * 0.12, 0.13, 0.72 - i * 0.10,
                                (3, 6, 10)[i], temporary, sides=5)
    return pieces


def landmark_05(center, temporary):
    pieces = []
    origin = Vector(center) + HERO * 3.08
    tangent = Vector((1.0, 0.0, 0.0))
    bitangent = HERO.cross(tangent).normalized()
    pieces.append(arc_band_piece(
        "CrystalCrescent", origin, HERO, 1.00, 0.70, math.radians(132),
        0.075, 7, temporary, segments=18))
    for i in range(5):
        angle = math.radians(-88 + i * 44)
        offset = tangent * math.cos(angle) * 0.84 + bitangent * math.sin(angle) * 0.84
        direction = (HERO + offset.normalized() * 0.22).normalized()
        pieces += crystal_piece("CrescentJewel", origin + offset, direction, 0.10,
                                0.46 + 0.07 * (i % 2), (5, 10, 4)[i % 3],
                                temporary, sides=5)
    pieces += crystal_piece("CrescentStar", origin + tangent * 0.16 - bitangent * 0.04,
                            HERO, 0.27, 1.12, 13, temporary, sides=6)
    return pieces


def landmark_06(center, temporary):
    pieces = []
    origin = Vector(center) + HERO * 2.82
    tangent = Vector((1.0, 0.0, 0.0))
    bitangent = HERO.cross(tangent).normalized()
    for ring, count in ((0.42, 8), (0.72, 12)):
        for i in range(count):
            angle = math.tau * i / count + ring
            offset = tangent * math.cos(angle) * ring + bitangent * math.sin(angle) * ring
            direction = (HERO + offset.normalized() * 0.42).normalized()
            pieces += crystal_piece("CrystalBloom", origin + offset, direction, 0.085,
                                    0.62 if count == 8 else 0.46,
                                    (10, 11, 4, 7)[i % 4], temporary, sides=4)
    pieces.append(ico_piece("BloomCore", origin + HERO * 0.28, 0.28, 13, temporary, subdivisions=2))
    return pieces


def landmark_07(center, temporary):
    pieces = []
    origin = Vector(center) + HERO * 2.74
    tangent = Vector((1.0, 0.0, 0.0))
    for side in (-1, 1):
        pieces += crystal_piece("TwinObelisk", origin + tangent * side * 0.55, HERO,
                                0.27, 1.55 if side < 0 else 1.28,
                                3 if side < 0 else 6, temporary, sides=6)
    bridge = box_piece("LightBridge", origin + HERO * 0.72, (0.62, 0.07, 0.08),
                       8, temporary)
    bridge.rotation_mode = "QUATERNION"
    bridge.rotation_quaternion = Vector((0.0, 0.0, 1.0)).rotation_difference(HERO)
    pieces.append(bridge)
    pieces += radial_landmark(center, temporary, 6, 0.82, 0.44, (5, 7, 15),
                              phase=0.2, tilt=0.30, origin=origin + HERO * 0.22)
    return pieces


def landmark_08(center, temporary):
    pieces = []
    origin = Vector(center) + HERO * 3.04
    tangent = Vector((1.0, 0.0, 0.0))
    bitangent = HERO.cross(tangent).normalized()
    pieces.append(ico_piece("HeartLeft", origin - tangent * 0.25 + bitangent * 0.17,
                            0.48, 10, temporary, subdivisions=2, scale=(1.0, 0.72, 0.88)))
    pieces.append(ico_piece("HeartRight", origin + tangent * 0.25 + bitangent * 0.17,
                            0.48, 11, temporary, subdivisions=2, scale=(1.0, 0.72, 0.88)))
    pieces += crystal_piece("HeartPoint", origin - bitangent * 0.12, -bitangent + HERO * 0.20,
                            0.46, 0.96, 4, temporary, sides=6)
    pieces += radial_landmark(center, temporary, 7, 0.88, 0.40, (10, 7, 13),
                              phase=math.pi / 2, tilt=0.28, origin=origin)
    return pieces


def landmark_09(center, temporary):
    pieces = []
    origin = Vector(center) + HERO * 3.02
    ring = torus_piece("PrismObservatory", origin, 0.73, 0.105, 7, temporary,
                       rotation=(math.radians(62), 0.0, math.radians(12)),
                       major_segments=18, minor_segments=4)
    pieces.append(ring)
    pieces.append(ico_piece("ObservatoryCore", origin, 0.38, 8, temporary, subdivisions=2))
    pieces += radial_landmark(center, temporary, 8, 0.92, 0.52, (4, 6, 10, 13),
                              phase=math.pi / 8, tilt=0.36, origin=origin)
    return pieces


def landmark_10(center, temporary):
    pieces = radial_landmark(center, temporary, 14, 0.72, 1.05,
                             (3, 4, 6, 7, 10, 13), phase=math.pi / 14, tilt=0.27)
    origin = Vector(center) + HERO * 3.12
    pieces += crystal_piece("CathedralPrime", Vector(center) + HERO * 2.76, HERO,
                            0.40, 2.05, 4, temporary, sides=6)
    pieces.append(torus_piece("CathedralHalo", origin, 0.88, 0.075, 13, temporary,
                              rotation=(math.radians(64), 0.0, 0.0),
                              major_segments=20, minor_segments=4))
    return pieces


LANDMARKS = [
    landmark_01, landmark_02, landmark_03, landmark_04, landmark_05,
    landmark_06, landmark_07, landmark_08, landmark_09, landmark_10,
]


def emphasize_landmark(pieces, center, index):
    # Enlarge only the existing hero formation. The surface detail count,
    # triangle count and sphere remain untouched.
    planar_factors = (1.48, 1.58, 1.58, 1.50, 1.52, 1.50, 1.58, 1.48, 1.52, 1.44)
    planar = planar_factors[index]
    radial = 0.78
    anchor = Vector(center) + HERO * 2.86
    tangent = Vector((1.0, 0.0, 0.0))
    bitangent = HERO.cross(tangent).normalized()
    brighten = {3: 4, 4: 5, 5: 8, 6: 7, 7: 8, 10: 11, 14: 6, 15: 8}

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
        for polygon in obj.data.polygons:
            polygon.material_index = brighten.get(
                polygon.material_index, polygon.material_index)


def join_planet(name, center, pieces, final_material, temporary):
    bpy.ops.object.select_all(action="DESELECT")
    for piece in pieces:
        piece.select_set(True)
    bpy.context.view_layer.objects.active = pieces[0]
    bpy.ops.object.join()
    planet = bpy.context.object
    planet.name = name

    # Convert each source color slot to one texel in the shared palette.
    while planet.data.uv_layers:
        planet.data.uv_layers.remove(planet.data.uv_layers[0])
    uv = planet.data.uv_layers.new(name="CrystalPaletteUV")
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

    # Pivot is the mathematical center of the sphere, not the landmark centroid.
    bpy.context.scene.cursor.location = center
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR", center="MEDIAN")
    planet["hero_landmark"] = name.replace("Crystal_", "")
    planet["mobile_optimized"] = True
    planet["single_material"] = True
    return planet


def add_camera_and_lights():
    bpy.ops.object.camera_add(location=(8.2, -11.4, 7.8))
    camera = bpy.context.object
    camera.name = "Crystal_RenderCamera"
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 6.9
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

    area("Crystal_KeyLight", (-4.8, -6.2, 10.8), 1480, (0.70, 0.94, 1.0), 6.0)
    area("Crystal_MagentaFill", (6.0, 0.8, 4.2), 900, (1.0, 0.42, 0.82), 5.0)
    area("Crystal_RimLight", (0.0, 5.5, 7.0), 1050, (0.52, 0.72, 1.0), 4.0)


def configure_render():
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1024
    scene.render.resolution_y = 1024
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.film_transparent = True
    scene.render.resolution_percentage = 100
    scene.render.image_settings.color_depth = "8"
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.view_transform = "AgX"
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.035, 0.028, 0.10, 1.0)
    background.inputs["Strength"].default_value = 0.48


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
        uv_layers = len(planet.data.uv_layers)
        materials = len(planet.data.materials)
        if not 1800 <= triangles <= 2500:
            raise RuntimeError(f"{planet.name}: {triangles} triangles outside 1800-2500")
        if uv_layers != 1 or materials != 1:
            raise RuntimeError(f"{planet.name}: expected one UV layer and one material")
        report.append((planet.name, triangles, len(planet.data.vertices)))
    return report


def main():
    ensure_dirs()
    clear_scene()
    palette_image = make_palette()
    temporary, final_material = make_materials(palette_image)
    planets = []

    for index, center_tuple in enumerate(GRID):
        name = f"Crystal_{index + 1:02d}"
        center = Vector(center_tuple)
        pieces = [base_planet(name, center, temporary, 2100 + index)]
        pieces += surface_details(center, temporary, 3100 + index, count=34)
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
    print("CRYSTAL_COLLECTION_COMPLETE", report)


if __name__ == "__main__":
    main()
