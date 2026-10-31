shader_type spatial;
uniform sampler2D artwork : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform mat4 rest_transform;
uniform int assembly = 0; // 1: pitched gun, 2: yawing drum, 3: weapon cradle
uniform int finish = 0; // 0: iron, 1: steel, 2: edge, 3: recess
uniform float side = 1.0;
varying vec3 rest_position;
varying vec3 rest_normal;

float hash(vec3 p) { return fract(sin(dot(p, vec3(127.1,311.7,74.7))) * 43758.5453); }
float noise(vec3 p) {
 vec3 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
 return mix(mix(mix(hash(i),hash(i+vec3(1,0,0)),f.x),mix(hash(i+vec3(0,1,0)),hash(i+vec3(1,1,0)),f.x),f.y),
 mix(mix(hash(i+vec3(0,0,1)),hash(i+vec3(1,0,1)),f.x),mix(hash(i+vec3(0,1,1)),hash(i+vec3(1,1,1)),f.x),f.y),f.z);
}
vec3 paint(vec2 uv, vec2 tile) {
 return texture(artwork, (tile + clamp(uv,vec2(.005),vec2(.995))) * .5).rgb;
}
void vertex() {
 rest_position = (rest_transform * vec4(VERTEX,1.0)).xyz;
 rest_normal = normalize(mat3(rest_transform) * NORMAL);
}
void fragment() {
 vec3 p=rest_position, n=abs(normalize(rest_normal));
 vec3 gold=vec3(.29,.185,.065), red=vec3(.14,.008,.012), iron=vec3(.012,.019,.023);
 vec3 color=iron;
 float painted=0.0;
 float edge_wear=0.0;
 if (finish == 2) { color=gold; }
 if (finish == 3) { color=vec3(.006,.009,.011); }
 if (assembly == 1 && finish != 3) {
  float x=p.x-side*.28896;
  // Receiver box: independent planar faces, matching physical motif scale.
  if (p.z > -.65 && abs(x)<.24) {
   vec2 uv=vec2((p.z+.65)/.89, .5-p.y/.40);
   if(n.y>n.x && n.y>n.z) { uv=vec2((p.z+.65)/.89, x/.40+.5); }
   if(n.z>n.x && n.z>n.y) { uv=vec2(x/.40+.5,.5-p.y/.40); }
   color=paint(vec2(uv.x,uv.y*.5),vec2(0)); painted=1.0;
   float border=min(min(uv.x,1.0-uv.x),min(uv.y,1.0-uv.y));
   edge_wear=1.0-smoothstep(.025,.08,border);
   if(border<.045) { color=gold; painted=0.0; }
   if(abs(x)>.20 && n.x>.65) { color=paint(uv,vec2(1,0)); }
   // Dark frontal armor and a narrow top ribbon preserve the reference's
   // hierarchy instead of covering every face with high-value diamonds.
   if(n.z>.65 && p.z<-.55) {color=paint(uv,vec2(1,1));painted=1.0;}
   if(n.y>.65 && abs(x)>.115) {color=iron;painted=1.0;}
   if(p.y<-.145) { color=paint(vec2(fract((p.z+.57)*6.0),clamp((-p.y-.145)*15.0,0.0,1.0)),vec2(0,1)); }
  }
  // Cylindrical shroud unwrap: pattern closes after one revolution.
  if(p.z<-.65 && p.z> -1.075 && length(vec2(x,p.y))>.067) {
   float angle=atan(p.y,x)/6.2831853+.5;
   color=paint(vec2((p.z+1.075)/.425,fract(angle*2.0)*.5),vec2(0)); painted=1.0;
   edge_wear=step(p.z,-1.035)+step(-.69,p.z);
   if(p.z< -1.04 || p.z> -.675) {color=gold;painted=0.0;}
  }
  if(p.z< -1.70 && finish==2) {color=gold;}
 }
 if(assembly==2 && finish!=3) {
  vec3 d=p-vec3(side*.1755,-.8,0);
  float angle=atan(d.z,d.y)/6.2831853+.5;
  if(n.x<.65 && length(d.yz)>.245) {
   color=paint(vec2(clamp(d.x/.273+.5,0.0,1.0)*.5,fract(angle*4.0)*.5),vec2(0)); painted=1.0;
   edge_wear=smoothstep(.105,.14,abs(d.x));
  }
  if(n.x>.65) {color=iron;}
  if(finish==2) {color=gold;painted=0.0;}
  if(finish==2 && length(d.yz)>.075 && length(d.yz)<.232) {color=red;painted=1.0;edge_wear=.7;}
  if(n.x>.65 && length(d.yz)<.067) {color=paint(vec2(d.z/.134+.5,.5-d.y/.134),vec2(1,1));painted=1.0;}
  if(finish==1 && d.z>.255) {color=gold;painted=0.0;}
 }
 if(assembly==3 && finish!=3 && p.y>-.37) {
  vec2 uv=vec2(fract((p.x+p.z)*3.0),clamp((-p.y-.25)*8.0,0.0,1.0));
  color=paint(uv,vec2(0,1));painted=1.0;
  if(p.y>-.28 && abs(p.x)>.48) {color=paint(vec2((p.z+.18)/.36,clamp((.07-p.y)/.38,0.0,1.0)),vec2(1,0));}
  if(finish==2) {color=gold;painted=0.0;}
  if(n.y>.65) {color=iron;painted=0.0;}
  // Existing circular pitch caps receive the same dark/gold emblem as drums.
  if(n.x>.65 && abs(p.x)>.615 && length(p.yz)<.066) {
   color=paint(vec2(p.z/.132+.5,.5-p.y/.132),vec2(1,1));painted=1.0;
  }
  edge_wear=max(edge_wear,.3);
 }
 // Rest-space patina remains fixed under recoil, aiming and deployment.
 float coarse=noise(p*95.0), fine=noise(p*630.0);
 float detail=1.0-smoothstep(.001,.008,length(fwidth(p)));
 float chip=smoothstep(.76-edge_wear*.17,.84-edge_wear*.17,coarse*.65+fine*.35);
 float scratches=smoothstep(.86,.95,noise(p*vec3(38,720,95)))*.35*detail;
 float wear=clamp(chip+scratches,0.0,1.0);
 float patina=noise(p*24.0);
 color*=mix(.85+.15*patina,.48+.14*patina,painted);
 // Warm exposed substrate with sparse brighter edge abrasion, not uniform dust.
 vec3 substrate=mix(iron,gold,.25+.45*edge_wear);
 ALBEDO=mix(color,substrate,wear*.78);
 METALLIC=mix(mix(.55,.05,painted),.82,wear);
 ROUGHNESS=clamp(mix(.46,.64,painted)+fine*.10-wear*.12,.34,.8);
 SPECULAR=.28;
}
