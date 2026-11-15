Shader "Dogpatch/FlatVertexColor"
{
    // The one shared material for the whole world (Speed Dial). Vertex color is the albedo and
    // every triangle shades as a flat facet. Light is one sun (or moon) with one shadow map plus a
    // three-way ambient: a sky fill from above, a periwinkle fill on the sides and a warm bounce from
    // below, so shadows read periwinkle and never black. No specular, no reflections.
    //
    // Environment globals (set by WorldEnvironment): _DogpatchNight glows vertices whose alpha is
    // below 1 (alpha = 1 - glow), _DogpatchWet darkens and blues grey upward faces and adds flat
    // puddle patches tinted by the sky, _DogpatchSnow whitens upward faces.
    Properties
    {
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _FogAmount ("Fog Amount", Range(0, 1)) = 1
        _OffsetFactor ("Depth Offset Factor", Float) = 0
        _OffsetUnits ("Depth Offset Units", Float) = 0
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comparison", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilOp ("Stencil Pass", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    fixed4 _Tint;
    float _FogAmount;
    float4 _SkyAmbient;
    float4 _SideAmbient;
    float4 _GroundAmbient;
    float4 _SkyHorizon;
    float _DogpatchNight;
    float _DogpatchWet;
    float _DogpatchSnow;

    float3 FaceNormal(float3 worldPos)
    {
        // Face normal from screen-space derivatives: every triangle shades as one flat facet,
        // whatever normals the mesh carries. Flip it toward the camera so the sign of the
        // derivatives (which differs between graphics APIs) never matters.
        float3 n = normalize(cross(ddy(worldPos), ddx(worldPos)));
        n *= dot(n, _WorldSpaceCameraPos - worldPos) < 0 ? -1 : 1;
        return n;
    }

    float Hash(float2 p)
    {
        return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
    }

    float3 Surface(float3 albedo, float3 n, float3 worldPos)
    {
        float up = saturate((n.y - 0.85) * 6.7);
        float grey = 1 - saturate((max(albedo.r, max(albedo.g, albedo.b)) - min(albedo.r, min(albedo.g, albedo.b))) * 12);
        float wet = _DogpatchWet * up * grey;
        float2 cell = floor(worldPos.xz / 3.0);
        float puddle = step(0.72, Hash(cell)) * step(0.35, Hash(floor(worldPos.xz / 1.5) + 7.3));
        float3 damp = albedo * 0.68 + float3(0.0, 0.012, 0.04);
        float3 sheen = lerp(damp, _SkyHorizon.rgb * 0.75, 0.55);
        albedo = lerp(albedo, lerp(damp, sheen, puddle * saturate(_DogpatchWet * 1.6 - 0.4)), wet);
        float snow = _DogpatchSnow * saturate((n.y - 0.55) * 3);
        return lerp(albedo, float3(0.9, 0.93, 0.98), snow);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Offset [_OffsetFactor], [_OffsetUnits]
            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass [_StencilOp]
            }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdbase nolightmap nodynlightmap nodirlightmap novertexlight
            #pragma multi_compile_fog
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

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
                float3 n = FaceNormal(i.worldPos);
                float3 albedo = Surface(i.color.rgb, n, i.worldPos);
                float ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                float shadow = SHADOW_ATTENUATION(i);
                float3 ambient = _SideAmbient.rgb * (1 - abs(n.y)) + _SkyAmbient.rgb * saturate(n.y) + _GroundAmbient.rgb * saturate(-n.y);
                float3 lit = albedo * (ambient + _LightColor0.rgb * ndl * shadow);
                lit += i.color.rgb * saturate(1 - i.color.a) * _DogpatchNight * 1.8;
                fixed4 col = fixed4(lit, 1);
                fixed4 fogged = col;
                UNITY_APPLY_FOG(i.fogCoord, fogged);
                return lerp(col, fogged, _FogAmount);
            }
            ENDCG
        }

        // Extra pixel lights (the driven car's headlights at night), flat-shaded like the base pass.
        Pass
        {
            Tags { "LightMode" = "ForwardAdd" }
            Blend One One
            ZWrite Off
            Offset [_OffsetFactor], [_OffsetUnits]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fwdadd
            #pragma multi_compile_fog
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

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
                LIGHTING_COORDS(1, 2)
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color * _Tint;
                TRANSFER_VERTEX_TO_FRAGMENT(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = FaceNormal(i.worldPos);
                float3 toLight = normalize(_WorldSpaceLightPos0.xyz - i.worldPos * _WorldSpaceLightPos0.w);
                float3 lit = Surface(i.color.rgb, n, i.worldPos) * _LightColor0.rgb * saturate(dot(n, toLight)) * LIGHT_ATTENUATION(i);
                fixed4 col = fixed4(lit, 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0, 0, 0, 0));
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
