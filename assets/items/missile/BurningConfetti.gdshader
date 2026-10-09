shader_type spatial;
render_mode unshaded, cull_disabled, shadows_disabled, depth_draw_never;

// The particle color's fading alpha supplies a local burn clock; no gameplay state.
void fragment() {
    float age = 1.0 - COLOR.a;
    float grain = fract(sin(dot(floor(UV * 19.0), vec2(12.9898, 78.233))) * 43758.5453);
    float edge = min(min(UV.x, 1.0 - UV.x), min(UV.y, 1.0 - UV.y));
    float burn = age * 0.58 + grain * 0.035;
    float remaining = smoothstep(burn - 0.035, burn, edge);
    float ember = (1.0 - smoothstep(burn, burn + 0.05, edge)) * remaining;
    ALBEDO = mix(vec3(0.58, 0.006, 0.012), vec3(0.035, 0.004, 0.003), age);
    EMISSION = vec3(1.0, 0.075, 0.008) * ember * 2.4;
    ALPHA = COLOR.a * remaining;
}
