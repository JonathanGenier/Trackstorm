"""Export a material-batched game copy without altering the editable master."""
from pathlib import Path
import bpy
ROOT=Path(__file__).resolve().parents[1]
REPOSITORY_ROOT=ROOT.parents[1]
SOURCE=(ROOT/'source/TrackstormCar.blend').resolve()
OUTPUT=(ROOT/'TrackstormCar.glb').resolve()
if not (REPOSITORY_ROOT/'project.godot').is_file():
 raise RuntimeError('Car exporter could not identify the Trackstorm repository root')
if SOURCE.parent != (ROOT/'source').resolve() or OUTPUT.parent != ROOT:
 raise RuntimeError('Car exporter paths escaped their intentional asset directories')
bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
bpy.context.scene.frame_set(1)
root=bpy.data.objects['Car'];groups={}
for o in list(bpy.context.scene.objects):
 if o.type!='MESH' or o.parent!=root or o.name=='Identification':continue
 if o.name.startswith('BodyPanel_'):continue # Preserve independently useful exterior panels.
 if o.data.materials[0].name.startswith('Light_') or o.name.startswith(('SuspensionLink_','ShockRod_','ShockRod2_','LiftPiston_')):continue
 if o.data.materials[0].name=='Car_ArmoredGlass':continue # Separate panes sort independently.
 group='InternalCage' if o.name.startswith('InternalCage_') else 'Chassis'
 groups.setdefault((o.data.materials[0],group),[]).append(o)
for (mat,group),objects in groups.items():
 bpy.ops.object.select_all(action='DESELECT')
 for o in objects:
  bpy.context.view_layer.objects.active=o;o.select_set(True)
  for mod in list(o.modifiers):bpy.ops.object.modifier_apply(modifier=mod.name)
 bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.join();objects[0].name=group+'_'+mat.name
bpy.ops.export_scene.gltf(filepath=str(OUTPUT),export_format='GLB',export_yup=True,export_animations=False,export_apply=True)
print('TS259_GAME_EXPORT',sum(o.type=='MESH' for o in bpy.context.scene.objects),flush=True)
