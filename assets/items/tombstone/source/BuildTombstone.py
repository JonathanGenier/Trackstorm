"""Approved Carnage Circus armor. Blender metres: +Y forward, +Z up.
Original editable geometry; the linked production Car is never exported.
"""
from pathlib import Path
from math import pi, sin, cos
import hashlib
import json
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
CAR = ROOT.parents[1] / 'vehicles/source/TrackstormCar.blend'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
reference = bpy.data.collections.new('Production Car - linked fit reference')
scene.collection.children.link(reference)
with bpy.data.libraries.load(str(CAR), link=True, relative=True) as (src, dst):
    dst.objects = src.objects
for obj in dst.objects:
    if obj:
        reference.objects.link(obj)
collection = bpy.data.collections.new('Carnage Circus - export only')
scene.collection.children.link(collection)

def empty(name, pos=(0, 0, 0), parent=None):
    obj = bpy.data.objects.new(name, None)
    collection.objects.link(obj)
    obj.parent = parent
    obj.location = pos
    obj.empty_display_size = .08
    return obj

root = empty('Tombstone', (0, -3.25, .25))

def material(name, color, metallic=.3, roughness=.6, emission=0):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    shader = mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*color, 1)
    shader.inputs['Metallic'].default_value = metallic
    shader.inputs['Roughness'].default_value = roughness
    shader.inputs['Emission Color'].default_value = (*color, 1)
    shader.inputs['Emission Strength'].default_value = emission
    return mat

red = material('Circus_Crimson', (.32, .025, .032))
ivory = material('Circus_Ivory', (.77, .64, .43), .15)
steel = material('Circus_BlackenedSteel', (.055, .064, .068), .8, .45)
iron = material('Circus_WornIron', (.27, .29, .29), .8, .4)
gold = material('Circus_TarnishedBrass', (.48, .28, .075), .8, .43)
black = material('Circus_PaintedGrin', (.009, .007, .006), .05, .85)
amber = material('Circus_AmberLamp', (1, .26, .015), .15, .25, 2)
materials = [red, ivory, steel, iron, gold, black, amber]

def finish(obj, name, mat, parent, bevel=.009):
    obj.name = name
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    collection.objects.link(obj)
    obj.parent = parent
    obj.data.materials.append(mat)
    if bevel:
        mod = obj.modifiers.new('Worn forged edge', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        obj.modifiers.new('Face normals', 'WEIGHTED_NORMAL')
    return obj

def box(name, pos, size, mat, parent, bevel=.009):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, name, mat, parent, bevel)

def slab(name, outline, y, thickness, mat, parent, bevel=.006):
    # Normalize outline winding; every paint/decor slab remains closed/outward.
    outline = list(dict.fromkeys(outline))
    if sum(outline[i][0]*outline[(i+1)%len(outline)][1]-outline[(i+1)%len(outline)][0]*outline[i][1] for i in range(len(outline))) < 0:
        outline = list(reversed(outline))
    n = len(outline)
    vertices = [(x, y+d, z) for d in [-thickness/2, thickness/2] for x,z in outline]
    faces = [tuple(range(n)), tuple(range(2*n-1,n-1,-1))]
    faces += [(i,i+n,(i+1)%n+n,(i+1)%n) for i in range(n)]
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    return finish(obj, name, mat, parent, bevel)

def rod(name, a, b, radius, mat, parent, sides=10):
    a,b = Vector(a),Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=(b-a).length, location=(a+b)/2)
    obj=bpy.context.object
    obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return finish(obj,name,mat,parent,.004)

def rivet(x,z,parent,y=-.165):
    rod('Hex fastener',(x,y-.018,z),(x,y+.018,z),.031,gold,parent,6)

def star(x,z,parent,r=.12):
    shape=[(x+cos(pi/2+i*pi/5)*(r if i%2==0 else r*.45),z+sin(pi/2+i*pi/5)*(r if i%2==0 else r*.45)) for i in range(10)]
    slab('Pressed brass star',shape,-.152,.026,gold,parent,.003)

def lamp(x,z,parent,lit=True):
    box('Lamp socket',(x,0,z),(.15,.17,.045),steel,parent)
    bpy.ops.mesh.primitive_uv_sphere_add(segments=12,ring_count=6,radius=1,location=(x,0,z+.10))
    obj=bpy.context.object
    obj.scale=(.051,.053,.079)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    finish(obj,'Amber bulb' if lit else 'Broken bulb',amber if lit else black,parent,0)
    for dx in [-.065,.065]:
        rod('Lamp cage',(x+dx,0,z),(x+dx,0,z+.18),.009,iron,parent,6)
    rod('Cage cap',(x-.068,0,z+.18),(x+.068,0,z+.18),.013,iron,parent,8)

