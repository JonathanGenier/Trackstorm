"""Original low-poly splinter/foil/star masters for the cosmetic pickup smash.

Blender metres/Z-up; the runtime reuses the intact crate's procedural wood maps.
Painted faces carry red vertex mask; freshly torn edges carry zero. No image pixels
are taken from the approved reference. Run with Blender --background --python.
"""
from pathlib import Path
from math import sin, cos, pi
import bpy
import random
import json
import hashlib

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.context.scene.unit_settings.system = 'METRIC'


def mesh(name, vertices, faces, painted=()):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    colors = data.color_attributes.new(name='PaintMask', type='FLOAT_COLOR', domain='CORNER')
    uv = data.uv_layers.new(name='WoodUV')
    for poly in data.polygons:
        for loop in poly.loop_indices:
            vertex = data.vertices[data.loops[loop].vertex_index].co
            uv.data[loop].uv = (vertex.x + .5, vertex.z + .5)
            colors.data[loop].color = (1 if poly.index in painted else 0, 1, 1, 1)
    return obj


for variant in range(3):
    rng = random.Random(311 + variant)
    # Irregular long fibres at both ends, with a solid thin board profile.
    outline = [(-.48, -.48), (-.26, -.31), (-.17, -.53), (.02, -.35), (.23, -.5),
               (.47, -.37), (.43, .47), (.18, .31), (.07, .52), (-.14, .35), (-.45, .48)]
    outline = [(x + rng.uniform(-.035, .035), z + rng.uniform(-.035, .035)) for x, z in outline]
    count = len(outline)
    vertices = [(x, y, z) for y in [-.055, .055] for x, z in outline]
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces += [(i, (i + 1) % count, (i + 1) % count + count, i + count) for i in range(count)]
    mesh('WoodShard' + str(variant), vertices, faces, (0, 1))

# A thin raised, faceted five-point circus star, visible from both sides.
rim = [(sin(i*pi/5) * (.5 if i % 2 == 0 else .22), 0,
        cos(i*pi/5) * (.5 if i % 2 == 0 else .22)) for i in range(10)]
mesh('GoldStar', rim + [(0, -.085, 0), (0, .085, 0)],
     [(10, (i+1) % 10, i) for i in range(10)] + [(11, i, (i+1) % 10) for i in range(10)])

# Folded foil catches moving highlights; neither a flat particle nor a rigid body.
vertices = []
for i in range(7):
    t = i / 6
    for x in [-.5, .5]:
        vertices.append((x, sin(t*pi*2) * .16, t - .5))
mesh('GoldFoil', vertices, [(i*2, i*2+1, i*2+3, i*2+2) for i in range(6)])
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/CarnivalSmash.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT/'CarnivalSmash.glb'), export_format='GLB',
    export_apply=True, export_cameras=False, export_lights=False, export_all_vertex_colors=True)
files = ['source/BuildCarnivalSmash.py', 'source/CarnivalSmash.blend', 'CarnivalSmash.glb', 'source/ApprovedSmash.png']
(ROOT/'smash-sources.json').write_text(json.dumps({
    'author': 'Original Trackstorm project artwork', 'tool': bpy.app.version_string,
    'reference': 'User-approved 3 crates explosion.png, local TS-309/TS-310 reference for golden_confetti_circus_crate_explosion.png. Reference pixels are not used at runtime.',
    'materials': 'Runtime reuses the original intact crimson, ivory and exposed-endgrain maps.',
    'files': {name: hashlib.sha256((ROOT/name).read_bytes()).hexdigest() for name in files}
}, indent=2) + '\n')
