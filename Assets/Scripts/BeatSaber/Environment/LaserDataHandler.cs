// ==============================================================================
//  LaserDataHandler — GPU-Laser-/Occlusion-Buffer für die Umgebungs-Shader.
// ==============================================================================
//  Port von ArcViewers LaserDataHandler (GUID-treu, daher dieselbe Datei).
//  Sammelt jeden Frame die Licht-Emitter (LightHandler) und Occlusion-Marker
//  der geladenen Umgebung und schreibt sie in Shader-Globals:
//    _LaserColors/_LaserOrigins/_LaserDirections/_LaserFalloffInfos
//    _OcclusionColors/_OcclusionOrigins/_OcclusionDirections/_OcclusionFalloffInfos
//  Diese Arrays liest der DiffusePlatformShader für die weiche Umgebungs-
//  Beleuchtung durch die Laserstrahlen.
//
//  WICHTIG (Drehung): Es werden WELT-Positionen der Transforms übergeben.
//  Da die gesamte Umgebung (auch die Licht-Emitter) unter dem Raum-Anker
//  (_root) hängt, drehen sich Position und Richtung automatisch mit dem Raum —
//  der Shader rechnet in denselben Weltkoordinaten. Es ist also KEINE
//  zusätzliche Umrechnung nötig, solange nichts aus der Hierarchie fällt.
//
//  Die serialisierten Felder (lasers/occlusionMarkers) kommen aus den
//  ArcViewer-Szenen und binden auf unsere LightHandler/OcclusionMarker.
// ==============================================================================
using System;
using UnityEngine;

namespace BeatSaber.Environment
{
    public class LaserDataHandler : MonoBehaviour
    {
        public const int MaxLasers = 15;
        public const int MaxOcclusions = 3;

        [SerializeField] private LightHandler[] lasers;
        [SerializeField] private OcclusionMarker[] occlusionMarkers;

        private Vector4[] laserColors;
        private Vector4[] laserOrigins;
        private Vector4[] laserDirections;
        private Vector4[] laserFalloffInfos;

        private Vector4[] occlusionColors;
        private Vector4[] occlusionOrigins;
        private Vector4[] occlusionDirections;
        private Vector4[] occlusionFalloffInfos;

        private void UpdateLaserBuffer(bool forceUpdate = false)
        {
            // ArcViewer hat hier eine Einstellungs-Kopplung; wir halten es fest.
            const float brightnessMult = 1f;

            if (laserColors == null || laserColors.Length != MaxLasers)
            {
                laserColors = new Vector4[MaxLasers];
                laserOrigins = new Vector4[MaxLasers];
                laserDirections = new Vector4[MaxLasers];
                laserFalloffInfos = new Vector4[MaxLasers];

                if (lasers != null && lasers.Length > MaxLasers)
                    Debug.Log($"[BeatSaber] {lasers.Length} Licht-Emitter vorhanden — nur " +
                              $"die ersten {MaxLasers} werden in den Shader-Buffer übernommen.");
            }

            if (lasers == null) return; // keine Szenen-Bindung -> alles bleibt dunkel

            bool dirty = false;
            for (int i = 0; i < MaxLasers; i++)
            {
                if (i >= lasers.Length)
                {
                    if (laserColors[i] != Vector4.zero)
                    {
                        laserColors[i] = Vector4.zero;
                        dirty = true;
                    }
                    continue;
                }

                LightHandler lightHandler = lasers[i];
                if (lightHandler == null || !lightHandler.enabled || !lightHandler.gameObject.activeInHierarchy)
                {
                    if (laserColors[i] != Vector4.zero)
                    {
                        laserColors[i] = Vector4.zero;
                        dirty = true;
                    }
                    continue;
                }

                if (!lightHandler.Dirty && !forceUpdate) continue;

                Transform lightTransform = lightHandler.transform;
                laserColors[i] = lightHandler.CurrentColor * lightHandler.diffuseMult * brightnessMult;
                laserOrigins[i] = lightTransform.position;
                laserDirections[i] = lightTransform.up;

                laserFalloffInfos[i] = new Vector4
                {
                    x = Mathf.Abs(lightTransform.lossyScale.y) / 2f, // halbe Länge
                    y = lightHandler.diffuseBrightnessCap,
                    z = lightHandler.diffuseFalloff,
                    w = lightHandler.diffuseFalloffSteepness
                };

                lightHandler.Dirty = false;
                dirty = true;
            }

            if (dirty)
            {
                Shader.SetGlobalVectorArray("_LaserColors", laserColors);
                Shader.SetGlobalVectorArray("_LaserOrigins", laserOrigins);
                Shader.SetGlobalVectorArray("_LaserDirections", laserDirections);
                Shader.SetGlobalVectorArray("_LaserFalloffInfos", laserFalloffInfos);
            }
        }

