using UnityEngine;

namespace BeatSaber
{
    /// <summary>
    /// Eine Wand (_obstacle): bewegt sich wie eine Note zum Spieler, blockiert per
    /// Headset-Kollision (Combo-Reset). Art 0 = volle Wand, Art 1 = Kauer-Wand.
    /// Optik wie im Referenz-Viewer ArcViewer: halbtransparenter Block mit leuchtenden
    /// Kanten (Körper- und Kanten-Shader von dort, keine Texturen).
    /// </summary>
    [DefaultExecutionOrder(-100)] // Positions-Update VOR GameManager.Update (Kopf-Test)
    public sealed class WallView : MonoBehaviour
    {
        public ObstacleModel Obs { get; private set; }
        public bool Consumed { get; private set; }
        public bool HeadAlreadyHit { get; set; }

        private bool _moving;
        private float _length;           // Länge in Bewegungsrichtung (Basis-Einheiten)
        private float _njs;              // Map-Tempo (Basis-Einheiten/s)
        private float _hitTime;          // Start-Beat der Wand in Lied-Sekunden
        private float _rawBottom;        // Unterkante der Wand (Roh-Map-Wert; 0 = auf dem Boden)
        private float _rawHeight;        // Höhe der Wand (Roh-Map-Wert)
        private bool _isFloorWall;       // true = Unterkante 0 (bodenständig, folgt der Feldhöhe NICHT)
        private Transform _edges;        // Kantenhülle (Gegenskalierung der Höhe)
        private bool _geomApplied;       // Geometrie schon gesetzt? (spart Per-Frame-Writes)
        private float _geomFieldHeight;  // zuletzt angewandte „Feldhöhe"

        /// <summary>
        /// Baut die Wand visuell + physikalisch auf. <paramref name="width"/> und
        /// <paramref name="height"/> sind Basis-Einheiten (Raster), <paramref name="length"/>
        /// die gestreckte Wandlänge in Bewegungsrichtung (Dauer × NJS). <paramref name="rawBottom"/>
        /// ist die Unterkante aus der Map (0 = auf dem Spielboden). Die „Feldhöhe" verschiebt alles
        /// nach oben; bodenständige Wände werden bis zum Boden verlängert.
        /// </summary>
        public static WallView Create(
            ObstacleModel obs, Vector3 position, Transform parent,
            float width, float height, float length,
            float hitTime, float njs, float rawBottom)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Wall";
            go.layer = GameConfig.WallLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position; // Y korrigiert gleich ApplyVerticalGeometry

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var cubeMesh = go.GetComponent<MeshFilter>().sharedMesh;

            // Wandgröße in Basis-Einheiten: Breite/Höhe aus dem Raster, Länge = Dauer × NJS
            // (ArcViewer WallManager: worldScale = (Width, Height, wallLength)).
            var fullScale = new Vector3(
                Mathf.Max(GameConfig.MinWallSize, width),
                Mathf.Max(GameConfig.MinWallSize, height),
                Mathf.Max(GameConfig.MinWallSize, length));
            // Der KÖRPER ist minimal kleiner als die Kantenhülle (ArcViewer WallHandler):
            // ohne diesen Offset würde es Z-Fighting geben, wenn die Kanten zweier
            // aneinanderliegender Wände exakt aufeinander fallen.
            const float centerScaleOffset = 0.001f;
            var bodyScale = new Vector3(
                fullScale.x - centerScaleOffset,
                fullScale.y - centerScaleOffset,
                fullScale.z - centerScaleOffset);
            go.transform.localScale = bodyScale;

            var edges = ApplyWallLook(go, renderer, cubeMesh, bodyScale, fullScale);

            Destroy(go.GetComponent<Collider>());

            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var view = go.AddComponent<WallView>();
            view.Obs = obs;
            view._moving = true;
            view._length = fullScale.z;
            view._hitTime = hitTime;
            view._njs = njs;
            view._rawBottom = rawBottom;
            view._rawHeight = height;
            view._isFloorWall = rawBottom <= 0.001f;
            view._edges = edges;
            view.ApplyVerticalGeometry(); // Höhe/Position inkl. „Feldhöhen"-Versatz setzen
            return view;
        }

