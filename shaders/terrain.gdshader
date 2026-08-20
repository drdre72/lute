shader_type spatial;

uniform sampler2D sand_color_tex : source_color;
uniform sampler2D sand_normal_tex;
uniform sampler2D sand_roughness_tex;

uniform sampler2D grass_color_tex : source_color;
uniform sampler2D grass_normal_tex;
uniform sampler2D grass_roughness_tex;

uniform sampler2D rock_color_tex : source_color;
uniform sampler2D rock_normal_tex;
uniform sampler2D rock_roughness_tex;

uniform float texture_scale = 32.0;
uniform float sand_height = 2.5;
uniform float rock_slope = 0.75;

varying float world_y;
varying vec3 world_normal;

void vertex() {
    world_y = VERTEX.y;
    world_normal = NORMAL;
}

void fragment() {
    float slope = dot(world_normal, vec3(0.0, 1.0, 0.0));
    vec2 uv = UV * texture_scale;
    
    // Sand layer (near water)
    vec3 sand_albedo = texture(sand_color_tex, uv).rgb;
    vec3 sand_normal = texture(sand_normal_tex, uv).rgb;
    float sand_rough = texture(sand_roughness_tex, uv).r;
    
    // Grass layer (flat terrain above water)
    vec3 grass_albedo = texture(grass_color_tex, uv).rgb;
    vec3 grass_normal = texture(grass_normal_tex, uv).rgb;
    float grass_rough = texture(grass_roughness_tex, uv).r;
    
    // Rock layer (steep slopes)
    vec3 rock_albedo = texture(rock_color_tex, uv).rgb;
    vec3 rock_normal = texture(rock_normal_tex, uv).rgb;
    float rock_rough = texture(rock_roughness_tex, uv).r;
    
    // Blend sand -> grass by height
    float sand_blend = clamp(world_y / sand_height, 0.0, 1.0);
    vec3 albedo = mix(sand_albedo, grass_albedo, sand_blend);
    vec3 normal_map = mix(sand_normal, grass_normal, sand_blend);
    float roughness = mix(sand_rough, grass_rough, sand_blend);
    
    // Blend in rock on steep slopes
    float rock_blend = smoothstep(rock_slope, 0.5, slope);
    albedo = mix(albedo, rock_albedo, rock_blend);
    normal_map = mix(normal_map, rock_normal, rock_blend);
    roughness = mix(roughness, rock_rough, rock_blend);
    
    ALBEDO = albedo;
    ROUGHNESS = roughness;
    
    // Apply normal map
    NORMAL_MAP = normal_map;
    NORMAL_MAP_DEPTH = 1.0;
}
