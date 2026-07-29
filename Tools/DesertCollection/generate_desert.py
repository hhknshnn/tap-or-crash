import bpy
import math
import os
from mathutils import Vector, Matrix

ROOT = r"C:\Users\HAKAN\Documents\GitHub\tap-or-crash-new"
MODEL_DIR = os.path.join(ROOT, "Assets", "Models", "DesertPlanets")
SPRITE_DIR = os.path.join(ROOT, "Assets", "Sprites", "DesertPlanets")
os.makedirs(MODEL_DIR, exist_ok=True)
os.makedirs(SPRITE_DIR, exist_ok=True)

PALETTE = [
    (0.90, 0.55, 0.13, 1.0),  # golden sand
    (1.00, 0.69, 0.23, 1.0),  # sunlit sand
    (0.80, 0.30, 0.10, 1.0),  # terracotta
    (0.91, 0.67, 0.39, 1.0),  # sandstone
    (0.52, 0.19, 0.07, 1.0),  # rock shadow
    (0.07, 0.40, 0.20, 1.0),  # cactus
    (0.15, 0.67, 0.30, 1.0),  # fresh green
    (0.04, 0.55, 0.67, 1.0),  # turquoise
    (0.12, 0.72, 0.92, 1.0),  # oasis blue
    (0.96, 0.36, 0.42, 1.0),  # flower coral
    (1.00, 0.86, 0.34, 1.0),  # flower yellow
    (0.56, 0.88, 0.88, 1.0),  # crystal pale
    (0.48, 0.25, 0.13, 1.0),  # palm trunk
    (0.37, 0.20, 0.13, 1.0),  # fossil/bone shadow
    (0.93, 0.90, 0.72, 1.0),  # bone
    (0.71, 0.36, 0.42, 1.0),  # magical mauve
]

ANIMATIONS = [
    "oasis_ripples", "cactus_blossom_drift", "temple_dust_motes",
    "canyon_sand_gust", "arch_heat_shimmer", "crystal_sparkle",
    "palm_leaf_sway", "wind_ripple_drift", "ruin_fireflies",
    "sanctuary_solar_dust",
]

# The approved packs present their identity across the camera-facing hemisphere,
# not only on the north pole.  Keep this vector matched to the render camera.
VIEW_FRONT = Vector((8.2, -11.4, 7.8)).normalized()
VIEW_RIGHT = VIEW_FRONT.cross(Vector((0, 0, 1))).normalized()
VIEW_UP = VIEW_RIGHT.cross(VIEW_FRONT).normalized()

def view_normal(x=0.0, y=0.0, depth=1.0):
    """Surface normal in render-view space; x/y are thumbnail composition offsets."""
    return (VIEW_FRONT * depth + VIEW_RIGHT * x + VIEW_UP * y).normalized()

def clear_scene():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials,
                       bpy.data.cameras, bpy.data.lights):
        for block in list(datablocks):
            if block.users == 0:
                datablocks.remove(block)

def make_palette():
    path = os.path.join(MODEL_DIR, "Desert_Palette.png")
    img = bpy.data.images.get("Desert_Palette") or bpy.data.images.new(
        "Desert_Palette", width=len(PALETTE), height=1, alpha=True
    )
    pixels = []
    for c in PALETTE:
        pixels.extend(c)
    img.pixels = pixels
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()

    mat = bpy.data.materials.new("Desert_Palette_URP")
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
    bsdf.inputs["Roughness"].default_value = 0.82
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
    if not obj.data.materials:
        obj.data.materials.append(MAT)
    for poly in mesh.polygons:
        poly.use_smooth = False

def finish_piece(obj, name, color_index):
    obj.name = name
    color_uv(obj, color_index)
    PIECES.append(obj)
    return obj

def align_outward(obj, normal, roll=0.0):
    n = Vector(normal).normalized()
    obj.rotation_mode = 'QUATERNION'
    obj.rotation_quaternion = Vector((0, 0, 1)).rotation_difference(n)
    if roll:
        obj.rotation_quaternion = Vector(n).rotation_difference(Vector(n)) @ obj.rotation_quaternion
    obj.location = n * 2.92
    return n

