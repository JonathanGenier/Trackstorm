"""Original Trackstorm Proxy Mine. Run with Blender --background --python.

Editable components remain in the .blend; only material-batched body and beacon
are exported. Metres, Z up in Blender, Y up in Godot. No collision is exported.
"""
from pathlib import Path
from math import sin, cos, pi
import hashlib
import json
import bpy
import bmesh
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.preferences.filepaths.save_version = 0
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
art = bpy.data.collections.new('Proxy Mine - editable components')
scene.collection.children.link(art)

# Original deterministic patina; no external images. Broad paint fields carry
# the identity; restrained pits and scratches reward close inspection.
n = 512
y, x = np.mgrid[0:n, 0:n].astype(np.float32) / n
rng = np.random.default_rng(249)
cloud = (np.sin(x*43+np.sin(y*37)*2) * np.sin(y*61+x*11) + 1)/2
grain = rng.random((n, n))
shade = .78 + .16*cloud + .06*grain
shade[grain > .992] = .34
scratches = (np.sin((x+y*.21)*840) > .999) & (cloud > .63)
shade[scratches] = .45
rgba = np.stack([shade, shade, shade, np.ones_like(shade)], axis=-1).astype(np.float32)
image = bpy.data.images.new('Original metal patina', width=n, height=n)
image.pixels.foreach_set(rgba.ravel())
image.filepath_raw = str(ROOT/'Patina.png')
image.file_format = 'PNG'
image.save()
image.pack()

def material(name, color, metal, rough):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bs = m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*color, 1)
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    # A colored copy makes the exported PBR material portable without shaders.
    tex = bpy.data.images.new(name+' patina', width=n, height=n)
    pixels = rgba.copy()
    pixels[:, :, :3] *= np.array(color)
    tex.pixels.foreach_set(pixels.ravel())
    tex.pack()
    node = m.node_tree.nodes.new('ShaderNodeTexImage')
    node.image = tex
    m.node_tree.links.new(node.outputs['Color'], bs.inputs['Base Color'])
    return m

iron = material('Gunmetal', (.19,.215,.23), .78, .39)
dark = material('Recess', (.045,.054,.059), .6, .57)
red = material('Circus oxblood', (.29,.025,.013), .48, .48)
bone = material('Aged ivory', (.59,.49,.31), .32, .56)
copper = material('Magnet copper', (.39,.21,.09), .83, .34)
edge = material('Worn steel', (.42,.46,.47), .82, .32)
beacon = bpy.data.materials.new('Beacon glass')
beacon.diffuse_color = (.7,.015,.008,1)
beacon.use_nodes = True
bs = beacon.node_tree.nodes.get('Principled BSDF')
bs.inputs['Base Color'].default_value = beacon.diffuse_color
bs.inputs['Emission Color'].default_value = (1,.01,.002,1)
bs.inputs['Emission Strength'].default_value = 2

def mesh(name, verts, faces, mat, bevel=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.000001)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    data.validate()
    data.update()
    obj = bpy.data.objects.new(name, data)
    art.objects.link(obj)
    data.materials.append(mat)
    if bevel:
        mod = obj.modifiers.new('Machined edge bevel', 'BEVEL')
        mod.width = bevel
        mod.segments = 1
        mod = obj.modifiers.new('Weighted hard-surface normals', 'WEIGHTED_NORMAL')
        mod.keep_sharp = True
    return obj

def sector(name, profile, a, b, mat, steps=12, bevel=.004):
    # Closed cross-section swept over an angle; each sector is independently editable.
    verts = [(r*cos(t),r*sin(t),z) for z,r in profile
             for t in [a+(b-a)*k/steps for k in range(steps+1)]]
    faces = []
    q = steps+1
    for j in range(len(profile)):
        jj = (j+1) % len(profile)
        for k in range(steps):
            faces.append((j*q+k,j*q+k+1,jj*q+k+1,jj*q+k))
    faces += [tuple(j*q for j in reversed(range(len(profile)))),
              tuple(j*q+steps for j in range(len(profile)))]
    return mesh(name, verts, faces, mat, bevel)

def ring(name, z, r, height, thick, mat, steps=48):
    return sector(name, [(z-height/2,r-thick),(z-height/2,r),
                        (z+height/2,r),(z+height/2,r-thick)], 0,2*pi,mat,steps,.002)

def bolt(name, angle, r, z):
    bpy.ops.mesh.primitive_cylinder_add(vertices=6, radius=.016, depth=.012,
        location=(r*cos(angle),r*sin(angle),z))
    obj = bpy.context.object
    obj.name = name
    for col in list(obj.users_collection): col.objects.unlink(obj)
    art.objects.link(obj)
    obj.data.materials.append(edge)

# The base is exactly at -0.25 m, matching the existing cylinder centre.
sector('Sealed lower hull', [(-.25,0),(-.25,.44),(-.19,.59),(-.08,.62),
       (.085,.60),(.18,.43),(.19,0)],0,2*pi,dark,64,.006)
ring('Lower bumper',-.178,.627,.052,.045,iron)
ring('Upper equatorial armor rail',.072,.642,.035,.035,edge)
ring('Lower equatorial armor rail',-.072,.642,.035,.035,iron)

