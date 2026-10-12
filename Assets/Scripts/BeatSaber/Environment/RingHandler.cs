// ==============================================================================
//  RingHandler — Rotiert einen Ring-Lichtkörper (Port, GUID-treu).
// ==============================================================================
//  Dateiname/-GUID bleiben identisch zu ArcViewer (RingHandler.cs.meta), damit die
//  übernommenen Ring-Prefabs ihre Skript-Referenz behalten. `bigRing`/`id` sind
//  1:1 die serialisierten Namen und werden je Ring-Instanz im Prefab/der Szene gesetzt.
// ==============================================================================
using UnityEngine;

namespace BeatSaber.Environment
{
    public class RingHandler : MonoBehaviour
    {
        [SerializeField] private bool bigRing;
        [SerializeField] private int id;

        public void UpdateRingRotations(RingRotationEventArgs eventArgs)
        {
            if ((!bigRing && !eventArgs.affectSmallRings) || (bigRing && !eventArgs.affectBigRings))
                return;

            RingRotationEvent current = null;
            for (int i = eventArgs.currentEventIndex; i >= 0; i--)
            {
                if (eventArgs.events[i].StartInfluenceTime(id) <= EnvironmentClock.CurrentTime)
                {
                    current = eventArgs.events[i];
                    break;
                }
            }

            if (current == null)
            {
                float defaultAngle = bigRing ? EnvironmentRingManager.BigRingStartAngle : EnvironmentRingManager.SmallRingStartAngle;
                float defaultStep = bigRing ? EnvironmentRingManager.BigRingStartStep : EnvironmentRingManager.SmallRingStartStep;
                SetRotation(defaultAngle + (defaultStep * id));
                return;
            }

            SetRotation(current.GetRingAngle(EnvironmentClock.CurrentTime, id));
        }

        private void SetRotation(float angle)
        {
            Vector3 eulerAngles = transform.localEulerAngles;
            eulerAngles.z = angle % 360;
            transform.localEulerAngles = eulerAngles;
        }

        private void OnEnable()
        {
            EnvironmentRingManager.OnRingRotationsChanged += UpdateRingRotations;
        }

        private void OnDisable()
        {
            EnvironmentRingManager.OnRingRotationsChanged -= UpdateRingRotations;
        }
    }
}
