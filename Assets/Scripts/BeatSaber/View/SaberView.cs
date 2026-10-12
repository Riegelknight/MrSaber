using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace BeatSaber
{
    /// <summary>Schwert-/Handseite: Links = rot, Rechts = blau.</summary>
    public enum SaberSide { Left = 0, Right = 1 }

    /// <summary>
    /// Schwert: Geometrie (Klinge + Griff), Pose-Historie für die Treffererkennung,
    /// Klingenrichtungs-Historie für Anlauf/Durchzug (wie OpenSaberPlus `LightSaber.gd`),
    /// Ein-/Ausfahren und Trail.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class SaberView : MonoBehaviour
    {
        public SaberSide Side { get; private set; }
        public Material CrossSectionMaterial { get; private set; }
        public Transform Tip { get; private set; }
        public Transform BaseTrans { get; private set; }
        /// <summary>Raum-Root, in dem die Klingenrichtung gemessen wird (wie die Noten).</summary>
        public Transform Room { get; set; }

        /// <summary>Ankerpunkt für die Grifflabel-Beschriftung.</summary>
        public Transform LabelAnchor { get; private set; }

        /// <summary>Klingen-Radius und Enden in Root-Koordinaten (aus den echten Blade-Mesh-Bounds).</summary>
        public float BladeClashRadiusBase { get; private set; }
        public Vector3 BladeClashBaseLocal { get; private set; }
        public Vector3 BladeClashTipLocal { get; private set; }

        private MeshRenderer _bladeRenderer;
        private Transform _extendPivot;
        private SaberTrail _trail;

        private Vector3 _baseScale = new Vector3(0.75f, 0.5f, 0.75f);
        private float _scaleMultiplier = 1f;

        public void SetScaleMultiplier(float s)
        {
            _scaleMultiplier = Mathf.Max(0.01f, s);
            transform.localScale = _baseScale * _scaleMultiplier;
        }

        // ---- Ein-/Ausfahren ----
        public bool Retracted { get; private set; }
        private float _retractTarget = 1f;
        private const float RetractBladeScale = 0.02f;
        private const float RetractLerpSpeed = 14f;

        // ---- Pose-Historie (LateUpdate, also genau die gezeichnete Pose) ----
        private Vector3 _prevBasePos, _prevTipPos;
        private Vector3 _curBasePos, _curTipPos;
        private float _prevSampleTime, _curSampleTime;
        private bool _hasPrevPose, _hasCurPose;

        public Vector3 PreviousBasePosition => _prevBasePos;
        public Vector3 PreviousTipPosition => _prevTipPos;
        public Vector3 CurrentBasePosition => _curBasePos;
        public Vector3 CurrentTipPosition => _curTipPos;
        public bool HasCurrentPose => _hasCurPose;
        public bool HasPreviousPose => _hasPrevPose && _hasCurPose;
        /// <summary>Score-Uhr-Abstand der beiden Posen (&lt;= 0 wenn ungültig).</summary>
        public float SampleTimeDelta => _curSampleTime - _prevSampleTime;

        // ---- Schwung-Bewertung: Klingenrichtungs-Historie ----
        // 1:1 OpenSaberPlus (`LightSaber.gd`): Anlauf/Durchzug = größte Änderung der
        // KLINGENRICHTUNG in einem Zeitfenster (±200 ms) um den Treffer — ebenen- und
        // richtungsunabhängig, ohne Sättigung (deshalb gerade ≙ diagonal).
        private const int DirHistorySize = 64;   // ≈ 1 s bei 72 Hz
        private readonly float[] _dirTime = new float[DirHistorySize];
        private readonly Vector3[] _dirVec = new Vector3[DirHistorySize];
        // Raum-lokale Klingenpositionen parallel zur Richtung — für die MITTE-Bewertung
        // (nächster XY-Vorbeilauf der Klinge an der Notenmitte über das Messfenster).
        private readonly Vector3[] _dirBase = new Vector3[DirHistorySize];
        private readonly Vector3[] _dirTip = new Vector3[DirHistorySize];
        private int _dirCount;   // gültige Einträge
        private int _dirWrite;   // nächster Schreibindex

        /// <summary>Raum-lokale Klingenrichtung der aktuellen Pose.</summary>
        public Vector3 CurrentBladeDirectionLocal { get; private set; }

        /// <summary>
        /// Größte Änderung (Grad) der Klingenrichtung im Score-Uhr-Fenster
        /// [<paramref name="fromTime"/>, <paramref name="toTime"/>] gegenüber
        /// <paramref name="reference"/>. 1:1 OpenSaberPlus `get_preswing_angle`
        /// (Fenster VOR dem Treffer) bzw. `get_followthrough_angle` (danach).
        /// </summary>
        public float MaxBladeDeviation(Vector3 reference, float fromTime, float toTime)
        {
            float max = 0f;
            for (int i = 0; i < _dirCount; i++)
            {
                int idx = _dirWrite - 1 - i;
                if (idx < 0) idx += DirHistorySize;
                float t = _dirTime[idx];
                if (t < fromTime || t > toTime) continue;
                float angle = Vector3.Angle(reference, _dirVec[idx]);
                if (angle > max) max = angle;
            }
            return max;
        }

        /// <summary>Merkt die Klingenrichtung + Positionen der aktuellen Pose auf der Score-Uhr.</summary>
        private void RecordBladeDirection()
        {
            Vector3 d = _curTipPos - _curBasePos;
            if (d.sqrMagnitude < 1e-10f) return;
            d = Room != null ? Room.InverseTransformDirection(d) : d;
            CurrentBladeDirectionLocal = d.normalized;

            _dirTime[_dirWrite] = _curSampleTime;
            _dirVec[_dirWrite] = CurrentBladeDirectionLocal;
            if (Room != null)
            {
                _dirBase[_dirWrite] = Room.InverseTransformPoint(_curBasePos);
                _dirTip[_dirWrite] = Room.InverseTransformPoint(_curTipPos);
            }
            else
            {
                _dirBase[_dirWrite] = _curBasePos;
                _dirTip[_dirWrite] = _curTipPos;
            }
            _dirWrite = (_dirWrite + 1) % DirHistorySize;
            if (_dirCount < DirHistorySize) _dirCount++;
        }

        private void ClearBladeHistory()
        {
            _dirCount = 0;
            _dirWrite = 0;
        }

        /// <summary>
        /// Nächster XY-Vorbeilauf der Klingen-LINIE an der Note (<paramref name="centerLocalXY"/>,
        /// Raum-Lokal, nur Bildschirm-Ebene) im Score-Uhr-Fenster [<paramref name="fromTime"/>,
        /// <paramref name="toTime"/>]. Liefert die kleinste 2D-Distanz (Basis-Einheiten). Damit
        /// ist die MITTE-Bewertung unabhängig vom Bewertungszeitpunkt: Gemessen wird, wie nah die
        /// Klinge der Notenmitte WIRKLICH kam — nicht, wo sie in einem einzelnen Frame stand.
        /// </summary>
        public float MinBladeCenterDeviation(Vector2 centerLocalXY, float fromTime, float toTime)
        {
            float min = float.MaxValue;
            for (int i = 0; i < _dirCount; i++)
            {
                int idx = _dirWrite - 1 - i;
                if (idx < 0) idx += DirHistorySize;
                float t = _dirTime[idx];
                if (t < fromTime || t > toTime) continue;
                Vector2 pa = new Vector2(_dirBase[idx].x, _dirBase[idx].y);
                Vector2 pb = new Vector2(_dirTip[idx].x, _dirTip[idx].y);
                Vector2 ab = pb - pa;
                float k = ab.sqrMagnitude > 1e-8f
                    ? Mathf.Clamp01(Vector2.Dot(centerLocalXY - pa, ab) / ab.sqrMagnitude)
                    : 0f;
                float dev = Vector2.Distance(centerLocalXY, pa + ab * k);
                if (dev < min) min = dev;
            }
            return min == float.MaxValue ? 0f : min;
        }

        // Geglättete SPITZEN-Geschwindigkeit (Welt, 5-Frame-Mittel) für Schnittebene/Prüfung.
        private readonly Vector3[] _tipVelSamples = new Vector3[5];
        private int _tipVelIdx;
        private Vector3 _tipVelocity;
        public Vector3 TipVelocity => _tipVelocity;

        public Vector3 GetBladeDirection()
        {
            if (Tip == null || BaseTrans == null) return transform.up;
            Vector3 d = Tip.position - BaseTrans.position;
            return d.sqrMagnitude > 0.0001f ? d.normalized : transform.up;
        }

        // ======================================================================
        // Aufbau
        // ======================================================================

        public static SaberView Build(SaberSide side, Transform anchor, Material crossSectionMaterial, Color glowColor)
        {
            var go = new GameObject(side == SaberSide.Left ? "SaberLeft" : "SaberRight");
            go.layer = GameConfig.SaberLayer;
            go.transform.SetParent(anchor, false);

            // Geometrie nach SuperSaber: Klinge zeigt nach vorn (+Z), Griff mittig um den Controller.
            // Handlage (PICO): 25° Hochneigung, 3 cm zurück entlang der Klingenachse.
            const float SaberPitchUp = 25f;
            const float SaberBackShift = 0.03f;
            go.transform.localPosition = new Vector3(0f, 0f, -SaberBackShift);
            go.transform.localRotation = Quaternion.Euler(90f - SaberPitchUp, 0f, 0f);
            go.transform.localScale = new Vector3(0.75f, 0.5f, 0.75f);

            var view = go.AddComponent<SaberView>();
            view.Side = side;
            view._baseScale = go.transform.localScale;

            // Grifflabel-Anker: achsenparallel, mit dem Kehrwert der Schwert-Skalierung.
            var labelGo = new GameObject("GripLabelAnchor");
            labelGo.transform.SetParent(go.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0f, -0.025f);
            labelGo.transform.localRotation = Quaternion.identity;
            labelGo.transform.localScale = new Vector3(1f / go.transform.localScale.x, 1f / go.transform.localScale.y, 1f / go.transform.localScale.z);
            view.LabelAnchor = labelGo.transform;

            // ArcViewer-Saber-Modell (Blade + Handle).
            Mesh bladeMesh = null, handleMesh = null;
            var saberModel = Resources.Load<GameObject>("BeatSaberDemo/Models/Saber");
            if (saberModel != null)
            {
                foreach (var mf in saberModel.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.name == "Blade") bladeMesh = mf.sharedMesh;
                    else if (mf.name == "Handle") handleMesh = mf.sharedMesh;
                }
            }
            Material saberBladeMat = MakeSaberMaterial("BeatSaberDemo/Materials/SaberBlade", glowColor, "SaberBlade");
            Material saberHandleMat = MakeSaberMaterial("BeatSaberDemo/Materials/SaberHandle", glowColor, "SaberHandle");

            // Griff.
            var grip = new GameObject("Grip");
            grip.layer = GameConfig.SaberLayer;
            grip.transform.SetParent(go.transform, false);
            grip.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
            grip.transform.localScale = new Vector3(1.25f, 1.25f, 2.5f);
            if (handleMesh != null)
            {
                grip.AddComponent<MeshFilter>().sharedMesh = handleMesh;
                var gripMr = grip.AddComponent<MeshRenderer>();
                gripMr.sharedMaterial = saberHandleMat;
                gripMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Griff-Oberseite schließen.
            var gripCap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            gripCap.name = "GripCap";
            gripCap.layer = GameConfig.SaberLayer;
            Destroy(gripCap.GetComponent<Collider>());
            gripCap.transform.SetParent(go.transform, false);
            gripCap.transform.localPosition = new Vector3(0f, 0.19f, 0f);
            gripCap.transform.localScale = new Vector3(0.05f, 0.03f, 0.05f);
            var capMr = gripCap.GetComponent<MeshRenderer>();
            if (capMr != null)
            {
                capMr.sharedMaterial = MakeFlatMaterial(new Color(0.10f, 0.10f, 0.11f), "GripCap");
                capMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Klinge am Einfahr-Pivot (Basisende).
            var pivot = new GameObject("BladePivot");
            pivot.transform.SetParent(go.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            view._extendPivot = pivot.transform;

            var blade = new GameObject("Blade");
            blade.layer = GameConfig.SaberLayer;
            blade.transform.SetParent(pivot.transform, false);
            blade.transform.localRotation = Quaternion.Euler(270f, 0f, 0f);
            blade.transform.localScale = new Vector3(1.125f, 1.125f, 2.0f);
            blade.transform.localPosition = new Vector3(0f, -0.26f, 0f);
            if (bladeMesh != null)
            {
                blade.AddComponent<MeshFilter>().sharedMesh = bladeMesh;
                var bladeMr = blade.AddComponent<MeshRenderer>();
                bladeMr.sharedMaterial = saberBladeMat;
                bladeMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                view._bladeRenderer = bladeMr;
            }

            // Kollisions-Geometrie für Schwert↔Schwert/Wand aus dem Blade-Mesh.
            if (bladeMesh != null)
            {
                var b = bladeMesh.bounds;
                float half = (b.extents.x + b.extents.y) * 0.5f;
                view.BladeClashRadiusBase = Mathf.Max(0.004f, half * 1.125f);
                view.BladeClashBaseLocal = new Vector3(0f, b.min.z * 2f - 0.06f, 0f);
                view.BladeClashTipLocal = new Vector3(0f, b.max.z * 2f - 0.06f, 0f);
            }
            else
            {
                view.BladeClashRadiusBase = GameConfig.SaberBladeRadius;
                view.BladeClashBaseLocal = new Vector3(0f, 0.1f, 0f);
                view.BladeClashTipLocal = new Vector3(0f, 2.1f, 0f);
            }

            // Schwert-Spitze und -Basis (Root-lokal): Basis 0,1 m, Spitze 2,1 (= 0,05 bzw. 1,05 m Welt).
            var tipGo = new GameObject("Tip");
            tipGo.transform.SetParent(go.transform, false);
            tipGo.transform.localPosition = new Vector3(0f, 2.1f, 0f);
            var baseGo = new GameObject("Base");
            baseGo.transform.SetParent(go.transform, false);
            baseGo.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            view.Tip = tipGo.transform;
            view.BaseTrans = baseGo.transform;

            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(go.transform, false);
            view._trail = SaberTrail.Attach(trailGo, baseGo.transform, tipGo.transform, MakeSaberTrailMaterial(glowColor));

            view.CrossSectionMaterial = crossSectionMaterial;
            return view;
        }

        // ======================================================================
        // Frame-Kern
        // ======================================================================

        private void Update()
        {
            if (!enabled) return;
            AnimateRetract();
        }

        private void LateUpdate()
        {
            if (!enabled) return;
            if (Retracted || Tip == null || BaseTrans == null)
            {
                InvalidatePose();
                ClearBladeHistory();
                return;
            }

            CapturePose();
            UpdateVelocity();
            RecordBladeDirection();
        }

        private void CapturePose()
        {
            _prevBasePos = _curBasePos;
            _prevTipPos = _curTipPos;
            _prevSampleTime = _curSampleTime;
            _hasPrevPose = _hasCurPose;

            _curBasePos = BaseTrans.position;
            _curTipPos = Tip.position;
            _curSampleTime = GameManager.ScoreTime;
            _hasCurPose = true;
        }

        private void InvalidatePose()
        {
            _hasPrevPose = false;
            _hasCurPose = false;
            ClearTipVelocity();
        }

        /// <summary>Spitzen-Geschwindigkeit der aktuellen Pose (Welt, 5-Frame-Mittel).</summary>
        private void UpdateVelocity()
        {
            if (!_hasPrevPose) return;
            float dt = SampleTimeDelta > 0.0001f ? SampleTimeDelta : Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            PushTipSample((_curTipPos - _prevTipPos) / dt);
        }

        private void PushTipSample(Vector3 v)
        {
            _tipVelSamples[_tipVelIdx] = v;
            _tipVelIdx = (_tipVelIdx + 1) % _tipVelSamples.Length;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < _tipVelSamples.Length; i++) sum += _tipVelSamples[i];
            _tipVelocity = sum / _tipVelSamples.Length;
        }

        private void ClearTipVelocity()
        {
            Array.Clear(_tipVelSamples, 0, _tipVelSamples.Length);
            _tipVelIdx = 0;
            _tipVelocity = Vector3.zero;
        }

        // ======================================================================
        // Ein-/Ausfahren, Trail
        // ======================================================================

        public void SetRetracted(bool retracted)
        {
            if (Retracted == retracted) return;
            Retracted = retracted;
            _retractTarget = retracted ? RetractBladeScale : 1f;
            if (retracted)
            {
                InvalidatePose();
                ClearBladeHistory();
            }
            if (_trail != null)
            {
                _trail.Clear();
                _trail.SetVisible(!retracted);
            }
        }

        public void SetTrailActive(bool active)
        {
            if (_trail == null) return;
            _trail.Clear();
            _trail.SetVisible(active && !Retracted);
        }

        private void AnimateRetract()
        {
            if (_extendPivot == null) return;
            if (!Mathf.Approximately(_extendPivot.localScale.y, _retractTarget))
            {
                float s = Mathf.MoveTowards(_extendPivot.localScale.y, _retractTarget,
                    Time.unscaledDeltaTime * RetractLerpSpeed);
                var ls = _extendPivot.localScale;
                ls.y = s;
                _extendPivot.localScale = ls;
            }
            if (_bladeRenderer != null)
                _bladeRenderer.enabled = !(Retracted && _extendPivot.localScale.y <= RetractBladeScale + 0.001f);
        }

        // ======================================================================
        // Materialien
        // ======================================================================

        private static Material MakeFlatMaterial(Color color, string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        private static Material MakeSaberMaterial(string resourcePath, Color color, string name)
        {
            var baseMat = Resources.Load<Material>(resourcePath);
            Material mat;
            if (baseMat != null) mat = new Material(baseMat) { name = name };
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                mat = new Material(shader) { name = name };
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        private static Material MakeSaberTrailMaterial(Color color)
        {
            var baseMat = Resources.Load<Material>("BeatSaberDemo/Materials/SaberTrail");
            Material mat;
            if (baseMat != null) mat = new Material(baseMat) { name = "SaberTrail" };
            else
            {
                var shader = Shader.Find("Sprites/Default");
                mat = new Material(shader) { name = "SaberTrail" };
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (mat.HasProperty("_Brightness")) mat.SetFloat("_Brightness", 1f);
            var tex = Resources.Load<Texture2D>("BeatSaberDemo/Textures/DefaultTrail");
            if (tex != null)
            {
                if (mat.HasProperty("_TrailTexture")) mat.SetTexture("_TrailTexture", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            return mat;
        }
    }

    [DefaultExecutionOrder(-500)]
    public sealed class XrPoseAnchorFollower : MonoBehaviour
    {
        public XRNode Node;

        private readonly List<InputDevice> _devices = new(2);

        private void Update()
        {
            _devices.Clear();
            InputDevices.GetDevicesAtXRNode(Node, _devices);
            for (int i = 0; i < _devices.Count; i++)
            {
                var d = _devices[i];
                if (!d.isValid) continue;
                if (d.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 pos))
                    transform.position = pos;
                if (d.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rot))
                    transform.rotation = rot;
                break;
            }
        }
    }
}
