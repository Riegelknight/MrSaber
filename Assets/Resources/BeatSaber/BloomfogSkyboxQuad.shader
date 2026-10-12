// ==============================================================================
//  BloomfogSkyboxQuad — leuchtender Hintergrund aus der Bloomfog-Textur.
// ==============================================================================
//  1:1 Port aus ArcViewer (Assets/Bloomfog/Shaders/BloomfogSkyboxQuad.shader):
//  Ein Quad, dessen Vertices direkt in NDC (−1..1, z = 0) geschrieben werden —
//  füllt exakt den Bildschirm (pro Auge, multipass-stereofest, PICO-Renderingpfad
//  = MultiPass) und zeigt die weichgeblurnte Lightshow (`_BloomfogTex`) als
//  „Himmel". Sample-UV über `_FogTextureToScreenRatio` (setzt das Bloomfog-
//  Feature je Frame aus der Capture-Geometrie).
//  Das Quad hängt am Raum-Anker (in Passthrough inaktiv) und zeichnet mit
//  Queue 990 VOR aller opaken Geometrie (ZWrite aus) — es übermalt nichts.
// ==============================================================================
Shader "BeatSaber/BloomfogSkyboxQuad"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            sampler2D _BloomfogTex;
            float2 _FogTextureToScreenRatio;

            v2f vert(appdata v)
            {
                v2f o;
                // NDC-Vollbild: Objekt-/Kamera-Transforms werden absichtlich ignoriert.
                o.vertex = float4(v.vertex.xy, 0, 1);
                o.screenPos = ComputeNonStereoScreenPos(o.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 screenSpaceUV = i.screenPos.xy / i.screenPos.w;
                float2 scaledUV = float2(
                    (screenSpaceUV.x + -0.5) * _FogTextureToScreenRatio.x + 0.5,
                    (screenSpaceUV.y + -0.5) * _FogTextureToScreenRatio.y + 0.5
                );
                return tex2D(_BloomfogTex, scaledUV);
            }
            ENDCG
        }
    }
    FallBack Off
}