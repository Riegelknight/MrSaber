// ==============================================================================
//  TunnelLaserController — Tunnellaser (Port, GUID-treu; Klasse wie im Original).
// ==============================================================================
//  Dateiname/-GUID bleiben identisch zu ArcViewer (TunnelLaserHandler.cs.meta),
//  damit übernommene Prefabs (z. B. Linkin) ihre Skript-Referenz behalten.
//  Die Klasse heißt — wie im Original — TunnelLaserController.
// ==============================================================================
using UnityEngine;

namespace BeatSaber.Environment
{
    public class TunnelLaserController : RotatingLaserHandler
    {
        [Space]
        [SerializeField] private bool rotateClockwiseDefault;
        [SerializeField] private float step = 5f;

        private bool laserModeParity = false;

        public void UpdateRingRotations(RingRotationEventArgs eventArgs)
        {
            if (eventArgs.affectBigRings != eventArgs.affectSmallRings) return;
            if (eventArgs.currentEventIndex < 0 || eventArgs.currentEventIndex >= eventArgs.events.Count)
            {
                laserModeParity = false;
                return;
            }
            laserModeParity = eventArgs.events[eventArgs.currentEventIndex].Parity;
        }

        public override void UpdateLaserRotations(LaserSpeedEvent laserSpeedEvent, LightEventType type)
        {
            if (type != eventType) return;

            if (laserSpeedEvent == null)
            {
                for (int i = 0; i < targets.Count; i++)
                    SetLaserRotation(targets[i], step * i, defaultRotations[i]);
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                float angle;
                if (laserModeParity || laserSpeedEvent.RotationSpeed < Mathf.Epsilon)
                    angle = laserSpeedEvent.GetLaserRotationWithStep(EnvironmentClock.CurrentTime, rotateClockwiseDefault, step, i);
                else angle = laserSpeedEvent.GetLaserRotation(EnvironmentClock.CurrentTime, i);
                SetLaserRotation(targets[i], angle, defaultRotations[i]);
            }
        }

        protected override void Start()
        {
            EnvironmentRingManager.OnRingRotationsChanged += UpdateRingRotations;
            base.Start();
        }

        protected override void OnDestroy()
        {
            EnvironmentRingManager.OnRingRotationsChanged -= UpdateRingRotations;
            base.OnDestroy();
        }
    }
}
