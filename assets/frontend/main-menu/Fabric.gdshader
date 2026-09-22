shader_type canvas_item;
uniform float phase = 0.0;
varying vec4 tint;
void vertex() { tint = COLOR; }
void fragment() {
    // Keep the complete upper attachment region fixed; only loose cloth ripples.
    float weight = smoothstep(0.24, 0.95, UV.y);
    vec2 uv = UV;
    // Broad traveling folds plus a smaller faster ripple keep the free ends fabric-like.
    uv.x += (sin(TIME * 2.4 + UV.y * 10.0 + phase) * 0.035
        + sin(TIME * 4.1 - UV.y * 17.0 + phase) * 0.012) * weight;
    uv.y += sin(TIME * 2.0 + UV.x * 7.0 + UV.y * 8.0 + phase) * 0.006 * weight;
    COLOR = texture(TEXTURE, uv) * tint;
}
