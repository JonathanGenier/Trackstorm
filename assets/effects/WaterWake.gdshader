shader_type spatial;
render_mode cull_disabled, depth_draw_never;
uniform float elapsed;
varying float born;
void vertex() { born = INSTANCE_CUSTOM.x; }
void fragment() {
    float age = elapsed-born;
    float radius = .12+age*.4;
    float distance_to_ring = abs(length((UV-.5)*vec2(1.0,1.4))-radius);
    float ring = 1.0-smoothstep(.01,.06,distance_to_ring);
    ALBEDO = vec3(.3,.46,.48);
    ROUGHNESS = .3;
    ALPHA = ring * .45 * (1.0-smoothstep(.1,.9,age)) * step(0.0,age) * (1.0-smoothstep(50.0,85.0,length(VERTEX)));
}
