// ==============================================================================
//  PicoSeeThrough — Aktiviert das PICO Video-Seethrough (Passthrough) für MR und
//  hält den Guardian (Systemgrenze) per Watchdog dauerhaft deaktiviert. Setzt die
//  Kamera auf SolidColor mit Alpha 0 und allowHDR = false (PICO-Pflicht).
// ==============================================================================
//  Beim Ändern beachten:
//   - Passthrough funktioniert NUR bei deaktiviertem Guardian — der Watchdog (10 s)
//     erzwingt den Zustand dauerhaft, auch nach Resume/Fokus-Rückkehr/Session-Wechsel.
//   - ENTWICKLER-SCHALTER (Menü „Passthrough"): `Enabled` (Default true). Alle
//     automatischen Aufrufe (Init/Watchdog/Resume/Fokus/Session) wenden IMMER den
//     aktuellen Enabled-Zustand an — aus bleibt aus, ohne dass etwas ihn zurückreisst.
//   - Selbstinitialisierend ([RuntimeInitializeOnLoadMethod], eigenes
//     DontDestroyOnLoad-GameObject) — kein Inspector-Wiring nötig.
//   - Nur auf Android aktiv (UNITY_ANDROID-Guards um die nativen Aufrufe).
//  Kontext: Architektur und Versionsstände siehe PROJECT_NOTES.md (Projektwurzel);
//  SDK-Versionen: ProjectInfo.cs.
// ==============================================================================
using System.Collections;
using Unity.XR.PXR;
using UnityEngine;

namespace PICO
{
    /// <summary>
    /// Aktiviert das PICO "Video Seethrough" (Passthrough) für MR-Spiele im
    /// Beat-Saber-Stil und hält den Guardian (Systemgrenze) dauerhaft deaktiviert.
    ///
    /// Problem: Sobald die Grenze auf dem Headset wieder aktiv ist (z. B. in den
    /// erweiterten Entwicklereinstellungen eingeschaltet), blendet PICO das
    /// Passthrough aus — der Spieler sieht nur noch einen schwarzen Raum.
    /// Lösung: Die App erzwingt den Guardian-Zustand "aus" (SetGuardianSystemDisable)
    /// und hält ihn per Watchdog aus, damit das Passthrough immer sichtbar bleibt.
    ///
    /// Setzt den Kamera-Hintergrund auf Schwarz mit Alpha 0 (Pflicht laut PICO-Doku,
    /// sonst bleibt alles schwarz) und schaltet den PXR-Seethrough-Layer ein, sobald
    /// das XR-Runtime bereit ist. Der Zustand wird zusätzlich
    ///   - bei XR-Session-Statuswechseln (SessionStateChanged, Muster aus PXR_CameraSubsystem),
    ///   - nach dem Resume (Pause unterbricht Seethrough auf PICO) und beim Fokus-Rückkehr,
    ///   - periodisch (Watchdog)
    /// erneut durchgesetzt.
    ///
    /// Kein Inspector-Wiring nötig: Initialisiert sich selbst per
    /// [RuntimeInitializeOnLoadMethod] (eigenes DontDestroyOnLoad-GameObject).
    /// </summary>
    public static class PicoSeeThrough
    {
        /// <summary>Maximale Wartezeit, bis das XR-Runtime den Seethrough annimmt.</summary>
        public const float EnableTimeoutSeconds = 10f;

        internal const float EnableRetryIntervalSeconds = 0.5f;

        /// <summary>Intervall des Watchdogs, der Guardian aus + Passthrough an erzwingt.</summary>
        internal const float GuardWatchdogIntervalSeconds = 10f;

        /// <summary>
        /// Entwickler-Schalter (Menü „Passthrough", persistent in
        /// <see cref="BeatSaber.SaveData.PassthroughDisabled"/>): true = Video-Seethrough
        /// aktiv (der echte Raum ist sichtbar), false = aus (die App rendert normal auf
        /// schwarzem Hintergrund). Default true = bisheriges Verhalten.
        /// </summary>
        public static bool Enabled { get; private set; } = true;

        /// <summary>
        /// Setzt den Entwickler-Schalter und wendet ihn sofort an. Alle automatischen
        /// Aufrufe (Init/Watchdog/Resume/Fokus/Session) wenden danach ebenfalls NUR noch
        /// diesen Zustand an — ein „aus" wird also nicht vom Watchdog zurückgedreht.
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            ApplySeethrough(enabled);
            Debug.Log($"[PICO] Passthrough {(enabled ? "AN" : "AUS")} (Entwickler-Schalter).");
        }

