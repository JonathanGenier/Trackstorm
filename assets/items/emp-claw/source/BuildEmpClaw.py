"""Original TS-296 production meshes and editable rigid animation storyboard.

Coordinates below use Godot metres (+Y up, -Z forward); exported pivots preserve
that contract. Rebuilding replaces the generated masters: preserve manual edits.
"""
from pathlib import Path
import math
import json
import hashlib
import bpy
import bmesh
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parents[2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.context.scene.unit_settings.system = 'METRIC'
collection = bpy.context.collection

def point(v):
    return Vector((v[0], -v[2], v[1]))

def material(name, rgb, metal, rough):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*rgb, 1)
    mat.use_nodes = True
    bs = mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*rgb, 1)
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    # Packed original UV enamel wear, also carried by GLB (no external maps).
    n = 256
    y, x = np.mgrid[:n, :n] / n
    rng = np.random.default_rng(296)
    noise = rng.random((n, n))
    shade = .87 + .07 * np.sin(x * 27 + np.cos(y * 31)) + .06 * noise
    chips = (noise > .985) | ((np.sin(x * 160 + y * 18) > .997) & (noise > .6))
    pixels = np.ones((n, n, 4), dtype=np.float32)
    pixels[:, :, :3] = shade[:, :, None] * np.array(rgb)
    pixels[chips, :3] = (.12, .085, .05)
    image = bpy.data.images.new(name + ' patina', width=n, height=n)
    image.pixels.foreach_set(pixels.ravel())
    image.pack()
    tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image = image
    mat.node_tree.links.new(tex.outputs['Color'], bs.inputs['Base Color'])
    return mat

red = material('EMP crimson enamel', (.65, .018, .029), .42, .32)
ivory = material('EMP warm porcelain', (.88, .81, .65), .25, .40)
steel = material('EMP brushed steel', (.34, .39, .43), .86, .30)
iron = material('EMP blackened iron', (.042, .052, .064), .80, .42)
gold = material('EMP brass fittings', (.65, .37, .07), .80, .29)
rubber = material('EMP rubber hoses', (.016, .020, .025), .05, .58)
nosemat = material('EMP balloon red', (.82, .009, .018), .12, .20)
blue = material('EMP energized eyes', (.04, .38, .95), .20, .22)
bs = blue.node_tree.nodes.get('Principled BSDF')
bs.inputs['Emission Color'].default_value = (.025, .24, 1, 1)
bs.inputs['Emission Strength'].default_value = 2

