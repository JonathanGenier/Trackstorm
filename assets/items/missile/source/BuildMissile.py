"""Original Blender production geometry for TS-240; rebuild only before manual edits."""
from pathlib import Path
import math, json, hashlib, random
import bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
REPO=ROOT.parents[2]
random.seed(240)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version=0
collection=bpy.data.collections.new('Missile production')
bpy.context.scene.collection.children.link(collection)
def point(v): return Vector((v[0],-v[2],v[1]))
def material(name,color,metal,rough):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    return m
red=material('Missile_Crimson',(.48,.018,.027),.65,.34)
ivory=material('Missile_Ivory',(.82,.73,.55),.3,.48)
gold=material('Missile_Gold',(.7,.36,.045),.82,.32)
steel=material('Missile_Steel',(.20,.23,.25),.85,.4)
black=material('Missile_Iron',(.026,.032,.039),.75,.5)
rust=material('Missile_Oxide',(.12,.052,.028),.35,.82)
eye=material('Missile_Eye',(1,.67,.18),.45,.22)
def node(name,parent=None,pos=(0,0,0)):
    o=bpy.data.objects.new(name,None); collection.objects.link(o); o.parent=parent; o.location=point(pos); return o
def finish(o,name,mat,parent,bevel=0):
    o.name=name
    for c in list(o.users_collection): c.objects.unlink(o)
    collection.objects.link(o); o.parent=parent; o.data.materials.append(mat)
    if bevel:
        m=o.modifiers.new('Machined edges','BEVEL'); m.width=bevel; m.segments=2
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return o
def box(name,pos,size,mat,parent,bevel=.003):
    bpy.ops.mesh.primitive_cube_add(size=1,location=point(pos)); o=bpy.context.object; o.scale=(size[0],size[2],size[1])
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat,parent,bevel)
def pin(name,a,b,r,mat,parent,n=20):
    a,b=point(a),point(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=r,depth=(b-a).length,location=(a+b)/2)
    o=bpy.context.object; o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return finish(o,name,mat,parent,.002)
def ball(name,pos,scale,mat,parent):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,location=point(pos)); o=bpy.context.object; o.scale=(scale[0],scale[2],scale[1])
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for p in o.data.polygons: p.use_smooth=True
    return finish(o,name,mat,parent)
def plate(name,verts,mat,parent,depth=.004):
    mesh=bpy.data.meshes.new(name); mesh.from_pydata([point(v) for v in verts],[],[list(range(len(verts)))])
    o=bpy.data.objects.new(name,mesh); collection.objects.link(o); o.parent=parent; mesh.materials.append(mat)
    m=o.modifiers.new('Solid sheet','SOLIDIFY'); m.thickness=depth; m.offset=0
    m=o.modifiers.new('Plate edges','BEVEL'); m.width=.002; m.segments=2
    return o
missile=node('Missile'); body=node('ForwardBody',missile); tail=node('TailExtension',missile)
pin('Pressure vessel',(0,0,-.31),(0,0,.12),.145,red,body,40)
pin('Ivory barrel',(0,0,-.22),(0,0,-.055),.148,ivory,body,40)
for z in [-.31,-.045,.11]:
    pin('Body hoop',(0,0,z-.013),(0,0,z+.013),.156,gold if z==-.31 else steel,body,40)
    for j in range(12):
        a=j*math.tau/12; ball('Rivet',(.157*math.cos(a),.157*math.sin(a),z),(.011,.011,.009),steel,body)
pin('Telescopic chamber',(0,0,-.08),(0,0,.43),.113,black,missile,32)
for j in range(8):
    a=j*math.tau/8; x,y=.129*math.cos(a),.129*math.sin(a)
    pin('Chrome guide',(x,y,-.10),(x,y,.39),.013,steel,missile,12)
pin('Engine casing',(0,0,.13),(0,0,.40),.139,red,tail,32)
for z in [.14,.25,.39]: pin('Engine band',(0,0,z-.016),(0,0,z+.016),.15,gold if z==.25 else steel,tail,32)
pin('Nozzle shroud',(0,0,.39),(0,0,.48),.117,steel,tail,32)
pin('Nozzle throat',(0,0,.478),(0,0,.481),.085,black,tail,32)
node('ExhaustSocket',tail,(0,0,.49))
ball('Clown mask',(0,0,-.355),(.159,.154,.125),ivory,body)
ball('Red nose',(0,.015,-.49),(.055,.051,.047),red,body)
ball('Grin lip',(0,-.060,-.454),(.131,.068,.025),red,body)
ball('Grin shadow',(0,-.060,-.474),(.116,.052,.012),black,body)
for j in range(9):
    x=(j-4)*.024; yy=-.046-.021*(1-(x/.11)**2)
    o=box('Tooth',(x,yy,-.487),(.021,.034,.014),ivory,body,.006); o.rotation_euler[1]=-(j-4)*.10