def frame(parent,left,right,top_left,top_right):
    box('Scraping shoe',((left+right)/2,-.105,-1.16),(right-left,.18,.18),iron,parent)
    for x,top in [(left,top_left),(right,top_right)]:
        box('Edge post',(x,0,(top-1.15)/2),(.085,.22,top+1.15),steel,parent)
        for z in [-1.16,-.6,0,.6,top-.06]: rivet(x,z,parent)
    rod('Brass upper trim',(left,-.126,top_left),(right,-.126,top_right),.018,gold,parent)
    for x in [left+.15,(left+right)/2,right-.15]: rivet(x,-1.16,parent,y=-.21)
    for x in [left+.22,right-.22]:
        slab('Shoe gusset',[(x-.055,-1.07),(x+.055,-1.07),(x,-.87)],-.19,.08,steel,parent)

center=empty('Center',parent=root)
slab('Central armor',[(-1.85,-1.25),(1.85,-1.25),(1.85,1.03),(0,1.25),(-1.85,1.03)],0,.20,steel,center)
slab('Crimson face',[(-1.76,-1.07),(1.76,-1.07),(1.76,.98),(0,1.18),(-1.76,.98)],-.111,.018,red,center)
# Broad hand-painted circus tent stripes, on the same formed central steel.
for x in [-1.42,-.7,0,.7,1.42]:
    top=1.16-abs(x)*.105
    slab('Ivory circus stripe',[(x-.15,-1.065),(x+.15,-1.065),(x+.11,top),(x-.11,top)],-.124,.005,ivory,center,0)
# One broad readable grin, with individually authored triangular teeth.
mouth=[(-1.37,-.14),(-1.05,-.36),(-.58,-.52),(0,-.6),(.58,-.52),(1.05,-.36),(1.37,-.14),(1.14,-.66),(.67,-.94),(0,-1.035),(-.67,-.94),(-1.14,-.66)]
slab('Painted carnival grin',mouth,-.133,.005,black,center,0)
for x in [-1.10,-.83,-.55,-.27,0,.27,.55,.83,1.10]:
    top=-.58+.32*(abs(x)/1.10)**1.5
    bottom=-.995+.29*(abs(x)/1.10)**2
    slab('Upper painted tooth',[(x-.095,top+.02),(x+.095,top+.02),(x,top-.19)],-.14,.003,ivory,center,0)
    if abs(x)<1:
        slab('Lower painted tooth',[(x-.08,bottom),(x+.08,bottom),(x,bottom+.16)],-.14,.003,ivory,center,0)
for side in [-1,1]:
    eye=[(side*.27,.20),(side*1.30,.79),(side*1.02,.21),(side*.55,.06)]
    slab('Painted slanted eye',eye,-.133,.005,black,center,0)
    slab('Ivory eye glint',[(side*.44,.22),(side*1.12,.59),(side*.93,.26)],-.14,.003,ivory,center,0)
    # Welded chevron reinforces the steel, crossing above the grin.
    a=Vector((side*1.68,-.185,.32)); b=Vector((0,-.185,-.31))
    obj=box('Chevron rib',(a+b)/2,((b-a).length,.11,.115),iron,center)
    obj.rotation_euler.y=-side*.36
    rivet(side*1.6,.28,center,y=-.255)
    rivet(side*.45,-.14,center,y=-.255)
frame(center,-1.80,1.80,1.04,1.04)
# Separate upper chevron trim follows the shallow peak, not a rounded crown.
for side in [-1,1]: rod('Peaked brass crown',(side*1.8,-.125,1.04),(0,-.125,1.24),.021,gold,center)
star(0,.88,center,.15)
for x in [-1.35,-.62,.62,1.35]: lamp(x,1.12-abs(x)*.08,center,lit=x!=.62)

