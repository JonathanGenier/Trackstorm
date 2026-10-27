"""Original Trackstorm carnival pickup. Blender 5.2; metres, Z up.

Editable boards/trim/hardware are retained in the blend; GLB batches by crate.
No reference pixels or external textures are used. Run with --background --python.
"""
from pathlib import Path
from math import sin, cos, pi
import hashlib
import json
import random
import bpy
import bmesh
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
random.seed(310)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.context.scene.unit_settings.system = 'METRIC'


def material(name, color, metal=0, rough=.72, wood=False):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    bs = mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    n = 1024 if wood else 512
    y, x = np.mgrid[:n, :n] / n
    rng = np.random.default_rng(310)
    noise = rng.random((n, n))
    grain = np.sin(x * 520 + 3*np.sin(y*9) + np.sin(y*31))
    broad = np.sin(x*47 + .6*np.sin(y*18))
    shade = .82 + .09*broad + .06*grain + .07*noise
    rgb = np.array(color)[None, None, :] * shade[:, :, None]
    if wood:
        # Long fine fibres, irregular exposed timber islands and dark splits.
        chips = (np.sin(x*74+np.sin(y*47)*2) + np.sin(y*143+x*39) + .5*noise) > 1.83
        timber = np.stack((.25+.10*noise, .13+.075*noise, .055+.04*noise), axis=-1)
        rgb[chips] = timber[chips]
        cracks = (grain < -.992) & (np.sin(y*27+x*6) > .1)
        rgb[cracks] *= .25
        rgb[noise > .997] *= .3
    else:
        patina = (np.sin(x*81+np.sin(y*41)) + np.sin(y*73+x*32)) > 1.5
        rgb[patina] *= .42
        rgb[noise > .985] *= .48
    pixels = np.ones((n, n, 4), dtype=np.float32)
    pixels[:, :, :3] = np.clip(rgb, 0, 1)
    image = bpy.data.images.new(name, width=n, height=n)
    image.pixels.foreach_set(pixels.ravel())
    image.pack()
    node = mat.node_tree.nodes.new('ShaderNodeTexImage')
    node.image = image
    mat.node_tree.links.new(node.outputs['Color'], bs.inputs['Base Color'])
    if wood:
        # Portable tangent-space grain relief, retained in the packed source.
        height = .06*grain + .02*broad - .09*chips - .06*cracks
        dy, dx = np.gradient(height)
        normal = np.dstack((-dx*5, -dy*5, np.ones_like(dx)))
        normal /= np.linalg.norm(normal, axis=2)[:, :, None]
        pixels[:, :, :3] = normal*.5+.5
        relief = bpy.data.images.new(name+' relief', width=n, height=n)
        relief.colorspace_settings.name = 'Non-Color'
        relief.pixels.foreach_set(pixels.ravel())
        relief.pack()
        tex = mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = relief
        bump = mat.node_tree.nodes.new('ShaderNodeNormalMap')
        bump.inputs['Strength'].default_value = .45
        mat.node_tree.links.new(tex.outputs['Color'], bump.inputs['Color'])
        mat.node_tree.links.new(bump.outputs['Normal'], bs.inputs['Normal'])
    return mat


red = material('Crate crimson wood', (.52, .018, .009), wood=True)
ivory = material('Crate ivory wood', (.70, .62, .46), wood=True)
timber = material('Crate exposed endgrain', (.29, .16, .065), wood=True)
brass = material('Crate aged brass', (.78, .48, .11), .66, .40)
gold = material('Crate raised gold', (.88, .56, .14), .58, .30)
dark = material('Crate recessed iron', (.09, .045, .018), .50, .62)