        private void UpdateOcclusionBuffer(bool forceUpdate = false)
        {
            if (occlusionColors == null || occlusionColors.Length != MaxOcclusions)
            {
                occlusionColors = new Vector4[MaxOcclusions];
                occlusionOrigins = new Vector4[MaxOcclusions];
                occlusionDirections = new Vector4[MaxOcclusions];
                occlusionFalloffInfos = new Vector4[MaxOcclusions];

                if (occlusionMarkers != null && occlusionMarkers.Length > MaxOcclusions)
                    Debug.Log($"[BeatSaber] {occlusionMarkers.Length} Occlusion-Marker vorhanden — nur " +
                              $"die ersten {MaxOcclusions} werden übernommen.");
            }

            if (occlusionMarkers == null) return;

            bool dirty = false;
            for (int i = 0; i < MaxOcclusions; i++)
            {
                if (i >= occlusionMarkers.Length)
                {
                    if (occlusionColors[i] != Vector4.zero)
                    {
                        occlusionColors[i] = Vector4.zero;
                        dirty = true;
                    }
                    continue;
                }

                OcclusionMarker marker = occlusionMarkers[i];
                if (marker == null || !marker.enabled || !marker.gameObject.activeInHierarchy)
                {
                    if (occlusionColors[i] != Vector4.zero)
                    {
                        occlusionColors[i] = Vector4.zero;
                        dirty = true;
                    }
                    continue;
                }

                if (!marker.Dirty && !forceUpdate) continue;

                Transform occlusionTransform = marker.transform;
                occlusionColors[i] = Color.white * marker.intensity;
                occlusionOrigins[i] = occlusionTransform.position;
                occlusionDirections[i] = occlusionTransform.up;

                occlusionFalloffInfos[i] = new Vector4
                {
                    x = Mathf.Abs(occlusionTransform.lossyScale.y) / 2f,
                    y = marker.brightnessCap,
                    z = marker.falloff,
                    w = marker.falloffSteepness
                };

                marker.Dirty = false;
                dirty = true;
            }

            if (dirty)
            {
                Shader.SetGlobalVectorArray("_OcclusionColors", occlusionColors);
                Shader.SetGlobalVectorArray("_OcclusionOrigins", occlusionOrigins);
                Shader.SetGlobalVectorArray("_OcclusionDirections", occlusionDirections);
                Shader.SetGlobalVectorArray("_OcclusionFalloffInfos", occlusionFalloffInfos);
            }
        }

        private void LateUpdate()
        {
            UpdateLaserBuffer();
            UpdateOcclusionBuffer();
        }

        private void OnEnable()
        {
            UpdateLaserBuffer(true);
            UpdateOcclusionBuffer(true);
            Debug.Log($"[BeatSaber] LaserDataHandler aktiv: {lasers?.Length ?? 0} Laser, " +
                      $"{occlusionMarkers?.Length ?? 0} Occlusion-Marker gebunden.");
        }
    }
}