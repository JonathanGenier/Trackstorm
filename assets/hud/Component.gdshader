shader_type canvas_item;

// Original component artwork stays intact on disk. Replace only example values,
// and recolor the authored gauge cells; native labels supply all live text.
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
    } else if (component == 1) {
        if (inside(UV, vec4(0.329, 0.46, 0.683, 0.726)) || inside(UV, vec4(0.409, 0.748, 0.586, 0.825))) { c = blank(UV); }
        vec2 radial = (UV - vec2(0.5, 0.785)) * vec2(1.0, 0.75);
        float angle = atan(-radial.y, -radial.x);
        // Red-only mask preserves all metal dividers, rivets and spikes.
        if (length(radial) > 0.34 && UV.y < 0.77 && c.r > c.g * 1.25 && c.r > c.b * 1.6) {
            if (angle / 3.14159265 > fill) { c.rgb = vec3(max(c.r, 0.20) * 0.13); }
        }
    } else if (component == 2) {
        // The reference's Q, machine gun and .50 CAL are examples, not game controls/items.
        if (inside(UV, vec4(0.395, 0.135, 0.66, 0.283)) || inside(UV, vec4(0.186, 0.29, 0.875, 0.729)) || inside(UV, vec4(0.295, 0.753, 0.793, 0.879))) { c = blank(UV); }
    } else {
        if (inside(UV, vec4(0.275, 0.38, 0.708, 0.805))) { c = blank(UV); }
    }
    COLOR = c;
}
