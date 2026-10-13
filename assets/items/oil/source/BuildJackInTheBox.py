import bpy, math, random
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1]
import numpy as np
noise=np.random.default_rng(245).random((256,256))
for k in [2,4,8]: noise += np.repeat(np.repeat(np.random.default_rng(245+k).random((256//k,256//k)),k,0),k,1)
noise=np.clip(noise/4,.22,1)
paint=np.stack([noise,noise,noise,np.ones_like(noise)],axis=-1).astype('float32')
patina=bpy.data.images.new('Original oil patina',width=256,height=256);patina.pixels.foreach_set(paint.ravel());patina.filepath_raw=str(root/'source/Patina.png');patina.file_format='PNG';patina.save();patina.pack()
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
random.seed(245)
def xyz(p): return (p[0],-p[2],p[1])
def mat(name,color,metal=0,rough=.5):
 m=bpy.data.materials.new(name);m.diffuse_color=(*color,1);m.use_nodes=True;p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metal;p.inputs['Roughness'].default_value=rough
 # Bake the color into the original patina: glTF cannot carry Blender's partial
 # MixRGB graph, which previously exported grayscale and discarded the tint.
 rgba=paint.copy();rgba[:,:,:3]=np.array(color)*(0.82+0.18*noise[:,:,None])
 image=bpy.data.images.new('Oil '+name,width=256,height=256);image.pixels.foreach_set(rgba.ravel());image.filepath_raw=str(root/'source'/('Oil '+name+'.png'));image.file_format='PNG';image.save();image.pack()
 tex=m.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
 m.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color']);return m
red=mat('Weathered crimson',(.72,.012,.018),.08,.36);cream=mat('Aged ivory',(.93,.91,.85),0,.38);black=mat('Black enamel',(.009,.007,.008),.12,.28);steel=mat('Worn iron',(.10,.095,.08),.8,.26);brass=mat('Old brass',(.72,.43,.075),.82,.22);white=mat('Teeth ivory',(.96,.95,.91),0,.25)
def empty(name,pos,parent=None):
 o=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(o);o.location=xyz(pos);o.parent=parent;return o
box=empty('OilBox',(0,0,0));lid=empty('LidPivot',(0,.68,-.39),box);head=empty('JesterPivot',(0,1.42,0),box);spring=empty('Spring',(0,.105,0),box)
def finish(o,name,material,parent,pos):
 o.name=name;o.data.materials.append(material);o.parent=parent;o.location=xyz(pos);return o
def cube(name,pos,size,material,parent=box,bevel=.015):
 bpy.ops.mesh.primitive_cube_add();o=bpy.context.object;finish(o,name,material,parent,pos);o.scale=(size[0]/2,size[2]/2,size[1]/2);bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
 if bevel: mod=o.modifiers.new('Machined edges','BEVEL');mod.width=bevel;mod.segments=2;o.modifiers.new('Normals','WEIGHTED_NORMAL')
 return o
def ball(name,pos,size,material,parent=head):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16);o=bpy.context.object;finish(o,name,material,parent,pos);o.scale=(size[0],size[2],size[1]);
 for p in o.data.polygons:p.use_smooth=True
 return o
def tube(name,points,radius,material,parent):
 c=bpy.data.curves.new(name,'CURVE');c.dimensions='3D';c.bevel_depth=radius;c.bevel_resolution=3;s=c.splines.new('POLY');s.points.add(len(points)-1)
 for q,p in zip(s.points,points):q.co=(*xyz(p),1)
 o=bpy.data.objects.new(name,c);bpy.context.collection.objects.link(o);o.parent=parent;c.materials.append(material);return o
# Square hollow crate, metal bands, rivets and diamond paint.
cube('Floor',(0,.05,0),(.82,.1,.82),steel)
for side in range(4):
 ang=side*math.pi/2;out=Vector((math.sin(ang),0,math.cos(ang)));axis=Vector((math.cos(ang),0,-math.sin(ang)))
 panel=cube('Ivory panel '+str(side),tuple(out*.385+Vector((0,.38,0))),(.75,.62,.035),cream);panel.rotation_euler[2]=-ang
 for i in range(-2,3):
  for j in range(2):
   p=out*.407+axis*(i*.145)+Vector((0,.21+j*.27,0));d=cube('Crimson diamond',tuple(p),(.1,.17,.008),red,bevel=.003);d.rotation_euler[2]=-ang;d.rotation_euler[1]=math.pi/4
 for y in [.09,.65]:
  p=out*.416+Vector((0,y,0));o=cube('Border strap',tuple(p),(.84,.06,.025),brass);o.rotation_euler[2]=-ang
  for x in [-.34,-.17,0,.17,.34]:ball('Rivet',tuple(p+axis*x),(.013,.013,.013),brass,box)
 for x in [-.38,.38]:
  o=cube('Corner strap',tuple(out*.414+axis*x+Vector((0,.37,0))),(.055,.6,.025),brass);o.rotation_euler[2]=-ang
# Original painted jester crest on the rear panel; reference pixels are never used.
ball('Crate mask',(0,.37,.435),(.14,.19,.012),cream,box)
for side in [-1,1]:
 ball('Crest eye',(side*.047,.41,.45),(.037,.043,.008),black,box)
 brow=cube('Crest brow',(side*.047,.463,.448),(.077,.015,.006),black,box,.001);brow.rotation_euler[1]=-side*.45
 ball('Crest iris',(side*.047,.41,.459),(.012,.014,.006),brass,box)
vertices=[]
for i in range(25):
 x=(i/24-.5)*.21;u=x/.105
 for y in [.29+.075*u*u,.24+.125*u*u]:vertices.append(xyz((x,y,.456)))
mesh=bpy.data.meshes.new('Crest smile');mesh.from_pydata(vertices,[],[(i*2,i*2+1,i*2+3,i*2+2) for i in range(24)]);mesh.update()
o=bpy.data.objects.new('Painted scary smile',mesh);bpy.context.collection.objects.link(o);o.parent=box;mesh.materials.append(black)
ball('Crest nose',(0,.365,.46),(.03,.028,.014),red,box)
for j in range(9):
 x=(j-4)*.019;cube('Crest tooth',(x,.276+.075*(x/.105)**2,.464),(.014,.022,.003),white,box,.001)
# Lid is authored open; runtime owns one rigid hinge.
cube('Lid plate',(0,0,.39),(.86,.06,.86),red,lid)
cube('Lid ivory inset',(0,.032,.39),(.72,.012,.70),cream,lid,.005)
for x in [-.22,0,.22]:
 for z in [.17,.39,.61]:
  d=cube('Lid crimson diamond',(x,.043,z),(.14,.008,.14),red,lid,.002);d.rotation_euler[2]=math.pi/4
for x in [-.4,.4]:cube('Lid brace',(x,.025,.39),(.04,.035,.82),brass,lid)
for z in [0,.8]:cube('Lid brace',(0,.025,z),(.82,.035,.04),brass,lid)
for x in [-.26,.26]:
 cube('Brass hinge leaf',(x,.61,-.422),(.14,.14,.028),brass)
 tube('Brass hinge barrel',[(x-.085,.68,-.405),(x+.085,.68,-.405)],.025,brass,box)
cube('Brass front clasp',(0,.60,.438),(.12,.14,.026),brass)
ball('Clasp pin',(0,.62,.459),(.022,.022,.012),steel,box)
# Stable helical spring between the box floor and the collar.
pts=[(.16*math.cos(i*math.tau*7/224),i*1.145/224,.16*math.sin(i*math.tau*7/224)) for i in range(225)]
tube('SpringCoil',pts,.027,steel,spring)
# Project-authored sculpted jester: cheek planes, brows, pointed nose, black eyes, grin and teeth.
ball('Face',(0,.16,0),(.24,.29,.2),white)
ball('Jaw',(0,-.045,.055),(.175,.13,.18),white)
# Upturned, wide grin is a shaped surface, not a downturned oval mouth.
def grin(name,material,yshift,z,scale=1):
 vertices=[]
 for i in range(33):
  x=(i/32-.5)*.35;u=x/.175
  top=.012+.115*u*u;bottom=-.073+.20*u*u
  for y in [top,bottom]:vertices.append(xyz((x*scale,(y+yshift)*scale,z-.085*u*u)))
 faces=[(i*2,i*2+1,i*2+3,i*2+2) for i in range(32)]
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update()
 o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);o.parent=head;mesh.materials.append(material)
grin('Carmine smile outline',red,0,.242,1.12);grin('Deep upturned grin',black,0,.249)
for i in range(11):
 x=(i-5)*.027;u=x/.175;y=.010+.115*u*u
 tooth=cube('Smile upper tooth',(x,y-.014,.254-.085*u*u),(.022,.025,.010),white,head,.003)
 tooth.rotation_euler[1]=-.6*u
for i in range(9):
 x=(i-4)*.027;u=x/.175;y=-.068+.20*u*u
 cube('Smile lower tooth',(x,y+.010,.254-.085*u*u),(.020,.018,.010),white,head,.002)
for side in [-1,1]:
 ball('High smiling cheek',(side*.18,.14,.13),(.074,.063,.072),white)
 eye=ball('Eye makeup',(side*.092,.245,.176),(.065,.074,.029),black);eye.rotation_euler[1]=-side*.30
 ball('Staring iris',(side*.084,.239,.205),(.022,.022,.010),brass)
 ball('Pupil',(side*.084,.239,.215),(.010,.013,.005),black)
 ball('Eye glint',(side*.080,.249,.220),(.006,.007,.003),white)
 brow=ball('Menacing brow',(side*.089,.309,.174),(.101,.021,.032),black);brow.rotation_euler[1]=-side*.40
 ball('Ear',(side*.255,.19,-.025),(.05,.10,.05),white)
 tube('Smile crease',[(side*.14,.13,.205),(side*.18,.10,.18),(side*.20,.04,.16)],.009,red,head)
# Thin radial pleats form a flat clown ruff and leave the spring visible below it.
vertices=[];faces=[]
for i in range(65):
 a=i*math.tau/64
 for r in [.075,.23,.29]:
  vertices.append(xyz((r*math.cos(a),-.17+.025*math.cos(a*12)*(r/.29),r*.85*math.sin(a))))
for i in range(64):
 for j in range(2):faces.append((i*3+j,i*3+j+1,(i+1)*3+j+1,(i+1)*3+j))
mesh=bpy.data.meshes.new('Pleated ruff');mesh.from_pydata(vertices,[],faces);mesh.update()
o=bpy.data.objects.new('Flat clown collar',mesh);bpy.context.collection.objects.link(o);o.parent=head;mesh.materials.append(cream);mesh.materials.append(red)
for p in mesh.polygons:p.material_index=1 if p.index//2%16==0 else 0
mod=o.modifiers.new('Cloth thickness','SOLIDIFY');mod.thickness=.007
ball('Nose',(0,.15,.238),(.058,.055,.059),red)
# Continuous tapered cloth horns: shared rings cannot separate during head motion.
ball('Joined hat cap',(0,.365,-.025),(.23,.115,.18),black)
for side in [-1,1]:
 vertices=[];faces=[]
 for i in range(33):
  t=i/32;x=side*(.09+.39*t);y=.38+.24*math.sin(t*math.pi)-.10*t;r=.145*(1-t)**.85+.018
  tangent=Vector((side*.39,.24*math.pi*math.cos(t*math.pi)-.10,0)).normalized();right=Vector((0,0,1));up=tangent.cross(right)
  for j in range(16):
   a=j*math.tau/16;p=Vector((x,y,-.025))+r*(right*math.cos(a)+up*math.sin(a));vertices.append(xyz(p))
 for i in range(32):
  for j in range(16):faces.append((i*16+j,i*16+(j+1)%16,(i+1)*16+(j+1)%16,(i+1)*16+j))
 mesh=bpy.data.meshes.new('Continuous horn');mesh.from_pydata(vertices,[],faces);mesh.update();mesh.materials.append(red);mesh.materials.append(black)
 o=bpy.data.objects.new('Joined red black horn '+str(side),mesh);bpy.context.collection.objects.link(o);o.parent=head
 for p in mesh.polygons:p.material_index=(p.index//16//5+p.index%16//8)%2;p.use_smooth=True
 tube('Bell link',[(side*.48,.28,-.025),(side*.48,.225,-.025)],.018,brass,head)
 ball('Gold bell',(side*.48,.21,-.025),(.058,.058,.058),brass)
 tube('Bell slit',[(side*.48-.034,.19,.026),(side*.48+.034,.19,.026)],.006,black,head)
# Paint wear consists of original geometry, no external textures.
for i in range(65):
 x=random.uniform(-.36,.36);y=random.uniform(.12,.61);cube('Paint chip',(x,y,.408),(random.uniform(.005,.025),random.uniform(.003,.012),.001),steel,box,0)
bpy.ops.wm.save_as_mainfile(filepath=str(root/'source/JackInTheBox.blend'))
for parent in [box,lid,head,spring]:
 objects=[o for o in bpy.context.scene.objects if o.parent==parent and o.type in {'MESH','CURVE'}]
 bpy.ops.object.select_all(action='DESELECT')
 for o in objects:o.select_set(True)
 if objects:
  bpy.context.view_layer.objects.active=objects[0];bpy.ops.object.convert(target='MESH');bpy.ops.object.join();bpy.context.object.name=parent.name+'Geometry'
bpy.ops.export_scene.gltf(filepath=str(root/'JackInTheBox.glb'),export_format='GLB',export_apply=True,export_cameras=False,export_lights=False)
