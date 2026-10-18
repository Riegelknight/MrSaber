// ==============================================================================
//  SongInbox — Lieder aus dem Download-Ordner der Brille einlesen.
// ==============================================================================
//  Abgeschaut von QuestSaber (Assets/Plugins/Android/SafHelper.java: importFromDownload,
//  walkFs, importZip, extractZip, safe), aber OHNE Java/SAF: MrSaber läuft auf Android 10
//  (PICO 4) UND hat die externe Speicher-Berechtigung (WRITE über das Player-Setting
//  „Write Permission = External", READ über AndroidStorageManifestPostProcessor; zur
//  Laufzeit angefordert in GameManager.EnsureStoragePermission). Damit funktionieren
//  /storage/emulated/0/...-Pfade direkt mit System.IO — kein Document-Chooser nötig.
//
//  Regeln (bewusst identisch zu QuestSaber):
//   - Ein ORDNER mit info.dat/info.json ist ein Lied  -> kopieren, dann Quelle löschen.
//   - Eine .zip wird NUR entpackt, wenn sie ein Lied enthält -> sonst bleibt sie liegen.
//   - Alles andere bleibt unangetastet: der Download-Ordner wird NIE geleert.
//   - Erst nach erfolgreichem Kopieren wird die Quelle gelöscht (kein Datenverlust).
//   - Vorhandene Zieldateien gleicher Größe werden übersprungen (Import ist wiederholbar).
//
//  Der Import läuft auf einem Hintergrund-Task (Task.Run), damit ein großer Import die
//  VR-Session nicht einfriert. Der GameManager pollt ImportRunning/LastSummary.
// ==============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using UnityEngine;

namespace BeatSaber
{
    /// <summary>
    /// Datei-Import/-Löschen für Lieder. Reine Dateisystem-Logik (kein Unity-Objekt),
    /// damit sie gefahrlos in einem Hintergrund-Task laufen kann.
    /// </summary>
    public static class SongInbox
    {
        // Schutznetze (QuestSaber: MAX_FILE_BYTES / MAX_ZIP_BYTES).
        private const long MaxFileBytes = 100L * 1024 * 1024;       // Einzeldatei
        private const long MaxZipBytes = 2L * 1024 * 1024 * 1024;   // entpacktes Archiv gesamt
        private const int MaxDepth = 8;                             // Verschachtelungstiefe
        private const string UnpackPrefix = ".unpack_";             // Staging-Präfix (Punkt = unsichtbar)

        private static volatile bool _running;
        private static volatile string _summary = "";

        /// <summary>true, solange ein Import im Hintergrund läuft.</summary>
        public static bool ImportRunning => _running;

        /// <summary>Kurzfassung des letzten Imports (für Log/Anzeige).</summary>
        public static string LastSummary => _summary;

        // ======================================================================
        // Import
        // ======================================================================
        /// <summary>
        /// Startet den Import als Hintergrund-Task: alles Liedartige im
        /// <paramref name="downloadDir"/> wird in <paramref name="destDir"/> verschoben/
        /// entpackt. No-op, wenn schon ein Import läuft (parallele Läufe wären schädlich).
        /// </summary>
        public static void ImportAsync(string downloadDir, string destDir)
        {
            if (_running) return;
            if (string.IsNullOrEmpty(downloadDir) || string.IsNullOrEmpty(destDir)) return;

            _running = true;
            _summary = "";
            Task.Run(() =>
            {
                string summary;
                try
                {
                    summary = Import(downloadDir, destDir);
                }
                catch (Exception ex)
                {
                    summary = "Import fehlgeschlagen: " + ex.Message;
                    Debug.LogWarning("[BeatSaber] " + summary);
                }
                _summary = summary;   // erst setzen, dann _running löschen (Poll liest die Summary)
                _running = false;
            });
        }

        private static string Import(string downloadDir, string destDir)
        {
            if (!Directory.Exists(downloadDir))
                return "Kein Download-Ordner (" + downloadDir + ")";

            try { Directory.CreateDirectory(destDir); }
            catch (Exception ex) { return "Zielordner nicht anlegbar: " + ex.Message; }

            var st = new Stats();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(downloadDir); }
            catch (Exception ex) { return "Download-Ordner nicht lesbar: " + ex.Message; }

            foreach (string entry in entries)
            {
                string name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name) || name.StartsWith("."))
                    continue; // versteckte/Staging-Reste in Ruhe lassen

