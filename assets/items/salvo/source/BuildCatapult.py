"""Original TS-175 catapult. Blender 5.2; authored in metres, Godot-space helper coordinates.

The production Car/rack is never rebuilt. Named rigid joints are the runtime contract.
Run with Blender --background --python-exit-code 1 --python <this absolute path>.
"""
from pathlib import Path
from math import sin, cos, pi
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.context.scene.unit_settings.system = 'METRIC'

def v(p): return Vector((p[0], -p[2], p[1]))

def mat(name, rgb, metal=.0, rough=.55, wood=False):
    m = bpy.data.materials.new(name); m.diffuse_color = (*rgb, 1); m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Metallic'].default_value = metal; bs.inputs['Roughness'].default_value = rough
    n = 512 if wood else 128
    y, x = np.mgrid[:n, :n] / n; rng = np.random.default_rng(175)
    noise = rng.random((n, n))
    warp = x + .025*np.sin(y*14) + .018*np.sin(y*37+x*9)
    grain = np.sin(warp * 180 + np.sin(x * 21 + y * 6))
    shade = .64 + .22 * noise + (.09 * grain + .055*np.sin(warp*620) if wood else .08 * np.sin(x * 57 + y * 41))
    shade[(noise > .993)] = .35
    pixels = np.ones((n, n, 4), dtype=np.float32); pixels[:, :, :3] = shade[:, :, None] * np.array(rgb)
    im = bpy.data.images.new(name, width=n, height=n); im.pixels.foreach_set(pixels.ravel()); im.pack()
    tex = m.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = im
    m.node_tree.links.new(tex.outputs['Color'], bs.inputs['Base Color'])
    return m

wood = mat('Salvo oiled ash grain', (.52, .265, .095), .0, .48, True)
iron = mat('Salvo forged graphite', (.12, .145, .16), .78, .36)
steel = mat('Salvo worn steel edges', (.34, .39, .43), .68, .44)
brass = mat('Salvo brass bushings', (.59, .37, .12), .72, .35)
red = mat('Salvo crimson paper', (.75, .045, .025), 0, .73)
ivory = mat('Salvo paper labels', (.89, .78, .49), 0, .65)
rubber = mat('Salvo rubber buffers', (.025, .03, .035), .0, .7)

def empty(name, parent=None, at=(0, 0, 0)):
    o = bpy.data.objects.new(name, None); bpy.context.collection.objects.link(o)
    o.parent = parent; o.location = v(at); o.empty_display_size = .06
    return o

def finish(o, name, material, parent, bevel=.0):
    o.name = name; o.parent = parent; o.data.materials.append(material)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        b = o.modifiers.new('Forged rounded edges', 'BEVEL'); b.width = bevel; b.segments = 2
        bpy.ops.object.modifier_apply(modifier=b.name)
    if o.type == 'MESH':
        for p in o.data.polygons: p.use_smooth = len(p.vertices) == 4 and 'cyl' in name
        if not o.data.uv_layers:
            bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
            bpy.ops.uv.smart_project(island_margin=.02); bpy.ops.object.mode_set(mode='OBJECT')
    return o

def box(name, at, size, material, parent, bevel=.012):
    bpy.ops.mesh.primitive_cube_add(size=1, location=v(at)); o=bpy.context.object
    o.scale = (size[0], size[2], size[1]); return finish(o, name, material, parent, bevel)

def cyl(name, a, b, radius, material, parent, verts=24):
    d = v(b) - v(a)
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=radius, depth=d.length, location=(v(a)+v(b))/2)
    o=bpy.context.object; o.rotation_mode='QUATERNION'; o.rotation_quaternion=d.to_track_quat('Z','Y')
    return finish(o, 'cyl_'+name, material, parent, .004)

def path(name, pts, radius, material, parent):
    curve=bpy.data.curves.new(name,'CURVE'); curve.dimensions='3D'; curve.resolution_u=1
    curve.bevel_depth=radius; curve.bevel_resolution=2
    s=curve.splines.new('POLY'); s.points.add(len(pts)-1)
    for p,co in zip(s.points,pts): p.co=(*v(co),1)
    o=bpy.data.objects.new(name,curve); bpy.context.collection.objects.link(o); o.parent=parent
    o.data.materials.append(material); bpy.context.view_layer.objects.active=o
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.ops.object.convert(target='MESH')
    return o

