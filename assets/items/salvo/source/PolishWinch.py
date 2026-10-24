"""Refine only the retained catapult's rope, fairleads and winch presentation.

Run after RefineCatapult.py for a full rebuild, or against the existing master.
Approved spoon, spring, plate, turret and ammunition geometry/animation are retained.
"""
from pathlib import Path
from math import sin, cos, pi
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'source' / 'SalvoCatapult.blend'))
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.frame_set(1)
for obj in list(bpy.data.objects):
    if obj.get('ts175_winch_polish'):
        bpy.data.objects.remove(obj, do_unlink=True)

def v(p): return Vector((p[0], -p[2], p[1]))
def joint(name): return bpy.data.objects[name]
def clear_meshes(parent):
    for obj in list(parent.children):
        if obj.type == 'MESH': bpy.data.objects.remove(obj, do_unlink=True)
def empty(name, parent, at=(0, 0, 0)):
    obj = bpy.data.objects.get(name)
    if obj is None:
        obj = bpy.data.objects.new(name, None); bpy.context.collection.objects.link(obj)
    obj.animation_data_clear(); obj.parent = parent; obj.location = v(at)
    obj.rotation_mode = 'XYZ'; obj.rotation_euler = (0, 0, 0); obj.scale = (1, 1, 1)
    return obj

iron = bpy.data.materials['Salvo forged graphite']
brass = bpy.data.materials['Salvo brass bushings']
red = bpy.data.materials['Salvo crimson paper']
# Original repeating three-ply hemp pattern. No external texture is acquired.
image_name = 'Salvo carnival rope'
texture = bpy.data.images.get(image_name) or bpy.data.images.new(image_name, 256, 256)
pixels = []
for y in range(256):
    for x in range(256):
        strand = .5 + .5 * cos(2*pi*(3*x/256-y/256))
        fibre = .5 + .5 * sin(2*pi*(39*x/256-13*y/256))
        shade = .57 + .36*strand**.45 + .07*fibre
        pixels.extend((.34*shade, .145*shade, .045*shade, 1))
texture.pixels.foreach_set(pixels)
texture.filepath_raw = str(ROOT / 'SalvoCatapult_Salvo carnival rope.png')
texture.file_format = 'PNG'; texture.save()
# Keep the master self-contained and export the image's logical name. Otherwise
# Godot prefixes an already-prefixed filename when extracting this embedded PNG.
texture.pack(); texture.filepath_raw = ''
rope = bpy.data.materials.get(image_name) or bpy.data.materials.new(image_name)
rope.use_nodes = True; rope.node_tree.nodes.clear()
output = rope.node_tree.nodes.new('ShaderNodeOutputMaterial')
shader = rope.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
shader.inputs['Roughness'].default_value = .86
tex = rope.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = texture
rope.node_tree.links.new(tex.outputs['Color'], shader.inputs['Base Color'])
rope.node_tree.links.new(shader.outputs['BSDF'], output.inputs['Surface'])

def finish(obj, name, material, parent):
    obj.name = name; obj.parent = parent; obj.data.materials.append(material)
    obj['ts175_winch_polish'] = True
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if not obj.data.uv_layers:
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(island_margin=.02); bpy.ops.object.mode_set(mode='OBJECT')
    return obj
def box(name, at, size, material, parent):
    bpy.ops.mesh.primitive_cube_add(size=1, location=v(at)); obj=bpy.context.object
    obj.scale=(size[0],size[2],size[1]); return finish(obj,name,material,parent)
def cyl(name, a, b, radius, material, parent, sides=32):
    d=v(b)-v(a)
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides,radius=radius,depth=d.length,location=(v(a)+v(b))/2)
    obj=bpy.context.object; obj.rotation_mode='QUATERNION'; obj.rotation_quaternion=d.to_track_quat('Z','Y')
    return finish(obj,name,material,parent)