                try
                {
                    if (Directory.Exists(entry)) Walk(entry, destDir, st, 0);
                    else if (IsZip(name)) ImportZip(entry, destDir, st);
                    else st.Skipped++; // fremde Dateien (Fotos, PDFs, …) bewusst NICHT anfassen
                }
                catch (Exception ex)
                {
                    st.Errors++;
                    Debug.LogWarning("[BeatSaber] Download-Eintrag '" + name + "' übersprungen: " + ex.Message);
                }
            }

            string summary =
                $"{st.Songs} Lied(er) übernommen ({st.Folders} Ordner, {st.Zips} ZIP), " +
                $"{st.Skipped} übersprungen, {st.Errors} Fehler";
            Debug.Log("[BeatSaber] Lied-Import aus '" + downloadDir + "': " + summary);
            return summary;
        }

        /// <summary>Durchsucht einen Ordner nach Liedern (Ordner mit info-Datei oder .zip).</summary>
        private static void Walk(string dir, string destRoot, Stats st, int depth)
        {
            if (depth > MaxDepth) { st.Skipped++; return; }

            if (ContainsInfoFile(dir))
            {
                // Ein Liedordner: komplett kopieren, danach die Quelle entfernen.
                string target = Path.Combine(destRoot, Safe(Path.GetFileName(dir)));
                if (CopyTree(dir, target, st, depth))
                {
                    st.Folders++;
                    st.Songs++;
                    if (DeleteTree(dir)) st.Moved++;
                }
                return;
            }

            string[] kids;
            try { kids = Directory.GetFileSystemEntries(dir); }
            catch { st.Errors++; return; }

            foreach (string kid in kids)
            {
                string name = Path.GetFileName(kid);
                if (string.IsNullOrEmpty(name) || name.StartsWith(".")) continue;
                try
                {
                    if (Directory.Exists(kid)) Walk(kid, destRoot, st, depth + 1);
                    else if (IsZip(name)) ImportZip(kid, destRoot, st);
                    else st.Skipped++;
                }
                catch (Exception ex)
                {
                    st.Errors++;
                    Debug.LogWarning("[BeatSaber] Eintrag '" + name + "' übersprungen: " + ex.Message);
                }
            }
        }

        /// <summary>Entpackt eine .zip nur, wenn sie ein Lied enthält (sonst liegen lassen).</summary>
        private static void ImportZip(string zipPath, string destRoot, Stats st)
        {
            string baseName = Safe(Path.GetFileNameWithoutExtension(zipPath));
            string staging = Path.Combine(destRoot, UnpackPrefix + baseName);
            string target = Path.Combine(destRoot, baseName);
            DeleteTree(staging); // Reste eines früheren, abgebrochenen Laufs

            bool song = false;
            try
            {
                using (var fs = File.OpenRead(zipPath))
                    song = ExtractZip(fs, staging, st);
            }
            catch (Exception ex)
            {
                st.Errors++;
                Debug.LogWarning("[BeatSaber] Entpacken fehlgeschlagen ('" +
                                 Path.GetFileName(zipPath) + "'): " + ex.Message);
            }

            if (!song)
            {
                DeleteTree(staging); // keine info-Datei -> kein Lied -> ZIP bleibt unangetastet
                st.Skipped++;
                return;
            }

            DeleteTree(target); // gleichnamiges Lied ersetzen (ZIP ist die neuere Quelle)
            try
            {
                Directory.Move(staging, target);
            }
            catch (Exception ex)
            {
                st.Errors++;
                Debug.LogWarning("[BeatSaber] Lied konnte nicht übernommen werden ('" + baseName +
                                 "'): " + ex.Message);
                DeleteTree(staging);
                return;
            }

            st.Zips++;
            st.Songs++;
            if (DeleteTree(zipPath)) st.Moved++; // Quelle erst NACH erfolgreichem Übernehmen
        }

        /// <summary>
        /// Entpackt ein Archiv in <paramref name="staging"/>. Rückgabe: true, wenn eine
        /// info.dat/info.json enthalten war (dann ist es ein Lied). Einträge werden
        /// entschärft (keine absoluten Pfade, kein „.."); __MACOSX wird übersprungen.
        /// </summary>
        private static bool ExtractZip(Stream raw, string staging, Stats st)
        {
            bool song = false;
            long total = 0;

            using (var zip = new ZipArchive(raw, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string name = SanitizeEntry(entry.FullName);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (name.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;

                    string rel = name.Replace('/', Path.DirectorySeparatorChar);
                    string full = Path.Combine(staging, rel);

                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(full);
                        continue;
                    }

                    string dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    using (Stream es = entry.Open())
                    using (FileStream os = File.Create(full))
                    {
                        var buf = new byte[64 * 1024];
                        int n;
                        while ((n = es.Read(buf, 0, buf.Length)) > 0)
                        {
                            os.Write(buf, 0, n);
                            total += n;
                            if (total > MaxZipBytes)
                                throw new IOException("Archiv zu groß (Entpack-Schutz)");
                        }
                    }

                    st.Files++;
                    if (IsInfoFile(Path.GetFileName(rel))) song = true;
                }
            }
            return song;
        }

        // ======================================================================
        // Löschen
        // ======================================================================
        /// <summary>
        /// Löscht eine Datei oder einen Ordner (rekursiv). Rückgabe false bei Fehler.
        /// Die Pfad-Prüfung (nur MrSaber-eigene Lieder, siehe GameConfig.IsDeletableSongFolder)
        /// übernimmt der Aufrufer.
        /// </summary>
        public static bool DeleteTree(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return true;
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BeatSaber] Löschen fehlgeschlagen ('" + path + "'): " + ex.Message);
                return false;
            }
        }

        // ======================================================================
        // Helfer (nach QuestSaber SafHelper.java)
        // ======================================================================
        /// <summary>info.dat/info.json (Groß-/Kleinschreibung egal) kennzeichnet ein Lied.</summary>
        public static bool IsInfoFile(string name)
            => !string.IsNullOrEmpty(name)
               && (name.Equals("info.dat", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("info.json", StringComparison.OrdinalIgnoreCase));

        public static bool IsZip(string name)
            => !string.IsNullOrEmpty(name)
               && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        /// <summary>Enthält der Ordner direkt eine info-Datei?</summary>
        public static bool ContainsInfoFile(string dir)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                    if (IsInfoFile(Path.GetFileName(f))) return true;
            }
            catch { /* nicht lesbar = kein Liedordner */ }
            return false;
        }

        /// <summary>Macht aus einem Namen einen sicheren Ordnernamen (keine Pfadtrenner/„..").</summary>
        private static string Safe(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            string s = name.Replace('/', '_').Replace('\\', '_').Replace("..", "_").Trim();
            return (s.Length == 0 || s == ".") ? "unnamed" : s;
        }

        /// <summary>Zip-Eintrag entschärfen: keine absoluten Pfade, kein „..", kein Laufwerksbuchstabe.</summary>
        private static string SanitizeEntry(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            string s = raw.Replace('\\', '/');
            while (s.StartsWith("/")) s = s.Substring(1);
            if (s.Length == 0) return null;
            if (s.Contains("..")) return null;
            if (s.Length > 1 && s[1] == ':') return null; // Laufwerksbuchstabe
            return s;
        }

        // ---- Kopieren ----
        private static bool CopyTree(string src, string dest, Stats st, int depth)
        {
            if (depth > MaxDepth + 2) { st.Errors++; return false; }
            try { Directory.CreateDirectory(dest); }
            catch { st.Errors++; return false; }

            bool ok = true;
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(src); }
            catch { st.Errors++; return false; }

            foreach (string entry in entries)
            {
                string name = Path.GetFileName(entry);
                try
                {
                    if (Directory.Exists(entry))
                    {
                        if (!CopyTree(entry, Path.Combine(dest, Safe(name)), st, depth + 1)) ok = false;
                    }
                    else if (!CopyFile(entry, Path.Combine(dest, Safe(name)), st)) ok = false;
                }
                catch (Exception ex)
                {
                    st.Errors++;
                    ok = false;
                    Debug.LogWarning("[BeatSaber] Kopieren fehlgeschlagen ('" + name + "'): " + ex.Message);
                }
            }
            return ok;
        }

        private static bool CopyFile(string src, string dest, Stats st)
        {
            long size;
            try { size = new FileInfo(src).Length; }
            catch { size = -1; }

            if (size > MaxFileBytes) { st.Skipped++; return true; } // Riesendatei bewusst überspringen

            try
            {
                // Schon vorhanden und gleich groß -> überspringen (Import bleibt wiederholbar).
                if (size > 0 && File.Exists(dest) && new FileInfo(dest).Length == size)
                {
                    st.Skipped++;
                    return true;
                }
                File.Copy(src, dest, true);
                st.Files++;
                return true;
            }
            catch (Exception ex)
            {
                st.Errors++;
                Debug.LogWarning("[BeatSaber] Datei kopieren fehlgeschlagen ('" +
                                 Path.GetFileName(src) + "'): " + ex.Message);
                return false;
            }
        }

        /// <summary>Zähler eines Import-Laufs.</summary>
        private sealed class Stats
        {
            public int Songs;    // übernommene Lieder (Ordner + ZIP)
            public int Folders;  // davon aus einem Ordner
            public int Zips;     // davon aus einer ZIP
            public int Files;    // geschriebene Dateien
            public int Skipped;  // übersprungene Einträge (kein Lied / zu groß / doppelt)
            public int Errors;
            public int Moved;    // gelöschte Quellen
        }
    }
}
