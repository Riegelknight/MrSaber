#ifndef BLOOM_FOG_CG_INCLUDED
#define BLOOM_FOG_CG_INCLUDED

uniform sampler2D _BloomfogTex;
uniform float _CustomFogOffset;
uniform float _CustomFogAttenuation;
uniform float _CustomFogHeightFogStartY;
uniform float _CustomFogHeightFogHeight;
uniform float2 _FogTextureToScreenRatio;

// VP der RAUM-verankerten Erfassungskamera (09.10o). Die Erfassung hängt fest am
// Raum-Anker — jede Nebel-/Reflexionsprobe wird dadurch bühnenfest: Raum-Drehung/-Versatz
// (Menü/Joystick) dreht alles mit, Kopf-/Körperbewegung des Spielers dreht NICHTS.
uniform float4x4 _BloomfogCaptureVP;

// Raum-Metrik des EnvironmentDirector (Eltern-Kind an `_gameRoot`): Nebel-Band,
// Nebel-Dämpfung und Erfassung folgen der RAUM-Skalierung/-Lage. Bei Skalierung 1
// und Raum-Y 0 sind diese Werte neutral (exakt ArcViewer), bei anderem Raum wächst/
// schrumpft der Nebel mit der Bühne. _RoomScale > 0, sonst wird 1 angenommen.
uniform float _RoomScale;
uniform float4 _RoomAnchorPos;

inline float2 ConvertUV(float2 uv)
{
  return float2(
    (uv.x + -0.5) * _FogTextureToScreenRatio.x + 0.5,
    (uv.y + -0.5) * _FogTextureToScreenRatio.y + 0.5
  );
}

inline float2 GetFogCoord(float3 worldPos) {
  // Weltpunkt -> UV im Sichtfeld der (raumfesten) Erfassungskamera.
  float4 clip = mul(_BloomfogCaptureVP, float4(worldPos, 1.0));
  float2 uv = clip.xy / clip.w;
  uv = uv * 0.5 + 0.5;
  #if UNITY_UV_STARTS_AT_TOP
  uv.y = 1.0 - uv.y;
  #endif
  uv = clamp(uv, 0.0, 1.0);
  return ConvertUV(uv);
}

inline float GetHeightFogIntensity(float3 worldPos, float fogHeightOffset, float fogHeightScale) {
  // Welt-Y in RAUM-lokale Höhe übersetzen: (Welt-Y − Raum-Y) / Raum-Skalierung.
  // Das Nebel-Band liegt damit relativ zur Bühne und skaliert mit ihr.
  float roomScale = max(_RoomScale, 0.001);
  float localY = (worldPos.y - _RoomAnchorPos.y) / roomScale;
  float heightFogIntensity = _CustomFogHeightFogHeight + _CustomFogHeightFogStartY;
  heightFogIntensity = ((localY * fogHeightScale) + fogHeightOffset) + -heightFogIntensity;
  heightFogIntensity = heightFogIntensity / _CustomFogHeightFogHeight;
  heightFogIntensity = clamp(heightFogIntensity, 0, 1);
  return ((-heightFogIntensity * 2) + 3) * (heightFogIntensity * heightFogIntensity);
}

inline float GetFogIntensity(float3 distance, float fogStartOffset, float fogScale) {
  // Entfernung in RAUM-lokaler Metrik: bei doppeltem Raum beginnt der Nebel doppelt
  // so weit entfernt (er wächst/schrumpft mit der Bühne).
  float roomScale = max(_RoomScale, 0.001);
  float3 localDist = distance / roomScale;
  float fogIntensity = max(dot(localDist, localDist) + -fogStartOffset, 0);
  fogIntensity = max((fogIntensity * fogScale) + -_CustomFogOffset, 0);
  fogIntensity = 1 / ((fogIntensity * _CustomFogAttenuation) + 1);
  return -fogIntensity;
}

inline void SampleBloomfog_float(float4 col, float3 worldPos, float fogStartOffset, float fogScale, out float4 outCol)
{
  float3 fogDistance = worldPos + -_WorldSpaceCameraPos;
  float4 fogCol = -float4(col.rgb, 1) + tex2D(_BloomfogTex, GetFogCoord(worldPos));
  fogCol.a = -col.a;
  outCol = col + ((GetFogIntensity(fogDistance, fogStartOffset, fogScale) + 1) * fogCol);
}

#define BLOOM_FOG_COORDS(screenPosIndex, fogCoordIndex, worldPosIndex) \
  float4 screenPos : TEXCOORD##screenPosIndex; \
  float2 fogCoord : TEXCOORD##fogCoordIndex; \
  float3 worldPos : TEXCOORD##worldPosIndex

#define BLOOM_FOG_SURFACE_INPUT \
  float4 screenPos; \
  float2 fogCoord; \
  float3 worldPos;

#define BLOOM_FOG_INITIALIZE_VERT(outputStruct, inputVertex) \
  outputStruct.screenPos = ComputeScreenPos(UnityObjectToClipPos(inputVertex)); \
  outputStruct.worldPos = mul(unity_ObjectToWorld, inputVertex)

#define BLOOM_FOG_INITIALIZE_FRAG(inoutStruct) \
  inoutStruct.fogCoord = GetFogCoord(inoutStruct.worldPos)

#define BLOOM_FOG_SAMPLE(fogCoord) \
  tex2D(_BloomfogTex, fogCoord)

#define BLOOM_FOG_APPLY(fogData, col, fogStartOffset, fogScale) \
  float3 fogDistance = fogData.worldPos + -_WorldSpaceCameraPos; \
  float4 fogCol = -float4(col.rgb, 1) + BLOOM_FOG_SAMPLE(fogData.fogCoord); \
  fogCol.a = -col.a; \
  col = col + ((GetFogIntensity(fogDistance, fogStartOffset, fogScale) + 1) * fogCol)

#define BLOOM_HEIGHT_FOG_APPLY(fogData, col, fogStartOffset, fogScale, fogHeightOffset, fogHeightScale) \
  float3 fogDistance = fogData.worldPos + -_WorldSpaceCameraPos; \
  float4 fogCol = -float4(col.rgb, 1) + BLOOM_FOG_SAMPLE(fogData.fogCoord); \
  fogCol.a = -col.a; \
  col = col + (((GetHeightFogIntensity(fogData.worldPos, fogHeightOffset, fogHeightScale) * GetFogIntensity(fogDistance, fogStartOffset, fogScale)) + 1) * fogCol)

#endif // BLOOM_FOG_CG_INCLUDED
