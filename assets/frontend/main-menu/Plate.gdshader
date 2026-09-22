shader_type canvas_item;
uniform float selected : hint_range(0.0, 1.0) = 0.0;
uniform float pressed : hint_range(0.0, 1.0) = 0.0;
varying vec4 tint;
void vertex() { tint = COLOR; }
void fragment() {
    vec4 source = texture(TEXTURE, UV);
    vec3 red = vec3(min(1.0, source.r * 1.6 + 0.10), source.g * 0.19, source.b * 0.16);
    red = mix(red, vec3(1.0, 0.90, 0.68), smoothstep(0.65, 0.95, source.g));
    COLOR = vec4(mix(source.rgb, red, selected) * (1.0 - pressed * 0.16), source.a) * tint;
}
