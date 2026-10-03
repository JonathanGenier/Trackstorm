"""Read-only closed-mesh and production Car swept-envelope audit."""
from pathlib import Path
from math import pi
import json
import bpy
import bmesh

ROOT=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'source/Tombstone.blend'))
root=bpy.data.objects['Tombstone']
objects=[o for o in root.children_recursive if o.type=='MESH']
mount=[o for o in bpy.data.objects['TombstoneRack'].children_recursive if o.type=='MESH']
for obj in objects+mount:
    mesh=bmesh.new()
    mesh.from_mesh(obj.data)
    assert all(e.is_manifold for e in mesh.edges), obj.name+' is not closed'
    assert mesh.calc_volume(signed=True)>0, obj.name+' has inward volume'
    mesh.free()
print('TOMBSTONE_MOUNT_MESH_PASS '+json.dumps({'closed_outward_meshes':len(mount)}),flush=True)
car=bpy.data.collections['Production Car - linked fit reference']
car_vertices=[o.matrix_world@v.co for o in car.objects if o.type=='MESH' for v in o.data.vertices]
rear=max(-v.y for v in car_vertices)
half_width=max(abs(v.x) for v in car_vertices)
top=max(v.z for v in car_vertices)
minimum=float('inf')
def ease(t):
    t=max(0,min(1,t))
    return t*t*(3-2*t)
for step in range(61):
    progress=step/60
    root.location.y=-(3.25+.60*ease(progress*.42/.18))
    for side,name in [(-1,'L'),(1,'R')]:
        bpy.data.objects['Wing_'+name].rotation_euler.z=side*pi/2*(1-ease(progress))
    bpy.context.view_layer.update()
    vertices=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    # Every armor point must be aft of the entire Car or outside its full lateral
    # envelope. This is conservative even where the Car body narrows at the rear.
    clearance=min(max(-v.y-rear,abs(v.x)-half_width) for v in vertices)
    minimum=min(minimum,clearance)
    assert clearance>.015, (step,clearance)
    if step==0:
        bottom=min(v.z for v in vertices)
        shield_top=max(v.z for v in vertices)
        assert shield_top>=top and bottom>-1.145, (bottom,shield_top,top)
width=max(v.x for v in vertices)-min(v.x for v in vertices)
height=max(v.z for v in vertices)-min(v.z for v in vertices)
assert abs(width-6.6)<.04 and 2.5<=height<2.65, (width,height)
print('TOMBSTONE_AUDIT_PASS '+json.dumps({'mesh_objects':len(objects),'closed_outward_meshes':True,'production_car_rear_extent_m':rear,'production_car_half_width_m':half_width,'minimum_separating_clearance_m':minimum,'deployment_samples':61,'mounted_road_clearance_m':bottom+1.145,'vehicle_local_top_m':top,'shield_local_top_m':shield_top,'expanded_width_m':width,'expanded_height_including_lamps_m':height,'limitations':'Conservative mesh sweep at authored frame-32 Car reference pose; runtime must cover articulated wheels, trunk/rack, terrain and gameplay contacts.'}),flush=True)
