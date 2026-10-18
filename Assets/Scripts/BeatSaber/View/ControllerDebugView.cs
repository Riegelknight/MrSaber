using UnityEngine;

namespace BeatSaber
{
    public sealed class ControllerDebugView : MonoBehaviour
    {
        private static readonly ControllerDebugView[] Views = new ControllerDebugView[2];
        private static bool _visible;

        /// <summary>Session-Sichtbarkeit (Default AUS, nicht persistiert).</summary>
        public static bool Visible => _visible;

        private SaberSide _side;

        // Geteilte Debug-Materialien (einmal erzeugt, dann von beiden Händen genutzt).
        private static Material _matX, _matY, _matZ, _matOrigin, _matProxy;

        private static readonly Color ColorX = new Color(1.00f, 0.15f, 0.15f);
        private static readonly Color ColorY = new Color(0.25f, 1.00f, 0.25f);
        private static readonly Color ColorZ = new Color(0.30f, 0.55f, 1.00f);
        private static readonly Color ColorOrigin = new Color(1.00f, 0.85f, 0.10f);
        private static readonly Color ColorProxy = new Color(0.85f, 0.85f, 0.90f);

        /// <summary>
        /// Hängt die Debug-Ansicht an einen Controller-Anker (idempotent je Seite).
        /// Der Aufruf passiert NACH dem Ausblenden der Vorlagen-Modelle; die
        /// Marker-Komponente schützt die Debug-Objekte zusätzlich vor dem
        /// wiederholten Ausblenden (HideControllerModelsRoutine).
        /// </summary>
        public static ControllerDebugView Attach(SaberSide side, Transform anchor)
        {
            if (anchor == null) return null;
            var existing = Views[(int)side];
            if (existing != null) return existing;

            var go = new GameObject("ControllerDebug");
            go.transform.SetParent(anchor, false);
            var view = go.AddComponent<ControllerDebugView>();
            view._side = side;
            view.Build();
            Views[(int)side] = view;
            go.SetActive(_visible);
            return view;
        }

        /// <summary>Schaltet beide Debug-Controller ein/aus (Session, nicht persistent).</summary>
        public static void SetVisible(bool visible)
        {
            _visible = visible;
            for (int i = 0; i < Views.Length; i++)
                if (Views[i] != null) Views[i].gameObject.SetActive(visible);
        }

        public static void Toggle() => SetVisible(!_visible);

        // ---------- Aufbau ----------

        private void Build()
        {
            BuildOriginGizmo();
            BuildModel();
        }

        /// <summary>
        /// Ursprungs-Kugel + Achsenkreuz am ROHEN Tracking-Nullpunkt. Bleibt bei
        /// jeder späteren Kalibrierung an Ort und Stelle — nur so sieht man, wohin
        /// die Pose zeigt und wie weit das Modell/Schwert davon abweicht.
        /// </summary>
        private void BuildOriginGizmo()
        {
            var origin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            origin.name = "Origin";
            Destroy(origin.GetComponent<Collider>());
            origin.transform.SetParent(transform, false);
            origin.transform.localScale = Vector3.one * 0.013f;
            SetMaterial(origin, OriginMat());

            AddAxis("AxisX", Vector3.right, XMat());
            AddAxis("AxisY", Vector3.up, YMat());
            AddAxis("AxisZ", Vector3.forward, ZMat());
        }

        private void AddAxis(string axisName, Vector3 direction, Material mat)
        {
            const float length = 0.10f;
            var axis = GameObject.CreatePrimitive(PrimitiveType.Cylinder); // Zylinder liegt entlang Y
            axis.name = axisName;
            Destroy(axis.GetComponent<Collider>());
            axis.transform.SetParent(transform, false);
            // Standard-Zylinder: Höhe 2, Radius 0,5 -> auf Länge/Breite skalieren.
            axis.transform.localScale = new Vector3(0.0035f, length * 0.5f, 0.0035f);
            axis.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
            axis.transform.localPosition = direction * (length * 0.5f); // Ursprung = Achsenanfang
            SetMaterial(axis, mat);
        }