for side in [-1,1]:
    plate('Eye paint',[(side*.072,.12,-.43),(side*.032,.047,-.467),(side*.116,.027,-.446)],black,body)
    ball('Eye socket',(side*.073,.062,-.449),(.044,.038,.026),black,body)
    ball('Amber eye',(side*.073,.059,-.472),(.026,.024,.016),eye,body)
    ball('Pupil',(side*.074,.06,-.487),(.009,.013,.005),black,body)
    pin('Brow',(side*.037,.095,-.46),(side*.112,.105,-.431),.016,ivory,body)
for side in [-1,0,1]:
    verts=[]; faces=[]; rings=9; sides=10
    for j in range(rings):
        t=j/(rings-1); x=side*(.028+.073*t); y=.115+.12*math.sin(t*math.pi*.8); z=-.28+.035*t; r=.041*(1-t)+.007
        for k in range(sides):
            a=k*math.tau/sides; verts.append(point((x+r*math.cos(a),y,z+r*math.sin(a))))
    for j in range(rings-1):
        for k in range(sides): faces.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
    faces.extend([tuple(reversed(range(sides))),tuple(range((rings-1)*sides,rings*sides))])
    mesh=bpy.data.meshes.new('Jester horn'); mesh.from_pydata(verts,[],faces)
    o=bpy.data.objects.new('Jester horn',mesh); collection.objects.link(o); o.parent=body; mesh.materials.append(red if side!=-1 else gold)
    ball('Jester bell',(side*.101,.185,-.245),(.022,.022,.022),gold,body)
for i in range(4):
    fin=node('Fin_'+str(i),tail); fin.rotation_euler[1]=i*math.pi/2
    hinge=node('FinHinge_'+str(i),fin,(.14,0,.22))
    plate('Fin rim',[(0,0,-.05),(.18,0,-.14),(.22,0,.18),(0,0,.20)],steel,hinge,.013)
    plate('Crimson fin',[(.014,.013,-.035),(.169,.013,-.115),(.202,.013,.16),(.015,.013,.179)],red,hinge,.005)
    for offset in [-.010,.019]:
        verts=[]
        for j in range(10):
            a=j*math.pi/5; r=.039 if j%2==0 else .017; verts.append((.105+math.cos(a)*r,offset,.045+math.sin(a)*r))
        plate('Fin star',verts,gold,hinge,.001)
    plate('Crimson fin back',[(.014,-.009,-.035),(.169,-.009,-.115),(.202,-.009,.16),(.015,-.009,.179)],red,hinge,.003)
    pin('Hinge',(0,-.018,-.04),(0,-.018,.18),.016,steel,hinge,12)
    for z in [-.025,.16]: ball('Hinge bolt',(0,.008,z),(.018,.012,.018),gold,hinge)
for i in range(100):
    a=random.uniform(0,math.tau); z=random.uniform(-.29,.10); r=.15
    o=box('Paint chip',(r*math.cos(a),r*math.sin(a),z),(random.uniform(.003,.009),.0015,random.uniform(.004,.014)),rust if i%3 else steel,body,0)
    o.rotation_euler[1]=a-math.pi/2
launcher=node('MissileLauncher')
pin('Socket turntable',(0,.025,0),(0,.065,0),.25,black,launcher,48)
for x in [-.22,.22]:
    for z in [-.22,.22]:
        box('Mount foot',(x,.025,z),(.13,.05,.13),red,launcher)
        pin('Socket bolt',(x,.051,z),(x,.068,z),.026,gold,launcher,6)
yaw=node('TurretYaw',launcher)
pin('Yaw bearing',(0,.07,0),(0,.105,0),.225,red,yaw,48)
for i in range(36):
    a=i*math.tau/36
    o=box('Gear tooth',(.228*math.cos(a),.077,.228*math.sin(a)),(.033,.025,.027),steel,yaw,.002); o.rotation_euler[2]=-a
for x in [-.13,.13]:
    pin('Lift sleeve',(x,.08,0),(x,.19,0),.053,black,yaw)
    pin('Lift collar',(x,.17,0),(x,.20,0),.058,gold,yaw)
lift=node('MountLift',yaw)
for x in [-.13,.13]:
    pin('Lift piston',(x,-.10,0),(x,.12,0),.035,steel,lift)

for x in [-.185,.185]:
    box('Trunnion',(x,.132,0),(.055,.16,.15),red,lift)
    pin('Pitch axle',(x-.035,.20,0),(x+.035,.20,0),.047,steel,lift)
pitch=node('CradlePitch',lift,(0,.20,0))
for x in [-.115,.115]:
    box('Cradle rail',(x,-.12,0),(.052,.055,.72),black,pitch)
    pin('Rail spindle',(x,-.083,-.34),(x,-.083,.34),.012,steel,pitch,12)
