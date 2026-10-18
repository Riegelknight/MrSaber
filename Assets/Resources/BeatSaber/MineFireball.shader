// ==============================================================================
//  MineFireball — additiver, unlit VOLUMEN-Shader der Minen-Explosion (Runde 10.11h).
// ==============================================================================
//  Rendert die prozedurale Kugel (Fx.MineSphereMesh) als echtes 3D-Gas-Feuer —
//  KEIN Billboard. Prozedurales 3D-Value-Noise (fbm, 2 Oktaven, rechenarm) im
//  OBJEKTRAUM, das über die Zeit „strömt"; heiße weiße Mitte → gelb → orange → rot
//  (warm), weicher Auslauf am Rand (kein harter Kreis). _Fade blendet global aus
//  (und kühlt die Farbe leicht nach rot), _Seed verschiebt das Noise je Explosion.
//  Blend SrcAlpha One, ZWrite Off, Cull Off (Vorder- UND Rückseite addieren →
//  volumig). Kein Lighting, kein Schatten. Passt in den Bloom/Bloomfog-Look.
// ==============================================================================
Shader "BeatSaber/MineFireball"
{
    Properties
    {
        [HDR]_Color ("Color", Color) = (1,1,1,1)
        _Fade ("Fade", Range(0,1)) = 1
        _Seed ("Seed", Float) = 0
        _Flow ("Flow Speed", Float) = 0.8
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 obj : TEXCOORD0; };

            fixed4 _Color;
            float _Fade;
            float _Seed;
            float _Flow;

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise13(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash13(i + float3(0.0, 0.0, 0.0));
                float n100 = Hash13(i + float3(1.0, 0.0, 0.0));
                float n010 = Hash13(i + float3(0.0, 1.0, 0.0));
                float n110 = Hash13(i + float3(1.0, 1.0, 0.0));
                float n001 = Hash13(i + float3(0.0, 0.0, 1.0));
                float n101 = Hash13(i + float3(1.0, 0.0, 1.0));
                float n011 = Hash13(i + float3(0.0, 1.0, 1.0));
                float n111 = Hash13(i + float3(1.0, 1.0, 1.0));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }

            float Fbm13(float3 p)
            {
                return Noise13(p) * 0.65 + Noise13(p * 2.07 + 13.1) * 0.35;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.obj = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Wir rendern NUR die Kugel-HÜLLE (Radius 0,5). Die Dichte kommt aus der
                // Blickrichtung: in der Silhouetten-Mitte blickt man durch den dicken
                // Kern (ball→1), am Rand streift der Blick die Hülle (ball→0) → rundes
                // Volumen. KEIN massiver Ball: das Noise reißt die Fläche mit Lücken auf
                // (kontrastreich, dichte Stellen glühen, dazwischen scheint es durch).
                float3 op = i.obj;                            // Punkt auf der Hülle
                float3 nrm = normalize(op);                   // Kugelnormale (objektraum)
                float3 camObj = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float3 vd = normalize(camObj - op);           // Blickrichtung (objektraum)
                float facing = saturate(dot(nrm, vd));
                float ball = pow(facing, 1.35);               // Volumen-Silhouette
                float core = pow(facing, 3.2);                // kleiner, sehr heißer Kern

                float3 p = op * 2.0;                          // Kugelradius 0,5 → p in [-1,1]
                float t = _Time.y * _Flow;

                // Zwei Noise-Skalen: große Flammen wabern, kleine reißen den Rand scharf auf.
                float nBig = Fbm13(p * 2.0 + float3(_Seed, -t, _Seed * 0.7));
                float nSm = Fbm13(p * 6.5 - float3(t * 0.9, _Seed * 0.3, t * 0.5));
                float n = nBig * 0.68 + nSm * 0.32;

                // Kontrastreich + mit Lücken → klare Flammenzungen statt weichem Brei.
                float flame = saturate((ball * (0.55 + 1.25 * n) - 0.30) * 2.6);

                // Helligkeit: heißer Kern + Flammen.
                float heat = saturate(core * 1.1 + flame * 0.75);

                // Alpha BEWUSST < 1 (teilweise durchsichtig); Kern trägt am meisten.
                float a = flame * (0.30 + 0.8 * core) * _Fade;

                // Kühlt beim Ausfaden leicht nach rot.
                float h = saturate(heat) * lerp(0.6, 1.0, _Fade);

                float3 cRed = float3(0.80, 0.13, 0.02);
                float3 cOrange = float3(1.0, 0.45, 0.10);
                float3 cYellow = float3(1.0, 0.84, 0.38);
                float3 cWhite = float3(1.0, 0.98, 0.90);
                float3 warm;
                if (h < 0.35) warm = lerp(cRed, cOrange, h / 0.35);
                else if (h < 0.70) warm = lerp(cOrange, cYellow, (h - 0.35) / 0.35);
                else warm = lerp(cYellow, cWhite, (h - 0.70) / 0.30);

                fixed4 o;
                o.rgb = _Color.rgb * warm;
                o.a = _Color.a * a;
                return o;
            }
            ENDCG
        }
    }
    FallBack Off
}
