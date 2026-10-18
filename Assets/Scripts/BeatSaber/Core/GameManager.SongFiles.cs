// GameManager: Lied-Dateien — Import aus dem Download-Ordner + Löschen von Liedern.
// Die reine Dateisystem-Logik liegt in SongInbox; hier nur die Anbindung an Spielablauf,
// Menü und Liederliste.
using System.Collections;
using UnityEngine;

namespace BeatSaber
{
    public sealed partial class GameManager
    {
        // Der Start-Import blockiert den Boot höchstens so lange; danach werden die
        // (bereits gefundenen) Lieder geladen. Ein großer Rest-Import läuft im Hintergrund
        // weiter und wird ausgewertet, sobald er fertig ist (PollSongImport).
        // Der Import läuft AUSSCHLIESSLICH beim App-Start (BootSongsRoutine) — NICHT während
        // die App bereits läuft (kein Resume-Import).
        private const float ImportBootTimeoutSeconds = 20f;

        /// <summary>Startet den Lied-Import aus dem Download-Ordner (Hintergrund-Task).</summary>
        private void StartSongImport()
        {
            _importInFlight = true;
            if (SongInbox.ImportRunning) return; // ein Lauf ist schon unterwegs
            SongInbox.ImportAsync(GameConfig.DownloadPath, GameConfig.MrSaberSongsPath);
            Debug.Log("[BeatSaber] Lied-Import gestartet (" + GameConfig.DownloadPath +
                      " -> " + GameConfig.MrSaberSongsPath + ").");
        }

        /// <summary>
        /// Jeden Frame aufrufen: erkennt das Ende eines Imports und liest dann die
        /// Liederliste neu ein (das Menü wird dabei aktualisiert).
        /// </summary>
        private void PollSongImport()
        {
            if (!_importInFlight || SongInbox.ImportRunning) return;
            _importInFlight = false;
            Debug.Log("[BeatSaber] Lied-Import beendet: " + SongInbox.LastSummary);

            // Im Boot lädt BootSongsRoutine die Liste selbst — dann hier nichts tun.
            if (Phase == GamePhase.Boot) return;
            RescanSongs();
            if (Phase == GamePhase.Menu) BuildMenuContent();
        }

        /// <summary>Boot-Schritt: kurz auf den Import warten (Cap), damit neue Lieder direkt in der Liste stehen.</summary>
        private IEnumerator ImportSongsAtBootRoutine()
        {
            StartSongImport();
            float waited = 0f;
            while (_importInFlight && waited < ImportBootTimeoutSeconds)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (_importInFlight)
                Debug.Log("[BeatSaber] Lied-Import läuft nach " + ImportBootTimeoutSeconds.ToString("0") +
                          " s noch — die Lieder erscheinen, sobald er fertig ist.");
        }

        /// <summary>Liest die Liederliste neu ein und behält dabei das aktuelle Lied (per Key).</summary>
        private void RescanSongs()
        {
            string keepKey = _current != null ? _current.Key : null;
            _songs = BeatmapParser.LoadSongList();   // liefert immer mind. das Demo-Lied
            if (_songs.Count == 0) return;
            RestoreDifficultyChoices();              // gespeicherte Schwierigkeits-Wahl je Lied
            int idx = keepKey != null ? _songs.FindIndex(s => s.Key == keepKey) : -1;
            if (idx < 0) idx = 0;
            _songIndex = idx;
            _current = _songs[idx];
        }

        // ======================================================================
        // Lied löschen („X" in der Songliste)
        // ======================================================================
        /// <summary>true, wenn dieses Lied gelöscht werden darf: MrSaber-eigener Ordner und
        /// kein Demo-Lied. Der SongCore-Ordner der offiziellen Beat Saber bleibt immer tabu.</summary>
        private static bool CanDeleteSong(SongMeta song)
            => song != null && !song.IsDemo && GameConfig.IsDeletableSongFolder(song.FolderPath);

        /// <summary>
        /// Öffnet den modalen Lösch-Dialog für das Lied, dessen Zeile der Laser anvisiert hat
        /// (Lösch-„X" in der Songliste). Wird vom GameManager bei Trigger auf dem X gerufen.
        /// </summary>
        private void ShowSongDeleteConfirm()
        {
            if (_menu == null || !_songListOpen) return;
            int id = _menu.CurrentRowId();
            if (id < IdSongBase || id >= IdSongBase + _songs.Count) return;
            int idx = id - IdSongBase;
            if (idx < 0 || idx >= _songs.Count) return;

            SongMeta song = _songs[idx];
            if (!CanDeleteSong(song))
            {
                Debug.Log($"[BeatSaber] Lied löschen ignoriert (nicht löschbar): '{song.SongName}'.");
                return;
            }

            _pendingSongDeleteIndex = idx;
            _confirmingDelete = true;
            _confirmingSongDelete = true;
            DismissKeyboard(); // keine hängende OS-Tastatur über dem Dialog
            LockMenuInput();
            Debug.Log($"[BeatSaber] Lied-Lösch-Dialog: '{song.SongName}' ({song.FolderPath}).");
            _menu.ShowConfirm("Lied löschen?", Shorten(song.SongName, 20));
        }

        /// <summary>Bricht das Lied-Löschen ab („Nein") — die Liste bleibt unverändert.</summary>
        private void CancelSongDelete()
        {
            _confirmingDelete = false;
            _confirmingSongDelete = false;
            _pendingSongDeleteIndex = -1;
            if (_menu != null) _menu.HideConfirm();
            LockMenuInput();
        }

        /// <summary>Löscht den Liedordner des in der Bestätigung freigegebenen Lieds und liest die Liste neu ein.</summary>
        private void ExecuteSongDelete()
        {
            int idx = _pendingSongDeleteIndex;
            _pendingSongDeleteIndex = -1;
            _confirmingDelete = false;
            _confirmingSongDelete = false;
            if (_menu != null) _menu.HideConfirm();

            if (idx < 0 || idx >= _songs.Count)
            {
                LockMenuInput();
                BuildMenuContent();
                return;
            }

            SongMeta song = _songs[idx];
            if (!CanDeleteSong(song))
            {
                Debug.LogWarning("[BeatSaber] Lied kann nicht gelöscht werden (kein MrSaber-eigener Ordner).");
                LockMenuInput();
                BuildMenuContent();
                return;
            }

            bool ok = SongInbox.DeleteTree(song.FolderPath);
            Debug.Log($"[BeatSaber] Lied löschen '{song.SongName}': " +
                      $"{(ok ? "entfernt" : "FEHLGESCHLAGEN")} ({song.FolderPath})");

            RescanSongs();               // das gelöschte Lied ist weg
            _songSelectionQueued = true; // beim Schließen ein gültiges Lied neu starten
            LockMenuInput();
            BuildMenuContent();
        }
    }
}
