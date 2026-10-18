// GameManager: Speichern und Laden (Save, Highscores).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BeatSaber
{
    public sealed partial class GameManager
    {
        // ======================================================================
        // Persistenz
        // ======================================================================
        private void LoadSave()
        {
            try
            {
                if (File.Exists(GameConfig.ProfilesPath))
                {
                    var json = File.ReadAllText(GameConfig.ProfilesPath);
                    _save = JsonUtility.FromJson<SaveData>(json) ?? new SaveData();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BeatSaber] Save laden fehlgeschlagen: " + ex.Message);
            }

            _save.Players ??= new List<PlayerProfile>();
            _save.DifficultyChoices ??= new List<SongDiffChoice>();

            foreach (var p in _save.Players)
            {
                bool oldHeight = p.HeightCm >= 100f && p.HeightCm <= 250f;
                if (oldHeight && Mathf.Approximately(p.Scale, 1f))
                    p.Scale = GameConfig.HeightCmToScale(p.HeightCm);
                if (oldHeight) p.HeightCm = 0f;
                p.Scale = Mathf.Clamp(p.Scale, GameConfig.ScaleMin, GameConfig.ScaleMax);
                // Run-and-Play „Lauf-Tempo": alte Saves ohne Feld haben 0 → Default 1 (echter
                // Lock), sonst auf den gültigen Bereich klemmen.
                if (p.RunComfort <= 0.01f) p.RunComfort = GameConfig.RunComfortDefault;
                p.RunComfort = Mathf.Clamp(p.RunComfort, GameConfig.RunComfortMin, GameConfig.RunComfortMax);
            }
            ApplyFieldHeight();
            if (_save.PassthroughDisabled && _save.BackgroundMode == 0)
                _save.BackgroundMode = (int)Environment.BackgroundMode.Default;
            _save.BackgroundMode = Mathf.Clamp(_save.BackgroundMode, 0, 2);
            _save.PassthroughDisabled = false;
            // Run and Play erzwingt die Raum-Mitnahme auch nach einem Neustart.
            if (_save.RunAndPlay) _save.RoomFollowsPlayer = true;
        }

        /// <summary>Gespeicherte Schwierigkeits-Wahl je Lied auf die Lieder anwenden (nach dem Song-Scan).</summary>
        private void RestoreDifficultyChoices()
        {
            foreach (var choice in _save.DifficultyChoices)
            {
                var song = _songs.FirstOrDefault(s => s.Key == choice.SongKey);
                if (song == null) continue;
                for (int i = 0; i < song.Difficulties.Count; i++)
                {
                    if (string.Equals(song.Difficulties[i].Name, choice.Difficulty,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        song.SelectedDifficulty = i;
                        break;
                    }
                }
            }
        }

        /// <summary>Aktuelle Schwierigkeits-Wahl des Lieds global speichern (bleibt nach Neustart aktiv).</summary>
        private void SaveDifficultyChoice()
        {
            if (_current == null || _current.CurrentDifficulty == null) return;
            _save.DifficultyChoices.RemoveAll(c => c.SongKey == _current.Key);
            _save.DifficultyChoices.Add(new SongDiffChoice
            {
                SongKey = _current.Key,
                Difficulty = _current.CurrentDifficulty.Name
            });
            Save();
        }

        private void MigrateLegacyScores()
        {
            bool changed = false;
            foreach (var p in _save.Players)
            {
                for (int i = 0; i < p.BestScores.Count; i++)
                {
                    var r = p.BestScores[i];
                    if (r.Key.Contains('|')) continue;

                    string newKey = null;
                    if (r.Key == SongMeta.DemoKey)
                    {
                        newKey = SongMeta.DemoKey + "|" + GameConfig.DemoDifficultyName;
                    }
                    else
                    {
                        var song = _songs.FirstOrDefault(s => s.Key == r.Key);
                        if (song != null && song.Difficulties.Count > 0)
                        {
                            var d = song.Difficulties[song.LegacyDefaultIndex];
                            newKey = song.Key + "|" + d.Name;
                        }
                    }
                    if (newKey != null)
                    {
                        p.BestScores[i] = new ScoreRecord { Key = newKey, Score = r.Score };
                        changed = true;
                    }
                }
            }
            if (changed) Save();
        }

        /// <summary>
        /// Einmal-Migration (12.10.2026): Der frühere harte Audio-Kalibrierwert −85 ms ist
        /// jetzt in der Noten-Uhr eingebacken (<see cref="GameConfig.AudioLatencyBaseMs"/> =
        /// 275 ms) und der Regler startet bei 0. Ein gespeicherter Alt-Wert würde doppelt
        /// kompensieren — daher wird er einmalig auf 0 gezogen (nur solange die Migration
        /// noch nicht gelaufen ist, danach bleibt eine Feinkorrektur erhalten).
        /// </summary>
        private void MigrateAudioOffset()
        {
            if (_save.SoundDelayBakedIn) return;
            _save.SoundDelayBakedIn = true;
            _save.SoundDelayMs = 0f;
            if (_audio != null) _audio.SetAudioOffset(0f);
            Save();
        }

        private void EnsurePlayer()
        {
            if (_save.Players.Count == 0)
            {
                _save.Players.Add(new PlayerProfile { Name = "Spieler 1" });
            }
            PlayerProfile selected = null;
            foreach (var p in _save.Players)
            {
                if (p.Name == _save.SelectedPlayer) { selected = p; break; }
            }
            if (selected == null)
            {
                selected = _save.Players[0];
                _save.SelectedPlayer = selected.Name;
            }
            _player = selected;
            ApplyRoomScale();
            Save();
        }

        /// <summary>
        /// Persistenz anfordern (Werte stehen SOFORT im Speicher-Zustand). Der eigentliche
        /// Datei-Write wird gebündelt (max. 1× pro Sekunde, siehe SaveThrottleSeconds) —
        /// dadurch kein Main-Thread-Disk-I/O bei jedem Rekord-Schnitt. Offene Änderungen
        /// werden beim Liedende (OnSongEnded) und bei Quit zuverlässig geflusht.
        /// </summary>
        private void Save()
        {
            _savePending = true;
            if (Time.unscaledTime - _lastSaveTime < SaveThrottleSeconds) return;
            FlushSave();
        }

        /// <summary>Save-Daten JETZT auf die Platte schreiben (unabhängig vom Throttle).</summary>
        private void FlushSave()
        {
            _savePending = false;
            _lastSaveTime = Time.unscaledTime;
            try
            {
                if (!_saveDirReady)
                {
                    Directory.CreateDirectory(GameConfig.SaveRootPath);
                    _saveDirReady = true;
                }
                File.WriteAllText(GameConfig.ProfilesPath, JsonUtility.ToJson(_save, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BeatSaber] Save schreiben fehlgeschlagen: " + ex.Message);
            }
        }

    }
}
