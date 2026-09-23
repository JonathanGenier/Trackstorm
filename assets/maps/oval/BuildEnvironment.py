"""Original TS-100 conifer meshes and grass maps; Blender metres, deterministic seed.
Run Blender --background --python-exit-code 1 --python assets/maps/oval/BuildEnvironment.py.
Foundation geometry is read-only and remains owned by BuildOval.py.
"""
import bpy
import bmesh
import math
import random
import numpy as np
from pathlib import Path
from mathutils import Vector
ROOT = Path(__file__).resolve().parent
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1
rng = random.Random(100)
materials = []
for name, color in [('Bark', (.16,.105,.064)), ('NeedlesDark', (.045,.10,.063)), ('Needles', (.068,.145,.083)), ('NeedlesSun', (.09,.17,.09))]:
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color,1)
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (*color,1)
    m.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .94
    materials.append(m)
ico=bmesh.new()
bmesh.ops.create_icosphere(ico,subdivisions=1,radius=1)
ico.verts.ensure_lookup_table()
ico.verts.index_update()
clump_vertices=[tuple(v.co) for v in ico.verts]
clump_faces=[tuple(v.index for v in f.verts) for f in ico.faces]
ico.free()
for variant in range(3):
    verts, faces, slots = [], [], []
    def cone(center, radius, height, sides, material, phase=0):
        start = len(verts)
        x,y,z = center
        for i in range(sides):
            angle = 2*math.pi*i/sides + phase
            r = radius*rng.uniform(.83,1.13)
            verts.append((x+math.cos(angle)*r,y+math.sin(angle)*r,z+rng.uniform(-.12,.12)))
        verts.append((x,y,z+height))
        for i in range(sides):
            faces.append((start+i,start+(i+1)%sides,start+sides)); slots.append(material)
        faces.append(tuple(start+i for i in reversed(range(sides)))); slots.append(material)
    def clump(center, scale, material):
        start=len(verts)
        for point in clump_vertices:
            verts.append(tuple(center[axis]+point[axis]*scale[axis]*rng.uniform(.88,1.12) for axis in range(3)))
        for face in clump_faces:
            faces.append(tuple(start+i for i in face)); slots.append(material)
    def branch(start, end, radius):
        axis=Vector(end)-Vector(start)
        side=axis.normalized().cross(Vector((0,0,1))).normalized()*radius
        up=axis.normalized().cross(side).normalized()*radius
        first=len(verts)
        for center,width in [(Vector(start),1),(Vector(end),.2)]:
            for i in range(5):
                angle=i*math.tau/5
                verts.append(tuple(center+(side*math.cos(angle)+up*math.sin(angle))*width))
        for i in range(5):
            faces.append((first+i,first+(i+1)%5,first+(i+1)%5+5,first+i+5)); slots.append(0)
    # Distinct mature fir, open-crowned pine and young spruce silhouettes.
    height = [16,14,8][variant]
    cone((0,0,0),.24,height,9,0)
    if variant == 1:
        for limb in range(13):
            angle=limb*2.4+rng.uniform(-.25,.25)
            z=6.0+limb*.52
            reach=rng.uniform(2.0,3.8)*(1-(z-6)/13)
            end=(math.cos(angle)*reach,math.sin(angle)*reach,z+rng.uniform(.3,1.2))
            branch((0,0,z-1),end,.11)
            clump(end,(rng.uniform(1.2,1.9),rng.uniform(1.2,1.9),rng.uniform(.75,1.3)),1+limb%3)
            clump((end[0]*.6,end[1]*.6,end[2]+.6),(1.4,1.3,1.0),2)
        clump((.15,-.1,height-.9),(1.6,1.45,1.6),2)
    else:
        tiers=9 if variant == 0 else 7
        for tier in range(tiers):
            z=1.4+tier*(height-2.4)/tiers+rng.uniform(-.18,.18)
            radius=(height-z)*(.23 if variant == 0 else .27)*rng.uniform(.87,1.1)
            cone((rng.uniform(-.14,.14),rng.uniform(-.14,.14),z),radius,radius*.75+.8,9,2,rng.random()*math.tau)
            for limb in range(5):
                angle=limb*math.tau/5+tier*2.4
                reach=radius*.64
                clump((math.cos(angle)*reach,math.sin(angle)*reach,z+.12),(radius*.5,radius*.42,.48),1+rng.randrange(3))
        cone((.05,0,height-1.4),.5,1.8,7,2)
    mesh=bpy.data.meshes.new('Conifer'+str(variant)); mesh.from_pydata(verts,[],faces); mesh.update()
    obj=bpy.data.objects.new('Conifer'+str(variant),mesh); bpy.context.collection.objects.link(obj)
    for m in materials: mesh.materials.append(m)
    for p,slot in zip(mesh.polygons,slots): p.material_index=slot
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'source/OvalEnvironment.blend'))
bpy.ops.export_scene.gltf(filepath=str(ROOT/'conifers.glb'), export_format='GLB', export_yup=True, export_animations=False)
# Original seamless 2 m grass patch with fine blade detail, soil flecks and broad variation.
size=1024
nr=np.random.default_rng(100)
y,x=np.mgrid[0:size,0:size]
base=.5+nr.random((size,size))*.16
for i in range(34000):
    xx,yy=nr.integers(0,size,2); length=int(nr.integers(3,14)); slope=nr.uniform(-.5,.5)
    shade=nr.uniform(.18,.94)
    for step in range(length): base[(yy+step)%size,(xx+int(step*slope))%size]=shade
