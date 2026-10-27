"""Original Twin Fang game geometry. Godot coordinates: Y up, -Z forward."""
from pathlib import Path
import math, json, hashlib
import bpy
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parents[2]
GUN_WIDTH_HEIGHT = 1.344
BARREL_LENGTH = 1.755
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
collection = bpy.data.collections.new('Twin Fang production')
bpy.context.scene.collection.children.link(collection)
def point(v): return Vector((v[0], -v[2], v[1]))
def material(name, color, metal, rough):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Metallic'].default_value=metal; p.inputs['Roughness'].default_value=rough
    return m
steel=material('Fang_Steel',(.23,.26,.28),.8,.43)
iron=material('Fang_Iron',(.055,.064,.072),.75,.48)
edge=material('Fang_Edges',(.40,.43,.44),.82,.36)
brass=material('Fang_Brass',(.61,.36,.10),.75,.36)
dark=material('Fang_Recess',(.013,.018,.022),.3,.68)
red=material('Fang_Red',(.36,.025,.022),.6,.54)
def node(name,parent=None,pos=(0,0,0)):
    o=bpy.data.objects.new(name,None); collection.objects.link(o); o.parent=parent; o.location=point(pos); return o
def finish(o,name,mat,parent,bevel=0):
    o.name=name
    for c in list(o.users_collection): c.objects.unlink(o)
    collection.objects.link(o); o.parent=parent; o.data.materials.append(mat)
    if bevel:
        m=o.modifiers.new('Edge bevel','BEVEL'); m.width=bevel; m.segments=2
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return o
def box(name,pos,size,mat,parent,bevel=.004):
    bpy.ops.mesh.primitive_cube_add(size=1,location=point(pos)); o=bpy.context.object; o.scale=(size[0],size[2],size[1])
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(o,name,mat,parent,bevel)
def pin(name,a,b,r,mat,parent,n=20):
    a,b=point(a),point(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=n,radius=r,depth=(b-a).length,location=(a+b)/2)
    o=bpy.context.object; o.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return finish(o,name,mat,parent,.002)
def tube(name,x,z0,z1,r,inside,mat,parent,n=24):
    verts=[]; faces=[]
    for z,rad in [(z0,r),(z1,r),(z0,inside),(z1,inside)]:
        for j in range(n):
            a=j*math.tau/n; verts.append(point((x+rad*math.cos(a),rad*math.sin(a),z)))
    for j in range(n):
        k=(j+1)%n
        faces += [(j,k,n+k,n+j),(2*n+j,3*n+j,3*n+k,2*n+k),(j,2*n+j,2*n+k,k),(n+j,n+k,3*n+k,3*n+j)]
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.materials.append(mat)
    o=bpy.data.objects.new(name,mesh);collection.objects.link(o);o.parent=parent;return o

root=node('TwinFang')
yaw=node('FangYaw',root)
pitch=node('FangPitch',yaw)
# Fixed attachment saddle conceals the existing crossmember junction; no replacement rack.
pin('Fixed bearing',(0,-.43,0),(0,-.31,0),.255,iron,root,48)
pin('Yaw race',(0,-.35,0),(0,-.28,0),.40,steel,yaw,48)
box('Cradle bridge',(0,-.32,0),(1.10,.07,.40),iron,yaw,.02)
for j in range(12):
    a=j*math.tau/12
    pin('Race bolt',(.365*math.cos(a),-.28,.365*math.sin(a)),(.365*math.cos(a),-.262,.365*math.sin(a)),.016,edge,yaw,6)
