using System;
using UnityEngine;
using EzySlice;

namespace BeatSaber
{
    public enum NoteKind { Red, Blue, Bomb }

    /// <summary>
    /// Eine Note/Bombe: gleichmäßiger Flug entlang der Score-Uhr (NJS der Map), sofortige
    /// chart-treue Ausrichtung, Entsorgung hinter dem Spieler und optisches Zerschneiden
    /// via EzySlice (Körper und Pfeil, mit Querschnittsfläche).
    /// </summary>
    [DefaultExecutionOrder(-100)] // Positions-Update VOR GameManager (Treffer-Test im LateUpdate)
    public sealed class NoteView : MonoBehaviour
    {
        public NoteModel Note { get; private set; }
        public NoteKind Kind { get; private set; }
        public bool IsConsumed { get; private set; }

        /// <summary>v3/v4-Chain-Kopf: wird mit max. 85 Punkten (statt 115) gewertet.</summary>
        public bool IsChainHead => Note != null && Note._isChainHead;

        /// <summary>Pfeilrichtung der Note (0..7 gerichtet, 8/9 = Dot/aus jeder Richtung gültig).</summary>
        public int CutDirection => _cutDirection;

        public Action<NoteView> OnPassedMiss;
        public Action<NoteView> OnComboBreak;

        /// <summary>True, wenn der letzte TrySlice-Aufruf ein gültiger Schnitt war (Score-Routing).</summary>
        public bool WasValidSlice { get; private set; }

        /// <summary>Beat-Zeitpunkt der Note in Lied-Sekunden (für die Timing-Bewertung).</summary>
        public float HitTime { get; private set; }

        private Rigidbody _rb;
        private MeshRenderer _bodyRenderer;
        private MeshFilter _bodyFilter;
        private Transform _dot;
        private bool _moving;
        private float _njs;              // Map-Tempo (Basis-Einheiten/s, aus den Song-Daten)
        private float _reactionTime;     // Sekunden bis zur halben Sprungdistanz
        private float _halfJumpDistance; // Meter (Basis-Einheiten)
        private Quaternion _targetRotation;    // Endausrichtung (Pfeilrichtung)
        private float _targetRotationZ;         // Ziel-Z-Winkel (Grad), z. B. für Noodle-Flip
        // Muster-Effekte (nur wenn das Muster sie vorgibt, sonst neutral):
        private Vector3 _targetBase;     // Zielposition (x/y) ohne Z-(Flug)-Anteil
        private float _startY;           // Start-Höhe der Stapel-Animation (== Zielhöhe bei normalen Noten)
        private float _flipStartX;       // Start-X bei überkreuzten Doppeln
        private float _flipYHeight;      // 0 = kein Flip; sonst ±0.45/-0.15 (ArcViewer)
        private int _cutDirection; // 0..7 gerichtet, 8/9 = beliebig (aus jeder Richtung schneidbar)

        // ---------- Statische Material-/Mesh-Ladehilfen ----------
        // Der Pfeil wird in das Körper-Mesh eingebacken (Sub-Mesh 1) — kein separates
        // Pfeil-Objekt mehr. Material-Array ist immer [0]=Körper, [1]=Pfeil.
        private static Material _redBody, _blueBody, _bombBody, _arrowRed, _arrowBlue;
        private static Mesh _bodyMesh, _bombMesh, _arrowMesh, _chainHeadMesh;
        private static Mesh _bodyMeshArrowed, _chainHeadMeshArrowed;
        private static Material[] _matRedArrow, _matBlueArrow, _matRed, _matBlue, _matBomb;
        private static Material _dotMaterial;
        private static Mesh _dotMesh;
        private static bool _assetsLoaded;

        /// <summary>
        /// Pfeil-Auflagepunkt (lokal): obere Notenhälfte, bündig auf der Vorderfläche
        /// (BeatSaberClone-Referenz: Box 0,45 → Pfeil bei y = 0,125, z = −0,225; bei
        /// Notengröße 0,5 also y = 0,125, z = −0,25). Der Pfeil wird beim Einbacken um
        /// diesen Punkt versetzt, das Mesh liegt damit flach auf der Note auf.
        /// </summary>
        private static readonly Vector3 ArrowLocalPos = new Vector3(0f, 0.125f, -GameConfig.NoteSize * 0.5f);

