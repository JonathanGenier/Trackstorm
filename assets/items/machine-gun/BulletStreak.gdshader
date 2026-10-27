shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, fog_disabled;

varying float tracer;
varying float tint;
varying float brightness;

void vertex() {
    tracer = INSTANCE_CUSTOM.r;
    tint = INSTANCE_CUSTOM.g;
    brightness = INSTANCE_CUSTOM.b;
}

void fragment() {
    float across = abs(UV.x * 2.0 - 1.0);
    float edge = 1.0 - smoothstep(0.15, 1.0, across);
    // Quad +Y points toward travel: the tail tapers away behind a small hot core.
    float along = (1.0 - smoothstep(0.76, 1.0, UV.y)) * smoothstep(0.0, 0.12, UV.y);
    float core = 1.0 - smoothstep(0.0, 0.38, across);
    vec3 warm = mix(vec3(0.95, 0.37, 0.08), vec3(1.0, 0.87, 0.52), core);
    vec3 red = mix(vec3(0.75, 0.035, 0.015), vec3(1.0, 0.25, 0.12), core);
    vec3 green = mix(vec3(0.04, 0.55, 0.08), vec3(0.35, 0.9, 0.30), core);
    ALBEDO = (tint > 0.75 ? green : tint > 0.25 ? red : warm) * brightness;
    ALPHA = edge * along * mix(0.32, 0.85, tracer);
}
