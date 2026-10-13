shader_type spatial;
uniform vec4 base_color : source_color = vec4(0.18, 0.20, 0.22, 1.0);
varying vec3 local_position;
float hash(vec3 p) { return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453); }
void vertex() { local_position = VERTEX; }
void fragment() {
    float grain = hash(floor(local_position * 80.0));
    float patch = hash(floor(local_position * 33.0));
    float wear = step(0.91, patch) * step(0.50, grain);
    ALBEDO = mix(base_color.rgb * (0.94 + 0.06 * grain), base_color.rgb * 1.25, wear * 0.18);
    METALLIC = 0.65;
    ROUGHNESS = 0.62 + grain * 0.14;
}
