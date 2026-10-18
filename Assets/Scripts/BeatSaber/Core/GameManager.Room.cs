// GameManager: Raum-Ausrichtung, Verankerung, Beenden.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace BeatSaber
{
    public sealed partial class GameManager
    {
        private void RecenterRoomOnHead()
        {
            if (_cam == null) return;

            Vector3 head = _cam.transform.position;

            // 1) Spieler-Anker auf das Head-Set (nur X/Z — ApplyRoomAnchor schiebt die Score-
            //    Zone um den Zonen-Abstand davor und führt Y weiter über _floorOffsetMeters,
            //    die Höhe bleibt also exakt so, wie sie gerade ist).
            _roomFollowOffset = new Vector3(head.x, 0f, head.z);

            // 2) Raum in Blickrichtung drehen: Euler(0, yaw, 0) bildet +Z auf
            //    (sin yaw, 0, cos yaw), also yaw = atan2(forward.x, forward.z).
            Vector3 look = _cam.transform.forward;
            look.y = 0f;
            if (look.sqrMagnitude > 1e-6f)
                _roomYawDeg = Mathf.Repeat(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, 360f);

            // Tiefen-Folge ab hier neu einmessen, damit der erste Frame nach dem Zentrieren
            // („Raum folgt Spieler", falls aktiv) keinen Sprung macht.
            _lastFollowHead = new Vector3(head.x, 0f, head.z);
            _followInit = true;

            Debug.Log($"[BeatSaber] Spieler-Anker gesetzt (X/Z = {head.x:0.00}/{head.z:0.00}, " +
                      $"{GameConfig.PlayerDistanceBehindZone * _roomScale:0.00} m hinter der Score-Zone, " +
                      $"Höhe unverändert), Blickrichtung {_roomYawDeg:0}°.");
        }

        // ======================================================================
        // Raum-Verankerung
        // ======================================================================

        /// <summary>
        /// Hält den Spielraum (`_gameRoot`, Eltern von Noten/Wänden/HUD) verankert. Läuft
        /// jeden Frame im <c>LateUpdate</c>. X/Z = Raum-Anker (<see cref="RecenterRoomOnHead"/>;
        /// bei aktiver Tiefen-Folge zusätzlich geführt von <see cref="UpdateRoomFollow"/>);
        /// Y = Session-Bodenversatz (linker Stick, <see cref="UpdateRoomAdjustment"/>).
        /// Es gibt bewusst KEINE Kopplung an die Kopfhöhe (kein Ursprungs-Ausgleich) — das hatte
        /// das Feld an die Kopfbewegung gehängt und beim Start abgesenkt.
        /// Gedreht wird mit dem Gierwinkel (`_roomYawDeg`, rechter Stick; Taste A = Blickrichtung).
        ///
        /// Bewusst wird hier NICHTS am XR-Rig geschrieben: Kamera und deren Eltern-Transform
        /// entstehen zur Laufzeit im PICO-SDK und gehören dem Tracking (siehe Datei-Header).
        /// </summary>
        private void ApplyRoomAnchor()
        {
            if (_gameRoot == null) return;

            // Manuelle Raum-Drehung (nur Gier/Y) — 0° = Noten-Feld zeigt nach world +Z.
            Quaternion rot = Quaternion.Euler(0f, _roomYawDeg, 0f);

            // _roomFollowOffset = Standort des SPIELERS (Welt X/Z). Der Raum-Ursprung (Score-
            // Zone, Z=0) liegt davor — in Raum-Vorwärtsrichtung um den Zonen-Abstand
            // (skaliert mit dem Raum). So steht der Spieler bei Start und nach jedem Recenter
            // exakt an der hinteren Position (Raum-Z = −Abstand), und der Raum dreht sich
            // beim Stick-Drehen um den Spieler statt um die Zone.
            Vector3 toZone = rot * Vector3.forward * (GameConfig.PlayerDistanceBehindZone * _roomScale);

            // X/Z = Spieler-Standort + Zonen-Abstand; Y = Session-Bodenversatz (linker Stick).
            Vector3 pos = new Vector3(
                _roomFollowOffset.x + toZone.x, _floorOffsetMeters, _roomFollowOffset.z + toZone.z);
            _gameRoot.SetPositionAndRotation(pos, rot);

            // Hintergrund folgt automatisch: Die Bühne (inkl. Erfassung + Sonde) hängt als
            // Kind an `_gameRoot` (Setup: AttachToRoom) — sie braucht hier KEINEN eigenen
            // Sync-Aufruf mehr. Im Passthrough bleibt sie deaktiviert
            // (`_root.SetActive`, siehe EnvironmentDirector.ApplyMode).
        }

        /// <summary>
        /// „Raum folgt Spieler" (Menüschalter, Tiefen-Folge): Ist die Option aktiv, wird der
        /// Spielraum um die VORWÄRTS-/RÜCKWÄRTS-Komponente der Kopfbewegung (Welt-X/Z, entlang
        /// der Raum-Vorwärtsachse) mitgeführt. Seitliche Bewegung wird NICHT gefolgt — der
        /// Spieler kann zur Seite wandern, der Abstand zur Noten-Ebene bleibt aber entlang der
        /// Vorwärtsachse konstant. Läuft im <c>LateUpdate</c> VOR <see cref="ApplyRoomAnchor"/>;
        /// nur die reale Kopfbewegung zählt (die Raumverschiebung verändert die Kopfposition nicht).
        /// </summary>
        private void UpdateRoomFollow()
        {
            if (_cam == null) return;

            Vector3 head = _cam.transform.position;
            Vector3 headXZ = new Vector3(head.x, 0f, head.z);
            if (!_followInit)
            {
                _lastFollowHead = headXZ;
                _followInit = true;
                return;
            }

            Vector3 delta = headXZ - _lastFollowHead;
            _lastFollowHead = headXZ;

            // Raum-Vorwärtsachse (Gier): nur deren Komponente wird gefolgt.
            Vector3 forward = Quaternion.Euler(0f, _roomYawDeg, 0f) * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;
            forward.Normalize();
            float signedForward = Vector3.Dot(delta, forward);

            // Run and Play: Vorwärtsgeschwindigkeit einspeisen, solange gespielt wird
            // (ohne Menü). Sonst Puffer verwerfen — nach Menü/Songwechsel frisch messen.
            if (_save.RunAndPlay && Phase == GamePhase.Playing && _menuSide == 0)
                _runAndPlay.Push(signedForward, Time.deltaTime);
            else
                _runAndPlay.Reset();

            if (!_save.RoomFollowsPlayer || delta.sqrMagnitude <= 1e-8f) return;

            // Im Run-and-Play-Modus Stillstands-Totzone: darunter friert auch der Raum ein.
            if (_save.RunAndPlay && Mathf.Abs(signedForward) < GameConfig.RunPlayDeadzoneMeters)
                return;

            // Nur die Vorwärts-/Rückwärts-Komponente (signed) wird gefolgt — bei
            // Rückwärts zieht das Feld mit dem Spieler mit (Abstand bleibt konstant).
            _roomFollowOffset += forward * signedForward;
        }

        /// <summary>
        /// Run and Play: Übersetzt den gepufferten Bewegungszustand in Audio-Pause/Rate.
        ///   Still    => Song pausiert vollständig (Noten stehen).
        ///   Backward => Gameplay stoppt SOFORT; das Feld zieht über UpdateRoomFollow mit
        ///               (Abstand Spieler↔Feld konstant, kein Zurück-Schummeln).
        ///   Forward  => Rate = v × 1/(roomScale·NJS) × Lauf-Tempo; Song läuft synchron.
        /// </summary>
        private void RunAndPlayUpdate()
        {
            if (!_save.RunAndPlay) return;

            if (_runAndPlay.State == RunPlayState.Forward)
            {
                float comfort = _player != null
                    ? Mathf.Clamp(_player.RunComfort, GameConfig.RunComfortMin, GameConfig.RunComfortMax)
                    : GameConfig.RunComfortDefault;
                float rate = RunAndPlay.ComputePlayRate(
                    _runAndPlay.SmoothedSpeed, _njs, _roomScale, comfort);
                _audio.SetRunPlayRate(rate);
                _audio.SetRunPlayPaused(false);
            }
            else
            {
                // Stillstand ODER Rückwärts: Song + Noten-Timeline sofort anhalten.
                _audio.SetRunPlayPaused(true);
            }
        }

        private void SetRunAndPlay(bool on)
        {
            if (_save.RunAndPlay == on) return;

            if (on)
            {
                _roomFollowBeforeRun = _save.RoomFollowsPlayer;
                _save.RoomFollowsPlayer = true;
            }
            else
            {
                _save.RoomFollowsPlayer = _roomFollowBeforeRun;
                _runAndPlay.Reset();
                _audio.ClearRunPlay();
            }
            _save.RunAndPlay = on;
            Save();
            if (_menuSide != 0) BuildMenuContent();
        }

        /// <summary>
        /// Setzt den Boden-Ursprung auf „Floor" (PICO-API UND OpenXR) und wartet auf die
        /// Bestätigung der Runtime. Zwei Wege, weil sie unterschiedlich greifen:
        ///  1) `PXR_System.SetTrackingOrigin(PxrTrackingOrigin.Floor)` — PICO-nativ (im
        ///     Editor No-op, weil die SDK-Hülle mit UNITY_ANDROID gekappt ist).
        ///  2) `XRInputSubsystem.TrySetTrackingOriginMode(Floor)` — Unity/OpenXR.
        ///
        /// <paramref name="mustSucceed"/> = true (Spielstart): der Modus wird IMMER gesetzt
        /// (erzwingt eine frische Boden-Ableitung) und bei Misserfolg so lange erneut versucht,
        /// bis Floor bestätigt ist („darf nicht fehlschlagen").
        /// false: nur setzen, wenn der Modus noch nicht Floor ist. Derzeit nicht verwendet —
        /// die A-Taste fasst den Tracking-Ursprung nicht an (sie setzt nur Raum-Anker X/Z
        /// unter das Head-Set und die Drehung auf die Blickrichtung, siehe RecenterRoomOnHead).
        /// </summary>
        private IEnumerator EnsureFloorOrigin(bool mustSucceed)
        {
            const float retryDelaySeconds = 0.5f;
            const int framesPerAttempt = 30;
            int attempts = 0;

            while (true)
            {
                attempts++;
                bool picoApiWirkt = !Application.isEditor;
                Unity.XR.PXR.PxrTrackingOrigin picoMode = Unity.XR.PXR.PxrTrackingOrigin.Eye;
                TrackingOriginModeFlags unityMode = TrackingOriginModeFlags.Unknown;
                bool bestaetigt = false;

                // --- Modus setzen (beide Hebel) ---
                // Beim SPIELSTART (mustSucceed) IMMER setzen — auch wenn die Runtime bereits
                // „Floor" meldet. Das erzwingt eine frische Boden-Ableitung, falls der Ursprung
                // zuvor (z. B. durch ein XR-Recenter) verschoben wurde. Bei mustSucceed=false
                // nur setzen, wenn der Modus noch nicht „Floor" ist (keine Rekalibrierung).
                if (picoApiWirkt)
                {
                    try
                    {
                        Unity.XR.PXR.PXR_System.GetTrackingOrigin(out picoMode);
                        if (mustSucceed || picoMode != Unity.XR.PXR.PxrTrackingOrigin.Floor)
                            Unity.XR.PXR.PXR_System.SetTrackingOrigin(Unity.XR.PXR.PxrTrackingOrigin.Floor);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[BeatSaber] Tracking-Origin (PICO): " + ex.Message);
                        picoApiWirkt = false;
                    }
                }
                foreach (var s in RunningInputSubsystems())
                {
                    if (mustSucceed || s.GetTrackingOriginMode() != TrackingOriginModeFlags.Floor)
                        s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                }

                // --- Nur beobachten, bis bestätigt (nicht erneut setzen) ---
                for (int i = 0; i < framesPerAttempt && !bestaetigt; i++)
                {
                    if (picoApiWirkt)
                    {
                        try
                        {
                            Unity.XR.PXR.PXR_System.GetTrackingOrigin(out picoMode);
                            if (picoMode == Unity.XR.PXR.PxrTrackingOrigin.Floor) bestaetigt = true;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning("[BeatSaber] Tracking-Origin (PICO): " + ex.Message);
                            picoApiWirkt = false;
                        }
                    }

                    foreach (var s in RunningInputSubsystems())
                    {
                        var mode = s.GetTrackingOriginMode();
                        if (mode == TrackingOriginModeFlags.Floor) { bestaetigt = true; unityMode = mode; }
                        else if (unityMode == TrackingOriginModeFlags.Unknown) unityMode = mode;
                    }

                    if (!bestaetigt) yield return null;
                }

                if (bestaetigt)
                {
                    // Erfolgreiche Boden-Erkennung: den manuellen Stick-Versatz zurücksetzen.
                    _floorOffsetMeters = 0f;
                    float headY = _cam != null ? _cam.transform.position.y : float.NaN;
                    Debug.Log($"[BeatSaber] Bodenniveau = Floor bestätigt (PICO: {picoMode}, " +
                              $"Unity: {unityMode}) — Kopf steht {headY:0.000} m über dem Boden; " +
                              $"Stick-Bodenversatz auf 0 gesetzt.");
                    yield break;
                }

                if (Application.isEditor || !mustSucceed)
                {
                    Debug.LogWarning($"[BeatSaber] Bodenniveau: Floor nicht bestätigt " +
                                     $"(PICO: {picoMode}, Unity: {unityMode}) — Höhe bleibt unverändert.");
                    yield break;
                }

                if (attempts % 10 == 0)
                    Debug.LogWarning($"[BeatSaber] Bodenniveau: Floor noch nicht bestätigt " +
                                     $"(Versuch {attempts}) — weiterer Versuch.");
                yield return new WaitForSeconds(retryDelaySeconds);
            }
        }

        /// <summary>
        /// Füllt <see cref="_subsystemScratch"/> mit den laufenden XR-Input-Subsystemen und
        /// gibt dieselbe Liste zurück. Bewusst allokationsfrei; die Liste wird vom Aufrufer
        /// vor dem nächsten Aufruf überschrieben und darf daher nicht gespeichert werden.
        /// </summary>
        private List<XRInputSubsystem> RunningInputSubsystems()
        {
            _subsystemScratch.Clear();
            SubsystemManager.GetSubsystems(_subsystemScratch);
            _subsystemScratch.RemoveAll(s => !s.running);
            return _subsystemScratch;
        }

        /// <summary>
        /// Raum-Ausrichtung per Joystick (nur außerhalb der Songliste — dort blättert der Stick
        /// durch die Lieder):
        ///  - rechter Stick links/rechts => Raum-Gierwinkel (<see cref="_roomYawDeg"/>),
        ///  - linker  Stick hoch/runter  => Session-Bodenversatz (<see cref="_floorOffsetMeters"/>).
        /// Der Bodenversatz beginnt bei JEDEM Spielstart bei 0 und wird NICHT gespeichert;
        /// Taste A ändert die Höhe nicht (A setzt Raum-X/Z unter das Head-Set und die Drehung
        /// auf die Blickrichtung). Die Totzone ist bewusst größer
        /// (<see cref="GameConfig.FloorOffsetDeadzone"/>), damit ein leichtes/versehentliches
        /// Auslenken nichts verschiebt.
        /// </summary>
        private void UpdateRoomAdjustment()
        {
            if (_songListOpen) return;

            float dt = Time.unscaledDeltaTime;

            // Rechter Stick links/rechts: Raum drehen.
            float yaw = ApplyStickDeadzone(_input.RightThumbX, GameConfig.RoomAdjustDeadzone);
            if (yaw != 0f)
            {
                _roomYawDeg = Mathf.Repeat(
                    _roomYawDeg + yaw * GameConfig.RoomYawDegPerSecond * dt, 360f);
            }

            // Linker Stick hoch/runter: Session-Bodenversatz — nur bei KLARER Auslenkung
            // (größere Totzone). Startet bei 0, nicht gespeichert, A fasst ihn nicht an.
            float floor = ApplyStickDeadzone(_input.LeftThumbY, GameConfig.FloorOffsetDeadzone);
            if (floor != 0f)
                _floorOffsetMeters += floor * GameConfig.FloorOffsetMetersPerSecond * dt;
        }

        /// <summary>
        /// Wendet eine Totzone auf einen Stick-Wert (−1..1) an und skaliert den Rest wieder
        /// auf 0..1, damit die Bewegung ab der Schwelle weich (ohne Sprung) einsetzt.
        /// </summary>
        private static float ApplyStickDeadzone(float value, float deadzone)
        {
            float mag = Mathf.Abs(value);
            if (mag <= deadzone) return 0f;
            return Mathf.Sign(value) * Mathf.InverseLerp(deadzone, 1f, mag);
        }

        private void Quit()
        {
            if (_savePending) FlushSave(); // offene (gebündelte) Änderungen vor dem Beenden sichern
            Debug.Log("[BeatSaber] Beende Spiel (Y/B lange gedrückt).");
            _keyboard = null;
            if (_audio != null) _audio.StopSong();
            Application.Quit();
        }
    }
}
// refresh-touch-9
