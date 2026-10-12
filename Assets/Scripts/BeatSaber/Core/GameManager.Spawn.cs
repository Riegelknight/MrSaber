// GameManager: Lied-Ablauf, Scheduler, Spawn von Noten, Wänden, Bögen.
using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber
{
    public sealed partial class GameManager
    {
        // ======================================================================
        // Spielablauf: Lied, Scheduler, Spawn
        // ======================================================================
        /// <summary>Mischt die Liederliste nach dem Einlesen (Fisher-Yates) — zufällige Reihenfolge am Start.</summary>
        private void ShuffleSongsAtStart()
        {
            for (int i = _songs.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                var tmp = _songs[i];
                _songs[i] = _songs[j];
                _songs[j] = tmp;
            }
            _songIndex = -1;
        }

        private void LoadSong(int index)
        {
            if (_songs.Count == 0) return;
            if (_savePending) FlushSave(); // garantiert: offene (gebündelte) Save-Änderungen vor Songwechsel sichern
            _songIndex = ((index % _songs.Count) + _songs.Count) % _songs.Count;
            _current = _songs[_songIndex];

            // Zustand zurücksetzen
            _score = 0; _combo = 0; _multTier = 0; _comboProgress = 0; _songEnded = false;
            _noteCursor = 0; _wallCursor = 0; _arcCursor = 0; _chainCursor = 0;
            _runAndPlay.Reset(); // Run-and-Play: frische Messung ab dem ersten Frame des Lieds
            _noteSchedule.Clear(); _wallSchedule.Clear(); _arcSchedule.Clear(); _chainSchedule.Clear();

            foreach (var w in _activeWalls) if (w != null) Destroy(w.gameObject);
            _activeWalls.Clear();
            NotePool.ReturnAll(); // Alte Noten (auch zerschnittene) sofort entsorgen — sonst fliegen sie ins neue Lied
            _activeNotes.Clear();
            ArcView.ReturnAll(_gameRoot);   // Bögen/Ketten ebenfalls ins neue Lied nicht mitnehmen
            ChainView.ReturnAll(_gameRoot);
            _activeArcs.Clear();
            _activeChains.Clear();

            _current.ApplySelectedDifficulty();
            _chart = BeatmapParser.ParseChart(_current);
            // Exakte Beat->Zeit-Umrechnung (BPM-Verlauf der Map; ohne Events = Info-BPM)
            _toSeconds = BeatmapParser.BuildBeatToSeconds(
                _chart.BpmEvents, _current != null ? _current.BeatsPerMinute : 120f);
            // Lightshow der Map an die Umgebung übergeben (nur Modus „Animiert").
            if (_environment != null)
            {
                _environment.SetEnvironment(_current?.EnvironmentName, _toSeconds);
                _environment.LoadLightshow(_chart, _current, _toSeconds);
            }
            ComputeJumpParameters();
            BuildSchedule();

            Phase = GamePhase.Boot;
            if (_audioLoad != null) StopCoroutine(_audioLoad);
            _audioLoad = StartCoroutine(_audio.PlaySong(_current, () =>
            {
                _audioLoad = null;
                Phase = GamePhase.Playing;
                _songEnded = false;
                UpdateHud();
            }));
        }

        private void ComputeJumpParameters()
        {
            float bpm = _current != null && _current.BeatsPerMinute > 0 ? _current.BeatsPerMinute : 120f;
            float spb = 60f / bpm;
            float njs = _current != null ? _current.NoteJumpMovementSpeed : 0f;
            if (njs <= 0.01f)
            {
                int rank = _current != null && _current.CurrentDifficulty != null
                    ? _current.CurrentDifficulty.Rank : 2;
                njs = GameConfig.DefaultNjsForRank(rank);
            }
            float offset = _current != null ? _current.NoteJumpStartBeatOffset : 0f;

            float hjd = GameConfig.HalfJumpDurationDefault;
            while (njs * 2f * spb * hjd > GameConfig.MaxJumpDistance) hjd *= 0.5f;
            hjd = Mathf.Max(hjd + offset, GameConfig.MinHalfJumpDuration);

            _njs = njs;
            _reactionTime = spb * hjd;              // ArcViewer: 60/BPM × HJD
            _halfJumpDistance = njs * _reactionTime;
            _spawnLeadTime = GameConfig.SpawnLeadTime(_reactionTime);
        }

        private void BuildSchedule()
        {
            if (_chart == null) return;
            var toSeconds = _toSeconds ?? (beats => BeatmapParser.BeatsToSeconds(beats, 120f));

            // Musterabhängige Positions-Effekte einmalig vorberechnen (Stapel/Flip).
            var byBeat = new Dictionary<float, List<NoteModel>>();
            foreach (var note in _chart._notes)
            {
                if (!byBeat.TryGetValue(note._time, out var group))
                {
                    group = new List<NoteModel>(4);
                    byBeat[note._time] = group;
                }
                group.Add(note);
            }
            var effects = new Dictionary<NoteModel, (float startYRel, float flipStartX, float flipYHeight)>(_chart._notes.Count);
            BuildNoteEffects(byBeat, effects);

            foreach (var note in _chart._notes)
            {
                float t = toSeconds(note._time);
                if (t < 0f) continue;
                // Spawn-Zeit = Beat-Zeit minus voller Sprungdauer (2 × ReactionTime);
                // Noten ganz am Liedanfang spawnen entsprechend näher.
                effects.TryGetValue(note, out var fx);
                _noteSchedule.Add((Mathf.Max(0f, t - _spawnLeadTime), note,
                    fx.startYRel, fx.flipStartX, fx.flipYHeight));
            }
            _noteSchedule.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            foreach (var obs in _chart._obstacles)
            {
                float t = toSeconds(obs._time);
                if (t < 0f) continue;
                // Dauer über den BPM-Verlauf auflösen (Wechsel mitten in der Wand -> exakt)
                float dur = toSeconds(obs._time + obs._duration) - t;
                _wallSchedule.Add((Mathf.Max(0f, t - _spawnLeadTime), obs, dur));
            }
            _wallSchedule.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            // Bögen (sliders): reine Optik, Spawn wie Noten/Wände 2× ReactionTime vor dem Kopf.
            // Anschluss-Erkennung (ArcViewer ObjectManager): Für jedes Bogen-Ende prüfen, ob
            // eine Note mit GLEICHER Zeit und GLEICHEM Rasterfeld existiert. Nur dann verschiebt
            // ArcView den Punkt um ArcAttachOffset (Bogen endet an der Notenkante).
            var noteAt = new HashSet<long>(_chart._notes.Count);
            foreach (var n in _chart._notes)
                noteAt.Add(NoteGridKey(n._time, n._lineIndex, n._lineLayer));
            foreach (var arc in _chart.Sliders)
            {
                float t = toSeconds(arc._headTime);
                if (t < 0f) continue;
                arc._headAttached = noteAt.Contains(NoteGridKey(arc._headTime, arc._headLineIndex, arc._headLineLayer));
                arc._tailAttached = noteAt.Contains(NoteGridKey(arc._tailTime, arc._tailLineIndex, arc._tailLineLayer));
                _arcSchedule.Add((Mathf.Max(0f, t - _spawnLeadTime), arc));
            }
            _arcSchedule.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            // Ketten (burstSliders): nur mit verknüpfter Kopfnote (v3/v4) spielbar.
            foreach (var chain in _chart.Chains)
            {
                if (chain._headNote == null) continue;
                float t = toSeconds(chain._time);
                if (t < 0f) continue;
                _chainSchedule.Add((Mathf.Max(0f, t - _spawnLeadTime), chain));
            }
            _chainSchedule.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        }

        private static long NoteGridKey(float beat, int line, int layer)
            => (long)Mathf.Round(beat * 1000f) * 10000L + line * 100L + layer;

        /// <summary>
        /// Berechnet die musterabhängigen Positions-Effekte je Note (1:1 ArcViewer
        /// ObjectManager/NOTE-MANAGER):
        ///  - STAPEL: Nur wenn zur selben Zeit mehrere Noten in DERSELBEN Spalte liegen,
        ///    bekommt die Note eine versetzte Start-Höhe (`StackIndex × StackSpacing`
        ///    unter der Container-Ebene) und gleitet in ihre Reihe. Normale Noten
        ///    (Spalte einmal belegt) starten auf ihrer Zielreihe — kein Effekt.
        ///  - FLIP: Genau zwei Farbnoten zur selben Zeit und überkreuzt (rot rechts von
        ///    Blau) ⇒ Start-X = Position der jeweils anderen Note und der vertikale
        ///    Über-/Unter-Bogen aus ArcViewer (`FlipYOver`/`FlipYUnder`).
        /// </summary>
        private void BuildNoteEffects(
            Dictionary<float, List<NoteModel>> byBeat,
            Dictionary<NoteModel, (float startYRel, float flipStartX, float flipYHeight)> result)
        {
            foreach (var group in byBeat.Values)
            {
                // Spaltenbelegung zur selben Zeit (für „ist das ein Stapel?").
                int[] perColumn = new int[4];
                foreach (var n in group)
                    perColumn[Mathf.Clamp(n._lineIndex, 0, 3)]++;

                foreach (var n in group)
                {
                    int row = Mathf.Clamp(n._lineLayer, 0, GameConfig.RowHeights.Length - 1);
                    // Start-Höhe RELATIV zur Zielreihe (Feldhöhen-unabhängig).
                    float startYRel = 0f;
                    if (perColumn[Mathf.Clamp(n._lineIndex, 0, 3)] > 1)
                    {
                        int index = StackIndex(n, group);
                        // Stapel-Animation: eine Rastereinheit unter der untersten Notenreihe
                        // starten und in die eigene Zielreihe gleiten.
                        float staggeredRel = GameConfig.RowHeights[0]
                                             + index * GameConfig.StackSpacing - GameConfig.StackSpacing
                                             - GameConfig.RowHeights[row];
                        // Nie über der eigenen Zielreihe starten (Sicherheitsnetz für
                        // ungewöhnliche Muster).
                        startYRel = Mathf.Min(0f, staggeredRel);
                    }
                    result[n] = (startYRel, 0f, 0f);
                }

                // Seiten-Flip: genau zwei Farbnoten auf diesem Beat (Bomben zählen nicht).
                var colorNotes = new List<NoteModel>(2);
                foreach (var n in group)
                    if (n._type == 0 || n._type == 1) colorNotes.Add(n);
                if (colorNotes.Count != 2) continue;

                // Deterministische Reihenfolge: links vor rechts (ArcViewer nutzt die
                // Datei-Reihenfolge; das Ergebnis ist für gleiche Reihen identisch).
                colorNotes.Sort((a, b) => a._lineIndex.CompareTo(b._lineIndex));
                NoteModel first = colorNotes[0], second = colorNotes[1];

                float f0 = 0f, f1 = 0f;
                if (first._type == second._type)
                {
                    // gleiche Farbe -> keine Überkreuzung
                }
                else if ((first._type == 0 && first._lineIndex <= second._lineIndex)
                      || (first._type == 1 && first._lineIndex >= second._lineIndex))
                {
                    // keine Überkreuzung (rot links / blau rechts)
                }
                else if (first._type == 0)
                {
                    if (first._lineLayer < second._lineLayer) { f0 = GameConfig.FlipYUnder; f1 = GameConfig.FlipYOver; }
                    else { f0 = GameConfig.FlipYOver; f1 = GameConfig.FlipYUnder; }
                }
                else
                {
                    if (first._lineLayer > second._lineLayer) { f0 = GameConfig.FlipYOver; f1 = GameConfig.FlipYUnder; }
                    else { f0 = GameConfig.FlipYUnder; f1 = GameConfig.FlipYOver; }
                }

                if (Mathf.Approximately(f0, 0f) && Mathf.Approximately(f1, 0f)) continue;

                // Start-X = Position der anderen Note (ArcViewer `FlipStartX`).
                float x0 = GameConfig.GridPosition(first._lineIndex, first._lineLayer).x;
                float x1 = GameConfig.GridPosition(second._lineIndex, second._lineLayer).x;
                var e0 = result[first];
                var e1 = result[second];
                e0.flipStartX = x1; e0.flipYHeight = f0;
                e1.flipStartX = x0; e1.flipYHeight = f1;
                result[first] = e0;
                result[second] = e1;
            }
        }

        /// <summary>
        /// Stapel-Index einer Note: 0, wenn keine Noten direkt darunter liegen (gleiche
        /// Spalte, tiefere Reihe, gleiche Zeit); sonst rekursiv 1 + größter Index darunter.
        /// Quelle: ArcViewer `NoteManager.GetStartY`.
        /// </summary>
        private static int StackIndex(NoteModel note, List<NoteModel> sameBeat)
        {
            if (note._lineLayer <= 0) return 0;
            int best = -1;
            foreach (var other in sameBeat)
            {
                if (ReferenceEquals(other, note)) continue;
                if (other._lineIndex != note._lineIndex || other._lineLayer >= note._lineLayer) continue;
                int v = StackIndex(other, sameBeat);
                if (v > best) best = v;
            }
            return best < 0 ? 0 : best + 1;
        }

        private float _roomScale = 1f;

        /// <summary>
        /// Setzt die EINE Raum-Skalierung auf allen Spielobjekten um: Raum-Root (Noten,
        /// Wände, Anzeigetafel, Score-Texte) + Lichtschwerter + Schwert-Labels.
        /// (Wirkt live, weil alles Kinder des skalierten Roots ist.)
        ///
        /// WICHTIG (Invariante): Alle Spielobjekte benutzen AUSSCHLIESSLICH lokale
        /// Basis-Koordinaten unter dem Raum-Root. Deshalb gilt welt = Skalierung × Basis
        /// für Stellung UND Größe — Raster, Lücke zwischen den Notenblöcken, Spawn-Distanz
        /// und Blockgröße skalieren damit IMMER gemeinsam.
        ///
        /// WICHTIG (Neustart-Verhalten): Der Wert wird IMMER ABSOLUT zugewiesen
        /// (`localScale = Basis × Regler`), nie multiplikativ auf einen gespeicherten
        /// Faktor — nach jedem Start entsteht dadurch exakt derselbe Zustand. Der Regler
        /// wird ausschließlich aus dem Spielerprofil gelesen (EnsurePlayer -> ApplyRoomScale).
        ///
        /// HINWEIS: Die vertikale Feinjustage der Noten/Minen/bodenfreien Wände ist die
        /// separate Menüzeile „Feldhöhe" (GameConfig.FieldHeightOffsetMeters) — NICHT Teil
        /// dieser Skalierung. Die Grundposition kommt aus dem ArcViewer-Raster
        /// (GameConfig.GridPosition); der Raum-Root wird hier (und in ApplyRoomAnchor) geführt.
        /// </summary>
        private void ApplyRoomScale()
        {
            _roomScale = _player != null
                ? Mathf.Clamp(_player.Scale, GameConfig.ScaleMin, GameConfig.ScaleMax)
                : 1f;
            if (_gameRoot != null)
                _gameRoot.localScale = Vector3.one * _roomScale;
            if (_saberL != null) _saberL.SetScaleMultiplier(_roomScale);
            if (_saberR != null) _saberR.SetScaleMultiplier(_roomScale);
            // Hinweis: Die Grifflabel sind Kinder des Label-Ankers der Schwert-Griffe und
            // skalieren automatisch mit der Raum-Skalierung (Anker neutralisiert die
            // Grundskalierung des Schwerts -> Weltfaktor = _roomScale). NICHT zusätzlich
            // localScale setzen, sonst würde die Schrift doppelt skaliert.
        }

        private void GameplayUpdate()
        {
            // Run and Play: Laufgeschwindigkeit → Zustand. Stillstand/Rückwärts pausiert
            // (Audio + Noten-Timeline stehen), Vorwärts koppelt die Wiedergaberate.
            RunAndPlayUpdate();

            // Drei Ebenen (PROJECT_NOTES §4): Score-Uhr = feste Referenz (Hitboxen/Saber-
            // Prüfung). Visual-Uhr = Score + Noten-Offset (Spawn/Sichtbarkeit der Noten).
            float scoreTime = _audio.ScoreTime;
            float visualTime = _audio.VisualTime;

            // Verbrauchte Noten VOR dem Spawn ausdünnen: Ein gepooltes NoteView kann sonst
            // im Spawn-Schritt wiederverwendet werden, während sein alter (verbrauchter)
            // Listeneintrag noch steht -> Doppel-Eintrag. Siehe PruneActiveNotes.
            PruneActiveNotes();

            // Spawn nach der VISUAL-Uhr (die Noten sollen zum richtigen Anzeige-Zeitpunkt
            // erscheinen); die Vorlaufzeit ist um Größenordnungen größer als der Offset.
            while (_noteCursor < _noteSchedule.Count && _noteSchedule[_noteCursor].spawn <= visualTime)
            {
                SpawnNote(_noteSchedule[_noteCursor].note,
                    _noteSchedule[_noteCursor].startYRel,
                    _noteSchedule[_noteCursor].flipStartX,
                    _noteSchedule[_noteCursor].flipYHeight);
                _noteCursor++;
            }

            while (_wallCursor < _wallSchedule.Count && _wallSchedule[_wallCursor].spawn <= visualTime)
            {
                SpawnWall(_wallSchedule[_wallCursor].obs, _wallSchedule[_wallCursor].durSec);
                _wallCursor++;
            }

            while (_arcCursor < _arcSchedule.Count && _arcSchedule[_arcCursor].spawn <= visualTime)
            {
                SpawnArc(_arcSchedule[_arcCursor].arc);
                _arcCursor++;
            }

            while (_chainCursor < _chainSchedule.Count && _chainSchedule[_chainCursor].spawn <= visualTime)
            {
                SpawnChain(_chainSchedule[_chainCursor].chain);
                _chainCursor++;
            }

            // Abgelaufene Bögen/Ketten (selbst in den Pool zurückgekehrt) aus den Listen räumen.
            for (int i = _activeArcs.Count - 1; i >= 0; i--)
                if (_activeArcs[i] == null || !_activeArcs[i].gameObject.activeSelf) _activeArcs.RemoveAt(i);
            for (int i = _activeChains.Count - 1; i >= 0; i--)
                if (_activeChains[i] == null || !_activeChains[i].gameObject.activeSelf) _activeChains.RemoveAt(i);

            // Treffer werden NICHT hier geprüft, sondern im LateUpdate NACH
            // ApplyRoomAnchor (GeometricHitUpdate): erst dann stehen Raum-Root UND
            // Controller-Anker auf der Welt-Pose, die in DIESEM Frame gezeichnet wird.
            // Ein Test gegen die Root-Pose des Vorframes war die Ursache der
            // „visuell getroffen, aber MISS / seitlich berührt, aber 110"-Fehler.

            // Wände: aufräumen + Kopf-Kollision (nur Combo-Reset)
            Vector3 head = _cam != null ? _cam.transform.position : Vector3.zero;
            bool headInWall = false;
            for (int i = _activeWalls.Count - 1; i >= 0; i--)
            {
                var w = _activeWalls[i];
                if (w == null) { _activeWalls.RemoveAt(i); continue; }
                if (w.Consumed) { _activeWalls.RemoveAt(i); Destroy(w.gameObject); continue; }
                if (w.IsHeadInside(head))
                {
                    headInWall = true;
                    if (!w.HeadAlreadyHit)
                    {
                        w.HeadAlreadyHit = true;
                        // Wand getroffen: Combo UND Multiplikator werden zurückgesetzt (x1).
                        BreakCombo(true);
                    }
                }
            }
            // (3) Kopf in Wand: Musik sofort dumpf (< 400 Hz); beim Verlassen sofort zurück.
            if (_audio != null) _audio.SetWallMuffle(headInWall);

            // Leichtes, anhaltendes Haptik-Feedback bei Klingen-Kontakt (Schwert↔Schwert
            // bzw. Schwert↔Wand) — pro Hand gedrosselt, allokationsfrei.
            UpdateContactHaptics();

            // Endlos: nahtlos nächstes Lied. (a) physisches Datei-Ende ist der härteste
            // Schalter; (b) die feste SCORE-Uhr hat die Überlappung erreicht (letzte Takte
            // + 0,4 s). Beides ist unabhängig von Audio-/Noten-Offset.
            if (_audio.ClipDuration > 0 && !_songEnded
                && (!_save.RunAndPlay || _runAndPlay.State == RunPlayState.Forward)
                && (_audio.AudioFinished || scoreTime >= _audio.ClipDuration - 0.4f))
            {
                _songEnded = true;
                OnSongEnded();
            }
        }

        private void SpawnNote(NoteModel note, float startYRel, float flipStartX, float flipYHeight)
        {
            // Exakte Hit-Zeit der Note (Map-BPM-Verlauf) — identisch zu BuildSchedule;
            // darüber wird der Treffer-Zeitpunkt (Punkteverteilung) bestimmt.
            float hitTime = _toSeconds != null ? _toSeconds(note._time)
                : BeatmapParser.BeatsToSeconds(note._time, _current != null ? _current.BeatsPerMinute : 120f);

            // Chart-treue Position: ArcViewer-Raster (Spalte + Vanilla-Reihe, GameConfig.
            // GridPosition); den „Feldhöhen"-Versatz addiert NoteView jeden Frame (live).
            // Die Treffer-/Score-Prüfung liest dieselbe Transform-Position (eine Ebene).
            var pos = GameConfig.GridPosition(note._lineIndex, note._lineLayer);

            // Noodle Extensions: freie Position (`coordinates`/`_position`) überschreibt das
            // Raster (ArcViewer `CalculateObjectPosition`: x um +2 versetzt, Einheit 0,6 m).
            var noodle = note._customData;
            if (noodle != null && noodle.HasPosition)
            {
                float cx = noodle.Position[0];
                float cy = noodle.Position[1];
                pos.x = GameConfig.GridLeftX + (cx + 2f) * GameConfig.LaneWidth;
                pos.y = GameConfig.RowHeights[0] + cy * GameConfig.LaneWidth;
                // Muster-Effekte (Stapel/Flip) gelten für frei positionierte Noten nicht.
                startYRel = 0f;
                flipStartX = pos.x;
                flipYHeight = 0f;
            }
            if (noodle != null && noodle.Flip) pos.x = -pos.x; // Spiegelung an der X-Mitte

            // Stapel-Start ist relativ zur Zielreihe gespeichert (Feldhöhen-unabhängig).
            float startY = pos.y + startYRel;
            // Z aus der VISUAL-Uhr (kontinuierlicher NJS-Flug, Tempo aus den Song-Daten):
            // Startwert zum Spawn-Augenblick; ab dann führt NoteView.Update nach.
            pos.z = GameConfig.JumpZ(hitTime, VisualTime, _njs);
            var view = NotePool.Get(note, pos, hitTime, _njs, _reactionTime, _halfJumpDistance,
                startY, flipStartX, flipYHeight);
            _activeNotes.Add(view);

            // Referenz 1:1 (BeatSaberClone BoxSpawner): Spawn-Partikel an der Notenposition —
            // Rot für `_type 0`, Blau für `_type 1`. Minen (Typ 3) sind keine Notenblöcke und
            // bleiben bewusst ohne Effekt.
            if (note._type != 3)
                Fx.PlayNoteSpawn(view.transform.position, note._type == 0, _roomScale);

            view.OnPassedMiss = null;
            view.OnPassedMiss += OnNoteMissed;
            view.OnComboBreak = null;
            view.OnComboBreak += OnNoteComboBreak;
        }

        /// <summary>
        /// Spawnt eine Wand aus dem ArcViewer-Raster (Wand-Container 0.1 m, Rastereinheit
        /// 0.6 m) mit zeitbasierter Z-Position. Die Länge in Bewegungsrichtung ist
        /// Dauer × NJS (ArcViewer WallManager) — dadurch entspricht die Durchquerzeit des
        /// Spielers exakt der Map-Dauer, und die Vorderkante fluchtet mit den Noten.
        /// </summary>
        private void SpawnWall(ObstacleModel obs, float durationSeconds)
        {
            // Exakte Hit-Zeit (BPM-Verlauf) — konsistent zu BuildSchedule
            float hitTime = _toSeconds != null ? _toSeconds(obs._time)
                : BeatmapParser.BeatsToSeconds(obs._time, _current != null ? _current.BeatsPerMinute : 120f);

            // Breite = Spalten × 0.6; linke Kante auf dem Spaltenraster
            // (ArcViewer WallManager.CalculateWallPosition + Wall-Konstruktor).
            float widthCols = Mathf.Max(1f, obs._width);
            float width = widthCols * GameConfig.LaneWidth;
            float x = GameConfig.GridLeftX + obs._lineIndex * GameConfig.LaneWidth
                      + (width - GameConfig.LaneWidth) * 0.5f;

            // Vertikale Lage aus der Map (Rohwerte). Die „Feldhöhe" verschiebt ALLE Wände nach
            // oben (bodenständige werden bis zum Boden verlängert); das GESAMTE Feld hebt/senkt
            // der linke Stick.
            GameConfig.ObstacleBounds(obs, out float bottom, out float height);
            float y = bottom + height * 0.5f;

            float length = Mathf.Max(durationSeconds * _njs, GameConfig.MinWallSize);
            float front = GameConfig.JumpZ(hitTime, VisualTime, _njs)
                          - GameConfig.NoteSize * 0.5f; // Vorderkante = Noten-Vorderfläche
            var pos = new Vector3(x, y, front + length * 0.5f);

            var wall = WallView.Create(obs, pos, _gameRoot, width, height, length,
                hitTime, _njs, bottom);
            _activeWalls.Add(wall);
        }

        /// <summary>Spawnt einen Bogen (v3/v4 slider). Der Bogen ist reine Optik; die
        /// Kopf-/Endnoten existieren als colorNotes und werden normal gewertet.</summary>
        private void SpawnArc(ArcModel arc)
        {
            float headTime = _toSeconds != null ? _toSeconds(arc._headTime)
                : BeatmapParser.BeatsToSeconds(arc._headTime, _current != null ? _current.BeatsPerMinute : 120f);
            float tailTime = _toSeconds != null ? _toSeconds(arc._tailTime)
                : BeatmapParser.BeatsToSeconds(arc._tailTime, _current != null ? _current.BeatsPerMinute : 120f);
            _activeArcs.Add(ArcView.Get(arc, headTime, tailTime, _njs, _gameRoot));
        }

        /// <summary>Spawnt eine Kette (v3/v4 burstSlider): sichtbare Glieder zwischen Kopf-
        /// und Endpunkt. Die Kopfnote selbst läuft als normale (markierte) Note mit.</summary>
        private void SpawnChain(ChainModel chain)
        {
            float headTime = _toSeconds != null ? _toSeconds(chain._time)
                : BeatmapParser.BeatsToSeconds(chain._time, _current != null ? _current.BeatsPerMinute : 120f);
            float tailTime = _toSeconds != null ? _toSeconds(chain._tailTime)
                : BeatmapParser.BeatsToSeconds(chain._tailTime, _current != null ? _current.BeatsPerMinute : 120f);
            _activeChains.Add(ChainView.Get(chain, headTime, tailTime, _njs, _gameRoot));
        }

    }
}
