shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

// Crossed longitudinal surfaces hold a continuous hot core at flight speed.
void fragment() {
    float t = 1.0 - UV.y;
    float clock = TIME * 18.0;
    float waves = sin(t * 28.0 - clock) * sin(t * 47.0 - clock * 1.31);
    float center = 0.5 + sin(t * 22.0 - clock * 0.8) * 0.035 * t;
    float radius = (0.34 * pow(1.0 - t, 0.7) + 0.012) * (1.0 + waves * 0.14);
    float edge = abs(UV.x - center) / radius;
    float envelope = (1.0 - smoothstep(0.6, 1.0, edge)) * (1.0 - smoothstep(0.78, 1.0, t));
    float hot = (1.0 - smoothstep(0.05, 0.50, edge)) * (1.0 - smoothstep(0.05, 0.75, t));
    vec3 orange = mix(vec3(1.0, 0.09, 0.006), vec3(1.0, 0.42, 0.025), 0.5 + waves * 0.3);
    ALBEDO = mix(orange, vec3(1.0, 0.84, 0.34), hot);
    ALPHA = envelope * (0.48 + hot * 0.3) * smoothstep(0.0, 0.025, t);
}
