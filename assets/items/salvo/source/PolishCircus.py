"""Apply original red/white/gold livery to the retained Salvo Blender master.

Run after PolishWinch.py, or reapply to the saved master. Geometry and articulation
of approved mechanisms and ammunition are retained. All dimensions use Godot axes.
"""
from pathlib import Path
from math import sin, cos, pi
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'source' / 'SalvoCatapult.blend'))
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.frame_set(1)
for obj in list(bpy.data.objects):
    if obj.get('ts175_circus_polish'):
        bpy.data.objects.remove(obj, do_unlink=True)


def v(p): return Vector((p[0], -p[2], p[1]))
def joint(name): return bpy.data.objects[name]


def material(name, color, metallic, roughness):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    mat.node_tree.nodes.clear()
    shader = mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Metallic'].default_value = metallic
    shader.inputs['Roughness'].default_value = roughness
    output = mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    return mat, shader


paint, shader = material('Salvo circus enamel', (.56, .016, .028), .24, .48)
white, _ = material('Salvo circus white', (.92, .90, .84), .12, .52)
gold = bpy.data.materials['Salvo brass bushings']
wood = bpy.data.materials['Salvo oiled ash grain']
# Original deterministic scuffed enamel; no shared ammunition/vehicle texture edits.
name = 'Salvo circus enamel'
texture = bpy.data.images.get(name) or bpy.data.images.new(name, 256, 256)
y, x = np.mgrid[:256, :256] / 256
noise = np.random.default_rng(17504).random((256, 256))
shade = .78 + .16 * noise + .04 * np.sin(x * 43 + y * 11)
pixels = np.ones((256, 256, 4), dtype=np.float32)
pixels[:, :, :3] = shade[:, :, None] * np.array([.56, .016, .028])
scuffs = (noise > .997) | ((np.sin(y * 230 + x * 13) > .998) & (noise > .65))
pixels[scuffs, :3] = (.13, .11, .085)
texture.pixels.foreach_set(pixels.ravel())
texture.filepath_raw = str(ROOT / 'SalvoCatapult_Salvo circus enamel.png')
texture.file_format = 'PNG'
texture.save()
texture.pack()
texture.filepath_raw = ''
tex = paint.node_tree.nodes.new('ShaderNodeTexImage')
tex.image = texture
paint.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color'])

# Paint fixed outer housings; keep runners, spring/seats, bearings, fasteners,
# rope, drum, motors and every firecracker in their existing contrasting materials.
painted_prefixes = (
    'root ash beam', 'middle ash beam', 'tip ash beam', 'steel saddle',
    'Rack mounting shoe', 'Platform guide channel', 'Platform nose crosshead',
    'Platform rear tie', 'Rearward plate root', 'Plate bearing clevis',
    'Hinge lift guide', 'magazine side flange', 'Behind spring rope strap',
)
for obj in bpy.data.objects:
    if obj.type == 'MESH' and obj.name.startswith(painted_prefixes):
        obj.data.materials.clear()
        obj.data.materials.append(paint)
        for polygon in obj.data.polygons:
            polygon.material_index = 0

# Outside bowl: six bold ivory meridians on red; retain the warm wood interior.
bowl = joint('Carved ash bowl')
bowl.data.materials.clear()
for mat in (wood, paint, white): bowl.data.materials.append(mat)
for polygon in bowl.data.polygons:
    if polygon.index < 9 * 48:
        polygon.material_index = 2 if polygon.index % 8 < 2 else 1
    elif 18 * 48 <= polygon.index < 19 * 48:
        polygon.material_index = 2 if polygon.index % 8 < 2 else 1
    else:
        polygon.material_index = 0


