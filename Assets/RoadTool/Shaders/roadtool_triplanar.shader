
HEADER
{
	Description = "";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#include "common/shared.hlsl"
	#include "procedural.hlsl"

	#define S_UV2 1
	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vPositionOs : TEXCOORD14;
	float3 vNormalOs : TEXCOORD15;
	float4 vTangentUOs_flTangentVSign : TANGENT	< Semantic( TangentU_SignV ); >;
	float4 vColor : COLOR0;
	float4 vTintColor : COLOR1;
	#if ( PROGRAM == VFX_PROGRAM_PS )
		bool vFrontFacing : SV_IsFrontFace;
	#endif
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		
		PixelInput i = ProcessVertex( v );
		i.vPositionOs = v.vPositionOs.xyz;
		i.vColor = v.vColor;
		
		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;
		
		VS_DecodeObjectSpaceNormalAndTangent( v, i.vNormalOs, i.vTangentUOs_flTangentVSign );
		return FinalizeVertex( i );
		
	}
}

PS
{
	#include "common/pixel.hlsl"

	RenderState(CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT);

	DynamicCombo(D_OPAQUE_FADE, 0..1, Sys(PC));

	// General
    bool g_bFollowWorldSpace < UiGroup("General,0/,0/0"); Default1(1); > ;
    bool g_bUseObjectSpace < UiGroup("General,0/,0/0"); Default1(0); > ;
	float2 g_vTile < UiGroup( "General,0/,0/0" ); Default2( 1,1 ); Range2( 0,0, 100,100 ); >;
	float2 g_vOffset < UiGroup( "General,0/,0/0" ); Default2( 0,0 ); Range2( 0,0, 1,1 ); >;

	// Albedo
	CreateInputTexture2D( BaseColour, Srgb, 8, "None", "_color", "Albedo,1/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tBaseColour < Channel( RGBA, Box( BaseColour ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	
	float4 g_vBlendColour < UiType( Color ); UiGroup( "Albedo,1/,0/0" ); Default4( 0.00, 0.00, 0.00, 1.00 ); >;
	float g_flBlendAmount < UiGroup( "Albedo,1/,0/0" ); Default1( 0.5 ); Range1( 0, 1 ); >;

	// Normal
	CreateInputTexture2D( NormalMap, Linear, 8, "NormalizeNormals", "_normal", "Normal,2/,0/0", Default4( 0.50, 0.50, 1.00, 1.00 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
	float g_flNormalStrength < UiGroup( "Normal,2/,0/0" ); Default1( 1.0 ); Range1( 0, 10 ); >;

	// Emission
	CreateInputTexture2D( Emission, Srgb, 8, "None", "_selfillum", "Emission,3/,0/0", Default4( 0.00, 0.00, 0.00, 1.00 ) );
	Texture2D g_tEmission < Channel( RGBA, Box( Emission ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
	float4 g_vEmissionTint < UiType( Color ); UiGroup( "Emission,3/,0/0" ); Default4( 1.00, 1.00, 1.00, 1.00 ); >;
	float g_flEmissionTintBlend < UiType( Slider ); UiGroup( "Emission,3/,0/0" ); Default1( 0 ); Range1( 0, 1 ); >;
	float g_flEmissionTintBoost < UiType( Slider ); UiGroup( "Emission,3/,0/0" ); Default1( 1 ); Range1( 0, 100 ); >;

	// Roughness
	CreateInputTexture2D( Roughness, Linear, 8, "Inverse", "_rough", "Roughness,4/,0/0", Default4(0.5, 0.5, 0.5, 0) );
    Texture2D g_tRoughness < Channel(R, Box(Roughness), Linear); OutputFormat(ATI1N); SrgbRead(False); > ;
	float g_flRoughnessScaleFactor < Default(1); Range(0, 2); UiType(Slider); UiGroup("Roughness"); >;

	// Metalness
	CreateInputTexture2D( Metalness, Linear, 8, "None", "_metal", "Metalness,5/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tMetalness < Channel(R, Box(Metalness), Linear); OutputFormat(ATI1N); SrgbRead(False); >;

	// Ambient Occlusion
	CreateInputTexture2D( AmbientOcclusion, Linear, 8, "None", "_ao", "Ambient Occlusion,6/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tAmbientOcclusion < Channel(R, Box(AmbientOcclusion), Linear); OutputFormat(ATI1N); SrgbRead(False); >;

	float4 TexTriplanar_Color( in Texture2D tTex, in SamplerState sSampler, float3 vPosition, float3 vNormal )
	{
		float2 uvX = vPosition.zy;
		float2 uvY = vPosition.xz;
		float2 uvZ = vPosition.xy;

		if (g_bFollowWorldSpace)
		{
			float2 tiling = g_vTile * 0.05;
			
			uvX = vPosition.zy * tiling + g_vOffset;
			uvY = vPosition.xz * tiling + g_vOffset;
			uvZ = vPosition.xy * tiling + g_vOffset;
		}
	
		float3 triblend = saturate(pow(abs(vNormal), 4));
		triblend /= max(dot(triblend, half3(1,1,1)), 0.0001);
	
		half3 axisSign = vNormal < 0 ? -1 : 1;
	
		uvX.x *= axisSign.x;
		uvY.x *= axisSign.y;
		uvZ.x *= -axisSign.z;
	
		float4 colX = Tex2DS( tTex, sSampler, uvX );
		float4 colY = Tex2DS( tTex, sSampler, uvY );
		float4 colZ = Tex2DS( tTex, sSampler, uvZ );
	
		return colX * triblend.x + colY * triblend.y + colZ * triblend.z;
	}

	float3 DecodeNormalWithStrength(float3 n, float strength)
	{
		n = n * 2.0 - 1.0;
		n.xy *= strength;
		n.z = sqrt(saturate(1.0 - dot(n.xy, n.xy)));
		return n;
	}
	
	float3 TexTriplanar_Normal( in Texture2D tTex, in SamplerState sSampler, float3 vPosition, float3 vNormal )
	{
		float2 uvX = vPosition.zy;
		float2 uvY = vPosition.xz;
		float2 uvZ = vPosition.xy;

		if (g_bFollowWorldSpace)
		{
			float2 tiling = g_vTile * 0.05;
			
			uvX = vPosition.zy * tiling + g_vOffset;
			uvY = vPosition.xz * tiling + g_vOffset;
			uvZ = vPosition.xy * tiling + g_vOffset;
		}
	
		float3 triblend = saturate( pow( abs( vNormal ), 4 ) );
		triblend /= max( dot( triblend, half3( 1, 1, 1 ) ), 0.0001 );

		// float3 triblend = abs(vNormal);
		// triblend = triblend / (triblend.x + triblend.y + triblend.z);
	
		half3 axisSign = vNormal < 0 ? -1 : 1;
	
		uvX.x *= axisSign.x;
		uvY.x *= axisSign.y;
		uvZ.x *= -axisSign.z;
	
		float3 tnormalX = DecodeNormalWithStrength( Tex2DS( tTex, sSampler, uvX ).xyz, g_flNormalStrength );
		float3 tnormalY = DecodeNormalWithStrength( Tex2DS( tTex, sSampler, uvY ).xyz, g_flNormalStrength );
		float3 tnormalZ = DecodeNormalWithStrength( Tex2DS( tTex, sSampler, uvZ ).xyz, g_flNormalStrength );
	
		tnormalX.x *= axisSign.x;
		tnormalY.x *= axisSign.y;
		tnormalZ.x *= -axisSign.z;
	
		tnormalX = half3( tnormalX.xy + vNormal.zy, vNormal.x );
		tnormalY = half3( tnormalY.xy + vNormal.xz, vNormal.y );
		tnormalZ = half3( tnormalZ.xy + vNormal.xy, vNormal.z );
	
		return normalize(
			tnormalX.zyx * triblend.x +
			tnormalY.xzy * triblend.y +
			tnormalZ.xyz * triblend.z +
			vNormal
		);
	}
	
	float4 MainPs(PixelInput i) : SV_Target0
	{
		Material m = Material::Init();

		float2 l_2 = TileAndOffsetUv( i.vTextureCoords.xy, g_vTile, g_vOffset );
		float3 texturePosition_TAO = float3( l_2, 0 );

		float3 worldPosition_NTAO = (i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz) / 39.3701;
		float3 inputPosition = lerp( texturePosition_TAO, worldPosition_NTAO, g_bFollowWorldSpace );

        float3 normalWs = normalize(i.vNormalWs.xyz);
        float3 normalToUse = normalWs;

        if (g_bUseObjectSpace)
        {
            inputPosition = i.vPositionOs.xyz;
            normalToUse = normalize(i.vNormalOs.xyz);
		}

        float4 l_3 = TexTriplanar_Color(g_tBaseColour, g_sTrilinearWrap, inputPosition, normalToUse);
		float4 l_4 = g_vBlendColour;
		float l_5 = g_flBlendAmount;
        float4 l_6 = saturate(lerp(l_3, l_4, l_5));
        float3 l_7 = TexTriplanar_Normal(g_tNormalMap, g_sTrilinearWrap, inputPosition, normalToUse);
        float4 l_9 = TexTriplanar_Color(g_tRoughness, g_sTrilinearWrap, inputPosition, normalToUse);
        float4 l_10 = TexTriplanar_Color(g_tAmbientOcclusion, g_sTrilinearWrap, inputPosition, normalToUse);
        float4 l_11 = TexTriplanar_Color(g_tMetalness, g_sTrilinearWrap, inputPosition, normalToUse);
        float4 l_12 = TexTriplanar_Color(g_tEmission, g_sTrilinearWrap, inputPosition, normalToUse);
		float4 l_13 = g_vEmissionTint;
		float l_14 = g_flEmissionTintBlend;
		float4 l_15 = saturate( lerp( l_12, l_13, l_14 ) );

		m.Albedo = l_6.rgb;
        m.Normal = l_7;
        m.Roughness = l_9.r * g_flRoughnessScaleFactor;
		m.Metalness = l_11.r;
		m.AmbientOcclusion = l_10.r;
		m.Emission = l_15.rgb * g_flEmissionTintBoost;
		m.TintMask = 1;
		m.Opacity = 1;
		m.Transmission = 0;

		// Apply tint (if used on a model renderer)
		m.Albedo *= i.vTintColor.rgb;
		m.Opacity *= i.vTintColor.a;

		// Result node takes normal as tangent space, convert it to world space now (Disabled bcs this line seems to cause weird stuff for triplanar normals)
		// m.Normal = TransformNormal( m.Normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		
		// for some toolvis shit
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = i.vTextureCoords.xy;
				
		return ShadingModelStandard::Shade(i, m);
	}
}