for side in [-1,1]:
    x=side*.56
    box('Pitch cheek',(x,-.12,.015),(.10,.38,.36),steel,yaw,.025)
    pin('Pitch bearing',(x-side*.075,0,0),(x+side*.06,0,0),.105,iron,yaw,32)
    pin('Pitch cap',(x+side*.06,0,0),(x+side*.075,0,0),.063,edge,yaw,24)
    box('Under-rack hanger',(side*.1755,-.64,0),(.065,.30,.09),iron,yaw)
    drum=node('Drum_'+str(side),yaw)
    # Small central rotating envelope stays within the two production lift posts.
    center=side*.135
    pin('Drum body',(center-.105,-.80,0),(center+.105,-.80,0),.205,iron,drum,48)
    for xx in [center-.105,center+.105]:
        exterior=xx+(-.025 if xx<center else .025)
        pin('Rolled rim',(xx-.012,-.80,0),(xx+.012,-.80,0),.214,edge,drum,48)
        pin('Inset drum face',(xx-.014,-.80,0),(xx+.014,-.80,0),.182,steel,drum,40)
        pin('Hub',(xx-.022,-.80,0),(xx+.022,-.80,0),.050,iron,drum,24)
        for j in range(8):
            a=j*math.tau/8
            pin('Face rib',(exterior,-.80+.065*math.cos(a),.065*math.sin(a)),(exterior,-.80+.171*math.cos(a),.171*math.sin(a)),.010,edge,drum,8)
    for j in range(12):
        a=j*math.tau/12
        pin('Shell seam',(center-.08,-.80+.206*math.cos(a),.206*math.sin(a)),(center+.08,-.80+.206*math.cos(a),.206*math.sin(a)),.005,steel,drum,6)
    box('Drum latch',(center,-.84,.207),(.12,.09,.03),steel,drum)
    pin('Latch handle',(center-.04,-.84,.235),(center+.04,-.84,.235),.008,edge,drum,8)
    gun=node('Gun_'+str(side),pitch)
    x=side*.215
    box('Receiver',(x,0,-.14),(.30,.29,.60),iron,gun,.023)
    box('Receiver cover',(x,.158,-.125),(.285,.035,.59),steel,gun,.008)
    box('Rear cap',(x,0,.18),(.31,.29,.035),steel,gun)
    box('Side plate',(x+side*.156,0,-.11),(.018,.23,.52),steel,gun,.009)
    for z in [-.29,-.07,.13]:
        pin('Receiver screw',(x+side*.167,0,z),(x+side*.178,0,z),.014,edge,gun,6)
    pin('Charging rail',(x+side*.166,.085,-.29),(x+side*.166,.085,.13),.018,iron,gun,12)
    box('Charging handle',(x+side*.185,.085,.08),(.075,.032,.055),edge,gun)
    box('Top latch',(x,.188,-.06),(.13,.035,.075),iron,gun)
    box('Feed guide',(x+side*.163,.02,-.22),(.04,.065,.085),edge,gun,.009)
    box('Feed opening',(x+side*.186,.02,-.22),(.006,.043,.055),dark,gun,.002)
    tube('Barrel',x,-.18,-1.06,.038,.019,iron,gun)
    shroud=tube('Cooling shroud',x,-.22,-.68,.086,.070,steel,gun)
    # Actual openings, not painted black spots; two rows on each visible flank.
    for z in [-.275,-.36,-.445,-.53,-.615]:
        for yy in [-.032,.032]:
            bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=.018,depth=.25,location=point((x,yy,z)),rotation=(0,math.pi/2,0))
            cutter=bpy.context.object
            mod=shroud.modifiers.new('Cooling aperture','BOOLEAN');mod.operation='DIFFERENCE';mod.object=cutter
            bpy.context.view_layer.objects.active=shroud;bpy.ops.object.modifier_apply(modifier=mod.name)
            bpy.data.objects.remove(cutter,do_unlink=True)
    for z in [-.22,-.68]: tube('Shroud collar',x,z-.012,z+.012,.094,.070,iron,gun)
    tube('Muzzle',x,-1.055,-1.145,.056,.024,steel,gun)
    tube('Muzzle lip',x,-1.145,-1.16,.061,.024,iron,gun)
    for z in [-1.075,-1.115]:
        for sign in [-1,1]: box('Muzzle slot',(x+sign*.056,0,z),(.002,.028,.022),dark,gun,.001)
    node('Muzzle_'+str(side),gun,(x,0,-1.16))
