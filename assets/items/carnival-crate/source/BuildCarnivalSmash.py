"""Original bevelled plank, foil and carnival-token masters for the pickup smash.

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


def mesh(name, vertices, faces, painted=(), bevel=0):
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
            # Match the intact crate's lengthwise grain; use endgrain on broken sides.
            uv.data[loop].uv = (vertex.x / .28 + .5, vertex.z + .5)
            colors.data[loop].color = (1 if poly.index in painted else 0, 1, 1, 1)
    if bevel:
        bpy.context.view_layer.objects.active = obj
        modifier = obj.modifiers.new('Worn edge depth', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 2
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        # Flat face normals plus rounded bevel faces retain clear, physical thickness.
        weighted = obj.modifiers.new('Plank face normals', 'WEIGHTED_NORMAL')
        weighted.keep_sharp = True
        bpy.ops.object.modifier_apply(modifier=weighted.name)
    return obj


for variant in range(3):
    rng = random.Random(311 + variant)
    # Long parallel sides, one torn end and asymmetric split fibres: actual planks,
    # not a repeated sawtooth sheet. Model thickness rather than a two-sided card.
    profiles = [
        [(-.14,-.5),(.14,-.5),(.14,.32),(.07,.47),(.02,.39),(-.02,.52),(-.055,.36),(-.14,.44)],
        [(-.12,-.48),(.12,-.42),(.12,.49),(.065,.40),(.025,.54),(-.02,.35),(-.12,.40)],
        [(-.14,-.45),(-.06,-.51),(.015,-.38),(.14,-.47),(.14,.5),(-.14,.5)],
    ]
    outline = [(x + rng.uniform(-.005,.005), z + rng.uniform(-.015,.015)) for x,z in profiles[variant]]
    count = len(outline)
    vertices = [(x, y, z) for y in [-.045, .045] for x, z in outline]
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces += [(i, (i + 1) % count, (i + 1) % count + count, i + count) for i in range(count)]
    mesh('WoodShard' + str(variant), vertices, faces, (0, 1), bevel=.009)

# A thin raised, faceted five-point circus star, visible from both sides.
rim = [(sin(i*pi/5) * (.5 if i % 2 == 0 else .22), 0,
        cos(i*pi/5) * (.5 if i % 2 == 0 else .22)) for i in range(10)]
mesh('GoldStar', rim + [(0, -.085, 0), (0, .085, 0)],
     [(10, (i+1) % 10, i) for i in range(10)] + [(11, i, (i+1) % 10) for i in range(10)])

# Coins have a substantial milled rim and a raised star on both faces.
segments = 24
coin_vertices = []
coin_faces = []
for radius, depth in [(.44,-.055),(.5,-.04),(.5,.04),(.44,.055)]:
    coin_vertices += [(sin(i*2*pi/segments)*radius,depth,cos(i*2*pi/segments)*radius) for i in range(segments)]
for ring in range(3):
    coin_faces += [(ring*segments+i,ring*segments+(i+1)%segments,(ring+1)*segments+(i+1)%segments,(ring+1)*segments+i) for i in range(segments)]
coin_faces += [tuple(reversed(range(segments))),tuple(range(3*segments,4*segments))]
for side in [-1,1]:
    start = len(coin_vertices)
    coin_vertices += [(sin(i*pi/5)*(.29 if i%2==0 else .13),side*.058,cos(i*pi/5)*(.29 if i%2==0 else .13)) for i in range(10)]
    coin_vertices.append((0,side*.09,0))
    coin_faces += [(start+10,start+i,start+(i+1)%10) if side==1 else (start+10,start+(i+1)%10,start+i) for i in range(10)]
mesh('GoldCoin',coin_vertices,coin_faces)

# A recognisable solid zigzag, with bevels that catch light while tumbling.
bolt = [(-.02,.52),(.3,.52),(.05,.1),(.25,.1),(-.28,-.53),(-.07,-.06),(-.28,-.06)]
n = len(bolt)
mesh('GoldBolt',[(x,y,z) for y in [-.045,.045] for x,z in bolt],
     [tuple(reversed(range(n))),tuple(range(n,n*2))] + [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)],bevel=.012)

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
    'materials': 'Runtime reuses intact crimson, ivory and exposed-endgrain maps on bevelled planks. Original thick stars, rimmed embossed coins and bevelled lightning bolts use runtime metallic gold.',
    'files': {name: hashlib.sha256((ROOT/name).read_bytes()).hexdigest() for name in files}
}, indent=2) + '\n')