rgb=np.stack((.14+base*.19,.18+base*.24,.055+base*.095),axis=-1)
def save(name,values):
    image=bpy.data.images.new(name,width=size,height=size,alpha=True)
    rgba=np.ones((size,size,4),dtype=np.float32); rgba[:,:,:3]=values
    image.pixels.foreach_set(rgba.ravel()); image.filepath_raw=str(ROOT/(name+'.png')); image.file_format='PNG'; image.save()
save('grass_albedo',rgb)
dx=(np.roll(base,-1,axis=1)-np.roll(base,1,axis=1))*.8
dy=(np.roll(base,-1,axis=0)-np.roll(base,1,axis=0))*.8
normal=np.stack((-dx,-dy,np.ones_like(base)),axis=-1); normal/=np.linalg.norm(normal,axis=-1)[:,:,None]
save('grass_normal',normal*.5+.5)
# A separate 64 m macro layer breaks up distant repetition without enlarging grain/blades.
macro=np.zeros((size,size))
for cells,weight in [(8,.5),(16,.3),(32,.15),(64,.05)]:
    grid=nr.random((cells,cells))
    coordinate=np.arange(size)*cells/size
    index=coordinate.astype(int); fraction=coordinate-index
    fraction=fraction*fraction*(3-2*fraction)
    horizontal=grid[:,index]*(1-fraction)+grid[:,(index+1)%cells]*fraction
    macro+=(horizontal[index,:]*(1-fraction[:,None])+horizontal[(index+1)%cells,:]*fraction[:,None])*weight
macro=.55+macro*.45
save('ground_macro',np.repeat(macro[:,:,None],3,axis=2))
# Grass has restrained, isotropic colour variation instead of metre-wide high-contrast blobs.
frequency=np.fft.fftfreq(size)
radius=np.sqrt(frequency[:,None]**2+frequency[None,:]**2)
field=np.fft.ifft2(np.fft.fft2(nr.random((size,size)))*np.exp(-(radius/.025)**2)).real
field=(field-field.mean())/field.std()
variation=np.clip(.975+field*.009,.95,1.0)
save('grass_variation',np.stack((variation,variation,variation*.997),axis=-1))
print('Authored three conifers and original seamless grass maps.')