def node(name, parent=None, pos=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    collection.objects.link(obj)
    obj.parent = parent
    obj.location = point(pos)
    obj.empty_display_size = .04
    return obj

def finish(obj, name, mat, parent, bevel=0):
    obj.name = name
    obj.parent = parent
    obj.data.materials.append(mat)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = obj.modifiers.new('Machined edge', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        obj.modifiers.new('Weighted normal', 'WEIGHTED_NORMAL')
    return obj

def box(name, pos, size, mat, parent, bevel=.004):
    bpy.ops.mesh.primitive_cube_add(size=1, location=point(pos))
    obj = bpy.context.object
    obj.scale = (size[0], size[2], size[1])
    return finish(obj, name, mat, parent, bevel)

def pin(name, a, b, r, mat, parent, sides=24):
    a, b = point(a), point(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=r, depth=(b-a).length, location=(a+b)/2)
    obj = bpy.context.object
    obj.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    for face in obj.data.polygons:
        face.use_smooth = len(face.vertices) == 4
    return finish(obj, name, mat, parent, .002)

def ball(name, pos, size, mat, parent):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=16, location=point(pos))
    obj = bpy.context.object
    obj.scale = (size[0], size[2], size[1])
    for face in obj.data.polygons:
        face.use_smooth = True
    return finish(obj, name, mat, parent)

def mesh(name, verts, faces, mat, parent):
    data = bpy.data.meshes.new(name)
    data.from_pydata([point(v) for v in verts], [], faces)
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    obj = bpy.data.objects.new(name, data)
    collection.objects.link(obj)
    obj.parent = parent
    data.materials.append(mat)
    return obj

def plate(name, verts, mat, parent, thickness=.003):
    obj = mesh(name, verts, [list(range(len(verts)))], mat, parent)
    mod = obj.modifiers.new('Sheet thickness', 'SOLIDIFY')
    mod.thickness = thickness
    return obj

def tube(name, z0, z1, radius, thickness, mat, parent, x=0, y=0):
    n = 48
    verts = [(x+math.cos(i*math.tau/n)*r, y+math.sin(i*math.tau/n)*r, z)
             for z, r in [(z0, radius), (z1, radius), (z0, radius-thickness), (z1, radius-thickness)] for i in range(n)]
    faces = []
    for i in range(n):
        j = (i+1) % n
        faces += [(i,j,n+j,n+i),(2*n+i,3*n+i,3*n+j,2*n+j),
                  (i,2*n+i,2*n+j,j),(n+i,n+j,3*n+j,3*n+i)]
    return mesh(name, verts, faces, mat, parent)

def hose(name, points, r, parent, mat=rubber):
    curve = bpy.data.curves.new(name, 'CURVE')
    curve.dimensions = '3D'
    curve.bevel_depth = r
    curve.bevel_resolution = 2
    spline = curve.splines.new('BEZIER')
    spline.bezier_points.add(len(points)-1)
    for p, v in zip(spline.bezier_points, points):
        p.co = point(v)
        p.handle_left_type = p.handle_right_type = 'AUTO'
    obj = bpy.data.objects.new(name, curve)
    collection.objects.link(obj)
    obj.parent = parent
    curve.materials.append(mat)
    return obj

launcher = node('EmpClawLauncher')
box('Rack saddle', (0, .015, 0), (1.10, .045, .66), iron, launcher)
for x in [-.48, .48]:
    for z in [-.26, .26]:
        box('Red clamp', (x,.045,z), (.14,.04,.12), red, launcher)
        pin('Captive bolt', (x,.06,z), (x,.08,z), .024, gold, launcher, 6)
pin('Slew bearing fixed', (0,.045,0),(0,.105,0),.25,steel,launcher,48)
yaw = node('TurretYaw', launcher)
pin('Geared turntable',(0,.105,0),(0,.14,0),.24,red,yaw,48)
for i in range(40):
    a=i*math.tau/40
    obj=box('Slew gear tooth',(.245*math.cos(a),.115,.245*math.sin(a)),(.027,.03,.024),iron,yaw,.001)
    obj.rotation_euler[2]=a
for x in [-.20,.20]:
    pin('Vertical lift guide',(x,.08,0),(x,.23,0),.045,iron,yaw)
lift=node('CannonLift',yaw)
for x in [-.20,.20]:
    pin('Polished lift piston',(x,-.06,0),(x,.20,0),.028,steel,lift)
    box('Cradle trunnion',(x,.18,0),(.08,.18,.19),red,lift)
    pin('Pitch bearing',(x-.055,.23,0),(x+.055,.23,0),.068,steel,lift)
pitch=node('CannonPitch',lift,(0,.23,0))
recoil=node('BarrelRecoil',pitch)
pin('Pressure breech',(0,0,-.08),(0,0,.19),.17,red,recoil,48)
ball('Breech cap',(0,0,.19),(.17,.17,.065),iron,recoil)
tube('Fixed receiver',-.36,.05,.178,.010,ivory,recoil)
for z in [-.27,-.02,.15]:
    tube('Receiver steel band',z-.018,z+.018,.186,.018,steel,recoil)
    for i in range(12):
        a=i*math.tau/12
        ball('Band rivet',(.19*math.cos(a),.19*math.sin(a),z),(.013,.013,.013),gold,recoil)
middle=node('BarrelMiddle',recoil)
tube('Intermediate telescopic sleeve',-.45,.05,.165,.010,steel,middle)
tube('Intermediate crimson collar',-.46,-.41,.175,.012,red,middle)
extension=node('BarrelExtension',recoil)
tube('Telescopic cannon bore',-.49,0,.150,.010,red,extension)
tube('Black muzzle lining',-.505,-.32,.145,.009,iron,extension)
tube('Flared muzzle crown',-.53,-.47,.185,.035,steel,extension)
for i in range(12):
    a=i*math.tau/12
    def ringpos(angle,z): return (.152*math.cos(angle),.152*math.sin(angle),z)
    plate('Ivory circus muzzle tooth',[ringpos(a-.20,-.45),ringpos(a+.20,-.45),ringpos(a,-.30)],ivory,extension)
node('Muzzle',extension,(0,0,-.54))
for x in [-.19,.19]:
    pin('Barrel guide',(x,-.07,-.32),(x,-.07,.13),.012,steel,recoil,12)
    pin('Guide collar',(x,-.07,-.19),(x,-.07,-.14),.023,gold,recoil,12)
# Air reservoir rides the fixed saddle. Its metal body remains full size in the bay.
pin('Air tank',( -.43,-.075,-.20),(.43,-.075,-.20),.095,red,launcher,40)
for x in [-.43,.43]:
    ball('Tank dished end',(x,-.075,-.20),(.06,.095,.095),red,launcher)
for x in [-.32,.32]:
    box('Tank retaining strap',(x,-.07,-.20),(.035,.205,.205),steel,launcher,.025)
pin('Valve stem',(.49,-.075,-.20),(.55,-.075,-.20),.022,gold,launcher)
pin('Valve wheel',(.54,-.075,-.22),(.54,-.075,-.18),.052,red,launcher)
hose('Fixed pneumatic line',[(.52,-.075,-.20),(.52,.07,-.12),(.30,.07,.14),(.23,.11,.06)],.016,launcher)
pin('Pressure gauge housing',(-.30,.08,-.27),(-.30,.08,-.31),.055,gold,launcher)
pin('Pressure gauge face',(-.30,.08,-.311),(-.30,.08,-.315),.046,ivory,launcher)
pin('Gauge needle',(-.30,.08,-.317),(-.32,.105,-.317),.003,iron,launcher,8)
# Suspended magazine belongs to the fixed rack, below its crossmembers. Only the
# cartridge being fed leaves this position; cannon yaw never carries the magazine.
for index,x in enumerate([-.37,0,.37]):
    box('Feed rail',(x,-.260,.28),(.29,.016,.24),iron,launcher)
    for sx in [-.115,.115]:
        pin('Feed slide',(x+sx,-.245,.16),(x+sx,-.245,.40),.010,steel,launcher,12)
        pin('Magazine hanger',(x+sx,.015,.20),(x+sx,-.245,.20),.010,iron,launcher,12)
    socket=node('RoundSocket_'+str(index),launcher,(x,-.125,.28))
    box('Feed saddle',(0,-.125,0),(.21,.020,.16),red,socket)
    for side in [-1,1]:
        box('Round retaining cheek',(side*.13,-.065,0),(.025,.10,.11),steel,socket)
    rod=node('FeedRod_'+str(index),launcher,(x,-.245,.28))
    pin('Feeder piston',(0,0,0),(0,1,0),.018,steel,rod,16)
    rod.scale=(1,1,.020)
    pin('Feeder cylinder',(x,-.257,.28),(x,-.240,.28),.032,iron,launcher,20)
    box('Magazine bracket',(x,-.240,.40),(.26,.035,.025),red,launcher)
    for sx in [-.10,.10]:
        ball('Magazine bolt',(x+sx,-.240,.419),(.013,.013,.008),gold,launcher)

# Clown device faces -Z; its Attachment is the rear metal-contact plane.
claw=node('EmpClaw')
pin('Device drum',(0,0,.045),(0,0,-.025),.135,iron,claw,48)
tube('Armored rim',-.045,.04,.143,.018,steel,claw)
ball('Clown porcelain face',(0,0,-.045),(.123,.125,.045),ivory,claw)
for i in range(20):
    a=i*math.tau/20
    box('Vent slot',(.137*math.cos(a),.137*math.sin(a),.01),(.012,.012,.028),iron,claw,.001)
    ball('Rim rivet',(.129*math.cos(a),.129*math.sin(a),-.052),(.007,.007,.005),gold,claw)
for side in [-1,1]:
    ball('Eye socket',(side*.048,.032,-.080),(.041,.033,.018),iron,claw)
    ball('Blue iris',(side*.049,.031,-.094),(.022,.018,.007),blue,claw)
    plate('Angry brow',[(side*.009,.061,-.104),(side*.090,.074,-.090),(side*.083,.041,-.109),(side*.028,.040,-.112)],ivory,claw)
    plate('Clown eye paint',[(side*.043,.075,-.078),(side*.065,.110,-.067),(side*.076,.067,-.079)],red,claw)
ball('Grinning mouth',(0,-.050,-.080),(.090,.049,.012),iron,claw)
for i in range(9):
    x=(i-4)*.018
    top=-.037-.015*(1-abs(i-4)/4)
    plate('Serrated grin',[(x-.007,top,-.096),(x+.007,top,-.096),(x,top-.032,-.099)],gold,claw)
hose('Painted grin',[(-.095,-.035,-.083),(-.065,-.077,-.089),(0,-.101,-.076),(.065,-.077,-.089),(.095,-.035,-.083)],.006,claw,red)
nose=node('BalloonNose',claw,(0,-.002,-.10))
ball('NoseMembrane',(0,0,-.025),(.050,.050,.050),nosemat,nose)
pin('Balloon ferrule',(0,0,.006),(0,0,-.012),.026,gold,nose)
node('NoseHitAnchor',nose,(0,0,-.025))
node('Attachment',claw,(0,0,.055))
node('TrailSocket',claw,(0,0,.07))
for i in range(4):
    a=math.pi/4+i*math.pi/2
    radial=node('JawRadial_'+str(i),claw)
    radial.rotation_euler[1]=-a
    joint=node('JawHinge_'+str(i),radial,(.116,0,0))
    pin('Hinge axle',(0,-.021,0),(0,.021,0),.025,gold,joint)
    # Swept taper with a hooked metal contact at +Z when deployed.
    plate('Serrated claw armor',[(-.014,-.033,-.016),(.095,-.041,.004),(.145,-.029,.055),(.115,.035,.062),(.015,.038,.006)],ivory,joint,.022)
    box('Forged jaw core',(.048,0,.014),(.080,.050,.031),steel,joint,.006)
    pin('Claw spine',(.008,0,.008),(.082,0,.024),.019,steel,joint)
    pin('Hooked contact',(.082,0,.024),(.140,0,.065),.015,iron,joint,12)
    for t in [.025,.048,.072]:
        plate('Claw tooth',[(t,-.017,.024),(t+.015,-.017,.029),(t+.008,-.032,.043)],steel,joint)
    plate('Claw red slash',[(.020,-.026,.008),(.042,-.028,.013),(.066,-.015,.025)],red,joint)
    node('GripContact_'+str(i),joint,(.140,0,.065))

# Preserve all source parts and the named rigid animation controls in the master.
scene=bpy.context.scene
scene.frame_start=1
scene.frame_end=180
scene['Reference attachments']='TS-296 10155/10156/10157/10158; original geometry, reference pixels not redistributed'
scene['Runtime axes']='Godot metres; CannonPitch deployed at rack + (0,.47,0), matching WeaponAim.Pivot'
for frame, height, lift_height, extent in [(1,-.08,0,0),(32,1.34,0,0),(44,1.34,.45,0),(54,1.34,.24,0),(72,1.34,.24,1)]:
    launcher.location=point((0,height,1.845)); launcher.keyframe_insert(data_path='location',frame=frame)
    lift.location=point((0,lift_height,0)); lift.keyframe_insert(data_path='location',frame=frame)
    extension.location=point((0,0,-.68*extent)); extension.keyframe_insert(data_path='location',frame=frame)
    middle.location=point((0,0,-.34*extent)); middle.keyframe_insert(data_path='location',frame=frame)
for frame, spread, inflation in [(1,0,1),(78,.35,1),(100,1,1),(120,1,2.6),(155,1,.05),(175,0,.05)]:
    for i in range(4):
        joint=bpy.data.objects['JawHinge_'+str(i)]
        joint.rotation_euler[2]=(1-spread)*math.radians(-78)
        joint.keyframe_insert(data_path='rotation_euler',frame=frame)
    nose.scale=(inflation,)*3; nose.keyframe_insert(data_path='scale',frame=frame)
for frame,label in [(1,'STOWED'),(32,'RACK_RAISED'),(44,'EMERGED'),(54,'LOWERED'),(72,'READY'),(78,'FIRED'),(100,'LANDED'),(120,'ACTIVATED'),(155,'DEFLATED'),(175,'RELEASED')]:
    scene.timeline_markers.new(label,frame=frame)
car=REPO/'assets/vehicles/source/TrackstormCar.blend'
with bpy.data.libraries.load(str(car),link=True) as (src,dst):
    dst.objects=src.objects
reference=bpy.data.collections.new('Production Car reference (unchanged)')
scene.collection.children.link(reference)
reference.hide_render=True
for obj in dst.objects:
    if obj: reference.objects.link(obj)
for library in bpy.data.libraries:
    library.filepath=bpy.path.relpath(library.filepath,start=str(ROOT/'source'))
scene.frame_set(1)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/EmpClaw.blend'))
for obj in [launcher,lift,middle,extension,nose]+[bpy.data.objects['JawHinge_'+str(i)] for i in range(4)]:
    obj.animation_data_clear()
launcher.location=(0,0,0)
lift.location=(0,0,0)
extension.location=(0,0,0)
middle.location=(0,0,0)
nose.scale=(1,1,1)
for i in range(4): bpy.data.objects['JawHinge_'+str(i)].rotation_euler=(0,0,0)
# Merge only surfaces sharing a rigid parent/material in the runtime exports.
for parent in [o for o in list(collection.objects) if o.type=='EMPTY']:
    for mat in bpy.data.materials:
        group=[o for o in parent.children if o.type in ('MESH','CURVE') and o.data.materials and o.data.materials[0]==mat]
        if not group: continue
        bpy.ops.object.select_all(action='DESELECT')
        for obj in group: obj.select_set(True)
        bpy.context.view_layer.objects.active=group[0]
        bpy.ops.object.convert(target='MESH')
        if len(group)>1: bpy.ops.object.join()
        bpy.context.object.name=parent.name+'_'+mat.name.replace(' ','_')
for root,filename in [(launcher,'EmpClawLauncher.glb'),(claw,'EmpClaw.glb')]:
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [root]+list(root.children_recursive): obj.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(ROOT/filename),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
paths=[ROOT/'source/BuildEmpClaw.py',ROOT/'source/EmpClaw.blend',ROOT/'EmpClaw.glb',ROOT/'EmpClawLauncher.glb']
manifest={'authoring':'Original Blender geometry, UV patina and rigid animation; no third-party acquisition',
 'references':{'10155':'t shirt launcher.png','10156':'Back stowed.png','10157':'emp claw activated.png','10158':'emp claw landed.png'},
 'reference_policy':'User-supplied Jira design direction; no reference pixels redistributed',
 'car_source_sha256':hashlib.sha256(car.read_bytes()).hexdigest(),
 'sha256':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}}
(ROOT/'sources.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print('EMP_CLAW_AUTHORING_COMPLETE',flush=True)
