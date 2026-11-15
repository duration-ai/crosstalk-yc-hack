Shader "Dogpatch/CrowdVertexColor"
{
    // The flat vertex-colour look of Dogpatch/FlatVertexColor for GPU-skinned, instanced crowds.
    // Each vertex carries (bone, outfit slot, head flag, shade) in TEXCOORD1; _Bones holds a 3x4
    // skin matrix per bone per frame; each instance picks two frames to blend, a head turn and
    // its outfit colours.
    Properties
    {
        _Bones ("Bones", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        CGINCLUDE
// Upgrade NOTE: excluded shader from OpenGL ES 2.0 because it uses non-square matrices
#pragma exclude_renderers gles
        #include "UnityCG.cginc"

        sampler2D _Bones;
        float4 _BoneTexel;
        float4 _HeadPivot;
        float _CrowdHeadScale;

        UNITY_INSTANCING_BUFFER_START(Crowd)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Anim)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Top)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Under)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Bottom)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Hair)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Skin)
        UNITY_INSTANCING_BUFFER_END(Crowd)

        float3x4 BoneMatrix(float bone, float frame)
        {
            float du = _BoneTexel.x;
            float u = (bone * 3 + 0.5) * du;
            float v = (frame + 0.5) * _BoneTexel.y;
            float4 r0 = tex2Dlod(_Bones, float4(u, v, 0, 0));
            float4 r1 = tex2Dlod(_Bones, float4(u + du, v, 0, 0));
            float4 r2 = tex2Dlod(_Bones, float4(u + 2 * du, v, 0, 0));
            return float3x4(r0, r1, r2);
        }

        // anim: x = frame being faded out, y = current frame, z = fade toward y, w = head yaw (radians, + is right)
        void Skin(inout float4 vertex, inout float3 normal, float4 data)
        {
            float4 anim = UNITY_ACCESS_INSTANCED_PROP(Crowd, _Anim);
            float3 p = vertex.xyz;
            float3 n = normal;
            if (data.z > 0.5)
            {
                float s, c;
                sincos(anim.w, s, c);
                float3 d = (p - _HeadPivot.xyz) * (_CrowdHeadScale > 0 ? _CrowdHeadScale : 1);
                p = _HeadPivot.xyz + float3(d.x * c + d.z * s, d.y, -d.x * s + d.z * c);
                n = float3(n.x * c + n.z * s, n.y, -n.x * s + n.z * c);
            }
            float3x4 a = BoneMatrix(data.x, anim.x);
            float3x4 b = BoneMatrix(data.x, anim.y);
            float4 hp = float4(p, 1);
            vertex = float4(lerp(mul(a, hp), mul(b, hp), anim.z), 1);
            normal = normalize(lerp(mul((float3x3)a, n), mul((float3x3)b, n), anim.z));
        }

        float3 Outfit(float3 color, float4 data)
        {
            if (data.y < 0.5) return color;
            float4 c = data.y < 1.5 ? UNITY_ACCESS_INSTANCED_PROP(Crowd, _Top)
                : data.y < 2.5 ? UNITY_ACCESS_INSTANCED_PROP(Crowd, _Under)
                : data.y < 3.5 ? UNITY_ACCESS_INSTANCED_PROP(Crowd, _Bottom)
                : data.y < 4.5 ? UNITY_ACCESS_INSTANCED_PROP(Crowd, _Hair)
                : UNITY_ACCESS_INSTANCED_PROP(Crowd, _Skin);
            return c.a > 0.5 ? c.rgb * data.w : color;
        }
        ENDCG

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_fwdbase nolightmap nodynlightmap nodirlightmap novertexlight
            #pragma multi_compile_fog
            #include "Lighting.cginc"
            #include "AutoLight.cginc"

            float4 _SkyAmbient;
            float4 _GroundAmbient;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 color : COLOR;
                float4 texcoord1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
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
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                Skin(v.vertex, v.normal, v.texcoord1);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = float4(Outfit(v.color.rgb, v.texcoord1), 1);
                TRANSFER_SHADOW(o);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(cross(ddy(i.worldPos), ddx(i.worldPos)));
                float3 toCamera = _WorldSpaceCameraPos - i.worldPos;
                n *= dot(n, toCamera) < 0 ? -1 : 1;

                float ndl = saturate(dot(n, _WorldSpaceLightPos0.xyz));
                float shadow = SHADOW_ATTENUATION(i);
                float3 ambient = lerp(_GroundAmbient.rgb, _SkyAmbient.rgb, n.y * 0.5 + 0.5);
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
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma multi_compile_shadowcaster

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 texcoord1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                Skin(v.vertex, v.normal, v.texcoord1);
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
