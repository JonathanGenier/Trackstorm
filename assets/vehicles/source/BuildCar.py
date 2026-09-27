"""Author Trackstorm's articulated Car in Blender; metres, +Y forward, +Z up.

Run with Blender --background --python assets/vehicles/source/BuildCar.py.
The saved master contains editable geometry, semantic collections and deployment keys.
"""
from pathlib import Path
from math import sin, cos, pi
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
bpy.context.preferences.filepaths.save_version = 0

def material(name, color, metal=0, rough=.6):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    return m

red = material('Car_OxideRed', (.24,.045,.026), .65)
steel = material('Car_WornSteel', (.17,.135,.10), .8, .45)
dark = material('Car_Graphite', (.025,.032,.032), .6)
rubber = material('Car_Rubber', (.023,.027,.025), 0, .92)
chrome = material('Car_PistonChrome', (.38,.42,.43), .9, .23)
glass = material('Car_ArmoredGlass', (.018,.038,.042), .65, .22)
cream = material('Car_Stencil', (.65,.59,.43), .15, .78)
amber = material('Light_Indicator', (.8,.24,.025), .1, .3)
white = material('Light_Headlight', (.85,.73,.48), .1, .25)
tail = material('Light_Running', (.45,.012,.008), .1, .3)
brake = material('Light_Brake', (.65,.018,.008), .1, .3)
reverse = material('Light_Reverse', (.68,.75,.75), .1, .3)

def empty(name, pos=(0,0,0), parent=None):
    o=bpy.data.objects.new(name,None); scene.collection.objects.link(o)
    o.location=pos; o.parent=parent; o.empty_display_size=.12
    return o

root=empty('Car')
def finish(o,name,mat,parent=root,bevel=0):
    o.name=name; o.data.name=name+'_Mesh'; o.data.materials.append(mat)
    o.parent=parent
    if bevel:
        mod=o.modifiers.new('Machined edge bevel','BEVEL'); mod.width=bevel; mod.segments=2
        mod=o.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
    return o

def box(name,pos,size,mat=steel,parent=root,bevel=.015):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pos)
    o=bpy.context.object; o.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat,parent,bevel)

def mesh(name,verts,faces,mat=red,parent=root,bevel=.012):
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    o=bpy.data.objects.new(name,data); scene.collection.objects.link(o)
    return finish(o,name,mat,parent,bevel)

def rod(name,a,b,r=.025,mat=steel,parent=root,n=12):
    a,b=Vector(a),Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=r,depth=(b-a).length,location=(a+b)/2)
    o=bpy.context.object; o.rotation_mode='QUATERNION'; o.rotation_quaternion=(b-a).to_track_quat('Z','Y')
    return finish(o,name,mat,parent,.006)

def panel(name,points,mat=red,parent=root,thickness=.025):
    o=mesh(name,points,[tuple(range(len(points)))],mat,parent)
    sol=o.modifiers.new('Armor thickness','SOLIDIFY'); sol.thickness=thickness
    return o

def grille(name,a,b,steps,mat=steel,parent=root):
    # A rectangular lattice in the plane described by four corners.
    p,q,r,s=map(Vector,(a[0],a[1],b[0],b[1]))
    for i in range(steps+1):
        t=i/steps
        rod(name+'_V%02d'%i,p.lerp(q,t),r.lerp(s,t),.009,mat,parent,6)
    for i in range(6):
        t=i/5
        rod(name+'_H%02d'%i,p.lerp(r,t),q.lerp(s,t),.009,mat,parent,6)

# Frame and protected drivetrain are deliberately separate from the body panels.
for x in [-.51,.51]:
    box('FrameRail', (x,0,-.36),(.105,3.8,.16),dark)
for y in [-1.6,-.65,.55,1.55]:
    box('FrameCrossmember',(0,y,-.36),(1.2,.1,.12),steel)
box('BellySkid',(0,.05,-.44),(1.25,2.55,.055),dark)
rod('Driveshaft',(0,-1.4,-.42),(0,1.4,-.42),.065,dark)

