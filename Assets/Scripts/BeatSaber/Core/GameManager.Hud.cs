// GameManager: Grifflabel, HUD und Menü-Aufbau.
using System;
using UnityEngine;

namespace BeatSaber
{
    public sealed partial class GameManager
    {
        // ======================================================================
        // UI aufbauen
        // ======================================================================
        private void BuildUi()
        {
            var hudGo = new GameObject("FloorHUD");
            _hud = hudGo.AddComponent<HudDisplay>();
            if (_cam != null) _hud.Build(_cam);
            // Punkteanzeige gehört zum RAUM, nicht zum Spieler: fest an den Spielwelten-Root
            // gehängt => keine Billboard-/Kopf-Verfolgung. `_gameRoot` steht auf Y 0 = dem
            // vom Head-Set gemeldeten Boden, das HUD beginnt also auf Bodenhöhe.
            hudGo.transform.SetParent(_gameRoot, false);
            // DEUTLICH über der Bodenebene: Bei 0,08 lag die Unterkante der 45°-geneigten
            // Tafel praktisch AUF dem Boden (und die Combo-Zeile lief noch darunter) —
            // gemeldet als „Anzeige zur Hälfte im Boden". 0,30 m (× Raum-Skalierung) lässt
            // die ganze Scheibe frei stehen; bei ScaleMin 0,5 bleiben noch ~11 cm Freiheit.
            hudGo.transform.localPosition = new Vector3(0f, 0.30f, 0.7f);
            // Optik: um 45° nach hinten geneigt (Oberkante vom Spieler weg) — wie eine
            // erhöhte Ablage, damit der am Boden stehende Anzeiger bequem lesbar ist.
            hudGo.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);

            var anchor = _leftAnchor != null ? _leftAnchor : transform;
            var menuGo = new GameObject("HandMenu");
            _menu = menuGo.AddComponent<HandMenu>();
            _menu.Build(anchor, _cam);

