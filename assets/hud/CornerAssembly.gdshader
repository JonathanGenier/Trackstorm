shader_type canvas_item;

uniform float fill : hint_range(0.0, 1.0) = 0.0;

void fragment() {
    // Remove transparent generator padding in sampling, preserving original asset bytes.
    vec2 pixel = vec2(11.0, 292.0) + UV * vec2(1901.0, 457.0);
    vec4 color = texture(TEXTURE, pixel / vec2(1926.0, 817.0));
    vec2 radial = (pixel - vec2(433.0, 706.0)) / vec2(300.0, 333.0);
    float radius = length(radial);
    if (radial.y <= 0.0 && radius > 0.80 && radius < 1.0) {
        float progress = clamp(atan(-radial.y, -radial.x) / 3.14159265, 0.0, 1.0);
        float cell = fract(progress * 16.0);
        bool inside = cell > 0.07 && cell < 0.93;
        vec3 dark = mix(vec3(0.055), vec3(0.13), (radius - 0.8) * 5.0);
        vec3 lit = mix(vec3(1.0, 0.31, 0.025), vec3(0.95, 0.035, 0.015), (radius - 0.8) * 5.0);
        color.rgb = inside ? (progress < fill ? lit : dark) : vec3(0.018);
        color.a = 1.0;
    }
    COLOR = color;
}