        // ---------- Optik: Wand aus ArcViewer (Körper + Kanten) ----------
        // Zwei Renderer je Wand (Körper = unlit transparent, Kanten = Lit + AlphaClip),
        // beide Materialien GETEILT und ohne Texturen; die Shader rechnen nur mit
        // Position/Normal (siehe PROJECT_NOTES §10). Der Kanten-Shader braucht die
        // Wandgröße als "_WallScale", damit die Kantenbreite auch bei sehr
        // unterschiedlichen Maßen (4 Spalten breit vs. 0,5 hoch) überall gleich bleibt.
        private static Material _wallBodyMat, _wallEdgeMat;
        private static MaterialPropertyBlock _wallProps; // wiederverwendet (keine Allokation je Wand)
        private static bool _wallLookLoaded;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int WallScaleId = Shader.PropertyToID("_WallScale");
        private static readonly int LightnessId = Shader.PropertyToID("_Lightness");

        private static void EnsureWallLook()
        {
            if (_wallLookLoaded) return;
            _wallLookLoaded = true;
            _wallProps = new MaterialPropertyBlock();
            _wallBodyMat = Resources.Load<Material>("BeatSaberDemo/Materials/Wall");
            _wallEdgeMat = Resources.Load<Material>("BeatSaberDemo/Materials/WallEdges");
            if (_wallBodyMat == null) _wallBodyMat = CreateFallbackGlassMaterial();
        }

        private static Transform ApplyWallLook(
            GameObject body, MeshRenderer bodyRenderer, Mesh cubeMesh, Vector3 bodyScale, Vector3 fullScale)
        {
            EnsureWallLook();

            // Körper: halbtransparenter Wandblock (ArcViewer-WallShader). Farbe, Deckkraft und
            // Helligkeit pro Wand über den wiederverwendeten Block — das Material bleibt geteilt.
            var color = GameConfig.WallColor;
            bodyRenderer.sharedMaterial = _wallBodyMat;
            _wallProps.SetColor(BaseColorId, new Color(color.r, color.g, color.b, GameConfig.WallOpacity));
            _wallProps.SetFloat(LightnessId, GameConfig.WallLightness);
            bodyRenderer.SetPropertyBlock(_wallProps);

            if (_wallEdgeMat == null) return null; // Fallback (Assets fehlen): ohne Kanten

            var edges = new GameObject("Edges");
            edges.layer = GameConfig.WallLayer;
            edges.transform.SetParent(body.transform, false);
            // Gegenskalierung des minimal kleineren Körpers => die Kantenhülle liegt exakt
            // auf fullScale (ArcViewer WallHandler.SetScale).
            edges.transform.localScale = new Vector3(
                fullScale.x / bodyScale.x, fullScale.y / bodyScale.y, fullScale.z / bodyScale.z);

            var edgeRenderer = edges.AddComponent<MeshRenderer>();
            var edgeFilter = edges.AddComponent<MeshFilter>();
            edgeFilter.sharedMesh = cubeMesh;
            edgeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            edgeRenderer.sharedMaterial = _wallEdgeMat;
            _wallProps.SetColor(BaseColorId, color);      // Kantenfarbe (voll deckend)
            _wallProps.SetVector(WallScaleId, fullScale); // Kantenbreite unabhängig von der Wandgröße
            edgeRenderer.SetPropertyBlock(_wallProps);
            return edges.transform;
        }

