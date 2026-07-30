import bpy
import math
import os
import random
from mathutils import Vector, noise


ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
MODEL_DIR = os.path.join(ROOT, "Assets", "Models", "SakuraPlanets")
SPRITE_DIR = os.path.join(ROOT, "Assets", "Sprites", "SakuraPlanets")
BLEND_PATH = os.path.join(MODEL_DIR, "Sakura_Planet_Collection.blend")
PALETTE_PATH = os.path.join(MODEL_DIR, "Sakura_Palette.png")

# Only build this many planets (1 while art-directing, 10 for the full pack).
PLANET_COUNT = 10

PALETTE = [
    (0.230, 0.120, 0.150, 1.0),   # 0  trunk plum
    # Deep plum ground: Natural owns green and Desert owns sand, and a mid-pink
    # ground gave the pink canopies nothing to sit against.
    # Values are kept well above the purple space backdrop the level renders on;
    # a darker ground made the whole planet sink into the background at sprite size.
    (0.290, 0.170, 0.235, 1.0),   # 1  plum shadow
    (0.430, 0.255, 0.340, 1.0),   # 2  plum earth
    (0.590, 0.355, 0.450, 1.0),   # 3  lit plum
    (0.940, 0.730, 0.800, 1.0),   # 4  petal drift
    (0.700, 0.490, 0.340, 1.0),   # 5  garden path
    (0.880, 0.180, 0.440, 1.0),   # 6  blossom magenta
    (1.000, 0.360, 0.620, 1.0),   # 7  blossom pink
    (1.000, 0.580, 0.760, 1.0),   # 8  blossom light
    (1.000, 0.840, 0.900, 1.0),   # 9  blossom near-white
    (0.860, 0.155, 0.130, 1.0),   # 10 torii vermilion
    (0.980, 0.330, 0.190, 1.0),   # 11 vermilion highlight
    (1.000, 0.810, 0.360, 1.0),   # 12 lantern gold
    (0.960, 0.930, 0.880, 1.0),   # 13 cream stone
    (0.270, 0.760, 0.720, 1.0),   # 14 jade water
    (0.330, 0.300, 0.360, 1.0),   # 15 slate
]

GRID = [
    (-14.4, -3.6, 0.0), (-7.2, -3.6, 0.0), (0.0, -3.6, 0.0),
    (7.2, -3.6, 0.0), (14.4, -3.6, 0.0),
    (-14.4, 3.6, 0.0), (-7.2, 3.6, 0.0), (0.0, 3.6, 0.0),
    (7.2, 3.6, 0.0), (14.4, 3.6, 0.0),
]

# ~38 degrees off the camera axis. Shallower angles look down the barrel of every
# vertical (torii posts, pagoda tiers, trunks) and flatten them into blobs; the
# render frame is widened to 7.6 so this tilt still keeps the landmark inside it.
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
    image = bpy.data.images.new("Sakura_Palette", width=256, height=16, alpha=True)
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
        material = bpy.data.materials.new(f"Sakura_Color_{index:02d}")
        material.diffuse_color = color
        material["palette_index"] = index
        temporary.append(material)

    material = bpy.data.materials.new("Sakura_Palette_URP")
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
    # Soft, matte petals: no coat, no metal — the opposite of the crystal pack.
    principled.inputs["Roughness"].default_value = 0.62
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


def box_piece(name, location, scale, color, temporary, direction=None, spin=0.0):
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if direction is not None:
        point_z(obj, direction)
        if spin:
            obj.rotation_mode = "QUATERNION"
            obj.rotation_quaternion = obj.rotation_quaternion @ Vector(
                (0.0, 0.0, 1.0)).rotation_difference(Vector((0.0, 0.0, 1.0)))
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


# ── Signature silhouette: layered blossom canopies ─────────────────────────────
def canopy(name, apex, direction, radius, temporary, puffs=4, subdivisions=1,
           colors=(6, 7, 8, 9), spread=0.72):
    """A ring of small puffs around a lower core — a bushy crown rather than a
    handful of oversized polygon blobs, which is what reads as blossom."""
    direction = Vector(direction).normalized()
    tangent, bitangent = basis(direction)
    pieces = [ico_piece(
        f"{name}_Core", Vector(apex), radius * 0.66, colors[0], temporary,
        subdivisions=subdivisions, scale=(1.20, 1.20, 0.72), direction=direction)]
    ring = max(1, puffs - 1)
    for i in range(ring):
        angle = math.tau * i / ring + 0.35
        offset = (tangent * math.cos(angle) + bitangent * math.sin(angle)) \
            * radius * spread
        lift = radius * (0.20 + 0.16 * math.sin(angle * 2.0))
        pieces.append(ico_piece(
            f"{name}_Puff{i}", Vector(apex) + offset + direction * lift,
            radius * (0.46 + 0.07 * (i % 3)), colors[1 + i % (len(colors) - 1)],
            temporary, subdivisions=subdivisions, scale=(1.10, 1.10, 0.76),
            direction=direction))
    return pieces


