"""Blender-authored rear jet, linked to the unmodified production Car for fit.

Metres; Blender +Y forward, +Z up. Only Boost hierarchy is exported, never the Car.
"""
from pathlib import Path
from math import sin, cos, pi
import json, hashlib
import bpy
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[1]
CAR = ROOT.parent / 'source/TrackstormCar.blend'
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
bpy.context.preferences.filepaths.save_version = 0
# A library reference retains the actual vehicle as editable fit context without a copied car.
reference = bpy.data.collections.new('Production Car - linked fit reference')
scene.collection.children.link(reference)
with bpy.data.libraries.load(str(CAR), link=True, relative=True) as (src, dst):
    dst.objects = src.objects
for obj in dst.objects:
    if obj: reference.objects.link(obj)

collection = bpy.data.collections.new('Boost Jet - export only')
scene.collection.children.link(collection)
def empty(name, pos=(0,0,0), parent=None):
    o = bpy.data.objects.new(name, None); collection.objects.link(o)
    o.parent = parent; o.location = pos; o.empty_display_size = .1
    return o
root = empty('BoostJet',(0,-1.47,-.08)) # Fit at the production rack's stowed origin.
def material(name, color, metal, rough):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    return m
steel=material('Boost_Titanium',(.17,.20,.22),.8,.38)
dark=material('Boost_Carbon',(.027,.032,.038),.55,.63)
bronze=material('Boost_HeatBronze',(.28,.135,.05),.8,.4)
paint=material('Boost_OxideArmor',(.23,.045,.024),.65,.52)
chrome=material('Boost_RamChrome',(.42,.47,.51),.9,.2)
hot=material('Boost_Throat',(.05,.09,.16),.55,.4)

def finish(o,name,mat,parent=root,bevel=0):
    o.name=name; o.data.materials.append(mat)
    for c in list(o.users_collection): c.objects.unlink(o)
    collection.objects.link(o); o.parent=parent
    if bevel:
        mod=o.modifiers.new('Machined edges','BEVEL'); mod.width=bevel; mod.segments=2
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return o
def box(name,pos,size,mat=steel,parent=root):
    bpy.ops.mesh.primitive_cube_add(size=1,location=pos); o=bpy.context.object; o.scale=size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat,parent,.012)
def tube(name,profile,mat,parent=root,n=48):
    # Ring profile is (rearward distance, radius); return along inner wall to make an open nozzle.
    verts=[]; faces=[]
    for y,r in profile:
        for k in range(n):
            a=k*2*pi/n; verts.append((r*cos(a),-y,r*sin(a)))
    for j in range(len(profile)-1):
        for k in range(n): faces.append((j*n+k,j*n+(k+1)%n,(j+1)*n+(k+1)%n,(j+1)*n+k))
    data=bpy.data.meshes.new(name); data.from_pydata(verts,[],faces); data.update()
    o=bpy.data.objects.new(name,data); collection.objects.link(o); o.parent=parent; data.materials.append(mat)
    for p in data.polygons:p.use_smooth=True
    return o
def rod(name,a,b,r,mat,parent=root):
    a,b=Vector(a),Vector(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=r,depth=(b-a).length,location=(a+b)/2)
    o=bpy.context.object; o.rotation_mode='QUATERNION'; o.rotation_quaternion=(b-a).to_track_quat('Z','Y')
    return finish(o,name,mat,parent,.004)

# Approved concept: forward-facing intake above the rack, suspended engine below it.
mount=empty('Mount',(0,.12,-.46),root)
intake=empty('Intake',(0,0,0),root)
verts=[];faces=[]
for y,w,h,r in [(-.29,.66,.26,.06),(.39,.74,.32,.065),(.39,.65,.23,.04),(-.24,.57,.17,.035)]:
    for cx,cz,start in [(w/2-r,h/2-r,0),(-w/2+r,h/2-r,pi/2),(-w/2+r,-h/2+r,pi),(w/2-r,-h/2+r,3*pi/2)]:
        for k in range(5):
            a=start+k*pi/8;verts.append((cx+r*cos(a),y,.325+cz+r*sin(a)))
for j in range(3):
    for k in range(20): faces.append((j*20+k,(j+1)*20+k,(j+1)*20+(k+1)%20,j*20+(k+1)%20))
