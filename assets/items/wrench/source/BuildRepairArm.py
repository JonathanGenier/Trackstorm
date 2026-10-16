"""Original TS-292 rigid mechanical rig; metres. Blender 5.2, no external assets.

Joint-local +Z becomes Godot +Y. Editable components and named pivot hierarchy
are retained in the master. Export batches surfaces by joint/material only.
"""
from pathlib import Path
from math import sin, cos, pi
import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.context.scene.unit_settings.system = 'METRIC'

def material(name, rgb, metal, rough):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*rgb, 1)
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    # Original UV patina, deliberately restrained to preserve broad colour fields.
    n = 256
    y,x = np.mgrid[:n,:n]/n
    rng = np.random.default_rng(292)
    noise = rng.random((n,n))
    shade = .80+.10*np.sin(x*29+np.cos(y*37))+.10*noise
    shade[(np.sin(x*175+y*31)>.995)&(noise>.70)] = .43
    shade[(noise>.992)&(np.sin(y*90)>0)] = .33
    pixels = np.ones((n,n,4),dtype=np.float32)
    pixels[:,:,:3] = shade[:,:,None]*np.array(rgb)
    tex = bpy.data.images.new(name, width=n, height=n)
    tex.pixels.foreach_set(pixels.ravel())
    tex.pack()
    node=m.node_tree.nodes.new('ShaderNodeTexImage'); node.image=tex
    m.node_tree.links.new(node.outputs['Color'],bs.inputs['Base Color'])
    return m

red=material('Repair crimson enamel',(.62,.018,.027),.42,.32)
ivory=material('Repair warm ivory',(.88,.82,.65),.22,.38)
brass=material('Repair aged brass',(.56,.32,.075),.78,.30)
gold=material('Repair yellow stars',(.98,.62,.025),.40,.30)
iron=material('Repair dark steel',(.075,.09,.105),.80,.34)
steel=material('Repair worn edges',(.40,.44,.47),.83,.28)
rubber=material('Repair hose rubber',(.022,.025,.028),.05,.55)

def pivot(name,parent=None,loc=(0,0,0)):
    o=bpy.data.objects.new(name,None); bpy.context.collection.objects.link(o)
    o.parent=parent; o.location=loc; o.empty_display_size=.09
    return o

root=pivot('RepairArm')
mount=pivot('Mount',root)
shoulder=pivot('Shoulder',root,(0,0,.23))
elbow=pivot('Elbow',shoulder,(0,0,.7))
slide=pivot('Slide',elbow,(0,0,.05))
reach=pivot('Reach',slide,(0,0,.05))
wrist=pivot('Wrist',reach,(0,0,1.15))
chuck=pivot('Chuck',wrist)

def finish(o,name,mat,parent,bevel=0):
    o.name=name; o.parent=parent; o.data.materials.append(mat)
    bpy.context.view_layer.objects.active=o
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod=o.modifiers.new('Rounded machined edges','BEVEL');mod.width=bevel;mod.segments=3
        mod=o.modifiers.new('Weighted normals','WEIGHTED_NORMAL');mod.keep_sharp=True
    return o

def box(name,loc,size,mat,parent,bevel=.012):
    bpy.ops.mesh.primitive_cube_add(size=1,location=loc)
    o=bpy.context.object; o.scale=size
    return finish(o,name,mat,parent,bevel)

def cyl(name,loc,r,depth,mat,parent,axis='Z',verts=48):
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts,radius=r,depth=depth,location=loc)
    o=bpy.context.object
    if axis=='X': o.rotation_euler[1]=pi/2
    if axis=='Y': o.rotation_euler[0]=pi/2
    finish(o,name,mat,parent,.003)
    for p in o.data.polygons: p.use_smooth=len(p.vertices)==4
    return o

def mesh(name,verts,faces,mat,parent):
    d=bpy.data.meshes.new(name);d.from_pydata(verts,[],faces);d.update()
    bm=bmesh.new();bm.from_mesh(d);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(d);bm.free()
    o=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(o);o.parent=parent;d.materials.append(mat)
    return o

def star(name,x,z,r,parent):
    verts=[(x,0,z)]
    for k in range(10):
        a=pi/2+2*pi*k/10; rr=r if k%2==0 else r*.43
        verts.append((x,rr*cos(a),z+rr*sin(a)))
    faces=[(0,k+1,(k+1)%10+1) for k in range(10)]
    o=mesh(name,verts,faces,gold,parent)
    mod=o.modifiers.new('Raised enamel emblem','SOLIDIFY');mod.thickness=.002

