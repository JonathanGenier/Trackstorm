shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled, ambient_light_disabled, specular_disabled;

varying float tint;
varying float brightness;

void vertex() {
    tint = INSTANCE_CUSTOM.g;
    brightness = INSTANCE_CUSTOM.b;
}

void fragment() {
    float across = abs(UV.x * 2.0 - 1.0);
    float edge = 1.0 - smoothstep(0.55, 1.0, across);
    // A solid luminous body with short tapered ends, not a translucent hairline.
    float along = smoothstep(0.0, 0.07, UV.y) * (1.0 - smoothstep(0.84, 1.0, UV.y));
    float core = 1.0 - smoothstep(0.16, 0.65, across);
    vec3 gold = mix(vec3(0.85, 0.28, 0.012), vec3(1.0, 0.68, 0.12), core);
    vec3 red = mix(vec3(0.7, 0.005, 0.002), vec3(1.0, 0.025, 0.008), core);
    vec3 green = mix(vec3(0.005, 0.5, 0.012), vec3(0.015, 1.0, 0.04), core);
    vec3 hue = tint > 0.75 ? green : tint > 0.25 ? red : gold;
    // Compatibility unshaded materials output albedo only. Use pure emission
    // with a black diffuse surface so lighting cannot dim the travelling core.
    ALBEDO = vec3(0.0);
    EMISSION = hue * brightness;
    ALPHA = edge * along;
}
