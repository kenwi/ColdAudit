#version 330

// Writes radial distance from the light, normalised to [0,1] by farPlane, so the PBR pass
// can compare against it directly without reconstructing the light's projection.
in vec3 fragPosition;

uniform vec3 lightPosition;
uniform float farPlane;

out vec4 finalColor;

void main()
{
    float dist = length(fragPosition - lightPosition)/farPlane;
    finalColor = vec4(clamp(dist, 0.0, 1.0), 0.0, 0.0, 1.0);
}
