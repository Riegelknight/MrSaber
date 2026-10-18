using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber
{
    /// <summary>Eine Note im BeatSaver-v2-Format.</summary>
    [Serializable]
    public sealed class NoteModel
    {
        public float _time;        // in Beats (Umrechnung über BeatmapParser.BuildBeatToSeconds)
        public int _lineIndex;     // 0..3 (Spalte)
        public int _lineLayer;     // 0..2 (Reihe)
        public int _type;          // 0 = rot/links, 1 = blau/rechts, 3 = Bombe
        public int _cutDirection;  // 0..8 Pfeilrichtung

        /// <summary>v3/v4: Winkel-Offset „a" in Grad, wird auf die Pfeilrichtung addiert
        /// (ArcViewer `CalculateObjectAngle(d, a)`). v2 kennt das Feld nicht → 0.</summary>
        public float _angleOffset;

        /// <summary>Vom Parser gesetzt: Diese Note ist der Kopf einer v3/v4-Chain
        /// (burstSlider). Sie wird als Chain-Kopf gewertet (max. 85 statt 115: kein
        /// Durchzug) und startet die Chain-Glieder. Nicht persistiert.</summary>
        [NonSerialized] public bool _isChainHead;

        /// <summary>Noodle-Extensions-Custom-Data (v2 `_customData` / v3/v4 `customData`).
        /// Position/Rotation/Flip/Scale verändern das Spawn-Verhalten.</summary>
        public NoteCustomData _customData;
    }

    /// <summary>
    /// Noodle-Extensions-Custom-Data einer Note (Auszug: Position/Rotation/Scale/Flip).
    /// v3/v4 nutzen unprefixierte Namen, v2 die „_"-Variante — beide stehen als Felder bereit;
    /// die Properties wählen die vorhandene Quelle. Nicht vorhandene Felder sind null/false.
    /// </summary>
    [Serializable]
    public sealed class NoteCustomData
    {
        // ---- v3/v4 ----
        public float[] coordinates;      // [x, y] (Noodle-Gitter, fraktional)
        public float[] worldRotation;    // [x, y, z, w] Quaternion
        public float[] localRotation;    // [x, y, z, w] Quaternion
        public float[] scale;            // [x, y, z]
        public bool flip;
        // ---- v2 ----
        public float[] _position;        // [x, y]
        public float[] _rotation;        // [x, y, z, w]
        public float[] _localRotation;   // [x, y, z, w]
        public float[] _scale;           // [x, y, z]
        public bool _flip;

        public float[] Position => coordinates ?? _position;
        public float[] WorldRotation => worldRotation ?? _rotation;
        public float[] LocalRotation => localRotation ?? _localRotation;
        public float[] Scale => scale ?? _scale;
        public bool Flip => flip || _flip;

        public bool HasPosition => Position != null && Position.Length >= 2;
        public bool HasWorldRotation => WorldRotation != null && WorldRotation.Length >= 4;
        public bool HasLocalRotation => LocalRotation != null && LocalRotation.Length >= 4;
        public bool HasScale => Scale != null && Scale.Length >= 3;
    }

    /// <summary>Ein Hindernis (_obstacles) im v2-Format.</summary>
    [Serializable]
    public sealed class ObstacleModel
    {
        public float _time;        // in Beats
        public int _lineIndex;     // Startspalte
        public int _type;          // 0 = volle Wand, 1 = Kauer-Wand
        public float _duration;    // in Beats
        public int _width;         // in Spalten
        /// <summary>Untere Zeile des Hindernisses in Rasterhöhen (0 = Boden). v3/v4 aus
        /// „y"; bei v2 (0) wird sie aus _type abgeleitet (Kauer-Wand = 2).</summary>
        public int _lineLayer;
        /// <summary>Höhe des Hindernisses in Rasterhöhen (v3/v4 aus „h"). 0 = aus _type ableiten.</summary>
        public int _height;
    }

    /// <summary>Ein BPM-Event (v2: _BPMChanges / v3+v4: bpmEvents) für die Beat→Zeit-Umrechnung.</summary>
    public sealed class BeatBpmPoint
    {
        public float Beat;   // Beat-Zeitpunkt des Wechsels (v2: _time, v3/v4: b)
        public float Bpm;    // BPM ab diesem Zeitpunkt (v2: _BPM, v3/v4: m)
    }

    /// <summary>Beatmap-Chart (v2/v3/v4 .dat, einheitlich als v2-Modelle).</summary>
    [Serializable]
    public sealed class BeatmapChart
    {
        public string _version;
        public List<NoteModel> _notes = new();
        public List<ObstacleModel> _obstacles = new();

        /// <summary>BPM-Verlauf der Map (leer = konstantes Info-BPM). Wird vom Parser befüllt
        /// und bestimmt die exakte Beat→Sekunden-Umrechnung (sonst verrutschen alle Noten
        /// nach einem BPM-Wechsel → Timing-Fehler → immer "MISS").</summary>
        public List<BeatBpmPoint> BpmEvents = new();

        // Arcs (v2.1 "_sliders") und Chains (v2.1 "_chains") werden gelesen, aber
        // (noch) nicht gespielt - die Architektur schliesst sie nicht aus.
        [NonSerialized] public List<ArcModel> Sliders = new();
        [NonSerialized] public List<ChainModel> Chains = new();

        /// <summary>Lightshow-Basis-Events (v2 "_events" / v3+v4 "basicBeatmapEvents"):
        /// steuern Farben/Ringe/Laser der Umgebung (Modus „Animiert").</summary>
        [NonSerialized] public List<LightEventModel> LightEvents = new();
        /// <summary>Color-Boost-Events (v4 "colorBoostBeatmapEvents" / v2 _customData).</summary>
        [NonSerialized] public List<ColorBoostModel> BoostEvents = new();
    }

    /// <summary>Ein Lightshow-Event (Beat, Typ, Wert, Float-Wert).</summary>
    public sealed class LightEventModel
    {
        public float _time;         // Beat-Zeitpunkt (v2 _time / v3+v4 b)
        public int _type;           // LightEventType (v2 _type / v3+v4 t)
        public int _value;          // LightEventValue (v2 _value / v3+v4 i)
        public float _floatValue = 1f; // Helligkeit (v2 _floatValue / v3+v4 f)
        // Chroma: per-Event-Farbe (`customData.color`/`_customData._color`).
        public bool _hasCustomColor;
        public Color _customColor = Color.white;
    }

    /// <summary>Ein Color-Boost-Event (Beat + an/aus).</summary>
    public sealed class ColorBoostModel
    {
        public float _time;         // Beat (v3/v4 b)
        public bool _boost;         // v3/v4 o
    }

    /// <summary>Ein Bogen (v2.1 "_sliders" / v3+v4 "sliders"): verbindet Kopf- und Endpunkt.
    /// Die Kopf-/Endnoten existieren separat als colorNotes und werden normal gewertet; der
    /// Bogen selbst ist reine Optik (ArcViewer `ArcManager`).</summary>
    [Serializable]
    public sealed class ArcModel
    {
        public float _headTime;                 // b
        public int _headLineIndex;              // x
        public int _headLineLayer;              // y
        public int _headColorType;              // c (0=rot/links, 1=blau/rechts)
        public float _headCutDirection;         // d (0..8)
        public float _headControlPointLengthMultiplier; // mu (v2.1: _headControlPointLengthMultiplier)
        public float _tailTime;                 // tb
        public int _tailLineIndex;              // tx
        public int _tailLineLayer;              // ty
        public float _tailCutDirection;         // tc (v3/v4) / _tailCutDirection (v2.1)
        public float _tailControlPointLengthMultiplier; // tmu
        public int _sliderMidAnchorMode;        // m (0 = keine Bogenmitte)

        /// <summary>Zur Laufzeit gesetzt (nicht persistiert): An Kopf bzw. Ende sitzt eine
        /// Note (gleiche Zeit + Rasterfeld). Dann verschiebt ArcView den Punkt um
        /// <see cref="GameConfig.ArcAttachOffset"/> in Bogenrichtung (ArcViewer
        /// ObjectManager `halfNoteOffset`), damit der Bogen an der Notenkante endet.</summary>
        [System.NonSerialized] public bool _headAttached;
        [System.NonSerialized] public bool _tailAttached;
    }

    /// <summary>Eine Kette (v2.1 "_chains" / v3+v4 "burstSliders"): Kopfnote (echte colorNote)
    /// plus erzeugte Glieder zwischen Kopf- und Endpunkt (ArcViewer `ChainManager`).</summary>
    [Serializable]
    public sealed class ChainModel
    {
        public float _time;             // b = Kopf-Beat
        public int _lineIndex;          // x
        public int _lineLayer;          // y
        public int _colorType;          // c
        public float _cutDirection;     // d
        public float _tailTime;         // tb (v3/v4; v2.1 hat keinen Endpunkt)
        public int _tailLineIndex;      // tx
        public int _tailLineLayer;      // ty
        public float _sliceCount;       // sc (Anzahl Segmente; Kopf + sc-1 Glieder)
        public float _squishAmount;     // s (0/fehlend = 1)

        /// <summary>Vom Parser verknüpfte Kopfnote (v3/v4: existiert als colorNote).
        /// Nicht persistiert.</summary>
        [NonSerialized] public NoteModel _headNote;
    }

    /// <summary>Eine spielbare Schwierigkeit eines Lieds (nur Standard-Characteristic).</summary>
    public sealed class SongDifficulty
    {
        public string Name = "";               // "Easy".."ExpertPlus"
        public int Rank;                       // 1..5 (Klassisches Beat-Saber-Schema)
        public string BeatmapFileName = "";    // .dat im Liedordner
        public float NoteJumpMovementSpeed = 0f;   // NJS dieser Difficulty (0 = Map gibt nichts an)
        public float NoteJumpStartBeatOffset = 0f; // Spawn-Versatz in Beats
        public string Key;                     // SongKey + "|" + Name (Highscore-Schlüssel)

        /// <summary>Sichtbare Kurzbezeichnung (gelesen aus der info).</summary>
        public string DisplayName => string.IsNullOrEmpty(Name) ? "Normal" : Name;
    }

    /// <summary>Angaben aus der info.json (v2).</summary>
    public sealed class SongMeta
    {
        public string FolderName = "";
        public string FolderPath = "";             // absoluter Liedordner-Pfad (für MapPath je Difficulty)
        public string SongName = "";
        public string SongSubName = "";
        public string SongAuthorName = "";
        public string SongFileName = "";       // OGG/MP3 im Liedordner
        public string MapFileName = "";        // .dat der AKTUELL gewählten Difficulty
        public float BeatsPerMinute = 120f;
        /// <summary>Umgebungs-Name der Map (info.json `_environmentName` bzw. v3/v4
        /// `environmentNames`). Bestimmt die ArcViewer-Hintergrund-Umgebung; Fallback
        /// DefaultEnvironment.</summary>
        public string EnvironmentName = "DefaultEnvironment";

        /// <summary>Map-eigene Umgebungsfarben (`_envColorLeft/Right` + Boost aus der info
        /// bzw. Difficulty-_customData). Wenn gesetzt, überschreiben sie die Default-Palette
        /// der Umgebung (wie im Original/ArcViewer).</summary>
        public bool HasEnvironmentColors;
        public Color EnvColorLeft = new Color(0.85f, 0.085f, 0.085f);
        public Color EnvColorRight = new Color(0.1882353f, 0.675294f, 1f);
        public Color EnvColorLeftBoost = new Color(0.85f, 0.085f, 0.085f);
        public Color EnvColorRightBoost = new Color(0.1882353f, 0.675294f, 1f);
        public bool IsDemo = false;
        public string AudioPath = "";          // absoluter Pfad der Audiodatei (kein Demo)
        public string MapPath = "";            // absoluter Pfad der .dat der gewählten Difficulty

        // NJS / Spawn-Verhalten (gilt für die gewählte Difficulty; 0 = Map gibt nichts an)
        public float NoteJumpMovementSpeed = 0f;   // Sprungtempo der Notes in m/s
        public float NoteJumpStartBeatOffset = 0f; // Spawn-Versatz in Beats

        /// <summary>Alle spielbaren Standard-Schwierigkeiten (aufsteigend nach Rang).</summary>
        public List<SongDifficulty> Difficulties = new();
        /// <summary>Index der aktuell gewählten Schwierigkeit (wird persistiert).</summary>
        public int SelectedDifficulty;
        public int LegacyDefaultIndex;

        /// <summary>Die aktuell gewählte Schwierigkeit (null, falls keine vorhanden).</summary>
        public SongDifficulty CurrentDifficulty =>
            Difficulties != null && SelectedDifficulty >= 0 && SelectedDifficulty < Difficulties.Count
                ? Difficulties[SelectedDifficulty]
                : null;

        /// <summary>Eindeutiger Highscore-Schlüssel inkl. Difficulty ("Song|Difficulty").</summary>
        public string BestKey
        {
            get
            {
                var d = CurrentDifficulty;
                return d != null ? d.Key : Key;
            }
        }

        /// <summary>Eindeutiger Schlüssel (für Bestwerte).</summary>
        public string Key => IsDemo ? DemoKey : FolderName;
        public const string DemoKey = "__demo__";

        /// <summary>Passt MapFileName/MapPath und NJS an die gewählte Schwierigkeit an (vor LoadSong/ParseChart rufen).</summary>
        public void ApplySelectedDifficulty()
        {
            var d = CurrentDifficulty;
            if (d == null) return;
            MapFileName = d.BeatmapFileName;
            MapPath = string.IsNullOrEmpty(FolderPath) ? d.BeatmapFileName
                : System.IO.Path.Combine(FolderPath, d.BeatmapFileName);
            NoteJumpMovementSpeed = d.NoteJumpMovementSpeed;
            NoteJumpStartBeatOffset = d.NoteJumpStartBeatOffset;
        }
    }

    /// <summary>Eintrag eines Bestwerts je Lied (Key = "Song|Difficulty").</summary>
    [Serializable]
    public sealed class ScoreRecord
    {
        public string Key;
        public int Score;
    }

    /// <summary>Persistierte Schwierigkeits-Wahl je Lied (global, nicht je Spieler).</summary>
    [Serializable]
    public sealed class SongDiffChoice
    {
        public string SongKey;
        public string Difficulty;
    }

    /// <summary>Spielerprofil mit Bestwerten je Lied und eigener Raum-Skalierung.</summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public string Name;
        public float HeightCm = 175f;
        /// <summary>Raum-Skalierung (1 = Referenzgröße). Wird pro Spieler gespeichert und
        /// skaliert das komplette Spiel (Noten/Wände/HUD/Schwerter).</summary>
        public float Scale = 1f;
        /// <summary>„Lauf-Tempo" (Run-and-Play, persönlich, persistent): eigener
        /// Multiplikator auf die aus Raumgeschwindigkeit × Notengeschwindigkeitsfaktor
        /// berechnete Spielgeschwindigkeit. Default 1,0 = Noten exakt statisch; Bereich
        /// 0,2…3,0 (GameConfig.RunComfort*).</summary>
        public float RunComfort = GameConfig.RunComfortDefault;
        public List<ScoreRecord> BestScores = new();

        public int GetBest(string songKey)
        {
            foreach (var r in BestScores)
            {
                if (r.Key == songKey) return r.Score;
            }
            return 0;
        }

        public void SetBest(string songKey, int score)
        {
            foreach (var r in BestScores)
            {
                if (r.Key == songKey)
                {
                    if (score > r.Score) r.Score = score;
                    return;
                }
            }
            BestScores.Add(new ScoreRecord { Key = songKey, Score = score });
        }
    }

    /// <summary>Persistenz-Container (JSON).</summary>
    [Serializable]
    public sealed class SaveData
    {
        public List<PlayerProfile> Players = new();
        public string SelectedPlayer = "";
        // Audio-Offset (Menü „Audio-Offset"): verschiebt AUSSCHLIESSLICH die hörbare Musik
        // relativ zu den Noten. START bei 0 — die am PICO 4 kalibrierten 85 ms stecken jetzt
        // in der Noten-Uhr (GameConfig.AudioLatencyBaseMs = 275). Synchron ist das Spiel bei
        // AudioOffset == AudioLatencyBaseMs (die Basis-Verschiebung AudioOffsetBaseMs kürzt
        // sich heraus); 0 = keinerlei harter Offset mehr. Auf anderen Geräten/Ausgängen
        // (z.B. Bluetooth) im Menü nachjustierbar.
        public float SoundDelayMs = 0f;
        /// <summary>Einmal-Migration: Der frühere harte PICO-Kalibrierwert (−85 ms) ist in
        /// <see cref="AudioLatencyBaseMs"/> eingebacken. Beim ersten Start nach dem Umbau
        /// wird ein gespeicherter Offset deshalb einmalig auf 0 gezogen.</summary>
        public bool SoundDelayBakedIn = false;
        public bool PassthroughDisabled = false;
        public int BackgroundMode = 0;
        /// <summary>„Raum folgt Spieler" (Menü, global, persistent): true = der virtuelle Raum
        /// verschiebt sich mit der Vorwärts-/Rückwärtsbewegung des Spielers (Tiefen-Folge).
        /// false = der Raum steht räumlich fest.</summary>
        public bool RoomFollowsPlayer = false;
        /// <summary>„Run and Play" (Menü, global, persistent): true = Spiel- UND
        /// Audiogeschwindigkeit sind an die physische Vorwärtsbewegung gekoppelt;
        /// „Raum folgt Spieler" wird zwangsweise eingeschaltet und gesperrt. Siehe
        /// GameConfig/`RunAndPlay`.</summary>
        public bool RunAndPlay = false;
        /// <summary>Vertikaler Versatz des Noten-/Minenfelds und bodenfreier Wände in Metern
        /// („Feldhöhe" im Menü, global, persistent, ±1,5 m in 0,05-m-Schritten). 0 = reines
        /// ArcViewer-Raster. Wirkt auf die Treffer-/Score-Position mit (Notenbox = Anzeige;
        /// bodenständige Wände bleiben unverändert).</summary>
        public float FieldHeightMeters = 0f;
        /// <summary>Gewählte Schwierigkeit je Lied (bleibt nach Neustart/Wechsel aktiv).</summary>
        public List<SongDiffChoice> DifficultyChoices = new();
    }
}
