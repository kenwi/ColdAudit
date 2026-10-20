#version 330

// Static-light path: the level's geometry carries a second UV set (glTF TEXCOORD_1,
// raylib mesh.texcoords2) holding its place in the baked atlas. Nothing here is lit
// in real time, so there are no lights, no shadows and no normals to transform.

// Input vertex attributes
in vec3 vertexPosition;
in vec2 vertexTexCoord;
in vec2 vertexTexCoord2;

// Input uniform values
uniform mat4 mvp;

// Output vertex attributes (to fragment shader)
out vec2 fragTexCoord;
out vec2 fragLightMapCoord;

void main()
{
    // Authored 1:1: the exporter bakes UVs at real-world scale, so no tiling here.
    fragTexCoord = vertexTexCoord;
    fragLightMapCoord = vertexTexCoord2;

    gl_Position = mvp*vec4(vertexPosition, 1.0);
}