wings=[]
for side,name in [(-1,'L'),(1,'R')]:
    wing=empty('Wing_'+name,(side*1.85,0,0),root)
    wings.append(wing)
    left,right=sorted([0,side*1.45])
    slab('Side armor',[(left,-1.25),(right,-1.25),(right,1.07),(left,1.07)],0,.18,steel,wing)
    box('Wing crimson face',((left+right)/2,-.101,0),(1.29,.018,2.12),red,wing)
    # Diagonal ray polygons clipped to the panel field.
    for z in [-1.05,-.30,.45]:
        outline=[(left+.09,z),(right-.09,min(z+.64,1.02)),(right-.09,min(z+.89,1.02)),(left+.09,z+.25)]
        slab('Ivory diagonal ray',outline,-.116,.005,ivory,wing,0)
    cx=(left+right)/2
    slab('Brass diamond',[(cx,-.3),(cx+.14,-.52),(cx,-.75),(cx-.14,-.52)],-.131,.009,gold,wing,0)
    slab('Black diamond',[(cx,-.35),(cx+.10,-.52),(cx,-.7),(cx-.10,-.52)],-.14,.005,black,wing,0)
    frame(wing,left+.035,right-.035,1.07,1.07)
    star(cx,.81,wing,.12)
    for x in [left+.25,right-.25]: lamp(x,1.08,wing,lit=(side<0 or x<cx))
    for z in [-.85,-.30,.25,.8]:
        rod('Hinge knuckle',(0,-.018,z-.14),(0,-.018,z+.14),.085,iron,wing,16)
        rod('Hinge brass collar',(0,-.018,z-.14),(0,-.018,z-.10),.099,gold,wing,16)
    rod('Hinge pin',(0,-.018,-1.1),(0,-.018,1.13),.034,steel,wing,10)
    # Backside ribs are visible from the chase camera / inside the shell.
    for x in [left+.23,right-.23]: box('Backside vertical brace',(x,.09,-.05),(.07,.06,2.1),iron,wing)

for x in [-.45,.45]:
    box('Rear release socket',(x,.17,.1),(.18,.10,.30),iron,center)
    box('Backside vertical stiffener',(x,.14,0),(.075,.08,2.1),iron,center)

# Batch by rigid part and material while keeping editable pivot groups.
for parent in [center]+wings:
    for mat in materials:
        objects=[o for o in parent.children if o.type=='MESH' and o.data.materials[0]==mat]
        if not objects: continue
        for obj in objects:
            bpy.context.view_layer.objects.active=obj
            for mod in list(obj.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.ops.object.select_all(action='DESELECT')
        for obj in objects: obj.select_set(True)
        bpy.context.view_layer.objects.active=objects[0]
        if len(objects)>1: bpy.ops.object.join()
        obj=objects[0]
        obj.name=parent.name+'_'+mat.name
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(island_margin=.025)
        bpy.ops.object.mode_set(mode='OBJECT')
for frame_num,deployed in [(1,0),(32,1),(60,1)]:
    for side,wing in zip([-1,1],wings):
        wing.rotation_euler.z=side*pi/2*(1-deployed)
        wing.keyframe_insert(data_path='rotation_euler',frame=frame_num)
scene.frame_start=1
scene.frame_end=60
scene.frame_set(1)

# Shoes sit on production rear rack sockets; aft uprights clear the wrap guards.
rack=empty('TombstoneRack',(0,-1.845,1.34))
for x in [-.45,.45]:
    box('Rear socket foot',(x,-.3,.17),(.17,.18,.055),steel,rack)
    box('Rearward support beam',(x,-.76,.22),(.08,.99,.08),iron,rack)
    box('Shield support post',(x,-1.22,-.20),(.09,.09,.9),steel,rack)
    box('Release jaw',(x,-1.30,-.97),(.17,.20,.14),gold,rack)
scene['Mount contract']='Production Car; center Godot (0,.25,3.25). Center 3.7x2.5m, wings 1.45m. Wings wrap forward outside X +/-1.85. All three rigid parts preserve height and width on release.'
scene['Approved reference']='reference/concepts-v4/carnage-circus.png'
scene['Animation']='Frames 1/32 folded/deployed; same panels, fully flat wings; runtime drives accepted state.'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/Tombstone.blend'))
root.location=(0,0,0)
bpy.ops.object.select_all(action='DESELECT')
for obj in [root]+list(root.children_recursive): obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'Tombstone.glb'),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
rack.location=(0,0,0)
bpy.ops.object.select_all(action='DESELECT')
for obj in [rack]+list(rack.children_recursive): obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'TombstoneRack.glb'),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
manifest={'author':'Original Trackstorm project work','tool':bpy.app.version_string,'source':'source/Tombstone.blend','export':'Tombstone.glb','reference':'../../vehicles/source/TrackstormCar.blend','reference_sha256':hashlib.sha256(CAR.read_bytes()).hexdigest(),'concept':'reference/concepts-v4/carnage-circus.png; user-approved built-in imagegen reference, not a runtime texture','no_car_geometry_exported':True,'sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT/'Tombstone.glb',ROOT/'TombstoneRack.glb',ROOT/'source/Tombstone.blend']}}
(ROOT/'sources.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('TOMBSTONE_AUTHORING_COMPLETE',flush=True)
