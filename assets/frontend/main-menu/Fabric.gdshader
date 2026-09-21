shader_type canvas_item;
uniform float phase = 0.0;
varying vec4 tint;
void vertex() { tint = COLOR; }
void fragment() {
    // Keep the complete upper attachment region fixed; only loose cloth ripples.
    float weight = smoothstep(0.24, 0.95, UV.y);
    vec2 uv = UV;
    uv.x += sin(TIME * 1.7 + UV.y * 10.0 + phase) * 0.009 * weight;
    uv.y += sin(TIME * 1.3 + UV.x * 7.0 + phase) * 0.002 * weight;
    COLOR = texture(TEXTURE, uv) * tint;
}
