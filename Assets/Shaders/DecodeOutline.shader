Shader "Unlit/DecodeOutline"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _OutlineColor ("Outline Color", Color) = (0.05,0.1,0.2,1.0)
        _OutlineWidth ("Outline Width", Float) = 0.1
        [Enum(Normal, 0, From Vertex Color, 1)] _OutlineMode ("Outline Mode", Int) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            Name "FORWARD"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, TRANSFORM_TEX(i.uv, _MainTex));
            }
            ENDCG
        }

        Pass
        {
            Name "FORWARD_OUTLINE"
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex       : POSITION;
                float4 uv0          : TEXCOORD0;
                float4 color        : COLOR;    // for outline
                float3 normalOS     : NORMAL;   // for outline
                float4 tangentOS    : TANGENT;  // for outline
                // when using other than color
                /*
                float4 uv1          : TEXCOORD1;
                float4 uv2          : TEXCOORD2;
                float4 uv3          : TEXCOORD3;
                float4 uv4          : TEXCOORD4;
                float4 uv5          : TEXCOORD5;
                float4 uv6          : TEXCOORD6;
                float4 uv7          : TEXCOORD7;
                */
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _OutlineColor;
            float _OutlineWidth;
            uint _OutlineMode;

            // object space
            float3 CalcOutlineVectorOS(float4 color, float3 normalOS, float4 tangentOS)
            {
                float3 bitangentOS = cross(normalOS, tangentOS.xyz) * tangentOS.w;
                float3 outlineVectorTS = color.rgb * 2.0 - 1.0;
                float3 outlineVector = outlineVectorTS.x * tangentOS.xyz + outlineVectorTS.y * bitangentOS + outlineVectorTS.z * normalOS;
                return outlineVector * color.a;
            }

            // world space
            float3 CalcOutlineVectorWS(float4 color, float3 normalOS, float4 tangentOS)
            {
                float3 normalWS = UnityObjectToWorldNormal(normalOS);
                float3 tangentWS = UnityObjectToWorldDir(tangentOS.xyz);
                float3 bitangentWS = cross(normalWS, tangentWS.xyz) * tangentOS.w * unity_WorldTransformParams.w;

                float3 outlineVectorTS = color.rgb * 2.0 - 1.0;
                float3 outlineVector = outlineVectorTS.x * tangentWS.xyz + outlineVectorTS.y * bitangentWS + outlineVectorTS.z * normalWS;
                return outlineVector * color.a;
            }

            v2f vert(appdata v)
            {
                v2f o;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // for outline
                if(_OutlineMode == 0)
                {
                    v.vertex.xyz += v.normalOS * _OutlineWidth * 0.01;
                }
                else if(_OutlineMode == 1)
                {
                    v.vertex.xyz += CalcOutlineVectorOS(v.color, v.normalOS, v.tangentOS) * _OutlineWidth * 0.01;
                }

                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv0.xy;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, TRANSFORM_TEX(i.uv, _MainTex)) * _OutlineColor;
            }
            ENDCG
        }
    }
}