def sakura_tree(name, base, direction, trunk_length, canopy_radius, temporary,
                puffs=4, subdivisions=1, colors=(6, 7, 8, 9), trunk_color=0):
    direction = Vector(direction).normalized()
    pieces = [cone_piece(f"{name}_Trunk", base, direction, canopy_radius * 0.20,
                         canopy_radius * 0.11, trunk_length, trunk_color, temporary,
                         sides=5)]
    pieces += canopy(name, Vector(base) + direction * (trunk_length * 0.96),
                     direction, canopy_radius, temporary, puffs=puffs,
                     subdivisions=subdivisions, colors=colors)
    return pieces


def stone_lantern(name, base, direction, height, temporary, scale=1.0):
    direction = Vector(direction).normalized()
    pieces = [
        cone_piece(f"{name}_Post", base, direction, 0.105 * scale, 0.080 * scale,
                   height * 0.62, 15, temporary, sides=6),
        box_piece(f"{name}_Light", tuple(Vector(base) + direction * height * 0.78),
                  (0.135 * scale, 0.135 * scale, 0.125 * scale), 12, temporary,
                  direction=direction),
        cone_piece(f"{name}_Roof", Vector(base) + direction * height * 0.90, direction,
                   0.195 * scale, 0.0, height * 0.30, 13, temporary, sides=6),
    ]
    return pieces


def torii_gate(name, origin, direction, width, height, temporary):
    direction = Vector(direction).normalized()
    tangent, _ = basis(direction)
    pieces = []
    for side in (-1, 1):
        pieces.append(cone_piece(
            f"{name}_Pillar{side}", Vector(origin) + tangent * side * width,
            direction, 0.105, 0.085, height, 10, temporary, sides=6))
    top = Vector(origin) + direction * height
    pieces.append(box_piece(f"{name}_Beam", tuple(top + direction * 0.10),
                            (width * 1.32, 0.075, 0.070), 10, temporary,
                            direction=direction))
    pieces.append(box_piece(f"{name}_Lintel", tuple(top - direction * 0.16),
                            (width * 1.06, 0.060, 0.055), 11, temporary,
                            direction=direction))
    return pieces


def pond(name, center, direction, radius, temporary, color=14):
    return [cylinder_piece(f"{name}_Water", center, direction, radius, 0.06, color,
                           temporary, sides=12, offset=0.02)]


# ── Terrain ───────────────────────────────────────────────────────────────────
def base_planet(name, center, temporary, seed):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=RADIUS, location=center)
    sphere = bpy.context.object
    sphere.name = name + "_Sphere"
    add_slots(sphere, temporary)

    # Soft rolling hills: the sakura pack reads calm, so the displacement stays
    # gentle and the drama comes from the canopy silhouettes instead.
    offset = Vector((seed * 0.31, seed * 0.17, seed * 0.53))
    for vertex in sphere.data.vertices:
        direction = vertex.co.normalized()
        hill = noise.noise(direction * 2.1 + offset)
        vertex.co = direction * (RADIUS + hill * 0.13)

    # Painted, not modelled: coherent noise keeps the petal carpet and moss as
    # readable meadows instead of per-face speckle.
    carpet_offset = Vector((seed * 0.71, seed * 0.29, seed * 0.11))
    moss_offset = Vector((seed * 0.13, seed * 0.83, seed * 0.47))
    for polygon in sphere.data.polygons:
        normal = polygon.center.normalized()
        light = max(0.0, normal.z * 0.55 - normal.y * 0.30 + normal.x * 0.12)
        index = max(1, min(3, 1 + int(light * 3.2)))
        if noise.noise(normal * 2.6 + carpet_offset) > 0.42:
            index = 4
        elif noise.noise(normal * 3.4 + moss_offset) > 0.24:
            index = 5
        polygon.material_index = index
    return sphere