        private static Material CreateFallbackGlassMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = "WallGlassFallback" };

            mat.SetFloat("_Surface", 1f);                          // Transparent
            mat.SetFloat("_Blend", 0f);                            // Alpha-Blending
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            mat.SetColor("_BaseColor", new Color(0.80f, 0.90f, 1.00f, 0.30f));
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.92f);
            mat.SetColor("_EmissionColor", Color.black);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_ON");
            return mat;
        }

        /// <summary>
        /// Bewegt die Wand zeitbasiert (GameConfig.JumpZ, kontinuierlicher NJS-Flug) und
        /// sammelt sie ein, sobald ihre HINTERkante die Entsorgungslinie passiert hat.
        /// ANZEIGE = Score-Uhr + Noten-Offset (GameManager.VisualTime); die Kollisionen
        /// (IsHeadInside/IntersectsSegment) rechnen dagegen immer auf der festen Score-Ebene.
        /// Die Vorderkante ist um <c>NoteSize/2</c> versetzt, damit sie mit der
        /// Vorderfläche der Noten fluchtet (ArcViewer WallManager.UpdateVisual).
        /// </summary>
        private void Update()
        {
            if (!_moving || Consumed) return;

            float visualTime = GameManager.VisualTime;
            float front = GameConfig.JumpZ(_hitTime, visualTime, _njs)
                          - GameConfig.NoteSize * 0.5f;

            // Vertikale Geometrie live an den „Feldhöhen"-Versatz anpassen (Boden-Wände
            // bleiben am Boden und wachsen nach oben; schwebende Wände werden mitverschoben).
            ApplyVerticalGeometry();

            var pos = transform.localPosition;
            pos.z = front + _length * 0.5f;
            transform.localPosition = pos;

            if (front + _length <= GameConfig.DestroyZ)
            {
                _moving = false;
                Consumed = true;
            }
        }

        /// <summary>
        /// Weltposition der festen SCORE-Ebene (Kollisionen) zum aktuellen Score-Zeitpunkt.
        /// Die Anzeige kann per Noten-Offset verschoben sein — die Kollision nicht.
        /// </summary>
        private Vector3 ScoreWorldPosition()
        {
            float front = GameConfig.JumpZ(_hitTime, GameManager.ScoreTime, _njs)
                          - GameConfig.NoteSize * 0.5f;
            var local = transform.localPosition;
            local.y = CurrentCenterY();
            local.z = front + _length * 0.5f;
            Transform parent = transform.parent;
            return parent != null ? parent.TransformPoint(local) : local;
        }

        /// <summary>
        /// Setzt die Wand-Geometrie (Höhe/Y). Die „Feldhöhe" verschiebt ALLES außer dem Boden:
        /// schwebende Wände komplett; bodenständige Wände werden nach UNTEN bis zum Boden
        /// verlängert, damit sie ihn weiter berühren. Läuft jeden Frame → live.
        /// </summary>
        private void ApplyVerticalGeometry()
        {
            // Nur neu anwenden, wenn sich die „Feldhöhe" geändert hat: Roh-Geometrie und
            // Wandtyp sind fix, alles andere konstant — spart Transform-Dirty-Writes pro
            // Frame und Wand. Bei einer echten Feldhöhen-Änderung greift es sofort wieder.
            float fieldH = GameConfig.FieldHeightOffsetMeters;
            if (_geomApplied && Mathf.Approximately(fieldH, _geomFieldHeight)) return;
            _geomApplied = true;
            _geomFieldHeight = fieldH;

            float fullY = CurrentFullHeight();
            float bottom = CurrentBottom();
            float bodyY = Mathf.Max(GameConfig.MinWallSize, fullY - 0.001f);

            var scale = transform.localScale;
            scale.y = bodyY;
            transform.localScale = scale;

            if (_edges != null)
            {
                var es = _edges.localScale;
                es.y = fullY / bodyY;
                _edges.localScale = es;
            }

            var pos = transform.localPosition;
            pos.y = bottom + fullY * 0.5f + GameConfig.WallContainerOffsetY;
            transform.localPosition = pos;
        }

        /// <summary>Unterkante (lokal): Alles außer dem Boden folgt dem „Feldhöhen"-Versatz.
        /// Bodenständige Wände werden bis zum Boden (0) verlängert, damit sie ihn weiter berühren.</summary>
        private float CurrentBottom()
            => _isFloorWall ? 0f : _rawBottom + GameConfig.FieldHeightOffsetMeters;

        /// <summary>Gesamthöhe: bodenständige Wände werden um den „Feldhöhen"-Versatz nach UNTEN
        /// bis zum Boden verlängert (Höhe = Rohhöhe + Versatz); schwebende Wände behalten ihre Höhe
        /// und werden nur verschoben.</summary>
        private float CurrentFullHeight()
            => Mathf.Max(GameConfig.MinWallSize,
                _isFloorWall ? _rawHeight + GameConfig.FieldHeightOffsetMeters : _rawHeight);

        /// <summary>Y-Mitte (lokal) der aktuellen Wand (für die Kollision) — inkl. des
        /// Wand-Containers (ArcViewer „Walls" y = 0.1).</summary>
        private float CurrentCenterY()
            => CurrentBottom() + CurrentFullHeight() * 0.5f + GameConfig.WallContainerOffsetY;

        /// <summary>Welt→Lokal-Matrix der SCORE-Position (Rotation/Skalierung der Wand).</summary>
        private Matrix4x4 ScoreToLocal()
        {
            return Matrix4x4.TRS(ScoreWorldPosition(), transform.rotation, transform.lossyScale).inverse;
        }

        /// <summary>
        /// True, wenn der Kopf (HMD) im Wand-Quader steht. Rechnung im LOKALEN Raum der
        /// SCORE-Position (Matrix berücksichtigt die komplette Skalierung des Raum-Roots);
        /// der Einheitswürfel reicht in Mesh-Koordinaten von -0.5..0.5. Bewusst OHNE
        /// Toleranz: Die Wand ist in Bewegungsrichtung auf Dauer × NJS gestreckt — eine
        /// feste lokale Toleranz würde sich in der Länge zu Metern aufblähen.
        /// </summary>
        public bool IsHeadInside(Vector3 headPos)
        {
            if (Consumed) return false;
            Vector3 local = ScoreToLocal().MultiplyPoint3x4(headPos);
            return Mathf.Abs(local.x) <= 0.5f
                && Mathf.Abs(local.y) <= 0.5f
                && Mathf.Abs(local.z) <= 0.5f;
        }

        /// <summary>
        /// True, wenn das Strecken-Segment (z. B. die Schwertklinge) den Wand-Quader
        /// schneidet; liefert zusätzlich den WELT-Eintrittspunkt des Segments in den Quader
        /// (für die Wand-Funken-Position). Manueller Strecke-gegen-Quader-Test im LOKALEN
        /// Raum der SCORE-Position (Slab-/Schichtverfahren); die Matrix rechnet die komplette
        /// Skalierung des Raum-Roots heraus. Bewusst OHNE Collider — die Wand trägt aus
        /// Performance-/Invarianten-Gründen keinen (siehe IsHeadInside/Datei-Header).
        /// </summary>
        public bool TryIntersectSegment(Vector3 worldA, Vector3 worldB, out Vector3 worldPoint)
        {
            worldPoint = worldB;
            if (Consumed) return false;

            Matrix4x4 toWorld = Matrix4x4.TRS(ScoreWorldPosition(), transform.rotation, transform.lossyScale);
            Matrix4x4 toLocal = toWorld.inverse;
            Vector3 a = toLocal.MultiplyPoint3x4(worldA);
            Vector3 b = toLocal.MultiplyPoint3x4(worldB);
            // Der Einheitswürfel reicht in Mesh-Koordinaten von -0.5..0.5.
            const float half = 0.5f;
            float tMin = 0f, tMax = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float d = b[axis] - a[axis];
                if (Mathf.Abs(d) < 1e-6f)
                {
                    // Segment parallel zur Schicht: liegt es außerhalb, gibt es keinen Schnitt.
                    if (a[axis] < -half || a[axis] > half) return false;
                    continue;
                }
                float t1 = (-half - a[axis]) / d;
                float t2 = (half - a[axis]) / d;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tMin) tMin = t1;
                if (t2 < tMax) tMax = t2;
                if (tMin > tMax) return false;
            }
            Vector3 localHit = a + (b - a) * tMin;
            worldPoint = toWorld.MultiplyPoint3x4(localHit);
            return true;
        }
    }
}
