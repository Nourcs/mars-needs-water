// The painted sky of the mini Earth: a bright blue fading to a pale horizon (the fog's colour, so far land melts
// into it), a soft sun, and puffy two-tone clouds drawn from noise that drifts and never repeats.
Shader "MarsNeedsWater/EarthSky"
{
    Properties
    {
        _Zenith ("Overhead", Color) = (0.13, 0.42, 0.95, 1)
        _Middle ("Half-way up", Color) = (0.33, 0.66, 1.0, 1)
        _Horizon ("Horizon (same as the fog)", Color) = (0.78, 0.92, 1.0, 1)
        _CloudLight ("Cloud, sunny side", Color) = (1.0, 0.99, 0.96, 1)
        _CloudShade ("Cloud, shaded side", Color) = (0.70, 0.78, 0.96, 1)
        _Cover ("How much sky the clouds fill", Range(0, 1)) = 0.37
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
                half4 _Zenith, _Middle, _Horizon, _CloudLight, _CloudShade;
                half _Cover;
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

            // big rounded shapes: few octaves, each turned so nothing lines up
            float Puff(float2 p)
            {
                float a = Noise(p) * 0.55;
                p = mul(float2x2(0.8, -0.6, 0.6, 0.8), p) * 2.1 + 7.3;
                a += Noise(p) * 0.28;
                p = mul(float2x2(0.8, -0.6, 0.6, 0.8), p) * 2.3 + 3.1;
                a += Noise(p) * 0.17;
                return a;
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

                // clouds on a flat layer far overhead
                if (d.y > 0.01)
                {
                    // a gently domed layer: clouds keep their round shape instead of stretching out towards the horizon
                    float2 p = d.xz / (d.y * 0.55 + 0.45) * 2.7 + _Time.y * float2(0.012, 0.005);
                    float2 q = p + (float2(Noise(p * 0.9 + 11.0), Noise(p * 0.9 - 5.0)) - 0.5) * 0.5;
                    float cut = 1.0 - _Cover * (0.86 + 0.28 * Noise(p * 0.23 + 2.0));
                    float f = Puff(q);
                    float body = smoothstep(cut - 0.012, cut + 0.02, f);
                    // the same shape looked up a step towards the sun: where it is thinner there, this side is lit
                    float2 toSun = normalize(sun.direction.xz + 1e-4) * 0.22;
                    float f2 = Puff(q + toSun);
                    float lit = smoothstep(-0.015, 0.05, f - f2 + 0.02) ;
                    float core = smoothstep(cut + 0.05, cut + 0.2, f);          // thick middles sit in their own shade
                    half3 cloud = lerp(_CloudShade.rgb, _CloudLight.rgb, saturate(lit * (1.0 - core * 0.55) + 0.12));
                    // thin out and take the haze's colour towards the horizon
                    float far = smoothstep(0.02, 0.22, d.y);
                    cloud = lerp(_Horizon.rgb, cloud, 0.35 + 0.65 * far);
                    col = lerp(col, cloud, body * (0.25 + 0.75 * far));
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
