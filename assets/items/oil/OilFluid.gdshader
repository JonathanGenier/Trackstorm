shader_type spatial;
render_mode cull_disabled;
uniform float roughness = 0.16;
uniform bool flowing = false;
void fragment() {
    float phase = flowing ? TIME * 18.0 : 0.0;
    vec2 p = UV * (flowing ? 1.0 : 0.12);
    float ripple = sin(p.y * 42.0 - phase + sin(p.x * 19.0)) * sin(p.x * 31.0 + p.y * 7.0);
    ALBEDO = vec3(0.003, 0.0028, 0.0025);
    METALLIC = 0.0;
    SPECULAR = flowing ? 0.22 : 0.12;
    ROUGHNESS = clamp(roughness + ripple * 0.055, 0.08, 0.55);
    float strength = flowing ? 0.1 : 0.035;
    NORMAL = normalize(NORMAL + vec3(ripple * strength, cos(p.y * 51.0 - phase) * strength * 0.6, 0.0));
}
