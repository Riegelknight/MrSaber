using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace BeatSaber.Environment
{
    public enum BackgroundMode
    {
        /// <summary>Video-Seethrough an — der echte Raum ist sichtbar.</summary>
        Passthrough = 0,
        /// <summary>Song-Umgebung, ruhige Standardbeleuchtung.</summary>
        Default = 1,
        /// <summary>Song-Umgebung mit Lightshow.</summary>
        Animated = 2
    }

    /// <summary>
    /// Steuert die Hintergrund-Umgebung. LateUpdate (Order 1) läuft NACH dem GameManager
    /// (Default 0) und liest die Raum-Pose/-Skalierung in DIESEM Frame — einen Frame
    /// synchron zur gezeichneten Geometrie.
    /// </summary>
    [DefaultExecutionOrder(1)]
    public sealed class EnvironmentDirector : MonoBehaviour
    {
        public BackgroundMode Mode { get; private set; } = BackgroundMode.Passthrough;

        // ArcViewer-Standardwerte für den Nebel (EnvironmentManager.EnvironmentParameters).
        private const float FogAttenuation = 1000f;
        private const float FogOffset = 0f;
        private const float FogHeight = 25f;
        private const float FogStartY = -80f;

        // ArcViewer-Qualitätsstufe 1 (512 px Höhe): reduziert die Kosten der zweiten Render-Pass.
        private const int BloomfogQuality = 1;

        private const float CaptureCamHeight = 2.0f;

        /// <summary>Layer der Laser-Glow-Quads (ArcViewer „LightGlow") — die EINZIGE
        /// Ebene, die die Bloomfog-Erfassung fotografiert (wie ArcViewer: Capture-Maske
        /// 2048). Struktur/Noten/Wände fluten sonst die Nebeltextur.</summary>
        private const int LightGlowLayer = 11;

        private Transform _root;
        private readonly List<GameObject> _loadedObjects = new List<GameObject>();
        private string _loadedEnvironment;
        private string _loadingEnvironment;
        private string _desiredEnvironment = EnvironmentParametersTable.DefaultEnvironmentName;
        private Coroutine _loadRoutine;

        private Volume _postVolume;
        private Camera _camera;
        private Camera _captureCamera;
        private RenderTexture _captureTexture;
        private ReflectionProbe _reflectionProbe;

        // Sonde hängt am Raum-Root und rendert neu, wenn sich dessen Pose/Skalierung
        // wesentlich geändert hat (nicht jede Frame).
        private Vector3 _probeAnchorPos;
        private Quaternion _probeAnchorRot;
        private Vector3 _probeAnchorScale;
        private bool _probeAnchorValid;
        private float _nextProbeRenderTime;

        // Merker für die RAUM-lokalen Shader-Globals (_RoomScale/_RoomAnchorPos): werden
        // nur bei Änderung gesetzt (keine unnötigen SetGlobal-Aufrufe pro Frame).
        private float _roomScaleGlobals = 1f;
        private float _roomYGlobals;

        // Layer, die die Reflexions-Sonde aufnimmt: Struktur (0) + Laser (10) + Glow (11).
        // Noten/Wände/HUD bewusst NICHT (statische Bühne → stabil, kein Flackern).
        private const int ReflectionCullingMask = (1 << 0) | (1 << 10) | (1 << 11);

        private void Awake()
        {
            var rootGo = new GameObject("[ArcViewer Environment]");
            rootGo.transform.SetParent(transform, false);
            _root = rootGo.transform;

            // Globals vorinitialisieren, damit kein Frame mit einer Null-Matrix sampelt
            // (Shader-Default wäre 0 -> NaN-UVs). Ersetzt jede Frame von UpdateCaptureMatrix.
            Shader.SetGlobalMatrix("_BloomfogCaptureVP", Matrix4x4.identity);
            Shader.SetGlobalMatrix("_BloomfogCaptureRP", Matrix4x4.identity);

            Bloomfog.Quality = BloomfogQuality;
            Shader.SetGlobalTexture("_BloomfogTex", Texture2D.blackTexture);
            Shader.SetGlobalFloat("_CustomFogAttenuation", 1f / FogAttenuation);
            Shader.SetGlobalFloat("_CustomFogOffset", FogOffset);
            Shader.SetGlobalFloat("_CustomFogHeightFogHeight", FogHeight);
            Shader.SetGlobalFloat("_CustomFogHeightFogStartY", FogStartY);

            // Raum-Metrik neutral vorinitialisieren (Skalierung 1, Raum-Y 0 = authored
            // ArcViewer-Look); LateUpdate hält sie ab dem ersten Frame aktuell.
            Shader.SetGlobalFloat("_RoomScale", 1f);
            Shader.SetGlobalVector("_RoomAnchorPos", Vector3.zero);

            EnsurePostFx();
            EnsureReflectionProbe(); // Beat-Saber-Look: gebackene Reflection Probes (Dump)
        }

        private void OnDestroy()
        {
            if (_captureTexture != null) _captureTexture.Release();
        }

        private void Update()
        {
            // Kamera transparent + Umgebung sichtbar ist immer falsch: sobald der echte
            // Raum durchkommt (Modus Passthrough ODER PICO-Seethrough-Schalter an), muss
            // die Umgebung verschwinden — unabhängig davon, WER den Zustand gesetzt hat.
            bool seeThrough = Mode == BackgroundMode.Passthrough || PICO.PicoSeeThrough.Enabled;
            if (!seeThrough) return;

            // (a) Bühnen-Root darf nie aktiv sein.
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);

            // (b) Alle Renderer unter dem Director (auch hypothetische, außerhalb
            //     von `_root` erzeugte) sind aus — deckt auch die frisch geladene
            //     Szene eines nachträglich beendeten Lade-Vorgangs ab.
            SetEnvironmentRenderers(false);

            // (c) Bloomfog-Erfassung + Reflexions-Sonde + Fake-Reflexionsstärke aus.
            SetBloomfogActive(false);
            try
            {
                if (_reflectionProbe != null && _reflectionProbe.enabled)
                {
                    _reflectionProbe.enabled = false;
                    Shader.SetGlobalFloat("_ProbeReflStrength", 0f);
                }
            }
            catch
            {
                // Sonde darf das Sicherheitsnetz nie blockieren.
            }

            // (d) Hauptkamera transparent halten (PICO-Pflicht: SolidColor Alpha 0,
            //     allowHDR aus) — auch gegen spätere Opak-Rücksetzer.
            EnforceTransparentCamera();
        }

        // ==================================================================
        // Raum-Synchronisation (LateUpdate, nach GameManager — DefaultExecutionOrder 1)
        // ==================================================================
        // Die Bühne ist Kind des Raum-Roots: Position/Drehung/Skalierung übernimmt sie
        // automatisch. Hier werden nur die EFFEKTE nachgezogen, die der Transform allein
        // nicht mitnehmen kann: die Nebel-Globals der Shader (Raum-Metrik) und die
        // Welt->Capture-VP der Erfassung. Die Sonde rendert bei Raum-Änderung neu.
        // ==================================================================
        private void LateUpdate()
        {
            if (_root == null) return;
            Vector3 pos = _root.position;

            // RAUM-lokale Nebel-Metrik: Faktor UND Bodenhöhe des Raums (Fog-Band/-Dämpfung
            // wachsen/schrumpfen mit der Bühne; bei Skalierung 1 und Raum-Y 0 = authored
            // ArcViewer-Verhalten).
            float scale = _root.lossyScale.x;
            if (Mathf.Abs(scale - _roomScaleGlobals) > 0.0001f
                || Mathf.Abs(pos.y - _roomYGlobals) > 0.0001f)
            {
                _roomScaleGlobals = scale;
                _roomYGlobals = pos.y;
                Shader.SetGlobalFloat("_RoomScale", scale);
                Shader.SetGlobalVector("_RoomAnchorPos", pos);
            }

            // Erfassungs-VP nur, wenn die Erfassung aktiv ist (sonst wäre die letzte
            // Matrix stehen geblieben; beim Abschalten setzt SetBloomfogActive Schwarz).
            if (_captureCamera != null && _captureCamera.enabled)
                UpdateCaptureMatrix();

            TrackReflectionProbe();
        }

        private void EnforceTransparentCamera()
        {
            FindCamera();
            if (_camera == null) return;
            if (_camera.clearFlags == CameraClearFlags.SolidColor
                && _camera.backgroundColor.a < 0.01f
                && !_camera.allowHDR)
            {
                return;
            }

            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _camera.allowHDR = false;
        }

        // ==================================================================
        // Öffentliche API (vom GameManager — Signaturen unverändert)
        // ==================================================================

        public void ApplyMode(BackgroundMode mode)
        {
            Mode = mode;
            bool opaque = mode != BackgroundMode.Passthrough;

            SetPostProcessing(opaque);
            SetCameraClear(opaque);
            if (_root != null) _root.gameObject.SetActive(opaque);
            SetEnvironmentRenderers(opaque);
            bool lit = mode != BackgroundMode.Passthrough;
            SetBloomfogActive(lit);

            // Reflexions-Sonde (echte Neon-Reflexion auf dem Boden), wie im Original.
            // Der Bodenshader sampelt `unity_SpecCube0`; `_ProbeReflStrength` schaltet ein/aus.
            try
            {
                if (_reflectionProbe != null) _reflectionProbe.enabled = lit;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BeatSaber] Reflexions-Sonde: " + ex.Message);
            }
            Shader.SetGlobalFloat("_ProbeReflStrength", lit ? 1f : 0f);

            if (!opaque)
            {
                EnvironmentLightManager.StaticLights = true;
                return;
            }

            if (_loadedEnvironment != _desiredEnvironment
                && _loadingEnvironment != _desiredEnvironment
                && _loadRoutine == null)
            {
                _loadingEnvironment = _desiredEnvironment;
                _loadRoutine = StartCoroutine(SwitchEnvironmentRoutine(_desiredEnvironment));
            }
            else
                EnvironmentLightManager.UpdateLightParameters();
        }

        /// <summary>Wählt die Umgebung (Map-Name); lädt sie bei Bedarf nach.</summary>
        public void SetEnvironment(string environmentName, Func<float, float> toSeconds)
        {
            _desiredEnvironment = EnvironmentParametersTable.Normalize(environmentName);
            EnvironmentClock.SetConverter(toSeconds);
            EnvironmentLightManager.CurrentParameters = EnvironmentParametersTable.Get(_desiredEnvironment);
            EnvironmentColorManager.SetPalette(EnvironmentColorTable.Get(_desiredEnvironment));

            if (Mode == BackgroundMode.Passthrough) return;
            if (_loadedEnvironment == _desiredEnvironment) return;
            if (_loadingEnvironment == _desiredEnvironment) return;
            if (_loadRoutine != null) return;
            _loadingEnvironment = _desiredEnvironment;
            _loadRoutine = StartCoroutine(SwitchEnvironmentRoutine(_desiredEnvironment));
        }

        public void AttachToRoom(Transform roomRoot)
        {
            if (_root == null || roomRoot == null) return;
            _root.SetParent(roomRoot, false);
            _root.localPosition = Vector3.zero;
            _root.localRotation = Quaternion.identity;
            _root.localScale = Vector3.one;
        }

        /// <summary>
        /// Hält die Reflexions-Sonde an der Raum-Pose (sie hängt als Kind am Raum-Root und
        /// übernimmt Pose + Skalierung von selbst) und rendert sie neu, wenn sich Pose ODER
        /// Skalierung wesentlich geändert haben. Da die Bühne relativ zur Sonde statisch
        /// bleibt (Self-Reflection), ist EIN Render pro wesentlicher Änderung korrekt —
        /// physisches Drehen des Spielers bewegt die Bühne NICHT (kein Neu-Render),
        /// Raum-Drehung/-Versatz im Menü, Raum-Folge oder Live-Skalierung dagegen schon.
        /// </summary>
        private void TrackReflectionProbe()
        {
            if (_reflectionProbe == null || !_reflectionProbe.enabled || _root == null) return;
            Vector3 pos = _root.position;
            Quaternion rot = _root.rotation;
            Vector3 scale = _root.lossyScale;
            bool changed = !_probeAnchorValid
                || (pos - _probeAnchorPos).sqrMagnitude > 0.05f * 0.05f
                || Quaternion.Angle(_probeAnchorRot, rot) > 0.5f
                || (scale - _probeAnchorScale).sqrMagnitude > 0.001f * 0.001f;
            if (!changed) return;
            if (Time.unscaledTime < _nextProbeRenderTime)
            {
                // Raum-Folge (Bühne bewegt sich pro Frame): Throttle aktiv. Pose-Merkwerte
                // erst beim tatsächlichen Render aktualisieren, damit nach Ablauf des
                // Throttles garantiert noch einmal neu gerendert wird.
                _probeAnchorValid = false;
                return;
            }
            _probeAnchorPos = pos;
            _probeAnchorRot = rot;
            _probeAnchorScale = scale;
            _probeAnchorValid = true;
            _nextProbeRenderTime = Time.unscaledTime + 0.15f;
            try
            {
                _reflectionProbe.RenderProbe();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BeatSaber] Reflexions-Sonde: Render fehlgeschlagen: " + ex.Message);
            }
        }

        /// <summary>Jeden Frame vom GameManager.</summary>
        public void Tick(float songTime, float songBeat)
        {
            EnvironmentClock.CurrentTime = songTime;
            EnvironmentClock.CurrentBeat = songBeat;
            if (Mode == BackgroundMode.Passthrough) return;

            if (Mode == BackgroundMode.Animated) EnvironmentLightManager.UpdateLights();
            if (_camera != null) _camera.backgroundColor = EnvironmentLightManager.CurrentBackground;
        }

        /// <summary>
        /// Lightshow der Map laden. Maps ohne Licht-Events bekommen die Auto-Lightshow,
        /// damit die Umgebung im Modus „Animiert" immer lebt.
        /// </summary>
        public void LoadLightshow(BeatmapChart chart, SongMeta meta, Func<float, float> toSeconds)
        {
            EnvironmentClock.SetConverter(toSeconds);

            EnvironmentColorManager.SetPalette(EnvironmentColorTable.Get(_desiredEnvironment));
            var palette = EnvironmentColorManager.CurrentColors;
            if (meta != null && meta.HasEnvironmentColors)
            {
                palette.LightColor1 = meta.EnvColorLeft;
                palette.LightColor2 = meta.EnvColorRight;
                palette.BoostLightColor1 = meta.EnvColorLeftBoost;
                palette.BoostLightColor2 = meta.EnvColorRightBoost;
            }

            EnvironmentLightManager.ResetAll();

            var types = new List<LightEventType>();
            var beats = new List<float>();
            var values = new List<int>();
            var floats = new List<float>();
            var hasColor = new List<bool>();
            var colors = new List<Color>();
            bool animated = Mode == BackgroundMode.Animated;

            if (animated && chart != null && chart.LightEvents != null)
            {
                foreach (var e in chart.LightEvents)
                {
                    types.Add((LightEventType)e._type);
                    beats.Add(e._time);
                    values.Add(e._value);
                    floats.Add(e._floatValue);
                    hasColor.Add(e._hasCustomColor);
                    colors.Add(e._customColor);
                }
            }

            var boostBeats = new List<float>();
            var boostVals = new List<bool>();
            if (animated && chart != null && chart.BoostEvents != null)
            {
                foreach (var b in chart.BoostEvents)
                {
                    boostBeats.Add(b._time);
                    boostVals.Add(b._boost);
                }
            }

            if (animated && types.Count == 0 && boostBeats.Count == 0)
            {
                float total = 0f;
                if (chart != null && chart._notes != null)
                    foreach (var n in chart._notes)
                        if (n._time > total) total = n._time;
                AutoLightshow.Populate(types, beats, values, floats, hasColor, colors,
                                       boostBeats, boostVals, total + 32f);
            }

            EnvironmentLightManager.LoadEvents(types, beats, values, floats, hasColor, colors,
                                               boostBeats, boostVals);

            if (Mode == BackgroundMode.Default) EnvironmentLightManager.StaticLights = true;
            EnvironmentLightManager.UpdateLightParameters();
        }

        // ==================================================================
        // Bloomfog-Erfassungskamera
        // ==================================================================
        private void EnsureCaptureCamera()
        {
            if (_captureCamera != null) return;

            var go = new GameObject("[Bloomfog Capture]");
            // Kind des Bühnen-Roots an lokaler Höhe CaptureCamHeight: Die Erfassung hängt
            // RAUM-FEST (dieselbe Pose) und skaliert mit dem Raum (Höhe = 2,0 × roomScale).
            go.transform.SetParent(_root, false);
            go.transform.localPosition = new Vector3(0f, CaptureCamHeight, 0f);
            go.transform.localRotation = Quaternion.identity;
            _captureCamera = go.AddComponent<Camera>();
            _captureCamera.enabled = false;
            _captureCamera.clearFlags = CameraClearFlags.SolidColor;
            _captureCamera.backgroundColor = Color.black;
            _captureCamera.allowHDR = true;
            _captureCamera.allowMSAA = false;
            _captureCamera.depthTextureMode = DepthTextureMode.None;
            // ArcViewer 1:1: die Erfassung fotografiert NUR die Glow-Ebene (die großen
            // weichen Laser-Glow-Quads). Ohne Maske landete die GESAMTE Szene (Noten,
            // Wände, Struktur) in der Nebeltextur — Kosten ohne Nutzen und falscher Look.
            _captureCamera.cullingMask = 1 << LightGlowLayer;

            var data = _captureCamera.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.allowXRRendering = false;   // mono, Hilfskamera
                data.renderPostProcessing = false;
            }
        }

        // ==================================================================
        // Reflexions-Sonde — Beat-Saber-Look (Dump: `BakedReflectionProbe`).
        // Realtime-Probe, aber ViaScripting + EINMALIG pro Umgebungs-Ladung gerendert
        // (128 px, nur die statische Bühne) — der performante VR-Weg statt SSR. Der
        // Bodenshader sampelt `unity_SpecCube0` (URP GlobalIllumination.hlsl).
        // ==================================================================
        private void EnsureReflectionProbe()
        {
            if (_reflectionProbe != null) return;
            try
            {
                var go = new GameObject("[Reflection Probe]");
                // Kind des Bühnen-Roots: Pose UND Größe folgen dem Raum (size 160×80×160
                // und center (0,12,0) sind lokal, skaliert mit roomScale).
                go.transform.SetParent(_root, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                var probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
                probe.resolution = 128;
                probe.cullingMask = ReflectionCullingMask;
                probe.size = new Vector3(160f, 80f, 160f);
                probe.center = new Vector3(0f, 12f, 0f);
                probe.nearClipPlane = 0.1f;
                probe.farClipPlane = 400f;
                probe.intensity = 1f;
                probe.enabled = false;
                _reflectionProbe = probe;
            }
            catch (Exception ex)
            {
                // Kein Spielbruch: ohne Probe bleibt der Boden beim Fake-Bloomfog-Glanz.
                _reflectionProbe = null;
                Debug.LogWarning("[BeatSaber] Reflexions-Sonde konnte nicht erstellt werden: " + ex.Message);
            }
        }

        /// <summary>Rendert die Reflexions-Sonde einmalig (nach dem Umgebungs-Laden).</summary>
        private void RefreshReflectionProbe()
        {
            if (_reflectionProbe == null || !_reflectionProbe.enabled) return;
            try
            {
                if (_root != null)
                {
                    // Die Sonde hängt als Kind am Raum-Root und steht damit bereits auf der
                    // Bühnen-Pose/-Skalierung. Merker setzen, damit TrackReflectionProbe die
                    // frisch gerenderte Reflexion nicht sofort erneut auslöst.
                    _probeAnchorPos = _root.position;
                    _probeAnchorRot = _root.rotation;
                    _probeAnchorScale = _root.lossyScale;
                    _probeAnchorValid = true;
                }
                _reflectionProbe.RenderProbe();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BeatSaber] Reflexions-Sonde: Render fehlgeschlagen: " + ex.Message);
            }
        }

        private void SetBloomfogActive(bool on)
        {
            if (!on)
            {
                Bloomfog.CaptureCamera = null;
                if (_captureCamera != null) _captureCamera.enabled = false;
                // Shader sofort neutral stellen (sonst bliebe die letzte Nebeltextur stehen).
                Shader.SetGlobalTexture("_BloomfogTex", Texture2D.blackTexture);
                return;
            }

            EnsureCaptureCamera();
            FindCamera();
            if (_captureTexture == null && _camera != null)
            {
                // Größe wie ArcViewer: Referenzhöhe × Seitenverhältnis der Hauptkamera.
                int refHeight = new[] { 256, 512, 1024, 2048, 4096 }[Mathf.Clamp(BloomfogQuality, 0, 4)];
                int width = Mathf.Max(16, Mathf.RoundToInt(refHeight * _camera.aspect));
                _captureTexture = new RenderTexture(width, refHeight, 0, RenderTextureFormat.DefaultHDR)
                {
                    name = "BloomfogCapture",
                    useMipMap = false,
                    autoGenerateMips = false
                };
                _captureTexture.Create();
            }
            if (_captureCamera != null)
            {
                _captureCamera.targetTexture = _captureTexture;
                _captureCamera.enabled = true;
            }
            Bloomfog.CaptureCamera = _captureCamera;
        }

        /// <summary>Jeden Frame (LateUpdate): VP der RAUM-festen Erfassung aktualisieren.
        /// Die Kamera selbst steht als Kind des Raum-Roots bereits korrekt — nur das
        /// Seitenverhältnis (von der Hauptkamera) und die Welt->Capture-VP-Matrix müssen
        /// pro Frame frisch sein.</summary>
        private void UpdateCaptureMatrix()
        {
            if (_captureCamera == null || _camera == null || _root == null) return;
            _captureCamera.aspect = _camera.aspect;

            // VP der (raumfesten) Erfassung: Welt -> Capture-NDC für die Shader-UVs. Die
            // Shader projizieren Weltpunkte mit `_BloomfogCaptureVP` in den Kamera-Sichtraum —
            // so bleiben Nebel und Fake-Reflexionen BÜHNENFEST: Raum-Drehung/-Versatz
            // (Joystick/Menü) dreht ALLES mit, Kopf-/Körperbewegung des Spielers dreht
            // NICHTS. Erfassungshöhe und Stage skalieren gemeinsam (beide unter dem Raum-Root).
            var vp = _captureCamera.projectionMatrix * _captureCamera.worldToCameraMatrix;
            Shader.SetGlobalMatrix("_BloomfogCaptureVP", vp);
            // Rotation Welt->Erfassung (Normalen-Projektion für den Reflexions-Versatz).
            Shader.SetGlobalMatrix("_BloomfogCaptureRP",
                Matrix4x4.TRS(Vector3.zero, _root.rotation, Vector3.one));
        }

        /// <summary>Schaltet ALLE Renderer unter dem EnvironmentDirector (Passthrough-Sicherung).</summary>
        private void SetEnvironmentRenderers(bool visible)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].enabled = visible;
            }
        }

        // ==================================================================
        // Szenen laden/entladen
        // ==================================================================
        private IEnumerator SwitchEnvironmentRoutine(string environmentName)
        {
            DestroyLoadedObjects();

            var load = SceneManager.LoadSceneAsync(environmentName, LoadSceneMode.Additive);
            if (load == null)
            {
                Debug.LogWarning($"[BeatSaber] Umgebungs-Szene '{environmentName}' nicht ladbar " +
                                 "(fehlt in den Build Settings?).");
                _loadingEnvironment = null;
                _loadRoutine = null;
                yield break;
            }

            while (!load.isDone) yield return null;

            var scene = SceneManager.GetSceneByName(environmentName);
            if (scene.IsValid() && scene.isLoaded)
            {
                if (_desiredEnvironment != environmentName)
                {
                    SceneManager.UnloadSceneAsync(scene);
                    _loadingEnvironment = null;
                    _loadRoutine = null;
                    TryStartPendingLoad();
                    yield break;
                }

                var managerScene = gameObject.scene;
                foreach (var go in scene.GetRootGameObjects())
                {
                    SceneManager.MoveGameObjectToScene(go, managerScene);
                    // worldPositionStays = FALSE ist der RICHTUNGS-Fix: Die Bühne hängt als
                    // Kind am Raum-Root (`_root` in `_gameRoot`, Identität) und übernimmt
                    // dessen Position + Gier + Skalierung; mit "false" bleibt der authored
                    // Lokaltransform erhalten. Mit "true" würde die authored WELT-Drehung
                    // eingefroren — dann dreht der Hintergrund NICHT mit den Noten
                    // (gemeldete Richtungs-Diskrepanz).
                    go.transform.SetParent(_root, false);
                    _loadedObjects.Add(go);
                }
                SceneManager.UnloadSceneAsync(scene);
            }

            _loadedEnvironment = environmentName;
            if (_root != null) _root.gameObject.SetActive(Mode != BackgroundMode.Passthrough);
            LogHierarchyOrphans();
            EnvironmentLightManager.UpdateLightParameters();
            RefreshReflectionProbe();
            Debug.Log($"[BeatSaber] Umgebung geladen: {environmentName}.");
            _loadingEnvironment = null;
            _loadRoutine = null;
        }

        /// <summary>Startet den aktuell gewünschten Umgebungs-Ladevorgang, falls zulässig.
        /// Auch nach einem VERWORFENEN (überholten) Ladevorgang aufgerufen — dort zieht er
        /// den später gewünschten Namen nach, den SetEnvironment nur gemerkt hatte.</summary>
        private void TryStartPendingLoad()
        {
            if (Mode == BackgroundMode.Passthrough) return;
            if (_loadedEnvironment == _desiredEnvironment) return;
            if (_loadingEnvironment == _desiredEnvironment) return;
            if (_loadRoutine != null) return;
            _loadingEnvironment = _desiredEnvironment;
            _loadRoutine = StartCoroutine(SwitchEnvironmentRoutine(_desiredEnvironment));
        }

        private void LogHierarchyOrphans()
        {
            if (_root == null) return;
            int orphans = 0;
            string examples = "";
            for (int i = 0; i < _loadedObjects.Count; i++)
            {
                if (_loadedObjects[i] == null) continue;
                foreach (var renderer in _loadedObjects[i].GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || renderer.transform.IsChildOf(_root)) continue;
                    if (orphans < 3) examples += (orphans > 0 ? " | " : "") + TransformPath(renderer.transform);
                    orphans++;
                }
            }
            if (orphans == 0)
                Debug.Log($"[BeatSaber] Hierarchie ok: alle {_loadedObjects.Count} geladene Wurzel(n) " +
                          "hängen unter dem Raum-Anker.");
            else
                Debug.LogWarning($"[BeatSaber] {orphans} Renderer außerhalb des Raum-Ankers " +
                                 $"(drehen NICHT mit); Beispiele: {examples}");
        }

        // Kleiner Helfer: Hierarchie-Pfad eines Transforms für die Diagnose.
        private static string TransformPath(Transform t)
        {
            if (t == null) return "<null>";
            var parts = new System.Collections.Generic.List<string>();
            while (t != null)
            {
                parts.Add(t.name);
                t = t.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private void DestroyLoadedObjects()
        {
            for (int i = 0; i < _loadedObjects.Count; i++)
            {
                if (_loadedObjects[i] != null) Destroy(_loadedObjects[i]);
            }
            _loadedObjects.Clear();
            _loadedEnvironment = null;

            for (int i = 0; i < EnvironmentParametersTable.Supported.Length; i++)
            {
                var orphan = SceneManager.GetSceneByName(EnvironmentParametersTable.Supported[i]);
                if (orphan.IsValid() && orphan.isLoaded) SceneManager.UnloadSceneAsync(orphan);
            }
        }

        // ==================================================================
        // Kamera + Post-Processing (Bloom + Grading) — nur in Default/Animiert
        // ==================================================================
        private void EnsurePostFx()
        {
            if (_postVolume != null) return;

            var go = new GameObject("[Environment PostFX]");
            go.transform.SetParent(transform, false);
            _postVolume = go.AddComponent<Volume>();
            _postVolume.isGlobal = true;
            _postVolume.priority = 100f;
            _postVolume.weight = 0f;

            // PostProcessProfile 1:1 aus ArcViewer (Assets/Rendering/PostProcessProfile.asset):
            // Bloom-Schwelle 1,0 (NUR die übersteuerten Laser/Glows glühen — der HDR-Boost
            // kommt aus EnvironmentLightManager.LightEmission 6,5), Intensität 1,5,
            // sehr enger Scatter 0,1 (kleine harte Halos), 4 statt 6 Iterationen.
            // Tonemapping bleibt ABWESEND (ArcViewer: None) — HDR-Werte rollen ungeclampt ab.
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.5f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.1f);
            bloom.maxIterations.Override(4); // ArcViewer: kompakte Blur-Kette (URP 14: maxIterations statt skipIterations)
            bloom.highQualityFiltering.Override(false);

            // Farb-Regularie wie ArcViewer: dunkle Schatten, leicht gezogene Lichter,
            // minimaler Gamma-Lift — lässt die Neon-Farben „knallen".
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(1f, 1f, 1f, -0.3937f));
            smh.midtones.Override(new Vector4(1f, 1f, 1f, 0f));
            smh.highlights.Override(new Vector4(1f, 1f, 1f, -0.1575f));
            smh.shadowsStart.Override(0f);
            smh.shadowsEnd.Override(0.15f);
            var lgg = profile.Add<LiftGammaGain>(true);
            lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0.0591f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.25f);
            vignette.smoothness.Override(0.4f);
            _postVolume.profile = profile;
        }

        private void FindCamera()
        {
            if (_camera != null) return;
            _camera = Camera.main;
            if (_camera == null) _camera = FindObjectOfType<Camera>();
        }

        private void SetPostProcessing(bool on)
        {
            if (_postVolume != null) _postVolume.weight = on ? 1f : 0f;
            FindCamera();
            if (_camera == null) return;
            var data = _camera.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = on;
        }

        // Passthrough braucht transparenten Hintergrund (PICO-Pflicht, siehe PicoSeeThrough).
        private void SetCameraClear(bool opaque)
        {
            FindCamera();
            if (_camera == null) return;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = opaque
                ? EnvironmentLightManager.CurrentBackground
                : new Color(0f, 0f, 0f, 0f);
        }
    }
}
