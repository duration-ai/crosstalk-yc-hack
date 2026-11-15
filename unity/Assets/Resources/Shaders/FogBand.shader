Shader "Dogpatch/FogBand"
{
    // The San Francisco fog bank lying on the bay: an unlit, alpha-blended band whose colour is the
    // live horizon colour lifted toward white, so it melts into the sky above it and changes with
    // time of day and weather. Vertex alpha carries the shape: dense on the water, a soft top edge
    // and soft ends. Depth-tested so the city hides it, never writes depth, never fogged.
    Properties
    {
        _Density ("Density", Range(0, 1)) = 0.8
        _Whiteness ("Whiteness", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+100" "RenderType" = "Transparent" }
        Pass
        {
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _SkyHorizon;
            float _Density;
            float _Whiteness;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float alpha : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.alpha = v.color.a;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 c = lerp(_SkyHorizon.rgb, float3(1, 1, 1), _Whiteness);
                return float4(c, saturate(i.alpha * _Density));
            }
            ENDCG
        }
    }
}
