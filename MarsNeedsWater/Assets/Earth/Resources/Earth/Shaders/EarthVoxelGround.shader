// The ground of the voxel look (Core/Voxel.cs): block columns. Every block takes the colour of the square's ground
// picture at its own middle, its own random shade, and earth under the grass on its sides.
Shader "MarsNeedsWater/EarthVoxelGround"
{
    Properties
    {
        [MainTexture] _BaseMap ("Ground picture (alpha = kind of ground)", 2D) = "white" {}
        _VoxN ("Columns along the square", Float) = 768
        _VoxSeed ("Seed of this square", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float _VoxN;
            float _VoxSeed;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EarthToon.hlsl"
            #include "EarthVoxel.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 extra : TEXCOORD1; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2;
                float fog : TEXCOORD3; float2 extra : TEXCOORD4; float3 normalOS : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.normalOS = v.normalOS;
                o.uv = v.uv;
                o.extra = v.extra.xy;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float side = step(0.5, i.extra.y);
                float2 g = i.uv * _VoxN;
                float far = saturate(max(fwidth(g.x), fwidth(g.y)) * 1.5 - 0.5);
                // a side face lies on the line between two columns: it belongs to the one behind it
                float2 own = g + float2(-i.normalOS.x, i.normalOS.z) * 0.5 * side;
                float2 cell = clamp(floor(own), 0.0, _VoxN - 1.0);
                half4 px = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_PointClamp, (cell + 0.5) / _VoxN, 0);
                half3 soft = SAMPLE_TEXTURE2D(_BaseMap, sampler_LinearClamp, i.uv).rgb;
                half3 col = lerp(px.rgb, soft, far * (1.0 - side));
                float kind = floor(px.a * 255.0 + 0.5);

                float depth = i.extra.x;                    // blocks below the top of this column (sides only)
                float row = floor(depth + 0.0001) * side;
                float2 inside = frac(g);
                if (side > 0.5)
                {
                    float along = abs(i.normalOS.x) > 0.5 ? g.y : g.x;
                    inside = float2(frac(along), frac(depth));
                    // what is under the top: earth under grass and crops, stone under snow and paving
                    half3 earth = half3(0.26, 0.155, 0.085);
                    half3 stone = half3(0.2, 0.19, 0.18);
                    float under = smoothstep(0.2, 0.22, depth);
                    if (kind < 3.5) col = lerp(col, earth, under);
                    else if (kind > 5.5 && kind < 7.5) col = lerp(col, stone, under);
                    else col *= lerp(1.0, 0.86, under);
                    cell += float2(abs(i.normalOS.x) > 0.5 ? 0.0 : 0.37, abs(i.normalOS.x) > 0.5 ? 0.37 : 0.0);
                }
                col = ToonPop(col);
                col = VShade(col, float3(cell, row), inside, _VoxSeed, kind > 6.5 && kind < 7.5 ? 0.07 : 0.12, far);

                float3 n = normalize(i.normalWS);
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 sky = SampleSH(n);
                half shadow = ToonShadow(sun.shadowAttenuation, i.positionWS);
                col = ToonLight(col, dot(n, sun.direction), shadow, sun.color, sky, 1.0);
                return half4(MixFog(col, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vo { float4 positionCS : SV_POSITION; };
            Vo vert(A v)
            {
                Vo o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 pw = TransformObjectToWorld(v.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }
            half4 frag(Vo i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vo { float4 positionCS : SV_POSITION; };
            Vo vert(A v) { Vo o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag(Vo i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Vo { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Vo vert(A v) { Vo o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(Vo i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
