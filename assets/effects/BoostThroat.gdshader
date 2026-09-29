shader_type spatial;
render_mode unshaded, cull_disabled;
uniform float heat = 0.0;
void fragment() {
    float pulse = .9 + .1 * sin(TIME * 39.0);
    ALBEDO = mix(vec3(.018,.024,.031), vec3(.23,.52,1.0) * pulse, heat);
}