def hose(name,points,parent,r=.027):
    d=bpy.data.curves.new(name,'CURVE');d.dimensions='3D';d.bevel_depth=r;d.bevel_resolution=2;d.resolution_u=16
    s=d.splines.new('BEZIER');s.bezier_points.add(len(points)-1)
    for p,v in zip(s.bezier_points,points):p.co=v;p.handle_left_type='AUTO';p.handle_right_type='AUTO'
    o=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(o);o.parent=parent;d.materials.append(rubber)
    return o

# Four feet exactly over the existing rack's +/- .45, +/- .30 sockets.
for x in [-.45,.45]:
    box('Socket adapter rail',(x,0,.115),(.10,.72,.07),iron,mount)
    for y in [-.30,.30]:
        box('Bolted socket foot',(x,y,.095),(.17,.16,.045),red,mount)
        cyl('Foot fastener',(x,y,.13),.027,.018,brass,mount,verts=6)
box('Mount crossmember',(0,0,.135),(1.05,.19,.065),iron,mount)
cyl('Turntable bottom',(0,0,.16),.21,.045,brass,mount)
cyl('Turntable red drum',(0,0,.195),.17,.06,red,mount)
for k in range(16):
    a=k*2*pi/16
    cyl('Turntable bolt',(.185*cos(a),.185*sin(a),.189),.012,.016,brass,mount,verts=6)
# Scalloped ivory circus pennants on the low swivel drum.
for k in range(12):
    a=k*2*pi/12;b=(k+1)*2*pi/12
    mesh('Turntable ivory pennant',[(.172*cos(a),.172*sin(a),.22),(.172*cos(b),.172*sin(b),.22),(.173*cos((a+b)/2),.173*sin((a+b)/2),.17)],[(0,1,2)],ivory,mount)

def joint(parent):
    cyl('Hinge axle',(0,0,0),.118,.27,iron,parent,'X')
    for side in [-1,1]:
        cyl('Brass hinge flange',(side*.139,0,0),.112,.028,brass,parent,'X')
        cyl('Recessed hinge ring',(side*.157,0,0),.081,.011,iron,parent,'X')
        cyl('Brass pivot cap',(side*.165,0,0),.061,.018,brass,parent,'X')
        for k in range(8):
            a=k*2*pi/8
            cyl('Hinge bolt',(side*.158,.094*cos(a),.094*sin(a)),.009,.012,steel,parent,'X',6)

def link(parent,length):
    joint(parent)
    box('Forged link core',(0,0,length/2),(.19,.17,length-.13),iron,parent)
    for side in [-1,1]:
        box('Crimson link cheek',(side*.103,0,length/2),(.026,.20,length-.16),red,parent)
        # Slanted white bands on the two broad cheeks; actual geometry, no decal aliasing.
        count=2 if length<1 else 3
        period=(length-.22)/count
        for i in range(count):
            z=.13+i*period
            lo=max(.085,z-.04);hi=min(length-.085,z+.04)
            mesh('Ivory diagonal stripe',[(side*.118,-.10,lo),(side*.118,.10,lo+.07),(side*.118,.10,hi+.07),(side*.118,-.10,hi)],[(0,1,2,3)],ivory,parent)
            star('Yellow star',side*.120,z+period*.65,min(.065,period*.22),parent)
        for z in [.12,length-.12]:
            cyl('Cheek retaining bolt',(side*.124,0,z),.021,.016,brass,parent,'X',6)
    # Parallel hose runs and brass clamps retain service access around both pivots.
    hose('Black feed hose',[(0,-.135,.02),(0,-.155,.18),(0,-.155,length-.18),(0,-.135,length-.02)],parent)
    for k in range(16):
        z=.13+(length-.26)*k/15
        cyl('Red hose wrap',(0,-.155,z),.029,.021,red,parent,verts=16)
    for z in [.19,length*.53,length-.19]:
        cyl('Hose brass clamp',(0,-.155,z),.035,.028,brass,parent,verts=24)
    for side in [-1,1]:
        box('Edge reinforcement',(0,side*.094,length/2),(.19,.015,length-.24),brass,parent,.004)

