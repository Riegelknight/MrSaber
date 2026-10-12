using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

// Die JSON-DTO-Klassen unten werden von Unitys JsonUtility/Reflection befüllt; der
// Compiler sieht keine Zuweisung und würde für jedes Feld CS0649 melden. Das ist bei
// Serialisierungs-DTOs normal und harmlos — daher hier gezielt stummgeschaltet.
#pragma warning disable 0649

namespace BeatSaber
{
    /// <summary>
    /// Liest echte BeatSaver-v2-Maps: info.json (Song-Metadaten + Schwierigkeit Standard)
    /// und die .dat (Noten, Minen/Wände). Kein Unity-JsonUtility für Arrays nötig —
    /// die Arrays kommen direkt aus minimalen Wrapper-Klassen.
    /// </summary>
    public static class BeatmapParser
    {
        // ---------- NJS/_customData (v2.0: direkt am Eintrag, v2.1: im _customData) ----------
        [Serializable]
        private sealed class DifficultyCustomData
        {
            public float _noteJumpMovementSpeed = 0f;
            public float _noteJumpStartBeatOffset = 0f;
            // Umgebungsfarben werden NICHT hier gelesen (JSON-Form variiert: Array vs.
            // Objekt) — dafür der robuster Roh-Text-Scan `ApplyRawEnvColors`.
        }

        [Serializable]
        private sealed class ChartCustomData
        {
            public float _noteJumpMovementSpeed = 0f;
            public float _noteJumpStartBeatOffset = 0f;
            // Map-eigene Umgebungsfarben (manche Maps legen sie in die Difficulty).
            public float[] _envColorLeft;
            public float[] _envColorRight;
            public float[] _envColorLeftBoost;
            public float[] _envColorRightBoost;
        }

        // ---------- info.json ----------
        [Serializable]
        private sealed class InfoJson
        {
            [Serializable]
            public sealed class DifficultySet
            {
                [Serializable]
                public sealed class Difficulty
                {
                    public string _difficulty;
                    public string _beatmapFilename;

                    // v2.0.0: NJS/Offset stehen DIREKT am Difficulty-Eintrag,
                    // v2.1.0: sie liegen in _customData — beides wird gelesen.
                    public float _noteJumpMovementSpeed = 0f;
                    public float _noteJumpStartBeatOffset = 0f;
                    public DifficultyCustomData _customData;
                }

                public string _beatmapCharacteristicName;
                public List<Difficulty> _difficultyBeatmaps = new();
            }

            public string _songName = "";
            public string _songSubName = "";
            public string _songAuthorName = "";
            public float _beatsPerMinute = 120f;
            public string _songFilename = "";
            // Umgebung: v2 `_environmentName`, v3/v4 `environmentNames` (Liste).
            public string _environmentName = "";
            public List<string> environmentNames;
            // Umgebungsfarben: v2/v3 in `_customData._envColor*`, v4 zusätzlich als Hex
            // (`environmentColor0/1/…`).
            public InfoCustomData _customData;
            public string environmentColor0;
            public string environmentColor1;
            public string environmentColor0Boost;
            public string environmentColor1Boost;
            public List<DifficultySet> _difficultyBeatmapSets = new();
        }

        [Serializable]
        private sealed class InfoCustomData
        {
            public float[] _envColorLeft;
            public float[] _envColorRight;
            public float[] _envColorLeftBoost;
            public float[] _envColorRightBoost;
        }

        // ---------- .dat (v2-Legacy: lange Feldnamen) ----------
        [Serializable]
        private sealed class ChartJsonV2
        {
            public string _version = "";
            public List<NoteModel> _notes = new();
            public List<ObstacleModel> _obstacles = new();

            // Manche Maps tragen NJS/Offset am Anfang der Difficulty-.dat:
            public ChartCustomData _customData = null;
            // Arcs/Chains (v2.1) lesen wir bereits mit (noch ohne Gameplay):
            public List<ArcModel> _sliders = null;
            public List<ChainModel> _chains = null;
            // BPM-Wechsel der Map (Zeitpunkt in Beats)
            public List<V2BpmChange> _BPMChanges = null;
            // Lightshow-Events (v2: _events)
            public List<V2Event> _events = null;
        }

        // v2-BPM-Wechsel (Zeitpunkt in Beats + neue BPM)
        [Serializable]
        private sealed class V2BpmChange
        {
            public float _time;
            public float _BPM;
        }

        // ---------- .dat (v3/v4: kurze Feldnamen) ----------
        // v3/v4-Noten: b=Beat, x=Spalte, y=Reihe, c=Farbtyp (0=rot/links, 1=blau/rechts), d=Schnittrichtung, a=Winkel-Offset
        [Serializable]
        private sealed class V3Note
        {
            public float b;
            public int x;
            public int y;
            public int c;
            public int d;
            public float a;
            // Noodle Extensions (v3/v4): Position/Rotation/Flip/Scale/per-Note-NJS.
            public NoteCustomData customData;
        }

        // v3/v4-Bomben: eigene Liste (kein Typ-Feld mehr)
        [Serializable]
        private sealed class V3Bomb
        {
            public float b;
            public int x;
            public int y;
        }