# Coupe cabin: long bonnet, sloping windshield, short rear deck; no truck cab/bed.
box('CabinFloor',(0,0,-.25),(1.46,1.30,.18),dark)
box('Roof',(0,-.03,.94),(1.42,.96,.065),red,bevel=.035)
panel('Windshield',[(-.70,.44,.93),(.70,.44,.93),(.84,.92,.40),(-.84,.92,.40)],glass)
panel('RearWindow',[(-.7,-.51,.92),(.7,-.51,.92),(.83,-.91,.40),(-.83,-.91,.40)],glass)
grille('WindshieldGuard',[(-.69,.465,.93),(.69,.465,.93)],[(-.83,.945,.40),(.83,.945,.40)],14,dark)
grille('RearWindowGuard',[(-.69,-.535,.92),(.69,-.535,.92)],[(-.81,-.94,.41),(.81,-.94,.41)],12,dark)
for side in [-1,1]:
    x=side*.85
    panel('DoorArmor',[(x,-.58,-.32),(x,.64,-.32),(x,.75,.36),(x,-.69,.40)])
    panel('SideGlass',[(x,-.64,.44),(x,.67,.44),(side*.71,.40,.90),(side*.71,-.47,.90)],glass)
    for y0,y1,z0,z1 in [(.75,.42,.39,.94),(-.70,-.51,.4,.94)]:
        rod('CabinPillar',(x,y0,z0),(side*.71,y1,z1),.045,dark)
    rod('DoorSill',(x,-.69,-.34),(x,.74,-.34),.055,steel)
    rod('WindowBelt',(x,-.71,.41),(x,.77,.41),.034,steel)
    for i in range(9):
        y=-.53+i*.125
        rod('SideWindowBar',(side*.87,y,.46),(side*.73,y*.69,.88),.012,dark,n=8)
    for i in range(4):
        z=.50+i*.095; t=(z-.44)/.46
        rod('SideWindowCross',(side*(.865-.14*t),-.61+.14*t,z),(side*(.865-.14*t),.64-.25*t,z),.01,dark,n=6)
    box('DoorHandle',(side*.88,-.39,.27),(.055,.17,.035),chrome)
    box('DoorHinge',(side*.88,.52,.12),(.055,.06,.14),steel)
    rod('RockSlider',(side*.96,-.73,-.40),(side*.96,.75,-.40),.052,steel)
    rod('SideExhaust',(side*.91,-.65,-.29),(side*.91,.64,-.29),.055,chrome)
    for yy in [-.60,.56]:
        rod('SliderMount',(side*.5,yy,-.34),(side*.96,yy,-.4),.025,steel)
    box('MirrorArm',(side*.96,.64,.48),(.27,.035,.045),dark)
    box('ArmoredMirror',(side*1.08,.62,.5),(.15,.12,.11),red)
    # Rivet rows and door seams establish scale without baking opaque decoration.
    for yy in [-.54,-.34,-.14,.06,.26,.46,.61]:
        for zz in [-.27,.34]:
            rod('DoorRivet',(side*.867,yy,zz),(side*.883,yy,zz),.016,chrome,n=8)

panel('Bonnet',[(-.83,.94,.40),(.83,.94,.40),(.91,2.12,.27),(-.91,2.12,.27)])
box('HoodScoop',(0,1.19,.46),(.60,.47,.115),dark,bevel=.025)
box('ScoopMouth',(0,1.435,.46),(.49,.02,.06),steel)
for i in range(7):
    box('ScoopVent',(-.22+i*.073,1.449,.46),(.018,.025,.05),dark,bevel=.002)
for x in [-.76,.76]:
    rod('BonnetRail',(x,.9,.435),(x,2.09,.31),.022,steel)
    box('HoodLatch',(x,1.91,.33),(.065,.14,.035),chrome)

