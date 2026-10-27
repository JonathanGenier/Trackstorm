shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled, shadows_disabled;

varying vec4 particle;

void vertex() { particle = INSTANCE_CUSTOM; }

void fragment() {
    vec2 p = UV * 2.0 - 1.0;
    float mask;
    vec3 color = vec3(particle.b);
    if (particle.r > 0.75) {
        // Soft irregular neutral dust, with no hot core or expanding fireball.
        float radius = length(p * vec2(1.0, 1.15));
        float lobes = 0.08 * sin(p.x * 9.0 + particle.a * 6.0) * cos(p.y * 7.0);
        mask = 1.0 - smoothstep(0.2, 1.0, radius + lobes);
    } else if (particle.r > 0.25) {
        mask = (1.0 - smoothstep(0.25, 1.0, abs(p.x))) * (1.0 - smoothstep(0.5, 1.0, abs(p.y)));
        color = vec3(1.0, 0.85, 0.58);
    } else {
        // Angular chipped silhouettes with two restrained faces, not round glowing sprites.
        float edge = max(abs(p.x + p.y * 0.27), abs(p.y - p.x * 0.18));
        mask = 1.0 - smoothstep(0.65, 0.8, edge);
        color *= p.x + p.y * 0.4 > 0.0 ? 1.0 : 0.55;
    }
    ALBEDO = color;
    ALPHA = mask * particle.g;
}
