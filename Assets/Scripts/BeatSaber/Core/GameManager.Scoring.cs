// GameManager: Punkte, Combo, Schnitt-Anzeige.
using System;
using System.Collections;
using UnityEngine;

namespace BeatSaber
{
    public sealed partial class GameManager
    {
        // ======================================================================
        // Punkte / Combo
        // ======================================================================
        /// <summary>
        /// Wertet einen gültigen Schnitt um den MOMENT DES KLINGENKONTAKTS aus (nicht wie früher
        /// im Score-Zone-Moment, wenn die Note Z=0 erreicht): Der erste Kontakt ist der
        /// eigentliche Schnitt — die Klinge steht dann an der Note, Anlauf liegt davor,
        /// Durchzug dahinter. Wartet man stattdessen bis zur Score-Zone, ist die Klinge längst
        /// an der Note vorbeigezogen: Mitte≈0 und Durchzug≈0, obwohl perfekt geschnitten wurde
        /// (Geräte-Log: oft 85 statt 115, `center=0`).
        /// Bezugsrichtung ist die Klingenrichtung IM SCHNITTMOMENT (beim Kontakt erfasst);
        /// Anlauf/Durchzug = größte Änderung der Klingenrichtung im ±<see cref="GameConfig.SwingMeasureWindow"/>
        /// Fenster darum (OpenSaberPlus `LightSaber.gd`) — ebenen- und richtungsunabhängig,
        /// damit gerade und diagonale Schnitte gleicher Qualität gleich zählen.
        /// </summary>
        private IEnumerator ScoreAtCutAsync(
            SaberView saber, float cutTime, Vector3 reference, float preAngle,
            Vector3 noteLocal, Vector3 notePos, bool chainHead, int cutDirection)
        {
            if (_current == null) yield break;

            // Durchzug = größte Abweichung im Fenster NACH dem Schnitt (OpenSaberPlus
            // `followthrough_time`). Auf der Score-Uhr abwarten, mit Echtzeit-Sicherung
            // gegen Pause/Liedende.
            float postEnd = cutTime + GameConfig.SwingMeasureWindow;
            float realCap = Time.unscaledTime + GameConfig.SwingMeasureWindow * 3f + 0.25f;
            while (saber != null && saber.HasCurrentPose && ScoreTime < postEnd
                   && Time.unscaledTime < realCap)
                yield return null;
            float postAngle = saber != null && saber.HasCurrentPose
                ? saber.MaxBladeDeviation(reference, cutTime, postEnd)
                : 0f;

            // Mitte: nächster XY-Vorbeilauf der Klinge an der Notenmitte über das ganze
            // Messfenster — nicht ein einzelner Frame (der hing von der Treffer-Zone ab).
            Vector2 noteCenter = new Vector2(noteLocal.x, noteLocal.y);
            float centerScore = saber != null && saber.HasCurrentPose
                ? GameConfig.CenterMax * (1f - Mathf.Clamp01(
                    saber.MinBladeCenterDeviation(noteCenter,
                        cutTime - GameConfig.SwingMeasureWindow, postEnd) / GameConfig.CenterDistanceMax))
                : 0f;

            AwardSlice(notePos, preAngle, postAngle, centerScore, chainHead, cutDirection);
        }

        /// <summary>
        /// Punkte eines gültigen Notenschnitts (0..115, Original-Beat-Saber-Verteilung):
        /// Anlauf 70 (voll ab 100°), Durchzug 30 (voll ab 60°), Mitte 15 (voll in der
        /// Notenmitte) — linear dazwischen, ohne Totzone. Anlauf/Durchzug werden wie in
        /// OpenSaberPlus als Richtungsänderung gemessen (richtungsunabhängig).
        /// Chain-Kopf: kein Durchzug.
        /// Die Grundpunkte werden mit dem Combo-Multiplikator multipliziert.
        /// </summary>
        private void AwardSlice(Vector3 notePos, float preAngle, float postAngle, float centerScore, bool chainHead, int cutDirection)
        {
            int prePart = Mathf.RoundToInt(
                GameConfig.PreSwingMax * Mathf.Clamp01(preAngle / GameConfig.PreSwingFullAngle));
            int centerPart = Mathf.RoundToInt(Mathf.Clamp(centerScore, 0f, GameConfig.CenterMax));
            int postPart = chainHead ? 0 : Mathf.RoundToInt(
                GameConfig.FollowThroughMax * Mathf.Clamp01(postAngle / GameConfig.FollowThroughFullAngle));
            int baseScore = prePart + postPart + centerPart;

            if (GameConfig.LogHitDiagnostics)
                Debug.Log($"[Score] dir={cutDirection} chainHead={chainHead} pre={prePart} post={postPart} " +
                          $"center={centerPart} total={baseScore} preDeg={preAngle:0.0} postDeg={postAngle:0.0}");

            _combo++;
            // Multiplikator-Stufe exakt wie Original/ArcViewer (ScoringUtils.ManuallyAdvanceCombo):
            // Stufe steigt, sobald der Fortschritt die Trefferzahl der aktuellen Stufe erreicht;
            // die Note, die die Stufe erreicht, zählt bereits mit der NEUEN Stufe.
            if (_multTier < GameConfig.ComboMultipliers.Length - 1)
            {
                _comboProgress++;
                if (_comboProgress >= GameConfig.HitsNeededForComboIncrease[_multTier])
                {
                    _multTier++;
                    _comboProgress = 0;
                }
            }
            int multiplier = GameConfig.ComboMultipliers[_multTier];

            _score += (long)baseScore * multiplier;
            UpdateHud();
            TrySaveBestImmediately();

            // Referenz 1:1: Schnitt-Score (0..115) an der Noten-Position aufsteigen lassen
            ShowSliceScore(notePos, baseScore);
        }

