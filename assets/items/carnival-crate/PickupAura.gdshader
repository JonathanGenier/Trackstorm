shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

void fragment() {
    float edge = pow(max(0.0, 1.0 - abs(UV.y * 2.0 - 1.0)), 2.0);
    float ends = smoothstep(0.0, 0.08, UV.x) * smoothstep(0.0, 0.08, 1.0 - UV.x);
    float glint = pow(max(0.0, sin(UV.x * 52.0 - TIME * 1.7)), 18.0);
    ALBEDO = vec3(1.0, 0.57, 0.13);
    ALPHA = edge * ends * (0.20 + 0.65 * glint);
}
