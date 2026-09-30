"""Original TS-249 rack tool. Blender metres/Z-up, export Y-up; no colliders.

Independent machined parts remain editable. Runtime places the rigid pivots and
telescopic rods; all surface geometry is authored here, not generated in Godot.
"""
from pathlib import Path
from math import pi
import bpy
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.context.scene.unit_settings.system = 'METRIC'

def mat(name, color, metal, rough):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = m.diffuse_color
    p.inputs['Metallic'].default_value = metal
    p.inputs['Roughness'].default_value = rough
    # Original baked mottling and sparse paint wear, packed into the master/GLB.
    n=256
    y,x=np.mgrid[0:n,0:n].astype(np.float32)/n
    grain=np.random.default_rng(249).random((n,n))
    shade=.72+.18*(np.sin(x*55+np.sin(y*32))*np.cos(y*49)+1)/2+.10*grain
    shade[grain>.992]=.30
    rgba=np.ones((n,n,4),dtype=np.float32)
    rgba[:,:,:3]=shade[:,:,None]*np.array(color)
    image=bpy.data.images.new(name+' original patina',width=n,height=n)
    image.pixels.foreach_set(rgba.ravel()); image.pack()
    node=m.node_tree.nodes.new('ShaderNodeTexImage'); node.image=image
    m.node_tree.links.new(node.outputs['Color'],p.inputs['Base Color'])
    return m

iron = mat('Arm forged gunmetal', (.045,.057,.062), .65,.56)
edge = mat('Arm machined steel', (.18,.21,.22), .8,.39)
red = mat('Arm oxblood enamel', (.23,.021,.014), .4,.53)
dark = mat('Arm rubber and recess', (.025,.03,.031), .1,.7)
bone = mat('Arm ivory identification', (.67,.57,.38), .3,.5)

# Functions accept Godot coordinates for unambiguous runtime pivot dimensions.
def xyz(p): return (p[0],-p[2],p[1])
def empty(name, parent=None):
    o = bpy.data.objects.new(name,None)
    bpy.context.collection.objects.link(o)
    o.parent = parent
    return o

root=empty('ProxyMinePlacementArm')
def finish(o,name,p,material,parent,bevel=.008):
    o.name=name
    o.location=xyz(p)
    o.data.materials.append(material)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.parent=parent
    if bevel:
        b=o.modifiers.new('Machined softened edges','BEVEL'); b.width=bevel; b.segments=2
        n=o.modifiers.new('Face weighted normals','WEIGHTED_NORMAL'); n.keep_sharp=True
    return o

def box(name,p,size,material,parent):
    bpy.ops.mesh.primitive_cube_add()
    o=bpy.context.object; o.dimensions=(size[0],size[2],size[1])
    return finish(o,name,p,material,parent)

def cyl(name,p,radius,length,material,parent,axis='x',sides=16):
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides,radius=radius,depth=length)
    o=bpy.context.object
    if axis=='x': o.rotation_euler[1]=pi/2
    elif axis=='z': o.rotation_euler[0]=pi/2
    return finish(o,name,p,material,parent,.004)

def pin(name,p,r,parent):
    cyl(name+' axle',p,r,.48,iron,parent)
    for s in [-1,1]:
        cyl(name+' cap',(p[0]+s*.255,p[1],p[2]),r*.83,.042,edge,parent)
        cyl(name+' hex bolt',(p[0]+s*.28,p[1],p[2]),r*.35,.018,dark,parent,sides=6)

mount=empty('Mount',root)
box('Bolted underside saddle',(0,.07,0),(.64,.12,.46),iron,mount)
for x in [-.25,.25]:
    box('Shoulder cheek',(x,-.045,0),(.08,.28,.28),edge,mount)
    for z in [-.16,.16]: cyl('Saddle bolt',(x,.135,z),.035,.025,dark,mount,'y',6)
pin('Shoulder bearing',(0,-.07,0),.14,mount)