faces.extend([tuple(range(19,-1,-1)),tuple(range(60,80))])
data=bpy.data.meshes.new('RoundedIntakeShell');data.from_pydata(verts,[],faces);data.materials.append(paint);data.materials.append(dark)
obj=bpy.data.objects.new('RoundedIntakeShell',data);collection.objects.link(obj);obj.parent=intake
for p in data.polygons:
    p.material_index=1 if 40<=p.index<60 or p.index==61 else 0
for x in [-.345,.345]:
    box('IntakeMountFoot',(x,.02,.145),(.12,.55,.05),steel,intake)
    for y in [-.18,.23]: rod('IntakeBolt',(x,y,.18),(x,y,.205),.018,chrome,intake)
box('IntakeDepth',(0,.24,.325),(.64,.022,.225),dark,intake)
for z in [.235,.295,.355,.415]:
    rod('IntakeGrille',(-.31,.39,z),(.31,.39,z),.013,steel,intake)
for x in [-.29,.29]:
    for z in [.225,.425]: rod('RearPanelFastener',(x,-.292,z),(x,-.306,z),.016,chrome,intake)

def pipe(name,points,r,mat,parent):
    curve=bpy.data.curves.new(name,'CURVE');curve.dimensions='3D';curve.resolution_u=8
    curve.bevel_depth=r;curve.bevel_resolution=2
    spline=curve.splines.new('BEZIER');spline.bezier_points.add(len(points)-1)
    for p,co in zip(spline.bezier_points,points):
        p.co=co;p.handle_left_type='AUTO';p.handle_right_type='AUTO'
    obj=bpy.data.objects.new(name,curve);collection.objects.link(obj);obj.parent=parent
    curve.materials.append(mat)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.object.convert(target='MESH')
    return bpy.context.object

# Visible swept duct bypasses the rack crossmembers, joining the compressor at the front.
pipe('IntakeDuct',[(-.29,.06,.28),(-.44,.06,.28),(-.44,.18,.04),(-.34,.32,-.27),(0,.35,-.46)],.092,dark,intake)
for z in [-.05,.035,.12]:
    rod('DuctClamp',(-.54,.15,z),(-.34,.15,z),.014,steel,intake)
for x in [-.37,.37]:
    for y in [-.22,.22]:
        box('RackAnchor',(x,y,.09),(.16,.15,.055),steel,root)
        rod('AnchorBolt',(x,y,.11),(x,y,.145),.023,chrome,root)
        rod('SuspensionBrace',(x,y,.065),(x,y,-.44),.033,steel,root)
        rod('BraceSleeve',(x,y,-.12),(x,y,-.28),.046,dark,root)
        box('EngineClevis',(x,y,-.43),(.13,.12,.09),steel,root)
    rod('DiagonalBrace',(x,.25,.055),(x,-.25,-.40),.02,chrome,root)
    pipe('FeedLine',[(x,.28,-.04),(x*.86,.12,-.10),(x*.86,-.15,-.18),(x*.94,-.30,-.39)],.018,bronze,root)
    rod('FuelManifold',(x,.27,-.18),(x,-.10,-.18),.026,paint,root)
    for y in [.22,.08,-.06]: rod('ManifoldClamp',(x,y,-.145),(x,y,-.215),.034,steel,root)

tube('CompressorCasing',[(-.32,.29),(-.26,.35),(0,.355),(0,.29),(-.32,.29)],dark,mount)
tube('CompressorCap',[(-.325,0),(-.325,.29),(-.30,.31)],steel,mount)
for y in [-.26,-.19,-.12,-.05]:
    tube('CasingBand',[(y,.356),(y+.025,.356),(y+.025,.344),(y,.344)],steel,mount)
# Raised heat-shield strips leave dark channels between them instead of a blank shell.
for k in range(18):
    a=k*2*pi/18
    rod('HeatShieldRib',(.348*cos(a),.245,.348*sin(a)),(.348*cos(a),.045,.348*sin(a)),.012,bronze,mount)
tube('ArmoredCollar',[(0,.355),(.10,.41),(.22,.405),(.26,.365),(.26,.319),(.04,.319),(0,.355)],paint,mount)
tube('CollarRim',[(.205,.412),(.25,.412),(.27,.366),(.255,.352),(.205,.397)],steel,mount)
for k in range(12):
    a=k*2*pi/12
    rod('CollarBolt',(.378*cos(a),-.24,.378*sin(a)),(.378*cos(a),-.269,.378*sin(a)),.019,chrome,mount)

