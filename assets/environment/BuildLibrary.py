"""Original modular infield kit. Run with Blender --background --python-exit-code 1 --python.

Rebuild overwrites generated exports and EnvironmentLibrary.blend. Preserve manual edits first.
Blender Z-up metres, ground-centred pivots; glTF converts to Godot Y-up.
"""
import hashlib
import json
import math
import random
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector
from mathutils.noise import noise as coherent_noise
import numpy as np

ROOT = Path(__file__).resolve().parent
ROOT.joinpath('source').mkdir(exist_ok=True)
ROOT.joinpath('models').mkdir(exist_ok=True)
ROOT.joinpath('source/.gdignore').touch()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1
bpy.context.preferences.filepaths.save_version = 0
rng = random.Random(80)
materials = {}
for name, color, metal in [
    ('Rock', (.28, .27, .23), 0), ('Concrete', (.36, .37, .34), 0),
    ('Steel', (.26, .29, .29), .75), ('DarkSteel', (.075, .085, .085), .65),
    ('Bark', (.16, .105, .064), 0), ('Leaf', (.115, .16, .055), 0),
    ('DryGrass', (.29, .27, .105), 0), ('Lens', (.67, .72, .66), .2)]:
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = (*color, 1)
    bsdf = mat.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = .88 if metal == 0 else .48
    bsdf.inputs['Metallic'].default_value = metal
    mat.use_backface_culling = False
    materials[name] = mat


def stone_textures():
    """Original periodic mineral noise; no directional bands or external imagery."""
    texture_dir = ROOT/'textures'
    texture_dir.mkdir(exist_ok=True)
    generator = np.random.default_rng(80)
    n = 512
    fy,fx = np.meshgrid(np.fft.fftfreq(n),np.fft.fftfreq(n),indexing='ij')
    frequency = np.sqrt(fx*fx+fy*fy)
    def layer(scale):
        spectrum=np.fft.fft2(generator.normal(size=(n,n)))
        field=np.fft.ifft2(spectrum*np.exp(-(frequency*scale)**2)).real
        return field/max(field.std(),.001)
    broad=layer(90)
    grain=layer(6)
    pits=layer(2)
    height=.65*layer(23)+.2*grain+.06*pits
    tone=np.clip(.36+.035*broad+.024*grain+.012*pits,.20,.52)
    flecks=np.maximum(grain-1.3,0)*.035
    colors=np.stack([tone*1.03+flecks,tone+flecks,tone*.94+flecks,np.ones_like(tone)],axis=-1)
    dx=(np.roll(height,-1,axis=1)-np.roll(height,1,axis=1))*.55
    dy=(np.roll(height,-1,axis=0)-np.roll(height,1,axis=0))*.55
    normal=np.stack([-dx,-dy,np.ones_like(dx)],axis=-1)
    normal/=np.linalg.norm(normal,axis=-1,keepdims=True)
    normals=np.concatenate([normal*.5+.5,np.ones((n,n,1))],axis=-1)
    for name,pixels in [('StoneAlbedo',colors),('StoneNormal',normals)]:
        image=bpy.data.images.new(name,width=n,height=n,alpha=True)
        image.colorspace_settings.name='Non-Color' if name.endswith('Normal') else 'sRGB'
        image.pixels.foreach_set(pixels.astype(np.float32).ravel())
        image.filepath_raw=str(texture_dir/(name+'.png'))
        image.file_format='PNG'
        image.save()
        image.pack()
    # Editable Blender material previews use the same original maps as Godot.
    mat=materials['Rock']
    nodes=mat.node_tree.nodes
    links=mat.node_tree.links
    coordinate=nodes.new('ShaderNodeTexCoord')
    for name in ['StoneAlbedo','StoneNormal']:
        texture=nodes.new('ShaderNodeTexImage')
        texture.image=bpy.data.images[name]
        texture.projection='BOX'
        texture.projection_blend=.2
        links.new(coordinate.outputs['Object'],texture.inputs['Vector'])
        if name.endswith('Albedo'):
            links.new(texture.outputs['Color'],nodes['Principled BSDF'].inputs['Base Color'])
        else:
            normal_node=nodes.new('ShaderNodeNormalMap')
            normal_node.inputs['Strength'].default_value=.45
            links.new(texture.outputs['Color'],normal_node.inputs['Color'])
            links.new(normal_node.outputs['Normal'],nodes['Principled BSDF'].inputs['Normal'])