def patch(name, points, faces, mat, parent):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([v(p) for p in points], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    obj['ts175_circus_polish'] = True
    mesh.materials.append(mat)
    uv = mesh.uv_layers.new(name='Paint UV')
    for polygon in mesh.polygons:
        for index in polygon.loop_indices:
            point = mesh.vertices[mesh.loops[index].vertex_index].co
            uv.data[index].uv = (point.y * 2, point.z * 2)
    return obj


def side_patch(name, x, y0, y1, z0, z1, slant, mat, parent):
    points = [(x,y0,z0),(x,y0,z1),(x,y1,z1+slant),(x,y1,z0+slant)]
    # Godot-to-Blender mapping has positive determinant; choose outward winding.
    face = (0,1,2,3) if x < 0 else (3,2,1,0)
    return patch(name, points, [face], mat, parent)


def star(name, mapping, radius, parent, facing=1):
    points = [mapping(0, 0)]
    for i in range(10):
        angle = pi / 2 + i * pi / 5
        r = radius if i % 2 == 0 else radius * .43
        points.append(mapping(r * cos(angle), r * sin(angle)))
    faces = [(0, 1+i, 1+(i+1)%10) for i in range(10)]
    if facing < 0: faces = [face[::-1] for face in faces]
    patch(name, points, faces, gold, parent)


# Two travelling diagonal bands per telescopic beam; star only on the broad root.
for parent, half_width, half_height, centers in [
    (joint('SpoonHinge'), .105, .075, [.52,.64]),
    (joint('ArmMiddle'), .085, .060, [.15,.28]),
    (joint('ArmTip'), .070, .050, [.10,.20]),
]:
    for sign in [-1, 1]:
        x = sign * (half_width + .0015)
        for z in centers:
            side_patch('Circus beam stripe', x, -half_height+.013, half_height-.013,
                       z-.028, z+.028, .025, white, parent)
        if parent.name == 'SpoonHinge':
            star('Circus beam star', lambda u,w: (x,w,.27+u), .055, parent, -sign)

# Magazine livery lives on its outer flanges, never on the rounds or feed parts.
for sign in [-1,1]:
    x = sign * .249
    for z in [-.035,.415]:
        side_patch('Circus magazine stripe', x, .09, .20, z, z+.045, .035, white, joint('Magazine'))
    star('Circus magazine star', lambda u,w: (x,.145+w,.23+u), .047, joint('Magazine'), -sign)

# Restrained stripes on the fixed hinge cheeks and plate; sliding steel stays bare.
for sign in [-1,1]:
    for y0 in [.055,.175]:
        side_patch('Circus turret stripe', sign*.1865, y0, y0+.035,
                   -.075, .075, 0, white, joint('TurretYaw'))
    side_patch('Circus plate stripe', sign*.1265, -.105, -.035,
               .38, .425, .025, white, joint('PlateHinge'))

# Gold star medallions on broad red bowl panels; curved to follow the outer shell.
for theta in [pi*.31, pi*1.31]:
    def bowl_point(u, w, theta=theta):
        r = .76 + w
        t = theta + u
        return (.384*r*cos(t), -.204+.31*r*r, .81+.475*r*sin(t))
    star('Circus bowl star', bowl_point, .18, joint('ArmTip'), -1)

joint('SalvoCatapult')['mechanical_revision'] = 4
scene.frame_set(65)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'source' / 'SalvoCatapult.blend'))
scene.frame_set(1)
for obj in bpy.data.objects:
    obj.animation_data_clear()
    if obj.type == 'MESH' and obj.data.shape_keys:
        obj.data.shape_keys.animation_data_clear()
for parent in [obj for obj in bpy.data.objects if obj.type == 'EMPTY']:
    meshes = [child for child in parent.children if child.type == 'MESH' and not child.data.shape_keys]
    if len(meshes) < 2: continue
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes: obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    bpy.context.object.name = parent.name + '_Surfaces'
bpy.ops.export_scene.gltf(filepath=str(ROOT / 'SalvoCatapult.glb'), export_format='GLB',
                         export_yup=True, export_apply=False, export_animations=False, export_morph=True)
print('Applied original circus enamel, deliberate white stripes and gold stars; retained mechanical rig.')