for name in ['Upper','Forearm']:
    link=empty(name,root)
    pin(name+' root',(0,0,0),.135,link)
    box(name+' closed box beam',(0,.39,0),(.29,.70,.23),iron,link)
    for x in [-.168,.168]:
        box(name+' oxblood side armor',(x,.38,0),(.042,.54,.25),red,link)
        for y in [.14,.37,.61]:
            cyl(name+' armor rivet',(x*1.13,y,.08),.021,.014,edge,link,sides=8)
        box(name+' ivory stripe',(x*1.15,.49,0),(.009,.047,.22),bone,link)
    box(name+' sliding collar',(0,.735,0),(.38,.12,.31),edge,link)
    box(name+' wiper',(0,.806,0),(.30,.027,.26),dark,link)
    # Telescopic insertion is translated, never scales the outer armor or bolts.
    slide=empty('Slide',link)
    box(name+' inner beam',(0,.43,0),(.22,.86,.17),edge,slide)
    for x in [-.24,.24]:
        cyl(name+' hydraulic barrel',(x,.34,0),.059,.53,iron,link,'y')
        cyl(name+' hydraulic collar',(x,.62,0),.072,.065,edge,link,'y')
        cyl(name+' chrome piston',(x,.40,0),.033,.8,edge,slide,'y')
    pin(name+' end bearing',(0,.86,0),.115,slide)
    for y in [.20,.29,.38,.47]: box(name+' heat rib',(0,y,-.135),(.26,.024,.03),dark,link)

wrist=empty('Wrist',root)
box('Leveling crosshead',(0,0,0),(.91,.16,.25),iron,wrist)
cyl('Wrist yaw bearing',(0,.13,0),.17,.13,edge,wrist,'y')
box('Wrist oxblood guard',(0,.205,0),(.31,.025,.24),red,wrist)
for s,name in [(-1,'JawLeft'),(1,'JawRight')]:
    jaw=empty(name,wrist)
    # The jaw origin is at the crosshead end; runtime opens these around Z.
    jaw.location=xyz((s*.43,0,0))
    cyl('Jaw hinge',(0,0,0),.095,.29,edge,jaw,'z')
    o=box('Jaw swept upper',(s*.13,-.15,0),(.15,.39,.20),iron,jaw)
    o.rotation_euler[1]=s*.55
    box('Jaw armored finger',(s*.25,-.40,0),(.12,.31,.24),red,jaw)
    box('Jaw steel toe',(s*.16,-.56,0),(.26,.065,.24),edge,jaw)
    box('Replaceable grip pad',(s*.16,-.515,0),(.22,.027,.21),dark,jaw)
    for y in [-.30,-.45]: cyl('Jaw fastener',(s*.318,y,0),.027,.02,edge,jaw,sides=6)

# Preview the folded assembly in rack-local coordinates, including saved pivots.
mount.location=xyz((0,-.17,-.25))
def segment(name,a,b):
    o=bpy.data.objects[name]; v=Vector(xyz(b))-Vector(xyz(a))
    o.location=xyz(a); o.rotation_mode='QUATERNION'; o.rotation_quaternion=Vector((0,0,1)).rotation_difference(v.normalized())
    bpy.data.objects.get('Slide' if name=='Upper' else 'Slide.001').location=(0,0,v.length-.86)
segment('Upper',(0,-.24,-.25),(0,-.30,.64))
segment('Forearm',(0,-.30,.64),(0,-.43,-.27))
wrist.location=xyz((0,-.43,-.27)); wrist.rotation_euler[0]=pi/2

print('TS249_ARM_SOURCE',len([o for o in bpy.data.objects if o.type=='MESH']),'editable components')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/ProxyMinePlacementArm.blend'))
# Batch only within each independently driven rigid part. Editable source stays separate.
for pivot in [o for o in list(bpy.data.objects) if o.type=='EMPTY']:
    pieces=[o for o in pivot.children if o.type=='MESH']
    if not pieces: continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in pieces:
        o.select_set(True)
        bpy.context.view_layer.objects.active=o
        for modifier in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=modifier.name)
    bpy.context.view_layer.objects.active=pieces[0]
    bpy.ops.object.join()
    bpy.context.object.name=pivot.name+'Geometry'
bpy.ops.export_scene.gltf(filepath=str(ROOT/'ProxyMinePlacementArm.glb'),export_format='GLB',export_apply=True,export_animations=False,export_cameras=False,export_lights=False)
print('TS249_ARM_BUILT',len([o for o in bpy.data.objects if o.type=='MESH']),'editable components')
