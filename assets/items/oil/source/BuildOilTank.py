# Original geometry, paint and hand-drawn lettering; executed by BuildJackInTheBox.py.
tank=empty('Tank',(0,-.40,.38),box)
# Larger vessel sits behind the hydraulic columns, beneath the unchanged rack.
tank.scale=(1.15,1.25,1.25)
def barrel(name,length,radius,material,parent=tank):
 vertices=[];faces=[]
 for i in range(9):
  x=(i/8-.5)*length;r=radius*(.84+.16*math.sin(math.pi*i/8)**.35)
  for j in range(49):
   a=j*math.tau/48;vertices.append(xyz((x,r*math.cos(a),r*math.sin(a))))
 for i in range(8):
  for j in range(48):faces.append((i*49+j,i*49+j+1,(i+1)*49+j+1,(i+1)*49+j))
 faces.extend([tuple(range(48,-1,-1)),tuple(8*49+j for j in range(49))])
 mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update();mesh.materials.append(material)
 uv=mesh.uv_layers.new(name='Paint')
 for poly in mesh.polygons:
  poly.use_smooth=True
  for li in poly.loop_indices:
   vi=mesh.loops[li].vertex_index;uv.data[li].uv=(vi//49/8,vi%49/48)
 o=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(o);o.parent=parent;return o

# Painted diamonds and chipped enamel are baked, avoiding unsupported Blender graphs.
w,h=1024,512;yy,xx=np.mgrid[0:h,0:w];u=xx/w;v=yy/h
diamond=np.abs((u*9)%1-.5)/.5+np.abs((v*4)%1-.5)/.5 < .80
rng=np.random.default_rng(10152);grain=rng.random((h,w));wear=grain<.014
rgb=np.where(diamond[:,:,None],np.array((.67,.012,.018)),np.array((.87,.80,.62)))
rgb*=.87+.13*grain[:,:,None];rgb[wear]=(.09,.07,.04)
rgba=np.concatenate([rgb,np.ones((h,w,1))],axis=2).astype('float32')
im=bpy.data.images.new('Oil Tank paint',width=w,height=h);im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(root/'source/Oil Tank paint.png');im.file_format='PNG';im.save();im.pack()
paintmat=bpy.data.materials.new('Tank circus enamel');paintmat.use_nodes=True
p=paintmat.node_tree.nodes.get('Principled BSDF');p.inputs['Roughness'].default_value=.42;p.inputs['Metallic'].default_value=.15
tex=paintmat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;paintmat.node_tree.links.new(tex.outputs['Color'],p.inputs['Base Color'])
barrel('Horizontal oil tank',1.24,.25,paintmat)
for x in [-.58,.58]:
 tube('Iron retaining strap',[(x,.252*math.cos(j*math.tau/64),.252*math.sin(j*math.tau/64)) for j in range(65)],.023,steel,tank)
 tube('Brass end weld',[(x*.94,.252*math.cos(j*math.tau/64),.252*math.sin(j*math.tau/64)) for j in range(65)],.009,brass,tank)
 cube('Tank saddle',(x,-.23,0),(.10,.09,.44),steel,tank)
 cube('Saddle hanger',(x,.01,-.18),(.045,.60,.045),steel,tank)
 for a in range(8):ball('Tank end rivet',(x,.20*math.cos(a*math.tau/8),.20*math.sin(a*math.tau/8)),(.019,.019,.019),brass,tank)
for x in [-.38,.40]:
 tube('Top brass fitting',[(x,.225,.06),(x,.30,.06)],.047,brass,tank)
 tube('Fitting flange',[(x,.239,.06),(x,.255,.06)],.066,steel,tank)
tube('Filler cap',[(.40,.30,.06),(.40,.315,.06)],.074,brass,tank)
tube('Cap handle',[(.35,.33,.06),(.35,.36,.06),(.46,.36,.06),(.46,.33,.06)],.011,steel,tank)

# Visual-only liquid level: a separate bottom pivot survives rigid mesh batching.
cube('Gauge brass frame',(.43,-.005,.259),(.19,.37,.047),brass,tank,.025)
cube('Gauge black gasket',(.43,-.005,.289),(.143,.316,.016),black,tank,.023)
cube('Gauge ivory back',(.43,-.005,.300),(.119,.286,.006),cream,tank,.018)
meter=empty('GaugeLiquidLevel',(.43,-.14,.309),tank)
cube('Gauge oil sight',(0,.136,0),(.109,.272,.008),black,meter,.003)
glass=bpy.data.materials.new('Oil sight glass');glass.use_nodes=True;glass.surface_render_method='DITHERED'
gp=glass.node_tree.nodes.get('Principled BSDF');gp.inputs['Base Color'].default_value=(.53,.70,.66,.16);gp.inputs['Alpha'].default_value=.16;gp.inputs['Roughness'].default_value=.13
cube('Gauge glass pane',(.43,-.005,.322),(.119,.286,.003),glass,tank,.008)
for i in range(7):
 y=-.12+i*.038;cube('Sight scale',(.48,y,.315),(.030 if i%3==0 else .016,.004,.003),brass,tank,.001)
cube('Glass reflection',(.39,.005,.325),(.004,.248,.002),white,tank,.002)
for y in [-.158,.148]:
 for x in [.362,.498]:ball('Gauge screw',(x,y,.296),(.009,.009,.005),steel,tank)

# A plain enamel label field separates the joke from the busy diamond paint.
cube('Label brass edge',(-.10,0,.272),(.92,.40,.018),brass,tank,.032)
cube('Label ivory enamel',(-.10,0,.285),(.893,.377,.012),cream,tank,.027)
# Bespoke painted stroke alphabet; no font or reference pixels are redistributed.
glyphs={
'L':[[(0,1),(0,0),(.65,0)]], 'a':[[(0,0),(.34,1),(.68,0)],[(.15,.4),(.53,.4)]],
'u':[[(0,1),(0,.2),(.15,0),(.5,0),(.65,.2),(.65,1)]],
'g':[[(.65,.82),(.5,1),(.16,1),(0,.78),(0,.22),(.16,0),(.65,0),(.65,.48),(.38,.48)]],
'h':[[(0,0),(0,1)],[(.64,0),(.64,1)],[(0,.48),(.64,.48)]],
'i':[[(.32,0),(.32,1)],[(.12,1),(.52,1)],[(.12,0),(.52,0)]],
'n':[[(0,0),(0,1),(.66,0),(.66,1)]],
'G':[[(.65,.82),(.5,1),(.16,1),(0,.78),(0,.22),(.16,0),(.65,0),(.65,.48),(.38,.48)]],
's':[[(.64,.86),(.48,1),(.14,1),(0,.82),(.05,.62),(.56,.39),(.65,.17),(.48,0),(.12,0),(0,.12)]]}
def label(text,x,y,height):
 for letter in text:
  if letter==' ':x+=height*.4;continue
  for stroke in glyphs[letter]:
   pts=[(x+px*height,y+py*height,.307) for px,py in stroke]
   tube('Letter gold keyline',pts,.010,brass,tank);tube('Painted Laughing Gas',[(px,py,pz+.005) for px,py,pz in pts],.007,black,tank)
  x+=height*.84
label('Laughing',-.53,.035,.12)
label('Gas',-.48,-.145,.125)
# Obvious informal correction, above a red handwritten lower-case oil.
tube('Gas crossed out',[(-.50,-.135,.328),(-.20,-.035,.328)],.010,red,tank)
tube('Second strike',[(-.49,-.040,.330),(-.18,-.11,.330)],.009,red,tank)
tube('Handwritten o',[(.005,-.035,.321),(-.045,-.07,.321),(-.035,-.145,.321),(.025,-.16,.321),(.07,-.08,.321),(.04,-.035,.321),(.005,-.035,.321)],.012,red,tank)
tube('Handwritten i',[(.12,-.065,.322),(.095,-.155,.322)],.011,red,tank)
ball('Oil dot',(.13,-.025,.322),(.015,.015,.012),red,tank)
tube('Handwritten l',[(.21,.006,.322),(.17,-.15,.322),(.21,-.14,.322)],.012,red,tank)

# Stepped hex nuts, dark gaskets and scored brass collars read as hose adapters.
def coupling(name,start,end,parent):
 a=Vector(start);b=Vector(end);axis=(b-a).normalized()
 for t,r,length,material,sides in [(0,.070,.018,steel,24),(.22,.061,.035,brass,6),(.57,.048,.042,brass,24),(.78,.050,.009,steel,24),(.91,.043,.022,brass,24)]:
  bpy.ops.mesh.primitive_cylinder_add(vertices=sides,radius=r,depth=length)
  o=bpy.context.object;finish(o,name,material,parent,tuple(a.lerp(b,t)));o.rotation_mode='QUATERNION';o.rotation_quaternion=Vector(xyz(axis)).to_track_quat('Z','Y')
  bevel=o.modifiers.new('Worn adapter edges','BEVEL');bevel.width=.003;bevel.segments=2;o.modifiers.new('Adapter normals','WEIGHTED_NORMAL')
coupling('Tank supply adapter',(-.38,.23,.06),(-.38,.30,.06),tank)
coupling('Box feed adapter',(-.405,.38,.10),(-.50,.38,.10),box)
coupling('Spring hose adapter',(-.29,.53,.16),(-.29,.635,.16),box)
coupling('Temple hose adapter',(-.22,.20,-.015),(-.33,.20,-.015),head)
