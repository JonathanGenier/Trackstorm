shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

uniform float opacity = 1.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 cell = floor(p);
    vec2 blend = fract(p);
    blend = blend * blend * (3.0 - 2.0 * blend);
    return mix(mix(hash(cell), hash(cell + vec2(1.0, 0.0)), blend.x),
        mix(hash(cell + vec2(0.0, 1.0)), hash(cell + vec2(1.0, 1.0)), blend.x), blend.y);
}

void fragment() {
    vec2 p = (UV - vec2(0.5)) * 2.0;
    float coarse = noise(p * 8.0);
    float fine = noise(p * 34.0);
    float radius = length(p * vec2(1.0, 1.13));
    float edge = 0.66 + (coarse - 0.5) * 0.32;
    float shape = 1.0 - smoothstep(edge - 0.17, edge + 0.07, radius);
    float center = 1.0 - smoothstep(0.12, 0.63, radius);
    float mottling = mix(0.48, 1.0, fine);
    ALBEDO = mix(vec3(0.13, 0.105, 0.08), vec3(0.07, 0.06, 0.05), center);
    ALPHA = opacity * shape * mottling * (0.3 + center * 0.26);
}
