// ==============================================================================
//  LightHandler — Licht-Emitter der ArcViewer-Umgebung (Port, GUID-treu).
// ==============================================================================
//  Dateiname/-GUID bleiben identisch zu ArcViewer (LightHandler.cs.meta), damit
//  die übernommenen Umgebungs-Prefabs ihre Skript-Referenz behalten.
//  Felder (type/id/meshRenderer/glowRenderer/…) sind 1:1 die serialisierten Namen.
//  Setzt die Lichtfarbe jeden Frame aus dem EnvironmentLightManager.
// ==============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber.Environment
{
    public class LightHandler : MonoBehaviour
    {
        public Color CurrentColor = Color.blue;
        [NonSerialized] public bool Dirty = true;

        [Header("Components")]
        [SerializeField] private MeshRenderer meshRenderer;
        [SerializeField] private MeshRenderer glowRenderer;

        [Header("Parameters")]
        [SerializeField] public LightEventType type;
        [SerializeField] public int id;
        [SerializeField] public float emissionMult = 1f;
        [SerializeField] public float glowMult = 1f;

        [Space]
        [SerializeField] public float diffuseMult = 1f;
        [SerializeField] public float diffuseBrightnessCap = 0f;
        [SerializeField] public float diffuseFalloff = 10f;
        [SerializeField] public float diffuseFalloffSteepness = 1f;

        private MaterialPropertyBlock laserProperties;
        private MaterialPropertyBlock glowProperties;

        private Color _appliedLaser;
        private Color _appliedGlow;
        private bool _applied;

        private void SetCurrentColor(Color newColor)
        {
            if (CurrentColor == newColor) return;
            CurrentColor = newColor;
            Dirty = true;
        }

        private void UpdateLight(LightingPropertyEventArgs eventArgs)
        {
            if (eventArgs.type != type) return;
            SetCurrentColor(eventArgs.eventColor);
            if (_applied && _appliedLaser == eventArgs.laserColor && _appliedGlow == eventArgs.glowColor)
                return;
            _appliedLaser = eventArgs.laserColor;
            _appliedGlow = eventArgs.glowColor;
            _applied = true;
            SetProperties(eventArgs.laserColor, eventArgs.glowColor);
        }

        private void SetProperties(Color laserColor, Color glowColor)
        {
            laserProperties.SetColor("_LaserColor", laserColor);
            glowProperties.SetColor("_LaserColor", glowColor);
            UpdateProperties();
        }

        private void UpdateProperties()
        {
            if (meshRenderer != null) meshRenderer.SetPropertyBlock(laserProperties);
            if (glowRenderer != null) glowRenderer.SetPropertyBlock(glowProperties);
        }

        private void Awake()
        {
            laserProperties = new MaterialPropertyBlock();
            glowProperties = new MaterialPropertyBlock();
            laserProperties.SetFloat("_ColorMult", emissionMult);
            glowProperties.SetFloat("_ColorMult", glowMult);
        }

        /// <summary>Alle aktiven Leuchtelemente — Quelle für die Umgebungs-Reflexion.</summary>
        public static readonly List<LightHandler> Active = new List<LightHandler>();

        private void OnEnable()
        {
            EnvironmentLightManager.OnLightPropertiesChanged += UpdateLight;
            Active.Add(this);
            Dirty = true;
            _applied = false; // nach Re-Enable erneut anwenden (Renderer-Block kann weg sein)
        }

        private void OnDisable()
        {
            EnvironmentLightManager.OnLightPropertiesChanged -= UpdateLight;
            Active.Remove(this);
        }
    }
}