# Raised arched fenders leave an actual opening, not a wheel intersecting a box.
for side in [-1,1]:
    for y in [-1.3005525,1.3005525]:
        verts=[]
        for j in range(19):
            a=.20+(pi-.40)*j/18
            for x,r in [(side*.84,.85),(side*1.25,.85),(side*1.25,.92),(side*.84,.92)]:
                verts.append((x,y+cos(a)*r,-.29+sin(a)*r))
        faces=[]
        for j in range(18):
            for k in range(4): faces.append((j*4+k,j*4+(k+1)%4,(j+1)*4+(k+1)%4,(j+1)*4+k))
        mesh('ArmoredWheelArch',verts,faces,red)
        for j in range(1,18,2):
            a=.20+(pi-.40)*j/18
            rod('FenderBolt',(side*1.255,y+cos(a)*.89,-.29+sin(a)*.89),(side*1.27,y+cos(a)*.89,-.29+sin(a)*.89),.018,chrome,n=8)

# Front fascia, recessed lamps, grille and tubular push bar.
box('FrontFascia',(0,2.13,.07),(1.83,.13,.40),red)
box('Radiator',(0,2.21,.09),(.96,.04,.31),dark)
grille('RadiatorGuard',[(-.46,2.24,.24),(.46,2.24,.24)],[(-.46,2.24,-.06),(.46,2.24,-.06)],12)
for side in [-1,1]:
    x=side*.70
    rod('HeadlightBezel',(x,2.19,.09),(x,2.26,.09),.145,dark,n=32)
    rod('Headlight_'+('L' if side<0 else 'R'),(x,2.26,.09),(x,2.277,.09),.112,white,n=32)
    box('FrontIndicator',(x,2.26,-.12),(.19,.025,.042),amber,bevel=.008)
    for xx in [-.065,0,.065]: rod('LampGuard',(x+xx,2.294,-.005),(x+xx,2.294,.185),.007,steel,n=6)
    rod('PushBarUpright',(side*.49,2.28,-.47),(side*.49,2.31,.32),.037,steel)
    rod('RecoveryEye',(side*.72,2.25,-.30),(side*.72,2.37,-.30),.045,chrome)
rod('PushBarTop',(-.91,2.31,.30),(.91,2.31,.30),.04,steel)
rod('Bumper',(-1.08,2.28,-.26),(1.08,2.28,-.26),.055,steel)
panel('FrontSkid',[(-.47,2.27,-.17),(.47,2.27,-.17),(.40,1.94,-.56),(-.40,1.94,-.56)],steel)
for x in [-.26,0,.26]:
    for y,z in [(2.25,-.23),(2.12,-.37),(2.0,-.50)]:
        # Dark recessed perforation faces sit on the sloped skid.
        o=rod('SkidRecess',(x,y,z),(x,y+.009,z+.008),.038,dark,n=12)

# Independent roof/auxiliary lamp elements.
rod('RoofLightBar',(-.69,.40,1.025),(.69,.40,1.025),.03,dark)
for i,x in enumerate([-.54,-.18,.18,.54]):
    box('RoofLampHousing_%d'%i,(x,.40,1.09),(.22,.16,.15),dark,bevel=.025)
    box('RoofAuxLight_%d'%i,(x,.488,1.09),(.17,.02,.10),white,bevel=.02)

# Rear bay has real inner walls, floor and independently hinged split lids.
box('RearBayFloor',(0,-1.47,-.30),(1.56,1.28,.07),dark)
box('RearBayFrontWall',(0,-.83,.0),(1.58,.07,.56),dark)
box('RearFascia',(0,-2.14,.01),(1.9,.13,.54),red)
for side in [-1,1]:
    box('RearBaySideWall',(side*.78,-1.47,-.03),(.065,1.25,.5),dark)
    lid=empty('TrunkHinge_'+('L' if side<0 else 'R'),(side*.81,-1.48,.36),root)
    box('TrunkLid_'+('L' if side<0 else 'R'),(-side*.398,0,0),(.785,1.22,.055),red,lid,.025)
    for yy in [-.50,.50]:
        box('TrunkReinforcement',(-side*.4,yy,.035),(.76,.055,.035),steel,lid)
        rod('TrunkHingePin',(0,yy-.06,0),(0,yy+.06,0),.032,chrome,lid)
    for frame,ang in [(1,0),(25,side*1.70),(75,side*1.70),(100,0)]:
        lid.rotation_euler.y=ang; lid.keyframe_insert(data_path='rotation_euler',frame=frame)
    lid.rotation_euler.y=0
    for i,mat in enumerate([tail,brake,reverse]):
        box('RearLampHousing',(side*(.67+i*.115),-2.224,.08),(.10,.055,.32),dark)
        box(['TailRunning','Brake','Reverse'][i]+'_'+('L' if side<0 else 'R'),(side*(.67+i*.115),-2.256,.08),(.058,.019,.26),mat,bevel=.02)
    box('RearIndicator_'+str(side),(side*.89,-2.256,-.15),(.11,.02,.045),amber)
