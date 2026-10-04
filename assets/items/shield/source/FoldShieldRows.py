"""Add a horizontal top/bottom hinge to each of the four approved shield columns."""
from pathlib import Path
from math import pi
import hashlib
import json
import bpy
import bmesh
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'source/Shield.blend'))
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.frame_set(32)
root = bpy.data.objects['Shield']
collection = bpy.data.collections['Carnage Circus - export only']
iron = bpy.data.materials['Circus_WornIron']
brass = bpy.data.materials['Circus_TarnishedBrass']

def place(obj, parent, material):
    for c in list(obj.users_collection): c.objects.unlink(obj)
    collection.objects.link(obj)
    obj.parent = parent
    obj.data.materials.append(material)

if 'Center_L_Top' not in bpy.data.objects:
    # The common center pin must split with the armor, not bridge the folding seam.
    pin = bpy.data.objects['Center hinge continuous pin']
    world = pin.matrix_world.copy()
    pin.parent = bpy.data.objects['Center_L']
    pin.matrix_world = world
    for name in ['Center_L', 'Center_R', 'Wing_L', 'Wing_R']:
        column = bpy.data.objects[name]
        center = name.startswith('Center')
        side = -1 if name.endswith('L') else 1
        # Center tops fold behind the face; wing tops fold forward so the two
        # layers nest on the outside of the center when the wing folds inward.
        pivot_y = .50 if center else -.30
        originals = [o for o in column.children if o.type == 'MESH']
        halves = {}
        for label, sign in [('Top', 1), ('Bottom', -1)]:
            half = bpy.data.objects.new(name+'_'+label, None)
            collection.objects.link(half)
            half.parent = column
            half.location = (0, pivot_y, 0)
            halves[label] = half
            for original in originals:
                mesh = bmesh.new()
                mesh.from_mesh(original.data)
                mesh.transform(original.matrix_local)
                bmesh.ops.bisect_plane(mesh, geom=list(mesh.verts)+list(mesh.edges)+list(mesh.faces),
                    dist=.000001, plane_co=(0, 0, sign*.008), plane_no=(0, 0, sign), clear_inner=True)
                if not mesh.faces:
                    mesh.free()
                    continue
                boundary = [e for e in mesh.edges if e.is_boundary]
                if boundary: bmesh.ops.holes_fill(mesh, edges=boundary, sides=0)
                bmesh.ops.recalc_face_normals(mesh, faces=list(mesh.faces))
                mesh.transform(Matrix.Translation((0, -pivot_y, 0)))
                data = bpy.data.meshes.new(half.name+'_'+original.data.name)
                mesh.to_mesh(data)
                mesh.free()
                for material in original.data.materials: data.materials.append(material)
                obj = bpy.data.objects.new(half.name+'_'+original.name, data)
                collection.objects.link(obj)
                obj.parent = half
        for original in originals: bpy.data.objects.remove(original, do_unlink=True)
        width = 1.85 if center else 1.45
        for index, x in enumerate([side*width*.28, side*width*.72]):
            for label, sign in [('Top', 1), ('Bottom', -1)]:
                bpy.ops.mesh.primitive_cube_add(size=1, location=(x, -.10 if center else .10, sign*.12))
                lug = bpy.context.object
                lug.name = name+' '+label+' horizontal hinge leaf'
                lug.scale = (.30, .26, .24)
                bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
                place(lug, halves[label], iron)
                for offset in [-.105, 0, .105] if sign == 1 else [-.0525, .0525]:
                    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.055, depth=.048,
                        location=(x+offset, 0, 0), rotation=(0, pi/2, 0))
                    place(bpy.context.object, halves[label], brass)
            bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=.023, depth=.32,
                location=(x, 0, 0), rotation=(0, pi/2, 0))
            place(bpy.context.object, halves['Bottom'], iron)
        # Batch fittings per half/material without merging across an articulation.
        for half in halves.values():
            for material in [iron, brass]:
                parts = [o for o in half.children if o.type == 'MESH' and
                         len(o.data.materials) == 1 and o.data.materials[0] == material]
                if len(parts) < 2: continue
                bpy.ops.object.select_all(action='DESELECT')
                for obj in parts: obj.select_set(True)
                bpy.context.view_layer.objects.active = parts[0]
                bpy.ops.object.join()
                parts[0].name = half.name+'_'+material.name

for frame, vertical, horizontal, wings in [(1, 1, 1, 1), (20, 0, 1, 0), (32, 0, 0, 0), (60, 0, 0, -1)]:
    for side, suffix in [(-1, 'L'), (1, 'R')]:
        center = bpy.data.objects['Center_'+suffix]
        center.rotation_euler.z = -side*pi/2*vertical
        center.keyframe_insert(data_path='rotation_euler', frame=frame)
        wing = bpy.data.objects['Wing_'+suffix]
        wing.location = (side*1.85, .32+1.2*max(wings, 0), 0)
        wing.rotation_euler.z = side*pi/2*(1+wings)
        wing.keyframe_insert(data_path='location', frame=frame)
        wing.keyframe_insert(data_path='rotation_euler', frame=frame)
        for prefix, sign in [('Center', -1), ('Wing', 1)]:
            top = bpy.data.objects[prefix+'_'+suffix+'_Top']
            top.rotation_euler.x = sign*pi*horizontal
            top.keyframe_insert(data_path='rotation_euler', frame=frame)
scene['Animation'] = '1 eight-section packed stack; 20 horizontal fold; 32 rear shield; 60 wall. Center tops fold back; wing tops forward. Supporting carriage unchanged.'
scene.frame_set(1)
vertices = [o.matrix_world@v.co for o in root.children_recursive if o.type == 'MESH' for v in o.data.vertices]
print('PACKED_BOUNDS', [(min(v[i] for v in vertices), max(v[i] for v in vertices)) for i in range(3)], flush=True)
scene.frame_set(32)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/Shield.blend'))
root.location = (0, 0, 0)
bpy.ops.object.select_all(action='DESELECT')
for obj in [root]+list(root.children_recursive): obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'Shield.glb'), export_format='GLB',
    use_selection=True, export_yup=True, export_animations=False, export_apply=True)
manifest = json.loads((ROOT/'sources.json').read_text())
for path in [ROOT/'Shield.glb', ROOT/'source/Shield.blend']:
    manifest['sha256'][path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
(ROOT/'sources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print('SHIELD_EIGHT_SECTION_AUTHORING_COMPLETE', flush=True)
