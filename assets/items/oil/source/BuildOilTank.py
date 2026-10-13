# Original geometry, paint and hand-drawn lettering; executed by BuildJackInTheBox.py.
tank=empty('Tank',(0,-.40,0),box)
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

# A fixed decorative sight glass, not a new ammunition display.
cube('Gauge brass frame',(.43,-.005,.259),(.19,.37,.047),brass,tank,.025)
cube('Gauge black gasket',(.43,-.005,.289),(.143,.316,.016),black,tank,.023)
cube('Gauge ivory back',(.43,-.005,.300),(.119,.286,.006),cream,tank,.018)
cube('Gauge oil sight',(.43,-.057,.306),(.109,.176,.008),black,tank,.01)
for i in range(7):
 y=-.12+i*.038;cube('Sight scale',(.48,y,.315),(.030 if i%3==0 else .016,.004,.003),brass,tank,.001)
cube('Glass reflection',(.40,.005,.314),(.008,.248,.003),white,tank,.003)
for y in [-.158,.148]:
 for x in [.362,.498]:ball('Gauge screw',(x,y,.296),(.009,.009,.005),steel,tank)

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
   pts=[(x+px*height,y+py*height,.273) for px,py in stroke]
   tube('Letter dark outline',pts,.007,black,tank);tube('Painted Laughing Gas',[(px,py,pz+.003) for px,py,pz in pts],.004,brass,tank)
  x+=height*.84
label('Laughing Gas',-.52,.005,.083)
# Obvious informal correction, above a red handwritten lower-case oil.
tube('Gas crossed out',[(.12,.012,.293),(.31,.097,.293)],.009,red,tank)
tube('Second strike',[(.11,.078,.295),(.31,.034,.295)],.006,black,tank)
tube('Handwritten o',[(.12,-.095,.256),(.10,-.12,.246),(.105,-.16,.226),(.14,-.165,.223),(.16,-.13,.241),(.15,-.10,.254),(.12,-.095,.256)],.008,red,tank)
tube('Handwritten i',[(.19,-.10,.254),(.18,-.16,.226)],.007,red,tank)
ball('Oil dot',(.196,-.076,.266),(.009,.009,.009),red,tank)
tube('Handwritten l',[(.238,-.067,.269),(.22,-.16,.226),(.239,-.156,.228)],.008,red,tank)

# Visible endpoints seal the passive runtime hoses into the tank, box and temple.
tube('Box feed brass ferrule',[(-.405,.38,.10),(-.45,.38,.10)],.047,brass,box)
tube('Spring hose gland',[(-.29,.53,.16),(-.29,.60,.16)],.039,brass,box)
tube('Temple hose fitting',[(-.22,.20,-.015),(-.28,.20,-.015)],.041,brass,head)
