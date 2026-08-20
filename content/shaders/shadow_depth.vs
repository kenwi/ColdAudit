#version 330

// Depth-only pass for one point-light shadow cubemap face.
// Uniform and attribute names match Raylib's defaults so LoadShader wires them up.
in vec3 vertexPosition;

uniform mat4 mvp;
uniform mat4 matModel;

out vec3 fragPosition;

void main()
{
    fragPosition = vec3(matModel*vec4(vertexPosition, 1.0));
    gl_Position = mvp*vec4(vertexPosition, 1.0);
}