link(shoulder,.7)
joint(elbow)
# Three nested sliding box sections; solid rims and smaller insets explain extension.
for owner,width in [(elbow,.27),(slide,.21),(reach,.15)]:
    box('Telescopic steel channel',(0,0,.57),(width,.20,1.10),iron,owner,.015)
    for side in [-1,1]:
        box('Bevelled enamel panel',(side*(width/2+.008),0,.57),(.022,.175,1.03),red,owner,.012)
        for k in range(3):
            z=.15+k*.30
            mesh('Broad ivory diagonal',[(side*(width/2+.021),-.085,z),(side*(width/2+.021),.085,z+.08),(side*(width/2+.021),.085,z+.20),(side*(width/2+.021),-.085,z+.12)],[(0,1,2,3)],ivory,owner)
            star('Raised gold circus star',side*(width/2+.023),z+.245,.065,owner)
        for z in [.075,1.06]:
            box('Bolted brass guide shoe',(side*(width/2+.025),0,z),(.045,.23,.065),brass,owner,.006)
            for y in [-.065,.065]:cyl('Guide shoe bolt',(side*(width/2+.051),y,z),.013,.014,steel,owner,'X',6)
    for side in [-1,1]:
        box('Machined slide rail',(0,side*.105,.57),(width*.65,.018,1.07),steel,owner,.004)
    # Fixed hydraulic line terminates in sealed sliding manifolds, no stretched hose.
    hose('Section hydraulic feed',[(0,-.15,.07),(0,-.16,.25),(0,-.16,.95),(0,-.15,1.07)],owner,.022)
    for z in [.07,1.07]:cyl('Sealed slide union',(0,-.15,z),.035,.055,brass,owner,verts=24)
    for k in range(12):cyl('Red spiral hose wrap',(0,-.16,.18+k*.07),.024,.022,red,owner,verts=12)
joint(wrist)
# Thick clevis shoulders, bearing collars and inset keyed hubs.
for owner in [shoulder,elbow,wrist]:
    for side in [-1,1]:
        box('Sculpted pivot cheek',(side*.13,0,.055),(.045,.27,.24),red,owner,.045)
        cyl('Outer stepped brass bearing',(side*.174,0,0),.121,.025,brass,owner,'X')
        cyl('Bearing seal',(side*.191,0,0),.089,.014,iron,owner,'X')
        cyl('Keyed bearing hub',(side*.185,0,0),.063,.020,brass,owner,'X',12)
        cyl('Axle lock',(side*.197,0,0),.025,.010,steel,owner,'X',6)
        for k in range(8):
            a=k*2*pi/8
            cyl('Bearing perimeter stud',(side*.192,.103*cos(a),.103*sin(a)),.010,.013,steel,owner,'X',6)
    # Rotary unions carry hoses through the joint axis without bridging moving links.
    cyl('Hydraulic rotary union',(.185,0,0),.037,.025,brass,owner,'X')
    hose('Joint service elbow',[(.16,0,0),(.16,-.12,0),(0,-.16,.18)],owner,.022)

# Rack-top power pack and reservoir: no replacement lift or under-rack dressing.
box('Power unit casing',(-.36,-.52,.245),(.64,.30,.29),red,mount,.025)
for x in [-.66,-.06]:
    box('Power unit corner rail',(x,-.52,.245),(.038,.32,.31),iron,mount,.008)
    for z in [.12,.37]:cyl('Power case fastener',(x,-.686,z),.015,.016,brass,mount,'Y',6)
for x in [-.56,-.36,-.16]:
    box('Recessed ventilation panel',(x,-.677,.245),(.16,.015,.21),iron,mount,.009)
    for z in [.17,.21,.25,.29,.33]:box('Vent louvre',(x,-.691,z),(.135,.018,.012),steel,mount,.003)
mesh('Power lightning emblem',[(-.37,-.705,.34),(-.43,-.705,.24),(-.37,-.705,.24),(-.40,-.705,.15),(-.29,-.705,.27),(-.35,-.705,.27),(-.31,-.705,.34)],[(0,1,2,3,4,5,6)],gold,mount)
for x in [-.58,-.13]:
    box('Power mounting foot',(x,-.52,.10),(.11,.36,.04),brass,mount)
    cyl('Power terminal',(x,-.52,.405),.025,.035,brass,mount,verts=12)
# Horizontal pressure vessel at the rear-right of the rack, clear of the folded arm.
cyl('Reservoir enamel shell',(.39,-.52,.26),.135,.56,ivory,mount,'X')
for x in [.11,.67]:
    cyl('Reservoir dished end',(x,-.52,.26),.126,.04,red,mount,'X')
    cyl('Reservoir brass shoulder',(x,-.52,.26),.139,.035,brass,mount,'X')
    box('Reservoir saddle',(x,-.52,.12),(.07,.29,.07),iron,mount)
    for k in range(8):
        a=k*2*pi/8
        cyl('Tank ring bolt',(x+.021,-.52+.108*cos(a),.26+.108*sin(a)),.009,.012,steel,mount,'X',6)
