shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

uniform float opacity = 0.0;

float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

void fragment() {
    vec2 p = (UV - vec2(0.5)) * 2.0;
    float angle = atan(p.y, p.x);
    float ripple = sin(angle * 11.0) * 0.028 + sin(angle * 23.0 + 1.7) * 0.015;
    float radius = length(p);
    float soft_front = 1.0 - smoothstep(0.76 + ripple, 0.96 + ripple, radius);
    float soft_back = smoothstep(0.47 + ripple, 0.69 + ripple, radius);
    float grit = hash(floor(p * 45.0));
    float dusty_band = soft_front * soft_back * mix(0.62, 1.0, grit);
    ALBEDO = vec3(0.28, 0.25, 0.21);
    ALPHA = opacity * dusty_band;
}