            // 2) GRIFflabel ZULETZT und GEKAPSELT: reine Kosmetik. Ein Fehler hier wird
            //    abgefangen — Menü/HUD/Spiel laufen trotzdem.
            try
            {
                if (_cam != null)
                {
                    // EINGRAVIERT am Griff (nicht schwebend, kein Billboard). Fallback ohne
                    // Schwert: Beschriftung am Controller-Anker mit fester Hand-Position.
                    bool onNameGrip = _saberL != null && _saberL.LabelAnchor != null;
                    bool onBestGrip = _saberR != null && _saberR.LabelAnchor != null;
                    Transform nameParent = onNameGrip ? _saberL.LabelAnchor
                        : (_leftAnchor != null ? _leftAnchor : transform);
                    Transform bestParent = onBestGrip ? _saberR.LabelAnchor
                        : (_rightAnchor != null ? _rightAnchor : transform);
                    _labelName = CreateLabel(nameParent, "", true, onNameGrip);
                    _labelBest = CreateLabel(bestParent, "", false, onBestGrip);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BeatSaber] Grifflabel-Aufbau fehlgeschlagen (Spiel läuft weiter): "
                                 + ex.Message);
            }
        }

        // ---- Grifflabel: eingraviert (kein Billboard, kein Schweben) ----
        // Dunkle Gravur VORNE, helle Lichtkante dahinter — dadurch wirkt der Text in den
        // Griff eingelassen statt aufgedruckt/schwebend.
        private static readonly Color EngraveNameDark = new Color(0.12f, 0.02f, 0.02f, 0.95f);
        private static readonly Color EngraveNameLight = new Color(1f, 0.55f, 0.5f, 0.45f);
        private static readonly Color EngraveBestDark = new Color(0.02f, 0.05f, 0.16f, 0.95f);
        private static readonly Color EngraveBestLight = new Color(0.55f, 0.78f, 1f, 0.45f);

        private const int GripLabelFontSize = 64;
        private const float GripLabelCapHeight = 0.0275f;
        // Komprimierung quer zur Leserichtung (Schrift wird leicht schmaler gezogen, damit
        // z. B. „Riegholm" auf den kurzen Griff passt).
        private const float GripLabelWidthScale = 0.72f;
        private const float GripCurveRadius = 0.01875f;
        private const float GripLabelMaxArcDegrees = 150f;

        private GripCurvedLabel CreateLabel(Transform parent, string text, bool left, bool onGrip)
        {
            Color dark = left ? EngraveNameDark : EngraveBestDark;
            Color light = left ? EngraveNameLight : EngraveBestLight;

            Transform root;
            if (onGrip)
            {
                // Am Griff liegt der Nullpunkt exakt auf der Grifffläche; Position/Skalierung
                // kommen vom (achsenparallelen) Label-Anker des Schwerts. Die Wurzel dreht die
                // Leserichtung auf die Griffachse (90° links / 270° rechts, gespiegelt) und
                // komprimiert quer leicht. Die Schrifthöhe wickelt das Mesh um den Griff
                // (`curved: true`) — dadurch liegt die Schrift auf der Rundung statt flach.
                root = new GameObject("GripLabel").transform;
                root.SetParent(parent, false);
                root.localPosition = Vector3.zero;
                root.localRotation = Quaternion.Euler(0f, 0f, left ? 90f : 270f);
                root.localScale = new Vector3(GripLabelWidthScale, 1f, 1f);
                // Lichtkante zuerst: etwas tiefer/versetzt, damit die dunkle Gravur davor
                // liegt und nur die Kante hervorschaut.
                MakeLabelLayer(root, "LayerLight", light,
                    new Vector3(0.0006f, -0.0006f, 0.0005f), curved: true, sortOrder: 0);
            }
            else
            {
                // Fallback ohne Schwert: am Controller-Anker, feste Hand-Position, flach.
                root = new GameObject("HandLabel").transform;
                root.SetParent(parent, false);
                root.localPosition = new Vector3(left ? -0.02f : 0.02f, 0.06f, 0.05f);
                root.localRotation = Quaternion.identity;
                root.localScale = Vector3.one;
                MakeLabelLayer(root, "LayerLight", light,
                    new Vector3(0.0006f, -0.0006f, 0.0002f), curved: false, sortOrder: 0);
            }
            return MakeLabelLayer(root, "LayerDark", dark, Vector3.zero, curved: onGrip, sortOrder: 1);
        }

        private static GripCurvedLabel MakeLabelLayer(Transform parent, string name, Color color,
            Vector3 localPos, bool curved, int sortOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            var label = go.AddComponent<GripCurvedLabel>();
            label.font = BsUi.Font();
            label.fontSize = GripLabelFontSize;
            label.capHeight = GripLabelCapHeight;
            label.curveRadius = curved ? GripCurveRadius : 0f;
            label.maxArcDegrees = GripLabelMaxArcDegrees;
            label.color = color;
            label.sortOrder = sortOrder;
            return label;
        }

        /// <summary>Schreibt einen Text in ALLE Ebenen (Gravur + Lichtkante) des Labels.</summary>
        private static void SetLabelText(GripCurvedLabel main, string value)
        {
            if (main == null) return;
            main.SetText(value);
            var root = main.transform.parent;
            if (root == null) return;
            var layers = root.GetComponentsInChildren<GripCurvedLabel>(true);
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != main) layers[i].SetText(value);
        }

        /// <summary>
        /// Aktualisiert die eingravierten Grifflabel: links der Name des aktiven Spielers,
        /// rechts dessen Bestwert zum AKTUELLEN Song. Läuft jede Frame mit Text-Vergleich
        /// und greift damit auch sofort nach einem Rekord (ohne Songwechsel) — kein
        /// „Dirty"-Flag, kein Billboard/Drehen zum Kopf mehr.
        /// </summary>
        private void UpdateLabels()
        {
            if (_labelName == null || _labelBest == null) return;

            string name = _player != null ? Shorten(_player.Name, 12) : "";
            if (name != _labelNameText)
            {
                _labelNameText = name;
                SetLabelText(_labelName, name);
            }

            string best = "";
            if (_player != null && _current != null)
            {
                int b = _player.GetBest(_current.BestKey);
                if (b > 0) best = b.ToString("N0");
            }
            if (best != _labelBestText)
            {
                _labelBestText = best;
                SetLabelText(_labelBest, best);
            }
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

    }
}
