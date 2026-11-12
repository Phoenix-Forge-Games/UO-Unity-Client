// The game's sky (DayNight drives it): a day gradient with a warm glow round the sun near the horizon, the sun's disc,
// and at night a dark blue sky full of stars (they twinkle, and a faint band of the galaxy crosses the sky) with the
// moon; a layer of clouds drifting on the wind (cover and darkness from the weather), lit by the sun, the sunset or
// the moon, hiding what's behind them; and lightning flashes. _Day is 1 by day and 0 at night; _Dark (0..1) darkens
// everything further (dungeons).
Shader "UOGame/Sky"
{
    Properties
    {
        _Day ("Day (0 night, 1 day)", Range(0, 1)) = 1
        _Dark ("Extra darkness", Range(0, 1)) = 0
        _Golden ("Amber hour (sun low)", Range(0, 1)) = 0
        _SunDir ("Direction to the sun", Vector) = (0, 0.7, 0.7, 0)
        _MoonDir ("Direction to the moon", Vector) = (0.3, 0.6, -0.7, 0)
        _DayZenith ("Day zenith", Color) = (0.24, 0.45, 0.80, 1)
        _DayHorizon ("Day horizon", Color) = (0.70, 0.80, 0.90, 1)
        _NightZenith ("Night zenith", Color) = (0.005, 0.008, 0.025, 1)
        _NightHorizon ("Night horizon", Color) = (0.025, 0.035, 0.07, 1)
        _Sunset ("Sunset glow", Color) = (1.0, 0.56, 0.18, 1)
        _Ground ("Below the horizon", Color) = (0.20, 0.20, 0.22, 1)
        _SunColor ("Sun", Color) = (1.0, 0.95, 0.85, 1)
        _MoonColor ("Moon", Color) = (0.85, 0.88, 0.95, 1)
        _SunSize ("Sun size", Range(0.0001, 0.01)) = 0.0009
        _MoonSize ("Moon size", Range(0.0001, 0.01)) = 0.0014
        _MoonPhase ("Moon phase (0 new, 0.5 full, 1 new)", Range(0, 1)) = 0.5
        _Stars ("Star brightness", Range(0, 4)) = 1.6
        _StarDensity ("Star density", Range(50, 600)) = 260
        _Clouds ("Cloud cover", Range(0, 1)) = 0.3
        _CloudDark ("Cloud darkness", Range(0, 1)) = 0
        _Overcast ("Overcast (rain or snow: cloud from horizon to horizon)", Range(0, 1)) = 0
        _CloudOffset ("Cloud drift (xy)", Vector) = (0, 0, 0, 0)
        _CloudScale ("Cloud scale", Range(0.1, 4)) = 1.2
        _Flash ("Lightning flash", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Day, _Dark, _Golden;
                float4 _SunDir, _MoonDir;
                float4 _DayZenith, _DayHorizon, _NightZenith, _NightHorizon, _Sunset, _Ground, _SunColor, _MoonColor;
                float _SunSize, _MoonSize, _MoonPhase, _Stars, _StarDensity;
                float _Clouds, _CloudDark, _Overcast, _CloudScale, _Flash;
                float4 _CloudOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Stars: one candidate per cell of a grid on the sky, most cells empty, a few bright ones.
            float StarField(float3 dir)
            {
                float3 p = dir * _StarDensity;
                float3 cell = floor(p);
                float h = Hash(cell);
                if (h < 0.92)
                {
                    return 0;
                }

                float3 centre = cell + 0.5 + (float3(Hash(cell + 1.3), Hash(cell + 2.7), Hash(cell + 5.1)) - 0.5) * 0.6;
                float d = length(p - centre);
                float size = lerp(0.06, 0.16, Hash(cell + 9.2));
                float star = saturate(1 - d / size);
                star *= star;
                float twinkle = 0.75 + 0.25 * sin(_Time.y * (1.5 + 3 * Hash(cell + 4.4)) + h * 40);
                return star * twinkle * lerp(0.4, 1.6, pow(Hash(cell + 7.7), 4));
            }

            float Hash2(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash2(i), Hash2(i + float2(1, 0)), u.x), lerp(Hash2(i + float2(0, 1)), Hash2(i + 1), u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0, a = 0.5;
                for (int o = 0; o < 5; o++)
                {
                    v += a * Noise(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }

                return v;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 dir = normalize(i.dir);
                float up = dir.y;
                float3 sun = normalize(_SunDir.xyz);
                float3 moon = normalize(_MoonDir.xyz);

                // The sky's gradient, day and night, and below the horizon.
                float h = saturate(up);
                float3 zenith = lerp(_NightZenith.rgb, _DayZenith.rgb, _Day);
                float3 horizon = lerp(_NightHorizon.rgb, _DayHorizon.rgb, _Day);
                float3 col = lerp(horizon, zenith, pow(h, 0.45));

                // Amber mornings and evenings: the horizon warms all round while the sun is low, most toward the sun, and
                // the sky above takes a softer rose; the sun's own glow is strongest just above the horizon.
                float toward = pow(saturate(dot(dir, sun) * 0.5 + 0.5), 4);
                float low = _Golden;
                float hband = 1 - pow(h, 0.6);
                col = lerp(col, _Sunset.rgb * lerp(0.7, 1.15, toward), low * saturate(hband * 1.3) * lerp(0.7, 1.0, toward));
                col = lerp(col, col * float3(1.12, 0.98, 0.78), low * (1 - hband) * 0.7);
                col += _Sunset.rgb * low * pow(saturate(dot(dir, sun)), 12) * 0.6;

                // The sun's disc and its halo.
                float sd = dot(dir, sun);
                float disc = smoothstep(1 - _SunSize, 1 - _SunSize * 0.6, sd);
                float halo = pow(saturate(sd), 2000) * 0.8 + pow(saturate(sd), 60) * 0.15;
                col += _SunColor.rgb * (disc * 6 + halo) * saturate(_Day * 2) * step(-0.05, sun.y);

                // Night: stars (fading out near the horizon and as day comes), a faint galaxy band, and the moon.
                float night = saturate(1 - _Day * 1.6);
                if (night > 0.001 && up > -0.05)
                {
                    float fade = saturate(up * 4) * night;
                    float band = exp(-pow(dot(dir, normalize(float3(0.35, 0.25, 0.9))) * 3.2, 2));
                    float dust = Fbm(float2(dir.x + dir.y * 0.7, dir.z - dir.y * 0.4) * 9) ;
                    col += float3(0.10, 0.11, 0.16) * band * smoothstep(0.35, 0.8, dust) * 0.14 * fade;
                    col += StarField(dir) * _Stars * fade * float3(0.95, 0.97, 1.0);
                    // Denser faint stars in the band.
                    col += StarField(dir * 1.7 + 3.1) * _Stars * 0.35 * band * fade;

                    float md = dot(dir, moon);
                    float moonDisc = smoothstep(1 - _MoonSize, 1 - _MoonSize * 0.9, md);
                    if (moonDisc > 0)
                    {
                        // The lit part: a second disc, offset by the phase, takes a bite out of the moon.
                        // The dark part: a disc the moon's size, right over it at new moon and off its edge at full.
                        float3 side = normalize(cross(moon, float3(0, 1, 0)) + 1e-4);
                        float r = sqrt(2 * _MoonSize);
                        float k = abs(_MoonPhase - 0.5) * 2; // 0 full, 1 new
                        float3 shadowDir = normalize(moon + side * sign(_MoonPhase - 0.5) * (1 - k) * 2.2 * r);
                        float shadow = smoothstep(1 - _MoonSize * 1.05, 1 - _MoonSize * 0.85, dot(dir, shadowDir));
                        float lit = 1 - shadow;
                        float craters = 0.82 + 0.18 * Noise(float2(dot(dir, side), dir.y) * 260);
                        col = lerp(col, _MoonColor.rgb * craters * (0.15 + 0.85 * lit), moonDisc * night);
                    }

                    col += _MoonColor.rgb * pow(saturate(md), 3000) * 0.3 * night;
                }

                // Clouds: a flat layer overhead seen in perspective, thinning toward the horizon. Overcast (rain or snow)
                // closes every gap, down to the horizon, hiding the sun, moon and stars.
                if (up > -0.05 && (_Clouds > 0.001 || _Overcast > 0.001))
                {
                    float2 uv = dir.xz / (up + 0.12) * _CloudScale * 0.35 + _CloudOffset.xy;
                    float n = Fbm(uv) * 0.75 + Fbm(uv * 3.1 + 5.3) * 0.25;
                    float cover = lerp(smoothstep(1.05 - _Clouds * 1.1, 1.3 - _Clouds * 1.1, n), 1, _Overcast);
                    float horizonFade = lerp(saturate(up * 6), 1, _Overcast);
                    float shade = saturate(n * 1.4 - 0.2); // thicker parts darker underneath
                    float3 dayCloud = lerp(float3(1, 1, 1), float3(0.42, 0.44, 0.48), saturate(_CloudDark * 0.8 + shade * 0.35));
                    dayCloud *= lerp(1, 0.55, _CloudDark);
                    dayCloud += _Sunset.rgb * low * toward * 0.8;
                    float3 nightCloud = lerp(float3(0.045, 0.05, 0.075), float3(0.015, 0.016, 0.022), _CloudDark);
                    nightCloud += _MoonColor.rgb * pow(saturate(dot(dir, moon)), 8) * 0.06 * (1 - _CloudDark);
                    float3 cloudCol = lerp(nightCloud, dayCloud, _Day);
                    cloudCol += float3(0.8, 0.85, 1.0) * _Flash * (0.6 + shade);
                    col = lerp(col, cloudCol, cover * horizonFade * lerp(0.95, 1, _Overcast));
                    // Grey skies between the clouds too, as they close in.
                    col = lerp(col, cloudCol * 0.9, saturate(saturate(_Clouds - 0.6) * 1.5 * _CloudDark + _Overcast * 0.35) * horizonFade);
                }

                col += float3(0.5, 0.55, 0.7) * _Flash * 0.35;

                // Below the horizon: the ground's colour, lit by day.
                float below = saturate(-up * 8);
                col = lerp(col, _Ground.rgb * lerp(0.05, 1, _Day), below);

                col *= 1 - _Dark * 0.95;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
