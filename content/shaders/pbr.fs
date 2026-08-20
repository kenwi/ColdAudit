#version 330

#define MAX_LIGHTS              4
#define LIGHT_DIRECTIONAL       0
#define LIGHT_POINT             1
#define PI 3.14159265358979323846

// Portal-derived occlusion: each light may only illuminate points inside one of its
// convex volumes (own room, plus one shaft per doorway leaving it).
#define MAX_LIGHT_VOLUMES       4
#define VOLUME_PLANES           6

struct Light {
    int enabled;
    int type;
    vec3 position;
    vec3 target;
    vec4 color;
    float intensity;
};

// Input vertex attributes (from vertex shader)
in vec3 fragPosition;
in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragNormal;
in mat3 TBN;

// Output fragment color
out vec4 finalColor;

// Input uniform values
uniform int numOfLights;
uniform sampler2D albedoMap;
uniform sampler2D mraMap;
uniform sampler2D normalMap;
uniform sampler2D emissiveMap; // r: Hight g:emissive

uniform vec2 tiling;
uniform vec2 offset;

uniform int useTexAlbedo;
uniform int useTexNormal;
uniform int useTexMRA;
uniform int useTexEmissive;

uniform vec4  albedoColor;
uniform vec4  emissiveColor;
uniform float normalValue;
uniform float metallicValue;
uniform float roughnessValue;
uniform float aoValue;
uniform float emissivePower;

// Input lighting values
uniform Light lights[MAX_LIGHTS];
uniform vec3 viewPos;

uniform vec3 ambientColor;
uniform float ambient;

// Light occlusion volumes. Volumes are convex, stored as inward-facing planes
// (xyz = normal, w = offset); a light reaches a point inside ANY of its volumes.
// A light with zero volumes is unmasked.
uniform int  lightVolumeCount[MAX_LIGHTS];
uniform int  volumePlaneCount[MAX_LIGHTS*MAX_LIGHT_VOLUMES];
uniform vec4 volumePlanes[MAX_LIGHTS*MAX_LIGHT_VOLUMES*VOLUME_PLANES];

float LightReaches(int lightIndex, vec3 p)
{
    int volumes = lightVolumeCount[lightIndex];
    if (volumes <= 0) return 1.0;

    for (int v = 0; v < volumes; v++)
    {
        int slot = lightIndex*MAX_LIGHT_VOLUMES + v;
        int planes = volumePlaneCount[slot];
        bool inside = planes > 0;
        for (int k = 0; k < planes; k++)
        {
            vec4 plane = volumePlanes[slot*VOLUME_PLANES + k];
            if (dot(plane.xyz, p) + plane.w < 0.0)
            {
                inside = false;
                break;
            }
        }

        if (inside) return 1.0;
    }

    return 0.0;
}

// Per-light depth cubemaps holding radial distance to the light, normalised by
// shadowFarPlane. GLSL 3.30 forbids indexing a sampler array with a loop variable, so the
// cubes are separate uniforms and SampleShadowDistance switches on the light index.
uniform samplerCube shadowCube0;
uniform samplerCube shadowCube1;
uniform samplerCube shadowCube2;
uniform samplerCube shadowCube3;

uniform int shadowEnabled[MAX_LIGHTS];
uniform float shadowFarPlane;
uniform float shadowTexel;

float SampleShadowDistance(int lightIndex, vec3 dir)
{
    if (lightIndex == 0) return texture(shadowCube0, dir).r*shadowFarPlane;
    if (lightIndex == 1) return texture(shadowCube1, dir).r*shadowFarPlane;
    if (lightIndex == 2) return texture(shadowCube2, dir).r*shadowFarPlane;
    return texture(shadowCube3, dir).r*shadowFarPlane;
}