        public static Mesh CachedBodyMesh { get { EnsureAssetsLoaded(); return _bodyMesh; } }
        public static Mesh CachedDotMesh { get { EnsureAssetsLoaded(); return _dotMesh; } }
        public static Material CachedDotMaterial { get { EnsureAssetsLoaded(); return _dotMaterial; } }
        /// <summary>Noten-Körpermaterialien (BeatSaberClone-Stil) — von ChainView mitgenutzt,
        /// damit Ketten-Glieder exakt denselben Look haben.</summary>
        public static Material BodyMaterialRed { get { EnsureAssetsLoaded(); return _redBody; } }
        public static Material BodyMaterialBlue { get { EnsureAssetsLoaded(); return _blueBody; } }

        /// <summary>Weißer Kreis-Marker der Dot-Noten (Richtung 8/9), Kind der Note.</summary>
        public void AttachDot(Transform dot) => _dot = dot;

        public static void EnsureAssetsLoaded()
        {
            if (_assetsLoaded) return;
            _assetsLoaded = true;
            // Referenz 1:1 BeatSaberClone: Notenkörper = flache Unlit-Graphen (Rot/Blau, kein
            // Leuchten), Pfeile = Emission-Graphen. new Material(shader) liefert die
            // Graph-Defaults = exakt das eingebettete Material der Referenz.
            _redBody = BuildGraphMaterial("BeatSaberDemo/Shaders/RedUnlit");
            _blueBody = BuildGraphMaterial("BeatSaberDemo/Shaders/BlueUnlit");
            // Mine (Bombe): Modell + Material aus dem Referenz-Viewer ArcViewer
            // (Models/Bomb.obj + Materials/Bomb.mat, NoteShader/Cubemap). Fallback BlackMat.
            _bombBody = Resources.Load<Material>("BeatSaberDemo/Materials/Bomb");
            if (_bombBody == null) _bombBody = Resources.Load<Material>("BeatSaberDemo/Materials/BlackMat");
            _arrowRed = BuildGraphMaterial("BeatSaberDemo/Shaders/RedEmission");
            _arrowBlue = BuildGraphMaterial("BeatSaberDemo/Shaders/BlueEmission");
            if (_arrowRed == null) _arrowRed = Resources.Load<Material>("BeatSaberDemo/Materials/RedMat");
            if (_arrowBlue == null) _arrowBlue = Resources.Load<Material>("BeatSaberDemo/Materials/BlueMat");

            // Notenkörper = BeatSaberClone-Würfel Box.fbx (auf 0,50 m importiert und
            // read/write enabled → EzySlice kann ihn zerschneiden). WICHTIG: Ein Mesh mit
            // `isReadable: 0` (z. B. ArcViewers Cube.fbx) lässt den Schnitt stillschweigend
            // ausfallen — die Schnitt-Optik verschwindet dann komplett.
            var bodyPrefab = Resources.Load<GameObject>("BeatSaberDemo/Models/Box");
            if (bodyPrefab != null)
            {
                var mf = bodyPrefab.GetComponentInChildren<MeshFilter>();
                if (mf != null) _bodyMesh = mf.sharedMesh;
            }
            var arrowPrefab = Resources.Load<GameObject>("BeatSaberDemo/Models/Arrow");
            if (arrowPrefab != null)
            {
                var mf = arrowPrefab.GetComponentInChildren<MeshFilter>();
                if (mf != null) _arrowMesh = mf.sharedMesh;
            }
            // Chain-Kopf-Mesh (ArcViewer ChainHead.fbx).
            var chainHeadPrefab = Resources.Load<GameObject>("BeatSaberDemo/Models/ChainHead");
            if (chainHeadPrefab != null)
            {
                var mf = chainHeadPrefab.GetComponentInChildren<MeshFilter>();
                if (mf != null) _chainHeadMesh = mf.sharedMesh;
            }
            // Minen-Mesh (ArcViewer-Modell; nullptr = alte Würfel-Optik bleibt).
            var bombPrefab = Resources.Load<GameObject>("BeatSaberDemo/Models/Bomb");
            if (bombPrefab != null)
            {
                var mf = bombPrefab.GetComponentInChildren<MeshFilter>();
                if (mf != null) _bombMesh = mf.sharedMesh;
            }
            if (_bodyMesh == null) _bodyMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            // Den Pfeil fest in den Körper einbacken (EIN Mesh, 2 Sub-Meshes: 0=Körper, 1=Pfeil).
            // So ist der Pfeil Teil der Note (kein separates Pfeil-Objekt mehr) und wird beim
            // Schnitt als ein Stück mitgeschnitten. Beide Quell-Meshes sind read/write enabled.
            if (_bodyMesh != null && _arrowMesh != null)
                _bodyMeshArrowed = BakeArrowMesh(_bodyMesh, _arrowMesh, ArrowLocalPos);
            if (_chainHeadMesh != null && _arrowMesh != null)
                _chainHeadMeshArrowed = BakeArrowMesh(_chainHeadMesh, _arrowMesh, ArrowLocalPos);

            // Material-Arrays (0=Körper, 1=Pfeil) einmalig — kein GC beim Spawn.
            _matRedArrow = new[] { _redBody, _arrowRed };
            _matBlueArrow = new[] { _blueBody, _arrowBlue };
            _matRed = new[] { _redBody };
            _matBlue = new[] { _blueBody };
            _matBomb = new[] { _bombBody };

            // Dot-Marker: weißer, gefüllter Kreis (Durchmesser = 1/4 der Box, siehe NotePool).
            // Eigenes Mesh (Dreiecksfächer) + flaches weißes Unlit-Material; unabhängig von
            // den Noten-Graphen, damit der Kreis neutral weiß bleibt.
            _dotMesh = BuildCircleMesh(48, 0.5f);
            _dotMaterial = BuildWhiteUnlitMaterial();
        }