def tube(name, points, radius, material, parent, sides=8):
    verts=[]; faces=[]; coords=[v(p) for p in points]; travel=0; lengths=[]
    for i,p in enumerate(coords):
        if i: travel+=(p-coords[i-1]).length
        lengths.append(travel)
        tangent=(coords[min(i+1,len(coords)-1)]-coords[max(0,i-1)]).normalized()
        right=tangent.cross(Vector((1,0,0)) if abs(tangent.x)<.9 else Vector((0,1,0))).normalized()
        up=right.cross(tangent)
        for j in range(sides): verts.append(p+radius*(cos(2*pi*j/sides)*right+sin(2*pi*j/sides)*up))
    for i in range(len(coords)-1):
        for j in range(sides):
            a=i*sides+j; b=i*sides+(j+1)%sides; faces.append((a,a+sides,b+sides,b))
    mesh=bpy.data.meshes.new(name); mesh.from_pydata(verts,[],faces); mesh.update()
    obj=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(obj)
    mesh.uv_layers.new(name='Rope metres')
    for p in mesh.polygons:
        p.use_smooth=True
        for loop in p.loop_indices:
            n=mesh.loops[loop].vertex_index
            mesh.uv_layers.active.data[loop].uv=(n%sides/sides,lengths[n//sides]/.12)
    return finish(obj,name,material,parent)

root=joint('SalvoCatapult'); drum=joint('WinchDrum'); drum.animation_data_clear()
clear_meshes(drum)
cyl('Winch dark barrel',(-.30,0,0),(.30,0,0),.147,iron,drum)
for x in [-.30,.30]:
    cyl('Crimson winch rim',(x-.022,0,0),(x+.022,0,0),.242,red,drum,48)
    cyl('Brass winch hub',(x-.03,0,0),(x+.03,0,0),.064,brass,drum)
    # Deliberately asymmetric bold marker makes direction visible, not just rim blur.
    box('Winch rotation pointer',(x,.115,0),(.049,.17,.055),brass,drum)
    box('Winch rim countermark',(x,-.08,.14),(.049,.055,.055),iron,drum)
tube('Ten turns of brown ship rope', [(-.26+.52*i/320,.18*sin(i*20*pi/320),.18*cos(i*20*pi/320)) for i in range(321)], .030, rope, drum)

# Replace just the old cable guides/attachment, keeping the spring and plate intact.
for obj in list(bpy.data.objects):
    if obj.type=='MESH' and obj.name.startswith(('Cable sheave','Sheave cheek','Axial rotary tension','Rear spoon cable strap','Cable rear clevis')):
        bpy.data.objects.remove(obj,do_unlink=True)
exit_node=empty('WinchExit',root,(0,-.40,.18))
riser=empty('CableRiser',root,(0,-.13,.18))
rear=empty('CablePlatformRear',joint('PlatformLift'),(0,-.19,.18))
swivel=empty('CableSwivel',joint('TurretSlide'),(0,-.31,0))
swivel_top=empty('CableSwivelTop',joint('TurretYaw'),(0,-.045,0))
fairlead=empty('CableFairlead',joint('TurretYaw'),(-.245,.015,.15))
spoon_guide=empty('CableSpoonGuide',joint('SpoonHinge'),(-.245,-.135,.28))
eye=empty('CableEye',joint('ArmTip'),(-.245,-.135,.60))
level=empty('WinchLevelWind',root,(0,-.13,.18))
clear_meshes(level)
tube('Travelling brass rope guide',[(.045*cos(i*pi/16),0,.045*sin(i*pi/16)) for i in range(33)],.01,brass,level)
cyl('Level wind rail',(-.32,.21,.18),(.32,.21,.18),.021,iron,joint('WinchHousing'))
box('Level wind guide foot',(0,-.04,0),(.055,.065,.055),red,level)
for parent,at in [(joint('PlatformLift'),(0,-.19,.18)),(joint('TurretSlide'),(0,-.31,0)),(joint('TurretYaw'),(-.245,.015,.15))]:
    x,y,z=at
    cyl('Wide rope sheave',(x-.025,y,z),(x+.025,y,z),.065,brass,parent)
cyl('Rope rotary tension sleeve',(0,-.31,0),(0,-.045,0),.063,brass,joint('TurretYaw'))
box('Underspoon rope guide bracket',(-.215,-.065,.28),(.08,.14,.12),iron,joint('SpoonHinge'))
tube('Underspoon rope guide ring',[(-.245+.047*cos(i*pi/16),-.135+.047*sin(i*pi/16),.28) for i in range(33)],.01,brass,joint('SpoonHinge'))
box('Rear spoon rope strap',(0,-.09,.67),(.54,.055,.12),red,joint('ArmTip'))
cyl('Rear rope attachment pin',(-.30,-.135,.67),(-.18,-.135,.67),.035,brass,joint('ArmTip'))
tube('Spliced rope eye',[(-.245,-.135+.07*sin(i*pi/16),.67-.07*cos(i*pi/16)) for i in range(33)],.028,rope,joint('ArmTip'))

segments=[]
for index in range(7):
    segment=empty('Cable' if index==0 else 'CableSegment'+str(index),root); clear_meshes(segment)
    # A unit +Y rope with cylindrical UVs; runtime repeats its braid by physical length.
    obj=tube('Unit cable',[(0,0,0),(0,1,0)],.033,rope,segment,24)
    for loop in obj.data.uv_layers.active.data: loop.uv.y*=.12
    segments.append(segment)
points=[exit_node,riser,rear,swivel,swivel_top,fairlead,spoon_guide,eye]

def orient(obj,a,b):
    inv=obj.parent.matrix_world.inverted(); p=inv@a; d=inv@b-p
    obj.location=p; obj.rotation_mode='QUATERNION'
    obj.rotation_quaternion=(d if d.length>.001 else Vector((0,0,1))).to_track_quat('Z','Y')
    obj.scale=(1,1,max(.001,d.length))

# Bake only rope/drum transforms against the retained 240-frame mechanical study.
for frame in range(1,241):
    scene.frame_set(frame)
    for _ in range(4):
        bpy.context.view_layer.update()
        length=sum((b.matrix_world.translation-a.matrix_world.translation).length for a,b in zip(points,points[1:]))
        angle=(4-length)/.18; feed=.052*angle/(2*pi)
        for obj in [exit_node,riser,level]: obj.location.x=feed
    drum.rotation_mode='XYZ'; drum.rotation_euler=(angle,0,0)
    bpy.context.view_layer.update()
    for segment,a,b in zip(segments,points,points[1:]): orient(segment,a.matrix_world.translation,b.matrix_world.translation)
    for obj in [drum,exit_node,riser,level,*segments]:
        for prop in ['location','scale','rotation_quaternion' if obj.rotation_mode=='QUATERNION' else 'rotation_euler']:
            obj.keyframe_insert(data_path=prop,frame=frame)
root['mechanical_revision']=3
scene.frame_set(65)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source'/'SalvoCatapult.blend'))
scene.frame_set(1)
for obj in bpy.data.objects:
    obj.animation_data_clear()
    if obj.type=='MESH' and obj.data.shape_keys: obj.data.shape_keys.animation_data_clear()
for parent in [obj for obj in bpy.data.objects if obj.type=='EMPTY']:
    meshes=[child for child in parent.children if child.type=='MESH' and not child.data.shape_keys]
    if len(meshes)<2: continue
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes: obj.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0]; bpy.ops.object.join()
    bpy.context.object.name=parent.name+'_Surfaces'
bpy.ops.export_scene.gltf(filepath=str(ROOT/'SalvoCatapult.glb'),export_format='GLB',export_yup=True,export_apply=False,export_animations=False,export_morph=True)
print('Polished retained catapult rope, eye splice, indexed drum and travelling level wind.')