# Source belt samples are editable; runtime supplies the curved instances and feed phase.
cartridge=node('Cartridge')
pin('Cartridge case',(-.037,0,0),(.022,0,0),.009,brass,cartridge,10)
pin('Cartridge rim',(-.039,0,0),(-.035,0,0),.0105,brass,cartridge,10)
bpy.ops.mesh.primitive_cone_add(vertices=10,radius1=.009,radius2=.002,depth=.023,location=point((.0335,0,0)),rotation=(0,math.pi/2,0))
finish(bpy.context.object,'Projectile',brass,cartridge)
box('Belt link',(-.008,-.005,0),(.018,.008,.019),iron,cartridge,.001)
for parent in [o for o in collection.objects if o.type=='EMPTY']:
    for mat in list(bpy.data.materials):
        group=[o for o in parent.children if o.type=='MESH' and o.data.materials[0]==mat]
        if not group:continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in group:o.select_set(True)
        bpy.context.view_layer.objects.active=group[0];bpy.ops.object.convert(target='MESH')
        if len(group)>1:bpy.ops.object.join()
        bpy.context.object.name=parent.name+'_'+mat.name
# Enlarge guns and keep the rear swept envelope clear of the wider cradle.
# Blender Y is forward here. Extend everything ahead of the barrel root together
# so shrouds, bores, muzzle openings and sockets remain aligned.
for gun in [o for o in collection.objects if o.name in ('Gun_-1','Gun_1')]:
    for obj in gun.children:
        if obj.type == 'MESH':
            obj.data.transform(obj.matrix_local)
            obj.matrix_local = Matrix.Identity(4)
            for vertex in obj.data.vertices:
                vertex.co.x *= GUN_WIDTH_HEIGHT
                vertex.co.z *= GUN_WIDTH_HEIGHT
                if vertex.co.y < 0:
                    limit = math.sqrt(max(0, .218**2-vertex.co.y**2))
                    vertex.co.z = max(-limit, min(limit, vertex.co.z))
                if vertex.co.y > .18:
                    vertex.co.y = .18 + (vertex.co.y-.18)*BARREL_LENGTH
        elif obj.name.startswith('Muzzle_'):
            obj.location.x *= GUN_WIDTH_HEIGHT
            obj.location.y = .18 + (obj.location.y-.18)*BARREL_LENGTH
for drum in [o for o in collection.objects if o.name in ('Drum_-1','Drum_1')]:
    for obj in drum.children:
        if obj.type != 'MESH': continue
        obj.data.transform(obj.matrix_local)
        obj.matrix_local = Matrix.Identity(4)
        for vertex in obj.data.vertices:
            vertex.co.x *= 1.30
            vertex.co.y *= 1.30
            vertex.co.z = -.80 + (vertex.co.z+.80)*1.30
car=REPO/'assets/vehicles/source/TrackstormCar.blend'
with bpy.data.libraries.load(str(car),link=True) as (src,dst):dst.objects=src.objects
reference=bpy.data.collections.new('Unmodified production Car reference');bpy.context.scene.collection.children.link(reference)
reference.hide_render=True;reference.hide_viewport=True
for obj in dst.objects:
    if obj:reference.objects.link(obj)
for library in bpy.data.libraries:library.filepath=bpy.path.relpath(library.filepath,start=str(ROOT/'source'))
bpy.context.scene['Reference']='ApprovedTwinFang.png; built-in imagegen, user approved October 11 2026'
bpy.context.scene['Rig']='FangYaw includes drums; FangPitch includes paired Gun nodes; runtime flexes and advances belts'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/TwinFang.blend'))
for obj,path in [(root,'TwinFang.glb'),(cartridge,'Cartridge.glb')]:
    bpy.ops.object.select_all(action='DESELECT')
    for o in [obj]+list(obj.children_recursive):o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=str(ROOT/path),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
paths=[ROOT/'source/TwinFang.blend',ROOT/'source/BuildTwinFang.py',ROOT/'TwinFang.glb',ROOT/'Cartridge.glb',ROOT/'reference/ApprovedTwinFang.png',ROOT/'MuzzleFlash.gdshader',ROOT/'WornMetal.gdshader']
(ROOT/'sources.json').write_text(json.dumps({'authoring':'Original Blender 5.2 game geometry; built-in imagegen approved reference; no external acquisition','car_source_sha256':hashlib.sha256(car.read_bytes()).hexdigest(),'sha256':{p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}},indent=2)+'\n')
print('TWIN_FANG_AUTHORING_COMPLETE',flush=True)
