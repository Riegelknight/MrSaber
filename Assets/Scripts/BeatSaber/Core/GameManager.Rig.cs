// GameManager: Rig, Schwerter, Controller-Modelle, Haptik.
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace BeatSaber
{
    public sealed partial class GameManager
    {
        // ======================================================================
        // Template säubern (Coaching-UI, Spawner-Ball usw.)
        // ======================================================================
        private void SleepTemplateUi()
        {
            try
            {
                // Demo-/Coaching-Ballast deaktivieren (GoalManager/Fade bleibt aktiv!)
                DisableObjects("UI", "Coaching UI", "Hand Menu Setup", "Tutorial Player",
                    "Object Spawner", "Asset Spawner", "SpawnedObjectsManager");

                // Störende Template-KOMPONENTEN abschalten — NICHT deren GameObjects!
                // (Das „AR Session"-Objekt verwaltet die XR-Session; ein Deaktivieren des
                // Objekts kann die Session beenden -> PICO zeigt dann nur den Lade-Ring.)
                //  - HandVisualizer: ruft OpenXR ab, das hier nicht geladen ist
                //    (DllNotFoundException-Spam).
                //  - PermissionsCheck: fragt com.oculus.permission.USE_SCENE an (Meta) und
                //    wird auf PICO abgelehnt.
                var handVisualizer = FindObjectOfType<UnityEngine.XR.Hands.Samples.VisualizerSample.HandVisualizer>();
                if (handVisualizer != null) handVisualizer.enabled = false;
                var permissionsCheck = FindObjectOfType<UnityEngine.XR.Content.PermissionsCheck>();
                if (permissionsCheck != null) permissionsCheck.enabled = false;
                //  - JumpProvider (Starter-Assets-Rig unter „MR Interaction Setup“): die
                //    XRI-Action „Jump“ ist in „XRI Default Input Actions“ auf den
                //    PrimaryButton der rechten Hand = Taste A gebunden. Der Provider hebt
                //    den XR-Origin bis 1,25 m an — der eigentliche Grund für den „Sprung
                //    nach oben“ bei A. A gehört allein unserem RecenterRoomOnHead
                //    (Raum-X/Z unter den Kopf + Gier in Blickrichtung).
                foreach (var jump in FindObjectsOfType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump.JumpProvider>(true))
                    jump.enabled = false;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BeatSaber] Template-UI aufräumen: " + ex.Message);
            }
        }

        private static void DisableObjects(params string[] names)
        {
            foreach (var name in names)
            {
                var go = GameObject.Find(name);
                if (go != null) go.SetActive(false);
            }
        }

        // ======================================================================
        // Rig & Saber
        // ======================================================================
        private Transform FindRigAnchor(string name)
        {
            if (_cameraOffset != null)
            {
                var t = FindDescendant(_cameraOffset, name);
                if (t != null) return t;
            }
            var f = GameObject.Find(name);
            return f != null ? f.transform : null;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;
                var hit = FindDescendant(child, name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// Anker für eine Hand finden — unabhängig vom Rig-Typ:
        /// "Left Controller" (XRI-Starter), "LeftHand Controller" (MRTK 3, wie die
        /// Referenz-GameScene) oder "Left Hand" (XR-Hands-Rig der PICO-Vorlage).
        /// </summary>
        private Transform FindControllerAnchor(string side)
        {
            string[] candidates =
            {
                side + " Controller",      // XRI: "Left Controller" / "Right Controller"
                side + "Hand Controller",  // MRTK 3: "LeftHand Controller" / "RightHand Controller"
                side + " Hand"             // XR Hands-Vorlage: "Left Hand" / "Right Hand"
            };
            foreach (var name in candidates)
            {
                var t = FindRigAnchor(name);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Vollständiger Hierarchie-Pfad eines Ankers (Diagnose-Log beim Start).</summary>
        private static string AnchorPath(Transform t)
        {
            if (t == null) return "—";
            string path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private Transform CreateXrPoseAnchor(string objectName, XRNode node)
        {
            var go = new GameObject(objectName);
            var anchor = go.transform;
            anchor.SetParent(_cameraOffset != null ? _cameraOffset : null, false);
            var follower = go.AddComponent<XrPoseAnchorFollower>();
            follower.Node = node;
            return anchor;
        }

        // ======================================================================
        // Controller-Modelle ausblenden (Unity-seitige Meshes; Schwerter/UI bleiben)
        // ======================================================================
        private static readonly string[] ProtectedModelNames =
            { "Saber", "Blade", "Handle", "Trail", "PointerLaser", "Label" };

        /// <summary>
        /// Blendet alle Mesh-/SkinnedMesh-Renderer unter den Controller-Ankern aus,
        /// außer den selbst erzeugten Objekten (Schwerter, Laser, Labels).
        /// Hinweis: PICO 4 zeichnet Controller-Modelle im Video-Seethrough zusätzlich
        /// SYSTEM-seitig über das Kamerabild — dafür gibt es keine App-API.
        /// </summary>
        private void HideControllerModels()
        {
            if (_leftAnchor != null) HideRenderersUnder(_leftAnchor);
            if (_rightAnchor != null) HideRenderersUnder(_rightAnchor);
            DisableTemplatePointerRays();
        }

        private void DisableTemplatePointerRays()
        {
            // XRI 3.x: CurveVisualController steuert den LineRenderer des Interactors.
            var curves = FindObjectsOfType<CurveVisualController>(true);
            for (int i = 0; i < curves.Length; i++)
            {
                foreach (var lr in curves[i].GetComponentsInChildren<LineRenderer>(true))
                    lr.enabled = false;
                curves[i].enabled = false;
            }
            // Ältere XRI-Variante (falls im Rig vorhanden): XRInteractorLineVisual.
            var lineVisuals = FindObjectsOfType<XRInteractorLineVisual>(true);
            for (int i = 0; i < lineVisuals.Length; i++)
            {
                foreach (var lr in lineVisuals[i].GetComponentsInChildren<LineRenderer>(true))
                    lr.enabled = false;
                lineVisuals[i].enabled = false;
            }
        }

        private static void HideRenderersUnder(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (IsOwnObject(r.transform)) continue;
                r.enabled = false;
            }
        }

        private static bool IsOwnObject(Transform t)
        {
            // Debug-Controller (Tracking-Sichtprüfung/Kalibrierung) NIE ausblenden —
            // die Hide-Routine läuft mehrfach in den ersten Sekunden.
            for (var p = t; p != null; p = p.parent)
                if (p.GetComponent<ControllerDebugView>() != null) return true;
            int depth = 0;
            while (t != null && depth++ < 5)
            {
                for (int i = 0; i < ProtectedModelNames.Length; i++)
                {
                    if (t.name.StartsWith(ProtectedModelNames[i], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                t = t.parent;
            }
            return false;
        }

        /// <summary>
        /// PICO-/Interactor-Modelle können nach dem Start nachgeladen werden;
        /// daher in den ersten Sekunden mehrfach erneut ausblenden.
        /// </summary>
        private IEnumerator HideControllerModelsRoutine()
        {
            for (int i = 0; i < 7; i++)
            {
                yield return new WaitForSeconds(0.5f);
                HideControllerModels();
            }
        }

        private void BuildSabers()
        {
            if (_leftAnchor != null)
            {
                // Querschnittsmaterial (BeatSaberClone RedEmission) bleibt; Klinge/Griff/Trail
                // kommen aus ArcViewers Saber-Assets (SaberView lädt sie selbst).
                var cross = Resources.Load<Material>("BeatSaberDemo/Materials/RedEmission");
                _saberL = SaberView.Build(SaberSide.Left, _leftAnchor, cross,
                    new Color(0.7843137f, 0.07843138f, 0.07843138f));
                _saberL.Room = _gameRoot;
            }
            if (_rightAnchor != null)
            {
                var cross = Resources.Load<Material>("BeatSaberDemo/Materials/BlueEmission");
                _saberR = SaberView.Build(SaberSide.Right, _rightAnchor, cross,
                    new Color(0.1568627f, 0.5568627f, 0.8235294f));
                _saberR.Room = _gameRoot;
            }
        }

        private void SetSaberTrails(bool on)
        {
            if (_saberL != null) _saberL.SetTrailActive(on);
            if (_saberR != null) _saberR.SetTrailActive(on);
        }

        /// <summary>
        /// Easter Egg: Schwert ein-/ausfahren (Grip-Doppelklick der jeweiligen Hand).
        /// Im eingefahrenen Zustand sind Klingen-Cast und Trigger deaktiviert (SaberView),
        /// es lassen sich dann keine Noten treffen. SFX: Ausfahren = Hitsound-Klick,
        /// Einfahren = UI-Klick.
        /// </summary>
        private void ToggleSaberRetract(SaberSide side)
        {
            var saber = side == SaberSide.Left ? _saberL : _saberR;
            if (saber == null) return;
            if (_menuSide != 0) _menuManualChanged[(int)side] = true;
            bool nowRetracted = !saber.Retracted;
            saber.SetRetracted(nowRetracted);
            // Easter Egg: Ein- und Ausfahren bekommen DENSELBEN geklopften Slice-Sound
            // (vorne abgeschnitten, schnell, dumpfer) — er wirkt dadurch direkt wie ein
            // Schalter und nicht wie ein Schlag aufs Lichtschwert.
            _audio.PlaySaberToggleSfx();
            Debug.Log($"[BeatSaber] Lichtschwert {side} {(nowRetracted ? "eingefahren" : "ausgefahren")} (Easter Egg)");
        }

        private SaberView SaberFor(SaberSide side) => side == SaberSide.Left ? _saberL : _saberR;

        /// <summary>
        /// Fährt beim Öffnen des Menüs BEIDE Lichtschwerter automatisch ein. Dadurch
        /// bleibt als einziger „Beam" der Laser/Cursor der Gegenhand sichtbar; die Menüseite
        /// leuchtet nicht mehr (kein Klingen-/Trail-Leuchten, unabhängig vom Schwertstatus).
        /// </summary>
        private void AutoRetractSabersForMenu()
        {
            for (int i = 0; i < 2; i++)
            {
                _menuManualChanged[i] = false;
                _menuAutoRetracted[i] = false;
                var saber = SaberFor((SaberSide)i);
                if (saber == null) continue;
                if (!saber.Retracted) // nur einfahren, wenn gerade ausgefahren
                {
                    saber.SetRetracted(true);
                    _menuAutoRetracted[i] = true;
                }
            }
        }

        private void RestoreSabersAfterMenu()
        {
            for (int i = 0; i < 2; i++)
            {
                if (!_menuAutoRetracted[i] || _menuManualChanged[i]) continue;
                var saber = SaberFor((SaberSide)i);
                if (saber != null) saber.SetRetracted(false);
                _menuAutoRetracted[i] = false;
            }
        }

        private void HandleSaberHit(SaberView saber, NoteView note, Vector3 point, Vector3 normal, Vector3 vel)
        {
            if (Phase != GamePhase.Playing) return;
            Vector3 notePos = note.transform.position; // vor dem Verbrauch sichern (Referenz: Treffer-Position)
            Vector3 noteLocal = note.transform.localPosition; // Raum-Lokal, für die Mitte-Wertung
            bool chainHead = note.IsChainHead;
            bool consumed = note.TrySlice(saber.Side, point, normal, saber.CrossSectionMaterial, vel);
            if (!consumed) return;
            // Haptik je Objekt: Notenblöcke = etwas stärker als „leicht"; Minen = mittelstark
            // mit kurzer Nachvibration (GameConfig). Leichtes Kontakt-Feedback für
            // Schwert↔Schwert/Wand läuft fortlaufend in UpdateContactHaptics().
            if (note.Kind == NoteKind.Bomb)
                StartCoroutine(MineHapticsRoutine(saber.Side));
            else
                Haptics(saber.Side, GameConfig.HapticNoteAmplitude, GameConfig.HapticNoteDuration);
            Fx.PlayHit(point, saber.Side, vel,
                _gameRoot != null ? _gameRoot.forward : Vector3.forward, _roomScale);

            if (!note.WasValidSlice)
            {
                // Bombe / Fehler-Schnitt ("Bad Cut"): "MISS" anzeigen; Combo-Bruch kommt über
                // OnComboBreak. (Referenz: KEIN eigener Bad-Cut-Sound — nur der Schnitt-Sound.)
                ShowSliceScore(notePos, 0, true);
                return;
            }
            // Notenschnitt: der Slice-Sound, dumpf gefiltert und LEISE (Werte in
            // GameConfig: HitSfxPitch/HitSfxLowpassHz/HitSfxVolume) — das Feedback
            // kommt zusätzlich über die Haptik. HitSfxVolume = 0 = stumm.
            _audio.PlayHitSfx();

            // Bewertung ab dem SCHNITTMOMENT (Klingenkontakt), nicht erst in der Score-Zone:
            // Bezugsrichtung + Anlauf sofort erfassen, Durchzug/Mitte nach dem Messfenster.
            float cutTime = ScoreTime;
            Vector3 reference = saber.CurrentBladeDirectionLocal;
            float preAngle = saber.MaxBladeDeviation(reference,
                cutTime - GameConfig.SwingMeasureWindow, cutTime);
            StartCoroutine(ScoreAtCutAsync(
                saber, cutTime, reference, preAngle, noteLocal, notePos, chainHead, note.CutDirection));
        }

    }
}
