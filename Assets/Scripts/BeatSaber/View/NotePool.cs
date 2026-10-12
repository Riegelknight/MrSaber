using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber
{
    /// <summary>
    /// Einfacher Objektpool für NoteViews (ersetzt den Zenject-MemoryPool der Referenz).
    /// </summary>
    public static class NotePool
    {
        private static Transform _root;
        private static readonly Stack<NoteView> _free = new();

        public static void SetRoot(Transform root) => _root = root;

        public static NoteView Get(NoteModel note, Vector3 position,
            float hitTime, float njs, float reactionTime, float halfJumpDistance,
            float startY, float flipStartX, float flipYHeight)
        {
            NoteView view = _free.Count > 0 ? _free.Pop() : CreateNew();
            view.Spawn(note, position, hitTime, njs, reactionTime, halfJumpDistance,
                startY, flipStartX, flipYHeight);
            return view;
        }

        public static void Return(NoteView view)
        {
            if (view == null) return;
            view.gameObject.SetActive(false);
            _free.Push(view);
        }

        /// <summary>Alle aktiven Noten zurueckholen (Songwechsel): alte Noten wuerden sonst ins neue Lied fliegen.</summary>
        public static void ReturnAll()
        {
            if (_root == null) return;
            var active = _root.GetComponentsInChildren<NoteView>(); // nur aktive (gepoolte sind inaktiv)
            for (int i = 0; i < active.Length; i++) Return(active[i]);
        }

        private static NoteView CreateNew()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Note";
            Object.Destroy(go.GetComponent<Collider>());
            go.layer = GameConfig.NoteLayer;

            var mf = go.GetComponent<MeshFilter>();
            mf.sharedMesh = NoteView.CachedBodyMesh;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // Der Pfeil ist in das Noten-Mesh eingebacken (siehe NoteView.BakeArrowMesh) —
            // kein separates Pfeil-Objekt mehr. Bei gerichteten Noten setzt Spawn das
            // "Arrowed"-Mesh samt Material-Array (0=Körper, 1=Pfeil).
            var noteView = go.AddComponent<NoteView>();

            // Dot-Marker (nur bei Richtung 8/9): weißer Kreis, mittig auf der Vorderfläche,
            // Durchmesser = 1/4 der Box. Kleiner Tiefen-Versatz verhindert Z-Fighting mit
            // der Würfelfläche. Das Mesh hat Radius 0,5 (Durchmesser 1) -> localScale = Durchmesser.
            var dotGo = new GameObject("Dot");
            var dotMr = dotGo.AddComponent<MeshRenderer>();
            var dotMf = dotGo.AddComponent<MeshFilter>();
            dotMf.sharedMesh = NoteView.CachedDotMesh;
            dotMr.sharedMaterial = NoteView.CachedDotMaterial;
            dotMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dotGo.transform.SetParent(go.transform, false);
            dotGo.transform.localPosition = new Vector3(0f, 0f, -GameConfig.NoteSize * 0.5f - 0.002f);
            dotGo.transform.localScale = Vector3.one * (GameConfig.NoteSize * 0.35f);
            noteView.AttachDot(dotGo.transform);

            // false = lokale Skalierung (1) beibehalten: Der Raum-Root (_gameRoot) skaliert
            // dann Größe UND Abstände der Noten einheitlich. Mit 'true' würde Unity die
            // Skalierung kompensieren (Note bleibt gleich groß, Abstände skalieren trotzdem)
            // — genau dieses falsche Verhältnis war der Fehler.
            if (_root != null) go.transform.SetParent(_root, false);
            return noteView;
        }
    }
}