def surface_details(center, temporary, seed, count=10):
    """The orchard. Blossom crowns scattered over the whole globe carry the
    theme — a single giant tree either hides its trunk or leaves the frame."""
    pieces = []
    golden = math.pi * (3.0 - math.sqrt(5.0))
    rng = random.Random(seed)
    tangent, bitangent = basis(CAMERA_DIR)
    cap = math.cos(math.radians(76.0))
    for i in range(count):
        # Even area coverage of the camera-facing cap only: the sprite never
        # shows the far side, so every tree is spent where it is visible.
        u = (i + 0.55) / count
        polar = math.acos(1.0 - u * (1.0 - cap))
        angle = i * golden + seed * 0.37
        direction = (CAMERA_DIR * math.cos(polar)
                     + (tangent * math.cos(angle) + bitangent * math.sin(angle))
                     * math.sin(polar)).normalized()
        # Only the landmark's own footprint is kept clear.
        if direction.dot(HERO) > 0.84:
            direction = (direction - HERO * 0.55).normalized()
        base = Vector(center) + direction * (RADIUS - 0.05)
        pieces += sakura_tree(
            f"Grove_{i:02d}", base, direction,
            rng.uniform(0.40, 0.56), rng.uniform(0.30, 0.40), temporary,
            puffs=3, subdivisions=1,
            colors=rng.choice(((6, 7, 8), (7, 8, 9), (8, 9, 7), (6, 8, 9))))
    return pieces


# ── Hero landmarks ────────────────────────────────────────────────────────────
def hero_origin(center, lift=0.0):
    return Vector(center) + HERO * (RADIUS - 0.06 + lift)


def landmark_01(center, temporary):
    """Great Sakura: one enormous crown that owns the whole silhouette."""
    origin = hero_origin(center)
    pieces = sakura_tree("GreatSakura", origin, HERO, 0.62, 0.58, temporary,
                         puffs=8, subdivisions=1, colors=(6, 7, 8, 9))
    tangent, bitangent = basis(HERO)
    for i in range(3):
        angle = math.tau * i / 3 + 0.4
        offset = tangent * math.cos(angle) * 0.86 + bitangent * math.sin(angle) * 0.86
        pieces += sakura_tree(f"GreatSapling_{i}", origin + offset,
                              (HERO + offset.normalized() * 0.34).normalized(),
                              0.40, 0.38, temporary, puffs=3, subdivisions=1,
                              colors=(7, 8, 9))
    return pieces


def landmark_02(center, temporary):
    """Torii gate framed by two blossom trees."""
    origin = hero_origin(center)
    tangent, _ = basis(HERO)
    pieces = torii_gate("Torii", origin, HERO, 0.42, 0.86, temporary)
    for side in (-1, 1):
        pieces += sakura_tree(f"GateTree{side}", origin + tangent * side * 0.86,
                              (HERO + tangent * side * 0.22).normalized(),
                              0.44, 0.48, temporary, puffs=4, subdivisions=1,
                              colors=(6, 7, 8, 9))
    pieces += stone_lantern("GateLantern", origin + tangent * 0.58, HERO, 0.34, temporary)
    return pieces


def landmark_03(center, temporary):
    """Three-tier pagoda in cream and vermilion."""
    origin = hero_origin(center)
    tangent, _ = basis(HERO)
    pieces = []
    heights = (0.0, 0.34, 0.62)
    widths = (0.34, 0.26, 0.19)
    for i, (lift, width) in enumerate(zip(heights, widths)):
        pieces.append(box_piece(f"PagodaTier{i}", tuple(origin + HERO * (lift + 0.11)),
                                (width, width, 0.115), 13, temporary, direction=HERO))
        pieces.append(cone_piece(f"PagodaRoof{i}", origin + HERO * (lift + 0.22), HERO,
                                 width * 1.62, width * 0.18, 0.16, 10, temporary, sides=6))
    pieces.append(cone_piece("PagodaSpire", origin + HERO * 0.82, HERO, 0.05, 0.0,
                             0.34, 12, temporary, sides=5))
    pieces += sakura_tree("PagodaTree", origin + tangent * 0.78,
                          (HERO + tangent * 0.24).normalized(), 0.42, 0.46, temporary,
                          puffs=4, subdivisions=1, colors=(6, 7, 8, 9))
    return pieces