        // ---------- Floating Score (Referenz: InGameUiView.DisplaySliceScore) ----------

        private void ShowSliceScore(Vector3 notePosition, int score, bool isMiss = false)
        {
            if (_cam == null) return;
            var tmp = GetScoreText();
            // LOKAL im Raum-Root rechnen: Der Root skaliert Stellung und Größe gemeinsam,
            // die hier addierten Offsets sind Basis-Einheiten (kein eigenes × Skalierung).
            Vector3 localNote = _gameRoot != null ? _gameRoot.InverseTransformPoint(notePosition) : notePosition;
            Vector3 pos = localNote
                + Vector3.forward * ScoreSpawnForwardOffset
                + Vector3.down * ScoreSpawnDownwardOffset;
            tmp.transform.localPosition = pos;

            // Billboard zum Spieler (nur Gier): Front zeigt von der Kamera weg.
            // worldPos wird über den Raum-Root transformiert — das funktioniert auch bei
            // gedrehtem Raum (`_roomYawDeg`). Die Welt-Rotation wird direkt
            // gesetzt und ersetzt dadurch die Root-Rotation des Textes.
            Vector3 worldPos = _gameRoot != null ? _gameRoot.TransformPoint(pos) : pos;
            Vector3 away = worldPos - _cam.transform.position;
            away.y = 0f;
            tmp.transform.rotation = away.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(away.normalized)
                : Quaternion.identity;
            StartCoroutine(FloatScoreRoutine(tmp, score, isMiss));
        }

        private TMPro.TextMeshPro GetScoreText()
        {
            if (_scoreTextPool.Count > 0)
            {
                var pooled = _scoreTextPool.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }
            var go = new GameObject("SliceScore");
            if (_gameRoot != null) go.transform.SetParent(_gameRoot, false);
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.font = TMPro.TMP_Settings.defaultFontAsset;
            tmp.fontSize = 1f;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 2f);
            tmp.color = Color.white;
            return tmp;
        }

        private IEnumerator FloatScoreRoutine(TMPro.TextMeshPro tmp, int score, bool isMiss)
        {
            tmp.text = isMiss ? "MISS" : score.ToString();
            Vector3 start = tmp.transform.localPosition; // lokal im Raum-Root (skaliert mit)
            Color c = tmp.color;
            float t = 0f;
            while (t < ScoreDisplayDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / ScoreDisplayDuration);
                // Aufsteigen: Ease.OutQuad, Verblasen: Ease.InQuad — wie die Referenz-Tweens.
                // Lokale Basis-Einheiten: Der Raum-Root skaliert die Distanz automatisch mit.
                tmp.transform.localPosition = start + Vector3.forward
                    * (ScoreFloatDistance * (1f - (1f - k) * (1f - k)));
                c.a = 1f - k * k;
                tmp.color = c;
                yield return null;
            }
            tmp.gameObject.SetActive(false);
            _scoreTextPool.Push(tmp);
        }

        /// <summary>Bestwert sofort sichern, sobald er übertroffen wird (nicht erst am Liedende).</summary>
        private void TrySaveBestImmediately()
        {
            if (_player == null || _current == null) return;
            int best = _player.GetBest(_current.BestKey);
            if (_score > best)
            {
                _player.SetBest(_current.BestKey, (int)Math.Min(_score, int.MaxValue));
                Save();
            }
        }

        private void OnNoteMissed(NoteView note)
        {
            if (note != null && note.Kind == NoteKind.Bomb) return; // ausgewichene Bombe ist KEIN Fehler
            if (note != null) ShowSliceScore(note.transform.position, 0, true);
            BreakCombo();
        }

        private void OnNoteComboBreak(NoteView note) => BreakCombo();

        private void BreakCombo(bool fullReset = false)
        {
            if (_combo == 0 && _multTier == 0 && _comboProgress == 0) return; // nichts zu verlieren
            _combo = 0;
            _comboProgress = 0;
            if (fullReset)
            {
                _multTier = 0;
            }
            else
            {
                // Verpasste Note/Fehler: Original-Verhalten — Combo-Verlust halbiert den
                // Multiplikator (x8 -> x4 -> x2 -> x1); danach zählt der Fortschritt der Stufe
                // wieder von vorn (ArcViewer: comboMult--, comboProgress = 0).
                if (_multTier > 0) _multTier--;
            }
            UpdateHud();
        }

        private void UpdateHud()
        {
            if (_hud == null) return;
            // Fortschritt = Treffer INNERHALB der aktuellen Stufe (ArcViewer: comboProgress /
            // HitsNeededForComboIncrease[Stufe]); x8 ist immer voll.
            float progress = 1f;
            if (_multTier < GameConfig.ComboMultipliers.Length - 1)
            {
                int needed = GameConfig.HitsNeededForComboIncrease[_multTier];
                progress = needed > 0 ? Mathf.Clamp01(_comboProgress / (float)needed) : 1f;
            }
            _hud.SetScore((int)Math.Min(_score, int.MaxValue), _combo,
                GameConfig.ComboMultipliers[_multTier], progress);
        }

        private void OnSongEnded()
        {
            if (_player != null && _current != null && _score > _player.GetBest(_current.BestKey))
            {
                _player.SetBest(_current.BestKey, (int)Math.Min(_score, int.MaxValue));
                Save(); // als "pending" vormerken — der Write passiert im LoadSong-Flush
            }
            // Nahtlos weiter: nächstes Lied (bei nur einem kommt es sofort wieder)
            LoadSong(_songIndex + 1);
        }

    }
}