# Two nested sleeves and nozzle travel along local -Y. There is no pitch/sweep into the rack.
sleeve=empty('Sleeve',(0,0,0),mount)
tube('SlidingSleeve',[(.11,.312),(.36,.312),(.38,.292),(.38,.274),(.11,.274)],dark,sleeve)
for y in [.16,.22,.29,.35]: tube('CoolingRing',[(y,.316),(y+.018,.316),(y+.018,.305),(y,.305)],steel,sleeve)
nozzle=empty('Nozzle',(0,0,0),mount)
tube('InnerBarrel',[(-.10,.226),(.36,.226),(.36,.207),(-.10,.207)],dark,nozzle)
tube('NozzleBody',[(.25,.266),(.36,.289),(.49,.31),(.51,.30),(.51,.258),(.37,.234),(.25,.23)],bronze,nozzle)
tube('Throat',[(.25,.227),(.40,.249),(.49,.252)],hot,nozzle)
for k in range(12):
    a=(k+.5)*2*pi/12
    # Separate tapered ceramic petals, authored as trapezoids around the bell.
    verts=[]
    for y,r in [(.35,.291),(.51,.314)]:
        for off in [-.19,.19]: verts.append((r*cos(a+off),-y,r*sin(a+off)))
    data=bpy.data.meshes.new('Petal');data.from_pydata(verts,[],[(0,1,3,2)]);data.materials.append(steel)
    o=bpy.data.objects.new('NozzlePetal',data);collection.objects.link(o);o.parent=nozzle
    sol=o.modifiers.new('Petal thickness','SOLIDIFY');sol.thickness=.012
for x in [-.35,.35]:
    box('RamClevis',(x*.88,-.43,-.15),(.12,.06,.10),steel,nozzle)
    rod('HydraulicSleeve',(x,-.07,-.15),(x,-.30,-.15),.034,dark,mount)
    ram=empty('Ram_L' if x<0 else 'Ram_R',(x,-.17,-.15),mount)
    rod('HydraulicRod',(0,0,0),(0,-.26,0),.019,chrome,ram)
outlet=empty('Outlet',(0,-.515,0),nozzle)

# Recessed rotating turbine and hub; stationary outlet braces remain on the nozzle.
rotor=empty('Turbine',(0,-.355,0),nozzle)
tube('TurbineRim',[(0,.232),(.035,.232),(.035,.214),(0,.214),(0,.232)],steel,rotor)
tube('TurbineHub',[(-.035,.075),(.075,.105),(.185,.035),(.215,0)],chrome,rotor)
for k in range(16):
    a=k*2*pi/16
    verts=[]
    for r,angle,y in [(.073,a,-.01),(.215,a+.15,-.025),(.215,a+.36,.025),(.073,a+.21,.045)]:
        verts.append((r*cos(angle),-y,r*sin(angle)))
    data=bpy.data.meshes.new('TwistedBlade');data.from_pydata(verts,[],[(0,1,2,3)]);data.materials.append(steel if k%2 else chrome)
    obj=bpy.data.objects.new('TurbineBlade',data);collection.objects.link(obj);obj.parent=rotor
    mod=obj.modifiers.new('Blade thickness','SOLIDIFY');mod.thickness=.008
for k in range(3):
    a=(k+.25)*2*pi/3
    rod('OutletStator',(.245*cos(a),-.49,.245*sin(a)),(.076*cos(a),-.49,.076*sin(a)),.012,chrome,nozzle)
    rod('StatorFastener',(.245*cos(a),-.475,.245*sin(a)),(.245*cos(a),-.50,.245*sin(a)),.022,steel,nozzle)
tube('BearingRing',[(.475,.081),(.497,.081),(.497,.065),(.475,.065),(.475,.081)],bronze,nozzle)
# Fuller outlet matches the concept while the approved flame shell keeps its original size.
for obj in list(nozzle.children):
    if obj.type=='MESH' and not obj.name.startswith('RamClevis'):
        obj.matrix_basis=Matrix.Diagonal((1.18,1,1.18,1)) @ obj.matrix_basis
rotor.scale=(1.18,1,1.18)