def landmark_04(center, temporary):
    """Moon bridge arching over a jade pond."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = pond("MoonPond", origin - bitangent * 0.10, HERO, 0.62, temporary)
    pieces.append(arc_band_piece("MoonBridge", origin + HERO * 0.04, bitangent,
                                 0.56, 0.44, math.radians(72), 0.085, 10, temporary,
                                 segments=10, tangent=tangent))
    pieces += sakura_tree("BridgeTree", origin + tangent * 0.74 + bitangent * 0.18,
                          (HERO + tangent * 0.26).normalized(), 0.46, 0.52, temporary,
                          puffs=4, subdivisions=1, colors=(6, 7, 8, 9))
    pieces += stone_lantern("BridgeLantern", origin - tangent * 0.66, HERO, 0.38, temporary)
    return pieces


def landmark_05(center, temporary):
    """Lantern path: a warm processional line of stone lanterns."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    for i in range(5):
        step = (i - 2) * 0.34
        base = origin + tangent * step + bitangent * (0.10 - abs(step) * 0.18)
        pieces += stone_lantern(f"PathLantern{i}", base,
                                (HERO + tangent * step * 0.30).normalized(),
                                0.50 - abs(step) * 0.10, temporary,
                                scale=1.1 - abs(step) * 0.22)
    pieces += sakura_tree("PathTree", origin - bitangent * 0.52,
                          (HERO - bitangent * 0.30).normalized(), 0.50, 0.56, temporary,
                          puffs=5, subdivisions=1, colors=(6, 7, 8, 9))
    return pieces


def landmark_06(center, temporary):
    """Blossom grove: five crowns merging into one pink mass."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = []
    layout = ((0.0, 0.0, 0.62, 0.60), (-0.62, 0.12, 0.44, 0.44),
              (0.62, 0.10, 0.46, 0.46), (-0.34, -0.48, 0.34, 0.36),
              (0.38, -0.46, 0.36, 0.38))
    for i, (u, v, trunk, radius) in enumerate(layout):
        offset = tangent * u + bitangent * v
        pieces += sakura_tree(f"GroveTree{i}", origin + offset,
                              (HERO + offset * 0.34).normalized(), trunk, radius,
                              temporary, puffs=3 if i else 4, subdivisions=1,
                              colors=((6, 7, 8, 9) if i % 2 else (7, 8, 9, 8)))
    return pieces


def landmark_07(center, temporary):
    """Weeping sakura: long drooping strands tipped in blossom."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = sakura_tree("WeepingCore", origin, HERO, 0.78, 0.62, temporary,
                         puffs=4, subdivisions=1, colors=(6, 7, 8, 9))
    crown = origin + HERO * 0.82
    for i in range(8):
        angle = math.tau * i / 8
        radial = tangent * math.cos(angle) + bitangent * math.sin(angle)
        start = crown + radial * 0.52
        direction = (radial * 0.42 - HERO).normalized()
        pieces.append(cone_piece(f"WeepStrand{i}", start, direction, 0.035, 0.020,
                                 0.42 + 0.06 * (i % 3), 0, temporary, sides=4))
        pieces.append(ico_piece(f"WeepTip{i}", start + direction * (0.46 + 0.06 * (i % 3)),
                                0.085, (7, 8, 9)[i % 3], temporary, subdivisions=0,
                                scale=(1.2, 1.2, 0.8), direction=HERO))
    return pieces


def landmark_08(center, temporary):
    """Zen garden: raked disc, standing stones and a single tree."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    pieces = [cylinder_piece("ZenGravel", origin, HERO, 0.52, 0.06, 13, temporary,
                             sides=12, offset=0.02)]
    pieces.append(arc_band_piece("ZenRake", origin + HERO * 0.05, HERO, 0.42, 0.36,
                                 math.radians(180), 0.015, 4, temporary, segments=12,
                                 tangent=tangent))
    pieces += stone_lantern("ZenLantern", origin - tangent * 0.44 + bitangent * 0.26,
                            HERO, 0.46, temporary)
    # Two stones, deliberately off-axis: three evenly spaced ones read as a face.
    for i, (u, v, radius) in enumerate(((-0.24, 0.06, 0.14), (0.12, -0.20, 0.095))):
        pieces.append(ico_piece(f"ZenStone{i}", origin + tangent * u + bitangent * v
                                + HERO * radius * 0.5, radius, 15, temporary,
                                subdivisions=1, scale=(1.0, 0.82, 0.92),
                                direction=HERO))
    pieces += sakura_tree("ZenTree", origin + tangent * 0.66 - bitangent * 0.22,
                          (HERO + tangent * 0.26).normalized(), 0.52, 0.52, temporary,
                          puffs=4, subdivisions=1, colors=(6, 7, 8, 9))
    return pieces


def landmark_09(center, temporary):
    """Petal falls: a pink cascade spilling from a ledge into a jade pool."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    ledge = origin + HERO * 0.62 + bitangent * 0.24
    pieces = [box_piece("FallsLedge", tuple(ledge), (0.52, 0.20, 0.09), 15, temporary,
                        direction=HERO)]
    for i in range(3):
        offset = tangent * (i - 1) * 0.24
        pieces.append(box_piece(f"FallsStream{i}", tuple(ledge + offset - HERO * 0.30),
                                (0.085, 0.055, 0.34), (7, 8, 9)[i], temporary,
                                direction=HERO))
    pieces += pond("FallsPool", origin - bitangent * 0.06, HERO, 0.58, temporary)
    pieces += sakura_tree("FallsTree", ledge + tangent * 0.52 + HERO * 0.06,
                          (HERO + tangent * 0.20).normalized(), 0.44, 0.48, temporary,
                          puffs=4, subdivisions=1, colors=(6, 7, 8, 9))
    return pieces


