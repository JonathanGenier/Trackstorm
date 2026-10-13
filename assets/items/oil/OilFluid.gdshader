shader_type spatial;
render_mode cull_disabled;
uniform float roughness = 0.16;
uniform bool flowing = false;
uniform float dissipation = 0.0;
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1,0)), f.x), mix(hash(i + vec2(0,1)), hash(i + vec2(1,1)), f.x), f.y);
}
void fragment() {
    float phase = flowing ? TIME * 18.0 : 0.0;
    vec2 p = UV * (flowing ? 1.0 : 0.42);
    float grain = noise(UV * 7.0) * 0.60 + noise(UV * 29.0) * 0.40;
    if (dissipation > 0.0 && grain < dissipation) { discard; }
    float ripple = noise(p * 17.0 + vec2(phase * 0.17, -phase * 0.37)) * 2.0 - 1.0;
    ALBEDO = vec3(0.0045, 0.0042, 0.004);
    METALLIC = 0.0;
    SPECULAR = flowing ? 0.22 : 0.24;
    ROUGHNESS = clamp(roughness + ripple * 0.07 + grain * 0.08 + dissipation * 0.35, 0.09, 0.65);
    CLEARCOAT = (flowing ? 0.18 : 0.10) * (1.0 - dissipation);
    CLEARCOAT_ROUGHNESS = 0.2;
    float strength = flowing ? 0.12 : 0.06;
    NORMAL = normalize(NORMAL + vec3(ripple * strength, (noise(p * 21.0 + vec2(7.0, phase * 0.3)) * 2.0 - 1.0) * strength, 0.0));
}