def ico(name, normal, scale, color=3, subdivisions=1, sink=0.0):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1.0)
    o = bpy.context.object
    n = align_outward(o, normal)
    o.location = n * (2.92 - sink)
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_piece(o, name, color)

def cone(name, normal, radius, depth, color, vertices=7, offset=0.0, taper=0.65):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices, radius1=radius,
                                    radius2=radius*taper, depth=depth)
    o = bpy.context.object
    n = align_outward(o, normal)
    o.location = n * (2.92 + depth*0.5 + offset)
    return finish_piece(o, name, color)

def cylinder(name, normal, radius, depth, color, vertices=8, offset=0.0):
    return cone(name, normal, radius, depth, color, vertices, offset, 1.0)

def cube(name, normal, scale, color, offset=0.0, tangent_shift=(0, 0)):
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    o = bpy.context.object
    n = align_outward(o, normal)
    tangent = n.cross(Vector((0, 0, 1)))
    if tangent.length < 0.1:
        tangent = n.cross(Vector((0, 1, 0)))
    tangent.normalize()
    bitangent = n.cross(tangent).normalized()
    o.location = n * (2.92 + scale[2]*0.5 + offset) + tangent*tangent_shift[0] + bitangent*tangent_shift[1]
    o.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_piece(o, name, color)

def panel_cube(name, normal, size, color, screen_shift=(0, 0), offset=0.0):
    """A box composed in the local screen/tangent plane: width, height, depth."""
    n = Vector(normal).normalized()
    right = n.cross(Vector((0, 0, 1)))
    if right.length < 0.1:
        right = n.cross(Vector((0, 1, 0)))
    right.normalize()
    up = n.cross(right).normalized()
    bpy.ops.mesh.primitive_cube_add(size=1.0)
    o = bpy.context.object
    basis = Matrix((right, up, n)).transposed().to_4x4()
    basis.translation = (
        n * (2.92 + size[2]*0.5 + offset)
        + right * screen_shift[0]
        + up * screen_shift[1]
    )
    o.matrix_world = basis
    o.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish_piece(o, name, color)

def panel_disc(name, normal, radius, depth, color, screen_shift=(0, 0), vertices=18, scale_y=1.0):
    n = Vector(normal).normalized()
    right = n.cross(Vector((0, 0, 1)))
    if right.length < 0.1:
        right = n.cross(Vector((0, 1, 0)))
    right.normalize()
    up = n.cross(right).normalized()
    o = disc(name, n, radius, depth, color, vertices, 0.0, scale_y)
    o.location += right*screen_shift[0] + up*screen_shift[1]
    return o

def disc(name, normal, radius, depth, color, vertices=18, offset=0.0, scale_y=1.0):
    o = cylinder(name, normal, radius, depth, color, vertices, offset)
    o.scale.x = scale_y
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return o

def cactus(normal, size=1.0, flower=False, variant=0):
    stem = cone("CactusStem", normal, 0.13*size, 0.82*size, 5, 7, taper=0.82)
    n = Vector(normal).normalized()
    tangent = n.cross(Vector((0, 0, 1)))
    if tangent.length < 0.1:
        tangent = n.cross(Vector((0, 1, 0)))
    tangent.normalize()
    for sign, height in ((-1, 0.20), (1, 0.34 if variant else 0.26)):
        arm_n = (n + tangent*0.025*sign).normalized()
        arm = cone("CactusArm", arm_n, 0.075*size, 0.34*size, 6, 6, offset=height*size, taper=0.82)
        arm.location += tangent * sign * 0.16 * size
        arm.rotation_quaternion = Vector((0,0,1)).rotation_difference((n + tangent*sign*0.45).normalized())
    if flower:
        ico("CactusFlower", n, (0.13*size,)*3, 9 if variant else 10, 1)