        /// <summary>
        /// Kreis-Mesh in der XY-Ebene (Normal −Z), Radius in lokalen Einheiten. Als
        /// Dreiecksfächer aufgebaut; der Durchmesser ist damit 2 × <paramref name="radius"/>.
        /// </summary>
        private static Mesh BuildCircleMesh(int segments, float radius)
        {
            segments = Mathf.Max(3, segments);
            var mesh = new Mesh { name = "NoteDotCircle" };
            var verts = new Vector3[segments + 1];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f);
            }
            var tris = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris[i * 3] = 0;
                tris[i * 3 + 1] = next + 1;
                tris[i * 3 + 2] = i + 1;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flaches weißes Unlit-Material (URP/Unlit, Fallbacks wie im übrigen Projekt).</summary>
        private static Material BuildWhiteUnlitMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "NoteDotWhite" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            mat.enableInstancing = true;
            return mat;
        }

        /// <summary>Material aus einem Referenz-ShaderGraph (Graph-Defaults = eingebettetes Material).</summary>
        private static Material BuildGraphMaterial(string resourcePath)
        {
            var shader = Resources.Load<Shader>(resourcePath);
            if (shader == null)
            {
                Debug.LogWarning("[BeatSaber] Referenz-ShaderGraph nicht geladen: " + resourcePath);
                return null;
            }
            var mat = new Material(shader);
            mat.enableInstancing = true;
            return mat;
        }

        /// <summary>
        /// Backt den flachen Pfeil auf den Notenkörper: EIN Mesh mit 2 Sub-Meshes
        /// (0 = Körper, 1 = Pfeil um <paramref name="offset"/> versetzt). Der Pfeil ist
        /// damit Teil der Note und wird von EzySlice automatisch mitgeschnitten.
        /// Normalen und UVs werden mit übernommen (die Referenz-Graphen nutzen beide),
        /// Tangenten bewusst nicht (die Graphen brauchen sie nicht; EzySlice kommt ohne klar).
        /// </summary>
        private static Mesh BakeArrowMesh(Mesh body, Mesh arrow, Vector3 offset)
        {
            Vector3[] bv = body.vertices;
            Vector3[] av = arrow.vertices;
            int bodyCount = bv.Length;
            int total = bodyCount + av.Length;

            var verts = new Vector3[total];
            Array.Copy(bv, 0, verts, 0, bodyCount);
            for (int i = 0; i < av.Length; i++) verts[bodyCount + i] = av[i] + offset;

            var normals = new Vector3[total];
            Vector3[] bn = body.normals;
            Vector3[] an = arrow.normals;
            for (int i = 0; i < bodyCount; i++) normals[i] = i < bn.Length ? bn[i] : Vector3.up;
            for (int i = 0; i < av.Length; i++) normals[bodyCount + i] = i < an.Length ? an[i] : Vector3.up;

            var uvs = new Vector2[total];
            Vector2[] bu = body.uv;
            Vector2[] au = arrow.uv;
            for (int i = 0; i < bodyCount; i++) uvs[i] = i < bu.Length ? bu[i] : Vector2.zero;
            for (int i = 0; i < av.Length; i++) uvs[bodyCount + i] = i < au.Length ? au[i] : Vector2.zero;

            int[] bt = body.GetTriangles(0);
            int[] at = arrow.GetTriangles(0);
            var arrowTris = new int[at.Length];
            for (int i = 0; i < at.Length; i++) arrowTris[i] = at[i] + bodyCount;

            var mesh = new Mesh { name = body.name + "+Arrow" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(bt, 0, false);
            mesh.SetTriangles(arrowTris, 1, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Lebenszyklus ----------

        public void Spawn(NoteModel note, Vector3 position,
            float hitTime, float njs, float reactionTime, float halfJumpDistance,
            float startY, float flipStartX, float flipYHeight)
        {
            if (_rb == null) _rb = GetComponent<Rigidbody>();
            Note = note;
            Kind = note._type == 3 ? NoteKind.Bomb : note._type == 0 ? NoteKind.Red : NoteKind.Blue;
            IsConsumed = false;
            WasValidSlice = false;
            HitTime = hitTime;
            _njs = njs;
            _reactionTime = reactionTime;
            _halfJumpDistance = halfJumpDistance;
            _moving = true;

            // Aussehen: Der Pfeil ist bereits in das Körper-Mesh eingebacken (Sub-Mesh 1),
            // daher nur noch Mesh + Material-Array setzen. Pfeil nur bei gerichteten Noten
            // (0..7); bei 8/9 („Dot": aus jeder Richtung schneidbar) bleibt der Block
            // pfeillos — wie im Original.
            if (_bodyRenderer == null) _bodyRenderer = GetComponent<MeshRenderer>();
            if (_bodyFilter == null) _bodyFilter = GetComponent<MeshFilter>();
            bool showArrow = Kind != NoteKind.Bomb && note._cutDirection <= 7;

            Mesh bodyMesh;
            Material[] bodyMats;
            if (Kind == NoteKind.Bomb)
            {
                bodyMesh = _bombMesh != null ? _bombMesh : _bodyMesh;
                bodyMats = _matBomb;
            }
            else
            {
                bool chain = note._isChainHead && _chainHeadMesh != null;
                bool arrowed = showArrow && (chain ? _chainHeadMeshArrowed : _bodyMeshArrowed) != null;
                bodyMesh = chain
                    ? (arrowed ? _chainHeadMeshArrowed : _chainHeadMesh)
                    : (arrowed ? _bodyMeshArrowed : _bodyMesh);
                bodyMats = Kind == NoteKind.Red
                    ? (arrowed ? _matRedArrow : _matRed)
                    : (arrowed ? _matBlueArrow : _matBlue);
            }
            if (_bodyFilter != null && bodyMesh != null) _bodyFilter.sharedMesh = bodyMesh;
            if (_bodyRenderer != null) _bodyRenderer.sharedMaterials = bodyMats;

            // Dot-Noten (Richtung 8/9, pfeillos): weißer Kreis-Marker (separates Kind) statt Pfeil.
            bool showDot = Kind != NoteKind.Bomb && note._cutDirection >= 8;
            if (_dot != null) _dot.gameObject.SetActive(showDot);

            // Richtung = Pfeilrichtung + v3/v4-Winkel-Offset „a" (ArcViewer CalculateObjectAngle(d,a)).
            _targetRotationZ = TargetRotationZ(note._cutDirection) + note._angleOffset;
            // Noodle Extensions: freie Rotation (worldRotation/localRotation) überschreibt die
            // Pfeilrichtung; `flip` spiegelt sie. Bei freier Rotation entfällt das Z-Einrotieren.
            var noodle = note._customData;
            if (noodle != null && noodle.HasWorldRotation)
            {
                var q = noodle.WorldRotation;
                _targetRotation = new Quaternion(q[0], q[1], q[2], q[3]);
            }
            else if (noodle != null && noodle.HasLocalRotation)
            {
                var q = noodle.LocalRotation;
                _targetRotation = Quaternion.Euler(0f, 0f, _targetRotationZ)
                    * new Quaternion(q[0], q[1], q[2], q[3]);
            }
            else
            {
                _targetRotation = Quaternion.Euler(0f, 0f, _targetRotationZ);
            }
            if (noodle != null && noodle.Flip) _targetRotationZ = -_targetRotationZ;
            _targetRotationZ = _targetRotation.eulerAngles.z;
            _cutDirection = note._cutDirection;

            // Noodle-Scale (v3 `scale`) — die Trefferbox skaliert mit (lossyScale).
            transform.localScale = noodle != null && noodle.HasScale
                ? new Vector3(noodle.Scale[0], noodle.Scale[1], noodle.Scale[2])
                : Vector3.one;

            // Muster-Effekte (vom GameManager aus dem Chart berechnet): Stapel-Start-Höhe
            // und Flip-Daten. Bei normalen Noten ist startY == Ziel-Y und flipYHeight == 0 —
            // dann fliegt die Note gerade (kein Effekt).
            _targetBase = position;
            _startY = startY;
            _flipStartX = flipStartX;
            _flipYHeight = flipYHeight;

            // Position ist eine BASIS-Position im Raum-Root (_gameRoot). Der Root skaliert
            // den kompletten Raum (PlayerProfile.Scale) — deshalb lokal setzen, nicht welt.
            transform.localPosition = new Vector3(
                Mathf.Approximately(_flipYHeight, 0f) ? position.x : flipStartX,
                _startY + GameConfig.FieldHeightOffsetMeters, position.z);
            // Chart-treue Ausrichtung: Pfeilrichtung steht ab dem ersten Frame fest.
            transform.localRotation = _targetRotation;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!_moving) return;

            // Position aus der Visual-Uhr (Score-Uhr + ggf. Noten-Offset — der verschiebt
            // jetzt ALLES gemeinsam, auch die Trefferfläche).
            float visualTime = GameManager.VisualTime;
            transform.localPosition = LocalPositionAt(visualTime);

            // KEIN globales Einrotieren: ArcViewers `doRotationAnimation` ist standardmäßig AUS
            // (nur per Setting/Force-Accuracy). Noten stehen sofort auf ihrer Zieldrehung; nur
            // Noodle-Noten tragen eine freie Rotation (`_targetRotation`).
            transform.localRotation = _targetRotation;

            float z = transform.localPosition.z;
            if (!IsConsumed && z <= GameConfig.NoteMissZ)
            {
                IsConsumed = true;
                if (GameConfig.LogHitDiagnostics)
                    Debug.Log($"[Miss] kind={Kind} dir={_cutDirection} hitTime={HitTime:0.000}");
                OnPassedMiss?.Invoke(this);
            }

            if (IsConsumed && z <= GameConfig.NoteMissZ)
            {
                _moving = false;
                NotePool.Return(this);
            }
        }

        /// <summary>
        /// Position (lokal im Parent) zu einem beliebigen Lied-Zeitpunkt — identisch zur
        /// Update-Bewegung inklusive Stapel-Parabel und Seiten-Flip.
        /// </summary>
        public Vector3 LocalPositionAt(float songTime)
        {
            float z = GameConfig.JumpZ(HitTime, songTime, _njs);

            // Stapel-Parabel: Vor dem Eintritt in die letzte ReactionTime steht die Note auf
            // ihrer Start-Höhe; danach gleitet sie in die Zielreihe (bei startY == Ziel-Y
            // ist das eine Konstante = gerade Flugbahn).
            float x = _targetBase.x;
            float y = songTime < HitTime - _reactionTime
                ? _startY
                : GameConfig.SpawnParabola(_targetBase.y, _startY, _halfJumpDistance, z);

            // Seiten-Flip (nur wenn das Muster ihn vorgibt): Fortschritt 0 = Eintritt in
            // die Reaktion, 1 = Beat. Im ersten Halb-Fortschritt wechselt die Note von der
            // Startseite auf ihre eigene und hat dabei den vertikalen Über-/Unter-Bogen.
            if (!Mathf.Approximately(_flipYHeight, 0f))
            {
                float progress = (songTime + _reactionTime - HitTime) / _reactionTime;
                if (progress <= 0f)
                {
                    x = _flipStartX;
                }
                else if (progress < 0.5f)
                {
                    float t = progress / 0.5f;
                    x = Mathf.Lerp(_flipStartX, _targetBase.x, QuadInOut(t));
                    y += _flipYHeight * (0.5f - Mathf.Cos(progress * Mathf.PI * 4f) / 2f);
                }
            }

            // „Feldhöhen"-Versatz (Noten/Minen) jeden Frame addieren: wirkt live, und da
            // die Note-Transform selbst die Trefferfläche ist, wandert sie automatisch mit.
            return new Vector3(x, y + GameConfig.FieldHeightOffsetMeters, z);
        }

        /// <summary>EaseInOut-Quad wie ArcViewer (`Easings.Quad.InOut`) für den Seiten-Flip.</summary>
        private static float QuadInOut(float x)
        {
            return x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f;
        }

        // ---------- Schnitt ----------

        public bool TrySlice(
            SaberSide side,
            Vector3 slicePoint,
            Vector3 planeNormal,
            Material crossSectionMaterial,
            Vector3 cutVelocity)
        {
            if (IsConsumed || !_moving) return false;

            if (Kind == NoteKind.Bomb)
            {
                Fx.PlayMineExplosion(
                    transform.position,
                    Mathf.Max(0.01f, transform.lossyScale.x));
                ConsumeFail();
                return true;
            }

            // Farbe/Hand: Rot = linkes Schwert, Blau = rechtes (NoteKind stammt aus _type).
            bool wrongHand = (Kind == NoteKind.Red) != (side == SaberSide.Left);

            bool wrongDirection;
            float moveAngle = -1f; // nur für die Diagnose (LogHitDiagnostics)
            // Noten-Lokalsystem: nimmt die Anzeigen-Rotation (Z), die Raum-Gier und die
            // Raum-Skalierung mit — der Pfeil zeigt dadurch immer nach lokal −Y.
            Vector3 local = transform.InverseTransformDirection(cutVelocity);
            var v2 = new Vector2(local.x, local.y); // Tiefe ignorieren
            float speed2 = v2.magnitude;

            // ZWEI getrennte Fälle (statt eines gemeinsamen „falsch"):
            //  - tooSlow: die Klinge BERÜHRT die Note nur, es ist gar kein Schnitt-Versuch
            //    (Note bleibt am Leben, läuft ggf. als MISS durch).
            //  - falsche Richtung: ein ECHTER Hieb am Pfeil vorbei ⇒ sofort Bad Cut.
            bool tooSlow = speed2 < GameConfig.CutMinSpeed;
            if (tooSlow)
            {
                wrongDirection = false;
            }
            else if (_cutDirection >= 8)
            {
                // Dot (8/9): Richtung beliebig — nur echte Bewegung verlangt
                // (OpenSaberPlus: "ignore angle if is a dot").
                wrongDirection = false;
                moveAngle = 0f;
            }
            else
            {
                moveAngle = Vector2.Angle(v2, Vector2.down); // Pfeilrichtung = lokal −Y
                wrongDirection = moveAngle > GameConfig.CutAllowedAngle;
            }

            if (GameConfig.LogHitDiagnostics)
            {
                Debug.Log($"[Hit] side={side} kind={Kind} dir={_cutDirection} " +
                          $"valid={!(wrongHand || wrongDirection || tooSlow)} wrongHand={wrongHand} " +
                          $"wrongDirection={wrongDirection} angle={moveAngle:0.0} " +
                          $"z={transform.localPosition.z:0.00} speed={speed2:0.00}");
            }

            // Reine Berührung (kein Schwung): KEIN Verbrauch — gleiches Verhalten wie die
            // Referenz, die einen Schnitt erst ab einer Mindest-Klingengeschwindigkeit wertet.
            if (tooSlow)
            {
                if (GameConfig.LogHitDiagnostics)
                    Debug.Log($"[Ignore] side={side} kind={Kind} dir={_cutDirection} " +
                              $"z={transform.localPosition.z:0.00} angle={moveAngle:0.0} " +
                              $"speed={speed2:0.00} (kein Schwung)");
                return false;
            }

            // ---- Fehlerfall ("Bad Cut"): falsche Farbe/Hand oder falsche Richtung ----
            // Es gibt KEINEN „Aushol-Schutz" mehr (früher wurde ein falscher Kontakt weit vor
            // der Treffebene ignoriert). Der erlaubte, dass man eine Note von der FALSCHEN
            // SEITE anschneidet und sie danach trotzdem noch sauber trifft. Jetzt gilt wie in
            // der Referenz (SuperSaber `wrongHit` / Beat Saber): ein Hieb in die falsche
            // Richtung verbraucht die Note sofort als Bad Cut.
            if (wrongHand || wrongDirection)
            {
                IsConsumed = true;
                _moving = false;
                WasValidSlice = false;
                DoSlice(slicePoint, planeNormal, crossSectionMaterial);
                OnComboBreak?.Invoke(this);
                NotePool.Return(this);
                return true;
            }

            IsConsumed = true;
            _moving = false;
            WasValidSlice = true;
            DoSlice(slicePoint, planeNormal, crossSectionMaterial);

            NotePool.Return(this);
            return true;
        }

        /// <summary>
        /// Zerlegt die Note optisch in zwei Hälften (EzySlice) und lässt sie sichtbar
        /// auseinanderfliegen. <paramref name="planeNormal"/> ist die Welt-Normale der
        /// Schnittebene — jede Hälfte fliegt in ihre Richtung (±Normale).
        /// </summary>
        private void DoSlice(Vector3 slicePoint, Vector3 planeNormal, Material crossSection)
        {
            // Der Pfeil steckt als Sub-Mesh im Noten-Mesh und wird daher automatisch
            // mitgeschnitten — kein separates Pfeil-Objekt mehr.
            SliceVisual(gameObject, transform, slicePoint, planeNormal, crossSection);
        }

        /// <summary>
        /// Zerlegt ein sichtbares Mesh (EzySlice) und lässt beide Hälften auseinanderfliegen.
        /// <paramref name="placement"/> ist der Transform, auf den die Hälften gesetzt werden.
        /// </summary>
        private static void SliceVisual(GameObject target, Transform placement,
            Vector3 slicePoint, Vector3 planeNormal, Material crossSection)
        {
            var filter = target.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return;

            var hull = target.Slice(slicePoint, planeNormal);
            if (hull == null) return;

            var upper = hull.CreateUpperHull(target, crossSection);
            var lower = hull.CreateLowerHull(target, crossSection);

            PlacePieceAt(upper, placement);
            PlacePieceAt(lower, placement);

            AddPiecePhysics(upper, planeNormal);
            AddPiecePhysics(lower, -planeNormal);
        }

        /// <summary>
        /// Setzt eine Schnitt-Hälfte exakt auf die WELT-Transform des Ziels (Position, Drehung,
        /// Welt-Skalierung). Als Root-Objekt ist ihre localScale = Welt-Skalierung.
        /// </summary>
        private static void PlacePieceAt(GameObject piece, Transform source)
        {
            if (piece == null) return;
            piece.transform.SetPositionAndRotation(source.position, source.rotation);
            piece.transform.localScale = source.lossyScale;
        }

        private static void AddPiecePhysics(GameObject piece, Vector3 flyDirection)
        {
            if (piece == null) return;
            var rb = piece.AddComponent<Rigidbody>();
            // Statt eines konvexen MeshColliders (Convex-Hull-Cook läuft synchron auf dem
            // Main-Thread = Ruckel-Quelle bei schnellen Schnitten) ein billiger BoxCollider.
            // Für die rein OPTISCHEN Hälften identische Wirkung; die Schnitt-Optik selbst
            // (die EzySlice-Halbmesshes) bleibt völlig unberührt. Größe = Notenwürfel
            // (lokal; die Welt-Skalierung steckt bereits in der localScale der Hälfte).
            var col = piece.AddComponent<BoxCollider>();
            col.size = Vector3.one * GameConfig.NoteSize;

            // Sichtbares Auseinanderfliegen statt nur „nach unten fallen": Die Hälften sind
            // eigene WELT-Objekte (die Raum-Skalierung steckt bereits in ihrer Größe), daher
            // kommt die Raum-Skalierung hier direkt aus der lossyScale der Hälfte mit.
            float roomScale = Mathf.Max(0.01f, piece.transform.lossyScale.x);
            Vector3 dir = flyDirection.sqrMagnitude > 1e-6f ? flyDirection.normalized : Vector3.up;
            rb.velocity = dir * (GameConfig.SlicePieceSpeed * roomScale);
            // angularVelocity ist in rad/s (Unity) — SlicePieceSpin ist bereits rad/s.
            rb.angularVelocity = new Vector3(
                UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f))
                * GameConfig.SlicePieceSpin;

            Destroy(piece, GameConfig.DestroyDelayTime);
        }

        // Hinweis: Die MITTE-Bewertung liegt jetzt in SaberView.MinBladeCenterDeviation
        // (nächster XY-Vorbeilauf über das Messfenster) statt in einem einzelnen Frame —
        // siehe GameManager.ScoreAtCutAsync.

        private void ConsumeFail()
        {
            IsConsumed = true;
            _moving = false;
            // Der Explosions-Partikelstoß liegt im Bomben-Zweig von TrySlice (vor diesem
            // Aufruf); hier passiert nur der Combo-Bruch + Rückgabe in den Pool.
            OnComboBreak?.Invoke(this);
            NotePool.Return(this);
        }

        // ---------- Pfeil/Richtung ----------

        /// <summary>
        /// Pfeil-Rotation um Z. Das Pfeil-Mesh zeigt bei 0° NACH UNTEN (−Y). In Unity
        /// (linkshändig) dreht eine positive Z-Rotation aus Spielersicht GEGEN den
        /// Uhrzeigersinn (Unity-Doku: +X→+Y ist CCW beim Blick entlang +Z), d. h. rechnerisch
        /// 0°=unten, 90°=rechts, 180°=oben, 270°=links. Daraus ergeben sich die Diagonalen.
        /// 0=oben, 1=unten, 2=links, 3=rechts, 4..7 Diagonalen (45°), 8/9=Dot (pfeillos).
        /// </summary>
        private float TargetRotationZ(int cutDirection)
        {
            return cutDirection switch
            {
                0 => 180f,  // oben
                1 => 0f,    // unten
                2 => 270f,  // links
                3 => 90f,   // rechts
                4 => 225f,  // oben-links
                5 => 135f,  // oben-rechts
                6 => 315f,  // unten-links
                7 => 45f,   // unten-rechts
                _ => 0f,    // 8/9 = Dot (Pfeil wird ausgeblendet)
            };
        }

    }
}
