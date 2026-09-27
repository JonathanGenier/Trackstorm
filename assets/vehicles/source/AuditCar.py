"""Reopen the retained Blender master and validate its editable rig and deployment envelope."""
from pathlib import Path
import bpy,json,math
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'source/TrackstormCar.blend'))
scene=bpy.context.scene
required=['Car','WeaponRack','Identification','TrunkHinge_L','TrunkHinge_R']
for corner in ['FL','FR','RL','RR']:
    required += ['WheelCarrier_'+corner,'WheelSpin_'+corner,'Coil_'+corner,'ShockRod_'+corner]
for side in ['L','R']:
    required += [prefix+side for prefix in ['Headlight_','TailRunning_','Brake_','Reverse_']]
    required += ['WeaponMount_'+side+'_Front','WeaponMount_'+side+'_Rear']
assert all(bpy.data.objects.get(name) for name in required)
meshes=[o for o in scene.objects if o.type=='MESH']
assert all(all(math.isfinite(c) for c in v.co) for o in meshes for v in o.data.vertices)
assert all(o.data.materials for o in meshes)
# Coils use solid-color metal and intentionally need no authored texture UVs.
assert all(o.data.uv_layers or o.name.startswith('Coil_') for o in meshes)
samples=[]
for frame in [1,13,25,36,48,60,68,75,88,100]:
    scene.frame_set(frame); bpy.context.view_layer.update()
    rack=bpy.data.objects['WeaponRack']; left=bpy.data.objects['TrunkHinge_L']
    assert rack.location.z<=.83
    if rack.location.z>.0: assert abs(left.rotation_euler.y)>1.69
    piston=bpy.data.objects['LiftPiston_-1']
    bottom=(piston.matrix_world @ Vector((0,0,-.105))).z
    assert abs(bottom+.25)<.005
    samples.append({'frame':frame,'rack_height':rack.location.z,'lid_angle':left.rotation_euler.y,'piston_bottom':bottom})
scene.frame_set(1)
result={'result':'PASS','meshes':len(meshes),'vertices':sum(len(o.data.vertices) for o in meshes),'polygons':sum(len(o.data.polygons) for o in meshes),'required_nodes':len(required),'deployment_samples':samples}
out=ROOT.parents[1]/'.godot/ts164-car/blender-audit.json';out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result),flush=True)
