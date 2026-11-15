Shader "Dogpatch/Precipitation"
{
    // Rain streaks or snow flakes in a box that wraps around the camera, animated entirely in the
    // vertex shader: one static mesh, one draw call, no CPU work per frame. Each particle's anchor
    // (0..1 in the box) is its position, uv.xy its corner, uv.z its id for thinning by _Amount.
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 0.5)
        _Box ("Box size", Float) = 30
        _Fall ("Fall speed", Float) = 14
        _Size ("Width, length", Vector) = (0.012, 0.7, 0, 0)
        _Snow ("Snow", Float) = 0
        _Amount ("Amount", Float) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Box;
            float _Fall;
            float4 _Size;
            float _Snow;
            float _Amount;
            float4 _DogpatchWind;

            struct appdata { float4 vertex : POSITION; float4 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                float3 cam = _WorldSpaceCameraPos;
                float3 velocity = float3(_DogpatchWind.x, -_Fall, _DogpatchWind.z);
                float wobble = _Snow * sin(_Time.y * 1.7 + v.uv.z * 40.0) * 0.6;
                float3 travel = velocity * _Time.y + float3(wobble, 0, wobble * 0.7);
                float3 p = cam + (frac((v.vertex.xyz * _Box - cam + travel) / _Box) - 0.5) * _Box;
                float3 view = normalize(cam - p);
                float3 along = _Snow > 0.5 ? normalize(cross(view, float3(1, 0, 0))) : normalize(velocity);
                float3 side = normalize(cross(along, view));
                float3 corner = p + side * v.uv.x * _Size.x + along * v.uv.y * _Size.y;
                float keep = step(v.uv.z, _Amount);
                o.pos = UnityWorldToClipPos(corner) * keep;
                float fade = saturate(1 - length(p - cam) / (_Box * 0.5)) * saturate((length(p - cam) - 1.5) / 3.0);
                o.color = float4(_Color.rgb, _Color.a * fade);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