rod('RearBumper',(-1.08,-2.29,-.28),(1.08,-2.29,-.28),.055,steel)
box('RearPlate',(0,-2.223,-.12),(.32,.025,.10),dark)

# Two hydraulic columns raise the cross-braced rack vertically out of the bay.
rack=empty('WeaponRack',(0,-1.47,-.08),root)
for side in [-1,1]:
    x=side*.51
    rod('LiftCylinder_'+str(side),(x,-1.47,-.26),(x,-1.47,.12),.083,dark)
    rod('LiftCollar_'+str(side),(x,-1.47,.07),(x,-1.47,.13),.10,steel)
    rod('LiftPiston_'+str(side),(x,0,-.17),(x,0,.04),.045,chrome,rack)
    box('RackRail',(x,0,.055),(.095,1.00,.10),steel,rack)
    rod('HydraulicFeed',(x+.1,-1.47,-.27),(x+.1,-1.47,.08),.017,dark)
for y in [-.40,0,.40]: box('RackCrossmember',(0,y,.06),(1.12,.065,.075),steel,rack)
rod('RackBraceA',(-.51,-.42,.02),(.51,.42,.02),.02,dark,rack)
rod('RackBraceB',(.51,-.42,.02),(-.51,.42,.02),.02,dark,rack)
for side in [-1,1]:
    for yy in [-.30,.30]:
        box('WeaponSocketPlate',(side*.36,yy,.13),(.24,.20,.06),dark,rack)
        empty('WeaponMount_'+('L' if side<0 else 'R')+('_Front' if yy>0 else '_Rear'),(side*.36,yy,.17),rack)
for frame,z in [(1,-.08),(25,-.08),(48,.82),(60,.82),(75,-.08),(100,-.08)]:
    rack.location.z=z; rack.keyframe_insert(data_path='location',frame=frame)
rack.location.z=-.08

# Four steering carriers, tire spin pivots, and separate suspension attachment points.
for idx,(side,y) in enumerate([(-1,1.3005525),(1,1.3005525),(-1,-1.3005525),(1,-1.3005525)]):
    name=['FL','FR','RL','RR'][idx]
    carrier=empty('WheelCarrier_'+name,(side*1.03,y,-.565),root)
    wheel=empty('WheelSpin_'+name,parent=carrier)
    # Rounded tire carcass, oriented around local X.
    bpy.ops.mesh.primitive_torus_add(major_radius=.405,minor_radius=.15,major_segments=48,minor_segments=12)
    o=bpy.context.object; o.rotation_euler.y=pi/2
    finish(o,'Tire_'+name,rubber,wheel)
    for j in range(36):
        a=2*pi*j/36
        for row in [-1,0,1]:
            t=a+row*.027
            tread=box('Tread_'+name,(row*.105,sin(t)*.548,cos(t)*.548),(.10,.093,.067),rubber,wheel,.008)
            tread.rotation_euler.x=-t; tread.rotation_euler.z=row*.22
    rod('WheelRim_'+name,(-.15,0,0),(.15,0,0),.29,dark,wheel,32)
    rod('Beadlock_'+name,(side*.155,0,0),(side*.175,0,0),.31,steel,wheel,40)
    rod('RecessedRim_'+name,(side*.177,0,0),(side*.181,0,0),.26,dark,wheel,32)
    rod('Hub_'+name,(side*.182,0,0),(side*.205,0,0),.11,steel,wheel,16)
    for j in range(12):
        a=2*pi*j/12
        rod('BeadBolt',(side*.18,sin(a)*.283,cos(a)*.283),(side*.19,sin(a)*.283,cos(a)*.283),.011,chrome,wheel,8)
    for j in range(6):
        a=2*pi*j/6
        spoke=box('WheelSpoke',(side*.18,sin(a)*.175,cos(a)*.175),(.02,.045,.16),steel,wheel,.004)
        spoke.rotation_euler.x=-a
    for suffix,yy,zz in [('A',-.18,-.32),('B',.18,-.32),('Upper',0,.19)]:
        empty('SuspensionAnchor_'+name+'_'+suffix,(side*.48,y+yy,zz),root)
        # Meshes authored along local +Z, runtime aligns their endpoints.
        rod('SuspensionLink_'+name+'_'+suffix,(0,0,0),(0,0,1),.028 if suffix!='Upper' else .055,steel if suffix!='Upper' else dark,root)
    rod('ShockRod_'+name,(0,0,0),(0,0,1),.021,chrome,root)