/// 0 = fully shadowed, 1 = fully lit. Five taps soften the edge, and both the bias and the tap
/// spread are scaled by the world size of one cubemap texel at this distance, which is what
/// keeps a surface from shadowing itself when the light grazes it.
float ShadowVisibility(int lightIndex, vec3 p, vec3 N, vec3 lightPos)
{
    if (shadowEnabled[lightIndex] == 0) return 1.0;

    vec3 toFrag = p - lightPos;
    float dist = length(toFrag);
    if (dist >= shadowFarPlane || dist < 0.001) return 1.0;

    // A 90 degree face spans 2*dist across shadowTexel-sized texels at this range, so this is
    // roughly how far a neighbouring texel's depth can legitimately differ.
    float texelWorld = 2.0*dist*shadowTexel;

    // Normal-offset: compare from just above the surface rather than on it. Grazing light needs
    // the most room, since that is where one texel covers the largest depth range.
    float grazing = 1.0 - abs(dot(N, normalize(toFrag)));
    vec3 samplePos = p + N*(texelWorld*(2.0 + 6.0*grazing) + 0.02);

    vec3 offsetToFrag = samplePos - lightPos;
    vec3 dir = normalize(offsetToFrag);
    float threshold = length(offsetToFrag) - (texelWorld*2.0 + 0.05);

    vec3 basisUp = abs(dir.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 tangent = normalize(cross(basisUp, dir))*shadowTexel;
    vec3 bitangent = cross(dir, tangent/shadowTexel)*shadowTexel;

    float lit = step(threshold, SampleShadowDistance(lightIndex, dir));
    lit += step(threshold, SampleShadowDistance(lightIndex, dir + tangent));
    lit += step(threshold, SampleShadowDistance(lightIndex, dir - tangent));
    lit += step(threshold, SampleShadowDistance(lightIndex, dir + bitangent));
    lit += step(threshold, SampleShadowDistance(lightIndex, dir - bitangent));
    return lit*0.2;
}

// Reflectivity in range 0.0 to 1.0
// NOTE: Reflectivity is increased when surface view at larger angle
vec3 SchlickFresnel(float hDotV,vec3 refl)
{
    return refl + (1.0 - refl)*pow(1.0 - hDotV, 5.0);
}

float GgxDistribution(float nDotH,float roughness)
{
    float a = roughness*roughness*roughness*roughness;
    float d = nDotH*nDotH*(a - 1.0) + 1.0;
    d = PI*d*d;
    return (a/max(d,0.0000001));
}

float GeomSmith(float nDotV,float nDotL,float roughness)
{
    float r = roughness + 1.0;
    float k = r*r/8.0;
    float ik = 1.0 - k;
    float ggx1 = nDotV/(nDotV*ik + k);
    float ggx2 = nDotL/(nDotL*ik + k);
    return ggx1*ggx2;
}

vec3 ComputePBR()
{
    vec3 albedo = albedoColor.rgb;
    if (useTexAlbedo == 1)
    {
        vec3 albedoTex = texture(albedoMap, vec2(fragTexCoord.x*tiling.x + offset.x, fragTexCoord.y*tiling.y + offset.y)).rgb;
        albedo *= albedoTex;
    }

    float metallic = clamp(metallicValue, 0.0, 1.0);
    float roughness = clamp(roughnessValue, 0.0, 1.0);
    float ao = clamp(aoValue, 0.0, 1.0);

    if (useTexMRA == 1)
    {
        vec4 mra = texture(mraMap, vec2(fragTexCoord.x*tiling.x + offset.x, fragTexCoord.y*tiling.y + offset.y));
        metallic = clamp(mra.r + metallicValue, 0.04, 1.0);
        roughness = clamp(mra.g + roughnessValue, 0.04, 1.0);
        ao = (mra.b + aoValue)*0.5;
    }

    vec3 N = normalize(fragNormal);
    if (useTexNormal == 1)
    {
        N = texture(normalMap, vec2(fragTexCoord.x*tiling.x + offset.x, fragTexCoord.y*tiling.y + offset.y)).rgb;
        N = normalize(N*2.0 - 1.0);
        N = normalize(N*TBN);
    }

    vec3 V = normalize(viewPos - fragPosition);

    vec3 emissive = vec3(0);
    emissive = (texture(emissiveMap, vec2(fragTexCoord.x*tiling.x + offset.x, fragTexCoord.y*tiling.y + offset.y)).rgb).g*emissiveColor.rgb*emissivePower*useTexEmissive;

    // return N;//vec3(metallic,metallic,metallic);
    // If  dia-electric use base reflectivity of 0.04 otherwise ut is a metal use albedo as base reflectivity
    vec3 baseRefl = mix(vec3(0.04), albedo.rgb, metallic);
    vec3 lightAccum = vec3(0.0);  // Acumulate lighting lum

    for (int i = 0; i < numOfLights; i++)
    {
        float reaches = float(lights[i].enabled)*LightReaches(i, fragPosition);
        if (reaches <= 0.0) continue;                                // Occluded by walls

        vec3 L = normalize(lights[i].position - fragPosition);      // Compute light vector
        vec3 H = normalize(V + L);                                  // Compute halfway bisecting vector
        float dist = length(lights[i].position - fragPosition);     // Compute distance to light

        reaches *= ShadowVisibility(i, fragPosition, N, lights[i].position);
        if (reaches <= 0.0) continue;                                // Occluded by geometry

        float attenuation = 1.0/(dist*dist*0.23);                   // Compute attenuation
        vec3 radiance = lights[i].color.rgb*lights[i].intensity*attenuation; // Compute input radiance, light energy comming in

        // Cook-Torrance BRDF distribution function
        float nDotV = max(dot(N,V), 0.0000001);
        float nDotL = max(dot(N,L), 0.0000001);
        float hDotV = max(dot(H,V), 0.0);
        float nDotH = max(dot(N,H), 0.0);
        float D = GgxDistribution(nDotH, roughness);    // Larger the more micro-facets aligned to H
        float G = GeomSmith(nDotV, nDotL, roughness);   // Smaller the more micro-facets shadow
        vec3 F = SchlickFresnel(hDotV, baseRefl);       // Fresnel proportion of specular reflectance

        vec3 spec = (D*G*F)/(4.0*nDotV*nDotL);

        // Difuse and spec light can't be above 1.0
        // kD = 1.0 - kS  diffuse component is equal 1.0 - spec comonent
        vec3 kD = vec3(1.0) - F;

        // Mult kD by the inverse of metallnes, only non-metals should have diffuse light
        kD *= 1.0 - metallic;
        lightAccum += ((kD*albedo.rgb/PI + spec)*radiance*nDotL)*reaches; // Angle of light has impact on result
    }

    vec3 ambientFinal = (ambientColor + albedo)*ambient*0.5;

    return (ambientFinal + lightAccum*ao + emissive);
}

void main()
{
    vec3 color = ComputePBR();

    // HDR tonemapping
    color = pow(color, color + vec3(1.0));

    // Gamma correction
    color = pow(color, vec3(1.0/2.2));

    finalColor = vec4(color, 1.0);
}