def bolt(at, parent, axis='x'):
    a=list(at); b=list(at); i=0 if axis=='x' else 1; a[i]-=.015; b[i]+=.015
    cyl('hex fastener',a,b,.033,steel,parent,6)

root=empty('SalvoCatapult')
fixed=empty('FixedMount',root)
services=empty('WinchHousing',root,(0,-.40,0))
drum=empty('WinchDrum',services)
slide=empty('TurretSlide',root,(0,.12,-.65))
yaw=empty('TurretYaw',slide)
support=empty('SupportExtension',yaw)
arm=empty('SpoonHinge',yaw,(0,.20,0))
middle=empty('ArmMiddle',arm)
tip=empty('ArmTip',middle)
magazine=empty('Magazine',tip)
spring=empty('Spring',yaw)
cable=empty('Cable',root)
feed=empty('FeedGate',magazine)
empty('BowlSocket',tip,(0,.06,.77))
empty('CableEye',tip,(0,-.075,.43))
empty('SpringTop',tip,(0,-.06,.32))
empty('SpringFoot',support,(0,-.16,.90))
empty('WinchExit',root,(0,-.24,-.15))
empty('CableSwivel',slide,(0,-.02,0))

# Two overlapping slides bolt to the existing rack, never another rack.
for x in [-.29,.29]:
    box('fixed guide rail',(x,.04,-.20),(.10,.10,.90),iron,fixed)
    box('chrome sliding runner',(x,.01,.65),(.065,.065,1.38),steel,slide)
    for z in [-.58,.14]: bolt((x,.105,z),fixed,'y')
cyl('slew lower race',(0,-.035,0),(0,.035,0),.29,iron,yaw,64)
cyl('slew polished race',(0,.035,0),(0,.08,0),.27,steel,yaw,64)
for i in range(48):
    t=i*2*pi/48
    tooth=box('slew tooth',(.282*cos(t),.01,.282*sin(t)),(.028,.055,.025),steel,yaw,.003)
    tooth.rotation_euler.z=-t
for x in [-.14,.14]:
    box('hinge cheek',(x,.15,0),(.055,.23,.27),iron,yaw)
    cyl('axle',(-.21,.20,0),(.21,.20,0),.087,steel,yaw)
    bolt((x*1.45,.20,0),yaw)
box('spring support root',(0,-.23,.40),(.25,.11,.90),iron,yaw)
box('spring support telescoping',(0,-.23,.45),(.18,.075,.90),steel,support)
for x in [-.20,.20]:
    box('release stop bracket',(x,.13,.12),(.08,.12,.15),iron,yaw)
    cyl('stop buffer',(x-.035,.23,.15),(x+.035,.23,.15),.058,rubber,yaw)

# Nesting wooden beam inside steel collars. The grain continues into a deep spoon.
box('root ash beam',(0,0,.32),(.21,.15,.82),wood,arm)
box('middle ash beam',(0,0,.35),(.17,.12,.90),wood,middle)
box('tip ash beam',(0,0,.28),(.14,.10,.67),wood,tip)
for parent,z,width in [(arm,.1,.23),(arm,.44,.23),(middle,.48,.19),(tip,.47,.17)]:
    box('steel saddle',(0,-.015,z),(width,.17,.085),iron,parent)
    for x in [-width/2,width/2]: bolt((x,0,z),parent)

# Closed double-surface oval bowl, thirty-two stave segments and a thick rolled rim.
verts=[]; faces=[]; n=48; rings=10
for inner in [False,True]:
    for j in range(rings):
        r=.045+.955*j/(rings-1)
        for i in range(n):
            t=2*pi*i/n
            verts.append(tuple(v((.38*r*cos(t),-.20+.31*r*r+( .038 if inner else 0),.81+.47*r*sin(t)))))
for side in range(2):
    start=side*rings*n
    for j in range(rings-1):
        for i in range(n):
            a=start+j*n+i; b=start+j*n+(i+1)%n
            f=(a,b,b+n,a+n); faces.append(f if side else f[::-1])