# Original radial stencil, matching the reference set's weathered mechanical emblem.
def emblem(name,pos,axis,size,parent=root):
    e=empty(name,pos,parent)
    if axis=='side': e.rotation_euler.y=pi/2
    for j in range(12):
        a=j*pi/6
        o=box('StencilRay',(sin(a)*size*.75,cos(a)*size*.75,.002),(size*.09,size*.25,.002),cream,e,0)
        o.rotation_euler.z=-a
    bpy.ops.mesh.primitive_torus_add(major_radius=size*.49,minor_radius=size*.035,major_segments=32,minor_segments=6)
    finish(bpy.context.object,'StencilRing',cream,e)
    for x in [-1,1]:
        box('StencilEye',(x*size*.15,size*.07,.005),(size*.10,size*.17,.005),cream,e,0)
    box('StencilJaw',(0,-size*.15,.005),(size*.29,size*.11,.005),cream,e,0)
emblem('HoodEmblem',(0,1.76,.322),'top',.31)
emblem('RoofEmblem',(0,-.08,.977),'top',.30)
for side in [-1,1]: emblem('DoorEmblem',(side*.884,.03,.045),'side',.29)

# Small existing per-player color/damage-flash surface, separate from authored paint.
box('Identification',(0,-.38,.980),(.42,.11,.012),cream)
# Batch static detail and each rotating wheel by material. Lamps, mechanisms,
# attachment empties and the identification surface remain individually addressable.
groups={}
for o in list(scene.objects):
    if o.type!='MESH' or o.name=='Identification': continue
    if o.data.materials[0].name.startswith('Light_'): continue
    if o.name.startswith(('SuspensionLink_','ShockRod_','LiftPiston_')): continue
    p=o.parent
    if p==root or (p and p.name.startswith('WheelSpin_')):
        groups.setdefault((p,o.data.materials[0]),[]).append(o)
for (parent,mat),objects in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        bpy.context.view_layer.objects.active=o; o.select_set(True)
        for mod in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    objects[0].name=('Chassis_' if parent==root else parent.name+'_')+mat.name

# UV unwrap each mesh; retained edit geometry stays separate with deterministic names.
for o in list(scene.objects):
    if o.type!='MESH': continue
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active=o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.02)
    bpy.ops.object.mode_set(mode='OBJECT')

scene.frame_start=1; scene.frame_end=100; scene.render.fps=30; scene.frame_set(1)
for o in scene.objects:
    if o.animation_data and o.animation_data.action:
        o.animation_data.action.name='Deployment_'+o.name
scene['Authoring']='TS-164 original Blender geometry; all ten Jira references inspected. +Y forward.'
scene['Deployment']='Frames 1-25 lids open, 25-48 rack lifts, 60-75 retract, 75-100 close.'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/TrackstormCar.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT/'TrackstormCar.glb'),export_format='GLB',export_yup=True,export_animations=False,export_apply=True)
print('TS164_ASSET_EXPORTED',sum(len(o.data.polygons) for o in scene.objects if o.type=='MESH'))
