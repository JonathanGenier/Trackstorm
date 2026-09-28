shader_type spatial;
render_mode diffuse_burley, cull_disabled;
uniform vec4 paint_color : source_color = vec4(0.38, 0.012, 0.028, 1.0);
uniform float paint_roughness = 0.30;
uniform float coat_strength = 0.30;
varying vec3 local_position;
float hash(vec3 p) { return fract(sin(dot(p, vec3(127.1,311.7,74.7))) * 43758.5453); }
float noise3(vec3 p) {
    vec3 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
    return mix(mix(mix(hash(i),hash(i+vec3(1,0,0)),f.x),mix(hash(i+vec3(0,1,0)),hash(i+vec3(1,1,0)),f.x),f.y),mix(mix(hash(i+vec3(0,0,1)),hash(i+vec3(1,0,1)),f.x),mix(hash(i+vec3(0,1,1)),hash(i+vec3(1,1,1)),f.x),f.y),f.z);
}
void vertex() { local_position=VERTEX; }
void fragment() {
    vec3 p=local_position;
    float grain=noise3(p*180.0);
    float wear=noise3(p*13.0)*0.55+noise3(p*43.0)*0.3+noise3(p*3.0)*0.15;
    float chips=smoothstep(0.70,0.82,wear);
    vec3 oxide=mix(vec3(0.055,0.035,0.02),vec3(0.22,0.105,0.045),grain);
    ALBEDO=mix(paint_color.rgb*(0.86+grain*0.16),oxide,chips);
    METALLIC=mix(0.18,0.12,chips);
    SPECULAR=0.55;
    CLEARCOAT=coat_strength*(1.0-chips);
    CLEARCOAT_ROUGHNESS=0.18;
    ROUGHNESS=mix(paint_roughness,0.88,chips)+grain*0.025;
}
