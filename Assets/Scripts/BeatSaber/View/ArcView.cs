using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber
{
    /// <summary>Ein Bogen (Slider): Bézier-Band zwischen Kopf- und Endpunkt.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ArcView : MonoBehaviour
    {
        private LineRenderer _line;
        private float _njs;
        private float _headTime;   // Kopf-Beat in Sekunden
        private float _length;     // Bogenlänge in Bewegungsrichtung (Meter, Basis-Einheiten)
        private bool _moving;
        private MaterialPropertyBlock _mpb; // Textur-Scroll + Interaktions-Intensität (ArcShader)

        // Arc-Interaktion (Original `SliderInteractionManager`): der Bogen leuchtet stärker,
        // wenn die Saber-Klinge ihm folgt, und blendet aus, wenn sie ihn verlässt.
        private float _intensity = 1f;
        private bool _interacted;
        private float _interactionParam;
        private static Color _arcBaseRed, _arcBaseBlue;
        private static bool _arcBaseColorsCached;

        /// <summary>Farbtyp des Bogens: 0 = rot (linkes Schwert), 1 = blau (rechtes).</summary>
        public int ColorType { get; private set; }

        private static readonly Stack<ArcView> _free = new();
        private static Material _redMat, _blueMat;
        private static Gradient _fadeGradient; // wiederverwendet (Enden-Fade je Kurvenlänge)

        /// <summary>Holt einen (gepoolten) Bogen, baut die Kurve und aktiviert ihn.</summary>
        public static ArcView Get(ArcModel arc, float headTime, float tailTime, float njs, Transform root)
        {
            ArcView view = _free.Count > 0 ? _free.Pop() : CreateNew(root);
            view.Build(arc, headTime, tailTime, njs);
            return view;
        }

        public static void Return(ArcView view)
        {
            if (view == null) return;
            view._moving = false;
            view.gameObject.SetActive(false);
            _free.Push(view);
        }

        /// <summary>Alle aktiven Bögen einsammeln (Songwechsel). Gepoolte sind inaktiv.</summary>
        public static void ReturnAll(Transform root)
        {
            if (root == null) return;
            var active = root.GetComponentsInChildren<ArcView>(); // nur aktive
            for (int i = 0; i < active.Length; i++) Return(active[i]);
        }

        private static ArcView CreateNew(Transform root)
        {
            var go = new GameObject("Arc");
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;          // Kurve liegt im lokalen Raum-Root
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthMultiplier = GameConfig.ArcWidth;
            line.positionCount = 0;
            var view = go.AddComponent<ArcView>();
            view._line = line;
            if (root != null) go.transform.SetParent(root, false);
            return view;
        }

        private void Build(ArcModel arc, float headTime, float tailTime, float njs)
        {
            _headTime = headTime;
            _njs = njs;
            _moving = true;
            gameObject.SetActive(true);

            ColorType = arc._headColorType;
            _intensity = 1f;
            _interacted = false;
            _interactionParam = 0f;
            _line.sharedMaterial = arc._headColorType == 0 ? RedMat : BlueMat;

            // 1:1 ArcViewer Arc-Konstruktor (ArcManager.cs):
            Vector3 hp = GameConfig.GridPosition(arc._headLineIndex, arc._headLineLayer);
            Vector3 tp = GameConfig.GridPosition(arc._tailLineIndex, arc._tailLineLayer);
            int headDir = Mathf.Clamp((int)arc._headCutDirection, 0, 8);
            int tailDir = Mathf.Clamp((int)arc._tailCutDirection, 0, 8);
            Vector2 headDirVec = headDir == 8 ? Vector2.zero
                : GameConfig.DirectionVector(GameConfig.NoteAngleDegrees(headDir));
            Vector2 tailDirVec = tailDir == 8 ? Vector2.zero
                : -GameConfig.DirectionVector(GameConfig.NoteAngleDegrees(tailDir));
            Vector2 headControl = new Vector2(hp.x, hp.y)
                + headDirVec * (arc._headControlPointLengthMultiplier * GameConfig.ArcControlOffset);
            Vector2 tailControl = new Vector2(tp.x, tp.y)
                + tailDirVec * (arc._tailControlPointLengthMultiplier * GameConfig.ArcControlOffset);

            // Anschluss an eine Note (ArcViewer ObjectManager `halfNoteOffset` = 0.225):
            // Sitzt an einem Bogen-Ende eine Note, werden Endpunkt UND Kontrollpunkt um
            // ArcAttachOffset in Bogenrichtung verschoben — der Bogen endet so an der
            // Notenkante statt in ihrem Zentrum. Dot-Enden (Richtung 8) haben keinen
            // Richtungsvektor ⇒ kein Versatz (wie in der Referenz).
            if (arc._headAttached)
            {
                Vector2 off = headDirVec * GameConfig.ArcAttachOffset;
                hp.x += off.x; hp.y += off.y;
                headControl += off;
            }
            if (arc._tailAttached)
            {
                Vector2 off = tailDirVec * GameConfig.ArcAttachOffset;
                tp.x += off.x; tp.y += off.y;
                tailControl += off;
            }

            float duration = Mathf.Max(0f, tailTime - headTime);
            _length = Mathf.Max(0.01f, duration * njs);
            int count = Mathf.Clamp(
                Mathf.RoundToInt(GameConfig.ArcSegmentsPerSecond * duration) + 1,
                GameConfig.ArcMinPoints, GameConfig.ArcMaxPoints);
            _line.positionCount = count;

            Vector3 p0 = new Vector3(hp.x, hp.y, 0f);
            Vector3 p1 = new Vector3(headControl.x, headControl.y, 0f);
            Vector3 p2 = new Vector3(tailControl.x, tailControl.y, _length);
            Vector3 p3 = new Vector3(tp.x, tp.y, _length);
            // Kurve setzen und dabei die ECHTE Kurvenlänge messen (Grundlage des festen
            // Enden-Fades, ArcViewer `GetCurveLength`).
            Vector3 prev = CubicBezier(p0, p1, p2, p3, 0f);
            _line.SetPosition(0, prev);
            float curveLength = 0f;
            for (int i = 1; i < count; i++)
            {
                float t = (float)i / (count - 1);
                Vector3 p = CubicBezier(p0, p1, p2, p3, QuadInOut(t));
                _line.SetPosition(i, p);
                curveLength += Vector3.Distance(prev, p);
                prev = p;
            }
            ApplyEndFade(curveLength);

            // Position SOFORT setzen: Der Bogen wird noch in DIESEM Frame aktiviert, sein
            // `Update` (Order −100) lief aber bereits VOR GameManager (0). Ohne diese Zeile
            // würde er einen Frame lang an der ALTEN Position aus dem Pool erscheinen
            // (gemeldeter „Bogen blitzt oben auf"-Bug). Die Zeit ist dieselbe wie in Update.
            float z = GameConfig.JumpZ(_headTime, GameManager.VisualTime, _njs);
            transform.localPosition = new Vector3(0f, GameConfig.FieldHeightOffsetMeters, z);
        }

        private void Update()
        {
            if (!_moving) return;
            float z = GameConfig.JumpZ(_headTime, GameManager.VisualTime, _njs);
            transform.localPosition = new Vector3(0f, GameConfig.FieldHeightOffsetMeters, z);
            // ArcShader-Textur-Scroll (ArcViewer `arcAnimationSpeed`): das Band „fließt".
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.SetFloat("_TextureOffset",
                Mathf.Repeat(_headTime * 0.1f + Time.time * GameConfig.ArcAnimationSpeed, 1f));
            // Arc-Interaktion: gefolgt = heller, verlassen = ausblenden (Original `SliderIntensityEffect`).
            float target = _interactionParam > 0.001f
                ? 1f + GameConfig.ArcInteractIntensityBoost * _interactionParam
                : (_interacted ? GameConfig.ArcInteractFadedIntensity : 1f);
            _intensity = Mathf.MoveTowards(_intensity, target, Time.deltaTime * GameConfig.ArcInteractFadeSpeed);
            ApplyInteractionColor(_intensity);
            _line.SetPropertyBlock(_mpb);
            // Ende: Der Bogen verschwindet, sobald auch sein Ende die „verpasst"-Linie passiert.
            if (z <= GameConfig.NoteMissZ - _length)
            {
                Return(this);
            }
        }

        // ---------- Kurven-/Material-Helfer ----------

        /// <summary>Setzt den Interaktionsgrad (0..1) dieses Frames (vom GameManager).</summary>
        public void SetInteraction(float param)
        {
            _interactionParam = param;
            if (param > 0.001f) _interacted = true;
        }

        /// <summary>
        /// Interaktionsgrad 0..1 aus dem Abstand der Saber-Klinge (Basis→Spitze) zur Bogen-Kurve
        /// (Original `SliderInteractionManager.saberInteractionParam`): 1 = genau auf dem Band,
        /// 0 = außerhalb des Fangradius. Rechenarm (Stride-Stichproben, allokationsfrei).
        /// </summary>
        public float EvaluateInteraction(Vector3 saberBase, Vector3 saberTip, float radius)
        {
            if (!_moving || _line == null || _line.positionCount == 0 || radius <= 0f) return 0f;
            int count = _line.positionCount;
            int stride = Mathf.Max(1, count / 48);
            float best = float.MaxValue;
            for (int i = 0; i < count; i += stride)
                best = Mathf.Min(best, DistancePointSegment(saberBase, saberTip,
                    transform.TransformPoint(_line.GetPosition(i))));
            best = Mathf.Min(best, DistancePointSegment(saberBase, saberTip,
                transform.TransformPoint(_line.GetPosition(count - 1))));
            if (best >= radius) return 0f;
            return 1f - (best / radius);
        }

        private void ApplyInteractionColor(float intensity)
        {
            if (!_arcBaseColorsCached)
            {
                _arcBaseRed = RedMat.HasProperty("_BaseColor")
                    ? RedMat.GetColor("_BaseColor") : new Color(0.784f, 0.078f, 0.078f);
                _arcBaseBlue = BlueMat.HasProperty("_BaseColor")
                    ? BlueMat.GetColor("_BaseColor") : new Color(0.157f, 0.557f, 0.824f);
                _arcBaseColorsCached = true;
            }
            Color baseColor = ColorType == 0 ? _arcBaseRed : _arcBaseBlue;
            float k = Mathf.Max(intensity, 0f);
            var c = new Color(baseColor.r * k, baseColor.g * k, baseColor.b * k, baseColor.a);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
        }

        /// <summary>Abstand Punkt ↔ Strecke (a..b) — für die Klingen-Bogen-Nähe.</summary>
        private static float DistancePointSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float ab2 = ab.sqrMagnitude;
            if (ab2 < 1e-8f) return Vector3.Distance(p, a);
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab2);
            return Vector3.Distance(p, a + ab * t);
        }

        private static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        private static float QuadInOut(float x)
        {
            return x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f;
        }

        /// <summary>
        /// Enden-Fade als FESTE DISTANZ (ArcViewer `ArcHandler.SetGradient`): Start/Ende
        /// werden durch die Kurvenlänge geteilt, damit lange Bögen NICHT proportional länger
        /// ausblenden. Ein wiederverwendeter Gradient (kein GC je Bogen).
        /// </summary>
        private void ApplyEndFade(float curveLength)
        {
            float len = Mathf.Max(0.0001f, curveLength);
            // Auf 0.49 begrenzt: die Alpha-Keys müssen zeitlich sortiert bleiben.
            float fadeEnd = Mathf.Clamp(GameConfig.ArcEndFadeMeters / len, 0f, 0.49f);
            float fadeStart = Mathf.Clamp(GameConfig.ArcEndFadeStartMeters / len, 0f, fadeEnd);
            if (_fadeGradient == null) _fadeGradient = new Gradient();
            _fadeGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, fadeStart),
                    new GradientAlphaKey(1f, fadeEnd),
                    new GradientAlphaKey(1f, 1f - fadeEnd),
                    new GradientAlphaKey(0f, 1f - fadeStart),
                });
            _line.colorGradient = _fadeGradient;
        }

        private static Material RedMat => _redMat != null ? _redMat : (_redMat = BuildMaterial(new Color(0.784f, 0.078f, 0.078f)));
        private static Material BlueMat => _blueMat != null ? _blueMat : (_blueMat = BuildMaterial(new Color(0.157f, 0.557f, 0.824f)));

        private static Material BuildMaterial(Color color)
        {
            // 1:1 ArcViewer: `Arc.mat` (ArcShader) + `Arc.png`. Fallback: Unlit.
            var baseMat = Resources.Load<Material>("BeatSaberDemo/Materials/Arc");
            Material mat;
            if (baseMat != null)
            {
                mat = new Material(baseMat) { name = "Arc" };
            }
            else
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                mat = new Material(shader) { name = "Arc" };
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            var tex = Resources.Load<Texture2D>("BeatSaberDemo/Textures/Arc");
            if (tex != null)
            {
                if (mat.HasProperty("_Texture")) mat.SetTexture("_Texture", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            mat.renderQueue = 3000;
            mat.enableInstancing = true;
            return mat;
        }
    }
}
