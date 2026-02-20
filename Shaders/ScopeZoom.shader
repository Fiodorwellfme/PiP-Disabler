// ScopeZoom.shader — GrabPass screen magnification for non-PiP scopes
//
// How it works:
//   1. Scene renders normally (scope housing, world, etc.)
//   2. When this object (back lens quad) renders, GrabPass snapshots the screen
//   3. Fragment shader samples that snapshot with UVs pulled toward scope center
//   4. Result: zoomed world content displayed on the lens surface
//
// No feedback loop because GrabPass captures BEFORE this object draws.
// No extra camera, no extra CPU cost. Pure GPU: one framebuffer copy + one textured quad.
//
// Build into AssetBundle using the Editor/BuildAssetBundle.cs script.

Shader "ScopeHousingMeshSurgery/ScopeZoom"
{
    Properties
    {
        [Header(Zoom)]
        _Zoom ("Zoom Factor", Range(1, 16)) = 4
        _Brightness ("Brightness", Range(0.5, 2.0)) = 1.0

        [Header(Vignette)]
        _VignetteRadius ("Vignette Radius", Range(0.3, 1.0)) = 0.92
        _VignetteSoftness ("Vignette Softness", Range(0.01, 0.4)) = 0.08
        _VignetteColor ("Vignette Color (scope shadow)", Color) = (0, 0, 0, 1)

        [Header(Reticle)]
        _ReticleColor ("Reticle Color", Color) = (0, 0, 0, 0.85)
        _ReticleThickness ("Line Thickness", Range(0.0, 0.01)) = 0.0015
        _ReticleGap ("Center Gap", Range(0.0, 0.15)) = 0.03
        _ReticleLength ("Line Length (0=full)", Range(0.0, 1.0)) = 0.0
        [Toggle] _ReticleDot ("Center Dot", Float) = 1
        _ReticleDotSize ("Dot Size", Range(0.0, 0.02)) = 0.004

        [Header(Optional Reticle Texture)]
        _ReticleTex ("Reticle Texture (optional)", 2D) = "black" {}
        _ReticleTexOpacity ("Texture Opacity", Range(0, 1)) = 0
    }

    SubShader
    {
        // Render after all opaque geometry but before most transparent.
        // The scope housing (opaque) has already rendered, so GrabPass sees it.
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        // GrabPass: Unity copies the current screen to _ScopeGrabTexture.
        // Named grab pass = cached per frame (only one copy even with multiple lenses).
        GrabPass { "_ScopeGrabTexture" }

        Pass
        {
            Name "ScopeZoomPass"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            // Screen capture from GrabPass
            sampler2D _ScopeGrabTexture;
            float4 _ScopeGrabTexture_TexelSize;

            // Zoom
            float _Zoom;
            float _Brightness;

            // Vignette
            float _VignetteRadius;
            float _VignetteSoftness;
            float4 _VignetteColor;

            // Reticle (procedural)
            float4 _ReticleColor;
            float _ReticleThickness;
            float _ReticleGap;
            float _ReticleLength;
            float _ReticleDot;
            float _ReticleDotSize;

            // Reticle (texture)
            sampler2D _ReticleTex;
            float _ReticleTexOpacity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 grabPos : TEXCOORD0;  // screen-space grab UVs (needs perspective divide)
                float2 centerUV : TEXCOORD1; // scope center in screen UV (computed once, constant)
                float2 uv : TEXCOORD2;       // mesh UVs for vignette/reticle mask
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                o.uv = v.uv;

                // Compute scope center (object origin) in screen space.
                // This is the same for all vertices, so interpolation is safe.
                float4 centerClip = UnityObjectToClipPos(float4(0, 0, 0, 1));
                float4 centerGrab = ComputeGrabScreenPos(centerClip);
                // Perspective divide in vertex shader (safe because center is a constant point)
                o.centerUV = centerGrab.xy / centerGrab.w;

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                // --- ZOOM ---
                // Current pixel's screen UV
                float2 screenUV = i.grabPos.xy / i.grabPos.w;

                // Pull each pixel toward the scope center by the zoom factor
                float2 center = i.centerUV;
                float2 zoomedUV = center + (screenUV - center) / _Zoom;

                // Clamp to prevent sampling outside screen
                zoomedUV = saturate(zoomedUV);

                // Sample the screen at the zoomed position
                half4 color = tex2D(_ScopeGrabTexture, zoomedUV);
                color.rgb *= _Brightness;

                // --- VIGNETTE (scope shadow ring) ---
                float2 fromCenter = (i.uv - 0.5) * 2.0; // -1 to 1 range
                float dist = length(fromCenter);

                // Smooth circular falloff
                float vignette = smoothstep(_VignetteRadius, _VignetteRadius - _VignetteSoftness, dist);

                // Blend scope shadow color into edges
                float shadowMask = smoothstep(_VignetteRadius - _VignetteSoftness, _VignetteRadius, dist);
                color.rgb = lerp(color.rgb, _VignetteColor.rgb, shadowMask * _VignetteColor.a);

                // --- PROCEDURAL RETICLE ---
                float reticle = 0.0;
                if (_ReticleThickness > 0.0001)
                {
                    float2 rc = abs(fromCenter);
                    float halfThick = _ReticleThickness;

                    // Horizontal line
                    float hLine = step(rc.y, halfThick)   // within vertical thickness
                                * step(_ReticleGap, rc.x); // outside center gap
                    // Vertical line
                    float vLine = step(rc.x, halfThick)   // within horizontal thickness
                                * step(_ReticleGap, rc.y); // outside center gap

                    // Optional length limit (0 = full length to edge)
                    if (_ReticleLength > 0.001)
                    {
                        hLine *= step(rc.x, _ReticleLength);
                        vLine *= step(rc.y, _ReticleLength);
                    }

                    reticle = saturate(hLine + vLine);
                }

                // Center dot
                if (_ReticleDot > 0.5 && _ReticleDotSize > 0.0001)
                {
                    float dotMask = 1.0 - smoothstep(_ReticleDotSize * 0.7, _ReticleDotSize, dist);
                    reticle = saturate(reticle + dotMask);
                }

                // Apply reticle
                color.rgb = lerp(color.rgb, _ReticleColor.rgb, reticle * _ReticleColor.a);

                // --- TEXTURE RETICLE (optional overlay) ---
                if (_ReticleTexOpacity > 0.001)
                {
                    half4 reticleTex = tex2D(_ReticleTex, i.uv);
                    color.rgb = lerp(color.rgb, reticleTex.rgb, reticleTex.a * _ReticleTexOpacity);
                }

                // Final alpha = vignette mask (transparent outside scope circle)
                color.a = vignette;

                return color;
            }
            ENDCG
        }
    }
    Fallback Off
}