        // v3/v4-Wände: y=0 => ganze Wand, y=2 => Kauer-Wand (v2: _type 0/1)
        [Serializable]
        private sealed class V3Obstacle
        {
            public float b;
            public int x;
            public int y;
            public float d;
            public int w;
            public int h;
        }

        // v3/v4-BPM-Events (kurze Feldnamen, ohne Underscore-Präfix): b = Beat, m = BPM
        [Serializable]
        private sealed class V3BpmEvent
        {
            public float b;
            public float m;
        }

        // v3/v4-Bogen (sliders): b/x/y/c/d = Kopf, mu = Kopf-Kontrollmultiplizierer,
        // tb/tx/ty/tc = Ende, tmu = Ende-Kontrollmultiplizierer, m = Bogenmitte-Modus.
        [Serializable]
        private sealed class V3Slider
        {
            public float b;
            public int x;
            public int y;
            public int c;
            public int d;
            public float mu;
            public float tb;
            public int tx;
            public int ty;
            public int tc;
            public float tmu;
            public int m;
        }

        // v3/v4-Kette (burstSliders): b/x/y/c/d = Kopf, tb = End-Beat, tx/ty = Endposition,
        // sc = Segmentzahl (Kopf + sc-1 Glieder), s = Squish.
        [Serializable]
        private sealed class V3BurstSlider
        {
            public float b;
            public int x;
            public int y;
            public int c;
            public int d;
            public float tb;
            public int tx;
            public int ty;
            public int sc;
            public float s;
        }

        // v2-Lightshow-Events (lange Feldnamen)
        [Serializable]
        private sealed class V2Event
        {
            public float _time;
            public int _type;
            public int _value;
            public float _floatValue = 1f;
            public V2EventCustomData _customData;
        }

        [Serializable]
        private sealed class V2EventCustomData
        {
            public float[] _color;
            public float[] color;
            public int[] lightID;
        }

        // v3/v4-Lightshow-Events (kurze Feldnamen): b=Beat, t=Typ, i=Wert, f=Float
        [Serializable]
        private sealed class V3Event
        {
            public float b;
            public int t;
            public int i;
            public float f = 1f;
            public V3EventCustomData customData;
        }

        [Serializable]
        private sealed class V3EventCustomData
        {
            public float[] color;
            public int[] lightID;
        }

        // v3/v4-Color-Boost-Events: b=Beat, o=an/aus
        [Serializable]
        private sealed class V3BoostEvent
        {
            public float b;
            public bool o;
        }

        [Serializable]
        private sealed class ChartJsonV3
        {
            public string _version = "";
            public List<V3Note> _colorNotes = null;
            public List<V3Bomb> _bombNotes = null;
            public List<V3Obstacle> _obstacles = null;
            public List<V3Slider> sliders = null;
            public List<V3BurstSlider> burstSliders = null;
            public ChartCustomData _customData = null;
            public List<V3BpmEvent> bpmEvents = null;
            public List<V3Event> basicBeatmapEvents = null;
            public List<V3BoostEvent> colorBoostBeatmapEvents = null;
        }

        // v4 hat dieselben Felder wie v3, nur ohne Underscore-Präfix
        [Serializable]
        private sealed class ChartJsonV4
        {
            public string _version = "";
            public List<V3Note> colorNotes = null;
            public List<V3Bomb> bombNotes = null;
            public List<V3Obstacle> obstacles = null;
            public List<V3Slider> sliders = null;
            public List<V3BurstSlider> burstSliders = null;
            public ChartCustomData _customData = null;
            public List<V3BpmEvent> bpmEvents = null;
            public List<V3Event> basicBeatmapEvents = null;
            public List<V3BoostEvent> colorBoostBeatmapEvents = null;
        }

