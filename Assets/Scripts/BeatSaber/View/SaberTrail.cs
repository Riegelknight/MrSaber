using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber
{
    /// <summary>Prozedurales Klingen-Trail (Mesh-Ribbon) im ArcViewer-Stil.</summary>
    [DefaultExecutionOrder(0)]
    public sealed class SaberTrail : MonoBehaviour
    {
        /// <summary>Klingen-Basis (Griffseite) — eine Bandkante.</summary>
        public Transform BasePoint;
        /// <summary>Klingenspitze — andere Bandkante.</summary>
        public Transform TipPoint;
        /// <summary>Lebensdauer des Bandes in Sekunden (ArcViewer `sabertraillength` = 0,1).
        /// Kurz halten, sonst wird das Band bei schnellen Schwüngen LÄNGER als die Klinge.</summary>
        public float TrailTime = 0.1f;
        /// <summary>Maximale Samples (ArcViewer `sabertrailsegments`, 10..100).</summary>
        public int MaxSamples = 48;
        /// <summary>Mindestweg der Spitze zwischen zwei Samples (Meter).</summary>
        public float MinVertexDistance = 0.012f;

        private struct Sample
        {
            public Vector3 Base;
            public Vector3 Tip;
            public float Time;
        }

        private readonly List<Sample> _samples = new(64);
        private Mesh _mesh;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private bool _visible = true;
        private bool _hasLast;
        private Vector3 _lastTip;
        private readonly List<Vector3> _verts = new(128);
        private readonly List<Vector2> _uvs = new(128);
        private readonly List<Color> _colors = new(128);
        private readonly List<int> _tris = new(384);

        public static SaberTrail Attach(GameObject host, Transform basePoint, Transform tipPoint, Material material)
        {
            var trail = host.AddComponent<SaberTrail>();
            trail.BasePoint = basePoint;
            trail.TipPoint = tipPoint;
            trail._mesh = new Mesh { name = "SaberTrailMesh" };
            trail._filter = host.AddComponent<MeshFilter>();
            trail._filter.sharedMesh = trail._mesh;
            trail._renderer = host.AddComponent<MeshRenderer>();
            trail._renderer.sharedMaterial = material;
            trail._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail._renderer.receiveShadows = false;
            return trail;
        }

        /// <summary>Band leeren (Menü/Pause/Einfahren) — verhindert in der Luft hängende Spuren.</summary>
        public void Clear()
        {
            _samples.Clear();
            _hasLast = false;
            if (_mesh != null) _mesh.Clear();
        }

        /// <summary>Sichtbarkeit schalten (Einfahren/Menü).</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_renderer != null) _renderer.enabled = visible;
            if (!visible) Clear();
        }

        private void LateUpdate()
        {
            if (!_visible || BasePoint == null || TipPoint == null || _mesh == null) return;

            float now = Time.unscaledTime;
            Vector3 tip = TipPoint.position;
            Vector3 basePos = BasePoint.position;

            if (!_hasLast || (tip - _lastTip).sqrMagnitude >= MinVertexDistance * MinVertexDistance)
            {
                _samples.Add(new Sample { Base = basePos, Tip = tip, Time = now });
                _lastTip = tip;
                _hasLast = true;
                if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
            }

            // Alte Samples verwerfen.
            while (_samples.Count > 0 && now - _samples[0].Time > TrailTime) _samples.RemoveAt(0);

            BuildMesh();
        }

        private void BuildMesh()
        {
            int n = _samples.Count;
            if (n < 2) { _mesh.Clear(); return; }

            _verts.Clear(); _uvs.Clear(); _colors.Clear(); _tris.Clear();
            // WICHTIG (ArcViewer): u = 0 am NEUESTEN Sample, u = 1 am ältesten. Die
            // Trail-Texture blendet entlang u ab — sonst wäre der alte Teil am hellsten
            // („Schweif je weiter weg desto deutlicher").
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / (n - 1);   // 0 = ältestes, 1 = neuestes
                float u = 1f - t;
                var s = _samples[i];
                // WICHTIG: Mesh-Vertices sind LOKAL zum Trail-Objekt (das unter dem
                // skalierten/gedrehten Schwert hängt). Weltpositionen müssen umgerechnet
                // werden — sonst ist das Band verschoben/verzerrt (ArcViewer:
                // `transform.InverseTransformPoint`).
                _verts.Add(transform.InverseTransformPoint(s.Base));
                _verts.Add(transform.InverseTransformPoint(s.Tip));
                _uvs.Add(new Vector2(u, 0f));
                _uvs.Add(new Vector2(u, 1f));
                // Alpha-Fade über die Länge (zusätzlich zur Texture), neuestes = voll.
                float a = Mathf.Clamp01(t * 1.2f);
                _colors.Add(new Color(1f, 1f, 1f, a));
                _colors.Add(new Color(1f, 1f, 1f, a));
            }
            for (int i = 0; i < n - 1; i++)
            {
                int b = i * 2;
                _tris.Add(b); _tris.Add(b + 1); _tris.Add(b + 2);
                _tris.Add(b + 1); _tris.Add(b + 3); _tris.Add(b + 2);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetColors(_colors);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();
        }
    }
}