for i in range(n):
    a=(rings-1)*n+i; b=(rings-1)*n+(i+1)%n
    faces.append((a,b,b+rings*n,a+rings*n))
faces.append(tuple(range(n-1,-1,-1))); faces.append(tuple(rings*n+i for i in range(n)))
mesh=bpy.data.meshes.new('Hollow stave bowl'); mesh.from_pydata(verts,[],faces); mesh.update()
o=bpy.data.objects.new('Carved ash bowl',mesh); bpy.context.collection.objects.link(o)
bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); finish(o,o.name,wood,tip)
path('rolled steel lip',[(.38*cos(i*2*pi/96),.135,.81+.47*sin(i*2*pi/96)) for i in range(97)],.018,steel,tip)
for t in [0,pi/2,pi,3*pi/2]:
    pts=[]
    for j in range(19):
        r=j/18
        pts.append((.384*r*cos(t),-.207+.31*r*r,.81+.475*r*sin(t)))
    path('bowl forged rib',pts,.025,iron,tip)
    for r in [.7,.94]:
        cyl('bowl rivet',(.382*r*cos(t),-.20+.31*r*r,.81+.47*r*sin(t)),(.382*r*cos(t),-.172+.31*r*r,.81+.47*r*sin(t)),.023,steel,tip,12)
# Crossed safety fingers retain the loaded round in every car orientation.
for x in [-.115,.115]:
    path('bowl retention spring',[(x,.0,.62),(x,.09,.71),(x,.09,.90),(x,.0,.98)],.012,brass,tip)

# Flat five-lane magazine sits directly on the outer handle. Rounds lie crosswise.
box('magazine floor',(0,.083,.23),(.48,.025,.60),iron,magazine)
for x in [-.235,.235]: box('magazine side flange',(x,.145,.23),(.025,.13,.62),steel,magazine)
rounds=[]
for i in range(5):
    z=.0+i*.112
    socket=empty('Round_'+str(i),magazine,(0,.165,z)); rounds.append(socket)
    cyl('paper firecracker',(-.19,0,0),(.19,0,0),.047,red,socket)
    for x in [-.17,.17]: cyl('crimped paper end',(x-.012,0,0),(x+.012,0,0),.048,ivory,socket)
    cyl('paper wrap',(-.045,0,0),(.045,0,0),.048,ivory,socket)
    path('fuse',[(.19,0,0),(.22,.02,0),(.235,.04,.015)],.006,brass,socket)
    path('retainer bail',[(-.22,.11,z),(-.22,.22,z),(.22,.22,z),(.22,.11,z)],.009,brass,magazine)
box('escapement stop',(0,.12,.54),(.46,.075,.025),brass,feed)
for x in [-.23,.23]:
    cyl('feed guide',(x,.115,.50),(x,.05,.70),.013,steel,magazine)

# Wound winch, independent drum, motor, gearbox, four bolted feet. Underslung in rack.
for x in [-.36,.36]:
    box('winch bearing bracket',(x,-.02,0),(.07,.39,.36),iron,services)
    box('winch bolted foot',(x,-.22,0),(.18,.045,.45),steel,services)
    for z in [-.16,.16]: bolt((x,-.185,z),services,'y')
cyl('drum core',(-.31,0,0),(.31,0,0),.14,iron,drum)
for x in [-.30,.30]: cyl('drum flange',(x-.02,0,0),(x+.02,0,0),.21,steel,drum)
path('wound cable',[(-.28+.56*i/650,.158*cos(i*2*pi/26),.158*sin(i*2*pi/26)) for i in range(651)],.010,steel,drum)
cyl('motor shell',(-.61,0,0),(-.41,0,0),.135,iron,services)
for x in [-.59,-.55,-.51,-.47]: cyl('motor cooling ring',(x-.012,0,0),(x+.012,0,0),.15,steel,services)
box('gearbox',(.48,0,0),(.20,.32,.30),iron,services)
box('winch caution plate',(.485,.015,.156),(.12,.16,.008),brass,services,.002)
path('power conduit',[(-.52,.1,-.05),(-.52,.21,-.1),(.48,.21,-.1),(.48,.1,-.05)],.015,rubber,services)
# Unit-height authored spring, dynamically stretched between physical seats.
path('compression coil',[(.10*cos(i*2*pi/32),i/256,.10*sin(i*2*pi/32)) for i in range(257)],.022,steel,spring)
for y in [0,1]: cyl('spring seat',(0,y-.012,0),(0,y+.012,0),.145,iron,spring,32)
cyl('unit cable',(0,0,0),(0,1,0),.009,steel,cable,12)