        /// <summary>
        /// Scannt ALLE Lied-Ordner (GameConfig.SongsRootCandidates: SongCore der offiziellen
        /// Beat Saber, MrSaber-eigener Ordner, app-eigener Songs-Ordner), führt sie zusammen
        /// und fällt auf das Demo-Lied zurück. Bevorzugt "Standard"-Characteristic + höchste
        /// Difficulty.
        /// </summary>
        public static List<SongMeta> LoadSongList()
        {
            var result = new List<SongMeta>();
            foreach (var root in GameConfig.SongsRootCandidates)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    ScanSongRoot(root, result);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BeatSaber] Songs-Scan '{root}' fehlgeschlagen: {ex.Message}");
                }
            }

            if (result.Count == 0)
            {
                Debug.Log("[BeatSaber] Keine Lieder gefunden -> Demo-Lied als Fallback.");
                result.Add(CreateDemoMeta());
            }

            return result
                .OrderBy(s => s.SongName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>Scannt EINEN Lied-Wurzelordner: direkte Unterordner sind Lieder;
        /// hat ein Ordner selbst kein Info-File, wird eine Ebene tiefer gesucht
        /// (Sammelordner wie "CustomLevels").</summary>
        private static void ScanSongRoot(string root, List<SongMeta> result)
        {
            foreach (var dir in Directory.GetDirectories(root))
            {
                try
                {
                    var meta = ParseSongFolder(Path.GetFileName(dir), dir);
                    if (meta != null)
                    {
                        result.Add(meta);
                        continue;
                    }

                    // Kein info.json/info.dat hier: eine Ebene tiefer suchen.
                    foreach (var sub in Directory.GetDirectories(dir))
                    {
                        var subMeta = ParseSongFolder(Path.GetFileName(sub), sub);
                        if (subMeta != null) result.Add(subMeta);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BeatSaber] Liedordner '{dir}' übersprungen: {ex.Message}");
                }
            }
        }

        /// <summary>Ein Liedordner mit info.json + .dat + Audiodatei.</summary>
        public static SongMeta ParseSongFolder(string folderName, string folderPath)
        {
            // Auf der Brille liegen die Songs als info.dat (BeatSaver-v2); info.json als Fallback
            string infoPath = Path.Combine(folderPath, "info.json");
            if (!File.Exists(infoPath)) infoPath = Path.Combine(folderPath, "info.dat");
            if (!File.Exists(infoPath))
            {
                var cand = Directory.GetFiles(folderPath)
                    .FirstOrDefault(f => f.EndsWith("info.dat", StringComparison.OrdinalIgnoreCase)
                                      || f.EndsWith("info.json", StringComparison.OrdinalIgnoreCase));
                if (cand != null) infoPath = cand;
            }
            if (!File.Exists(infoPath)) return null;

            string json = File.ReadAllText(infoPath);
            var info = FromJson<InfoJson>(json);
            if (info == null) throw new InvalidDataException("info.json unlesbar");

            string audioName = string.IsNullOrEmpty(info._songFilename) ? "song.ogg" : info._songFilename;
            string audioPath = FindAudioFile(folderPath, audioName);

            return BuildMetaFromInfo(folderName, folderPath, info, false, audioName, audioPath, json);
        }

        private static SongMeta BuildMetaFromInfo(string folderName, string folderPath, InfoJson info,
            bool isDemo, string audioName, string audioPath, string rawInfoJson = null)
        {
            // NUR die Standard-Characteristic ist spielbar. Sondermodi wie OneSaber
            // („einhändig"), 360°/90° oder Lightshow bleiben komplett deaktiviert —
            // Lieder ohne Standard-Satz werden übersprungen.
            var set = info._difficultyBeatmapSets?.FirstOrDefault(s => string.Equals(
                s._beatmapCharacteristicName, "Standard", StringComparison.OrdinalIgnoreCase));
            if (set == null) return null; // kein Standard-Satz: Lied überspringen

            var diffs = set._difficultyBeatmaps ?? new List<InfoJson.DifficultySet.Difficulty>();
            var ordered = diffs
                .Where(d => !string.IsNullOrEmpty(d._beatmapFilename))
                .OrderBy(d => DifficultyRank(d._difficulty))
                .ToList();
            if (ordered.Count == 0) throw new InvalidDataException("Keine Beatmap-Dateien");

            // NJS/Spawn-Offset je Difficulty: v2.0 direkt am Eintrag, v2.1 in _customData
            var difficultyList = new List<SongDifficulty>();
            foreach (var d in ordered)
            {
                float njs = d._noteJumpMovementSpeed;
                float beatOffset = d._noteJumpStartBeatOffset;
                if (d._customData != null)
                {
                    if (d._customData._noteJumpMovementSpeed > 0f) njs = d._customData._noteJumpMovementSpeed;
                    if (d._customData._noteJumpStartBeatOffset != 0f) beatOffset = d._customData._noteJumpStartBeatOffset;
                }
                string diffName = string.IsNullOrEmpty(d._difficulty) ? "Normal" : d._difficulty;
                difficultyList.Add(new SongDifficulty
                {
                    Name = diffName,
                    Rank = DifficultyRank(diffName),
                    BeatmapFileName = MapFileNameFor(isDemo, d._beatmapFilename),
                    NoteJumpMovementSpeed = njs,
                    NoteJumpStartBeatOffset = beatOffset,
                });
            }

            int legacyIndex = difficultyList.Count - 1;
            int bestRank = -1;
            for (int i = 0; i < difficultyList.Count; i++)
            {
                bool inFolderName = folderName.IndexOf(difficultyList[i].Name, StringComparison.OrdinalIgnoreCase) >= 0;
                if (inFolderName && difficultyList[i].Rank > bestRank)
                {
                    legacyIndex = i;
                    bestRank = difficultyList[i].Rank;
                }
            }
            // Highscore-Schlüssel: das Demo nutzt DemoKey (nicht den Ordnernamen), damit die
            // alten "__demo__"-Einträge weiterhin darauf treffen (siehe MigrateLegacyScores).
            string keyBase = isDemo ? SongMeta.DemoKey : folderName;
            for (int i = 0; i < difficultyList.Count; i++)
                difficultyList[i].Key = keyBase + "|" + difficultyList[i].Name;

            var meta = new SongMeta
            {
                FolderName = folderName,
                FolderPath = folderPath,
                SongName = string.IsNullOrEmpty(info._songName) ? folderName : info._songName,
                SongSubName = info._songSubName,
                SongAuthorName = info._songAuthorName,
                SongFileName = audioName,
                BeatsPerMinute = info._beatsPerMinute > 0 ? info._beatsPerMinute : 120f,
                EnvironmentName = ResolveEnvironmentName(info),
                IsDemo = isDemo,
                AudioPath = audioPath,
                Difficulties = difficultyList,
                LegacyDefaultIndex = legacyIndex,
                SelectedDifficulty = legacyIndex,
            };
            meta.ApplySelectedDifficulty();
            ApplyInfoEnvColors(meta, info);
            // Roh-Text-Scan ZULETZT: liest `_envColor*` in BEIDEN Notationen
            // (Array `[r,g,b]` UND Objekt `{r,g,b}` — ChroMapper schreibt Letzteres, z. B.
            // „Radiant"/374e3) und überschreibt damit die DTO-Werte.
            ApplyRawEnvColors(meta, rawInfoJson);
            return meta;
        }

        /// <summary>Umgebungs-Name der Map: v3/v4 `environmentNames` (erster Eintrag) bzw.
        /// v2 `_environmentName`; Fallback DefaultEnvironment.</summary>
        private static string ResolveEnvironmentName(InfoJson info)
        {
            if (info == null) return "DefaultEnvironment";
            if (info.environmentNames != null && info.environmentNames.Count > 0
                && !string.IsNullOrEmpty(info.environmentNames[0]))
            {
                return info.environmentNames[0];
            }
            if (!string.IsNullOrEmpty(info._environmentName)) return info._environmentName;
            return "DefaultEnvironment";
        }

        private static string MapFileNameFor(bool isDemo, string beatmapFileName)
        {
            if (!isDemo) return beatmapFileName;
            return GameConfig.DemoMapResourcePrefix + Path.GetFileNameWithoutExtension(beatmapFileName);
        }

        private static readonly Dictionary<string, BeatmapChart> ChartCache = new();
        private const int ChartCacheMax = 128;

        /// <summary>Lädt die .dat eines Lieds zu NoteModel/ObstacleModel-Listen (v2, v3 und v4)
        /// — mit Chart-Cache: bereits geparste Songs/Difficulties bleiben im Speicher.
        /// Beim eingebauten Lied ist <see cref="SongMeta.MapPath"/> der Resources-Name.</summary>
        public static BeatmapChart ParseChart(SongMeta meta)
        {
            if (meta == null) return new BeatmapChart();

            string cacheKey = meta.MapPath;
            if (!string.IsNullOrEmpty(cacheKey) && ChartCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var chart = ParseChartCore(meta);

            if (ChartCache.Count >= ChartCacheMax) ChartCache.Clear();
            if (!string.IsNullOrEmpty(cacheKey)) ChartCache[cacheKey] = chart;
            return chart;
        }

        /// <summary>Eigentliches Parsen ohne Cache (v2/v3/v4 in einheitliche v2-Modelle).</summary>
        private static BeatmapChart ParseChartCore(SongMeta meta)
        {
            var chart = new BeatmapChart();

            string mapJson = meta.IsDemo
                ? ReadResourceText(meta.MapPath)
                : File.ReadAllText(meta.MapPath);
            int version = DetectMapVersion(mapJson);

            if (version == 2)
            {
                // v2-Legacy: _notes mit _type (0=rot, 1=blau, 3=Bombe) und _cutDirection 0..8
                var parsed = FromJson<ChartJsonV2>(mapJson);
                if (parsed == null) return chart;

                chart._version = parsed._version ?? "";
                chart._notes = parsed._notes ?? new List<NoteModel>();
                chart._obstacles = parsed._obstacles ?? new List<ObstacleModel>();

                // Arcs/Chains (v2.1) mitlesen — noch ohne Gameplay, aber die Daten sind da.
                chart.Sliders = parsed._sliders ?? new List<ArcModel>();
                chart.Chains = parsed._chains ?? new List<ChainModel>();

                // Manche Maps tragen NJS/Spawn-Offset am Anfang der Difficulty-.dat
                // (statt in der info): das überschreibt die info-Werte.
                if (parsed._customData != null && parsed._customData._noteJumpMovementSpeed > 0f)
                {
                    meta.NoteJumpMovementSpeed = parsed._customData._noteJumpMovementSpeed;
                    meta.NoteJumpStartBeatOffset = parsed._customData._noteJumpStartBeatOffset;
                }
                ApplyChartEnvColors(meta, parsed._customData);

                // BPM-Verlauf (v2: _BPMChanges): bestimmt die exakte Beat->Zeit-Umrechnung
                if (parsed._BPMChanges != null && parsed._BPMChanges.Count > 0)
                {
                    chart.BpmEvents = parsed._BPMChanges
                        .Where(c => c._BPM > 0f)
                        .Select(c => new BeatBpmPoint { Beat = c._time, Bpm = c._BPM })
                        .ToList();
                }
                ApplyV2Events(chart, parsed._events);
                return chart;
            }

            if (version == 3 || version == 4)
            {
                // v3/v4: _colorNotes/colorNotes mit kurzen Feldnamen, Bomben als eigene Liste
                var parsed = version == 4
                    ? FromJson<ChartJsonV4>(mapJson)
                    : FromJson<ChartJsonV3>(mapJson) as object;
                if (parsed is ChartJsonV4 v4)
                {
                    chart._version = v4._version ?? "";
                    ApplyV3Chart(chart, meta,
                        v4.colorNotes, v4.bombNotes, v4.obstacles, v4._customData);
                    chart.BpmEvents = ToBpmEvents(v4.bpmEvents);
                    ApplyV3Sliders(chart, v4.sliders, v4.burstSliders);
                    ApplyV3Events(chart, v4.basicBeatmapEvents, v4.colorBoostBeatmapEvents);
                }
                else if (parsed is ChartJsonV3 v3)
                {
                    chart._version = v3._version ?? "";
                    ApplyV3Chart(chart, meta,
                        v3._colorNotes, v3._bombNotes, v3._obstacles, v3._customData);
                    chart.BpmEvents = ToBpmEvents(v3.bpmEvents);
                    ApplyV3Sliders(chart, v3.sliders, v3.burstSliders);
                    ApplyV3Events(chart, v3.basicBeatmapEvents, v3.colorBoostBeatmapEvents);
                }
                return chart;
            }

            var fallback = FromJson<ChartJsonV2>(mapJson);
            if (fallback == null) return chart;
            chart._version = fallback._version ?? "";
            chart._notes = fallback._notes ?? new List<NoteModel>();
            chart._obstacles = fallback._obstacles ?? new List<ObstacleModel>();
            return chart;
        }

        /// <summary>Erkennt das Beatmap-Format anhand der _version bzw. der Feldnamen.</summary>
        private static int DetectMapVersion(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return 0;
            var match = System.Text.RegularExpressions.Regex.Match(
                json, "\"_version\"\\s*:\\s*\"(\\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int major)
                && major >= 2 && major <= 4)
            {
                return major;
            }
            // Ohne _version: an Feldnamen erkennen
            if (json.Contains("\"_notes\"")) return 2;
            if (json.Contains("\"_colorNotes\"")) return 3;
            if (json.Contains("\"colorNotes\"")) return 4;
            return 0;
        }

        /// <summary>Füllt das Chart aus v3/v4-Daten (kurze Feldnamen) — 1:1 wie v2 weiterverarbeitbar.</summary>
        private static void ApplyV3Chart(
            BeatmapChart chart, SongMeta meta,
            List<V3Note> colorNotes, List<V3Bomb> bombNotes, List<V3Obstacle> obstacles,
            ChartCustomData customData)
        {
            var notes = new List<NoteModel>();
            if (colorNotes != null)
            {
                foreach (var n in colorNotes)
                {
                    notes.Add(new NoteModel
                    {
                        _time = n.b,
                        _lineIndex = n.x,
                        _lineLayer = n.y,
                        _type = n.c,          // 0 = rot/links, 1 = blau/rechts
                        _cutDirection = n.d,  // 0..8 (8 = Dot)
                        _angleOffset = n.a,   // v3/v4: zusätzlicher Winkel-Offset in Grad
                        _customData = n.customData, // Noodle (v3/v4)
                    });
                }
            }
            if (bombNotes != null)
            {
                foreach (var bomb in bombNotes)
                {
                    notes.Add(new NoteModel
                    {
                        _time = bomb.b,
                        _lineIndex = bomb.x,
                        _lineLayer = bomb.y,
                        _type = 3,           // Bombe (v2-Konvention)
                        _cutDirection = 8,   // beliebig
                    });
                }
            }
            notes.Sort((a, b) => a._time.CompareTo(b._time));
            chart._notes = notes;

            var walls = new List<ObstacleModel>();
            if (obstacles != null)
            {
                foreach (var o in obstacles)
                {
                    walls.Add(new ObstacleModel
                    {
                        _time = o.b,
                        _lineIndex = o.x,
                        _type = o.y >= 2 ? 1 : 0, // y=2 => Kauer-Wand
                        _duration = o.d,
                        _width = Mathf.Max(1, o.w),
                        // Vertikale Lage aus der Map übernehmen (Unterkante/Höhe in Rasterhöhen).
                        _lineLayer = Mathf.Max(0, o.y),
                        _height = Mathf.Max(0, o.h),
                    });
                }
            }
            chart._obstacles = walls;

            // NJS kann (wie bei v2) auch am Anfang der Difficulty-.dat stehen
            if (customData != null && customData._noteJumpMovementSpeed > 0f)
            {
                meta.NoteJumpMovementSpeed = customData._noteJumpMovementSpeed;
                meta.NoteJumpStartBeatOffset = customData._noteJumpStartBeatOffset;
            }
            ApplyChartEnvColors(meta, customData);
        }

        /// <summary>
        /// Liest v3/v4-Bögen (sliders) und -Ketten (burstSliders) in die einheitlichen
        /// ArcModel/ChainModel und verknüpft die Chain-Kopfnote: In v3/v4 ist der Kopf eine
        /// echte colorNote am selben Beat/Feld/Farbe (ArcViewer `NoteManager.CheckChainHead`).
        /// Fehlt sie (defekte Map), bleibt der Kopf unverknüpft — die Kette wird dann nicht
        /// gespawnt (kein Absturz).
        /// </summary>
        private static void ApplyV3Sliders(
            BeatmapChart chart, List<V3Slider> sliders, List<V3BurstSlider> burstSliders)
        {
            if (sliders != null)
            {
                chart.Sliders = new List<ArcModel>(sliders.Count);
                foreach (var s in sliders)
                {
                    chart.Sliders.Add(new ArcModel
                    {
                        _headTime = s.b,
                        _headLineIndex = s.x,
                        _headLineLayer = s.y,
                        _headColorType = s.c,
                        _headCutDirection = s.d,
                        _headControlPointLengthMultiplier = s.mu,
                        _tailTime = s.tb,
                        _tailLineIndex = s.tx,
                        _tailLineLayer = s.ty,
                        _tailCutDirection = s.tc,
                        _tailControlPointLengthMultiplier = s.tmu,
                        _sliderMidAnchorMode = s.m,
                    });
                }
            }

            if (burstSliders != null)
            {
                chart.Chains = new List<ChainModel>(burstSliders.Count);
                foreach (var b in burstSliders)
                {
                    chart.Chains.Add(new ChainModel
                    {
                        _time = b.b,
                        _lineIndex = b.x,
                        _lineLayer = b.y,
                        _colorType = b.c,
                        _cutDirection = b.d,
                        _tailTime = b.tb,
                        _tailLineIndex = b.tx,
                        _tailLineLayer = b.ty,
                        _sliceCount = b.sc,
                        _squishAmount = b.s,
                    });
                }
            }

            // Chain-Kopfnoten markieren (v3/v4: der Kopf existiert als colorNote).
            foreach (var chain in chart.Chains)
            {
                NoteModel head = null;
                foreach (var note in chart._notes)
                {
                    if (note._isChainHead) continue;
                    if (Mathf.Abs(note._time - chain._time) > 0.001f) continue;
                    if (note._lineIndex != chain._lineIndex || note._lineLayer != chain._lineLayer) continue;
                    if (note._type != chain._colorType) continue;
                    head = note;
                    break;
                }
                if (head != null)
                {
                    head._isChainHead = true;
                    chain._headNote = head;
                }
            }
        }

        /// <summary>
        /// Wandelt eine v2-Zeit (Beats) in Spielzeit-Sekunden um. Das ist nur der Fall für
        /// EINEN konstanten BPM (keine Events in <see cref="BuildBeatToSeconds"/>); die
        /// Beat-Zeiten der Maps sind Beats, nicht Sekunden.
        /// </summary>
        public static float BeatsToSeconds(float beats, float bpm)
        {
            float safeBpm = bpm > 0 ? bpm : 60f;
            return beats * (60f / safeBpm);
        }

        /// <summary>Lightshow-Events (v2) in einheitliche Modelle übernehmen.</summary>
        private static void ApplyV2Events(BeatmapChart chart, List<V2Event> events)
        {
            if (events == null) return;
            foreach (var e in events)
            {
                var model = new LightEventModel
                {
                    _time = e._time,
                    _type = e._type,
                    _value = e._value,
                    _floatValue = e._floatValue,
                };
                float[] col = null;
                if (e._customData != null)
                    col = e._customData.color ?? e._customData._color;
                if (TryColor(col, out Color c)) { model._hasCustomColor = true; model._customColor = c; }
                chart.LightEvents.Add(model);
            }
        }

        /// <summary>Lightshow-Events (v3/v4) + Color-Boost in einheitliche Modelle übernehmen.</summary>
        private static void ApplyV3Events(BeatmapChart chart,
            List<V3Event> events, List<V3BoostEvent> boostEvents)
        {
            if (events != null)
            {
                foreach (var e in events)
                {
                    var model = new LightEventModel
                    {
                        _time = e.b,
                        _type = e.t,
                        _value = e.i,
                        _floatValue = e.f,
                    };
                    if (e.customData != null && TryColor(e.customData.color, out Color c))
                    {
                        model._hasCustomColor = true;
                        model._customColor = c;
                    }
                    chart.LightEvents.Add(model);
                }
            }
            if (boostEvents != null)
            {
                foreach (var b in boostEvents)
                {
                    chart.BoostEvents.Add(new ColorBoostModel { _time = b.b, _boost = b.o });
                }
            }
        }

        /// <summary>Chroma-/Env-Farb-Array [r,g,b(,a)] → Color.</summary>
        private static bool TryColor(float[] a, out Color c)
        {
            c = Color.white;
            if (a == null || a.Length < 3) return false;
            c = new Color(a[0], a[1], a[2], a.Length > 3 ? a[3] : 1f);
            return true;
        }

        /// <summary>Umgebungsfarben robust aus dem ROHEN info-JSON lesen — akzeptiert die
        /// Array-Form (`[r,g,b]`) UND die Objekt-Form (`{r,g,b}`, ChroMapper-Stil, z. B.
        /// „Radiant"/374e3). Nimmt das erste Vorkommen je Schlüssel (Maps setzen die Farben
        /// je Schwierigkeit identisch).</summary>
        private static void ApplyRawEnvColors(SongMeta meta, string json)
        {
            if (meta == null || string.IsNullOrEmpty(json)) return;
            if (TryRawColor(json, "_envColorLeft", out var cl)) { meta.EnvColorLeft = cl; meta.HasEnvironmentColors = true; }
            if (TryRawColor(json, "_envColorRight", out var cr)) { meta.EnvColorRight = cr; meta.HasEnvironmentColors = true; }
            if (TryRawColor(json, "_envColorLeftBoost", out var lb)) { meta.EnvColorLeftBoost = lb; meta.HasEnvironmentColors = true; }
            if (TryRawColor(json, "_envColorRightBoost", out var rb)) { meta.EnvColorRightBoost = rb; meta.HasEnvironmentColors = true; }
        }

        private static bool TryRawColor(string json, string key, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrEmpty(json)) return false;
            string k = "\"" + key + "\""; // Anführungszeichen: matcht NICHT „_envColorLeftBoost"
            int i = json.IndexOf(k, StringComparison.Ordinal);
            if (i < 0) return false;
            int j = i + k.Length;
            int arr = json.IndexOf('[', j);
            int obj = json.IndexOf('{', j);
            if (arr < 0 && obj < 0) return false;
            bool useArray = arr >= 0 && (obj < 0 || arr < obj);
            string tail = json.Substring(j);
            Match m = useArray
                ? Regex.Match(tail, @"^\s*:\s*\[\s*(-?[0-9.eE+]+)\s*,\s*(-?[0-9.eE+]+)\s*,\s*(-?[0-9.eE+]+)")
                : Regex.Match(tail, @"^\s*:\s*\{\s*""r""\s*:\s*(-?[0-9.eE+]+)\s*,\s*""g""\s*:\s*(-?[0-9.eE+]+)\s*,\s*""b""\s*:\s*(-?[0-9.eE+]+)");
            if (!m.Success) return false;
            if (!float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float r)) return false;
            if (!float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float g)) return false;
            if (!float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float b)) return false;
            c = new Color(r, g, b, 1f);
            return true;
        }

        private static bool TryHex(string hex, out Color c)
        {
            c = Color.white;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out c);
        }

        /// <summary>Map-Umgebungsfarben aus der info (v2/v3 `_customData` bzw. v4 Hex).</summary>
        private static void ApplyInfoEnvColors(SongMeta meta, InfoJson info)
        {
            if (info == null) return;
            var cd = info._customData;
            if (cd != null)
            {
                if (TryColor(cd._envColorLeft, out var cl)) { meta.EnvColorLeft = cl; meta.HasEnvironmentColors = true; }
                if (TryColor(cd._envColorRight, out var cr)) { meta.EnvColorRight = cr; meta.HasEnvironmentColors = true; }
                if (TryColor(cd._envColorLeftBoost, out var lb)) { meta.EnvColorLeftBoost = lb; meta.HasEnvironmentColors = true; }
                if (TryColor(cd._envColorRightBoost, out var rb)) { meta.EnvColorRightBoost = rb; meta.HasEnvironmentColors = true; }
            }
            // v4: Hex-Strings (`environmentColor0/1` + Boost).
            if (TryHex(info.environmentColor0, out var h0)) { meta.EnvColorLeft = h0; meta.HasEnvironmentColors = true; }
            if (TryHex(info.environmentColor1, out var h1)) { meta.EnvColorRight = h1; meta.HasEnvironmentColors = true; }
            if (TryHex(info.environmentColor0Boost, out var hb0)) { meta.EnvColorLeftBoost = hb0; meta.HasEnvironmentColors = true; }
            if (TryHex(info.environmentColor1Boost, out var hb1)) { meta.EnvColorRightBoost = hb1; meta.HasEnvironmentColors = true; }
        }

        /// <summary>Map-Umgebungsfarben aus der Difficulty-_customData (überschreiben die info).</summary>
        private static void ApplyChartEnvColors(SongMeta meta, ChartCustomData cd)
        {
            if (meta == null || cd == null) return;
            if (TryColor(cd._envColorLeft, out var cl)) { meta.EnvColorLeft = cl; meta.HasEnvironmentColors = true; }
            if (TryColor(cd._envColorRight, out var cr)) { meta.EnvColorRight = cr; meta.HasEnvironmentColors = true; }
            if (TryColor(cd._envColorLeftBoost, out var lb)) { meta.EnvColorLeftBoost = lb; meta.HasEnvironmentColors = true; }
            if (TryColor(cd._envColorRightBoost, out var rb)) { meta.EnvColorRightBoost = rb; meta.HasEnvironmentColors = true; }
        }

        private static List<BeatBpmPoint> ToBpmEvents(List<V3BpmEvent> events)
        {
            var result = new List<BeatBpmPoint>();
            if (events == null) return result;
            foreach (var e in events)
            {
                if (e.m > 0f) result.Add(new BeatBpmPoint { Beat = e.b, Bpm = e.m });
            }
            return result;
        }

        public static Func<float, float> BuildBeatToSeconds(
            List<BeatBpmPoint> events, float defaultBpm)
        {
            float baseBpm = defaultBpm > 0 ? defaultBpm : 60f;
            var pts = new List<BeatBpmPoint>();
            if (events != null)
            {
                foreach (var p in events)
                {
                    if (p.Bpm > 0f && p.Beat > 0f) pts.Add(p);
                }
            }
            pts.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            if (pts.Count == 0)
            {
                return beats => BeatsToSeconds(beats, baseBpm); // konstant (bisheriges Verhalten)
            }

            // Kumulierte Sekunden an jedem Wechselpunkt vorberechnen
            float spb = 60f / baseBpm;
            var cum = new float[pts.Count];
            float prevBeat = 0f;
            float prevSpb = spb;
            for (int i = 0; i < pts.Count; i++)
            {
                cum[i] = (i > 0 ? cum[i - 1] : 0f) + (pts[i].Beat - prevBeat) * prevSpb;
                prevBeat = pts[i].Beat;
                prevSpb = 60f / pts[i].Bpm;
            }

            return beats =>
            {
                if (beats <= pts[0].Beat) return beats * spb;
                // Segment mit einem Beat <= beats finden (binär, list ist sortiert)
                int lo = 0, hi = pts.Count - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (pts[mid].Beat <= beats) lo = mid;
                    else hi = mid - 1;
                }
                return cum[lo] + (beats - pts[lo].Beat) * (60f / pts[lo].Bpm);
            };
        }

        public static SongMeta CreateDemoMeta()
        {
            var asset = Resources.Load<TextAsset>(GameConfig.DemoInfoResource);
            if (asset != null)
            {
                var info = FromJson<InfoJson>(asset.text);
                if (info != null)
                {
                    var meta = BuildMetaFromInfo(GameConfig.DemoFolderName, "", info, true,
                        GameConfig.DemoTrackFileName, "", asset.text);
                    // null = info ohne "Standard"-Characteristic (dann ist das Lied nicht spielbar).
                    if (meta != null) return meta;
                }
                Debug.LogWarning("[BeatSaber] Demo-info.json unlesbar/ohne Standard-Schwierigkeiten"
                                 + " -> Notfall-Meta (nur 'Expert').");
            }
            else
            {
                Debug.LogError($"[BeatSaber] Demo-info fehlt in Resources: {GameConfig.DemoInfoResource}");
            }

            // Notfall-Meta: eine einzelne Schwierigkeit, damit das Spiel ohne Absturz startet
            // (die Map wird danach wie üblich geladen — fehlt auch sie, bleibt der Chart leer).
            var fallback = new SongMeta
            {
                FolderName = GameConfig.DemoFolderName,
                FolderPath = "",
                SongName = GameConfig.DemoSongName,
                SongAuthorName = GameConfig.DemoSongAuthor,
                SongFileName = GameConfig.DemoTrackFileName,
                BeatsPerMinute = GameConfig.DemoBpm,
                IsDemo = true,
                AudioPath = "",
            };
            fallback.Difficulties.Add(new SongDifficulty
            {
                Name = GameConfig.DemoDifficultyName,
                Rank = DifficultyRank(GameConfig.DemoDifficultyName),
                BeatmapFileName = GameConfig.DemoMapResourcePrefix + GameConfig.DemoDifficultyName,
                Key = SongMeta.DemoKey + "|" + GameConfig.DemoDifficultyName,
            });
            fallback.LegacyDefaultIndex = 0;
            fallback.SelectedDifficulty = 0;
            fallback.ApplySelectedDifficulty();
            return fallback;
        }

        // ---------- Hilfsfunktionen ----------

        private static int DifficultyRank(string difficulty)
        {
            switch ((difficulty ?? "").ToLowerInvariant())
            {
                case "easy": return 1;
                case "normal": return 2;
                case "hard": return 3;
                case "expert": return 4;
                case "expertplus": return 5;
                default: return 0;
            }
        }

        private static string FindAudioFile(string folderPath, string preferredName)
        {
            // Bevorzugt exakt, dann erste Audiodatei (.egg = OGG-Inhalt, BeatSaver-Standard)
            if (!string.IsNullOrEmpty(preferredName))
            {
                string exact = Path.Combine(folderPath, preferredName);
                if (File.Exists(exact)) return exact;
            }

            var candidates = Directory.GetFiles(folderPath, "*.*")
                .Where(f => f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".egg", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return candidates.Count > 0 ? candidates[0] : null;
        }

        /// <summary>Lädt einen Text-Asset (JSON) aus Resources — eingebautes Lied.</summary>
        private static string ReadResourceText(string resourceName)
        {
            var ta = Resources.Load<TextAsset>(resourceName);
            if (ta == null) throw new FileNotFoundException("Ressource fehlt in Resources: " + resourceName);
            return ta.text;
        }

        private static T FromJson<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return default;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BeatSaber] JSON-Fehler: {ex.Message}");
                return default;
            }
        }
    }
}
