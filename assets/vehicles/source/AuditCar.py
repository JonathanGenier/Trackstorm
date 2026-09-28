"""Reopen the retained Blender master and validate its editable rig and deployment envelope."""
from pathlib import Path
import bpy,json,math,bmesh
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'source/TrackstormCar.blend'))
scene=bpy.context.scene
required=['Car','WeaponRack','Identification','TrunkHinge_L','TrunkHinge_R']
for corner in ['FL','FR','RL','RR']:
    required += ['WheelCarrier_'+corner,'WheelSpin_'+corner,'Coil_'+corner,'ShockRod_'+corner,'Coil2_'+corner,'ShockRod2_'+corner]
for side in ['L','R']:
    required += [prefix+side for prefix in ['Headlight_','TailRunning_','Brake_','Reverse_']]
    required += ['WeaponMount_'+side+'_Front','WeaponMount_'+side+'_Rear']
assert all(bpy.data.objects.get(name) for name in required)
wheelbase=abs(bpy.data.objects['WheelCarrier_FL'].location.y-bpy.data.objects['WheelCarrier_RL'].location.y)
assert abs(wheelbase-3.351105)<.0001
tire_topology=[]
for corner in ['FL','FR','RL','RR']:
    tire=bpy.data.objects['WheelSpin_'+corner+'_Car_Rubber']
    bm=bmesh.new();bm.from_mesh(tire.data)
    assert all(edge.is_manifold for edge in bm.edges), 'Rubber must be a closed surface'
    pending=[next(iter(bm.verts))];seen=set(pending)
    while pending:
        for edge in pending.pop().link_edges:
            for vertex in edge.verts:
                if vertex not in seen:seen.add(vertex);pending.append(vertex)
    assert len(seen)==len(bm.verts), 'Tread must connect to the tire carcass'
    spin=bpy.data.objects['WheelSpin_'+corner]
    local=spin.matrix_world.inverted() @ tire.matrix_world
    points=[local @ v.co for v in tire.data.vertices]
    diameter=2*max(math.hypot(p.y,p.z) for p in points)
    tire_topology.append({'corner':corner,'vertices':len(bm.verts),'connected_closed_surface':True,'outside_diameter_m':diameter,'width_m':max(p.x for p in points)-min(p.x for p in points)})
    bm.free()
assert max(t['outside_diameter_m'] for t in tire_topology)-min(t['outside_diameter_m'] for t in tire_topology)<.00001, 'All four tires must have identical outside diameter'
assert max(bpy.data.objects['WheelCarrier_'+c].location.z for c in ['FL','FR','RL','RR'])-min(bpy.data.objects['WheelCarrier_'+c].location.z for c in ['FL','FR','RL','RR'])<.00001, 'Authored wheel center heights must agree'
meshes=[o for o in scene.objects if o.type=='MESH']
assert all(all(math.isfinite(c) for c in v.co) for o in meshes for v in o.data.vertices)
assert all(o.data.materials for o in meshes)
# Coils use solid-color metal and intentionally need no authored texture UVs.
assert all(o.data.uv_layers or o.name.startswith(('Coil_','Coil2_')) for o in meshes)
samples=[]
for frame in [1,13,25,36,48,60,68,75,88,100]:
    scene.frame_set(frame); bpy.context.view_layer.update()
    rack=bpy.data.objects['WeaponRack']; left=bpy.data.objects['TrunkHinge_L']
    assert abs(rack.location.y+1.845)<.0001, 'Rack keys must retain the extended bay station'
    assert rack.location.z<=1.35
    if rack.location.z>.0: assert abs(left.rotation_euler.y)>1.69
    piston=bpy.data.objects['LiftPiston_-1']
    bottom=(piston.matrix_world @ Vector((0,0,-.105))).z
    assert abs(bottom+.25)<.005
    samples.append({'frame':frame,'rack_height':rack.location.z,'lid_angle':left.rotation_euler.y,'piston_bottom':bottom})
scene.frame_set(1)
result={'result':'PASS','visual_wheelbase_m':wheelbase,'meshes':len(meshes),'vertices':sum(len(o.data.vertices) for o in meshes),'polygons':sum(len(o.data.polygons) for o in meshes),'required_nodes':len(required),'deployment_samples':samples}
result['tire_topology']=tire_topology
out=ROOT.parents[1]/'.godot/ts259-round7/blender-audit.json';out.parent.mkdir(parents=True,exist_ok=True)
# Rubber versus chassis surface overlap at representative full-travel/steer poses.
# This supplements runtime observation; it is not a physics/contact redesign.
from mathutils import Matrix
from mathutils.bvhtree import BVHTree
static_verts=[];static_faces=[];static_names=[]
deps=bpy.context.evaluated_depsgraph_get()
panel_names=['BodyPanel_Hood','TrunkLid_L','TrunkLid_R']
panel_names += [f'BodyPanel_{role}_{side}' for role in ['FrontFender','Door','RearQuarter'] for side in ['L','R']]
panel_trees={};panel_audit=[]
for name in panel_names:
    ob=bpy.data.objects[name];evaluated=ob.evaluated_get(deps);mesh=evaluated.to_mesh()
    bm=bmesh.new();bm.from_mesh(mesh)
    assert all(edge.is_manifold for edge in bm.edges), name+' must have closed formed edges'
    bm.free()
    panel_trees[name]=BVHTree.FromPolygons([ob.matrix_world @ v.co for v in mesh.vertices],[tuple(f.vertices) for f in mesh.polygons])
    panel_audit.append({'name':name,'closed_surface':True,'vertices':len(mesh.vertices)})
    evaluated.to_mesh_clear()