        /// <summary>
        /// Erzwingt, dass der Guardian (Systemgrenze) deaktiviert ist. Auf PICO schaltet
        /// eine aktive Grenze das Passthrough ab (schwarzer Raum) — das wird hier proaktiv
        /// verhindert. Ist die Grenze bereits aus, passiert nichts.
        /// </summary>
        public static void ForceDisableGuardian()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!PXR_Boundary.GetEnabled())
            {
                return;
            }

            int result = PXR_Plugin.Boundary.UPxr_SetGuardianSystemDisable(true);
            Debug.Log($"[PICO] Guardian aktiv -> deaktiviert (SetGuardianSystemDisable = {result}).");
#endif
        }

        /// <summary>
        /// Setzt den PXR-Seethrough-Zustand unkonditioniert (auch beim Resume — PICO setzt
        /// Seethrough beim Pausieren zurück). Vor dem Einschalten wird immer der Guardian
        /// deaktiviert, da er den Passthrough blockiert. Zusätzlich wird das statische
        /// PXR_Manager-Flag gesetzt, damit dessen Resume-Routine ebenfalls "true" nachzieht.
        /// </summary>
        public static void ApplySeethrough(bool enabled)
        {
            // Kamera-Zustand gehört zur Passthrough-Kette: an = komplett transparent
            // (Compositor zeigt das Kamerabild), aus = opak schwarz (die
            // ArcViewer-Umgebung/Default-Hintergrund wird sichtbar).
            ApplyCameraState(enabled);

            if (!enabled)
            {
                // WICHTIG: Auch das statische PXR_Manager-Flag auf "aus" setzen. Dessen
                // eigene Resume-Routine (PXR_Manager.OnApplicationPause) ruft sonst
                // EnableVideoSeeThrough(<alter Wert>) auf und schaltet das Seethrough
                // beim nächsten Resume wieder ein. Danach zusätzlich direkt abschalten,
                // falls das Flag bereits false war (dann ist der Property-Setter ein No-op).
                PXR_Manager.EnableVideoSeeThrough = false;
                PXR_MixedReality.EnableVideoSeeThrough(false);
                return;
            }

            ForceDisableGuardian();
            PXR_MixedReality.EnableVideoSeeThrough(true);
            PXR_Manager.EnableVideoSeeThrough = true;
        }

        /// <summary>
        /// Setzt den Kamera-Hintergrund passend zum Hintergrund-Modus. Passthrough
        /// (Seethrough AN) erfordert SolidColor mit Alpha 0 und allowHDR = false
        /// (PICO-Pflicht); bei ausgeschaltetem Seethrough wird opak schwarz gezeichnet,
        /// damit die virtuelle Umgebung (Modi Default/Animiert) auf einem Grund steht.
        /// </summary>
        public static void ApplyCameraState(bool seethrough)
        {
            var cam = Camera.main;
            if (cam == null) return;

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = seethrough ? new Color(0f, 0f, 0f, 0f) : new Color(0f, 0f, 0f, 1f);
            // HDR nur außerhalb des Passthrough: PICO verlangt beim Seethrough HDR AUS;
            // in Default/Animiert bringt HDR das Bloom-Glühen der emissiven Umgebung.
            cam.allowHDR = !seethrough;
        }

        /// <summary>
        /// Aktiviert Fixed Foveated Rendering (PICO-Performance-Hebel): die Randbereiche
        /// werden mit geringerer Auflösung gerendert, das Zentrum bleibt scharf. Auf Android
        /// über die PICO-API; im Editor/auf anderen Plattformen passiert nichts.
        /// </summary>
        public static void ApplyFoveation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                Unity.XR.PXR.PXR_FoveationRendering.SetFoveationLevel(
                    Unity.XR.PXR.FoveationLevel.Med, false);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[PICO] Foveation konnte nicht gesetzt werden: " + ex.Message);
            }
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInit()
        {
            var go = new GameObject("[PICO] SeeThrough");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<PicoSeeThroughBehaviour>();
        }
    }

    internal sealed class PicoSeeThroughBehaviour : MonoBehaviour
    {
        private void OnEnable()
        {
            PXR_Plugin.System.SessionStateChanged += OnSessionStateChanged;
        }

        private void OnDisable()
        {
            PXR_Plugin.System.SessionStateChanged -= OnSessionStateChanged;
        }

        private void Start()
        {
            PicoSeeThrough.ApplyFoveation();
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(LateLatchingRoutine());
#endif
            StartCoroutine(InitRoutine());
            StartCoroutine(GuardianWatchdogRoutine());
        }

        /// <summary>
        /// Hängt die PICO-Late-Latching-Komponente an die Kamera. Die Kamera entsteht erst
        /// zur Laufzeit (PICO-SDK), deshalb kann das nicht im Editor/der Szene verdrahtet
        /// werden. Late Latching senkt die Motion-to-Photon-Latenz.
        /// </summary>
        private IEnumerator LateLatchingRoutine()
        {
            for (int i = 0; i < 40; i++)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    if (cam.GetComponent<Unity.XR.PXR.PXR_LateLatching>() == null)
                        cam.gameObject.AddComponent<Unity.XR.PXR.PXR_LateLatching>();
                    yield break;
                }
                yield return new WaitForSeconds(0.5f);
            }
        }

        /// <summary>
        /// PICO signalisiert den XR-Session-Status (2 = "läuft"). Sobald die Session
        /// (neu) gestartet ist, werden Guardian + SeeThrough erneut durchgesetzt —
        /// gleiches Muster wie im SDK-eigenen PXR_CameraSubsystem.
        /// </summary>
        private void OnSessionStateChanged(int state)
        {
            if (state != 2)
            {
                return;
            }

            // Immer den aktuellen Entwickler-Zustand anwenden (an ODER aus).
            PicoSeeThrough.ApplySeethrough(PicoSeeThrough.Enabled);
            PicoSeeThrough.ApplyFoveation();
            Debug.Log($"[PICO] SeeThrough nach Session-Statuswechsel neu gesetzt ({(PicoSeeThrough.Enabled ? "AN" : "AUS")}).");
        }

        private IEnumerator InitRoutine()
        {
            // 1. Kamera vorbereiten (transparent bei AN, opak bei AUS).
            PicoSeeThrough.ApplyCameraState(PicoSeeThrough.Enabled);
            if (Camera.main == null)
                Debug.LogWarning("[PICO] SeeThrough: Kein Camera.main gefunden — Kamera-Hintergrund nicht angepasst.");

            // Entwickler-Schalter aus (z. B. auf einem Save, der "aus" gespeichert hat):
            // gar nicht erst einschalten — der GameManager setzt den Wert nach dem Laden
            // des Saves erneut, hier also nur kein unnötiges An.
            if (!PicoSeeThrough.Enabled) yield break;

            // 2. Guardian aus + Seethrough aktivieren, bis es das XR-Runtime akzeptiert (0 = Erfolg).
            float elapsed = 0f;
            while (elapsed < PicoSeeThrough.EnableTimeoutSeconds)
            {
                // Zwischenzeitlich ausgeschaltet? Der GameManager wendet den gespeicherten
                // Hintergrund-Modus erst nach dem Save-Laden an (Default/Animiert -> AUS).
                // Ohne diese Prüfung schaltet die Schleife Seethrough nach diesem Ausschalten
                // trotzdem wieder an -> Kamera transparent, WÄHREND die Umgebung sichtbar ist
                // (gemeldetes „Bühne/Ringe im Passthrough"). Also jeden Durchlauf prüfen.
                if (!PicoSeeThrough.Enabled) yield break;
                PicoSeeThrough.ForceDisableGuardian();
                if (PXR_MixedReality.EnableVideoSeeThrough(true) == 0)
                {
                    // Statisches Flag setzen, damit PXR_Manager.OnApplicationPause beim
                    // Resume ebenfalls "true" nachzieht (nicht "false").
                    PXR_Manager.EnableVideoSeeThrough = true;
                    Debug.Log("[PICO] SeeThrough aktiviert (Video Seethrough), Guardian aus.");
                    yield break;
                }
                elapsed += PicoSeeThrough.EnableRetryIntervalSeconds;
                yield return new WaitForSeconds(PicoSeeThrough.EnableRetryIntervalSeconds);
            }

            Debug.LogWarning("[PICO] SeeThrough: Konnte nicht aktiviert werden (Timeout).");
        }

        /// <summary>
        /// Hält den Zustand "Guardian aus + Passthrough an" dauerhaft aufrecht — auch wenn
        /// die Grenze auf dem Headset während des Spiels wieder aktiviert wird.
        /// </summary>
        private IEnumerator GuardianWatchdogRoutine()
        {
            var wait = new WaitForSeconds(PicoSeeThrough.GuardWatchdogIntervalSeconds);
            while (true)
            {
                yield return wait;
                // Aktuellen Entwickler-Zustand durchsetzen (an ODER aus).
                PicoSeeThrough.ApplySeethrough(PicoSeeThrough.Enabled);
            }
        }

        private void OnApplicationPause(bool pause)
        {
            // PICO setzt Seethrough beim Pausieren zurück -> beim Resume neu setzen
            // (immer der aktuelle Entwickler-Zustand, nicht blind "an").
            if (!pause)
            {
                PicoSeeThrough.ApplySeethrough(PicoSeeThrough.Enabled);
                Debug.Log($"[PICO] SeeThrough nach Resume neu gesetzt ({(PicoSeeThrough.Enabled ? "AN" : "AUS")}).");
            }
        }

        private void OnApplicationFocus(bool focus)
        {
            // Zusätzlich abfangen, wenn man z. B. über das Systemmenü die Grenze umschaltet
            // und zurückkehrt (Focus-Rückkehr ohne vorherige Pause).
            if (focus)
            {
                PicoSeeThrough.ApplySeethrough(PicoSeeThrough.Enabled);
            }
        }
    }
}