def palm(normal, size=1.0, lean=0.0):
    n = Vector(normal).normalized()
    trunk = cone("PalmTrunk", n, 0.11*size, 0.90*size, 12, 7, taper=0.65)
    crown = n * (2.92 + 0.88*size)
    tangent = n.cross(Vector((0,0,1)))
    if tangent.length < 0.1:
        tangent = n.cross(Vector((0,1,0)))
    tangent.normalize()
    for i in range(6):
        ang = i * math.tau/6 + lean
        direction = (tangent*math.cos(ang) + n.cross(tangent)*math.sin(ang)).normalized()
        bpy.ops.mesh.primitive_cone_add(vertices=3, radius1=0.34*size, radius2=0.03,
                                        depth=0.72*size)
        leaf = bpy.context.object
        leaf.location = crown + direction*0.30*size
        leaf.rotation_mode = 'QUATERNION'
        leaf.rotation_quaternion = Vector((0,0,1)).rotation_difference((direction + n*0.20).normalized())
        finish_piece(leaf, "PalmLeaf", 6 if i%2 else 5)

def crystal_cluster(normal, size=1.0, count=4, warm=False):
    n = Vector(normal).normalized()
    tangent = n.cross(Vector((0,0,1)))
    if tangent.length < 0.1:
        tangent = n.cross(Vector((0,1,0)))
    tangent.normalize()
    up = n.cross(tangent).normalized()
    for i in range(count):
        d = (i-(count-1)/2)*0.16*size
        c = cone("Crystal", n, 0.11*size, (0.48+0.12*(i%3))*size,
                 15 if warm and i%2 else (11 if i%2 else 7), 5, offset=0.02, taper=0.0)
        c.location += tangent*d + up*(0.03 + .04*(i%2))*size
        crystal_axis = (n + tangent*d*0.22 + up*(.42 + .08*(i%3))).normalized()
        c.rotation_quaternion = Vector((0,0,1)).rotation_difference(crystal_axis)

def flower(normal, color=9, size=1.0):
    n = Vector(normal).normalized()
    cone("FlowerStem", n, 0.025*size, 0.20*size, 6, 5, taper=0.75)
    for i in range(5):
        ico("FlowerPetal", (n + Vector((math.cos(i*math.tau/5), math.sin(i*math.tau/5), 0))*0.015).normalized(),
            (0.065*size, 0.035*size, 0.025*size), color, 1)

def rock_stack(normal, layers=3, size=1.0):
    n = Vector(normal).normalized()
    right = n.cross(Vector((0,0,1)))
    if right.length < 0.1:
        right = n.cross(Vector((0,1,0)))
    right.normalize()
    up = n.cross(right).normalized()
    for i in range(layers):
        r = (0.34 - i*0.055)*size
        o = ico("LayeredRock", n, (r*1.22, r*0.72, r*0.38), 2 if i%2 else 4, 1)
        o.location += up*i*0.22*size + right*((i%2)*.07-.035)*size + n*i*.015

def arch(normal, size=1.0, legendary=False):
    n = Vector(normal).normalized()
    levels = 3 if legendary else 4
    for side in (-1, 1):
        for h in range(levels):
            panel_cube("ArchStone", n, (.28*size,.31*size,.24*size),
                       3 if h%2 else 2,
                       (side*.62*size, (-.34+h*.30)*size))
    for i in range(7):
        ang = i*math.pi/6
        x = math.cos(ang)*.62*size
        y = (.52+math.sin(ang)*.58)*size
        panel_cube("ArchCrown", n, (.29*size,.27*size,.25*size),
                   3 if i%2 else 2, (x,y))

