shader_type canvas_item;
varying vec4 tint;
void vertex() { tint = COLOR; }
void fragment() {
    float weight = smoothstep(0.18, 0.95, UV.y);
    vec2 uv = UV;
    uv.x += sin(TIME * 1.7 + UV.y * 9.0) * 0.009 * weight;
    uv.y += sin(TIME * 2.1 + UV.x * 8.0) * 0.004 * weight;
    COLOR = texture(TEXTURE, uv) * tint;
}
