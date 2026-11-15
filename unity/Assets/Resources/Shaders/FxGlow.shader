Shader "Dogpatch/FxGlow"
{
    // Additive glow for world effects (the start ring's beacon and its floor overlay): vertex colour
    // times _Tint, with _Rim brightening silhouette edges like a soft fresnel. Depth-tested, never
    // writes depth, and pulled toward the camera so floor lines drawn just above the road never
    // z-fight it. No textures, no post-processing.
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Rim ("Rim", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha One
            Offset -1, -4
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float _Rim;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 n = normalize(UnityObjectToWorldNormal(v.normal));
                float3 view = normalize(WorldSpaceViewDir(v.vertex));
                float edge = 1.0 - abs(dot(n, view));
                o.color = v.color * _Tint;
                o.color.a *= lerp(1.0, 0.3 + edge * edge * 2.2, _Rim);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
