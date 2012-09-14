///////////////////////////////////////////////////
// Textures & Samplers
///////////////////////////////////////////////////

texture MeshTexture;

sampler MeshSampler = sampler_state
{
	texture = <MeshTexture>; 
	magfilter = LINEAR;
	minfilter = LINEAR;
	mipfilter = LINEAR;
	AddressU = wrap;
	AddressV = wrap;
};

///////////////////////////////////////////////////
// Globals
///////////////////////////////////////////////////

float4x4 matrixViewProj;
float4x4 worldTransform;
float3 CameraPosition;
static const float PI = 3.14159265f;
float4 fogColor = float4(0.6f, 0.6f, 0.6f, 1.0f); 
float fogStart = 500.0f;
float fogEnd = 530.0f;
float3 diffuseLight = float3(1, 1, 1);
float3 ambientLight = float3(0, 0, 0);
float3 SunDirection = float3(1, 1, -1);


///////////////////////////////////////////////////
// Helper functions
///////////////////////////////////////////////////

float4 ApplyFog(float4 colorIn, float depth, float angle)
{
	if(depth < fogStart)
		return colorIn;

	float4 skyCol = fogColor;
	if(depth > fogEnd)
		return float4(skyCol.rgb, colorIn.a);

	float ratio = (depth - fogStart) / (fogEnd - fogStart);
	float4 col = float4(lerp(colorIn.rgb, skyCol.rgb, ratio), colorIn.a);
	return col;
}

float CalcDepth(float3 vertexPos)
{
	float dx = CameraPosition.x - vertexPos.x;
	float dy = CameraPosition.y - vertexPos.y;
	float dz = CameraPosition.z - vertexPos.z;
	return sqrt(dx * dx + dy * dy + dz * dz);
}

float4 ApplySunLight(float4 base, float3 normal, float3 viewDirection)
{
	float light = dot(normal, -normalize(SunDirection));
	if(light < 0)
		light = 0;
	if(light > 0.5f)
		light = 0.5f + (light - 0.5f) * 0.65f;

	float3 Normal = normalize(normal);
	float3 LightDir = normalize(-SunDirection);
	float3 ViewDir = normalize(viewDirection);
	float4 diff = saturate(dot(Normal, LightDir));

	float3 reflect = normalize(2 * diff * Normal - LightDir);
	float4 specular = pow(saturate(dot(reflect, ViewDir)), 8);

	//light = saturate(light + 0.5f);
	float3 diffuse = diffuseLight * light;
	diffuse += ambientLight;
	diffuse += specular * 0.3f * diffuseLight;
	base.rgb *= diffuse;
	return base;
}



///////////////////////////////////////////////////
// Shader structs (PixelInput and VertexInput)
///////////////////////////////////////////////////

struct PixelInput
{
	float4 Position : POSITION0;
	float2 TextureCoords : TEXCOORD0;
	float Depth : TEXCOORD1;
	float Angle : TEXCOORD2;
	float3 Normal : TEXCOORD3;
	float3 ViewDirection : TEXCOORD4;
};

struct VertexInput
{
	float4 Position : POSITION0;
	float3 Normal : NORMAL;

	float2 TextureCoords : TEXCOORD0;
};



///////////////////////////////////////////////////
// Vertex-Shader
///////////////////////////////////////////////////

PixelInput MeshShader(VertexInput input)
{
	PixelInput retVal = (PixelInput)0;

	float4 instancePos = mul(input.Position, worldTransform);
	float3 instanceNorm = input.Normal;

	retVal.Position = mul(instancePos, matrixViewProj);
	retVal.TextureCoords = input.TextureCoords;
	retVal.Depth = CalcDepth(instancePos.xyz);
	retVal.Normal = normalize(instanceNorm);
	retVal.ViewDirection = CameraPosition - input.Position.xyz;

	//float3 diff = input.Position - CameraPosition;
	//float diff2D = sqrt(diff.x * diff.x + diff.z * diff.z);
	//retVal.Angle = atan(diff.y / diff2D);

	return retVal;
}



///////////////////////////////////////////////////
// Pixel-Shader
///////////////////////////////////////////////////

float4 PixelBlendShader(PixelInput input) : COLOR0
{
	float4 BaseColor = tex2D(MeshSampler, input.TextureCoords);
	BaseColor = ApplySunLight(BaseColor, input.Normal, input.ViewDirection);

	return ApplyFog(BaseColor, input.Depth, input.Angle);
}

///////////////////////////////////////////////////
// Technique
///////////////////////////////////////////////////


technique Blend1Layer
{
	pass Pass1
	{
		VertexShader = compile vs_3_0 MeshShader();
		PixelShader = compile ps_3_0 PixelBlendShader();
	}
}