// ==============================================================================
//  PlatformShader — Strukturflächen der ArcViewer-Umgebungen.
// ==============================================================================
//  Runde 09.10: ArcViewers ORIGINAL-Optik zurückgeholt (Port aus ArcViewer-main,
//  minimiert): Basis-Helligkeit = FAKE-REFLEXION der Bloomfog-Textur (fresnel-
  //  gewichteter Sample-Versatz entlang der Sicht-Normale — „schwarzes Glas, das
//  vom Lichtshow-Glühnebel reflektiert wird"), plus Ambient, Textur, Laserlicht
//  (LightHandler) und der MrSaber-Lightshow-Tint (`_EnvironmentTint`).
//  Der Bloomfog-Nebel (BLOOM_HEIGHT_FOG_APPLY) blendet alles in den weichen
//  Glühnebel. Vorher: statische Basisfarbe ohne Bloomfog-Lesen → matt.
//  Bewusst OHNE Normal-Map-Zweig (Unity-Quad hat keine Tangenten; die TBN-
//  Rechnung des Originals bräuchte sie — Reflexion läuft über die Mesh-Normale).
//  Property-Namen bleiben identisch (gleiche Materialien).
// ==============================================================================
Shader "Custom/PlatformShader"
{
    Properties
    {
        [HDR]_LaserColor ("Laser Color", Color) = (0,0,0,0)
        _ColorMult ("Laser Color Multiplier", float) = 1
        _BaseColor ("Base Color", Color) = (0.055,0.065,0.09,1)
        _MainTex ("Main Texture", 2D) = "white" {}
        _TextureDistance ("Max Texture Distance", float) = 100.0
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalDistance ("Max Normal Map Distance", float) = 100.0
        _FogStartOffset ("Fog Start Offset", float) = 0
        _FogScale ("Fog Scale", float) = 1
        _FogHeightOffset ("Fog Height Offset", float) = 0
        _FogHeightScale ("Fog Height Scale", float) = 1
        _AmbientStrength ("Ambient Light", float) = 1
        _ReflectionStrength ("Reflection Strength", float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

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
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                BLOOM_FOG_COORDS(2, 3, 4);
                float4 vertex : SV_POSITION;
            };

            uniform float4 _BloomfogTex_TexelSize;

            sampler2D _MainTex;
            float4 _MainTex_ST;

            fixed4 _LaserColor;
            fixed4 _BaseColor;
            float _ColorMult;
            float _TextureDistance, _NormalDistance;
            float _FogStartOffset, _FogScale;
            float _FogHeightOffset, _FogHeightScale;
            float _AmbientStrength;
            float _ReflectionStrength;

            // 0 = Modus „Default" (kein Reflexionsglanz), 1 = „Animiert".
            // (`_EnvironmentTint` wird bewusst NICHT mehr konsumiert — der globale Tint
            //  färbte jede Fläche gleich; Farbe kommt nur über die Reflexion.)
            uniform float _ProbeReflStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = mul((float3x3)unity_ObjectToWorld, v.normal);

                BLOOM_FOG_INITIALIZE_VERT(o, v.vertex);
                o.fogCoord = 0; // wird im Fragment aus screenPos bestimmt (Init. gegen Shader-Warnung)
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                BLOOM_FOG_INITIALIZE_FRAG(i);

                float3 cameraOffset = _WorldSpaceCameraPos - i.worldPos;
                float cameraDistance = length(cameraOffset);

                // RAUM-lokale Entfernung (Runde 10.11): Der Textur→Reflexions-Übergang
                // wächst/schrumpft mit der Bühne (gleiche RELATIVE Optik wie bei Skalierung 1).
                // Vorher: echte Welt-Entfernung → beim Skalieren kippten ferne Flächen früher
                // zur reinen Reflexion und verfärbten sich (Geräte-Feedback „während des
                // Vorganges"). Wie der Nebel in BloomFog.cginc über _RoomScale.
                float roomScale = max(_RoomScale, 0.001);
                float cameraDistanceLocal = cameraDistance / roomScale;

                float2 bloomfogRes = _BloomfogTex_TexelSize.xy;
                float3 worldNormal = normalize(i.worldNormal);

                // Fake-Reflexion (Original): Sample-Position der Bloomfog-Textur in
                // Pixelraster-Koordinaten entlang der Sicht-Normale verschieben — der
                // Versatz skaliert mit dem Fresnel-Winkel.
                float3 viewNormal = mul((float3x3)UNITY_MATRIX_V, worldNormal);
                float3 cameraDir = cameraOffset / cameraDistance;

                float2 originalFogCoord = (i.fogCoord * 2) - 1;
                originalFogCoord.y *= bloomfogRes;

                float fresnel = dot(cameraDir, worldNormal);
                float reflectionDist = fresnel * bloomfogRes.x * 0.5;
                float2 screenReflectPos = originalFogCoord + (viewNormal.xy * reflectionDist);

                screenReflectPos.y /= bloomfogRes;
                float2 screenReflectUV = (screenReflectPos + 1) * 0.5;

                // Mindest-Anteil klein halten: Der Großteil der Fake-Reflexion kommt über den
                // Fresnel-Winkel (streifender Blick = starke Reflexion). Bei senkrechtem Blick
                // (Arena-Boden aus der Nähe) bleibt die Fläche dadurch GRAU statt einer großen
                // farbigen Fläche (Nutzer-Feedback 09.10j).
                float reflectionMult = _ReflectionStrength * (0.18f + 0.82f * (1.0f - fresnel));

                // Basis = die Reflexion des Glühnebels (Original: „strictly reflections")
                fixed4 col = BLOOM_FOG_SAMPLE(screenReflectUV) * reflectionMult;
                col.rgb += _BaseColor.rgb;

                // Ambient je Flächennormale (Sky/Equator/Ground der Szene)
                fixed4 skyCol = unity_AmbientSky * clamp(worldNormal.y, 0, 1);
                fixed4 equatorCol = unity_AmbientEquator * (1 - abs(worldNormal.y));
                fixed4 groundCol = unity_AmbientGround * abs(clamp(worldNormal.y, -1, 0));
                col += (skyCol + equatorCol + groundCol) * _AmbientStrength;

                // Albedo-Textur (nahe = getönt, fern = Reflexion pur, wie Original) —
                // in RAUM-lokaler Metrik, damit der Übergang beim Skalieren stabil bleibt.
                col = lerp(col * tex2D(_MainTex, i.uv), col, clamp(cameraDistanceLocal / _TextureDistance, 0.001, 1));

                // Laserlicht (LightHandler). Der frühere GLOBALE `_EnvironmentTint`-Zusatz ist
                // bewusst ENTFERNT: er färbte JEDE Fläche mit derselben Ringfarbe ein („eine
                // Lichtquelle färbt alles gleich") und ließ die Arena flächig blinken. Farbe
                // kommt jetzt ausschließlich über die (Fake-)Reflexion.
                col.rgb += _LaserColor.rgb * _LaserColor.a * _ColorMult;

                // ECHTE Umgebungs-Reflexion aus der Reflection-Probe (`unity_SpecCube0`, URP):
                // der spiegelnde Boden zeigt die Neon-Bühne (Beat-Saber-Look). Nur „Animiert".
                // Schärferer Mip (1 statt 2) → sichtbare Reflexionen statt Farbbrei.
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
                float3 reflDir = reflect(-viewDir, worldNormal);
                half4 probeSample = UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflDir, 1.0);
                half3 probeCol = DecodeHDR(probeSample, unity_SpecCube0_HDR);
                float probeGloss = _ProbeReflStrength * _ReflectionStrength * (0.35f + 0.65f * (1.0f - fresnel));
                col.rgb += saturate(probeCol) * probeGloss;

                // Bloomfog-Nebel (Nahbereich → weicher Glühnebel)
                BLOOM_HEIGHT_FOG_APPLY(i, col, _FogStartOffset, _FogScale, _FogHeightOffset, _FogHeightScale);

                col.a = 1;
                return col;
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}