stone_textures()


def activate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def finish(obj, name, material):
    obj.name = name
    activate(obj)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.data.materials.clear()
    obj.data.materials.append(materials[material])
    return obj


def box(name, center, size, material='Concrete', bevel=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    obj = bpy.context.object
    obj.scale = size
    finish(obj, name, material)
    if bevel:
        mod = obj.modifiers.new('Cast edge', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def rod(name, start, end, radius, material='Steel', top=None, sides=10):
    axis = Vector(end) - Vector(start)
    bpy.ops.mesh.primitive_cone_add(vertices=sides, radius1=radius,
                                  radius2=radius if top is None else top,
                                  depth=axis.length, location=(Vector(start)+Vector(end))/2)
    obj = bpy.context.object
    obj.rotation_euler = axis.to_track_quat('Z', 'Y').to_euler()
    return finish(obj, name, material)


def rock(name, center, size, seed, subdivisions=4):
    noise = random.Random(seed)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=subdivisions, radius=1)
    obj = bpy.context.object
    planes = [(Vector((noise.uniform(-1,1), noise.uniform(-1,1), noise.uniform(-1,1))).normalized(), noise.uniform(.63,.92)) for _ in range(13)]
    for v in obj.data.vertices:
        direction = v.co.normalized()
        radius = min([1.0]+[distance/direction.dot(normal) for normal,distance in planes if direction.dot(normal) > .01])
        p = direction*radius
        offset = Vector((seed*.73, seed*.37, seed*.19))
        p += direction*(coherent_noise(p*3.7+offset)*.09 + coherent_noise(p*15+offset)*.018)
        x,y,z = p
        v.co = (center[0]+(x+.12*z)*size[0]*.58,
                center[1]+(y-.09*x)*size[1]*.58,
                center[2]+max(0,(z+.84)*.58)*size[2])
    finish(obj, name, 'Rock')
    mod = obj.modifiers.new('Fracture planes', 'DECIMATE')
    mod.ratio = .82
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bottom = min(v.co.z for v in obj.data.vertices)
    for v in obj.data.vertices:
        v.co.z -= bottom-center[2]
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def mesh(name, vertices, faces, material):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    return finish(obj, name, material)


def hull(obj, name):
    data = obj.data.copy()
    bm = bmesh.new()
    for vertex in data.vertices:
        bm.verts.new(vertex.co)
    bmesh.ops.convex_hull(bm, input=list(bm.verts), use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    clone = bpy.data.objects.new(name+'-convcolonly', data)
    bpy.context.collection.objects.link(clone)
    return clone


def unwrap(obj):
    activate(obj)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.025)
    bpy.ops.object.mode_set(mode='OBJECT')


catalog = []


def asset(name, parts, collision, purpose, snap=0):
    # One draw surface per material, reusable scene per asset. Editable mesh components
    # remain separate in the source collection; only the export copy is joined.
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    parent = bpy.data.objects.new(name, None)
    collection.objects.link(parent)
    collection.asset_mark()
    collection.asset_data.description = purpose
    for obj in parts+collision:
        for old in list(obj.users_collection):
            old.objects.unlink(obj)
        collection.objects.link(obj)
        obj.parent = parent
        if obj in parts:
            unwrap(obj)
    copies = []
    for obj in parts:
        copy = obj.copy()
        copy.data = obj.data.copy()
        copy.parent = None
        bpy.context.collection.objects.link(copy)
        copies.append(copy)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in copies:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    bpy.ops.object.join()
    visual = bpy.context.object
    visual.name = name+'Visual'
    visual.parent = parent
    # Aperiodic broad variation; no directional sine bands across unrelated assets.
    colors = visual.data.color_attributes.new(name='Color', type='FLOAT_COLOR', domain='POINT')
    for vertex, color in zip(visual.data.vertices, colors.data):
        z = vertex.co.z
        p = vertex.co
        shade = .84 + .18*coherent_noise(p*2.1+Vector((12,7,31)))
        color.color = (shade, shade*(.98+.02*min(z,1)), shade*.94, 1)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [parent, visual]+collision:
        obj.select_set(True)
    path = 'models/'+name+'.glb'
    bpy.ops.export_scene.gltf(filepath=str(ROOT/path), export_format='GLB',
                              use_selection=True, export_yup=True, export_animations=False,
                              export_apply=True, export_extras=True, export_vertex_color='ACTIVE')
    visual.data.calc_loop_triangles()
    bounds = [[round(min(v.co[i] for v in visual.data.vertices), 4),
               round(max(v.co[i] for v in visual.data.vertices), 4)] for i in range(3)]
    catalog.append(dict(id=name, model=path, purpose=purpose, snap_m=snap,
                        blender_bounds=bounds, triangles=len(visual.data.loop_triangles),
                        collision_parts=len(collision), collision='convex pieces' if collision else 'none',
                        lod='Godot import-generated screen-space LOD; collision remains full source',
                        materials=[m.name for m in visual.data.materials]))
    bpy.data.objects.remove(visual, do_unlink=True)
    for obj in collision:
        obj.hide_set(True)
        obj.hide_render = True
    # Separate collections at the origin are intentional: toggle a collection to edit
    # its asset without a catalogue offset contaminating the export pivot.
    parent['purpose'] = purpose
    parent['snap_m'] = snap


for index, dimensions in enumerate([(2.8, 2.2, 1.7), (1.6, 1.2, .8), (3.8, 2.0, 1.0)]):
    name = ['BoulderTall', 'BoulderLow', 'RockSlab'][index]
    obj = rock(name+'Stone', (0, 0, 0), dimensions, 80+index)
    asset(name, [obj], [hull(obj, name+'Collision')], 'Infield rock islands and shoulder outcrops')

parts = [rock('ClusterStone'+str(i), center, dims, 90+i) for i, (center, dims) in enumerate([
    ((-.9, .2, 0), (2.4, 2, 1.7)), ((.9, 0, 0), (2.1, 1.7, 1.2)),
    ((0, -.7, 0), (1.4, 1.3, .7))])]
asset('RockCluster', parts, [hull(p, 'ClusterCollision'+str(i)) for i, p in enumerate(parts)],
      'Reusable three-rock cluster for reserved obstacle islands')
parts = [rock('Stratum'+str(i), center, dims, 100+i) for i, (center, dims) in enumerate([
    ((-1.25, .15, 0), (2.8, 2.3, 2.6)), ((.25, .35, 0), (2.6, 2.5, 3.3)),
    ((1.55, .15, 0), (2.3, 2.0, 2.4)), ((-.2, -.7, 0), (3.4, 1.6, 1.0))])]
asset('RockLedge', parts, [hull(p, 'LedgeCollision'+str(i)) for i, p in enumerate(parts)],
      'Short fractured rock face embedded in infield slopes; overlap irregular ends')

for variant in range(2):
    parts = []
    verts, faces = [], []
    height = [.85, 1.35][variant]
    for limb in range(11):
        angle = rng.random()*math.tau
        spread = rng.uniform(.3,.7)
        base = Vector((rng.uniform(-.16,.16),rng.uniform(-.16,.16),0))
        tip = Vector((math.cos(angle)*spread, math.sin(angle)*spread, rng.uniform(.55,1)*height))
        middle = base.lerp(tip,.48)+Vector((-.08*math.sin(angle),.08*math.cos(angle),.12))
        parts.append(rod('StemBase',base,middle,.018,'Bark',.011,5))
        parts.append(rod('StemTip',middle,tip,.011,'Bark',.003,5))
        for twig in range(5):
            root = middle.lerp(tip,twig/5)
            a = angle+rng.uniform(-1.8,1.8)
            end = root+Vector((math.cos(a)*rng.uniform(.12,.28),math.sin(a)*rng.uniform(.12,.28),rng.uniform(.04,.19)))
            parts.append(rod('Twig',root,end,.004,'Bark',.001,4))
            for leaf in range(18):
                center = root.lerp(end,(leaf+.5)/18)
                azimuth = a+(-1 if leaf%2 else 1)*rng.uniform(.7,1.7)
                length = rng.uniform(.06,.12)
                direction = Vector((math.cos(azimuth),math.sin(azimuth),rng.uniform(-.3,.8))).normalized()
                side = direction.cross(Vector((.15,.1,1))).normalized()*length*.38
                start = center
                mid = center+direction*length*.55
                tip_leaf = center+direction*length+Vector((0,0,-length*.2))
                k=len(verts)
                verts.extend([start,mid+side,mid+Vector((0,0,length*.11)),mid-side,tip_leaf])
                faces.extend([(k,k+1,k+2),(k,k+2,k+3),(k+1,k+4,k+2),(k+2,k+4,k+3)])
    parts.append(mesh('Leaves', verts, faces, 'Leaf'))
    asset(['ScrubLow','ScrubTall'][variant], parts, [], 'Low scrub on non-driving dirt/grass shoulders')

for variant in range(2):
    verts, faces = [], []
    tufts = [(rng.uniform(-.32,.32),rng.uniform(-.32,.32),rng.uniform(.65,1.15)) for _ in range(7)]
    for blade in range([170, 210][variant]):
        a = rng.random()*math.tau
        tx,ty,scale = tufts[blade%len(tufts)]
        radius = rng.random()*.11
        x,y = tx+math.cos(a)*radius, ty+math.sin(a)*radius
        h = rng.uniform(.18,.62)*scale*[1,1.25][variant]
        width = rng.uniform(.012,.026)
        lean = rng.uniform(.12,.48)
        azimuth = a+rng.uniform(-.8,.8)
        k = len(verts)
        for step in range(6):
            t=step/5
            center=Vector((x+math.cos(a)*lean*t*t,y+math.sin(a)*lean*t*t,h*(t-.22*t*t*t)))
            side=Vector((-math.sin(azimuth+t*.7),math.cos(azimuth+t*.7),.08))*width*(1-t)*.5
            verts.extend([center-side,center+side])
            if step:
                j=k+step*2
                faces.extend([(j-2,j-1,j),(j-1,j+1,j)])
    asset(['GrassClump','DryGrassClump'][variant], [mesh('Blades',verts,faces,['Leaf','DryGrass'][variant])],
          [], 'Small opaque two-sided grass tufts for terrain edges; no wheel obstruction')

# Existing project-authored conifers become individually reusable, at their original size.
with bpy.data.libraries.load(str(ROOT.parent/'maps/oval/source/OvalEnvironment.blend'), link=False) as (source, target):
    target.objects = ['Conifer0','Conifer1','Conifer2']
for obj, name in zip(target.objects, ['FirMature','PineOpen','SpruceYoung']):
    bpy.context.collection.objects.link(obj)
    collision = rod(name+'Trunk-convcolonly',(0,0,0),(0,0,5 if name!='SpruceYoung' else 3),.22,'Bark',.12)
    asset(name, [obj], [collision], 'Existing oval conifer silhouette, individually placeable with trunk-only collision')

# 4 m repeatable corrugated guardrail. Posts stop short of ends to avoid duplicated posts.
parts = []
for x in [-1,1]:
    parts.append(box('Post', (x,0,.65), (.12,.14,1.3),'Steel',.015))
for height in [.62,1.05]:
    verts = [(x,y,height+z) for x in [-2,2] for y,z in [(0,-.16),(-.08,-.10),(0,0),(-.08,.10),(0,.16)]]
    rail = mesh('CorrugatedRail',verts,[(i,i+1,i+6,i+5) for i in range(4)],'Steel')
    parts.append(rail)
    for x in [-1,1]:
        parts.append(rod('RailBolt',(x,-.10,height),(x,-.12,height),.025,'DarkSteel',sides=8))
asset('Guardrail4m',parts,[box('RailCollision-convcolonly',(0,0,.65),(4,.16,1.3),'Steel')],
      'Restrained modular perimeter/service barrier; snap along local X',4)

# Concrete jersey segment with cast seams and two recessed lifting sockets.
profile = [(-.36,0),(.36,0),(.36,.22),(.16,.65),(.12,1.1),(-.12,1.1),(-.16,.65),(-.36,.22)]
verts = [(x,y,z) for x in [-1.5,1.5] for y,z in profile]
faces = [tuple(reversed(range(8))),tuple(range(8,16))]+[(i,(i+1)%8,(i+1)%8+8,i+8) for i in range(8)]
body = mesh('BarrierCast',verts,faces,'Concrete')
bm=bmesh.new(); bm.from_mesh(body.data); bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(body.data); bm.free()
collision = hull(body,'BarrierCollision')
activate(body)
mod=body.modifiers.new('Cast chamfer','BEVEL'); mod.width=.025; mod.segments=2
bpy.ops.object.modifier_apply(modifier=mod.name)
parts=[body]+[box('LiftingSocket',(x,0,1.102),(.16,.09,.007),'DarkSteel') for x in [-.8,.8]]
asset('Barrier3m',parts,[collision],'Concrete modular service/perimeter protection; snap along local X',3)

parts=[box('Footing',(0,0,.15),(.7,.7,.3),'Concrete',.04),
       box('BasePlate',(0,0,.325),(.44,.44,.05),'DarkSteel',.015),
       rod('Mast',(0,0,.35),(0,0,8.8),.13,'Steel',.075,12),
       box('CrossArm',(0,0,8.55),(1.55,.12,.12),'Steel',.02)]
for x in [-.52,.52]:
    parts += [box('FloodHousing',(x,-.14,8.7),(.55,.38,.25),'DarkSteel',.045),
              box('FloodLens',(x,-.15,8.565),(.45,.27,.012),'Lens',.01)]
for x in [-.16,.16]:
    for y in [-.16,.16]:
        parts.append(rod('AnchorBolt',(x,y,.35),(x,y,.39),.027,'Steel',sides=6))
asset('LightPole9m',parts,[box('PoleFooting-convcolonly',(0,0,.16),(.7,.7,.32)),
                         rod('PoleCollision-convcolonly',(0,0,.3),(0,0,8.9),.13,'Steel',.075)],
      'Twin downward floodlight fixture for outer arena infrastructure; mesh only, no light budget implied')

parts = [box('Invert',(0,0,.075),(2,.9,.15),'Concrete',.012)]
for y in [-.40,.40]:
    parts.append(box('Lip',(0,y,.25),(2,.1,.5),'Concrete',.012))
collision = [box('DrainBase-convcolonly',(0,0,.075),(2,.9,.15))]
collision += [box('DrainLip'+str(i)+'-convcolonly',(0,y,.25),(2,.1,.5)) for i,y in enumerate([-.4,.4])]
asset('DrainChannel2m',parts,collision,'Open U-channel matching existing tunnel drains; local X flow axis, open ends',2)

bpy.ops.object.select_all(action='DESELECT')
for collection in bpy.data.collections:
    if collection.name not in ['Collection', 'BoulderTall']:
        collection.hide_viewport = True
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/EnvironmentLibrary.blend'))
manifest = dict(provenance='Original Trackstorm Blender-authored modular kit. Conifers derived from existing OvalEnvironment.blend.',
                reference='TS-74 attachment 10017 art direction and current oval/infield implementation; no reference pixels redistributed.',
                units='metres', blender=bpy.app.version_string,
                orientation='Blender +Z up, glTF Y-up; ground-centred origins; linear modules extend along X.',
                assets=catalog)
paths=['BuildLibrary.py','source/EnvironmentLibrary.blend','textures/StoneAlbedo.png','textures/StoneNormal.png']+[a['model'] for a in catalog]
manifest['files']={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in paths}
manifest['conifer_source_sha256']=hashlib.sha256((ROOT.parent/'maps/oval/source/OvalEnvironment.blend').read_bytes()).hexdigest()
(ROOT/'library.json').write_text(json.dumps(manifest,indent=2)+'\n',newline='\n')
print('Environment library exported:',len(catalog),'assets')
