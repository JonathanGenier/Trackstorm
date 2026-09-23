"""Author the production junction structure without rebuilding the terrain.

Blender --background --python-exit-code 1 --python assets/maps/infield/BuildStructures.py
Coordinates below are Godot metres (X, up, Z); export converts Blender Z-up.
"""
import hashlib
import json
from pathlib import Path

import bpy
import bmesh

ROOT = Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1

concrete = bpy.data.materials.new('Structural concrete — neutral authoring material')
concrete.diffuse_color = (.32, .34, .33, 1)
concrete.use_nodes = True
concrete.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = concrete.diffuse_color
concrete.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .9


def solid(name, vertices, faces, bevel=.04):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([(x, -z, y) for x, y, z in vertices], [], faces)
    mesh.update()
    editable = bmesh.new()
    editable.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(editable, faces=list(editable.faces))
    editable.to_mesh(mesh)
    editable.free()
    obj = bpy.data.objects.new(name + '-col', mesh)
    bpy.context.collection.objects.link(obj)
    mesh.materials.append(concrete)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    # Retain editable modifiers in .blend; export evaluates the bevels.
    if bevel:
        modifier = obj.modifiers.new('Cast edge chamfer', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 2
    return obj


def box(name, center, size, bevel=.04):
    x, y, z = center
    a, b, c = [v / 2 for v in size]
    vertices = [(x+sx*a, y+sy*b, z+sz*c) for sx, sy, sz in
                [(-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                 (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1)]]
    return solid(name, vertices, [(0,1,2,3),(4,7,6,5),(0,4,5,1),
                                (3,2,6,7),(0,3,7,4),(1,5,6,2)], bevel)


# The approved four-pier envelope: all ground-level geometry stays outside
# both intersecting 18 m corridors. Foundations penetrate the flat junction.
for sx in [-1, 1]:
    for sz in [-1, 1]:
        tag = f'{"West" if sx < 0 else "East"}{"North" if sz < 0 else "South"}'
        box(tag + 'Foundation', (sx*10, -.4, sz*10), (2, .8, 2))
        box(tag + 'Pier', (sx*10, 2.65, sz*10), (2, 5.7, 2), .08)
        # Capitals overlap the deck, without lowering the specified soffit.
        box(tag + 'Capital', (sx*10, 5.75, sz*10), (2.5, .5, 2.5), .06)
        # Low tapered retaining returns occupy only the unused corner quadrant.
        # Each wall toes into the unchanged flat terrain by 25 cm.
        verts = [(sx*x, y, sz*z) for x,y,z in
                 [(10.5,-.25,10.5),(11.2,-.25,10.5),(15.5,-.25,14.8),(14.8,-.25,15.5),
                  (10.5,2.4,10.5),(11.2,2.4,10.5),(15.5,.35,14.8),(14.8,.35,15.5)]]
        faces = [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
        # The base ordering above points inward; correct parity explicitly.
        if sx * sz > 0:
            faces = [tuple(reversed(f)) for f in faces]
        solid(tag + 'RetainingWing', verts, faces)
        # Open U-channel behind each pier: floor and two lips, no route trench.
        box(tag + 'DrainInvert', (sx*12.1, .01, sz*10), (.9, .12, 2.8), .02)
        for edge in [-1, 1]:
            box(tag + f'DrainLip{edge}', (sx*12.1+edge*.4, .13, sz*10), (.1, .3, 2.8), .015)

# Segmented cast slab. Joints are visual reveals above a continuous soffit;
# there are no collision cracks or floor changes in the crossing beneath.
box('TunnelSoffit', (0, 5.7, 0), (22.5, .4, 22.5), .025)
for index in range(9):
    panel = box(f'DeckPanel{index:02}', (-10 + index*2.5, 6.12, 0), (2.48, .46, 22.5), .025)
    panel.name = f'DeckPanel{index:02}'
# One authored road collider bridges the visual expansion joints. Individual
# panels stay visible/editable, without overlapping wheel support colliders.
road = box('DeckRoad', (0, 6.125, 0), (22.5, .45, 22.5), 0)
road.name = 'DeckRoad-colonly'
for side in [-1, 1]:
    box(f'NorthSouthEdgeBeam{side}', (side*10.5, 5.98, 0), (1.5, .95, 22.5), .06)
    box(f'EastWestEdgeBeam{side}', (0, 5.98, side*10.5), (22.5, .95, 1.5), .06)
    # Retain north/south edge barriers; open the two jump-facing (X) ends.
    for segment in range(5):
        center = -8.8 + segment*4.4
        box(f'EastWestParapet{side}_{segment}', (center, 6.88, side*10.95), (4.38, 1.15, .55), .06)

# Source remains individually editable, including construction names/modifiers.
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'source/InfieldStructures.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT / 'infield_structures.glb'),
                          export_format='GLB', export_yup=True,
                          export_animations=False, export_extras=True, export_apply=True)
manifest = dict(provenance='Original Trackstorm Blender-authored structural set; no acquired assets.',
                units='metres', reference='TS-74 layout and approved TS-76 dirt tabletop connections.',
                minimum_opening_width_m=18, minimum_soffit_height_m=5.5,
                ground_routes='North/south passage retained; east/west crosses the open deck via dirt tabletops.',
                files={p: hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in
                       ['source/InfieldStructures.blend', 'infield_structures.glb', 'BuildStructures.py', 'layout.json']})
(ROOT/'structures-sources.json').write_text(json.dumps(manifest, indent=2)+'\n', newline='\n')
print('Production structure exported: ' + str(len(bpy.context.scene.objects)) + ' editable parts.')