def sand_details(seed):
    # Low-profile forms enrich the sphere without competing with the landmark.
    # Visible marks are asymmetrical; the rim/back marks preserve rotation appeal.
    visible = [
        (-.72,-.44), (-.42,-.68), (-.08,-.76), (.34,-.66), (.68,-.40),
        (-.76,.08), (-.48,.46), (.02,.62), (.52,.44), (.76,.06),
    ]
    count = 7 + seed // 3
    for i, (x, y) in enumerate(visible[:count]):
        n = view_normal(x, y, .92)
        dune = ico("SandDune", n, (0.38 + .04*((i+seed)%3), .22, .075),
                   1 if (i+seed)%3 else 0, 1, sink=0.045)
        dune.rotation_euler.rotate_axis('Z', (seed*.37+i*.81) % math.pi)

    pebble_spots = [(-.62,.28),(-.26,-.46),(.18,.48),(.52,-.34),(.70,.22)]
    for i, (x, y) in enumerate(pebble_spots):
        n = view_normal(x + .03*(seed%3), y, .94)
        ico("Pebble", n, (.11+.02*(i%2), .085, .07), 3 if i%2 else 4, 1)

    # Sparse far-side accents keep every rotation readable while retaining clean space.
    rim = [
        (VIEW_RIGHT*.92 + VIEW_UP*.12 - VIEW_FRONT*.24).normalized(),
        (-VIEW_RIGHT*.86 + VIEW_UP*.34 - VIEW_FRONT*.30).normalized(),
        (VIEW_RIGHT*.26 - VIEW_UP*.90 - VIEW_FRONT*.22).normalized(),
    ]
    for i, n in enumerate(rim):
        if (i + seed) % 2 == 0:
            ico("RimStone", n, (.23,.17,.14), 4 if i%2 else 2, 1)

    # Three restrained far-side silhouette notes prevent a plain-sphere read during
    # 3D rotation.  They remain subordinate to the hero landmark in height and color.
    for i, z in enumerate((.34,-.22,.56)):
        az = math.atan2(VIEW_FRONT.y, VIEW_FRONT.x) + math.pi + (i-1)*1.18 + seed*.11
        radial = math.sqrt(max(0.01, 1.0-z*z))
        n = Vector((math.cos(az)*radial, math.sin(az)*radial, z)).normalized()
        family = (seed+i) % 3
        if family == 0:
            cone("FarRockFin", n, .13, .48, 4, 5, taper=.18)
        elif family == 1:
            cone("FarDryBush", n, .12, .40, 2, 6, taper=.08)
        else:
            cone("FarCactus", n, .105, .45, 5, 6, taper=.72)

