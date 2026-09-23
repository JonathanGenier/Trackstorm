"""Blender-owned, metre-scale topology reservations; no final terrain or handling.

Run Blender --background --python-exit-code 1 --python this_file.
Coordinates below are Godot X/Z metres selected within the measured oval, not
measurements of Jira attachment 10017. Bottom of that image is Godot +Z.
"""
import hashlib
import json
import math
from pathlib import Path

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0


def material(name, color):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = 1
    return m


dirt = material('Graybox dirt route', (.34, .24, .15))
shoulder = material('Future berm and dirt grass blend', (.27, .29, .20))
water = material('Water footprint only', (.16, .30, .36))
takeoff = material('Takeoff reservation', (.60, .42, .20))
landing = material('Dirt landing reservation', (.45, .32, .20))
solid = material('Tunnel clearance graybox', (.40, .42, .40))
obstacle = material('Obstacle area reservation', (.35, .34, .32))


def mesh(name, points, faces, mat):
    data = bpy.data.meshes.new(name)
    # Author directly in metres; export converts Blender Z-up to Godot Y-up.
    data.from_pydata([(x, -z, y) for x, y, z in points], [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    obj.data.materials.append(mat)
    return obj


def ribbon(name, points, width, height, mat):
    vertices = []
    for i, p in enumerate(points):
        a, b = Vector(points[max(0, i-1)]), Vector(points[min(len(points)-1, i+1)])
        tangent = (b-a).normalized()
        side = Vector((-tangent.y, tangent.x)) * width/2
        for q in [Vector(p)+side, Vector(p)-side]:
            vertices.append((q.x, height, q.y))
    return mesh(name, vertices, [(2*i+2, 2*i+3, 2*i+1, 2*i) for i in range(len(points)-1)], mat)


def curve(control):
    # Catmull-Rom interpolation gives broad continuous direction changes.
    out = []
    cp = [control[0]] + control + [control[-1]]
    for i in range(1, len(cp)-2):
        a, b, c, d = [Vector(v) for v in cp[i-1:i+3]]
        count = max(2, math.ceil((c-b).length/2))
        for j in range(count):
            t = j/count
            p = .5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)
            out.append([round(p.x, 4), round(p.y, 4)])
    return out + [control[-1]]


def ellipse(name, x, z, rx, rz, mat, height=.045):
    p = [(x, height, z)] + [(x+rx*math.cos(i*math.tau/48), height, z+rz*math.sin(i*math.tau/48)) for i in range(48)]
    return mesh(name, p, [(0, (i+1)%48+1, i+1) for i in range(48)], mat)


routes = []


def route(name, controls, width=12):
    points = curve(controls)
    ribbon(name+'_BermReserve', points, width+8, .008, shoulder)
    ribbon(name, points, width, .016+len(routes)*.0005, dirt)
    routes.append(dict(id=name, width_m=width, shoulder_each_side_m=4, points=points))


# Concept relationships: central junction/tunnel, paired looping side networks,
# north/south entries, lateral shortcuts and separated water/obstacle islands.
route('WestLoop', [[-18,0],[-60,-8],[-106,-22],[-146,-36],[-164,-14],[-151,20],[-115,46],[-65,48],[-24,30],[-18,0]])
route('EastLoop', [[18,0],[60,-8],[106,-22],[146,-36],[164,-14],[151,20],[115,46],[65,48],[24,30],[18,0]])
route('NorthLink', [[-146,-36],[-122,-54],[-72,-55],[-30,-47],[0,-38],[30,-47],[72,-55],[122,-54],[146,-36]])
route('WestShortcut', [[-151,20],[-112,8],[-74,0],[-38,0],[0,0]], 14)
route('EastShortcut', [[0,0],[38,0],[74,0],[112,8],[151,20]], 14)
route('NorthSouth', [[0,-82],[0,-55],[0,-28],[0,0],[0,28],[0,55],[0,82]], 16)
route('SouthWestEntry', [[-90,82],[-90,66],[-65,48]], 16)
route('SouthEastEntry', [[90,82],[90,66],[65,48]], 16)
route('NorthWestEntry', [[-90,-82],[-90,-67],[-72,-55]], 16)
route('NorthEastEntry', [[90,-82],[90,-67],[72,-55]], 16)

ellipse('CentralOpenJunction', 0, 0, 28, 22, dirt, .028)
ellipse('WestOpenArea', -125, 12, 22, 17, dirt, .028)
ellipse('EastOpenArea', 125, 12, 22, 17, dirt, .028)

jumps = []
for name, x, z, sign in [('WestJump', -125, 0, 1), ('EastJump', 125, 0, -1)]:
    # A continuous 100 m lane through the lateral shortcut; no raised ramp.
    segments = [('Approach', 0, 30, dirt), ('Kicker',30,42,takeoff), ('Flight',42,55,dirt), ('Landing',55,77,landing), ('Recovery',77,100,dirt)]
    for stage, a, b, mat in segments:
        ribbon(name+'_'+stage, [[x+sign*a,z],[x+sign*b,z]], 12, .04, mat)
    jumps.append(dict(id=name, start=[x,z], direction=[sign,0], width_m=12, approach_m=30, kicker_m=12, flight_m=13, landing_m=22, recovery_m=23))

for name,x,z,rx,rz in [('NorthWestWater',-57,-29,17,10),('NorthEastWater',77,-35,16,8),('SouthWestWater',-85,28,15,9),('SouthEastWater',85,28,15,9)]:
    ellipse(name,x,z,rx,rz,water)
for name,x,z in [('WestObstacleArea',-130,-15),('EastObstacleArea',130,-15),('SouthObstacleArea',-35,65)]:
    ellipse(name,x,z,9,6,obstacle)
ribbon('WestRhythmReserve', [[-115,46],[-90,49],[-65,48]], 16, .032, landing)
ribbon('EastRhythmReserve', [[65,48],[90,49],[115,46]], 16, .032, landing)


def box(name, x,y,z, sx,sy,sz):
    bpy.ops.mesh.primitive_cube_add(size=1, location=(x,-z,y))
    obj=bpy.context.object
    obj.name=name+'-col'
    obj.scale=(sx,sz,sy)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.data.materials.append(solid)


# Open-sided tunnel envelope keeps both intersecting routes driveable now.
# TS-75 will shape the earth bridge and its approaches; this is clearance only.
for x in [-10,10]:
    for z in [-10,10]:
        box('TunnelPier',x,2.75,z,2,5.5,2)
box('TunnelRoof',0,5.75,0,22,.5,22)

# Reserve a 20 m broad interface strip along both straights; no new seam collider.
for z in [-72,72]:
    ribbon('BankTransitionReserve'+str(z),[[-107,z],[107,z]],20,.004,shoulder)

ROOT.joinpath('source').mkdir(exist_ok=True)
ROOT.joinpath('source/.gdignore').write_text('')
# Flatten the diagram into one original palette texture. Coplanar route ribbons
# are editable authoring guides, not stacked runtime surfaces that can z-fight.
size_x, size_z = 2048, 1024
extent_x, extent_z = 420, 200
pixels = [0.12, 0.16, 0.085, 1.0] * (size_x*size_z)
guides = [o for o in bpy.context.scene.objects if o.type == 'MESH' and not o.name.endswith('-col')]
for obj in sorted(guides, key=lambda o: o.data.vertices[0].co.z):
    color = list(obj.data.materials[0].diffuse_color)
    for face in obj.data.polygons:
        coords = [obj.data.vertices[i].co for i in face.vertices]
        polygon = [((p.x/extent_x+.5)*size_x, (p.y/extent_z+.5)*size_z) for p in coords]
        low = max(0, math.ceil(min(p[1] for p in polygon)-.5))
        high = min(size_z, math.ceil(max(p[1] for p in polygon)-.5))
        for row in range(low, high):
            y = row+.5
            cuts = []
            for a,b in zip(polygon, polygon[1:]+polygon[:1]):
                if min(a[1],b[1]) <= y < max(a[1],b[1]):
                    cuts.append(a[0]+(y-a[1])*(b[0]-a[0])/(b[1]-a[1]))
            cuts.sort()
            for start,end in zip(cuts[::2],cuts[1::2]):
                left, right = max(0,math.ceil(start-.5)), min(size_x,math.ceil(end-.5))
                if right > left:
                    offset = (row*size_x+left)*4
                    pixels[offset:offset+(right-left)*4] = color*(right-left)
image = bpy.data.images.new('Graybox topology palette', width=size_x, height=size_z)
image.pixels.foreach_set(pixels)
image.filepath_raw = str(ROOT/'layout_palette.png')
image.file_format = 'PNG'
image.save()
floor_material = material('Graybox topology diagram', (1,1,1))
texture = floor_material.node_tree.nodes.new('ShaderNodeTexImage')
texture.image = image
floor_material.node_tree.links.new(texture.outputs['Color'], floor_material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
sections=json.loads((ROOT.parent/'oval/measurements.json').read_text())['sections_godot']
rim=[s[0] for s in sections]
floor=mesh('LayoutFloor',[(0,.05,0)]+[(p[0],.05,p[2]) for p in rim],[(0,i+1,(i+1)%len(rim)+1) for i in range(len(rim))],floor_material)
uv=floor.data.uv_layers.new(name='Diagram')
for polygon in floor.data.polygons:
    for loop in polygon.loop_indices:
        p=floor.data.vertices[floor.data.loops[loop].vertex_index].co
        uv.data[loop].uv=(p.x/extent_x+.5,p.y/extent_z+.5)
for obj in guides:
    obj.hide_render=True
    obj.hide_set(True)
bpy.ops.object.select_all(action='DESELECT')
for obj in bpy.context.scene.objects:
    if obj == floor or obj.name.endswith('-col'):
        obj.select_set(True)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/InfieldGraybox.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT/'infield_graybox.glb'),use_selection=True,export_format='GLB',export_yup=True,export_animations=False,export_extras=True)
manifest = dict(units='metres',concept='Jira TS-74 attachment 10017, layout direction only',routes=routes,jumps=jumps,tunnel_clear_width_m=18,tunnel_clear_height_m=5.5,bank_transition_depth_m=20)
ROOT.joinpath('layout.json').write_text(json.dumps(manifest,indent=2)+'\n', newline='\n')
ROOT.joinpath('sources.json').write_text(json.dumps(dict(provenance='Original Trackstorm Blender-authored graybox; no third-party assets acquired.',reference=manifest['concept'],files={p:hashlib.sha256(ROOT.joinpath(p).read_bytes()).hexdigest() for p in ['source/InfieldGraybox.blend','infield_graybox.glb','layout.json','layout_palette.png']}),indent=2)+'\n', newline='\n')
print('Infield Blender source, GLB and metre-scale route manifest exported.')
