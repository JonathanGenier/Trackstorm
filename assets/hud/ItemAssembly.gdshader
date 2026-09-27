shader_type canvas_item;

// Blank 2172x724 chassis, with no baked live values or item/resource content.
uniform float fill : hint_range(0.0, 1.0) = 0.0;

void fragment() {
    vec4 c = texture(TEXTURE, UV);
    vec2 pixel = UV * vec2(2172.0, 724.0);
    vec2 radial = pixel - vec2(658.0, 546.0);
    float radius = length(radial);
    // Restrict to authored red gauge cells/ticks; never recolor metal or slot wells.
    if (pixel.y < 548.0 && pixel.x > 335.0 && pixel.x < 950.0 && radius > 225.0 && radius < 325.0
        && c.r > c.g * 1.7 && c.r > c.b * 1.8) {
        float progress = clamp(atan(-radial.y, -radial.x) / 3.14159265, 0.0, 1.0);
        if (progress >= fill) { c.rgb = vec3(max(c.r, 0.2) * 0.12); }
    }
    COLOR = c;
}
