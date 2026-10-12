// ==============================================================================
//  LaserShader — Emissiver Laserstrahl der ArcViewer-Umgebung.
// ==============================================================================
//  Runde 09.10: Auf den ArcViewer-ORIGINALstand zurückgesetzt (1:1 Port aus
//  ArcViewer-main `Assets/Shaders/Environment/LaserShader.shader`): Der Strahl
//  liest jetzt die Bloomfog-Textur (`_BloomfogTex`, BloomFog.cginc) und blendet
//  damit Nahbereich in den weichen Leuchtnebel — exakt der ArcViewer-Look.
//  Vorher stand hier eine „angepasste" Variante OHNE Fog-Anwendung (die Textur
//  wurde nie gelesen → alles wirkte matt und der Capture-Aufwand war umsonst).
//  Property-Namen `_LaserColor`/`_ColorMult` identisch (LightHandler setzt sie
//  per MaterialPropertyBlock); die Fog-Properties tragen ArcViewers Material-
//  Werte als Defaults (Laser.mat dort: _FogScale 0,5 — MrSabers Material hat
//  das Property nicht, der Shader-Default übernimmt es).
// ==============================================================================
Shader "Custom/LaserShader"
{
    Properties
    {
        [HDR]_LaserColor ("Color", Color) = (1,1,1,1)
        _ColorMult ("Color Multiplier", float) = 1
        _FogStartOffset ("Fog Start Offset", float) = 0
        _FogScale ("Fog Scale", float) = 0.5
        _FogHeightOffset ("Fog Height Offset", float) = 0
        _FogHeightScale ("Fog Height Scale", float) = 1
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType"="Transparent"
        }

        LOD 100
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "BloomFog.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                BLOOM_FOG_COORDS(0, 1, 2);
                float4 vertex : SV_POSITION;
            };

            fixed4 _LaserColor;
            float _ColorMult;
            float _FogStartOffset, _FogScale;
            float _FogHeightOffset, _FogHeightScale;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                BLOOM_FOG_INITIALIZE_VERT(o, v.vertex);
                o.fogCoord = 0; // wird im Fragment aus screenPos bestimmt (Init. gegen Shader-Warnung)
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                BLOOM_FOG_INITIALIZE_FRAG(i);

                fixed4 col = _LaserColor * _ColorMult;
                BLOOM_HEIGHT_FOG_APPLY(i, col, _FogStartOffset, _FogScale, _FogHeightOffset, _FogHeightScale);

                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}