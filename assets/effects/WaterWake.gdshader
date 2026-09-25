shader_type spatial;
render_mode cull_disabled, depth_draw_never;
uniform float elapsed;
uniform float visibility_distance = 100.0;
varying vec4 wake;
void vertex() { wake = INSTANCE_CUSTOM; }
void fragment() {
    float age = elapsed-wake.x;
    float radius = .12+(age/max(wake.y,.01))*.36;
    float distance_to_ring = abs(length((UV-.5)*vec2(1.0,1.4))-radius);
    float ring = 1.0-smoothstep(.01,.06,distance_to_ring);
    ALBEDO = vec3(.3,.46,.48);
    ROUGHNESS = .3;
    ALPHA = ring * .45 * wake.w * (1.0-smoothstep(wake.y-wake.z,wake.y,age)) * step(0.0,age) * (1.0-smoothstep(visibility_distance*.5,visibility_distance*.85,length(VERTEX)));
}
