shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, shadows_disabled, depth_draw_never;
void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float angle = atan(p.y, p.x);
    float spikes = 0.60 + 0.20 * cos(angle * 5.0);
    float flare = pow(max(0.0, 1.0 - length(p) / spikes), 1.5);
    float core = exp(-dot(p, p) * 32.0);
    ALBEDO = mix(vec3(1.0, 0.20, 0.025), vec3(1.0, 0.94, 0.65), core);
    EMISSION = ALBEDO * 2.0;
    ALPHA = clamp(flare + core, 0.0, 1.0);
}
