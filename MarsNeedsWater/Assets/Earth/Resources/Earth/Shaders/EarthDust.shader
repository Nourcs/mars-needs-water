// Dust, powder snow, tyre smoke and flying clods kicked up by the jeep's wheels (particles from Runtime/JeepTrails.cs).
// Each one is a soft lumpy blob drawn without a texture; the particle's own turn makes every blob sit differently.
Shader "MarsNeedsWater/EarthDust"
{
    Properties
    {
        _Soft ("Soft edge (0 hard clods, 1 soft puffs)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Dust"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "EarthToon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Soft;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.color = v.color;
                o.uv = v.uv;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float d = length(p), ang = atan2(p.y, p.x);
                // a lumpy outline: a few bulges of different sizes round the edge
                float r = 0.76 + 0.09 * sin(ang * 3.0 + 0.6) + 0.06 * sin(ang * 5.0 + 1.7) + 0.035 * sin(ang * 8.0 + 4.0);
                float edge = lerp(0.04, 0.34, _Soft);
                float mask = smoothstep(r, r - edge, d);
                half a = i.color.a * mask;
                clip(a - 0.004);

                // brighter in the middle, in two soft steps, like a painted cloud
                half tone = lerp(0.8, 1.0, smoothstep(0.7, 0.25, d)) + 0.06 * smoothstep(0.35, 0.1, d);
                Light sun = GetMainLight();
                half3 sky = SampleSH(half3(0, 1, 0));
                half3 col = ToonLight(ToonPop(i.color.rgb, 1.1), 0.55, CloudShade(i.positionWS), sun.color, sky, 1.0) * tone;
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