        private void BuildModel()
        {
            var modelRoot = new GameObject("Model");
            modelRoot.transform.SetParent(transform, false);

            GameObject prefab = null;
            string source = null;
            if (_side == SaberSide.Left)
            {
                prefab = Resources.Load<GameObject>("Prefabs/LeftControllerModel");
                source = "Prefabs/LeftControllerModel";
            }
            else
            {
                // Dateiname im SDK enthält ein Leerzeichen vor der Endung!
                prefab = Resources.Load<GameObject>("Prefabs/RightControllerModel ");
                source = "Prefabs/RightControllerModel ";
                if (prefab == null)
                {
                    prefab = Resources.Load<GameObject>("Prefabs/RightControllerModel");
                    source = "Prefabs/RightControllerModel";
                }
            }

            if (prefab == null)
            {
                string direct = _side == SaberSide.Left ? "Controller/PICO 4 L" : "Controller/PICO 4 R";
                prefab = Resources.Load<GameObject>(direct);
                source = direct;
            }

            if (prefab != null)
            {
                var inst = Instantiate(prefab, modelRoot.transform, false);
                inst.name = "ControllerModel";
                // Sicherheit: keinerlei Collider aus dem Modell in die Szene lassen.
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Destroy(c);
                Debug.Log($"[BeatSaber] Controller-Debug {_side}: SDK-Modell geladen ({source}).");
                return;
            }

            Debug.LogWarning("[BeatSaber] Controller-Debug: SDK-Controller-Modell nicht gefunden " +
                             "(Package-Resources nicht ladbar?) — benutze Proxy. " +
                             "Erwartet: Packages/com.unity.xr.picoxr/Assets/Resources/Prefabs/…");
            BuildProxy(modelRoot.transform);
        }

        /// <summary>
        /// Minimale Ersatz-Repräsentation (nur falls die SDK-Assets fehlen): Griff-
        /// Zylinder + Kopfblock, grau — zusammen mit Achsenkreuz und Schwert reicht
        /// das zur Positions-/Drehungsprüfung.
        /// </summary>
        private static void BuildProxy(Transform parent)
        {
            var mat = ProxyMat();

            var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "Grip";
            Destroy(grip.GetComponent<Collider>());
            grip.transform.SetParent(parent, false);
            grip.transform.localScale = new Vector3(0.045f, 0.07f, 0.045f); // 14 cm hoch
            SetMaterial(grip, mat);

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            Destroy(head.GetComponent<Collider>());
            head.transform.SetParent(parent, false);
            head.transform.localPosition = new Vector3(0f, 0.09f, -0.03f);
            head.transform.localScale = new Vector3(0.06f, 0.035f, 0.09f);
            SetMaterial(head, mat);
        }

        // ---------- Material-Helfer (unlit, keine Shader-Abhängigkeit der Szene) ----------

        private static void SetMaterial(GameObject go, Material mat)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null) r.sharedMaterial = mat;
        }

        private static Material MakeUnlit(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        private static Material XMat()
        {
            if (_matX == null) _matX = MakeUnlit(ColorX);
            return _matX;
        }

        private static Material YMat()
        {
            if (_matY == null) _matY = MakeUnlit(ColorY);
            return _matY;
        }

        private static Material ZMat()
        {
            if (_matZ == null) _matZ = MakeUnlit(ColorZ);
            return _matZ;
        }

        private static Material OriginMat()
        {
            if (_matOrigin == null) _matOrigin = MakeUnlit(ColorOrigin);
            return _matOrigin;
        }

        private static Material ProxyMat()
        {
            if (_matProxy == null) _matProxy = MakeUnlit(ColorProxy);
            return _matProxy;
        }
    }
}
