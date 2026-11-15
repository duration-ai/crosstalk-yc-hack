Shader "Dogpatch/Sky"
{
    // Unlit sky dome. Vertex color red is the blend from horizon to middle and green the blend from
    // middle to top; the three colors are globals so time of day and weather can move them. Drawn
    // first, never writes depth, never fogged.
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

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = float4(lerp(lerp(_SkyHorizon.rgb, _SkyMid.rgb, v.color.r), _SkyTop.rgb, v.color.g), 1);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