# Authored VFX shell: longitudinal/circumferential UVs, slightly lobed radial silhouette.
verts=[];faces=[];rings=36;sides=24
for j in range(rings+1):
    t=j/rings; radius=(.25+.34*sin(pi*t))*(1-.98*t)
    for k in range(sides+1):
        a=2*pi*k/sides; r=radius*(1+.07*sin(a*5+t*13)*t)
        verts.append((r*cos(a),-3.4*t,r*sin(a)))
for j in range(rings):
    for k in range(sides):
        i=j*(sides+1)+k;faces.append((i,i+1,i+sides+2,i+sides+1))
data=bpy.data.meshes.new('FlameSurface');data.from_pydata(verts,[],faces);data.update()
uv=data.uv_layers.new(name='FlowUV')
for poly in data.polygons:
    poly.use_smooth=True
    for li in poly.loop_indices:
        vi=data.loops[li].vertex_index;uv.data[li].uv=(vi%(sides+1)/sides,vi//(sides+1)/rings)
flame=bpy.data.objects.new('FlameSurface',data);collection.objects.link(flame);flame.parent=outlet;data.materials.append(hot)

# UV preparation for all hard-surface meshes; VFX flow UVs are deliberately excluded.
for o in list(collection.objects):
    if o.type!='MESH' or o==flame:continue
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
# Batch static surfaces under each moving root by material, retaining all pivots and flow mesh.
for parent in [root,intake,mount,sleeve,nozzle,rotor]:
    for mat in [steel,dark,bronze,paint,chrome,hot]:
        objects=[o for o in parent.children if o.type=='MESH' and o.data.materials[0]==mat]
        if not objects:continue
        for o in objects:
            bpy.context.view_layer.objects.active=o
            for mod in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
        bpy.ops.object.select_all(action='DESELECT')
        for o in objects:o.select_set(True)
        bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();objects[0].name=parent.name+'_'+mat.name

for frame,p in [(1,0),(12,1),(65,1),(85,0)]:
    sleeve.location.y=-.14*p;nozzle.location.y=-.28*p
    sleeve.keyframe_insert(data_path='location',frame=frame);nozzle.keyframe_insert(data_path='location',frame=frame)
    for name in ['Ram_L','Ram_R']:
        ram=bpy.data.objects[name];ram.scale.y=1+.28*p/.26;ram.keyframe_insert(data_path='scale',frame=frame)
scene.frame_start=1;scene.frame_end=85;scene.frame_set(1)
scene['Boost contract']='Rack-local mount; Sleeve moves -0.14 Y, Nozzle -0.28 Y. Selection raises rack then extends barrel; accepted use alone ignites.'
scene['Clearance']='Root at stowed WeaponRack origin for fit; exported at zero for parenting to WeaponRack. Rack opens before hardware rises; barrel nests before lowering.'
scene['Approved concept']='reference/ApprovedBoosterConcept.png; intake faces Blender +Y / Godot -Z; engine under rack; Turbine spins about local exhaust axis.'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/BoostJet.blend'))
root.location=(0,0,0)
bpy.ops.object.select_all(action='DESELECT')
for o in collection.objects:o.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'BoostJet.glb'),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
manifest={'author':'Original Trackstorm project work','tool':bpy.app.version_string,'source':'source/BoostJet.blend','export':'BoostJet.glb','reference':'../source/TrackstormCar.blend','reference_sha256':hashlib.sha256(CAR.read_bytes()).hexdigest(),'concept':{'path':'reference/ApprovedBoosterConcept.png','provenance':'User-approved built-in imagegen sketch supplied 2026-09-28; design reference only','sha256':hashlib.sha256((ROOT/'reference/ApprovedBoosterConcept.png').read_bytes()).hexdigest()},'coordinates':'metres; Blender +Y forward/+Z up; Godot -Z forward/+Y up; export relative to WeaponRack','deployment':{'sleeve_rearward_m':.14,'nozzle_rearward_m':.28,'outlet_stowed_rack_local_godot':[0,-.46,.395],'outlet_deployed_rack_local_godot':[0,-.46,.675]},'no_car_geometry_exported':True,'sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT/'BoostJet.glb',ROOT/'source/BoostJet.blend']}}
(ROOT/'sources.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('BOOST_AUTHORING_COMPLETE',json.dumps(manifest),flush=True)
