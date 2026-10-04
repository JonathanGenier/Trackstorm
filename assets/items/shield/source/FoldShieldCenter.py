"""Split the approved painted center into two closed, hinged rigid leaves.
Preserve the accepted carriage and all opened shield artwork except the hinge seam.
"""
from pathlib import Path
from math import pi
import hashlib
import json
import bpy
import bmesh
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'source/Shield.blend'))
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.frame_set(32)
root = bpy.data.objects['Shield']
center = bpy.data.objects['Center']
collection = bpy.data.collections['Carnage Circus - export only']
if 'Center_L' not in bpy.data.objects:
    originals = [o for o in center.children if o.type == 'MESH']
    for side, name in [(-1, 'L'), (1, 'R')]:
        panel = bpy.data.objects.new('Center_'+name, None)
        collection.objects.link(panel)
        panel.parent = center
        panel.location = (0, -.32, 0)
        for original in originals:
            mesh = bmesh.new()
            mesh.from_mesh(original.data)
            mesh.transform(original.matrix_local)
            bmesh.ops.bisect_plane(mesh, geom=list(mesh.verts)+list(mesh.edges)+list(mesh.faces),
                dist=0.000001, plane_co=(side*.009, 0, 0), plane_no=(side, 0, 0),
                clear_inner=True)
            if not mesh.faces:
                mesh.free()
                continue
            boundary = [e for e in mesh.edges if e.is_boundary]
            if boundary: bmesh.ops.holes_fill(mesh, edges=boundary, sides=0)
            bmesh.ops.recalc_face_normals(mesh, faces=list(mesh.faces))
            mesh.transform(Matrix.Translation((0, .32, 0)))
            data = bpy.data.meshes.new(panel.name+'_'+original.data.name)
            mesh.to_mesh(data)
            mesh.free()
            for material in original.data.materials: data.materials.append(material)
            obj = bpy.data.objects.new(panel.name+'_'+original.name, data)
            collection.objects.link(obj)
            obj.parent = panel
        wing = bpy.data.objects['Wing_'+name]
        wing.animation_data_clear()
        world = wing.matrix_world.copy()
        wing.parent = panel
        wing.matrix_world = world
        # Alternating knuckles on a common vertical axle, in front of the rib tips.
        for i in range(9):
            if (i % 2 == 0) != (side == -1): continue
            bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.048, depth=.235,
                location=(0, 0, -1.04+i*.26))
            knuckle = bpy.context.object
            knuckle.name = 'Center hinge knuckle '+name
            for c in list(knuckle.users_collection): c.objects.unlink(knuckle)
            collection.objects.link(knuckle)
            knuckle.parent = panel
            knuckle.data.materials.append(bpy.data.materials['Circus_WornIron'])
    for obj in originals: bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.022, depth=2.46, location=(0, -.32, 0))
    pin = bpy.context.object
    pin.name = 'Center hinge continuous pin'
    for c in list(pin.users_collection): c.objects.unlink(pin)
    collection.objects.link(pin)
    pin.parent = center
    pin.data.materials.append(bpy.data.materials['Circus_TarnishedBrass'])

for side, name in [(-1, 'L'), (1, 'R')]:
    panel = bpy.data.objects['Center_'+name]
    if panel.name+'_Hinge' in bpy.data.objects: continue
    # Solid hinge leaves bridge the front-set axle back to each armor half.
    for i in range(9):
        if (i % 2 == 0) != (side == -1): continue
        bpy.ops.mesh.primitive_cube_add(size=1, location=(side*.065, .105, -1.04+i*.26))
        lug = bpy.context.object
        lug.name = 'Center hinge leaf '+name
        lug.scale = (.13, .25, .16)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        for c in list(lug.users_collection): c.objects.unlink(lug)
        collection.objects.link(lug)
        lug.parent = panel
        lug.data.materials.append(bpy.data.materials['Circus_WornIron'])
    hinges = [o for o in panel.children if o.type == 'MESH' and o.name.startswith('Center hinge')]
    bpy.ops.object.select_all(action='DESELECT')
    for obj in hinges: obj.select_set(True)
    bpy.context.view_layer.objects.active = hinges[0]
    bpy.ops.object.join()
    hinges[0].name = panel.name+'_Hinge'

for frame, fold, wings in [(1, 1, 1), (32, 0, 0), (60, 0, -1)]:
    for side, name in [(-1, 'L'), (1, 'R')]:
        panel = bpy.data.objects['Center_'+name]
        panel.rotation_euler.z = -side*pi/2*fold
        panel.keyframe_insert(data_path='rotation_euler', frame=frame)
        wing = bpy.data.objects['Wing_'+name]
        wing.location = (side*1.85, .32+.4*max(wings, 0), 0)
        wing.rotation_euler.z = side*pi/2*(1+wings)
        wing.keyframe_insert(data_path='location', frame=frame)
        wing.keyframe_insert(data_path='rotation_euler', frame=frame)
scene['Animation'] = 'Frame 1 four-leaf folded stack; 32 mounted; 60 flat wall. Center_L/Center_R share a visible vertical hinge. Carriage design unchanged.'
scene.frame_set(32)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/Shield.blend'))
root.location = (0, 0, 0)
bpy.ops.object.select_all(action='DESELECT')
for obj in [root]+list(root.children_recursive): obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'Shield.glb'), export_format='GLB',
    use_selection=True, export_yup=True, export_animations=False, export_apply=True)
manifest = json.loads((ROOT/'sources.json').read_text())
for p in [ROOT/'Shield.glb', ROOT/'source/Shield.blend']:
    manifest['sha256'][p.name] = hashlib.sha256(p.read_bytes()).hexdigest()
(ROOT/'sources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print('SHIELD_FOUR_LEAF_AUTHORING_COMPLETE', flush=True)
