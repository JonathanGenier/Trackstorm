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
    for suffix in ['A','B','Upper']:
        anchor=bpy.data.objects['SuspensionAnchor_'+name+'_'+suffix].location
        end=anchor.lerp(hub,.57) if suffix=='Upper' else hub
        link=bpy.data.objects['SuspensionLink_'+name+'_'+suffix]
        direction=end-anchor
        link.location=(anchor+end)/2
        link.rotation_mode='QUATERNION';link.rotation_quaternion=direction.to_track_quat('Z','Y')
        link.scale=(1,1,direction.length)
        if suffix=='Upper':
            rod=bpy.data.objects['ShockRod_'+name]; d=hub-end
            rod.location=(end+hub)/2;rod.rotation_mode='QUATERNION';rod.rotation_quaternion=d.to_track_quat('Z','Y');rod.scale=(1,1,d.length)
            if bpy.data.objects.get('Coil_'+name): continue
            verts=[]; faces=[]; steps=120; sides=6
            for j in range(steps+1):
                a=2*pi*6*j/steps
                for k in range(sides):
                    b=2*pi*k/sides
                    r=.078+.012*cos(b)
                    verts.append((r*cos(a),r*sin(a),-.46+.92*j/steps+.012*sin(b)))
            for j in range(steps):
                for k in range(sides): faces.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
            data=bpy.data.meshes.new('Coil_'+name);data.from_pydata(verts,[],faces);data.materials.append(bpy.data.materials['Car_WornSteel'])
            coil=bpy.data.objects.new('Coil_'+name,data);scene.collection.objects.link(coil);coil.parent=link
for piston in [bpy.data.objects['LiftPiston_-1'],bpy.data.objects['LiftPiston_1']]:
    for frame,lift in [(1,0),(25,0),(48,1),(60,1),(75,0),(100,0)]:
        piston.scale.z=(.21+.9*lift)/.21;piston.location.z=-.065-.45*lift
        piston.keyframe_insert(data_path='scale',frame=frame);piston.keyframe_insert(data_path='location',frame=frame)
# Close the body above each wheel opening. These inner shoulder plates connect
# the flares to the bonnet/deck without filling the suspension travel cavity.
for side in [-1,1]:
    for yy in [-1.3005525,1.3005525]:
        name='ShoulderArmor_'+str(side)+'_'+str(yy)
        if bpy.data.objects.get(name):bpy.data.objects.remove(bpy.data.objects[name],do_unlink=True)
        verts=[];faces=[]
        for j in range(19):
            a=.20+(pi-.40)*j/18
            y=yy+cos(a)*.85;z=-.29+sin(a)*.85
            verts.extend([(side*.9,y,z),(side*.9,y,max(z,.36)),(side*.75,y,.36)])
        for j in range(18):
            faces.extend([(j*3,j*3+1,(j+1)*3+1,(j+1)*3), (j*3+1,j*3+2,(j+1)*3+2,(j+1)*3+1)])
        data=bpy.data.meshes.new(name);data.from_pydata(verts,[],faces);data.materials.append(bpy.data.materials['Car_OxideRed'])
        o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.parent=bpy.data.objects['Car']
        mod=o.modifiers.new('Armor thickness','SOLIDIFY');mod.thickness=.025
        bpy.ops.object.select_all(action='DESELECT');o.select_set(True);bpy.context.view_layer.objects.active=o
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.02);bpy.ops.object.mode_set(mode='OBJECT')
# Batch stencil components without losing their independent authored emblem roots.
for parent in [o for o in scene.objects if 'Emblem' in o.name and o.type=='EMPTY']:
    children=[o for o in parent.children if o.type=='MESH']
    if len(children)<2: continue
    bpy.ops.object.select_all(action='DESELECT')
    for o in children:o.select_set(True)
    bpy.context.view_layer.objects.active=children[0];bpy.ops.object.join();children[0].name=parent.name+'_Stencil'
scene.frame_set(1)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/TrackstormCar.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT/'TrackstormCar.glb'),export_format='GLB',export_yup=True,export_animations=False,export_apply=True)
print('TS164_FINALIZED meshes=',sum(o.type=='MESH' for o in scene.objects),flush=True)
