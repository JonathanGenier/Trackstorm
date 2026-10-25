"""Lock the retained spring base to turret yaw and rebake its spring study.

Run after PolishCircus.py, or reapply to the saved master. Spoon, rope, winch,
ammunition and their existing keys are preserved; only spring attachments rebake.
"""
from pathlib import Path
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'source' / 'SalvoCatapult.blend'))
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.frame_set(1)
for obj in list(bpy.data.objects):
    if obj.get('ts175_fixed_support'):
        bpy.data.objects.remove(obj, do_unlink=True)


def v(p): return Vector((p[0], -p[2], p[1]))
def joint(name): return bpy.data.objects[name]


plate = joint('PlateHinge')
plate.animation_data_clear()
plate.parent = joint('TurretYaw')
plate.location = v((0, -.08, .16))
plate.rotation_mode = 'XYZ'
plate.rotation_euler = (0, 0, 0)
plate.scale = (1, 1, 1)
# Retain the exported joint name, but it is now a fixed bracket, not a pitch hinge.
plate['articulation'] = 'Fixed to turret yaw; support slide retracts only for stow'
red = bpy.data.materials['Salvo circus enamel']
gold = bpy.data.materials['Salvo brass bushings']


def finish(obj, name, material):
    obj.name = name
    obj.parent = plate
    obj['ts175_fixed_support'] = True
    obj.data.materials.append(material)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bevel = obj.modifiers.new('Rounded brace edges', 'BEVEL')
    bevel.width = .004
    bevel.segments = 2
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.02)
    bpy.ops.object.mode_set(mode='OBJECT')


# Paired fixed triangular webs visibly lock the beam into its bearing bracket.
# Existing red/white/gold materials keep this restrained structural detail coherent.
for sign in [-1, 1]:
    points = [v((sign*x, y, z)) for x in [.112, .146]
              for y, z in [(-.10, -.03), (.09, -.03), (-.10, .30)]]
    faces = [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)]
    if sign < 0: faces = [face[::-1] for face in faces]
    mesh = bpy.data.meshes.new('Fixed spring-base gusset')
    mesh.from_pydata(points, [], faces)
    mesh.update()
    obj = bpy.data.objects.new('Fixed spring-base gusset', mesh)
    bpy.context.collection.objects.link(obj)
    finish(obj, 'Circus fixed support brace', red)
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=.026, depth=.028,
                                      location=v((sign*.151, -.058, .19)))
    bolt = bpy.context.object
    bolt.rotation_mode = 'QUATERNION'
    bolt.rotation_quaternion = Vector((sign, 0, 0)).to_track_quat('Z', 'Y')
    finish(bolt, 'Gold support locking bolt', gold)

spring = joint('Spring')
guide = joint('SpringGuide')
foot = joint('SpringFoot')
top = joint('SpringTop')
seats = [joint('SpringLowerSeat'), joint('SpringUpperSeat')]
morphs = joint('SpringCoil').data.shape_keys
morphs.animation_data_clear()
morph = morphs.key_blocks['Extended']
for obj in [spring, guide, *seats]: obj.animation_data_clear()


def orient(obj, a, b, stretch=False):
    inv = obj.parent.matrix_world.inverted()
    p = inv @ a
    d = inv @ b - p
    obj.location = p
    obj.rotation_mode = 'QUATERNION'
    obj.rotation_quaternion = d.to_track_quat('Z', 'Y')
    obj.scale = (1, 1, d.length if stretch else 1)


lengths = []
for frame in range(1, 241):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    # Check the actual rig at every retained study frame, including full yaw.
    relative = joint('TurretYaw').matrix_world.inverted() @ plate.matrix_world
    assert relative.to_quaternion().angle < .0001
    assert (relative.translation - v((0, -.08, .16))).length < .0001
    a = foot.matrix_world.translation.copy()
    b = top.matrix_world.translation.copy()
    length = (b-a).length
    assert .23 < length < 1.99, (frame, length)
    lengths.append(length)
    orient(spring, a, b)
    orient(guide, a, b, True)
    orient(seats[0], a, b)
    orient(seats[1], b, b+(b-a))
    morph.value = (length-.20)/1.8
    for obj in [spring, guide, *seats]:
        for prop in ['location', 'scale', 'rotation_quaternion']:
            obj.keyframe_insert(data_path=prop, frame=frame)
    morph.keyframe_insert(data_path='value', frame=frame)

joint('SalvoCatapult')['mechanical_revision'] = 5
scene.frame_set(65)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'source' / 'SalvoCatapult.blend'))
scene.frame_set(1)
for obj in bpy.data.objects:
    obj.animation_data_clear()
    if obj.type == 'MESH' and obj.data.shape_keys:
        obj.data.shape_keys.animation_data_clear()
for parent in [obj for obj in bpy.data.objects if obj.type == 'EMPTY']:
    meshes = [child for child in parent.children if child.type == 'MESH' and not child.data.shape_keys]
    if len(meshes) < 2: continue
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes: obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.join()
    bpy.context.object.name = parent.name + '_Surfaces'
bpy.ops.export_scene.gltf(filepath=str(ROOT / 'SalvoCatapult.glb'), export_format='GLB',
                         export_yup=True, export_apply=False, export_animations=False, export_morph=True)
print(f'Fixed plate verified through 240 study frames; spring travel {min(lengths):.6f}..{max(lengths):.6f} m.')
