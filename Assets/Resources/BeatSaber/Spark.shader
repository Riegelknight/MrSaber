// ==============================================================================
//  Spark — additiver Unlit-Shader für die Funken-/Explosions-Partikel (Fx).
// ==============================================================================
//  Prozedural gebaute ParticleSystems (Notenschnitt-Funken, Minen-Explosion)
//  brauchen ein dünnes, additiv glühendes Material mit HDR-Farbe (_Color wird
//  je Instanz per MaterialPropertyBlock gesetzt — Rot/Blau/Weißblitz). Die
//  Partikel-Vcolor (Color-over-Lifetime-Fade) steuert die Transparenz.
//  Kein Lighting, kein ZWrite: Blend SrcAlpha One, Queue Transparent (wie die
//  ArcViewer-Laser, damit sie in den Bloom/Bloomfog-Look passen).
//  Vorbild: Resources/BeatSaber/Shockwave.shader (gleicher Aufbau).
// ==============================================================================
Shader "BeatSaber/Spark"
{
    Properties
    {
        [HDR]_Color ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;   // Partikel-Fade (Color-over-Lifetime)
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = _Color;
                c.a *= i.color.a;       // Fade-Out über die Lebenszeit
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}