def empty(name, parent=None, location=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    obj.location = location
    return obj


def finish(obj, name, mat, parent, bevel=0):
    obj.name = name
    obj.parent = parent
    obj.data.materials.append(mat)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('Worn softened edges', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        mod = obj.modifiers.new('Weighted face normals', 'WEIGHTED_NORMAL')
        mod.keep_sharp = True
    return obj


def box(name, loc, size, mat, parent, bevel=.008):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object
    obj.scale = size
    finish(obj, name, mat, parent, bevel)
    # Project UVs using board-local dimensions. Every board has distinct grain.
    uv = obj.data.uv_layers.active
    offset = random.random()
    for face in obj.data.polygons:
        normal = face.normal
        axis = max(range(3), key=lambda i: abs(normal[i]))
        axes = [i for i in range(3) if i != axis]
        for idx in face.loop_indices:
            p = obj.data.vertices[obj.data.loops[idx].vertex_index].co
            uv.data[idx].uv = (p[axes[0]]*.7+offset, p[axes[1]]*.7+.5)
    return obj


def mesh(name, verts, faces, mat, parent):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    uv = data.uv_layers.new(name='UVMap')
    for loop in data.loops:
        p = data.vertices[loop.vertex_index].co
        uv.data[loop.index].uv = (p.x*.7+.5, p.z*.7+.5)
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    data.materials.append(mat)
    return obj


def tube(name, points, radius, mat, parent):
    data = bpy.data.curves.new(name, 'CURVE')
    data.dimensions = '3D'
    data.bevel_depth = radius
    data.bevel_resolution = 1
    spline = data.splines.new('POLY')
    spline.points.add(len(points)-1)
    for p, xyz in zip(spline.points, points):
        p.co = (*xyz, 1)
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.parent = parent
    data.materials.append(mat)


def bolt(x, z, parent, radius=.026):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=10, ring_count=6, radius=1, location=(x, -.768, z))
    obj = bpy.context.object
    obj.scale = (radius, radius*.52, radius)
    finish(obj, 'Riveted brass fastener', brass, parent)
    for face in obj.data.polygons:
        face.use_smooth = True


def plate(sx, sz, parent):
    # Folded brass corner: clipped inside edge, substantial outer silhouette.
    outline = [(0, 0), (.235, 0), (.235, .10), (.10, .235), (0, .235)]
    verts = [(sx*(.704-x), y, .70+sz*(.704-z)) for y in [-.739, -.757] for x, z in outline]
    faces = [(0, 1, 2, 3, 4), (9, 8, 7, 6, 5)]
    faces += [(i, (i+1)%5, (i+1)%5+5, i+5) for i in range(5)]
    obj = mesh('Clipped folded corner plate', verts, faces, brass, parent)
    mod = obj.modifiers.new('Rolled metal edge', 'BEVEL')
    mod.width = .006
    mod.segments = 2
    for dx, dz, r in [(.065, .065, .032), (.18, .04, .014), (.04, .18, .014)]:
        bolt(sx*(.704-dx), .70+sz*(.704-dz), parent, r)


def star(parent):
    rim = []
    for i in range(10):
        a = pi/2+i*pi/5
        r = .40 if i%2 == 0 else .182
        rim.append((r*cos(a), -.724, .69+r*sin(a)))
    # Faceted repoussé star; alternating valleys and raised radial ridges.
    verts = [(0, -.825, .69)] + rim
    faces = [(0, i+1, (i+1)%10+1) for i in range(10)]
    obj = mesh('Oversized raised five point gold star', verts, faces, gold, parent)
    mod = obj.modifiers.new('Star metal thickness', 'SOLIDIFY')
    mod.thickness = .012
    tube('Raised gold star perimeter', rim+[rim[0]], .009, brass, parent)


def scallops(parent):
    for i in range(4):
        cx = -.51+i*.34
        verts = [(cx-.17, -.75, 1.29), (cx+.17, -.75, 1.29)]
        arc = []
        for j in range(17):
            t = j/16
            arc.append((cx+.17-.34*t, -.76-.018*sin(t*pi), 1.29-.15*sin(t*pi)))
        verts += arc
        obj = mesh('Scalloped striped circus valance', verts, [tuple(range(len(verts)))], ivory if i%2==0 else red, parent)
        mod = obj.modifiers.new('Valance thickness', 'SOLIDIFY')
        mod.thickness = .009
        # Small alternating painted centre on each swag.
        patch = [(cx-.055, -.771, 1.288), (cx+.055, -.771, 1.288)]
        patch += [(cx+.055-.11*j/8, -.782, 1.29-.15*sin((.338+.324*j/8)*pi)) for j in range(9)]
        mesh('Contrasting valance stripe', patch, [tuple(range(len(patch)))], red if i%2==0 else ivory, parent)
        tube('Gold scalloped piping', arc, .011, gold, parent)
        bolt(cx-.17, 1.29, parent, .016)


root = empty('CarnivalCrateStack')
crates = []
for name, loc, angle in [('CrateLeft', (-.735, 0, .025), -.025), ('CrateRight', (.735, .025, .025), .025), ('CrateTop', (0, .055, 1.455), -.065)]:
    crate = empty(name, root, loc)
    crate.rotation_euler.z = angle
    crates.append(crate)
    box('Solid timber interior', (0, 0, .70), (1.34, 1.34, 1.35), timber, crate)
    for side in range(4):
        face = empty('Face'+str(side), crate)
        face.rotation_euler.z = side*pi/2
        for i in range(9):
            x = (i-4)*.146
            box('Individual distressed red board', (x, -.682, .70), (.141, .055, 1.30), red, face, .005)
        for sx in [-1, 1]:
            box('Worn white vertical stripe', (sx*.515, -.716, .70), (.105, .034, 1.28), ivory, face, .004)
            box('Red corner upright', (sx*.666, -.717, .70), (.075, .052, 1.38), red, face)
        for z in [.06, 1.34]:
            box('Heavy horizontal frame', (0, -.724, z), (1.41, .075, .115), red if z>1 else timber, face)
            box('Rubbed frame edge', (0, -.764, z-.034), (1.36, .008, .016), timber, face, .002)
        if side%2 == 0:
            star(face)
        else:
            for sign in [-1, 1]:
                beam = box('Diagonal side cross brace', (0, -.729, .70), (.105, .055, 1.58), red, face)
                beam.rotation_euler.y = sign*.73
        scallops(face)
        for sx in [-1, 1]:
            for sz in [-1, 1]:
                plate(sx, sz, face)
        # Sparse long exposed edge chips supplement the baked grain at close range.
        for i in range(15):
            x = random.choice([-1, 1])*random.uniform(.47, .56)
            z = random.uniform(.20, 1.13)
            box('Long worn paint edge', (x, -.736, z), (random.uniform(.005, .017), .002, random.uniform(.015, .075)), timber, face, 0)
    for i in range(9):
        box('Lid plank', ((i-4)*.146, 0, 1.382), (.141, 1.33, .035), red, crate, .004)
    for x in [-.48, .48]:
        box('Ivory lid batten', (x, 0, 1.405), (.09, 1.31, .024), ivory, crate, .004)

# Editable source is saved before flattening or batching any authoring groups.
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/CarnivalCrate.blend'))
for crate in crates:
    objects = [o for o in crate.children_recursive if o.type in {'MESH', 'CURVE'}]
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.convert(target='MESH')
    # Apply evaluated transforms before joining; preserve the named crate pivots.
    bpy.ops.object.join()
    obj = bpy.context.object
    matrix = obj.matrix_world.copy()
    obj.parent = crate
    obj.matrix_world = matrix
    obj.name = crate.name+'Geometry'
for obj in list(bpy.context.scene.objects):
    if obj.type == 'EMPTY' and obj.name.startswith('Face'):
        bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'CarnivalCrate.glb'), export_format='GLB', export_apply=True, export_cameras=False, export_lights=False)
triangles = sum(len(p.vertices)-2 for o in bpy.context.scene.objects if o.type=='MESH' for p in o.data.polygons)
manifest = {'author': 'Original Trackstorm project artwork', 'tool': bpy.app.version_string,
            'reference': 'User-approved 3 crates.png supplied 2026-10-26; retained authoring reference only; no reference pixels in runtime textures.',
            'units': 'metres; Blender Z-up exported to Godot Y-up', 'crateGroups': [o.name for o in crates],
            'triangles': triangles, 'files': {}}
for name in ['source/BuildCarnivalCrate.py', 'source/ApprovedIntact.png', 'source/CarnivalCrate.blend', 'CarnivalCrate.glb']:
    manifest['files'][name] = hashlib.sha256((ROOT/name).read_bytes()).hexdigest()
(ROOT/'sources.json').write_text(json.dumps(manifest, indent=2)+'\n')
print('Carnival crate exported:', triangles, 'triangles')
