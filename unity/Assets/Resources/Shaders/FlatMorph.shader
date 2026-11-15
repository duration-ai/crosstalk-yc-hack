Shader "Dogpatch/FlatMorph"
{
    // The world's flat vertex-colour look with a per-object material change for Operator effects
    // (CROS-39): _Mix blends the albedo toward a colour (stone, ice, jelly, glass, ghost), _Alpha
    // makes it see-through when blended, _Rim adds a pale edge sheen. Same facets, sun, three-way
    // ambient, night glow and fog as Dogpatch/FlatVertexColor.
    Properties
    {
        _Mix ("Mix (rgb colour, a amount)", Color) = (1, 1, 1, 0)
        _Alpha ("Alpha", Range(0, 1)) = 1
        _Rim ("Rim", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodynlightmap nodirlightmap novertexlight
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            fixed4 _Mix;
            float _Alpha;
            float _Rim;
            float4 _SkyAmbient;
            float4 _SideAmbient;
            float4 _GroundAmbient;
            float _DogpatchNight;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float3 worldPos : TEXCOORD0;
                SHADOW_COORDS(1)
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color;
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(cross(ddy(i.worldPos), ddx(i.worldPos)));
                float3 toEye = normalize(_WorldSpaceCameraPos - i.worldPos);
                n *= dot(n, toEye) < 0 ? -1 : 1;
                float3 albedo = lerp(i.color.rgb, _Mix.rgb, _Mix.a);
                float ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                float shadow = SHADOW_ATTENUATION(i);
                float3 ambient = _SideAmbient.rgb * (1 - abs(n.y)) + _SkyAmbient.rgb * saturate(n.y) + _GroundAmbient.rgb * saturate(-n.y);
                float3 lit = albedo * (ambient + _LightColor0.rgb * ndl * shadow);
                lit += i.color.rgb * saturate(1 - i.color.a) * _DogpatchNight * 3.6 * (1 - _Mix.a);
                float rim = pow(1 - saturate(dot(n, toEye)), 2) * _Rim;
                lit += rim;
                fixed4 col = fixed4(lit, saturate(_Alpha + rim * 0.5));
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }

        Pass
        {
            Tags { "LightMode" = "ShadowCaster" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
}
