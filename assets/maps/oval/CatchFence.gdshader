shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha;
// Original welded safety mesh, 15 x 20 cm openings. Integrate wire coverage
// over the pixel footprint so distant wires fade to their real area coverage.
vec2 integral_wire(vec2 x) {
    return floor(x) * 0.048 + min(fract(x), vec2(0.048));
}
void fragment() {
    vec2 grid = UV / vec2(0.15, 0.20) + vec2(0.024);
    vec2 footprint = max(fwidth(grid), vec2(0.0001));
    vec2 wire = clamp((integral_wire(grid + footprint * 0.5) - integral_wire(grid - footprint * 0.5)) / footprint, vec2(0.0), vec2(1.0));
    ALPHA = 1.0 - (1.0 - wire.x) * (1.0 - wire.y);
    ALBEDO = vec3(0.28, 0.32, 0.36);
    METALLIC = 0.65;
    ROUGHNESS = 0.65;
}
