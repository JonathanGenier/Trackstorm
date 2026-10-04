"""Rebuild only the articulated carriage; preserve approved shield geometry/export."""
from pathlib import Path
import hashlib
import json
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'source/Shield.blend'))
bpy.context.preferences.filepaths.save_version = 0
old = bpy.data.objects.get('ShieldRack')
if old:
    for obj in list(old.children_recursive)+[old]: bpy.data.objects.remove(obj, do_unlink=True)
collection = bpy.data.collections['Carnage Circus - export only']
steel = bpy.data.materials['Circus_BlackenedSteel']
iron = bpy.data.materials['Circus_WornIron']
gold = bpy.data.materials['Circus_TarnishedBrass']

def point(v): return Vector((v[0],-v[2],v[1]))
def node(name, parent=None):
    obj=bpy.data.objects.new(name,None)
    collection.objects.link(obj)
    obj.parent=parent
    return obj
root=node('ShieldRack')
def finish(obj,name,material,parent):
    obj.name=name
    for c in list(obj.users_collection): c.objects.unlink(obj)
    collection.objects.link(obj)
    obj.parent=parent
    obj.data.materials.append(material)
    mod=obj.modifiers.new('Forged edges','BEVEL'); mod.width=.008; mod.segments=2
    obj.modifiers.new('Face normals','WEIGHTED_NORMAL')
    return obj
def box(name,pos,size,mat,parent):
    bpy.ops.mesh.primitive_cube_add(size=1,location=point(pos))
    obj=bpy.context.object; obj.scale=(size[0],size[2],size[1])
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return finish(obj,name,mat,parent)
def pin(name,a,b,r,mat,parent):
    a,b=point(a),point(b)
    bpy.ops.mesh.primitive_cylinder_add(vertices=12,radius=r,depth=(b-a).length,location=(a+b)/2)
    obj=bpy.context.object; obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    return finish(obj,name,mat,parent)

box('Bolted saddle',(0,.10,.15),(1.12,.12,.53),steel,root)
for x in [-.45,.45]:
    for z in [-.08,.30]:
        box('Rack clamp shoe',(x,.16,z),(.24,.08,.17),iron,root)
        pin('Anchor bolt',(x,.19,z),(x,.235,z),.047,gold,root)
for side in ['L','R']:
    upper=node('Upper_'+side,root)
    lower=node('Lower_'+side,root)
    ram=node('Ram_'+side,root)
    rod=node('Rod_'+side,root)
    for part in [upper,lower]:
        box('Boxed load bearing sleeve',(0,0,.36),(.19,.18,.72),steel,part)
        box('Telescoping inner beam',(0,0,.76),(.125,.12,.48),iron,part)
        for z in [.05,.60,.94]:
            pin('Clevis axle',(-.14,0,z),(.14,0,z),.085,iron,part)
            pin('Retaining pin',(-.155,0,z),(-.145,0,z),.04,gold,part)
        box('Sleeve brass collar',(0,0,.65),(.205,.195,.065),gold,part)
    pin('Hydraulic barrel',(0,0,0),(0,0,1),.065,steel,ram)
    pin('Hydraulic chrome piston',(0,0,0),(0,0,1),.035,iron,rod)
cradle=node('Cradle',root)
for x in [-.45,.45]:
    box('Cradle vertical load spreader',(x,0,0),(.14,1.64,.105),steel,cradle)
    for y in [-.70,.70]:
        box('Four point locking jaw',(x,y,.08),(.23,.18,.20),iron,cradle)
        pin('Jaw lock', (x-.14,y,.04),(x+.14,y,.04),.05,gold,cradle)
for y in [-.70,.70]: box('Cross member',(0,y,0),(1.10,.13,.11),iron,cradle)
pin('Diagonal brace A',(-.45,-.67,-.04),(.45,.67,-.04),.047,steel,cradle)
pin('Diagonal brace B',(.45,-.67,-.04),(-.45,.67,-.04),.047,steel,cradle)
box('Central armored hinge housing',(0,0,-.03),(.34,.38,.18),iron,cradle)

# The editable source shows the final selected pose. Runtime moves named rigid parts.
def align(obj,a,b):
    a,b=point(a),point(b)
    obj.location=a
    obj.rotation_euler=(b-a).to_track_quat('-Y','Z').to_euler()
    obj.scale=(1,(b-a).length,1)
root.location=point((0,1.34,1.845))
cradle.location=point((0,-1.09,1.22))
for side,x in [('L',-.45),('R',.45)]:
    a=(x,.22,.30); e=(x,.32,1.30); b=(x,-1.09,1.22)
    align(bpy.data.objects['Upper_'+side],a,e)
    align(bpy.data.objects['Lower_'+side],e,b)
    ra=(x+.16,.20,.43); rb=(x+.16,-.77,1.24)
    middle=tuple(ra[i]+(rb[i]-ra[i])*.58 for i in range(3))
    align(bpy.data.objects['Ram_'+side],ra,middle)
    align(bpy.data.objects['Rod_'+side],middle,rb)
bpy.context.scene['Mount articulation']='Bolted saddle, two telescopic boxed arms, hydraulic rams, cross-braced cradle and four locking jaws. Runtime selection reverses before rack lowering.'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/Shield.blend'))
root.location=(0,0,0)
bpy.ops.object.select_all(action='DESELECT')
for obj in [root]+list(root.children_recursive): obj.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'ShieldRack.glb'),export_format='GLB',use_selection=True,export_yup=True,export_animations=False,export_apply=True)
manifest=json.loads((ROOT/'sources.json').read_text())
for p in [ROOT/'ShieldRack.glb',ROOT/'source/Shield.blend']: manifest['sha256'][p.name]=hashlib.sha256(p.read_bytes()).hexdigest()
(ROOT/'sources.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('SHIELD_MOUNT_AUTHORING_COMPLETE',flush=True)