for x in [.19,.39,.59]:
    cyl('Tank red stripe',(x,-.52,.26),.137,.09,red,mount,'X')
cyl('Pressure regulator',(.72,-.52,.26),.035,.07,brass,mount,'X')
hose('Reservoir supply',[(.75,-.52,.26),(.78,-.32,.22),(.25,-.21,.17),(.21,0,.19)],mount,.024)
hose('Power loom',[(-.58,-.52,.41),(-.70,-.36,.34),(-.33,-.20,.18),(-.18,0,.20)],mount,.024)

# Recognisable vertical impact tool: motor can, toothed cooling collar and drive socket.
cyl('Drill motor housing',(0,0,-.13),.115,.24,red,wrist)
for z in [-.035,-.22]:cyl('Drill brass collar',(0,0,z),.127,.032,brass,wrist)
for k in range(12):
    a=k*2*pi/12
    cyl('Motor collar fastener',(.12*cos(a),.12*sin(a),-.02),.009,.014,steel,wrist,verts=6)
    b=(k+1)*2*pi/12
    mesh('Drill ivory pennant',[(.117*cos(a),.117*sin(a),-.11),(.117*cos(b),.117*sin(b),-.11),(.118*cos((a+b)/2),.118*sin((a+b)/2),-.20)],[(0,1,2)],ivory,wrist)
for side in [-1,1]:star('Drill star',side*.117,-.075,.036,wrist)
cyl('Impact gearbox',(0,0,-.265),.095,.065,iron,wrist)
for k in range(12):
    a=k*2*pi/12
    box('Gearbox cooling fin',(.093*cos(a),.093*sin(a),-.265),(.012,.012,.052),steel,wrist,.002)
cyl('Socket coupling',(0,0,-.31),.067,.04,brass,chuck)
cyl('Impact socket',(0,0,-.40),.052,.16,iron,chuck,verts=12)
cyl('Contact drive',(0,0,-.489),.028,.018,steel,chuck,verts=6)
hose('Motor power loop',[(0,.145,0),(0,.18,.12),(0,.19,-.07),(0,.11,-.13)],wrist,.022)

# Motor ribs, service panels and a substantial impact gearbox distinguish the tool.
for side in [-1,1]:
    box('Tool service armor',(side*.107,0,-.13),(.035,.15,.18),red,wrist,.02)
    for z in [-.07,-.19]:
        for y in [-.052,.052]:cyl('Tool service screw',(side*.132,y,z),.011,.012,brass,wrist,'X',6)
for k in range(16):
    a=k*2*pi/16
    box('Motor cooling flute',(.107*cos(a),.107*sin(a),-.14),(.014,.014,.10),iron,wrist,.003)
cyl('Impact drive shoulder',(0,0,-.295),.079,.025,steel,wrist)

# UV unwrap all source geometry, including converted hose curves.
for o in list(bpy.data.objects):
    if o.type not in {'MESH','CURVE'}:continue
    bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
    if o.type=='CURVE':bpy.ops.object.convert(target='MESH')
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT')

# Pose the editable hierarchy in its exact transverse folded rest pose.
shoulder.rotation_euler[1]=pi/2
elbow.rotation_euler[1]=pi
wrist.rotation_euler[1]=-pi/2
wrist.rotation_euler[0]=pi/2
root['Design reference']='TS-292 attachments 10154 Arm 7.png and 10153 back red mount.png; direction only, no redistributed pixels'
root['Rig']='Rigid shoulder/elbow/wrist hierarchy; fixed .7m upper and 1.25-3.25m telescopic forearm; no scale animation; Client solves local-space IK'
bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/RepairArm.blend'))
# Bake modifiers and batch only within each articulated owner/material.
for o in list(bpy.data.objects):
    if o.type!='MESH':continue
    bpy.context.view_layer.objects.active=o
    for mod in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
for parent in [mount,shoulder,elbow,slide,reach,wrist,chuck]:
    for mat in [red,ivory,brass,gold,iron,steel,rubber]:
        objects=[o for o in list(bpy.data.objects) if o.type=='MESH' and o.parent==parent and o.data.materials[0]==mat]
        if not objects:continue
        bpy.ops.object.select_all(action='DESELECT')
        for o in objects:o.select_set(True)
        bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join()
        objects[0].name=parent.name+'_'+mat.name
bpy.ops.export_scene.gltf(filepath=str(ROOT/'RepairArm.glb'),export_format='GLB',export_animations=False,export_yup=True,export_cameras=False,export_lights=False)
print('REPAIR_ARM_EXPORTED',sum(len(o.data.polygons) for o in bpy.data.objects if o.type=='MESH'),'polygons')
