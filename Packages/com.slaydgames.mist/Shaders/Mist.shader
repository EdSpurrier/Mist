// Volumetric mist for URP. One pass per stage of section 4; MistRenderPass.cs runs them in order:
//   0 Ground Shade (camera res, multiplies the scene)  ->  1 March (mist res)  ->  2 Temporal
//   ->  3/4 Blur H/V  ->  5 Composite (camera res, depth-aware upsample, scene * a + rgb).
Shader "Hidden/SlaydGames/Mist"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        #pragma target 4.5
        #pragma vertex MistVert
        #include "MistCommon.hlsl"

        // Separable 5-tap Gaussian (4.4.2), each tap also weighted by how close its depth is to the
        // centre's, so there is no halo over silhouettes. Used by the Blur H and Blur V passes.
        float4 MistBlur(float2 uv, float2 dir)
        {
            static const float offsets[3] = { 0.0, 1.384615, 3.230769 };
            static const float weights[3] = { 0.227027, 0.316216, 0.070270 };

            float z0 = MistViewZ(MistRawDepth(uv));
            float4 sum = SAMPLE_TEXTURE2D_LOD(_MistSource, sampler_LinearClamp, uv, 0) * weights[0];
            float wsum = weights[0];
            [unroll] for (int k = 1; k < 3; k++)
            {
                [unroll] for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    float2 tuv = uv + dir * (offsets[k] * sgn) * _MistTargetSize.zw;
                    float z = MistViewZ(MistRawDepth(tuv));
                    float w = weights[k] * exp(-abs(z - z0) / (0.03 * abs(z0) + 0.2));
                    sum += SAMPLE_TEXTURE2D_LOD(_MistSource, sampler_LinearClamp, tuv, 0) * w;
                    wsum += w;
                }
            }
            return sum / max(wsum, 1e-5);
        }
        ENDHLSL

        // ------------------------------------------------------------ 0: ground shade (4.5)
        Pass
        {
            Name "Mist Ground Shade"
            Blend DstColor Zero, Zero One   // multiply the scene colour, keep its alpha

            HLSLPROGRAM
            #pragma fragment Frag

            float4 Frag(Varyings IN) : SV_Target
            {
                float raw = MistRawDepth(IN.uv);
                if (MistIsSky(raw) || _MistSunDir.w < 0.5) return 1.0;
                float3 p = MistWorldPos(IN.uv, raw);
                float3 L = _MistSunDir.xyz;

                // Analytic: how much mist the sun passes through on its way to this point. No noise, a soft shadow.
                float s = 0.0;
                [loop] for (int i = 0; i < _MistShadeCount; i++)
                {
                    float2 r = VolHit(i, p, L);
                    if (r.y > r.x) s += VolC(i).w * (r.y - r.x) * (1.0 - VolD(i).w * 0.5) * (1.0 - VolF(i).z * 0.5);
                }
                float shade = exp(-s * 0.6);
                return float4(lerp(1.0, shade, _MistParams1.x).xxx, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ 1: the march (4.3)
        Pass
        {
            Name "Mist March"
            Blend Off

            HLSLPROGRAM
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            // Interleaved gradient noise, per pixel.
            float Ign(float2 p) { return frac(52.9829189 * frac(0.06711056 * p.x + 0.00583715 * p.y)); }

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float raw = MistRawDepth(uv);

                // The ray runs from the near plane to the scene (or the far distance under the sky).
                // Starting at the near plane keeps orthographic cameras working.
                float3 pNear = MistWorldPos(uv, UNITY_NEAR_CLIP_VALUE);
                float3 pFar  = MistWorldPos(uv, UNITY_RAW_FAR_CLIP_VALUE);
                float3 toFar = pFar - pNear;
                float farLen = length(toFar);
                float3 dir = toFar / max(farLen, 1e-5);
                float sceneT = MistIsSky(raw) ? min(farLen, _MistParams1.z) : dot(MistWorldPos(uv, raw) - pNear, dir);

                MistGatherHits(pNear, dir, sceneT);
                if (s_HitCount == 0) return float4(0.0, 0.0, 0.0, 1.0);

                float jitter = frac(Ign(floor(uv * _MistTargetSize.xy)) + _MistParams0.w);
                int maxSteps = (int)_MistParams0.x;

                // Sun through a gentle Henyey-Greenstein forward lobe, constant along the ray.
                float3 L = _MistSunDir.xyz;
                const float g = 0.45;
                float phase = (1.0 - g * g) / pow(abs(1.0 + g * g - 2.0 * g * dot(dir, L)), 1.5) * 0.3 + 0.55;
                float3 sunCol = _MistSunColor.rgb * phase;
                bool hasSun = _MistSunDir.w > 0.5;
                bool selfShadow = _MistParams0.y > 0.5 && hasSun;

                float tLast = s_HitT1[0];
                for (int q = 1; q < s_HitCount; q++) tLast = max(tLast, s_HitT1[q]);
                MistPickLights(pNear, dir, s_HitT0[0], tLast);

                float3 inscatter = 0.0;
                float trans = 1.0;

                int s = 0;
                [loop] while (s < s_HitCount && trans >= 0.01)
                {
                    // A span: this box plus every later box that enters before the span ends. Marching
                    // them together is what keeps overlapping boxes from showing seams.
                    float t0 = s_HitT0[s], t1 = s_HitT1[s];
                    s_SpanMask = 1u << s;
                    float3 h0 = VolB(s_HitId[s]).xyz;
                    float smallest = min(h0.x, min(h0.y, h0.z));
                    int e = s + 1;
                    [loop] while (e < s_HitCount && s_HitT0[e] < t1)
                    {
                        t1 = max(t1, s_HitT1[e]);
                        s_SpanMask |= 1u << e;
                        float3 h = VolB(s_HitId[e]).xyz;
                        smallest = min(smallest, min(h.x, min(h.y, h.z)));
                        e++;
                    }
                    s = e;

                    float len = t1 - t0;
                    int steps = clamp((int)ceil(len / 0.08), 2, maxSteps);
                    float stepLen = len / steps;
                    float ls = clamp(0.3 * smallest, 0.1, 3.0);   // self-shadow tap length

                    [loop] for (int k = 0; k < steps; k++)
                    {
                        float3 p = pNear + dir * (t0 + stepLen * (k + jitter));
                        float3 col, glow;
                        float d = MistSpanDensity(p, col, glow);
                        if (d <= 0.001) continue;

                        float sunT = 1.0;
                        if (hasSun)
                        {
                            sunT = MainLightRealtimeShadow(TransformWorldToShadowCoord(p));   // the sun's shadow cascades at p
                            if (selfShadow && sunT > 0.0)
                                sunT *= exp(-(MistSpanDensityOnly(p + L * ls) * ls + MistSpanDensityOnly(p + L * (ls * 3.0)) * ls * 2.0));
                        }

                        float3 c = (sunCol * sunT + _MistAmbient.rgb + MistSceneLights(p)) * col + glow;
                        float st = exp(-d * stepLen);   // Beer-Lambert
                        inscatter += trans * c * (1.0 - st);
                        trans *= st;
                        if (trans < 0.01) break;
                    }
                }
                return float4(inscatter, trans);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ 2: temporal (4.4.1)
        Pass
        {
            Name "Mist Temporal"
            Blend Off

            HLSLPROGRAM
            #pragma fragment Frag

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float4 cur = SAMPLE_TEXTURE2D_LOD(_MistSource, sampler_PointClamp, uv, 0);

                // Reproject this pixel's world point into last frame's view.
                float raw = MistRawDepth(uv);
                float3 p = MistWorldPos(uv, raw);
                float4 prevCS = mul(_MistPrevViewProj, float4(p, 1.0));
                float2 ndc = prevCS.xy / max(prevCS.w, 1e-5);
            #if UNITY_UV_STARTS_AT_TOP
                ndc.y = -ndc.y;
            #endif
                float2 prevUV = ndc * 0.5 + 0.5;
                bool valid = _MistParams1.w > 0.5 && prevCS.w > 0.0 && all(prevUV >= 0.0) && all(prevUV <= 1.0);
                float4 hist = valid ? SAMPLE_TEXTURE2D_LOD(_MistSource2, sampler_LinearClamp, prevUV, 0) : cur;

                // Clamp the history to this pixel and its 4 neighbours, padded by 25%.
                float2 px = _MistTargetSize.zw;
                float4 n0 = SAMPLE_TEXTURE2D_LOD(_MistSource, sampler_PointClamp, uv + float2( px.x, 0), 0);
                float4 n1 = SAMPLE_TEXTURE2D_LOD(_MistSource, sampler_PointClamp, uv + float2(-px.x, 0), 0);
                float4 n2 = SAMPLE_TEXTURE2D_LOD(_MistSource, sampler_PointClamp, uv + float2(0,  px.y), 0);
                float4 n3 = SAMPLE_TEXTURE2D_LOD(_MistSource, sampler_PointClamp, uv + float2(0, -px.y), 0);
                float4 lo = min(cur, min(min(n0, n1), min(n2, n3)));
                float4 hi = max(cur, max(max(n0, n1), max(n2, n3)));
                float4 pad = (hi - lo) * 0.25;
                hist = clamp(hist, lo - pad, hi + pad);

                float blend = valid ? min(0.85, 0.15 + _MistParams1.y * 1.5) : 1.0;
                return lerp(hist, cur, blend);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------ 3/4: depth-aware blur (4.4.2)
        Pass
        {
            Name "Mist Blur H"
            Blend Off
            HLSLPROGRAM
            #pragma fragment Frag
            float4 Frag(Varyings IN) : SV_Target { return MistBlur(IN.uv, float2(1.0, 0.0)); }
            ENDHLSL
        }

        Pass
        {
            Name "Mist Blur V"
            Blend Off
            HLSLPROGRAM
            #pragma fragment Frag
            float4 Frag(Varyings IN) : SV_Target { return MistBlur(IN.uv, float2(0.0, 1.0)); }
            ENDHLSL
        }

        // ------------------------------------------------------------ 5: upsample and composite (4.4.3)
        Pass
        {
            Name "Mist Composite"
            Blend One SrcAlpha, Zero One   // scene = scene * transmittance + inscatter, alpha kept

            HLSLPROGRAM
            #pragma fragment Frag

            float4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float z = MistViewZ(MistRawDepth(uv));

                // The 4 mist texels round this pixel, bilinear weights times a depth weight each.
                float2 lowPix = uv * _MistTargetSize.xy - 0.5;
                float2 base = floor(lowPix);
                float2 f = lowPix - base;
                float2 halfTexel = 0.5 * _MistTargetSize.zw;

                float4 sum = 0.0;
                float wsum = 0.0;
                float4 nearest = 0.0;
                float nearestDz = 1e30;
                [unroll] for (int j = 0; j < 2; j++)
                {
                    [unroll] for (int i = 0; i < 2; i++)
                    {
                        float2 tuv = clamp((base + float2(i, j) + 0.5) * _MistTargetSize.zw, halfTexel, 1.0 - halfTexel);
                        float4 c = SAMPLE_TEXTURE2D_LOD(_MistSource, sampler_PointClamp, tuv, 0);
                        float tz = MistViewZ(MistRawDepth(tuv));   // depth at that texel's centre
                        float dz = abs(tz - z) / max(z, 0.1);
                        float w = (i == 0 ? 1.0 - f.x : f.x) * (j == 0 ? 1.0 - f.y : f.y) * (1.0 - smoothstep(0.015, 0.08, dz));
                        sum += c * w;
                        wsum += w;
                        if (dz < nearestDz) { nearestDz = dz; nearest = c; }
                    }
                }
                // If the weights sum to almost nothing, use the texel nearest in depth.
                return wsum > 1e-4 ? sum / wsum : nearest;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
