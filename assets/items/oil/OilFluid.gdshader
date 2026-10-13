shader_type spatial;
render_mode cull_disabled;
uniform float roughness = 0.16;
uniform bool flowing = false;
void fragment() {
    float phase = flowing ? TIME * 18.0 : 0.0;
    vec2 p = UV * (flowing ? 1.0 : 0.12);
    float ripple = sin(p.y * 42.0 - phase + sin(p.x * 19.0)) * sin(p.x * 31.0 + p.y * 7.0);
    ALBEDO = vec3(0.0045, 0.0042, 0.004);
    METALLIC = 0.0;
    SPECULAR = flowing ? 0.22 : 0.36;
    ROUGHNESS = clamp(roughness + ripple * 0.09, 0.07, 0.55);
    CLEARCOAT = flowing ? 0.18 : 0.45;
    CLEARCOAT_ROUGHNESS = 0.2;
    float strength = flowing ? 0.12 : 0.085;
    NORMAL = normalize(NORMAL + vec3(ripple * strength, cos(p.y * 51.0 - phase) * strength * 0.6, 0.0));
}