def landmark_10(center, temporary):
    """Sakura shrine: the ceremonial finale — hall, torii, lanterns and a crown."""
    origin = hero_origin(center)
    tangent, bitangent = basis(HERO)
    hall = origin + HERO * 0.16 - bitangent * 0.12
    pieces = [box_piece("ShrineHall", tuple(hall), (0.42, 0.30, 0.17), 13, temporary,
                        direction=HERO)]
    pieces.append(cone_piece("ShrineRoof", hall + HERO * 0.14, HERO, 0.62, 0.10,
                             0.24, 10, temporary, sides=6))
    pieces += torii_gate("ShrineTorii", origin - bitangent * 0.02 + tangent * 0.02,
                         HERO, 0.58, 0.70, temporary)
    pieces += sakura_tree("ShrineCrown", origin - tangent * 0.62 + bitangent * 0.10,
                          (HERO - tangent * 0.24).normalized(), 0.54, 0.56, temporary,
                          puffs=5, subdivisions=1, colors=(6, 7, 8, 9))
    for side in (-1, 1):
        pieces += stone_lantern(f"ShrineLantern{side}",
                                origin + tangent * side * 0.42 - bitangent * 0.34,
                                HERO, 0.34, temporary, scale=0.9)
    return pieces


LANDMARKS = [
    landmark_01, landmark_02, landmark_03, landmark_04, landmark_05,
    landmark_06, landmark_07, landmark_08, landmark_09, landmark_10,
]


def emphasize_landmark(pieces, center, index):
    # Widen the hero formation across the facing disc so it still reads at
    # gameplay size, while keeping its height off the surface restrained.
    planar_factors = (1.86, 2.15, 2.10, 2.16, 2.18, 1.82, 1.86, 2.05, 2.10, 2.00)
    planar = planar_factors[index]
    # Height is amplified, not squashed: at sprite size a landmark lying flat on
    # the surface disappears into the orchard.
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
    uv = planet.data.uv_layers.new(name="SakuraPaletteUV")
    for polygon in planet.data.polygons:
        slot = planet.material_slots[polygon.material_index]
        material = slot.material
        index = int(material.get("palette_index", 7)) if material else 7
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
    planet["hero_landmark"] = name.replace("Sakura_", "")
    planet["mobile_optimized"] = True
    planet["single_material"] = True
    return planet


def add_camera_and_lights():
    bpy.ops.object.camera_add(location=(8.2, -11.4, 7.8))
    camera = bpy.context.object
    camera.name = "Sakura_RenderCamera"
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

    # Warm spring afternoon: golden key, rose bounce, pale sky rim.
    area("Sakura_KeyLight", (-4.6, -6.4, 10.6), 1420, (1.0, 0.93, 0.82), 6.0)
    area("Sakura_RoseFill", (7.0, -2.4, 1.2), 1350, (1.0, 0.62, 0.72), 6.0)
    area("Sakura_SkyRim", (0.0, 5.6, 7.2), 940, (0.72, 0.86, 1.0), 4.0)
    # Lifts the lower third of the globe out of near-black.
    area("Sakura_UnderBounce", (2.0, -6.0, -6.4), 760, (1.0, 0.80, 0.78), 7.0)


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
    background.inputs["Color"].default_value = (0.16, 0.085, 0.105, 1.0)
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
        uv_layers = len(planet.data.uv_layers)
        materials = len(planet.data.materials)
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

    for index, center_tuple in enumerate(GRID[:PLANET_COUNT]):
        name = f"Sakura_{index + 1:02d}"
        center = Vector(center_tuple)
        pieces = [base_planet(name, center, temporary, 4100 + index)]
        pieces += surface_details(center, temporary, 5100 + index, count=14)
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
    print("SAKURA_COLLECTION_COMPLETE", report)


if __name__ == "__main__":
    main()
