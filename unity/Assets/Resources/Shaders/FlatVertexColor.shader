Shader "Dogpatch/FlatVertexColor"
{
    // The one shared material for the whole world (Speed Dial). Vertex color is the albedo and
    // every triangle shades as a flat facet. Light is one warm sun with one shadow map plus a
    // three-way ambient: a sky-blue fill from above, a periwinkle fill on the sides and a warm
    // bounce from below, so shadows read periwinkle and never black. No specular, no reflections.
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodynlightmap nodirlightmap novertexlight
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            fixed4 _Tint;
            float4 _SkyAmbient;
            float4 _SideAmbient;
            float4 _GroundAmbient;

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
                o.color = v.color * _Tint;
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Face normal from screen-space derivatives: every triangle shades as one flat facet,
                // whatever normals the mesh carries. Flip it toward the camera so the sign of the
                // derivatives (which differs between graphics APIs) never matters.
                float3 n = normalize(cross(ddy(i.worldPos), ddx(i.worldPos)));
                float3 toCamera = _WorldSpaceCameraPos - i.worldPos;
                n *= dot(n, toCamera) < 0 ? -1 : 1;

                float ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                float shadow = SHADOW_ATTENUATION(i);
                float3 ambient = _SideAmbient.rgb * (1 - abs(n.y)) + _SkyAmbient.rgb * saturate(n.y) + _GroundAmbient.rgb * saturate(-n.y);
                float3 lit = i.color.rgb * (ambient + _LightColor0.rgb * ndl * shadow);
                fixed4 col = fixed4(lit, 1);
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
