shader_type spatial;
render_mode world_vertex_coords, unshaded, blend_add, cull_disabled;
uniform float age = 0.0;
uniform float retire_opacity = 1.0;
varying float brightness;
void vertex() {
    float phase = INSTANCE_CUSTOM.z * 6.2831853;
    float life = INSTANCE_CUSTOM.w;
    float t = max(0.0, min(age, life)-UV.y*.13);
    float angle = INSTANCE_CUSTOM.x * 6.2831853;
    vec3 direction = vec3(cos(angle), .4+INSTANCE_CUSTOM.y, sin(angle));
    vec3 origin = MODEL_MATRIX[3].xyz;
    vec3 head = direction * (4.0+INSTANCE_CUSTOM.y*5.0) * (1.0-exp(-2.1*t))/2.1;
    head += vec3(sin(t*5.0+phase)-sin(phase), -.8*t*t, cos(t*5.0+phase)-cos(phase))*.25;
    float width = .016*(1.0-UV.y*.85);
    VERTEX = origin + head + INV_VIEW_MATRIX[0].xyz*(UV.x-.5)*width;
    brightness = (1.0-smoothstep(life*.15,life,age))*(.65+.35*sin(phase+t*30.0))*(1.0-UV.y)*retire_opacity;
}
void fragment() {
    ALBEDO = vec3(3.5, 2.15, .3);
    ALPHA = brightness;
}
