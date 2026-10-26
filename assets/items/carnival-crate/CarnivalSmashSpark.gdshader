shader_type spatial;
render_mode world_vertex_coords, unshaded, blend_add, cull_disabled;
uniform float age = 0.0;
uniform float retire_opacity = 1.0;
uniform mat4 burst_transform;
uniform mat4 burst_inverse;
uniform float effect_scale = 1.0;
varying float brightness;
void vertex() {
    float phase = INSTANCE_CUSTOM.z * 6.2831853;
    float life = INSTANCE_CUSTOM.w;
    float t = max(0.0, min(age, life)-UV.y*.13);
    float angle = INSTANCE_CUSTOM.x * 6.2831853;
    vec3 direction = vec3(cos(angle), .35+INSTANCE_CUSTOM.y*.45, sin(angle)*.5);
    vec3 origin = MODEL_MATRIX[3].xyz;
    vec3 start = (burst_inverse*vec4(origin,1.0)).xyz;
    vec3 head = start+direction*(.5+INSTANCE_CUSTOM.y*.35)*(1.0-exp(-7.0*t));
    head += vec3(sin(t*7.0+phase)-sin(phase), -.8*t*t, cos(t*7.0+phase)-cos(phase))*.08;
    head = clamp(head,vec3(-1.72,.1,-.95),vec3(1.72,3.08,.95));
    head = vec3(0.0,1.4,0.0)+(head-vec3(0.0,1.4,0.0))*effect_scale;
    float width = .022*(1.0-UV.y*.85)*effect_scale;
    VERTEX = (burst_transform*vec4(head,1.0)).xyz + INV_VIEW_MATRIX[0].xyz*(UV.x-.5)*width;
    brightness = (1.0-smoothstep(life*.15,life,age))*(.65+.35*sin(phase+t*30.0))*(1.0-UV.y)*retire_opacity;
}
void fragment() {
    ALBEDO = vec3(3.5, 2.15, .3);
    ALPHA = brightness;
}
