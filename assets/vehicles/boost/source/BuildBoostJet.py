"""Blender-authored rear jet, linked to the unmodified production Car for fit.

Metres; Blender +Y forward, +Z up. Only Boost hierarchy is exported, never the Car.
"""
from pathlib import Path
from math import sin, cos, pi
import json, hashlib
import bpy
from mathutils import Vector

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
root = empty('BoostJet')
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

# Rear fascia is y=-2.205, lamps start |x|=.62. Lids/rack end at y=-2.09/-1.97.
mount=empty('Mount',(0,-2.36,.055),root)
for x in [-.395,.395]:
    box('FasciaBracket',(x,.06,0),(.105,.22,.65),paint,mount)
    for z in [-.25,.25]: rod('AnchorBolt',(x,-.073,z),(x,-.099,z),.033,steel,mount)
    box('BumperTie',(x,-.02,-.27),(.09,.13,.15),dark,mount)
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
for parent in [mount,sleeve,nozzle]:
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
    sleeve.location.y=-.35*p;nozzle.location.y=-.72*p
    sleeve.keyframe_insert(data_path='location',frame=frame);nozzle.keyframe_insert(data_path='location',frame=frame)
    for name in ['Ram_L','Ram_R']:
        ram=bpy.data.objects[name];ram.scale.y=1+.72*p/.26;ram.keyframe_insert(data_path='scale',frame=frame)
scene.frame_start=1;scene.frame_end=85;scene.frame_set(1)
scene['Boost contract']='Mount fixed; Sleeve moves -0.35 Y, Nozzle -0.72 Y; Outlet is flame origin. Runtime presentation only.'
scene['Clearance']='All Boost geometry rear of fascia y=-2.205; central width <0.9m, outside lamps. Rack/lid paths remain forward of -2.09.'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/BoostJet.blend'))
bpy.ops.object.select_all(action='DESELECT')
for o in collection.objects:o.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'BoostJet.glb'),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
manifest={'author':'Original Trackstorm project work','tool':bpy.app.version_string,'source':'source/BoostJet.blend','export':'BoostJet.glb','reference':'../source/TrackstormCar.blend','reference_sha256':hashlib.sha256(CAR.read_bytes()).hexdigest(),'coordinates':'metres; Blender +Y forward/+Z up; Godot -Z forward/+Y up','deployment':{'sleeve_rearward_m':.35,'nozzle_rearward_m':.72,'outlet_stowed_godot':[0,.055,2.875],'outlet_deployed_godot':[0,.055,3.595]},'no_car_geometry_exported':True,'sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [ROOT/'BoostJet.glb',ROOT/'source/BoostJet.blend']}}
(ROOT/'sources.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('BOOST_AUTHORING_COMPLETE',json.dumps(manifest),flush=True)