for z in [-.24,.23]:
    for side in [-1,1]:
        for j in range(5):
            a=-math.pi/2+(j+.5)*math.pi/10
            box('Cradle saddle',(.17*math.cos(a)*side,.17*math.sin(a),z),(.07,.05,.06),red,pitch)
        pin('Saddle bolt',(side*.18,-.035,z),(side*.18,-.01,z),.018,gold,pitch,6)
node('MissileSocket',pitch)
for parent in [o for o in collection.objects if o.type == 'EMPTY']:
    for mat in list(bpy.data.materials):
        group=[o for o in parent.children if o.type == 'MESH' and o.data.materials and o.data.materials[0]==mat]
        if not group: continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in group: o.select_set(True)
        bpy.context.view_layer.objects.active=group[0]
        bpy.ops.object.convert(target='MESH');
        if len(group)>1: bpy.ops.object.join()
        bpy.context.object.name=parent.name+'_'+mat.name
car=REPO/'assets/vehicles/source/TrackstormCar.blend'
with bpy.data.libraries.load(str(car),link=True) as (src,dst): dst.objects=src.objects
reference=bpy.data.collections.new('Production Car reference')
bpy.context.scene.collection.children.link(reference); reference.hide_render=True
for obj in dst.objects:
    if obj: reference.objects.link(obj)
assert len(reference.objects)>0, 'Production Car reference must be linked'
for library in bpy.data.libraries: library.filepath=bpy.path.relpath(library.filepath,start=str(ROOT/'source'))
launcher.location=point((0,-.08,1.845)); missile.parent=pitch; missile.location=(0,0,0)
bpy.context.scene['TS-240 references']='10146, 10148, 10147; inspected before authoring'
bpy.context.scene['Deployment']='Rigid mount lift .27m, forward body -.36m, tail +.24m, symmetric hinged fins'
scene=bpy.context.scene
scene.frame_start=1; scene.frame_end=100
# Editable rigid deployment storyboard. Exports omit animation; production owns timing.
for frame,rack_height,mount_height,extension,fin_open in [(1,-.08,0,0,0),(36,1.34,0,0,0),(54,1.34,.27,0,0),(80,1.34,.27,1,0),(100,1.34,.27,1,1)]:
    launcher.location=point((0,rack_height,1.845)); launcher.keyframe_insert(data_path='location',frame=frame)
    lift.location=point((0,mount_height,0)); lift.keyframe_insert(data_path='location',frame=frame)
    body.location=point((0,0,-.36*extension)); body.keyframe_insert(data_path='location',frame=frame)
    tail.location=point((0,0,.24*extension)); tail.keyframe_insert(data_path='location',frame=frame)
    for i in range(4):
        hinge=bpy.data.objects['FinHinge_'+str(i)]; hinge.rotation_euler[1]=-(1-fin_open)*math.pi/2
        hinge.keyframe_insert(data_path='rotation_euler',frame=frame)
scene.frame_set(1)
bpy.context.view_layer.update()
points=[o.matrix_world @ Vector(corner) for o in collection.objects if o.type=='MESH' for corner in o.bound_box]
bounds={'minimum':[min(v[i] for v in points) for i in range(3)],'maximum':[max(v[i] for v in points) for i in range(3)]}
assert bounds['maximum'][2] < .37, bounds
assert bounds['minimum'][0] > -.6 and bounds['maximum'][0] < .6, bounds
scene['Compact Blender world bounds']=json.dumps(bounds)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/Missile.blend'))
# Export the neutral rigid hierarchy; MissileVisual folds the hinges on instantiation.
for o in [launcher,lift,body,tail]+[bpy.data.objects['FinHinge_'+str(i)] for i in range(4)]: o.animation_data_clear()
lift.location=(0,0,0); body.location=(0,0,0); tail.location=(0,0,0)
for i in range(4): bpy.data.objects['FinHinge_'+str(i)].rotation_euler=(0,0,0)
missile.parent=None; missile.location=(0,0,0); launcher.location=(0,0,0)
for root,path in [(missile,'Missile.glb'),(launcher,'MissileLauncher.glb')]:
    bpy.ops.object.select_all(action='DESELECT')
    for o in [root]+list(root.children_recursive): o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(ROOT/path),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
paths=[ROOT/'source/Missile.blend',ROOT/'Missile.glb',ROOT/'MissileLauncher.glb']
manifest={'authoring':'Original Blender 5.2 geometry; user-supplied concepts; no third-party acquisition','references':{'10146':'Missile compact 3.png','10148':'Missile Extended.png','10147':'Turret and mount.png'},'car_source_sha256':hashlib.sha256(car.read_bytes()).hexdigest(),'compact_blender_bounds':bounds,'sha256':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in paths+[ROOT/'BurningConfetti.gdshader']+list((ROOT/'reference').glob('*.png'))}}
(ROOT/'sources.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('MISSILE_AUTHORING_COMPLETE',flush=True)
