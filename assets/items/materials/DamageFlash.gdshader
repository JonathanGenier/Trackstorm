shader_type spatial;
uniform vec4 paint : source_color = vec4(0.1, 0.6, 0.8, 1.0);
uniform float flash : hint_range(0.0, 1.0) = 0.0;
void fragment() {
    ALBEDO = mix(paint.rgb, vec3(1.0, 0.3, 0.08), flash);
    EMISSION = vec3(1.0, 0.15, 0.02) * flash * 2.0;
    ROUGHNESS = 0.55;
}
