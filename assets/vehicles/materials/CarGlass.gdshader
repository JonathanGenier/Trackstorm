shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled;

void fragment() {
    float edge = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 4.0);
    ALBEDO = vec3(0.20, 0.30, 0.34);
    METALLIC = 0.0;
    ROUGHNESS = 0.12;
    SPECULAR = 0.65;
    ALPHA = mix(0.16, 0.48, edge);
}