def build_identity(index):
    front = view_normal(-.06, .12)
    if index == 1:
        disc("OasisRim", front, 1.18, 0.11, 3, 24, scale_y=.72)
        disc("OasisWater", front, 0.95, 0.12, 8, 24, offset=0.05, scale_y=.72)
        palm(view_normal(.33,.18), 1.30)
        cactus(view_normal(-.35,.12), 1.05, True)
        rock_stack(view_normal(.16,-.44), 2, .60)
    elif index == 2:
        cluster = [(-.30,.04),(.27,.08),(-.06,.17),(-.19,-.24),(.33,-.19)]
        for i,(x,y) in enumerate(cluster):
            size = 2.20 if i == 2 else (1.02+0.10*(i%2))
            cactus(view_normal(x,y), size, i in (1,4), i%2)
        flower(view_normal(-.60,-.46), 9, .85)
        flower(view_normal(.58,-.48), 10, .75)
    elif index == 3:
        # Sun-gate temple: orthogonal silhouette, turquoise seal.
        for side in (-1,1):
            panel_cube("TemplePillar", front, (.36,1.42,.34), 3, (side*.77,.12))
            panel_cube("TempleCapital", front, (.55,.22,.38), 2, (side*.77,.91))
        panel_cube("TempleLintel", front, (1.82,.29,.38), 3, (0,1.12))
        panel_disc("SunSeal", front, .29, .11, 7, (0,1.11), 14)
        panel_cube("TempleStep", front, (2.06,.22,.24), 4, (0,-.70))
        panel_cube("TempleStep", front, (1.75,.19,.26), 3, (0,-.48))
        panel_cube("TempleStep", front, (1.42,.17,.29), 2, (0,-.30))
        for x,y in [(.62,-.44),(-.58,-.48),(.54,.48),(-.52,.52)]:
            ico("TempleOfferingStone", view_normal(x,y), (.13,.10,.08), 3, 1)
    elif index == 4:
        for x,y,l,s in [(-.31,.04,4,1.75),(.26,.00,4,1.50),(.02,.34,3,1.10)]:
            rock_stack(view_normal(x,y),l,s)
        cactus(view_normal(.56,-.28),.80,False,1)
    elif index == 5:
        arch(front, 1.52)
        flower(view_normal(-.58,-.48), 15, .75)
        flower(view_normal(.60,-.46), 7, .75)
        rock_stack(view_normal(.54,.42), 2, .55)
    elif index == 6:
        crystal_cluster(view_normal(0,.10), 2.70, 7)
        crystal_cluster(view_normal(-.50,-.02), .82, 4, True)
        crystal_cluster(view_normal(.52,-.08), .72, 3)
        disc("MiragePool", view_normal(.10,-.46), .68, .06, 8, 20, scale_y=.64)
    elif index == 7:
        disc("PalmValleyWater", front, 1.00, 0.07, 8, 20, scale_y=.52)
        palm_specs = [(-.40,.06,.90),(.36,.08,.90),(-.13,.25,1.22),(.19,-.24,.82)]
        for i,(x,y,size) in enumerate(palm_specs):
            palm(view_normal(x,y),size,i*.25)
        flower(view_normal(.60,-.46),9,.8)
    elif index == 8:
        # Wind-carved fins, each tilted differently.
        for i,(x,y) in enumerate([(-.38,.02),(-.12,.16),(.18,.10),(.42,-.02)]):
            n=view_normal(x,y)
            o=cone("WindFin", n, .39+.04*i, 1.55+.18*(i%2), 3 if i%2 else 2, 5, taper=.14)
            o.scale.x=.42
            bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
            right=n.cross(Vector((0,0,1))).normalized()
            up=n.cross(right).normalized()
            o.rotation_quaternion=Vector((0,0,1)).rotation_difference(
                (n+up*(.52+.08*(i%2))+right*((i-1.5)*.10)).normalized())
        for i,r in enumerate((.42,.66,.88)):
            disc("SandRipple", front, r, .025, 0 if i == 1 else 1, 18, offset=-.02)
    elif index == 9:
        # Broken observatory/stone circle and fossil.
        for i in range(8):
            ang=i*math.tau/8
            n=(Vector(front)+VIEW_RIGHT*math.cos(ang)*.34+VIEW_UP*math.sin(ang)*.34).normalized()
            cube("StoneCircle", n, (.30,.30,1.00 if i not in (2,5) else .60), 3 if i%2 else 4)
        disc("ObservatoryDial", front,.82,.13,7,18,offset=.38)
        for i in range(5):
            cube("FossilRib", view_normal(-.58,-.44), (.05,.16,.30-i*.03), 14, offset=.05+i*.04,
                 tangent_shift=(-.22+i*.11,0))
    elif index == 10:
        # Legendary solar sanctuary; unique grand landmark.
        panel_cube("SanctuaryStep", front, (2.18,.26,.24), 4, (0,-.79))
        panel_cube("SanctuaryStep", front, (1.85,.24,.28), 2, (0,-.54))
        panel_cube("SanctuaryStep", front, (1.46,.22,.31), 3, (0,-.31))
        for side in (-1,1):
            panel_cube("SolarPillar", front,(.31,1.39,.34),3,(side*.86,.22))
            crystal_cluster((front+Vector((side*.17,0,.02))).normalized(),.52,3,True)
        panel_cube("SolarObelisk", front,(.36,1.70,.36),3,(0,.22))
        panel_disc("SolarEye", front,.34,.11,7,(0,.48),14)
        arch(view_normal(-.62,-.42),.56,True)
        palm(view_normal(.60,-.22),.78,.3)

