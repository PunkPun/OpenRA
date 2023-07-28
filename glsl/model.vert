#version {VERSION}

uniform mat4 View;
uniform vec3 Scroll;
uniform mat4 TransformMatrix;
uniform vec3 MyLocation;
uniform vec3 r1, r2;

#if __VERSION__ == 120
attribute vec4 aVertexPosition;
attribute vec4 aVertexTexCoord;
attribute vec2 aVertexTexMetadata;
attribute vec3 aVertexTint;
varying vec4 vTexCoord;
varying vec4 vChannelMask;
varying vec4 vNormalsMask;
#else
in vec4 aVertexPosition;
in vec4 aVertexTexCoord;
in vec2 aVertexTexMetadata;
in vec3 aVertexTint;
out vec4 vTexCoord;
out vec4 vChannelMask;
out vec4 vNormalsMask;
#endif

vec4 DecodeMask(float x)
{
	if (x > 0.0)
		return (x > 0.5) ? vec4(1,0,0,0) : vec4(0,1,0,0);
	else
		return (x < -0.5) ? vec4(0,0,0,1) : vec4(0,0,1,0);
}

void main()
{
	gl_Position = vec4(((View * TransformMatrix * aVertexPosition).xyz + MyLocation.xyz - Scroll.xyz) * r1 + r2, 1);
	vTexCoord = aVertexTexCoord;
	vChannelMask = DecodeMask(aVertexTexMetadata.s);
	vNormalsMask = DecodeMask(aVertexTexMetadata.t);
}
