// PixelQuantize: the home menu city as chunky terminal pixels (HomeFlyover blits the low-resolution camera image
// through it; the UI shows the result with point filtering). Each channel is cut to _Levels steps with a 4x4 ordered
// (Bayer) dither, so gradients turn into the dotted bands of an old display.
Shader "Hidden/Chopper/PixelQuantize"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Levels ("Colour steps per channel", Range(2, 32)) = 6
        _Dither ("Dither strength", Range(0, 1)) = 0.8
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Levels;
            float _Dither;

            static const float Bayer[16] =
            {
                0.0 / 16, 8.0 / 16, 2.0 / 16, 10.0 / 16,
                12.0 / 16, 4.0 / 16, 14.0 / 16, 6.0 / 16,
                3.0 / 16, 11.0 / 16, 1.0 / 16, 9.0 / 16,
                15.0 / 16, 7.0 / 16, 13.0 / 16, 5.0 / 16
            };

            float4 Frag(v2f_img i) : SV_Target
            {
                float3 c = saturate(tex2D(_MainTex, i.uv).rgb);
                int2 p = int2(i.uv * _MainTex_TexelSize.zw) & 3;
                float threshold = (Bayer[p.y * 4 + p.x] - 0.5) * _Dither;
                float steps = max(_Levels - 1, 1);
                c = floor(c * steps + 0.5 + threshold) / steps;
                return float4(saturate(c), 1);
            }
            ENDCG
        }
    }
}
