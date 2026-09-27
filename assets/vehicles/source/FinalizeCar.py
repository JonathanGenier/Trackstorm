"""Pose/audit the editable master and export its runtime rig after BuildCar.py."""
from pathlib import Path
from math import sin, cos, pi
import bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'source/TrackstormCar.blend'))
scene=bpy.context.scene
scene.frame_set(1)
for name in ['FL','FR','RL','RR']:
    carrier=bpy.data.objects['WheelCarrier_'+name]
    hub=carrier.location+Vector((.12 if name.endswith('L') else -.12,0,-.04))
    for suffix in ['A','B','Upper','Upper2']:
        anchor=bpy.data.objects['SuspensionAnchor_'+name+'_'+suffix].location
        hub=carrier.location+Vector((.14 if name.endswith('L') else -.14,(-.17 if suffix=='Upper' else .17) if suffix.startswith('Upper') else 0,-.04))
        end=anchor.lerp(hub,.57) if suffix.startswith('Upper') else hub
        link=bpy.data.objects['SuspensionLink_'+name+'_'+suffix]
        direction=end-anchor
        link.location=(anchor+end)/2
        link.rotation_mode='QUATERNION';link.rotation_quaternion=direction.to_track_quat('Z','Y')
        link.scale=(1,1,direction.length)
        if suffix.startswith('Upper'):
            rod=bpy.data.objects[('ShockRod2_' if suffix=='Upper2' else 'ShockRod_')+name]; d=hub-end
            rod.location=(end+hub)/2;rod.rotation_mode='QUATERNION';rod.rotation_quaternion=d.to_track_quat('Z','Y');rod.scale=(1,1,d.length)
            coilname=('Coil2_' if suffix=='Upper2' else 'Coil_')+name
            if bpy.data.objects.get(coilname): continue
            verts=[]; faces=[]; steps=120; sides=6
            for j in range(steps+1):
                a=2*pi*6*j/steps
                for k in range(sides):
                    b=2*pi*k/sides
                    r=.078+.012*cos(b)
                    verts.append((r*cos(a),r*sin(a),-.46+.92*j/steps+.012*sin(b)))
            for j in range(steps):
                for k in range(sides): faces.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
            data=bpy.data.meshes.new(coilname);data.from_pydata(verts,[],faces);data.materials.append(bpy.data.materials['Car_WornSteel'])
            coil=bpy.data.objects.new(coilname,data);scene.collection.objects.link(coil);coil.parent=link
for piston in [bpy.data.objects['LiftPiston_-1'],bpy.data.objects['LiftPiston_1']]:
    for frame,lift in [(1,0),(25,0),(48,1),(60,1),(75,0),(100,0)]:
        piston.scale.z=(.21+.9*lift)/.21;piston.location.z=-.065-.45*lift
        piston.keyframe_insert(data_path='scale',frame=frame);piston.keyframe_insert(data_path='location',frame=frame)
# Batch stencil components without losing their independent authored emblem roots.
for parent in [o for o in scene.objects if 'Emblem' in o.name and o.type=='EMPTY']:
    children=[o for o in parent.children if o.type=='MESH']
    if len(children)<2: continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in children:o.select_set(True)
    bpy.context.view_layer.objects.active=children[0];bpy.ops.object.join();children[0].name=parent.name+'_Stencil'
scene.frame_set(1)
# Recesses use matte graphite rather than sky-reflecting metal.
p=bpy.data.materials['Car_Graphite'].node_tree.nodes.get('Principled BSDF')
p.inputs['Metallic'].default_value=.10
p.inputs['Roughness'].default_value=.82
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/TrackstormCar.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT/'TrackstormCar.glb'),export_format='GLB',export_yup=True,export_animations=False,export_apply=True)
print('TS164_FINALIZED meshes=',sum(o.type=='MESH' for o in scene.objects),flush=True)
