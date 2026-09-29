shader_type canvas_item;

// Original component artwork stays intact on disk. Replace only example values,
// and recolor the authored health cells; native labels supply all live text.
// The only consumers are Health (0) and Timer (3).
uniform int component = 0;
uniform float fill : hint_range(0.0, 1.0) = 1.0;
uniform sampler2D steel : source_color, filter_linear;

bool inside(vec2 p, vec4 r) {
    return p.x > r.x && p.y > r.y && p.x < r.z && p.y < r.w;
}

vec4 blank(vec2 p) {
    return texture(steel, vec2(0.36, 0.64) + fract(p * 3.0) * vec2(0.19, 0.10));
}

void fragment() {
    vec4 c = texture(TEXTURE, UV);
    if (component == 0) {
        // Badge sample, numeric HP field, and authored eleven-cell health bar.
        if (inside(UV, vec4(0.075, 0.32, 0.225, 0.69))) {
            c = blank(UV) * vec4(2.1, 0.28, 0.20, 1.0);
        }
        if (inside(UV, vec4(0.676, 0.61, 0.912, 0.756))) { c = blank(UV); }
        if (inside(UV, vec4(0.258, 0.407, 0.909, 0.542))) {
            float intensity = max(c.r, max(c.g, c.b));
            if (intensity > 0.09) {
                c.rgb = (UV.x < 0.258 + 0.651 * fill) ? vec3(max(intensity, 0.40), intensity * 0.045, intensity * 0.025) : vec3(intensity * 0.13);
            }
        }
    } else {
        if (inside(UV, vec4(0.275, 0.38, 0.708, 0.805))) { c = blank(UV); }
    }
    COLOR = c;
}
