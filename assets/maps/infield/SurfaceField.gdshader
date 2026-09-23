shader_type spatial;
render_mode cull_back;

// Linear data, not source_color. Runtime samples the same non-mipmapped field.
uniform sampler2D surface_field : filter_linear, repeat_disable;
uniform vec4 surface_bounds = vec4(-256.0, -128.0, 512.0, 256.0);
uniform sampler2D grass_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D ground_detail : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D ground_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
varying vec3 local_position;

float noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f*f*(3.0-2.0*f);
    vec3 k = vec3(127.1,311.7,74.7);
    float a = fract(sin(dot(i,k.xy))*43758.5453);
    float b = fract(sin(dot(i+vec2(1,0),k.xy))*43758.5453);
    float c = fract(sin(dot(i+vec2(0,1),k.xy))*43758.5453);
    float d = fract(sin(dot(i+vec2(1,1),k.xy))*43758.5453);
    return mix(mix(a,b,f.x),mix(c,d,f.x),f.y);
}

void vertex() { local_position = VERTEX; }

void fragment() {
    vec2 p = local_position.xz;
    vec4 field = texture(surface_field,(p-surface_bounds.xy)/surface_bounds.zw);
    float grain = texture(ground_detail,p*.55).r;
    float macro = noise(p*.17)*.12+.94;
    vec3 grass = texture(grass_albedo,p*.5).rgb * vec3(.49,.46,.34);
    grass = mix(grass,vec3(.13,.108,.054),.22+noise(p*.35)*.16);
    float soil_detail = noise(p*6.7)*.10+noise(p*.8)*.12+.89;
    vec3 dirt = vec3(.215,.167,.112)*(grain*.55+.72)*macro*soil_detail;
    vec3 rock = vec3(.17,.175,.16)*(grain*.8+.6)*macro;
    vec3 mud = vec3(.075,.05,.029)*(grain*.35+.8);
    vec3 deep = vec3(.041,.027,.016)*(grain*.25+.85);
    vec3 water = vec3(.028,.058,.06)*(noise(p*.6)*.12+.94);
    vec3 base = mix(grass,dirt,field.r);
    base = mix(base,rock,field.b);
    float mud_weight = smoothstep(.05,.45,field.g);
    base = mix(base,mud,mud_weight);
    base = mix(base,deep,smoothstep(.45,.85,field.g));
    ALBEDO = mix(base,water,field.a);
    ROUGHNESS = mix(mix(.96,.58,mud_weight),.24,field.a);
    SPECULAR = mix(.18,.5,field.a);
    NORMAL_MAP = mix(texture(grass_normal,p*.5).rgb,texture(ground_normal,p*.55).rgb,max(field.r,max(field.g,field.b)));
    NORMAL_MAP_DEPTH = mix(.4,.07,field.a);
}
