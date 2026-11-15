Shader "Dogpatch/Sky"
{
    // Unlit sky dome. The gradient is computed per pixel from the view direction's elevation, so it
    // is exact at any dome tessellation and on every graphics API: pale _SkyHorizon on and below the
    // horizon, a thin pale band, _SkyMid a few degrees up and deep _SkyTop by the top of the frame.
    // _SkySpan (set by SkyRig from the camera) is the sine of the elevation where the top colour is
    // reached, so a low chase camera still shows the whole deep-to-pale gradient. The three colours
    // are globals so time of day and weather can move them. Drawn first, never writes depth, never
    // fogged.
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "PreviewType" = "Skybox" }
        Pass
        {
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _SkyTop;
            float4 _SkyMid;
            float4 _SkyHorizon;
            float _SkySpan;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 world : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.world - _WorldSpaceCameraPos);
                float g = saturate(dir.y / max(_SkySpan, 0.02));
                float3 c = lerp(_SkyHorizon.rgb, _SkyMid.rgb, smoothstep(0.02, 0.38, g));
                c = lerp(c, _SkyTop.rgb, smoothstep(0.3, 1.0, g));
                return float4(c, 1);
            }
            ENDCG
        }
    }
}
