// ==============================================================================
//  RingZoomHandler — Verschiebt Ringe im Zoom-Event (Port, GUID-treu).
// ==============================================================================
//  Dateiname/-GUID bleiben identisch zu ArcViewer (RingZoomHandler.cs.meta), damit
//  übernommene Prefabs ihre Skript-Referenz behalten. `firstRingPos` ist 1:1 der
//  serialisierte Name.
// ==============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber.Environment
{
    public class RingZoomHandler : MonoBehaviour
    {
        [SerializeField] private float firstRingPos;

        private readonly List<Transform> rings = new List<Transform>();

        public void UpdateRingZoom(float step)
        {
            for (int i = 0; i < rings.Count; i++)
            {
                Transform ring = rings[i];
                float ringPosition = firstRingPos + (step * i);
                ring.localPosition = GetTargetPosition(ring, ringPosition);
            }
        }

        private Vector3 GetTargetPosition(Transform target, float zPos)
        {
            Vector3 position = target.localPosition;
            position.z = zPos;
            return position;
        }

        private void OnEnable()
        {
            EnvironmentRingManager.OnRingZoomPositionChanged += UpdateRingZoom;

            if (rings.Count == 0)
            {
                foreach (Transform child in transform) rings.Add(child);
            }
        }

        private void OnDisable()
        {
            EnvironmentRingManager.OnRingZoomPositionChanged -= UpdateRingZoom;
        }
    }
}