for i in range(12):
    a = i*2*pi/12
    paint = red if i % 3 == 0 else bone if i % 3 == 1 else iron
    sector('Crown armor %02d'%i, [(.078,.625),(.126,.598),(.194,.49),(.235,.32),(.237,.225),
           (.208,.218),(.202,.31),(.166,.48),(.10,.578),(.055,.604)],a+.025,a+2*pi/12-.025,paint,5,.008)
    sector('Lower armor %02d'%i, [(-.232,.43),(-.175,.615),(-.105,.64),
           (-.095,.615),(-.163,.586),(-.215,.42)],a+.075,a+2*pi/12-.075,iron,4,.005)
    bolt('Crown rivet %02d'%i,a+pi/12,.48,.2)
    if i % 2 == 0:
        # Exposed stacked windings, framed by the bumper rails and radial clamps.
        for j in range(5):
            sector('Magnetic winding %02d-%d'%(i,j),[(-.054+j*.027,.602),
                (-.054+j*.027,.628),(-.038+j*.027,.628),(-.038+j*.027,.602)],
                a+.018,a+pi/6-.018,copper,8,0)
    else:
        sector('Circus band %02d'%i,[(-.055,.614),(-.055,.636),(.052,.636),(.052,.614)],
               a+.014,a+pi/6-.014,red,8,.002)
        # Three bold ivory teeth are actual surface geometry, not tiny decals.
        for j in range(3):
            t = a+.025+j*.155
            points=[(t,-.052),(t+.146,-.052),(t+.073,.05)]
            mesh('Ivory hazard tooth %02d-%d'%(i,j),
                 [(.638*cos(p),.638*sin(p),z) for p,z in points],[(0,1,2)],bone)
    sector('Radial clamp %02d'%i,[(-.098,.619),(-.098,.649),(.109,.636),(.109,.613)],
           a-.021,a+.021,iron,2,.004)
    if i % 2 == 0:
        sector('Heavy crown brace %02d'%i,[(.071,.638),(.146,.606),(.211,.494),(.248,.31),
               (.224,.301),(.185,.478),(.12,.58),(.062,.615)],a-.035,a+.035,iron,2,.004)

ring('Beacon socket',.224,.224,.041,.068,iron)
ring('Beacon bezel',.25,.176,.023,.023,edge)
# Wide, low lens: maximum top height .35 m; no tall snagging appendages.
lens = sector('Beacon',[(.241,0),(.241,.156),(.283,.15),(.319,.115),(.332,.05),(.333,0)],
              0,2*pi,beacon,48,0)
for poly in lens.data.polygons: poly.use_smooth=True
for i in range(4):
    a=i*pi/2
    sector('Beacon guard %d'%i,[(.234,.19),(.292,.176),(.346,.103),
           (.331,.096),(.279,.158),(.234,.17)],a-.055,a+.055,iron,3,.003)
ring('Beacon guard crown',.34,.105,.016,.022,iron,32)

# Author UVs and retain the editable components/modifiers in the source.
for obj in art.objects:
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(island_margin=.015)
    bpy.ops.object.mode_set(mode='OBJECT')
scene['Presentation contract']='Original 0.65 m radius / 0.25 m half-height collision unchanged. Body origin at collider centre. Beacon is cosmetic; no collider, radius display or physics animation.'
scene['Art direction']='Approved TS-249 imagegen concept: red/ivory segmented armor, recessed copper coils, protected red beacon; compressed to existing footprint.'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/ProxyMine.blend'))

# Material batching is export-only; master retains individually editable pieces.
exports=[]
for obj in list(art.objects):
    copy=obj.copy()
    copy.data=obj.data.copy()
    scene.collection.objects.link(copy)
    bpy.context.view_layer.objects.active=copy
    for modifier in list(copy.modifiers): bpy.ops.object.modifier_apply(modifier=modifier.name)
    exports.append(copy)
body=[o for o in exports if not o.name.startswith('Beacon.')]
export_lens=[o for o in exports if o not in body][0]
bpy.ops.object.select_all(action='DESELECT')
for obj in body: obj.select_set(True)
bpy.context.view_layer.objects.active=body[0]
bpy.ops.object.join()
body=body[0]
body.name='ArmoredBody'
export_lens.name='BeaconLens'
body.select_set(True)
export_lens.select_set(True)
bpy.ops.export_scene.gltf(filepath=str(ROOT/'ProxyMine.glb'),export_format='GLB',
    use_selection=True,export_yup=True,export_animations=False,export_cameras=False,
    export_lights=False,export_image_format='AUTO')
for obj in [body,export_lens]: obj.data.calc_loop_triangles()
vertices=[obj.matrix_world@v.co for obj in [body,export_lens] for v in obj.data.vertices]
audit={'triangles':sum(len(o.data.loop_triangles) for o in [body,export_lens]),
    'mesh_nodes':2,'body_surfaces':len(body.data.materials),
    'max_radial_m':max((v.x*v.x+v.y*v.y)**.5 for v in vertices),
    'min_z_m':min(v.z for v in vertices),'max_z_m':max(v.z for v in vertices),
    'collision_nodes':0}
print('PROXY_MINE_BOUNDS '+json.dumps(audit),flush=True)
assert audit['max_radial_m'] <= .651 and audit['min_z_m'] >= -.251
manifest={'asset':'Production Proxy Mine','author':'Original Trackstorm project art',
    'tool':'Blender '+bpy.app.version_string,'source_url':None,
    'license':'Project-original geometry and procedural textures; no third-party acquisition or license asserted',
    'reference':'reference/ApprovedProxyMineConcept.png; built-in imagegen concept approved by user in TS-249 chat on 2026-09-29; reference only, no pixels in runtime asset',
    'coordinates':'Metres. Blender Z up -> Godot Y up. Origin at unchanged collider centre.',
    'audit':audit,'sha256':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest()
      for p in [ROOT/'ProxyMine.glb',ROOT/'source/ProxyMine.blend',ROOT/'source/BuildProxyMine.py',ROOT/'Patina.png',ROOT/'reference/ApprovedProxyMineConcept.png']}}
(ROOT/'sources.json').write_text(json.dumps(manifest,indent=2)+'\n')
print('PROXY_MINE_AUTHORING_COMPLETE '+json.dumps(audit),flush=True)
