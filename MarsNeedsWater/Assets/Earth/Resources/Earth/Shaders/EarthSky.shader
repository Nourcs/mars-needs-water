// The painted sky of the mini Earth: a bright blue fading to a pale horizon (the fog's colour, so far land melts
// into it) and a soft sun. The clouds are real shapes in the world (Runtime/EarthClouds.cs).
Shader "MarsNeedsWater/EarthSky"
{
    Properties
    {
        _Zenith ("Overhead", Color) = (0.13, 0.42, 0.95, 1)
        _Middle ("Half-way up", Color) = (0.33, 0.66, 1.0, 1)
        _Horizon ("Horizon (same as the fog)", Color) = (0.78, 0.92, 1.0, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Middle, _Horizon;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash(float2 c)
            {
                float3 q = frac(float3(c.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float Noise(float2 p)
            {
                float2 c = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(c), Hash(c + float2(1, 0)), f.x), lerp(Hash(c + float2(0, 1)), Hash(c + float2(1, 1)), f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                Light sun = GetMainLight();
                float up = saturate(d.y);

                // the blue: horizon, middle, overhead
                half3 col = lerp(_Horizon.rgb, _Middle.rgb, smoothstep(0.0, 0.28, up));
                col = lerp(col, _Zenith.rgb, smoothstep(0.2, 0.95, up));
                // below the horizon: the haze carries on
                col = lerp(col, _Horizon.rgb, saturate(-d.y * 8.0));

                // the sun: a soft disc with a wide warm glow
                float sd = dot(d, sun.direction);
                col += half3(1.0, 0.9, 0.65) * (smoothstep(0.9993, 0.9997, sd) * 3.0 + pow(saturate(sd), 180.0) * 0.5 + pow(saturate(sd), 12.0) * 0.12);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
