"""Refine the retained TS-175 Blender master; preserve spoon, magazine and winch.

Run after BuildCatapult.py for a clean rebuild, or directly against the existing
master. Only the named platform, support and routing components are replaced.
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
    obj.animation_data_clear()
    if obj.get('ts175_refinement'):
        bpy.data.objects.remove(obj, do_unlink=True)

def v(p): return Vector((p[0], -p[2], p[1]))
def joint(name): return bpy.data.objects[name]
def mark(obj):
    obj['ts175_refinement'] = True
    return obj
def empty(name, parent, at=(0, 0, 0)):
    obj = bpy.data.objects.get(name)
    if obj is None:
        obj = bpy.data.objects.new(name, None); bpy.context.collection.objects.link(obj)
    obj.parent = parent; obj.location = v(at); obj.empty_display_size = .04
    obj.rotation_mode = 'XYZ'; obj.rotation_euler = (0, 0, 0); obj.scale = (1, 1, 1)
    return obj

iron = bpy.data.materials['Salvo forged graphite']
steel = bpy.data.materials['Salvo worn steel edges']
brass = bpy.data.materials['Salvo brass bushings']

def finish(obj, name, material, parent):
    obj.name = name; obj.parent = parent; obj.data.materials.append(material); mark(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel = obj.modifiers.new('Machined edges', 'BEVEL'); bevel.width = .008; bevel.segments = 2
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    if not obj.data.uv_layers:
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(island_margin=.02); bpy.ops.object.mode_set(mode='OBJECT')
    return obj

def box(name, at, size, material, parent):
    bpy.ops.mesh.primitive_cube_add(size=1, location=v(at)); obj = bpy.context.object
    obj.scale = (size[0], size[2], size[1]); return finish(obj, name, material, parent)

def cyl(name, a, b, radius, material, parent, vertices=24):
    d = v(b)-v(a)
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=d.length, location=(v(a)+v(b))/2)
    obj = bpy.context.object; obj.rotation_mode = 'QUATERNION'; obj.rotation_quaternion = d.to_track_quat('Z', 'Y')
    return finish(obj, name, material, parent)

def remove_meshes(parent):
    for obj in list(parent.children):
        if obj.type == 'MESH': bpy.data.objects.remove(obj, do_unlink=True)

root = joint('SalvoCatapult'); fixed = joint('FixedMount'); slide = joint('TurretSlide')
yaw = joint('TurretYaw'); arm = joint('SpoonHinge'); middle = joint('ArmMiddle'); tip = joint('ArmTip')
support = joint('SupportExtension'); spring = joint('Spring'); cable = joint('Cable')
remove_meshes(fixed); remove_meshes(slide); remove_meshes(support); remove_meshes(spring); remove_meshes(cable)
for obj in list(yaw.children):
    if obj.name.startswith('spring support root'): bpy.data.objects.remove(obj, do_unlink=True)

lift = empty('PlatformLift', root)
guides = empty('PlatformGuides', lift)
remove_meshes(guides)
slide.parent = lift
carriage = empty('HingeCarriage', yaw)
plate = empty('PlateHinge', yaw, (0,-.08,.16))
support.parent = plate
arm.parent = carriage
# Retain the original hinge, axle and stop components, now on the moving carriage.
for obj in list(yaw.children):
    if obj.type == 'MESH' and any(token in obj.name for token in ('hinge cheek', 'axle', 'stop bracket', 'stop buffer')):
        obj.parent = carriage
for x in [-.29, .29]:
    box('Rack mounting shoe', (x,-.035,-.18), (.18,.07,.82), iron, fixed)
    box('Platform guide channel', (x,-.10,-.18), (.115,.11,.98), iron, guides)
    box('Sliding platform runner', (x,-.24,.65), (.075,.075,1.42), steel, slide)
    for z in [-.52,.15]:
        box('Lift outer socket', (x,-.12,z), (.13,.22,.13), iron, fixed)
        # Positive overlap remains throughout the 0.28 m lift stroke.
        box('Lift inner post', (x,-.29,z), (.075,.42,.075), steel, guides)
        cyl('Rack shoe bolt', (x,.0,z), (x,.025,z), .034, steel, fixed, 6)
box('Platform nose crosshead', (0,-.24,0), (.69,.09,.34), iron, slide)
box('Platform rear tie', (0,-.10,.18), (.69,.10,.15), iron, guides)
cyl('Stationary turret pedestal', (0,-.245,0),(0,-.035,0),.225,iron,slide,48)
cyl('Slew lower stationary race', (0,-.045,0),(0,-.025,0),.30,steel,slide,48)
# A straight overlapping plate, mechanically bolted to the revolving bearing.
box('Rearward plate root', (0,-.07,.24), (.25,.10,.90), iron, plate)
box('Rearward plate extension', (0,-.15,.45), (.18,.075,.90), steel, support)
box('Plate bearing clevis', (0,-.045,.16), (.25,.23,.18), iron, yaw)
for x in [-.14,.14]:
    cyl('Plate pivot bolt', (x-.018,-.08,.16),(x+.018,-.08,.16),.045,steel,yaw,12)
    box('Hinge lift guide', (x,.14,0), (.09,.28,.19), iron, yaw)
    box('Hinge lift inner cheek', (x,-.015,0), (.052,.38,.13), steel, carriage)

foot = empty('SpringFoot', support, (0,-.08,.90))
top = empty('SpringTop', tip, (0,-.08,.32))
box('Lower spring saddle', (0,-.115,.90), (.31,.07,.22), iron, support)
box('Upper spring saddle', (0,-.045,.32), (.23,.08,.20), iron, tip)
for parent, y, z in [(support,-.115,.90),(tip,-.04,.32)]:
    cyl('Spring saddle through pin', (-.17,y,z),(.17,y,z),.035,steel,parent,16)

# Six working coils. Morphing changes pitch without stretching the wire gauge or
# the separate rigid seats. Both extremes retain a closed half-turn at each end.
verts=[]; extended=[]; faces=[]; steps=192; sides=8
for i in range(steps+1):
    t=i/steps; theta=t*12*pi
    travel=max(0,min(1,(t-.045)/.91))
    for j in range(sides):
        phi=j*2*pi/sides
        radial=.118+.017*cos(phi)
        verts.append(v((radial*cos(theta),.20*travel+.017*sin(phi),radial*sin(theta))))
        extended.append(v((radial*cos(theta),2.0*travel+.017*sin(phi),radial*sin(theta))))
for i in range(steps):
    for j in range(sides):
        a=i*sides+j; b=i*sides+(j+1)%sides
        faces.append((a,b,b+sides,a+sides))
mesh=bpy.data.meshes.new('Six-turn constant-gauge compression spring'); mesh.from_pydata(verts,[],faces); mesh.update()
coil=bpy.data.objects.new('SpringCoil',mesh); bpy.context.collection.objects.link(coil)
coil.parent=spring; coil.data.materials.append(steel); mark(coil)
for polygon in mesh.polygons: polygon.use_smooth=True
uv=mesh.uv_layers.new(name='Wire UV')
for polygon in mesh.polygons:
    for loop in polygon.loop_indices:
        index=mesh.loops[loop].vertex_index; uv.data[loop].uv=(index%sides/sides,index//sides/steps)
coil.shape_key_add(name='Basis'); morph=coil.shape_key_add(name='Extended')
for vertex,position in zip(morph.data,extended): vertex.co=position
for name,parent in [('SpringLowerSeat',foot),('SpringUpperSeat',top)]:
    seat=empty(name,parent); remove_meshes(seat)
    cyl(name+' plate',(0,-.018,0),(0,.018,0),.152,iron,seat,32)
    cyl(name+' collar',(0,-.03,0),(0,.03,0),.058,iron if parent==foot else brass,seat)
guide=empty('SpringGuide',yaw); remove_meshes(guide)
cyl('Spring guide rod',(0,0,0),(0,1,0),.023,steel,guide,16)

# Cable below the platform, axial thrust swivel, then outside the spring along
# the underside of the spoon to a captive rear lug. No diagonal shortcut through
# the rack and no stationary cable wrapping around a rotating bearing.
exit_node=empty('WinchExit',root,(0,-.40,-.158))
riser=empty('CableRiser',root,(0,-.10,-.158))
rear=empty('CablePlatformRear',lift,(0,-.10,-.158))
swivel=empty('CableSwivel',slide,(0,-.22,0))
swivel_top=empty('CableSwivelTop',yaw,(0,-.045,0))
fairlead=empty('CableFairlead',yaw,(.205,-.08,.20))
eye=empty('CableEye',tip,(.205,-.13,.66))
for parent,at in [(fixed,(0,-.10,-.158)),(guides,(0,-.10,-.158)),(slide,(0,-.22,0)),(yaw,(.205,-.08,.20))]:
    x,y,z=at
    cyl('Cable sheave', (x-.022,y,z),(x+.022,y,z),.038,brass,parent,24)
    for side in [-.04,.04]: box('Sheave cheek',(x+side,y,z),(.018,.10,.10),iron,parent)
cyl('Axial rotary tension coupling',(0,-.22,0),(0,-.045,0),.044,brass,yaw,24)
box('Rear spoon cable strap',(0,-.09,.66),(.47,.05,.07),iron,tip)
cyl('Cable rear clevis',(.205,-.13,.625),(.205,-.13,.69),.04,steel,tip,16)
cyl('Unit cable',(0,0,0),(0,1,0),.012,iron,cable,12)
# Normalize mesh-local transforms before posing; runtime stretches one metre +Y.
for obj in [*cable.children,*guide.children]:
    bpy.context.view_layer.objects.active=obj; bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

segments=[cable]
for index in range(1,6):
    segment=empty('CableSegment'+str(index),root); remove_meshes(segment)
    model=cable.children[0].copy(); model.data=cable.children[0].data; bpy.context.collection.objects.link(model); model.parent=segment; mark(model)
    segments.append(segment)

def orient(obj,a,b,scale=False):
    inv=obj.parent.matrix_world.inverted(); p=inv@a; q=inv@b; d=q-p
    obj.location=p; obj.rotation_mode='QUATERNION'; obj.rotation_quaternion=d.to_track_quat('Z','Y')
    obj.scale=(1,1,max(.001,d.length) if scale else 1)

def pose(extension, cock, load, heading, release):
    lift.location=v((0,.28*extension,0)); slide.location=v((0,.12,-.65-1.07*extension))
    carriage.location=v((0,.30*extension,0)); yaw.rotation_euler=(0,0,heading)
    middle.location=v((0,0,.68*extension)); tip.location=v((0,0,.64*extension)); support.location=v((0,.08,.74*extension-.16))
    angle=(45 if release else 24*extension*(1-cock)-5*cock)*pi/180
    arm.location=v((0,.20,0)); arm.rotation_euler=(-angle,0,0)
    plate.rotation_euler=(-max(0,angle)*.55,0,0)
    joint('WinchDrum').rotation_euler=(angle*9,0,0)
    joint('Round_4').location=v((0,.165,4*.112)).lerp(v((0,.06,.77)),load)
    bpy.context.view_layer.update()
    a=foot.matrix_world.translation; b=top.matrix_world.translation
    orient(spring,a,b); orient(guide,a,b,True); morph.value=max(0,min(1,((b-a).length-.20)/1.8))
    orient(joint('SpringLowerSeat'),a,b); orient(joint('SpringUpperSeat'),b,b+(b-a))
    points=[exit_node,riser,rear,swivel,swivel_top,fairlead,eye]
    for segment,a,b in zip(segments,points,points[1:]): orient(segment,a.matrix_world.translation,b.matrix_world.translation,True)

for frame,extension,cock,load,heading,release in [
    (1,0,0,0,0,False),(40,1,0,0,0,False),(55,1,1,0,0,False),(65,1,1,1,0,False),
    (80,1,1,0,0,True),(100,1,1,0,0,False),(110,1,1,1,0,False),
    (140,1,1,1,pi/2,False),(170,1,1,1,pi,False),(200,1,1,1,0,False),(240,0,0,0,0,False)]:
    pose(extension,cock,load,heading,release)
    for obj in [lift,slide,carriage,yaw,arm,middle,tip,plate,support,spring,guide,*segments,joint('SpringLowerSeat'),joint('SpringUpperSeat'),joint('WinchDrum'),joint('Round_4')]:
        for prop in ['location','scale','rotation_quaternion' if obj.rotation_mode=='QUATERNION' else 'rotation_euler']:
            obj.keyframe_insert(data_path=prop,frame=frame)
    morph.keyframe_insert(data_path='value',frame=frame)
root['mechanical_revision']=2
scene.frame_set(65)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source'/'SalvoCatapult.blend'))
scene.frame_set(1)
for obj in bpy.data.objects:
    obj.animation_data_clear()
    if obj.type=='MESH' and obj.data.shape_keys: obj.data.shape_keys.animation_data_clear()
# Keep the coil's mesh and morph separate. Bake sibling mesh offsets, not joint poses.
for parent in [obj for obj in bpy.data.objects if obj.type=='EMPTY']:
    meshes=[child for child in parent.children if child.type=='MESH' and not child.data.shape_keys]
    if len(meshes)<2: continue
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes: obj.select_set(True)
    bpy.context.view_layer.objects.active=meshes[0]; bpy.ops.object.join()
    bpy.context.object.name=parent.name+'_Surfaces'
bpy.ops.export_scene.gltf(filepath=str(ROOT/'SalvoCatapult.glb'),export_format='GLB',export_yup=True,export_apply=False,export_animations=False,export_morph=True)
print('Refined existing Salvo master and exported connected mechanical assembly.')