# Editable rigid-joint animation study. Runtime retains ownership of timing/state;
# these source clips document the authored clearances and never schedule gameplay.
def stretch(o,a,b):
    inv=o.parent.matrix_world.inverted(); p=inv@a; q=inv@b; d=q-p
    o.location=p; o.rotation_mode='QUATERNION'; o.rotation_quaternion=d.to_track_quat('Z','Y')
    o.scale=(1,1,max(.001,d.length))

for frame,extension,cock,load,heading,release in [
    (1,0,0,0,0,False),(40,1,0,0,0,False),(55,1,1,0,0,False),
    (65,1,1,1,0,False),(80,1,1,0,0,True),(100,1,1,0,0,False),
    (110,1,1,1,0,False),(140,1,1,1,pi/2,False),(170,1,1,1,pi,False),
    (200,1,1,1,0,False),(240,0,0,0,0,False)]:
    slide.location=v((0,.12+.28*extension,-.65-1.07*extension))
    yaw.rotation_euler=(0,0,heading)
    middle.location=v((0,0,.68*extension)); tip.location=v((0,0,.64*extension))
    support.location=v((0,0,.74*extension))
    angle=(45 if release else 24*extension*(1-cock)-5*cock)*pi/180
    arm.rotation_euler=(-angle,0,0); drum.rotation_euler=(angle*9,0,0)
    rounds[4].location=v((0,.165,4*.112)).lerp(v((0,.06,.77)),load)
    feed.location=v((0,-.07*sin(load*pi),0))
    bpy.context.view_layer.update()
    stretch(spring,bpy.data.objects['SpringFoot'].matrix_world.translation,bpy.data.objects['SpringTop'].matrix_world.translation)
    stretch(cable,bpy.data.objects['CableSwivel'].matrix_world.translation,bpy.data.objects['CableEye'].matrix_world.translation)
    for o in [slide,yaw,middle,tip,support,arm,drum,rounds[4],feed,spring,cable]:
        for prop in ['location','scale','rotation_quaternion' if o.rotation_mode=='QUATERNION' else 'rotation_euler']:
            o.keyframe_insert(data_path=prop,frame=frame)
scene=bpy.context.scene; scene.frame_end=240
for name,frame in [('Nested',1),('Extended',40),('Winched',55),('Loaded',65),('45 degree stop',80),('Reset',110),('Side aim',140),('Rear aim',170),('Stowed',240)]:
    scene.timeline_markers.new(name,frame=frame)
scene.frame_set(65)
root['authoring'] = 'Original geometry; user TS-210 Cata 5 / Winch / Cata 5 firecracker direction.'
root['axes'] = 'Blender +Z up / +Y forward; Godot +Y up / -Z forward.'
root['runtime'] = 'Named joints driven by SalvoCatapult. No physics/gravity-owned ammunition.'
bpy.context.scene.render.engine='CYCLES'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source'/'SalvoCatapult.blend'))
scene.frame_set(1)
for o in list(bpy.data.objects): o.animation_data_clear()
# Batch only static mesh siblings under a single rigid joint; retain editable master above.
for parent in [o for o in bpy.data.objects if o.type=='EMPTY']:
    meshes=[c for c in parent.children if c.type=='MESH']
    if len(meshes)<2: continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:o.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0]; bpy.ops.object.join()
    bpy.context.object.name=parent.name+'_Surfaces'
bpy.ops.export_scene.gltf(filepath=str(ROOT/'SalvoCatapult.glb'),export_format='GLB',export_yup=True,export_apply=True,export_animations=False)
print('Salvo catapult exported:', ROOT/'SalvoCatapult.glb')