for i,name in enumerate(panel_names):
    for other in panel_names[i+1:]:
        assert not panel_trees[name].overlap(panel_trees[other]), name+' intersects '+other
result['independent_body_panels']=panel_audit
result['panel_surface_intersections']=0
for o in meshes:
    if o.parent != bpy.data.objects['Car'] or o.name.startswith(('SuspensionLink_', 'ShockRod_', 'ShockRod2_')):continue
    evaluated=o.evaluated_get(deps); m=evaluated.to_mesh(); offset=len(static_verts)
    static_verts.extend(o.matrix_world @ v.co for v in m.vertices)
    static_faces.extend(tuple(offset+i for i in f.vertices) for f in m.polygons)
    static_names.extend([o.name]*len(m.polygons))
    evaluated.to_mesh_clear()
body=BVHTree.FromPolygons(static_verts,static_faces)
clearances=[]
contact_points=[]
mechanical_clearances=[]
for corner in ['FL','FR','RL','RR']:
    tire=bpy.data.objects['WheelSpin_'+corner+'_Car_Rubber']
    carrier=bpy.data.objects['WheelCarrier_'+corner]
    evaluated=tire.evaluated_get(deps); m=evaluated.to_mesh()
    local=carrier.matrix_world.inverted() @ tire.matrix_world
    for compression in [0,.15,.327,.50,.60,.7165]:
        for steer in ([-.6,-.3,0,.3,.6] if corner.startswith('F') else [0]):
            center=carrier.location.copy();center.z=-1.472+compression+.54
            transform=Matrix.Translation(center) @ Matrix.Rotation(steer,4,'Z') @ local
            tree=BVHTree.FromPolygons([transform @ v.co for v in m.vertices],[tuple(f.vertices) for f in m.polygons])
            mechanical_verts=[];mechanical_faces=[];mechanical_names=[]
            carrier_pose=Matrix.Translation(center) @ Matrix.Rotation(steer,4,'Z')
            for part,suffix in enumerate(['A','B','Upper','Upper2']):
                anchor=bpy.data.objects['SuspensionAnchor_'+corner+'_'+suffix].location
                mount=bpy.data.objects['WheelLinkMount_'+corner+'_'+suffix]
                hub=carrier_pose @ mount.location
                middle=anchor.lerp(hub,.57) if part>=2 else hub
                link=bpy.data.objects['SuspensionLink_'+corner+'_'+suffix]
                segments=[(link,anchor,middle)]
                if part>=2:segments.append((bpy.data.objects[('ShockRod_' if part==2 else 'ShockRod2_')+corner],middle,hub))
                for segment,start,end in segments:
                    direction=end-start
                    pose=Matrix.Translation((start+end)/2) @ direction.to_track_quat('Z','Y').to_matrix().to_4x4() @ Matrix.Diagonal((1,1,direction.length,1))
                    for component in [segment,*segment.children]:
                        evaluated_component=component.evaluated_get(deps);mesh=evaluated_component.to_mesh()
                        component_pose=pose if component==segment else pose @ component.matrix_local
                        offset=len(mechanical_verts)
                        mechanical_verts.extend(component_pose @ v.co for v in mesh.vertices)
                        mechanical_faces.extend(tuple(offset+i for i in f.vertices) for f in mesh.polygons)
                        mechanical_names.extend([component.name]*len(mesh.polygons))
                        evaluated_component.to_mesh_clear()
            mechanism=BVHTree.FromPolygons(mechanical_verts,mechanical_faces)
            mechanical_pairs=mechanism.overlap(tree)
            mechanical_clearances.append({'corner':corner,'compression':compression,'steer':steer,'triangle_overlaps':len(mechanical_pairs),'objects':sorted(set(mechanical_names[a] for a,b in mechanical_pairs))})
            pairs=body.overlap(tree)
            overlaps=len(pairs)
            if corner=='FL' and steer==0:
                contact_points.extend(tuple(round(c,3) for c in sum((static_verts[i] for i in static_faces[a]),Vector())/len(static_faces[a])) for a,b in pairs[:25])
            clearances.append({'corner':corner,'compression':compression,'steer':steer,'triangle_overlaps':overlaps,'objects':sorted(set(static_names[a] for a,b in pairs))})
    evaluated.to_mesh_clear()
print(json.dumps(clearances),flush=True)
assert sum(x['triangle_overlaps'] for x in clearances)==0, 'Rubber intersects chassis in sampled travel/steering poses'
print('MECHANICAL',json.dumps(mechanical_clearances),flush=True)
assert sum(x['triangle_overlaps'] for x in mechanical_clearances)==0, 'Suspension bars/coils intersect rubber'
result['rubber_suspension_samples']=mechanical_clearances
result['contact_points']=contact_points
result['rubber_chassis_samples']=clearances
result['clearance_surface_overlaps']=sum(x['triangle_overlaps'] for x in clearances)
out.write_text(json.dumps(result,indent=2)+'\n')
print(json.dumps(result),flush=True)