def join_and_optimize(name, index):
    bpy.ops.object.select_all(action='DESELECT')
    for p in PIECES:
        p.select_set(True)
    bpy.context.view_layer.objects.active = PIECES[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    obj.data.name = name + "_Mesh"
    obj.location = (0,0,0)
    obj.rotation_euler = (0,0,0)
    obj.scale = (1,1,1)
    obj["biome"] = "Desert"
    obj["planet_index"] = index
    obj["environment_animation"] = ANIMATIONS[index-1]
    obj["gameplay_clearance_radius"] = 3.0
    obj["lod_ready"] = True
    tri = obj.modifiers.new("ExportTriangulation", "TRIANGULATE")
    tri.quad_method = 'BEAUTY'
    tri.ngon_method = 'BEAUTY'
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.modifier_apply(modifier=tri.name)
    obj.data.validate(clean_customdata=False)
    for poly in obj.data.polygons:
        poly.use_smooth = False
    tris = len(obj.data.polygons)
    if tris > 2480:
        mod = obj.modifiers.new("MobileTriangleBudget", "DECIMATE")
        mod.ratio = 2380.0 / tris
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
    # Ensure tangent data and normals are clean.
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return obj

def setup_render():
    world = bpy.context.scene.world or bpy.data.worlds.new("DesertWorld")
    bpy.context.scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.045,0.055,0.08,1)
    world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.55
    bpy.ops.object.camera_add(location=(8.2,-11.4,7.8))
    cam=bpy.context.object
    cam.name="Desert_RenderCamera"
    cam.data.type='ORTHO'
    cam.data.ortho_scale=6.85
    cam.data.shift_y=0.0
    direction = Vector((0,0,0))-cam.location
    cam.rotation_euler=direction.to_track_quat('-Z','Y').to_euler()
    bpy.context.scene.camera=cam
    bpy.ops.object.light_add(type='AREA', location=(-4.5,-6.5,10.5))
    key=bpy.context.object
    key.name="Desert_KeyLight"
    key.data.energy=1080
    key.data.color=(1.0,0.78,0.58)
    key.data.shape='DISK'
    key.data.size=5.0
    key.rotation_euler=(Vector((0,0,0))-key.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.object.light_add(type='AREA', location=(6.0,1.0,3.0))
    fill=bpy.context.object
    fill.name="Desert_FillLight"
    fill.data.energy=680
    fill.data.color=(0.52,0.70,1.0)
    fill.data.size=6.0
    fill.rotation_euler=(Vector((0,0,0))-fill.location).to_track_quat('-Z','Y').to_euler()
    scene=bpy.context.scene
    scene.render.engine='BLENDER_EEVEE'
    scene.eevee.taa_render_samples=96
    if hasattr(scene.eevee, "use_gtao"):
        scene.eevee.use_gtao=True
        scene.eevee.gtao_distance=3.0
        scene.eevee.gtao_factor=1.1
    scene.render.resolution_x=1024
    scene.render.resolution_y=1024
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.image_settings.color_mode='RGBA'
    scene.render.film_transparent=True
    scene.render.image_settings.color_depth='8'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.render.resolution_percentage=100
    return cam,key,fill

def export_and_render(obj, index, all_planets):
    name=f"Desert_{index:02d}"
    for p in all_planets:
        p.hide_render = p != obj
    bpy.context.scene.render.filepath=os.path.join(SPRITE_DIR,name+".png")
    bpy.ops.render.render(write_still=True)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(MODEL_DIR,name+".fbx"),
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
PLANETS=[]
for index in range(1,11):
    PIECES=[]
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=4, radius=3.0, location=(0,0,0))
    base=bpy.context.object
    finish_piece(base,"PerfectSphere",0 if index%3 else 1)
    build_identity(index)
    sand_details(index)
    obj=join_and_optimize(f"Desert_{index:02d}",index)
    PLANETS.append(obj)

CAM,KEY,FILL=setup_render()
for index,obj in enumerate(PLANETS,1):
    export_and_render(obj,index,PLANETS)

# Presentation layout in the source blend. Exported FBX pivots remain at exact origin.
for i,obj in enumerate(PLANETS):
    obj.hide_render=False
    obj.location=((i%5)*7.2-14.4, (i//5)*7.2-3.6, 0)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(MODEL_DIR,"Desert_Planet_Collection.blend"))

report=[]
for obj in PLANETS:
    tris=sum(len(p.vertices)-2 for p in obj.data.polygons)
    report.append(f"{obj.name}: tris={tris}, verts={len(obj.data.vertices)}, uv={bool(obj.data.uv_layers)}, mats={len(obj.data.materials)}")
print("DESERT_BUILD_COMPLETE\n"+"\n".join(report))
