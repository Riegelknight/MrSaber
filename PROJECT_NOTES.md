# MrSaber — Beat-Saber-Klon für PICO 4 (Projekt-Notizen)

> **Zweck dieser Datei:** Einer externen KI (oder einem neuen Entwickler) den Einstieg
> zu ermöglichen: Architektur, Kern-Invarianten, Änderungsregeln und Versionsstände —
> kompakt und ohne dass der gesamte Code gelesen werden muss.
> **Lesereihenfolge:** diese Datei → `Assets/Scripts/BeatSaber/Core/ProjectInfo.cs`
> (Versionskonstanten) → die Kopf-Kommentare der jeweiligen Skript-Datei.

---

## 1. Überblick

Klon des VR-Spiels *Beat Saber* für die **PICO 4** (Android, MR): Der Spieler schneidet
mit zwei Lichtschwertern Noten aus der Musik und weicht Wänden aus. Die Welt erscheint
über **Passthrough** (Video-Seethrough) im realen Raum.

- Gespielt wird **nur mit physischen PICO-4-Controllern** (keine Handtracking-Steuerung).
- Noten, Wände und Punktanzeige sind seit der letzten Änderungsrunde **raumfest**:
  Kopfbewegungen verschieben die Spielwelt nicht mehr (Details in §4 „Kern-Invarianten").
- Scoring = **Original-Beat-Saber-Verteilung** (70 Anlauf + 30 Durchzug + 15 Mitte = **115**
  Punkte pro Note), Multiplikator x1 → x2 → x4 → x8 (x2 ab 2, x4 ab 6, x8 ab 14 Treffern —
  wie Original/ArcViewer). **Gemessen** wird aber wie in OpenSaberPlus: größte Änderung der
  **Klingenrichtung** in einem **±200-ms-Fenster** um den **Schnitt-Moment (Klingenkontakt)**
  (Anlauf voll bei
  **100°**, Durchzug voll bei **60°**, **ohne Totzone**) — richtungsunabhängig, damit gerade und
  diagonale Schnitte gleicher Qualität gleich zählen. **Arcs** (v3/v4 `sliders`) sind reine
  Optik; **Chains** (v3/v4 `burstSliders`) geben am Kopf max. **85** (kein Durchzug) und an
  jedem Glied fest **20** × Multiplikator. Siehe Invariante 6.

---

## 2. Versionsstände (SDK, Unity, PICO)

| Komponente | Version | Wo dokumentiert |
|---|---|---|
| **Unity Editor** | **2022.3.62f3** (Rev. 96770f904ca7) | `ProjectSettings/ProjectVersion.txt` |
| **Render Pipelines (URP)** | **14.0.12** | `Packages/manifest.json` |
| **Input System** | **1.14.0** | `Packages/manifest.json` |
| **TextMeshPro** | **3.0.9** | `Packages/manifest.json` |
| **XR Interaction Toolkit** | **3.1.2** | `Packages/manifest.json` |
| **XR Management** | **4.5.1** | `Packages/manifest.json` |
| **XR Hands** | **1.5.1** | `Packages/manifest.json` |
| **OpenXR** | **1.14.3** | `Packages/manifest.json` |
| **meta-openxr** | **1.0.4** | `Packages/manifest.json` |
| **AR Foundation** | **5.2.0** | `Packages/manifest.json` |
| **PICO SDK (com.unity.xr.picoxr)** | **3.0.4** (Package) = *PICO Unity Integration SDK release 3.0.0* (lokaler Pfad: `C:/Users/Riegholm/Downloads/PICO-Unity-Integration-SDK-release_3.0.0`) | `Packages/manifest.json` (file:-Referenz), `PXR_System.GetSDKVersion()` zur Laufzeit |
| **Gerät** | **PICO 4** | — |
| **PICO-Firmware (PUI-OS-Version)** | **nicht im Projekt hinterlegt** | auf dem Headset ablesen: *Einstellungen → Über → Systemversion*. Alternativ per `ProjectInfo.LogVersions()` (Android-Log) ergänzen |

> **Hinweis Firmware:** Die Firmware-Version ist eine Geräteeigenschaft und kann nicht aus
> dem Repository ermittelt werden. Tragen Sie sie bei Gerätetests in die Log-Ausgabe ein
> bzw. ergänzen Sie sie in `ProjectInfo.cs`, sobald bekannt.

**Laufzeit-Log:** `ProjectInfo.LogVersions()` (aufgerufen in `GameBootstrap.Boot`)
schreibt beim Spielstart Unity-Version, URP-/XR-Paketversionen und (auf dem Gerät) die
native PICO-SDK-Version in den Unity-Log.

---

## 3. Architektur

Projektstruktur unter `Assets/Scripts/`:

```
BeatSaber/
  Core/
    GameManager.cs      Zentraler Orchestrator (Spielzustand, Spawn, Scoring, Menü, Raum-Verankerung)
    GameConfig.cs       Zentrale Konstanten (Raster, Geschwindigkeiten, Scoring, Layers)
    GameBootstrap.cs    Auto-Start in der SampleScene (erzeugt GameManager)
    ControllerInput.cs  Controller-Eingabe direkt aus UnityEngine.XR.InputDevices
    AudioManager.cs     Musik, Liedzeit (Score-Uhr), Audio-Offset (DSP), SFX
    AudioDelayFilter.cs DSP-Verzögerungskeller (OnAudioFilterRead) NUR für die hörbare
                        Musik — Audio-Offset positiv = Musik später; liegt auf dem
                        Musik-Kind-Objekt [Music] (SFX-Kinder werden nicht berührt)
    RunAndPlay.cs       „Run and Play": pufferte Raumgeschwindigkeit → Zustand + Rate
  Data/
    BeatmapParser.cs    BeatSaver-Maps lesen (v2/v3/v4): colorNotes, bombNotes, obstacles,
                        sliders (Arcs) und burstSliders (Chains); BPM-Verlauf -> Beat-Sekunden
    BeatmapModels.cs    Datenmodelle + Persistenz (SaveData, PlayerProfile, Charts); NoteModel
                        trägt v3/v4-Winkel-Offset + Chain-Kopf-Flag, ArcModel/ChainModel die
                        v3/v4-Slider-Felder
  View/
    SaberView.cs        Schwert (Aufbau, Klingen-Daten/-Richtungs-Historie, Ein-/Ausfahren)
    NoteView.cs         Note/Bombe (Bewegung, Treffer-/Bad-Cut-Fälle, Optik via EzySlice;
                        Bombe EXPLODIERT bei Berührung — Fx.PlayMineExplosion)
    NotePool.cs         Objektpool für Noten
    ArcView.cs          Bogen (v3/v4 slider): Bézier-Band via LineRenderer, reine Optik
    ChainView.cs        Kette (v3/v4 burstSlider): erzeugte Glieder (Position/Treffer/Punkte)
    WallView.cs         Wand (Bewegung, ArcViewer-Optik mit Kanten, Kopf-Kollisions-Test)
    VelocityEstimator.cs Schwert-Geschwindigkeit aus letzten Frames
    Fx.cs               Partikel: Schnitt-Funken (richtungstreu entlang der Schwung-
                        geschwindigkeit, prozedural gepoolt), Minen-Explosion (radialer
                        Weißblitz), Spawn-Partikel an den Notenblöcken (Referenz-Prefabs)
    ControllerDebugView.cs  Sichtbare Tracking-Nullpunkte + PICO-4-Modell an den
                        Controller-Ankern (Menüzeile „Controller anzeigen", Session)
  UI/
    HandMenu.cs         Handgelenk-Menü (Anzeige + Laser) — Logik liegt im GameManager
    BsUi.cs             Deterministische UI-Helfer (Sprites, Font aus Code)
    HudDisplay.cs       Raumfeste Punkte-Anzeige (SCORE/COMBO/x1–x8)
  Environment/          Hintergrund-Option + ArcViewer-Umgebungs-SZENEN
    EnvironmentDirector.cs  Lädt die ArcViewer-Szenen (Assets/ArcViewerEnvironmentScenes,
                        13 Einträge in den Build Settings) additiv unter den RAUM-ROOT
                        (`AttachToRoom` → Bühne als Kind von `_gameRoot`, EINE Pose-/
                        Skalierungs-Quelle), schaltet Passthrough/Default/Animiert,
                        aktiviert Bloom+Grading (nur Default/Animiert, ArcViewer-Werte)
                        und füttert den Bloomfog-Capture (Culling NUR Glow-Ebene 11);
                        die Umgebungs-Shader lesen die Blur-Textur (Nebel + Fake-
                        Reflexionen) — kein NDC-Vollbild-Quad mehr
    AutoLightshow.cs  Standard-Lichtshow für Maps OHNE eigene _lightshowEvents
                        (16/32-Beat-Muster, Boost-Phasen, SuperSaber-Prinzip)
    EnvironmentParametersTable.cs  Ring-/Laser-Parameter je Umgebung (1:1 aus ArcViewer)
    EnvironmentLightManager.cs  Lightshow-Auswertung (Farben/Flash/Fade/Transition,
                        Boost) + globale Tint-Farben + CurrentBackground (Kamera-Clear);
                        LightEmission = 6,5 (ArcViewer) → HDR-Glühgrund für Bloom
    EnvironmentRingManager.cs   Ringspin/-zoom (Event-Typ 8/9) — nur noch Event-Daten
    EnvironmentLightshow.cs     Event-Modelle, Zeitquelle, Farb-Palette, Easings
    EnvironmentColorTable.cs    Farbpaletten je Umgebung
    LightHandler.cs / RingHandler.cs / RotatingLaserHandler.cs / TunnelLaserHandler.cs
      / RingZoomHandler.cs / LaserDataHandler.cs / MainSceneAutoLoad.cs /
      OcclusionMarker.cs  — RESTE der ehemaligen ArcViewer-Szenenbindung; LightHandler
                        setzt die Farben je Handler (nur bei ÄNDERUNG — Perf-Guard)
  Shaders (Aktive, mit Bloomfog-Lesen):
    Assets/ArcViewerEnvironment/Shaders/  LaserShader/TextureLaserShader (BLOOM_HEIGHT_
                        FOG_APPLY), PlatformShader/DiffusePlatformShader (Fake-Reflexion
                        der Bloomfog + Nebel), LaserGlowShader (reine Emission —
                        Bloomfog-QUELLE, wie im Original fog-frei), BloomFog.cginc
    Assets/Resources/BeatSaber/  Spark.shader (Funken/Explosion, additiv HDR),
                        BloomfogSkyboxQuad.shader (NDC-Vollbild-Hintergrund),
                        Shockwave.shader (brachliegend), GripCurvedText.shader
  PICO/
    PicoSeeThrough.cs     PICO Video-Seethrough (Passthrough) + Erzwingung per
                        Watchdog (Guardian aus); schaltet auch den Kamera-Hintergrund
                        passend zum Hintergrund-Modus (transparent vs. opak)
```

> **Stand der Umgebung (09.10.2026, korrigiert):** Die ArcViewer-Umgebungs-SZENEN sind
> wieder IM Projekt (`Assets/ArcViewerEnvironmentScenes`, 13 Einträge in den Build
> Settings) und werden von `EnvironmentDirector` additiv unter den Raum-Anker geladen.
> Der zwischenzeitliche „prozedurale Bühne (StageView)"-Umbau wurde wieder verworfen.
> Der Bloomfog ist damit wieder VOLL funktionsfähig: Capture-Kamera fotografiert NUR die
> Glow-Ebene 11 (wie ArcViewer, sonst Kosten ohne Nutzen), die aktiven Shader lesen die
> Textur (Nebel + Fake-Reflexionen), ein NDC-Vollbild-Quad zeigt sie als leuchtenden
> Hintergrund, Bloom/Grading laufen in ArcViewer-Werten, `LightEmission` = 6,5 (HDR-
> Grund für das Glühen). Editor und Gerät nutzen denselben Qualitätslevel „Performant"
> (Standalone-Default 1) → identische Optik in beiden. Shader-GUIDs der Materialien
> blieben unberührt (nur Shader-Inhalte aktualisiert). Farbpaletten/Parameter je
> Umgebung aus ArcViewer übernommen (`EnvironmentColorTable`/`EnvironmentParametersTable`,
> Lizenz GPL — Werte rekonstruiert).

### Zusammenspiel (wichtigste Pfade)

- `GameBootstrap` erzeugt `GameManager` (`[BeatSaberGame]`, DontDestroyOnLoad).
- `GameManager.Start()`: Template-UI aufräumen → Rig-Anker suchen → Schwerter bauen →
  UI (HUD + HandMenu) bauen → Save laden → **Boden beim Spielstart ermitteln**
  (`EnsureFloorOrigin`, Tracking-Origin `Floor`) → Songliste scannen → erstes Lied laden.
- `LoadSong(index)` baut aus der Chart einen Noten-/Wand-Zeitplan (`BuildSchedule`). Der
  Spielraum steht fest auf dem Weltursprung (X = Y = Z = 0) und wird jeden Frame im
  `LateUpdate` durch `ApplyRoomAnchor()` aufrechterhalten.
- `GameplayUpdate()` (jeden Frame im Zustand `Playing`): fällige Noten/Wände spawnen,
  Wand-Kopftreffer prüfen, Liedende erkennen (nahtloser Wechsel auf das nächste Lied).
- Noten/Wände fliegen selbst (Update, zeitbasiertes ArcViewer-Timing) im **lokalen** Raum-Root;
  die globale Raum-Skalierung (`PlayerProfile.Scale`, `ApplyRoomScale`) skaliert Root und
  Schwerter, nicht jeden Einzelwert.
- Treffer werden **geometrisch** erkannt (`GameManager.GeometricHitUpdate`, siehe
  Invariante 6): Schwert-Segment gegen Notenbox → `NoteView.TrySlice` entscheidet
  gültig/„Bad Cut" (Farbe + Richtung) → Scoring um den **Schnitt-Moment**
  (`GameManager.ScoreAtCutAsync`, direkt beim Klingenkontakt). Physik
  (SphereCast/Trigger/Noten-Collider) ist nur noch optionaler Fallback
  (`GameConfig.GeometricHitDetection = false`).

### Rig-Aufbau

- Die Kamera wird zur Laufzeit vom PICO-SDK erzeugt — die `SampleScene.unity`
  enthält **gar keine `Camera`-Komponente**. `_cameraOffset = _cam.transform.parent`
  ist damit ein **runtime-eigenes** Objekt des Trackings.
- Controller-Anker werden robust gesucht (`"Left Controller"`/`"LeftHand Controller"`/
  `"Left Hand"`); Fallback liest die Pose direkt aus `XRNode.LeftHand/RightHand`.
- `_gameRoot` (`BeatSaberWorld`) ist der Szenen-Root für Noten, Wände und das HUD und
  bleibt **verankert** (X = 0, Y = 0 = vom Head-Set gemeldeter Boden). Er ist unser Objekt — im Gegensatz zum
  XR-Rig wird er beliebig verschoben (siehe Invariante 1 und 2).
- `_cameraOffset` wird **ausschließlich als Suchwurzel** benutzt (Controller-/Hand-Anker
  darunter finden). Es wird **nie beschrieben** — siehe Invariante 2.

---

## 4. Kern-Invarianten / „Beim Ändern beachten" (projektsweit)

Diese Regeln sind bewusst und dürfen nur mit expliziter Freigabe des Benutzers geändert werden:

1. **Raum-Verankerung:** `ApplyRoomAnchor` (in `GameManager.LateUpdate`) positioniert den
   Spielraum `_gameRoot`: **Y** = Session-Bodenversatz, **X/Z** = Raum-Anker (von Taste A
   gesetzt; läuft bei „Raum folgt Spieler" entlang der Raum-Vorwärtsachse mit), Rotation =
   **Raum-Gierwinkel** `_roomYawDeg`. Kopfbewegungen verschieben Noten/Wände **nicht**.
   **Rechter Stick** links/rechts dreht den Raum; **linker Stick** hoch/runter verstellt den
   Session-Bodenversatz (beides nur außerhalb der Songliste). **„Raum folgt Spieler"**
   (Menü Ein/Aus) koppelt X/Z an die Vorwärts-/Rückwärtsbewegung. **Taste A** tut GENAU
   zwei Dinge: (1) `_roomFollowOffset` = Kopf-X/Z — das Head-Set sitzt damit über dem
   Raum-Ursprung (0/0) an der Noten-Treffebene; (2) `_roomYawDeg` = aktuelle (horizontale)
   Blickrichtung. Die **Höhe/Y bleibt** unverändert. Es fasst den **Tracking-Ursprung
   (Floor) NICHT** an (kein TryRecenter/SetTrackingOrigin).
   **Fallstrick (behoben):** Ein „Sprung nach unten" bei A kam NICHT von diesem Code, sondern
   vom Vorlagen-Rig: „MR Interaction Setup → XR Origin (XR Rig) → Jump" trägt einen
   XRI-`JumpProvider`, dessen Action „Jump" in „XRI Default Input Actions" an den
   PrimaryButton der rechten Hand (= A) gebunden ist — er versetzt den XR-Origin bis 1,25 m
   nach oben (inkl. Fall-Gravitation). `SleepTemplateUi()` deaktiviert die Komponente zur
   Laufzeit; das eigene A-Verhalten läuft parallel unverändert über InputDevices.
2. **Bodenniveau über Tracking-Origin `Floor` (PICO + OpenXR):** Beim **Spielstart** wird der
   Modus gesetzt und so lange **wiederholt, bis er bestätigt ist** („darf nicht fehlschlagen").
   Zur Laufzeit/bei A wird er NICHT mehr angefasst. Es gibt **KEINEN** erfundenen Boden aus
   „Kopfhöhe − 1,60 m",
   **KEINE** laufende Kopf-Sprung-Kompensation und **KEINEN** Tracking-Reset
   (`UPxr_ResetSensor`). Noten, Wände und HUD hängen über `Y = 0` am realen Boden.
   **Verboten:** Kamera, deren Eltern-Transform oder ein anderes XR-Rig-Objekt
   beschreiben (`.position`/`.localPosition`/`SetPositionAndRotation`). Die gehören dem
   Tracking; ein Schreiben darauf ließ den Boden um etwa eine Kopfhöhe absinken.
3. **Raum-Skalierung = DER eine Regler (skaliert wirklich ALLES):** Die frühere Spielergröße
   ist eine **globale Skalierung** (`PlayerProfile.Scale`, Menüzeile „Skalierung", Default 1,0)
   über `ApplyRoomScale()` → `PlayerProfile.Scale`. Sie wirkt auf den Raum-Root `_gameRoot`
   (Noten, Wände, Anzeigetafel, Score-Texte) **und** auf die Schwerter/Labels.
   **Harte Invariante:** Alle Spielobjekte verwenden **ausschließlich lokale Basis-Koordinaten
   unter dem Raum-Root** (Position wie Größe, `SetParent(root, false)`), damit
   **welt = Skalierung × Basis** gilt. Dadurch skalieren Raster, **Lücke zwischen den
   Notenblöcken**, Spawn-Distanz, Block-/Wandgröße und Abstände **immer gemeinsam** —
   damit lässt sich für jeden Spieler der äußere Rand der Notenspalten erreichbar einstellen
   („gleiches Erlebnis trotz unterschiedlicher Armlänge"). Es gibt bewusst **KEINEN aus einer
   Körpergröße abgeleiteten** Vertikal-Versatz mehr (früher `PlayerSizeOffset`) — die
   Skalierung ist der Regler für die Raum-GRÖßE. Bewegung läuft in lokalen Root-Koordinaten
   (Tempo/Strecke skalieren mit, Anflugzeit bleibt gleich). Skalierung wirkt live, auch bei Pause.
   **Neustart:** Es wird genau EIN Wert persistiert (`PlayerProfile.Scale`) — kein globaler
   „zuletzt verwendeter" Basisfaktor. Er wird am Start absolut zugewiesen (`LoadSave` →
   `EnsurePlayer` → `ApplyRoomScale`, Root-Basis 1,0), **nie multiplikativ** — dadurch kann
   der Raum nicht bei jedem Neustart weiterwachsen.
   Stehende (vertikale) Wände liegen unverändert auf Boden-/HUD-Ebene (Mitte = bottom + Höhe/2).
3b. **Notenraster & Sprung-Timing = 1:1 ArcViewer — KEIN Spieler-Offset mehr:** Es gibt
   keinen Regler (und keine Konstante), der das Notenfeld verschiebt. Position und Timing
   stammen vollständig aus dem Referenz-Viewer:
   - **Raster:** 1:1 ArcViewer (`ObjectManager`: `RowHeight` 0,55 + Container-Offset
     `-objectFloorOffset + 0,25` = 0,85): 4 Spalten à **0,6 m** (x = −0,9…0,9), Reihen
     **0,85/1,40/1,95** (`GameConfig.GridPosition`/`ObstacleBounds`), Notengröße **0,50 m**
     (BeatSaberClone-Würfel `Box.fbx`, Import-Scale 0,5; Optik siehe §7). Wände stehen auf dem
     Boden (kein Wand-Offset) und sind in Bewegungsrichtung auf **Dauer × NJS** gestreckt
     (Durchquerzeit = Map-Dauer). v3/v4-Noten übernehmen zusätzlich den Winkel-Offset `a`
     (ArcViewer `CalculateObjectAngle(d, a)`).
   - **Sprung:** NJS aus der Map (sonst Rang-Vorgabe Easy/Normal/Hard 10, Expert 12,
     Expert+ 16), DefaultHJD 4 Beats → halbieren bis Sprungdistanz ≤ 35,998 m,
     HJD = max(DefaultHJD + NoteJumpStartBeatOffset, 0,25), ReactionTime = 60/BPM × HJD
     (`GameManager.ComputeJumpParameters`).
   - **Bewegung:** KONTINUIERLICH mit dem Map-Tempo (NJS) — `Z = (Beat − Liedzeit) × NJS`
     (`GameConfig.JumpZ`), jeden Frame aus `GameManager.VisualTime` (Score-Uhr + Noten-
     Offset). Keine Anflug-Phase, kein `MinApproachTime`; Spawn 2 × ReactionTime vor dem Beat.
   - **Positions-Effekte PRO NOTE — NUR aus dem Muster (nicht global!):**
     Normale Noten fliegen gerade. `GameManager.BuildNoteEffects` berechnet je Note:
     * **STAPEL:** Nur wenn zur selben Zeit mehrere Noten in derselben Spalte liegen,
       startet die Note versetzt unter ihrer Reihe (`StackSpacing`) und gleitet per
       SpawnParabel hinein (`NoteView`/`GameConfig.SpawnParabola`).
     * **FLIP:** Genau zwei Farbnoten zur selben Zeit, überkreuzt (rot rechts / blau
       links) ⇒ Start auf der anderen Seite und Wechsel mit Über-/Unter-Bogen
       (`FlipYOver`/`FlipYUnder`, QuadInOut).
     Quellen: ArcViewer `NoteManager.GetStartY`/`GetFlipYHeights` + `ObjectManager`.
     Die Muster kommen aus den Song-Dateien (Stapel/Überkreuzungen sind dort erkennbar).
3c. **Notenfeld = quaderförmiges Raster, KEIN verjüngter Tunnel.** Das Feld ist bewusst
   unverändert: alle Noten/Minen/Wände stehen auf ihrem Rasterplatz in voller Breite und
   Höhe, auch im hinteren Bereich. Grund: Die PICO-4-Passthrough ist **monokular** (eine
   Kamera), das durchsichtige Bild ist damit schon eine andere Projektion als das
   Stereo-Bild der virtuellen Szene. Ein „Tunnel" (Querschnitt nach hinten verjüngen) oder
   eine Parallaxe-Korrektur gleichen das *nicht* aus — sie verschieben nur die Noten gegen
   den echten Raum und wirken dadurch zufällig. Siehe Fallstrick „Passthrough ist mono".
4. **Menü-Logik lebt im GameManager**, `HandMenu` ist nur Anzeige. Neue Menüpunkte brauchen:
   freie **Menü-ID** (`GameManager`: Hauptmenü 103–120, Spieler 200–249, Aktionen 250–254,
   Songliste 300+), eine Zeile in `MenuRows()` und einen `case` in `OnMenuAction()`.
   Belegt im Hauptmenü: 103 (Spieler), 106 (Skalierung), 108 (Audio-Offset),
   109 (Schließen), 110 (Schwierigkeit), 111 (Feldhöhe), 112 (Raum folgt Spieler),
   113 (Hintergrund), 115 (Run and Play), 116 (Lauf-Tempo), 117 (Controller anzeigen),
   120 (Songliste); 114 (Noten-Offset) ist ENTFERNT/reserviert.
   **Frei: 104, 105, 107, 118, 119 und 253/254** (104 war „Spielerhöhe", 105/107 wurden von der
   entfernten Tunnel-Verjüngung bzw. einer früheren Parallaxe nicht mehr gebraucht; 253/254
   trugen die frühere zeilenbasierte Lösch-Bestätigung, die jetzt ein modaler Dialog ist).
5. **Bestätigen = NUR Haupt-/Index-Trigger** der Zeiger-Hand; **Grip = NUR Easter Egg**
   (Doppelklick fährt das jeweilige Schwert ein/aus). Diese Trennung darf nicht verwischt werden.
6. **Scoring (live aus der Controller-Bewegung):** gültiger Schnitt = 0–115
   (70 Anlauf + 30 Durchzug + 15 Mitte, ×Multiplikator). Die Anteile
   werden **einzeln gerundet**, dann summiert.
   **Anlauf/Durchzug:** Gemessen wird die **Klingenrichtung** (`SaberView.CurrentBladeDirectionLocal`,
   raum-lokal) und deren größte Abweichung gegenüber der Klingenrichtung im **Schnitt-Moment**
   (erster Klingenkontakt, `HandleSaberHit`), gesucht
   im **±200-ms-Fenster** auf der Score-Uhr (`SaberView.MaxBladeDeviation`,
   `GameConfig.SwingMeasureWindow` = 0,2 s — entspricht `preswing_time`/`followthrough_time`).
   **Keine Totzone.** Anlauf voll bei **100°** (`PreSwingFullAngle`), Durchzug voll bei **60°**
   (`FollowThroughFullAngle`). Weil die **Richtung** (nicht ein Ebenen-Winkel) gemessen wird,
   zählen gerade und diagonale Schnitte gleicher Qualität gleich — die Punktzahl ist dadurch
   reproduzierbar. Quelle der Messung: OpenSaberPlus `LightSaber.gd`
   `get_preswing_angle`/`get_followthrough_angle`; die Verteilung ist Original/Beat Saber.
   (Früher: SuperSaber `maxAnglePlaneX/Y/XY` mit 15°-Totzone — verworfen: der Ebenen-Winkel
   sättigt bei 90° und unterscheidet gerade/diagonal nicht, dadurch fühlte sich jede Note anders an.)
   **Chains (v3/v4 `burstSliders`):** Kopfnote max. **85** = 70 Anlauf + 15 Mitte, **kein
   Durchzug**; jedes erzeugte Glied fest **20** × Multiplikator (ArcViewer `ScoringUtils`:
   `MaxChainLinkScore = 20`). Verpasste Glieder brechen die Combo nicht.
   **Arcs (v3/v4 `sliders`):** reine Optik (`ArcView`); die Kopf-/Endnoten existieren als
   colorNotes und werden normal gewertet.
   **Mitte-Score:** linear 15 → 0 über den Abstand Klingenlinie ↔ Notenmitte; 0 ab
   `GameConfig.CenterDistanceMax` (0,3 × Notengröße = 0,15). Gemessen wird der **nächste
   XY-Vorbeilauf** der (raum-lokalen) Klingenlinie an der Notenmitte über das ganze
   Messfenster (`SaberView.MinBladeCenterDeviation`) — nicht ein einzelner Frame, der von
   der Treffer-Zone abhing (Log 12.10: `center=0` trotz Mitte-Schnitt, weil im Zonen-Moment
   die Klinge längst weitergezogen war).
   **Multiplikator** (Original/ArcViewer, `ScoringUtils`): Stufen x1/x2/x4/x8
   (`GameConfig.ComboMultipliers`), je Stufe `HitsNeededForComboIncrease` = 2/4/8 Treffer
   bis zur nächsten (also x2 ab dem 2., x4 ab dem 6., x8 ab dem 14. Treffer). Die Note, die
   die Stufe erreicht, zählt mit der NEUEN Stufe. Zustand = `_multTier` + `_comboProgress`
   (bewusst vom Combo-Zähler entkoppelt); ein Fehler halbiert die Stufe nur EINE Stufe
   (x8→x4→x2→x1) und setzt den Fortschritt zurück.
   **Geometrische Treffer-Erkennung (alleinige Punktequelle, `GameConfig.GeometricHitDetection`) —
   Kollision = KLINGEN-SEGMENT gegen Notenbox, Gültigkeit 1:1 Clone `BoxView.CheckSliceDirection`,
   durch die Notenrotation erweitert um die 45°-Diagonalen:**
   Pro Frame prüft
   `GameManager.GeometricHitUpdate` die **Klinge als Segment (Basis→Spitze) gegen die Notenbox**
   (`NoteWorldBounds`: rotierte Noten-Würfelflächen inkl. Raum-Skalierung; Slab-Test
   `SegmentIntersectsBounds`), inklusive Sweep Vorframe↔Current gegen Tunneling schneller
   Schwünge. Bewusst NICHT AABB-gegen-AABB (SuperSaber `checkCollisions`): die Achsen-Box
   eines schnellen Schwungs umfasst die GANZE Bogenfläche und nimmt Noten mit, die die Klinge
   nie berührt — Treffer registrierten sich dadurch weit VOR der Zone (Log 12.10: z≈1,4–1,6
   statt ~0), die Richtung wurde zum falschen Zeitpunkt gelesen, und eine falsch
   angeschnittene Note konnte später trotzdem sauber zählen („falsche Seite"). Der
   Segment-Test trifft nur, was die Klinge WIRKLICH schneidet — präzise wie Beat Saber/
   Web-Replays; die Treffer-Toleranz steckt allein in der (großzügigen) Box, nicht in einer
   aufgeblasenen Achsen-Box. Auch Ketten-Glieder nutzen denselben Segment-Test.
   Frühere Zwischen-Regeln (Mittelebenen-Zwang, Mittelschicht, Reichweiten-Fenster
   HitReach*) bleiben verworfen — sie führten zu „Treffer sichtbar, aber nicht registriert".
   Gültigkeit entscheidet
   `NoteView.TrySlice` — 1:1 wie der Clone (`BoxView.CheckSliceDirection`), aber SO
   erweitert, dass die Rotation mitgelesen wird (der Clone hatte nur 4 Richtungen):
   1. **Winkel-Kegel gegen den ANGEZEIGTEN Pfeil, im NOTEN-LOKALSYSTEM:** gerechnet wird
      `transform.InverseTransformDirection(Schneidegeschwindigkeit)` und nur dessen
      lokales x/y — die TIEFE wird wegprojiziert, exakt wie OpenSaberPlus
      (`BeepCube.cut`: `Vector3(cut_speed.x, cut_speed.y, 0)`). Im Lokalsystem zeigt der
      angezeigte Pfeil immer nach −Y, weil die Note die Anzeigen-Rotation
      `TargetRotationZ` trägt (identisch zu Clone `BoxSettings.asset`: down→0°,
      up→180°, right→90°, left→270°) — der Score-Layer liest exakt dieselbe Drehung wie
      das Auge; 45°-Diagonalen (cutDirection 4..7) ergeben sich davon von selbst, eine
      eigene Richtungstabelle gibt es nicht mehr. Gültig:
      `Vector2.Angle(bewegungXY, (0,−1))` ≤ `GameConfig.CutAllowedAngle` (**90° =
      Vorzeichen-Regel wie die Web-Referenz SuperSaber `strokeDirection` und Beat Saber:
      Hieb mit positivem Anteil in Pfeilrichtung ist gültig, Gegenrichtung = Bad Cut. Der
      frühere Wert 45° (= Clone-Asset/OpenSaberPlus dot>0,7) erzeugte auf dem Gerät gehäuft
      Bad Cuts an der Schwelle — Log 12.10: 41 der 58 Bad Cuts lagen bei 46–90°, in Wahrheit
      gültige, leicht schräge Schnitte, die als „MISS" erschienen). Grund der
      Tiefen-Projektion: der 3D-Vergleich vorher bestrafte natürliche Aufwärtsschwünge
      (Hand führt hoch UND zum Körper) mit >45° — „unten liegende Note sauber von unten
      geschlagen" zeigte trotzdem MISS.
   2. **Schneide-Geschwindigkeit = geglättete SPITZEN-Geschwindigkeit**
      (`SaberView.TipVelocity`, Mittel aus den letzten 5 Spitzen-Schritten — Klon-Prinzip
      `VelocityEstimator`/`ObjectSlicer.Velocity`, nur an der SPITZE gemessen, weil bei
      Handgelenksschwüngen die Wurzel zu bewegungsarm ist). Der Ring läuft **kontinuierlich**
      mit (`SaberView.UpdateVelocity`) und wird nur bei eingefahrenem Schwert/verworfener Pose
      geleert. Unter `GameConfig.CutMinSpeed` (0,15) zählt die Kollision als „Berührung ohne
      Schnitt". Ersetzt SuperSabers `strokeDirection`-Vorzeichenregel (die Netto-Verschiebung
      ließ Schwenks mit Drift als korrekt gelten und reagierte bei Diagonalen unberechenbar).
   3. **Farbe = LAYER-TRENNUNG (OpenSaberPlus `LightSaber.gd` + `BeepCube.gd`):** In
      `GeometricHitUpdate` wird jede Note NUR gegen das zuständige Schwert geprüft —
      Rot→links, Blau→rechts, Bomben→beide. Das rote Schwert kann die blaue Note damit
      physisch nicht treffen, egal wie groß die Motion-Blur-AABB ist oder wie eng zwei
      verschieden-farbige Noten nebeneinander liegen. Das repariert den gemeldeten
      Fall „zwei Nachbar-Noten (rot+blau) gleichzeitig geschlagen, eine sagt MISS":
      früher prüfte pro Note immer erst das linke Schwert, dessen überlappende AABB
      verbrauchte die blaue Note als wrongHand-Bad-Cut, bevor das rechte Schwert
      drankam. Ein wrongHand-Bad-Cut ist jetzt konstruktionsbedingt unmöglich
      (`TrySlice`-Farbcheck bleibt nur als Sicherheitsnetz im Physik-Fallback).
      **Dot-Noten (8/9):** eigene Farbe, aber jede Schlagrichtung gültig.
      **Bombe:** jede Berührung = Fehler (Ausweichen ist die Aufgabe).
   Die Optik (EzySlice/Partikel) wird aus dem geometrischen Treffer ausgelöst, entscheidet
   aber nicht über Punkte. Physik (SphereCast/Trigger/Noten-Collider) ist abgeschaltet; der
   frühere Ansatz liegt hinter `GeometricHitDetection = false` als Fallback still.
   **Frame-Synchronität:** Der Treffer-testet NUR Posen, die in DIESEM Frame gezeichnet
   werden. Deshalb läuft `GeometricHitUpdate` im **LateUpdate NACH `ApplyRoomAnchor`**
   (früher Update — dort standen Raum-Root und Controller noch auf der Vorframe-Pose,
   ~14 ms ≈ 14–22 cm Notenflug Versatz: „visuell daneben, trotzdem Treffer" bzw.
   „visuell getroffen, trotzdem MISS"). `SaberView` erfasst Base+Tip des Schwerts samt
   Score-Uhr-Stempel in seinem eigenen LateUpdate (Order −200 → vor GameManager):
   `XrPoseAnchorFollower` **(−500, Update)** setzt die Fallback-Controller-Anker →
   `SaberView` **(−200, LateUpdate)** erfasst Pose + Klingenrichtung → `GameManager` **(0,
   LateUpdate)** verankert den Raum und prüft dann den Treffer.
   **NOTENEBENE VERSCHMOLZEN + NOTEN-OFFSET ENTFERNT (Nutzer-Freigabe):** Die sichtbare
   Notentransform IST die Trefferfläche. Der Spieler steht `PlayerDistanceBehindZone`
   = 0,30 m (× Raumskalierung) HINTER der Zone (Recenter setzt ihn an dieselbe hintere
   Position; 0,65 m — ArcViewer-Wert — fühlte sich zu weit weg an). Die Menüzeile
   „Noten-Offset" ist **gelöscht**
   (Menü-ID 114 bleibt reserviert), `AudioManager.VisualTime` ist fest = `ScoreTime`;
   `SaveData.VisualOffsetMs` wird nicht mehr angewandt (alte Werte sind wirkungslos).
   Die „verpasst"-Linie folgt SuperSaber (`beat.js`: Recycle + `beatmiss` bei 2
   Einheiten hinter der Schnittebene): `GameConfig.NoteMissZ = -2` (früher: Entsorgung
   erst bei -5, der Combo-Bruch kam deutlich zu spät). Durchflug-Miss zeigt jetzt wie
   Bad Cut ein „MISS"-Label an der Notenposition (`GameManager.OnNoteMissed` →
   `ShowSliceScore(..., 0)`, Bomben ausgenommen) — vorher brach still nur der Combo.
   WÄNDE behalten die feste Score-Uhr (`WallView.ScoreToLocal`) — Kopf-Kollision wie bisher.
   **AUDIO-OFFSET = NUR DIE HOERBARE MUSIK (Wieder-Einbau 09.10, NEUER Mechanismus, Nutzer-Wunsch):**
   Der Regler „Audio-Offset“ (Menü 108, `SaveData.SoundDelayMs`) verschiebt **nur die hörbare
   Audiospur** gegen die Noten — die Noten-Uhr bleibt UNBERÜHRT.
   **Standard seit 12.10.2026: 0 (KEIN harter Offset mehr).** Die am PICO 4 kalibrierten
   85 ms stecken jetzt in der Noten-Uhr: `GameConfig.AudioLatencyBaseMs` = **275 ms**
   (früher 190 + Offset −85). Synchron ist das Spiel, wenn `AudioOffset == AudioLatencyBaseMs`
   ist — beide Regler verschieben ihre Quelle (Noten bzw. Musik) um denselben Betrag, die
   gemeinsame Basis `AudioOffsetBaseMs` (300 ms) kürzt sich heraus. Eine Einmal-Migration
   (`GameManager.MigrateAudioOffset`, `SaveData.SoundDelayBakedIn`) zieht einen gespeicherten
   Alt-Wert deshalb auf 0; auf anderen Geräten/Audio-Ausgängen (z.B. Bluetooth) im Menü
   nachjustieren. Mechanismus: Die Musik-Quelle läuft auf einem eigenen
   Kind-GameObject `[Music]` durch **`AudioDelayFilter`** (`Core/AudioDelayFilter.cs`,
   `OnAudioFilterRead`, Audio-Thread): ein samplegenauer Ringpuffer-Verzögerer, der das
   AUSGABESIGNAL um X ms nach hinten schiebt. Das ist der erste Ansatz, der die Musik
   SELBST (hörbar) bewegt — ohne `AudioSource.time`-Seek (der war auf der PICO mit
   OGG-Streaming wirkungslos, siehe `AudioOffset_entfernt_Sicherung.md`), geräte- und
   formatunabhängig, live in 5-ms-Schritten hörbar (geglätteter Slew, keine Klicks).
   Bei Offset 0 ist der Filter reines Durchreichen (kein Nachteil).
   * **Positiver Offset (+X ms):** Musik hörbar X ms SPÄTER. `ScoreTime = AudioTime −
     Base(275ms)` unverändert → Noten stehen exakt wie getunt, nur der Klang rutscht.
   * **Negativer Offset (−X ms):** Ein Audiosignal kann physikalisch NICHT vorgezogen
     werden (die Zukunft liegt noch nicht im Puffer). Die Basis-Verzögerung der Delay-Line
     (300 ms) gleicht den Spielraum nach unten aus: die Verzögerung wird einfach kleiner
     (300 + Offset) — relativ zwischen Musik und Trefferfläche ergibt sich damit in BEIDEN
     Richtungen exakt Δ = Offset. **0 ist der kalibrierte Stand.**
   * **Abgrenzungen:** SFX (Treffer/UI/Schalter) hängen NICHT am Offset (eigene
     Kinder-Objekte, klingen sofort). `AudioLatencyBaseMs` (275 ms) bleibt FEST in der
     Score-Uhr. Lied-Ende/Wechsel unberührt (die Uhr läuft nach Datei-Ende weiter —
     der Delay-Layer betrifft nur das SIGNAL, nicht die Uhr).
   * **Historie:** Vier frühere Versuche (Score-Uhr-Offset, Start-Buffer, Audio-Seek,
     Live-Seek+Fallback) sind in `AudioOffset_entfernt_Sicherung.md` gesichert; sie
     griffen am falschen Ende an (Noten-Uhr bzw. Seek). Der DSP-Layer ist neu.
   Die Score-Zone bleibt fest an der Note (eine Ebene) → **kein Bug-Using**
   durch ein verschobenes Trefferfeld.
   **EBENEN (Architektur, Nutzer-Wunsch):** Die **SCORE-/SPAWN-Uhr**
   (`AudioManager.ScoreTime` / `GameManager.ScoreTime`) ist die **einzige** Zeitreferenz:
   Hitboxen, Wand-/Bomben-Kollisionen, Noten-Anflug UND der Saber (Echtzeit) rechnen damit.
   * **Audio-Offset** (`SaveData.SoundDelayMs` → `AudioManager.SetAudioOffset`): positiv =
     hörbare Musik später (DSP-Keller `AudioDelayFilter`), negativ = Musik früher (kleinere
     Verzögerung). Startet bei 0; die Geräte-Kalibrierung liegt in `AudioLatencyBaseMs`.
     existiert nicht mehr, `AudioManager.VisualTime` ist fest = `ScoreTime`;
     `SaveData.VisualOffsetMs` wird nicht mehr angewandt (alte Werte wirkungslos).
   Saber = **kein Input-Delay**, SFX klingen **sofort**. Zentrum-Score/Slicing rechnen auf der
   sichtbaren Notenposition, Wand-Kollision auf der Score-Position (`WallView.ScoreToLocal`).
   **Wand-Kopfkontakt** → `BreakCombo(true)` (Combo 0, Multiplikator x1 — bewusste eigene
   Regel); **verpasste Note/Fehler** → `BreakCombo()` (Multiplikator halbiert: x8→x4→x2→x1).
   Bomben-Berührung = Fehler (Combo-Bruch), ausgewichene Bombe = kein Fehler.
7. **Persistenz (JsonUtility):** neue Felder in `SaveData`/`PlayerProfile` brauchen Defaults;
   Listen gegen alte Saves mit `??= new()` schützen. Save-Pfad: `<persistentDataPath>/BeatSaberSave/Profiles.json`.
8. **Trailing-Marker NICHT löschen:** `GameManager.cs` endet mit `// refresh-touch-9`,
   `GameBootstrap.cs` mit `// refresh-touch` (Editor-/Arbeitsfluss-Marker).
9. **Layers** (TagManager.asset): Note=7, Saber=8, Wall=9. Wände tragen **keinen Collider** —
   die Kopfprüfung erfolgt manuell über `WallView.IsHeadInside` im `GameManager`.
10. **Kein Git-Repo** in `Y:\Unity\MrSaber` — Änderungen sind uncommitted, es gibt keinen
    einfachen Rollback. Vor größeren Umbauten Sicherungskopie anlegen.
11. **Konsistenz:** Kommentare/Code sind durchgehend auf **Deutsch** gehalten — neue
    Kommentare bitte ebenfalls deutsch verfassen.

---

## 5. Bedienung (PICO-4-Controller)

| Eingabe | Aktion |
|---|---|
| **A** (rechts, kurz) | **Raum unter das Head-Set stellen und in Blickrichtung drehen**: Raum-X/Z = Kopf-X/Z (Head-Set sitzt über dem Raum-Ursprung 0/0), Gierwinkel = aktuelle (horizontale) Blickrichtung. **Höhe/Y bleibt**; kein Tracking-Ursprung, keine Neu-Bodenermittlung. Bodenversatz (linker Stick) bleibt unverändert |
| **X** (links, kurz) | im Spiel: nächster Song; im Menü: nächstes Lied nur VORMERKEN (Menü bleibt offen, startet beim Schließen) |
| **Y** (links, kurz) | Menü links öffnen/schließen |
| **B** (rechts, kurz) | Menü rechts öffnen/schließen |
| Y / B / **Menütaste** ≥ 2 s | Spiel beenden (Quit) |
| **Grip** (seitlich, Doppelklick) | Easter Egg: Schwert ein-/ausfahren (keine Noten-Treffer eingefahren) |
| **Index-Trigger** (Zeiger-Hand) | Bestätigen / Zeile auslösen |
| **Joystick** (beide Hände) | Songliste endlos um einen Eintrag nach oben/unten bewegen (zyklisch, unabhängig von der Menü-Seite) |
| **Linker Joystick** hoch/runter | **Session-Bodenversatz:** Spielfeld anheben/absenken — **außer** bei geöffneter Songliste. Startet bei jedem Spielstart bei **0**, wird **NICHT** gespeichert; **A** setzt ihn **NICHT** zurück. Größere Totzone (`GameConfig.FloorOffsetDeadzone`) gegen versehentliches Auslenken |
| **Rechter Joystick** links/rechts | **Raum drehen** (`_roomYawDeg`) — **außer** bei geöffneter Songliste |
| **Laser** (andere Hand) | Zeile unter dem Laser = grün markiert (Songliste: keine dauerhafte Auswahl); (−)/(+)-Zeilen: links = Minus, rechts = Plus. Strahl nur bei **Treffer** auf der Tafel sichtbar |
| **Menütaste** in der Lösch-Bestätigung | 1×: Löschen abbrechen, zurück zur Spielerverwaltung; erneut: Menü schließen |

---

## 6. Datenpfade & Build

- **Songs auf dem Gerät (gemeinsamer Bestand mit der offiziellen Beat Saber):**
  `/storage/emulated/0/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels/`
  (= `GameConfig.ExternalSongsPath`). MrSaber liest von dort (erster nicht-leerer Ordner;
  Fallback: der frühere app-eigene `<persistentDataPath>/Songs`). Pro Lied: `Info.dat`/
  `info.json` + `.dat` + Audiodatei (oft `.egg` = OGG).
  **Berechtigung:** Der Ordner liegt außerhalb der App-Sandbox → Android braucht die
  externe Speicher-Berechtigung. WRITE kommt über Player-Setting „Write Permission =
  External" (`ProjectSettings.asset` → `ForceSDCardPermission: 1`); READ ergänzt der
  Editor-Post-Processor `Assets/Editor/AndroidStorageManifestPostProcessor.cs`
  (`IPostGenerateGradleAndroidProject`, weil es dafür keinen Player-Schalter gibt).
  `requestLegacyExternalStorage` setzt Unity im generierten Manifest ohnehin.
  Zur Laufzeit fragt `GameManager.EnsureSongsReadPermission` die Berechtigung VOR dem
  Lied-Scan an. Wird sie abgelehnt, fehlen die Lieder → in den App-Einstellungen der
  PICO nachtragen.
- **Save-Datei:** `<persistentDataPath>/BeatSaberSave/Profiles.json` (bleibt bei
  Lied-Umstellungen erhalten).
- **Build-Ziel:** Android, PICO 4, **nur physische Controller**. Passthrough wird von
  `PicoSeeThrough` erzwungen (Guardian aus + Watchdog); Kamera = SolidColor Alpha 0,
  `allowHDR = false` (PICO-Pflicht).
- **Arbeitsfluss:** Nach Code-Änderungen kompiliert/validiert der Assistent das Projekt
  (Batch-Mode, siehe §9 „Arbeitsweise") und behebt Fehler automatisch; der Benutzer
  übernimmt nur **„Build and Run"** auf der Brille. Kein ADB verfügbar. Ist eine
  Editor-Instanz offen, importiert/kompiliert sie erst beim Fokussieren des Fensters.

---

## 7. Bekannte Fallstricke (beim Ändern schnell übersehen)

- **Audio-Pfade mit Leerzeichen/`&`** (`Runaway (U & I) - Tpenny68`): nur dank der
  URL-Maskierung in `AudioManager.ToFileUri()` ladbar — nicht entfernen.
- **BPM-Verlauf der Map:** `BuildBeatToSeconds` (Parser) respektiert `_BPMChanges`/`bpmEvents`.
  Wird stattdessen ein einzelnes BPM verwendet, landen Noten nach einem BPM-Wechsel zur
  falschen Zeit (→ dauerhaft „MISS"). Beispiel: Song „Runaway" mit 2 BPM-Events.
- **v3.0.0-Dateien** nutzen `"version"` ohne Unterstrich + `colorNotes` → werden über den
  `"colorNotes"`-Fallback als v4 erkannt.
- **„Aufpoppen" von Noten — bewusst NICHT mehr verhindert:** Die Bewegung ist
  KONTINUIERLICH mit NJS (`GameConfig.JumpZ`), Spawn-Zeit = Beat −
  `GameConfig.SpawnLeadTime(ReactionTime)` = Beat − 2 × ReactionTime. Ein früheres
  `MinApproachTime`/eine Anflug-Phase gibt es NICHT mehr (Wunsch: Tempo allein aus den
  Song-Daten). Bei kleinen HJD/hohem NJS erscheinen Noten daher spät — das ist gewollt.
  Die sichtbaren Lageänderungen sind die **musterabhängigen Effekte** (Stapel/Flip,
  siehe Invariante 3b) — NICHT die Z-Bewegung und NICHT eine globale Drehung.
- **Demo-Fallback = echte Beat-Saber-v2-Map („Death to Disco" von Freeek, 115 BPM), KEIN
  Sekunden-Chart mehr:** Das eingebaute Lied liegt als vollständige Map in
  `Assets/Resources/BeatSaberDemo/DeathToDisco/` — `info.json`, `Easy/Normal/Hard/Expert.json`
  und `DeathToDisco.ogg` (Byte-identisch zur Quelle `608 (Death to Disco - freeek)`; `.dat`/
  `.egg` wurden beim Einbau nur umbenannt, der OGG-Stream ist unverändert). Es hat **echte
  Noten (504 auf Expert) und Wände (69)** und **keine** Dot-Noten (`_cutDirection` nur 0–7);
  Noten laufen von Beat 4 bis 272 (≈ 2:09), Wände bis Beat 280 (≈ 2:26 bei 115 BPM).
  **Wichtig:** Das Demo ist KEIN Sonderfall mehr im Parser — `BeatmapParser.CreateDemoMeta`
  liest dieselbe `info.json` und ruft dasselbe `BuildMetaFromInfo` wie ein Geräte-Lied auf; nur
  die Dateiquelle ist `Resources` statt Datenträger. Dadurch gelten NJS (= 10 aus der info),
  BPM, Wände, Schnitt-Richtungen und die **persistierte Schwierigkeitswahl** unverändert
  auch fürs Demo. `isDemo` schaltet nur zwei Dinge um: den Beatmap-Namen (Resource-Pfad ohne
  Endung, weil `MapPath` beim Demo der Load-Name ist) und den Highscore-Schlüssel-Präfix
  (`__demo__`, nicht der Ordnername — sonst träfen die migrierten Alt-Highscores ins Leere).
  Fehlt die Demo-info, gibt es eine Notfall-Meta mit nur `Expert` (`GameConfig.
  DemoDifficultyName`), damit das Spiel ohne Absturz startet. **Die alten Demo-Dateien
  (`NotesData.json`, `BeatSaberClone_DemoTrack*.ogg`) wurden ersatzlos entfernt.**
- **XR-Warnung „Call to StopSubsystems without an initialized manager"** ist harmlos
  (Unity-intern).
- **NIEMALS auf `_cameraOffset` (oder Kamera/Rig) schreiben:** Die `SampleScene.unity` enthält
  **keine `Camera`-Komponente**; die Kamera erzeugt zur Laufzeit das PICO-SDK.
  `_cameraOffset = _cam.transform.parent` ist damit ein Objekt des Trackings.
  `_cameraOffset` dient ausschließlich als Suchwurzel für die Controller-/Hand-Anker.
  Verankert wird `_gameRoot` (unser Objekt), und zwar fest auf dem Weltursprung.
- **Bodenniveau (Tracking-Origin `Floor`):** Beim Spielstart wird der Modus gesetzt und
  wiederholt, bis er bestätigt ist. Zur Laufzeit/bei A wird er NICHT mehr angefasst. Es gibt
  **keine** laufende Head-Set-Kopplung und **keinen** Tracking-Reset.
  Der frühere persistente **Boden-Anker** (`SaveData.FloorOffsetMeters`, Menüzeile
  „Boden-Anker") ist ersatzlos entfernt; der **Session-Bodenversatz** per linkem Stick gibt
  es wieder (startet bei jedem Spielstart bei 0, wird NICHT gespeichert, A setzt ihn NICHT zurück).
  `PXR_Enterprise.GetFloorHeight()` bleibt außen vor (ToB-Dienst). Die Unterkante von Wänden
  liegt **auf dem Boden**; das HUD beginnt auf Bodenhöhe. Siehe Invariante 1 und 2.
- **Raum-Ausrichtung:** **Taste A** tut GENAU zwei Dinge: `_roomFollowOffset` = Kopf-X/Z
  (Raum sitzt direkt unter dem Head-Set, Head-Set über dem Raum-Ursprung 0/0 an der
  Noten-Treffebene) und `_roomYawDeg` = aktuelle (horizontal projizierte) Blickrichtung.
  **Höhe/Y bleibt**; der Tracking-Ursprung wird NICHT angefasst (kein
  `TryRecenter`/`SetTrackingOrigin` — das verschob auf PICO den Boden). Der Boden wird
  **nur beim Spielstart** gesetzt (`EnsureFloorOrigin(mustSucceed: true)`). Der **rechte
  Stick** links/rechts dreht den Raum (`GameManager.UpdateRoomAdjustment`), der **linke
  Stick** hoch/runter verstellt den **Session-Bodenversatz** (`_floorOffsetMeters`).
- **Tiefen-Folge („Raum folgt Spieler", Menü Ein/Aus, persistent):** aktiv => der Raum wird
  um die **Vorwärts-/Rückwärts-Komponente** der Kopfbewegung entlang der Raum-Vorwärtsachse
  mitgeführt (`_roomFollowOffset`, `GameManager.UpdateRoomFollow`, im `LateUpdate` vor
  `ApplyRoomAnchor`); seitliche Bewegung wird **nicht** gefolgt.
- **Run and Play — niedriges Audio-Tempo ist KEIN Fehler:** Der echte Lock
  (`comfort = 1`) erzwingt `playRate = v/(_roomScale·NJS)`. Bei menschlichen
  Laufgeschwindigkeiten (≈1–2 m/s) und NJS ≈ 10 ergibt das ≈0,1–0,2 — die Musik ist dann
  absichtlich langsam/tief (Tempo = Laufgeschwindigkeit, `AudioSource.pitch` verschiebt auch
  die Tonhöhe; es gibt kein Time-Stretch). „Noten statisch" und „normales Tempo" sind
  mathematisch unvereinbar; wer das normale Tempo will, dreht „Lauf-Tempo" hoch und nimmt
  Notendrift in Kauf. **Nicht** mit `Time.timeScale = 0` pausieren (das nutzt das Menü):
  Der Lauf-Modus pausiert ausschließlich über `AudioManager.SetRunPlayPaused`
  (Score-Uhr friert ein, Noten stehen).
- **UI-Sprites** werden zur Laufzeit erzeugt (BsUi) — keine Asset-Abhängigkeit; Font =
  `LegacyRuntime.ttf`.
- **Floating Score/HUD:** `ShowSliceScore`-Text ist Kind von `_gameRoot` (raumfest),
  das Boden-HUD ebenfalls — beides bewusst NICHT am Spieler verankert.
- **Zerschnittene Noten (EzySlice) liegen falsch/zu klein:** EzySlice erzeugt die beiden
  Hälften als **Root-Objekte** und kopiert nur `localPosition`/`localRotation`/`localScale`
  der Note. Die Noten hängen aber im **skalierten/gedrehten Raum-Root** — ohne Korrektur
  stehen die Hälften an falscher Stelle und (bei Raum-Skalierung > 1) sichtbar **kleiner**;
  genau das sah aus wie „die Note fällt nur nach unten weg und wird kleiner".
  `NoteView.PlacePieceAtNote` setzt die Hälften deshalb auf die **Welt-Transform** der Note
  (Position/Drehung/`lossyScale`). Der Impuls kommt aus `GameConfig.SlicePieceSpeed`
  (±Schnitt-Normale) — **nicht** aus `Rigidbody.AddExplosionForce` mit dem Schnittpunkt als
  Zentrum: dort liegen beide Hälften, die Kraft wirkt praktisch nicht. Die Referenz nutzt
  `obj.transform.position` als Zentrum — das funktioniert dort nur, weil ihre Boxen
  ROOT-Objekte sind (nicht im skalierten Raum-Root).
- **Haptik (Controller-Vibration):** Intensitäten zentral in `GameConfig` (Reihenfolge
  **leicht < Noten < Minen**): `HapticLightAmplitude` (0,35) für Schwert↔Schwert und
  Schwert↔Wand, `HapticNoteAmplitude` (**0,95**, Dauer 0,14 s — bewusst die stärkste Stufe:
  Schwert↔Note ist das Primärfeedback bei jedem Schnitt), `HapticMineAmplitude` (1,0) +
  Nachvibration (0,60) für Minen/Bomben. **Geräte-Versand über die NATIVE PICO-API**
  (`Unity.XR.PXR.PXR_Input.SendHapticImpulse`, Dauer in **ms**) in `GameManager.PicoHaptics`;
  Grund: Der reine Unity-Weg (`InputDevice.SendHapticImpulse`) verwarf die Impulse auf der
  PICO 4 stillschweigend, weil `TryGetHapticCapabilities` dort **kein** `supportsImpulse`
  meldet — der Slice-Ton lief weiter, es vibrierte aber nicht. Das alte Capabilities-Gate
  ist deshalb **entfernt**; im Editor/auf anderen Runtimes greift `GameManager.UnityHaptics`
  (ebenfalls ohne Gate), auf Android immer der native Weg (`Application.platform`).
  Gecachte `_devices`/`_scratchDevices`, allokationsfrei.
  **Kontakt-Feedback** (Schwert↔Schwert/Wand) läuft anhaltend in
  `GameManager.UpdateContactHaptics` (nur im Zustand `Playing`, pro Hand über
  `HapticContactInterval` gedrosselt); Noten über `HandleSaberHit`, Minen über die
  Coroutine `MineHapticsRoutine`. Die **Wand bleibt colliderfrei**: Der Klingen-Treffer
  wird manuell über `WallView.IntersectsSegment` (Segment↔Quader, Slab-Verfahren) geprüft
  — analog zur bestehenden `IsHeadInside`-Kopfprüfung. Eingefahrene Schwerter (Easter Egg)
  geben keine Haptik.
- **Menü-Laser existiert nur bei offenem Menü UND nur bei Tafel-Treffer:** Der Strahl ist ein
  `LineRenderer` und liegt NICHT im Canvas — `_canvas.enabled = false` blendet ihn also
  **nicht** mit. Sein Zustand wird deshalb in `HandMenu.Update()` hart an
  `Visible && _canvas.enabled` gekoppelt, das Laser-GameObject bei geschlossenem Menü
  komplett deaktiviert (`SetActive(false)`) und der Strahl nur eingeschaltet, wenn er die
  Tafel wirklich trifft (`SetLaserVisible`, Referenz: SuperSaber `cursor-laser.js`).
  `GameManager.Update()` ruft bei `_menuSide == 0` zusätzlich `Hide()` nach. Beim Ändern von
  `Show()`/`Hide()` nicht darauf verlassen, dass der Strahl mit verschwindet.
- **XRI-Vorlagen-Zeigestrahlen der MR-Vorlage sind AUS:** Die Szene instanziiert
  `MRInteractionSetup.prefab` → `XR Origin Hands (XR Rig)`. Dessen Near-Far-Interactoren
  (`CurveVisualController`, XRI 3.1.2) zeichnen aus **BEIDEN** Händen dauerhaft einen
  schwachen **weißen** Strahl (`LineRenderer`, Zustand „kein gültiger Treffer" — er wirkt
  wie eine schwache Kopie des Menü-Lasers). Das ist NICHT der PICO-Systemstrahl und NICHT
  das Schwert. `GameManager.DisableTemplatePointerRays()` schaltet die Visual-Komponenten
  (`CurveVisualController`/`XRInteractorLineVisual`) samt ihrer `LineRenderer` hart ab; der
  Aufruf hängt an `HideControllerModels()` (Start + 7× über 3,5 s). MrSaber nutzt
  ausschließlich den eigenen `HandMenu._laser` (nur im offenen Menü, nur Gegenhand, nur bei
  Tafel-Treffer). **Nicht** die Interactoren selbst deaktivieren — nur die Visuals.
- **Lösch-Bestätigung = modaler Dialog MITTIG ÜBER der Spielerverwaltung (eigenes Overlay-Canvas):**
  Das Fenster der Spielerverwaltung wird beim Löschen **nicht mehr ersetzt** — es bleibt sichtbar,
  nur leicht abgedunkelt (Scrim), und der Bestätigungs-Dialog (`HandMenu.ShowConfirm`) liegt
  mittig darüber. Dadurch schließt sich optisch kein Fenster. Der Dialog hat **genau zwei**
  Auswahlmöglichkeiten: eine **grüne „Nein"**- und eine **rote „Ja"**-Schaltfläche
  (`UpdateConfirmVisual`: Nein = grün/hervorgehoben bei Auswahl, Ja = rot/hervorgehoben).
  **Wichtig (Fehlerbehebung):** Der Dialog ist bewusst **KEIN Kind des Panels** mehr, sondern ein
  **eigenes `Canvas` mit `overrideSorting = true` (sortingOrder 100)** direkt unter dem HandMenu-
  Root. Die frühere Panel-Kind-Variante wurde auf dem Gerät nicht sichtbar; das eigene Canvas
  erzwingt die Anzeige über der Tafel. `HandMenu.PlaceConfirmOverPanel` setzt die Weltposition
  jeden Frame mittig über die Tafel (`_panel.TransformPoint` der Panel-Mitte).
  Bedient wird der Dialog mit derselben Laser-Markierung wie das Menü:
  `HandMenu.UpdateConfirmPointer` setzt `ConfirmChoice` (0 = Ja, 1 = Nein, Standard Nein), der
  Index-Trigger bestätigt in `GameManager.HandleMenuInput` (`ExecuteDelete`/`CancelDeletePopup`).
  `MenuRows` enthält **keine** Lösch-Bestätigungszeilen mehr (Ids 253/254 frei), und
  `ShowDeleteConfirm` ruft **nur** `ShowConfirm` auf (kein `BuildMenuContent`, sonst würde das
  Fenster neu aufgebaut). `ShowDeleteConfirm` schließt vorher eine evtl. hängende OS-Tastatur,
  setzt die 0,4-s-Eingabesperre und schreibt eine Diagnose (`Lösch-Dialog öffnen …`). Die
  Menütaste bricht die Bestätigung weiterhin ab (siehe `GameManager.Update`).
- **Handmenü-Position & Optik (UI-Überarbeitung):** Die Tafel richtet sich in
  `HandMenu.LateUpdate` weiterhin zur Kamera aus (Billboard). Position: Pivot = Oberkante. Die
  Tafel liegt **immer über dem Controller** mit **festem Abstand** zwischen Controller und
  Tafel-**Unterkante**: `MenuBottomClearance` (**6 cm** Luft dazwischen, in Welt-„oben" gemessen).
  Zusätzlich sitzt sie `MenuForwardOffset` (**10 cm**) **weiter weg** (`+ _cam.transform.forward`)
  und ist `MenuTiltDeg` (**−10°** = 350°, aus 20° um 30° in die andere Richtung) gekippt.
  Da der Pivot die Oberkante ist, wird die volle Panelhöhe entlang
  `transform.up` aufgeschlagen — dadurch bleibt die Unterkante exakt auf dem festen Abstand,
  unabhängig von der Menühöhe. Die drei Konstanten liegen in `HandMenu` und sind ohne Umbau
  tunebar. (Das frühere Zentrieren auf die Controller-Mitte bzw. der 63°-Winkel war nicht gewünscht.)
  **Zeilentypen (alle in `HandMenu.RefreshGeometry`):**
  * **Bedienkarte** (`Row.MultiControl`; Skalierung, Feldhöhe, Audio-Offset,
    Schwierigkeit): Name **MITTIG** oben, darunter links/rechts je ein **sichtbares
    abgerundetes Bedienfeld** (−/◄ bzw. +/►) und der Wert (`Row.Value`) mittig. `Row.Cycle`
    schaltet auf ◄/►-Glyphen. Das anvisierte Feld wird grün hervorgehoben (`_controlHighlight`).
    Semantik: links = Minus/◄, rechts = Plus/► (`_menu.LastPointerX >= 0` = rechts).
  * **Ein/Aus-Zeile** (`Row.Card` + `Row.RightValue`): dezente graue Hinterlegung wie die
    Bedienkarten, **Name linksbündig**, **Status (`AN`/`AUS`) rechtsbündig** („Raum folgt
    Spieler", „Hintergrund"). Ein Trigger irgendwo auf der Zeile schaltet um
    (`GameManager.OnMenuAction` toggelt wieder ganzzeilig).
  * **Schaltfläche** (`Row.Button`): „Spielerverwaltung", „Neuer Spieler", „Spieler löschen",
    „Zurück" (sowie „Eingabe bestätigen"/„Abbrechen") — nur **dezent hellgrau hinterlegt**
    (`BsUi.RowColor`, genau wie die Audio-Offset-Karten), Text zentriert/fett; bewusst KEIN
    heller Akzentrahmen mehr.
  * **Wert rechts** (`Row.RightValue`): Spielerzeilen — Name **linksbündig**, Punkte
    **rechtsbündig**.
  Optik-Basis: `BsUi` liefert eine Farbpalette plus ein **abgerundetes 9-Slice-Sprite**
  (`BsUi.RoundedSprite`) für Tafel, Bedienfelder, Schaltflächen und Dialog.
- **Songliste: Laufschrift + KEINE Pfeil-Buttons, Blättern NUR per Joystick:** Die früheren
  ▲/▼-Buttons und die Laser-Haltezonen (Auto-Scroll am Rand) sind **ersatzlos entfernt**
  (`HandMenu`: `CreateArrowButton`/`UpdateArrowAppearance`/`HandleScrollZones`/`PointerOnUpArrow`
  … gelöscht). Geblättert wird ausschließlich über die Joysticks (beide Hände,
  `GameManager.HandleMenuInput` → `_menu.ScrollList`), die Liste bleibt zyklisch.
  **Liednamen ohne Kappung:** `MenuRows` übergibt den vollen Namen; die Zeile hat ein
  `RectMask2D` (Breite `SongTextWidth` 548), damit nichts über den Fensterrand läuft. Nur die
  Zeile unter dem Laser läuft bei Überbreite als **Laufschrift** durch (`HandMenu.UpdateMarquee`;
  kurz stehen → durchlaufen → kurz stehen; Timer-Reset bei Hover-/Scroll-Wechsel).
  **Gleiche Höhe wie das Hauptmenü (mehr Lieder sichtbar):** `GameManager.BuildMenuContent`
  übergibt beim Öffnen der Songliste `MainMenuPanelHeight()` als `targetPanelHeight` an
  `HandMenu.Show`. `MainMenuRows()`/`PlayersMenuRows()` sind aus `MenuRows()` ausgelagert, damit
  die Hauptmenü-Höhe über `HandMenu.HeightOf(Row)` (Summe + 16 px Rand) berechnet werden kann
  (sie hängt u. a. von „Run and Play" ab — dann kommt „Lauf-Tempo" dazu). `HandMenu` leitet
  daraus `_viewportRows = floor((Zielhöhe − 16) / 72)` ab (statt fix 7 → **11**, bzw. 13 mit
  Lauf-Tempo) und füllt den Rest gleichmäßig oben/unten als Rand auf (`extra`), sodass die Tafel
  **exakt** die Höhe des Hauptmenüs hat. `HandMenu.HeightOf` ist die öffentliche Fassung von
  `RowHeightFor`.
- **Namenseingabe (OS-Tastatur behalten, aber robuster):** Während `_awaitingName` zeigt
  `MenuRows` **nur** die Eingabezeilen (keine Titel-/Schwierigkeitszeile), damit der
  Bestätigungs-Trigger nicht versehentlich die Songliste öffnet bzw. die Schwierigkeit ändert
  (das ließ das „Namensfeld" scheinbar immer wiederkommen). Die erste auswählbare Zeile ist
  „Eingabe bestätigen" (Standard-Cursor). `PollKeyboard` schreibt `_keyboard.text` **jeden
  Frame** mit, wertet `Done` **und** `LostFocus` als Übernahme und zeigt den Namen live als
  „Name: …" an. `DismissKeyboard` schließt die Tastatur anschließend zwangsweise.
- **Grifflabel („eingraviert"):** Name des aktiven Spielers auf dem **roten (linken)**
  Griff, Bestwert des aktiven Spielers zum **aktuellen Song** auf dem blauen (rechten) Griff.
  Sitzt am `SaberView.LabelAnchor` **auf der Grifffläche** — bewusst **KEIN Billboard**
  (es gibt kein `FaceCamera` mehr; die Labels folgen dem Schwert, nicht dem Kopf).
  Zwei Ebenen (dunkle Gravur vorne, helle Lichtkante dahinter) erzeugen den eingelassenen
  Eindruck. **Wichtig:** Der Anker neutralisiert die nicht-homogene Grundskalierung des
  Schwerts (0.75/0.5/0.75) per Kehrwert — nur so skaliert die Schrift korrekt mit der
  Raum-Skalierung. Deshalb in `ApplyRoomScale` **kein** zusätzliches `localScale` setzen
  (sonst doppelt skaliert). `UpdateLabels` vergleicht jede Frame → Rekorde erscheinen
  sofort, ohne Songwechsel (deshalb kein `_labelsDirty` mehr).
  **Ausrichtung/Größe (beide Seiten kleiner und flacher):** Der Anker sitzt mittig auf
  dem Griff (`SaberView`: go-Lokal y = 0.10 = Zylindermitte, ±0.2), damit die Schrift an
  keinem Ende über den Griff hinausläuft. Die **blaue (rechte)** Beschriftung wird im
  `GripLabel`-Root um **180° um die Flächennormale (lokale Z)** gedreht (rote Seite
  ungedreht), weil sie sonst auf dem Kopf steht. Die Zeichengröße ist über
  `GripLabelCharSize` (0,010 statt 0,014) verkleinert, zusätzlich staucht
  `GripLabelHeightScale` (0,72) die Höhe quer zur Leserichtung (X = Leserichtung/
  Grifflänge, Y = Schrifthöhe über den Griff) — dadurch liegt der Text flach auf der
  schmalen, gebogenen Grifffläche auf, statt seitlich über deren Silhouette zu ragen.
  Das ist der **Basisfaktor** der Schrift (analog zu `charSize`), kein Per-Frame-Wert:
  die Raum-Skalierung kommt weiterhin allein über die Schwert-/Anker-Kette (nicht doppelt).
  **Krümmung (Text folgt dem Griff-Zylinder):** Der Text liest entlang der Griffachse (lokale
  X des Labels), seine Höhe verläuft um den Umfang (lokale Y). Damit der flache Text der
  Rundung folgt, wird er über den Shader **`BeatSaber/GripCurvedText`**
  (`Assets/Resources/BeatSaber/GripCurvedText.shader`, Basis = Unity `GUI/Text Shader`) auf
  einen zum Griff konzentrischen Zylinder gewickelt: `y → (R·sin φ)/h`, `z += R·(1−cos φ)` mit
  `φ = (y·_HeightScale)/R`. `R = GripCurveRadius` (**0,0195 m** = Abstand Label-Ebene ↔
  Griffachse) und `_HeightScale = GripLabelHeightScale`. Das Material wird pro Label-Ebene in
  `GameManager.MakeCurvedFontMaterial` aus dem Font-Atlas erzeugt (`Resources.Load<Shader>` →
  Build-sicher); fehlt/nicht unterstützt der Shader (`!isSupported`), bleibt das flache
  Font-Material. `RefreshCurvedAtlas` hält den Font-Atlas nach Textänderungen aktuell.
- **Ebenen-Offsets (Menüzeilen „Audio-Offset" / „Noten-Offset") — Drei-Ebenen-Modell:**
  Die **Score-Uhr** (`AudioManager.ScoreTime`) ist die feste Referenz; die beiden Regler
  verschieben NUR ihre eigene Ebene gegen diese Referenz (±300 ms, 5-ms-Schritte,
  `GameConfig.PlaneOffset*`; **+ = die Ebene kommt später**):
  1. **Audio** (`SaveData.SoundDelayMs` → `AudioManager.SetAudioOffset`): wird beim Songstart
     **physisch** angewendet — positiv = Vorlauf/Musik startet später, negativ = Sprung in
     den Clip (`BeginMusicWithOffset`); bei Änderung wird live nachgezogen (im Pausenmenü
     per Seek, ohne die Score-Uhr zu bewegen). `GameConfig.AudioLatencyBaseMs` = **275 ms
     FEST** als Standard-Kompensation der Geräte-Ausgabe-Latenz in der Score-Uhr
     (`ScoreTime = AudioTime − Basis`); der Regler startet bei **0** (früher 190 + −85).
     **Migration:** `LoadSave`/`MigrateAudioOffset` zieht den früher gespeicherten Offset
     EINMALIG auf 0 (`SaveData.SoundDelayBakedIn`), da die 85 ms jetzt in der Basis stecken.
     **Zwei Fehler, die nicht wiederkommen dürfen:**
     (a) `AudioTime` läuft nach natürlichem Datei-Ende **weiter** — sonst stünde die Uhr bei
     `length − Delay` still und ein Delay > 0,4 s würde das Song-Ende nie erreichen.
     (b) Der Songwechsel feuert bei `AudioFinished || scoreTime ≥ Länge − 0,4 s`.
  2. **Darstellung** (`SaveData.VisualOffsetMs` → `AudioManager.VisualTime =
     ScoreTime − VisualOffset`): verschiebt NUR die gezeichneten Noten/Wände samt
     Animationen/Spawn-Effekten (`NoteView.Update`/`WallView.Update`). Treffer-Erkennung,
     Zentrum-Score, Wand-Kollision, Saber und SFX bleiben **unbeeinflusst**
     (`NoteView.ScoreWorldPosition`, `WallView.ScoreToLocal`).
  Beide Offsets sind unabhängig; Default 0 = alles deckungsgleich. Saber = **kein
  Input-Delay**, SFX klingen **sofort**.
- **Sound: EIN Clip, zwei geklopfte Einsätze (keine vorberechneten Audiodateien):**
  Quelle beider Effekte ist `Slice.ogg` (`GameConfig.SliceSfxResource`). Die gewünschte
  Klangfärbung entsteht zur **Laufzeit** über Tiefpass (`AudioLowPassFilter`) +
  `AudioSource.pitch` + Startversatz (`timeSamples`) — alles per `GameConfig`-Konstante
  tunebar, ohne neue Assets im Build:
  - **Schwert↔Note** = `AudioManager.PlayHitSfx()` (Aufruf in `HandleSaberHit`): Tiefpass
    1000 Hz („dumpf"), Pitch 0,85 (tiefer), **Lautstärke 0,20** — „direktes dumpfes Geräusch,
    leise, aber hörbar". `HitSfxVolume` = 0 schaltet stumm, ohne dass der Aufruf verschwindet.
    Geräte-Erfahrung: **0,12 war zu leise (kaum hörbar), 0,28 war zu laut** → 0,20 ist der
    Kompromiss. Ebenso war der Tiefpass bei 850 Hz zu substanzlos → 1000 Hz.
  - **Schwert aus-/EINFahren** (Easter Egg, `ToggleSaberRetract`) =
    `AudioManager.PlaySaberToggleSfx()`: **beide** Richtungen, derselbe Slice-Sound aber
    **vorne abgeschnitten** (`SaberToggleSfxSkipMs` = 45 ms), **beschleunigt**
    (Pitch 1,9) und nur **leicht** dumpfer (Tiefpass 4000 Hz) — wirkt direkt wie ein
    Schalter-Klick statt wie ein Schlag. Früher: Ausfahren = Slice-Sound, Einfahren =
    UI-Klick. **Geräte-Erfahrung: Tiefpass 1800 Hz war „zu dunkel"/verstopft** → 4000 Hz;
    der Klick soll hell genug bleiben, um als Klick gelesen zu werden.
  - **Warum drei AudioSources:** Ein `AudioLowPassFilter` wirkt auf **alle** AudioSources
    seines GameObjects. Deshalb liegt jede Effekt-Quelle auf einem **eigenen Kind-Objekt**
    (`SfxUi` ungefiltert, `SfxHit`, `SfxToggle` — `AudioManager.MakeSfxSource`), sonst
    würde der dumpfe Schnitt-Ton auch Musik und UI-Klick mitfiltern.
  - **Warum `timeSamples` funktioniert:** `Slice.ogg.meta` hat `loadType: 0`
    (Decompress On Load) → der Clip liegt als PCM im Speicher, der Sample-Versatz ist
    zuverlässig. Bei einem **Streaming**-Clip (`loadType: 3`) gäbe es keinen Versatz —
    dann müsste der „vorne abgeschnittene" Ton als eigene Datei vorliegen.
  - **UI-Klick** (`UI_Confirm.mp3`, `UISfxVolume` = 0,20, vorher 0,48) bleibt unverändert;
    er wird nur noch im Menü benutzt, nicht mehr für das Easter-Egg-Schwert.
- **Rechtes Schwert +5 cm (PICO-Portierung, BEHOBEN):** `SaberView.SaberRightSideShift` stand
  auf 0,05 m und wirkte NUR auf die rechte/blaue Hand (Meta-Quest-Ergonomie). Auf der PICO ist
  das ein starrer Code-Offset im Controller-Lokalsystem: Das rechte Schwert erscheint im
  Fernbereich für **beide** Augen ~5 cm nach rechts (linkes Schwert korrekt). Der Wert MUSS 0
  bleiben; seitliche Ergonomie nur symmetrisch für beide Hände. Die im Rig versteckten
  Meta-Controller-Modelle beeinflussen das Schwert NICHT — es hängt am Controller-Anchor
  (Pose), nicht am Modell-Pivot.
- **Tracking-Nullpunkt sichtbar machen (Kalibrierung):** Hauptmenü-Zeile „Controller anzeigen"
  (Id 117, **Session**, Default AUS) hängt `ControllerDebugView` an BEIDE benutzten Anker
  („…/Camera Offset/Left|Right Controller"): gelbe Ursprungs-Kugel + RGB-Achsenkreuz am rohen
  Pose-Nullpunkt, dazu das PICO-4-Controller-Modell aus dem SDK (Package-Resources
  `Prefabs/LeftControllerModel` / `Prefabs/RightControllerModel ` — **Achtung: Leerzeichen vor
  der Endung**; auf dem Gerät lädt `PXR_ControllerLoader` das System-Modell, Editor/Fallback =
  SDK-Prefab, sonst grauer Proxy mit Warnung). Die Debug-Objekte sind über die Marker-Komponente
  von `HideControllerModels` ausgenommen und haben **keine Collider**. Beim Start loggt der
  GameManager außerdem den tatsächlich benutzten Anker-Pfad (`[BeatSaber] Controller-Anker: …`)
  — erster Blick bei allen Pose-/Offset-Fragen (das Rig verschachtelt mehrere Kandidaten).
- **Passthrough ist monokular — NICHT erneut mit Geometrie „reparieren":** Die PICO 4 zeigt nur
  **eine** Kamera durch. Das durchsichtige Bild ist damit bereits eine andere (mono)
  Projektion als das Stereo-Bild der virtuellen Szene; virtuelle und reale „Wirkung"
  lassen sich über Noten-Geometrie grundsätzlich nicht zur Deckung bringen. Zwei bereits
  gebaute und wieder **entfernte** Lösungsversuche:
  (a) „Parallaxe" = Verschatz des gesamten `_gameRoot` proportional zur seitlichen/vertikalen
  Kopfbewegung — wirkungslos, weil genau diese Kopfbewegung die Passthrough-Projektion schon
  enthält, die man eigentlich korrigieren wollte.
  (b) „Tunnel-Verjüngung" = Querschnitt des Notenfelds nach hinten verengen (pro Objekt über
  eine un-verjüngte Wahrheitsposition `_basePos` in `NoteView`/`WallView`).
  **Symptom, an dem man das wiedererkennt:** Der Eindruck „ferne Noten passen nicht zum echten
  Raum" ist der **Mono-Passthrough selbst**, kein Fehler im Noten-Raster. Bevor dafür erneut
  Code gebaut wird, erst prüfen, ob ein **stereoskopisches** Passthrough überhaupt verfügbar
  ist — ohne zwei Augen-Texturen ist das Ziel nicht erreichbar.
  **Nicht** IPD/Stereo-Separation verstellen: das PICO-SDK bietet dafür nur
  `PXR_Enterprise.SetIPD` (Enterprise-/ToB-Geräte) — und das ändert die **System**-IPD des
  Headsets, nicht die der App.
- **Menü-Automatik für Schwerter:** Beim Öffnen des Menüs fahren **BEIDE** Lichtschwerter
  automatisch ein. Dadurch bleibt als einziger „Beam" der Laser/Cursor der Gegenhand (der
  Hand, aus der der Laser kommt) sichtbar — die Menüseite leuchtet nicht mehr (früher blieb
  dort das ausgefahrene Schwert als dauerhaft sichtbarer „schwacher Beam"). Der Cursor
  erscheint unabhängig vom Schwertstatus. Beim Schließen fährt jedes Schwert automatisch
  wieder aus, **außer** sein Status wurde während des Menüs per Grip-Doppelklick manuell
  geändert (`_menuManualChanged[]`); dann bleibt der Nutzer-Zustand.
  **Ergänzung (Klingen-Rest):** Sobald die Klinge ganz eingefahren ist, wird ihr Renderer
  komplett ausgeblendet (`SaberView._bladeRenderer` in `AnimateRetract`). Die frühere
  Skalierung auf 0,02 ließ einen hell leuchtenden weißen Rest am Griff stehen, der wie ein
  schwacher zweiter „Beam" aus der Hand (bzw. aus BEIDEN Händen) aussah. Ein Seitenwechsel des
  Menüs braucht keine Schwert-Automatik mehr (beide sind bereits eingefahren).
- **Songliste (Überarbeitung) — endlos, ohne dauerhafte Auswahl:** Das aktuelle Lied ist nur
  per Hintergrund (`ActivePlayerBg`) hervorgehoben, **nicht angewählt**. Es gibt keinen
  persistenten Auswahl-Cursor mehr: Die grüne Markierung (`_cursorHighlight`) sitzt genau auf
  der Zeile unter dem Laser (`_hoverRow`, in `RowHitScan`/`HitRowAt` jeden Frame bestimmt) —
  zeigt der Laser auf kein Lied, ist nichts markiert (`ClearHover`). Der Joystick bewegt die
  **gesamte Liste** um einen Eintrag nach oben/unten; die Liste ist **zyklisch**
  (`_scrollOffset = Mod(offset + dir, Liederzahl)`), hat also kein Ende (Lieder wiederholen
  sich). Beide Sticks (links/rechts) blättern, unabhängig von der Menü-Seite. `Show(rows, true,
  currentIndex)` zentriert nur das Fenster auf das aktuelle Lied (`Mod(index - Viewport/2, n)`).
  **Fallstrick:** Die Zeilengeometrie (`_rowCenterY[_rowHeights]`) ist auf den **Inhalt-Index**
  gekeyed (nicht auf die Display-Position); das funktioniert, weil im Fenster bei `n > Viewport`
  jede Zeile höchstens einmal vorkommt. `ScrollBy` bestimmt nach dem Scrollen die Zeile unter
  dem ruhenden Laser neu, damit die Markierung auf dem jetzt dort stehenden Lied bleibt.

---

## 8. Referenzprojekt & bewusste Entscheidungen

Geprüfte Vorbilder (beide enthalten **keinen eigenen Floor-/Tracking-Origin-Code** — weder
`TrackingOriginModeFlags`, `TrySetTrackingOriginMode`, `TryRecenter` noch `PXR_System`/
`GetFloorHeight`; die Bodenhöhe entsteht dort rein aus der serialisierten `XROrigin`-Config):

- `Y:\Unity\BeatSaberClone-main - Kopie` (Build-Szene `Assets/Scenes/GameScene.unity`) —
  MRTK-3-Rig als Prefab-Instanz, **kein** Skript-Code am Rig.
- `Y:\Unity\My project` — dieselbe Vorlage ohne PICO-SDK; die `XROrigin`-Verweise in
  `SampleScene.unity` sind **stripped/tot** (die Quell-fileIDs existieren in
  `MRInteractionSetup.prefab` nicht). Echte `XROrigin`-Instanzen nur in ungenutzten
  XRI-Sample-Prefabs.
- `C:\Users\Riegholm\Downloads\ArcViewer-main` — Unity-Viewer; Quelle für das **Raster**
  (Spalten 0,6 m, Reihen 0,85/1,40/1,95, Wand-Längen = Dauer × NJS), das **Sprung-Timing**
  (HJD/NJS/ReactionTime, Rang-Vorgaben), die **musterabhängigen Noten-Effekte**
  (`NoteManager.GetStartY`/`GetFlipYHeights`) sowie **Arcs/Chains**
  (`ArcManager`/`ChainManager`/`ArcHandler`/`ChainLinkHandler`, `ScoringUtils`).
- `C:\Users\Riegholm\Downloads\Gitdownloade\OpenSaberPlus-godot-4-port` — Godot-4-Port
  (GDScript); **Quelle der Anlauf/Durchzug-MESSUNG** (`game/LightSaber.gd`:
  `get_preswing_angle`/`get_followthrough_angle` = größte Abweichung der KLINGENRICHTUNG in
  einem ±200-ms-Fenster; `game/Constants.gd`: `TARGET_PRESWING_ANGLE` 100° /
  `TARGET_FOLLOWTHROUGH_ANGLE` 60°). Deren Punktverteilung (`SWING_PRESWING_SCORE` 90 /
  `SWING_FOLLOWTHROUGH_SCORE` 40 / `SWING_ACCURACY_SCORE` 20 = 150) wurde **nicht** übernommen:
  hier bleibt die Original-Verteilung 70/30/15 = 115. Außerdem Quelle der Farbe-Layer-Trennung
  und der Ketten-Boxen.
- `C:\Users\Riegholm\Downloads\BeatSaber-Web-Replays-master` — Web-Viewer (JavaScript);
  frühere Quelle der Winkelformel (`src/components/saber-controls.js` `maxAnglePlaneX/Y/XY`),
  **verworfen**: Ein Ebenen-Winkel sättigt bei 90° und behandelt gerade/diagonal ungleich.
- `C:\Users\Riegholm\Downloads\supersaber-master` — Web-/A-Frame-Clone (JavaScript);
  Quelle für das **Cursor-Laser-Verhalten** (`src/components/cursor-laser.js`). Die dortige
  Schwungmessung (`saber-controls.js` `maxAnglePlaneX/Y/XY` mit 15°-Totzone) und die
  ±36°-Einrotier-Animation wurden bewusst NICHT übernommen (siehe Invariante 6 / 3b).

> **Achtung, häufige Fehldeutung:** `XROrigin.TrackingOriginMode` ist
> `NotSpecified = 0`, **`Device = 1`**, `Floor = 2`, `Unbounded = 3`
> (`XROrigin.cs:69-116` in `com.unity.xr.core-utils`). Ein serialisiertes
> `m_RequestedTrackingOriginMode: 1` bedeutet also **Device**, NICHT Floor. Die
> BeatSaberClone-Referenz läuft denn auch in `Device` mit `m_CameraYOffset: 0` — dort ist
> Welt-Y 0 die **Augenhöhe der Startpose**, der reale Boden liegt rechnerisch ~1,6 m
> darunter (Y ≈ −1,6). Sie ist damit **kein** Vorbild für einen bodennahen Aufbau.
> (`Assets/drgdrg.unity` nutzt `m_CameraYOffset: 1.1176` — dort wäre Y 0 eher der Boden.)


Bewusste Abweichungen/Entscheidungen (dokumentiert, nicht ohne Benutzer-Freigabe ändern):

- **Hintergrund-Option (Passthrough / Default / Animiert) — ersetzt den Passthrough-Schalter
  (Nutzer-Freigabe):** `SaveData.BackgroundMode` (0=Passthrough, 1=Default, 2=Animiert;
  Menüzeile „Hintergrund", ID 113). Der frühere Bool `SaveData.PassthroughDisabled` bleibt
  nur für alte Saves deklariert; ein altes „Passthrough AUS" wird EINMALIG auf „Default"
  migriert, sonst bleibt der Standard für Bestands-Saves Passthrough.
  * **Passthrough:** Video-Seethrough an, Kamera transparent (unverändert). Es wird
    KEINE Bühne gezeigt → weiterhin maximal performant.
  * **Default:** die prozedurale **Bühne** sichtbar, RUHIGE Standardbeleuchtung
    (`EnvironmentLightManager.StaticLights = true`) — KEINE Per-Frame-Animation.
  * **Animiert:** dieselbe Bühne mit der Song-Lightshow aus den Map-Dateien
    (v2 `_events`, v3/v4 `basicBeatmapEvents` + `colorBoostBeatmapEvents`): Farben
    On/Flash/Fade/Transition, Ring-Spin (8), Ring-Zoom (9), Laser-Drehgeschwindigkeit
    (12/13), Color-Boost — Auswertung `EnvironmentLightManager`. **Maps ohne Events
    bekommen `AutoLightshow`** (Standard-16/32-Beat-Muster aus der Palette) — damit
    ist die Bühne immer belebt (SuperSaber-Prinzip).
  **Farben (Original-treu):** Die Lichtfarbe kommt (a) aus der **Default-Palette der
  Umgebung** (`EnvironmentColorTable`, Werte rekonstruiert aus ArcViewer),
  (b) überschrieben durch die **Map-Umgebungsfarben** (`_envColorLeft/Right/…Boost` aus der
  info- bzw. Difficulty-`_customData`, v4 auch Hex `environmentColor0/1…`) und (c) für
  einzelne Events durch **Chroma-Event-Farben** (`customData.color` / `_customData._color`,
  `LightEvent.HasCustomColor`). Die Lightshow-Farben folgen damit exakt der Map.
   **Bühne (Stand 09.10.2026):** Die ArcViewer-Umgebungs-SZENEN sind wieder IM Projekt
   (`Assets/ArcViewerEnvironmentScenes`, 13 Einträge in den Build Settings) und werden
   von `EnvironmentDirector` additiv unter den Raum-Anker geladen (dreht garantiert mit).
   Der zwischenzeitliche prozedurale `StageView`-Umbau wurde wieder verworfen. Der
   BLOOMFOG ist damit wieder komplett: Capture-Kamera fotografiert NUR die Glow-Ebene 11
   (ArcViewer-Maske — vorher ohne Maske: Gesamtszene in der Nebeltextur, Kosten ohne
   Nutzen), die aktiven Shader lesen `_BloomfogTex` (Nebel + Fake-Reflexionen auf den
   Plattform-Shadern), ein NDC-Vollbild-Quad (`Resources/BeatSaber/BloomfogSkyboxQuad`)
   zeigt die Blur-Textur als leuchtenden Hintergrund (Queue 990, vor aller Geometrie),
   und Post-FX laufen in ArcViewer-Werten (Bloom Schwelle 1,0 / Intensität 1,5 / Scatter
   0,1 / 4 Iterationen, kein Tonemapping, Schatten-/Licht-Regularie + Gamma-Lift wie
   ArcViewer PostProcessProfile). Der HDR-Grund fürs Glühen ist
   `EnvironmentLightManager.LightEmission = 6,5` (ArcViewer-Wert; mit 1,0 blieb alles
   unter der Bloom-Schwelle = die gemeldete „matte" Optik). Tint/Hintergrundfarbe
   (`ApplyGlobalTint`) bleiben bewusst LDR (eventColor statt laserColor). Editor und
   Gerät nutzen denselben Qualitätslevel „Performant" (`m_PerPlatformDefaultQuality:
   Standalone: 1`) → identische Optik (der Balanced-Renderer hat kein Bloomfog-Feature).
   Der Environment-Name der Map steuert weiter NUR Farben/Parameter
   (`EnvironmentColorTable`/`EnvironmentParametersTable`, 13 Paletten), nicht Geometrie.
   **Hintergrund = Bloomfog-Blur + Solid-Color im Takt:** `EnvironmentLightManager.
   CurrentBackground` (dunkle Mischung aus Ring-/Rückenlaser-Farbe) bleibt die Kamera-
   Clear-Farbe; SICHTBAR ist dahinter das Skybox-Quad mit der weichgeblurnten Lightshow.
   **Gründe GEGEN die Szenen-Lösung (historisch, durch den Bloomfog-Vollausbau gegen-
   standslos):** Unity-6-Szenen in Unity 2022 → Parse-/Build-Fehler, Static-Batching,
   „missing script"-Warnungen. Die GUIDs der Skripte/Materialien blieben dadurch
   erhalten; nur die SHADER-Inhalte wurden auf den ArcViewer-Originalstand gesetzt.
   **STATIC-FLAGS (historisch):** Die ArcViewer-Szenen trugen
   `m_StaticEditorFlags: 2147483647` → statisch gebatchte Objekte bewegen sich beim
   Raumdrehen NICHT. Lösung heute: die Szenen-Objekte werden beim Laden unter den
   Raum-Anker umgehängt (`MoveGameObjectToScene` + `SetParent`), Static-Batching bleibt
   dafür AUS für sie. QUELLE DER EHEMALIGEN BUILD-BRUCHES: ein Regex-Patch ersetzte
   YAML-Zeilen wortwörtlich mit `$10` → Parser-Fehler → „Error building Player".
   **Merkregel:** PowerShell-Ersetzungen NIE mit `'$10'`-artigen Ersetzern (einzelne
   Hochkommas schützen nicht vor der .NET-Gruppen Syntax!) — Match-Evaluatoren oder
   `$1`-Referenzen mit geschweiften Klammern `'${1}0'` verwenden.
   **Performance (09.10):** Die Erfassungs-Kamera rendert dank Culling-Maske nur die
   ~64 Glow-Quads (vorher die GESAMTE Szene bei 512 px + 6 Blur-Pässe). `LightHandler`
   setzt seine MaterialPropertyBlocks NUR bei Farbänderung (vorher >180 Renderer-Writes
   pro Frame im Modus „Animiert" — MPB macht Renderer zudem SRP-Batcher-untauglich).
   Im Passthrough passiert NICHTS (Root inaktiv, Capture aus, Post-FX aus).
- **Treffer-Effekte (Runde 09.10 neu):** Pro Schnitt ein richtungstreuer Funken-Stoß am
  Schnittpunkt (`Fx.PlayHit(point, side, vel, scale)` — nicht am Schwertgriff): Die
  Funken fliegen entlang der Schwunggeschwindigkeit der Klingenspitze (gestreckte
  Streaks, Kegel 22°, kaum Gravitation, HDR-Farben → Bloom-Glühen). Das bisherige
  `ParticleRedFire/BlueFire`-Prefab emittierte als Kegel nach OBEN mit Gravitation 1 —
  exakt der gemeldete „Fontänen"-Look — und ist ungenutzt (im Projekt belassen, wie die
  brachliegenden Schockwellen `Fx.PlayShockwave`).
- **Minen explodieren (Runde 09.10):** Eine Berührung der Mine löst
  `Fx.PlayMineExplosion` aus (radialer Weißblitz-Stoß an der Minenposition — Referenz
  BeatSaber-Web-Replays `destroyMine`: omnidirektional ~1,4–1,9 m/s, ohne Gravitation,
  additiv, gestreckt, schrumpfend) — vorher verschwand die Mine still. Haptik
  (`MineHapticsRoutine`), „MISS"-Label und Combo-Bruch bleiben wie bisher.
  Beide Effekte sind prozedural gebaut (Code-konfigurierte ParticleSystems, Vorlagen
  gepoolt, Farbe je Stoß über `MaterialPropertyBlock` auf `BeatSaber/Spark`-Shader)
  — keine neuen Asset-Abhängigkeiten, allokationsfrei im Trefferpfad.
- **Bodenniveau über Tracking-Origin `Floor` (PICO + OpenXR):** Beim Spielstart gesetzt und
  wiederholt, bis bestätigt. Zur Laufzeit/bei A wird er NICHT mehr angefasst. Es gibt **keine**
  laufende Head-Set-Kopplung und **keinen** Tracking-Reset. Der frühere persistente **Boden-Anker**
  (`SaveData.FloorOffsetMeters`, Menüzeile „Boden-Anker") ist ersatzlos entfernt; der
  **Session-Bodenversatz** per linkem Stick gibt es wieder (startet bei jedem Spielstart bei 0,
  wird NICHT gespeichert, A setzt ihn NICHT zurück). `PXR_Enterprise.GetFloorHeight()` bleibt außen vor
  (ToB-Dienst). Die Unterkante von Wänden liegt **auf dem Boden**; das HUD beginnt auf
  Bodenhöhe.
- **Raum-Ausrichtung:** **Taste A** tut GENAU zwei Dinge: `_roomFollowOffset` = Kopf-X/Z
  (Raum sitzt direkt unter dem Head-Set, Head-Set über dem Raum-Ursprung 0/0 an der
  Noten-Treffebene) und `_roomYawDeg` = aktuelle (horizontal projizierte) Blickrichtung.
  **Höhe/Y bleibt**; der Tracking-Ursprung wird NICHT angefasst (kein
  `TryRecenter`/`SetTrackingOrigin` — das verschob auf PICO den Boden). Der Boden wird
  **nur beim Spielstart** gesetzt (`EnsureFloorOrigin(mustSucceed: true)`). Der **rechte
  Stick** links/rechts dreht den Raum (`GameManager.UpdateRoomAdjustment`), der **linke
  Stick** hoch/runter verstellt den **Session-Bodenversatz** (`_floorOffsetMeters`).
- **Tiefen-Folge („Raum folgt Spieler", Menü Ein/Aus, persistent):** aktiv => der Raum wird
  um die **Vorwärts-/Rückwärts-Komponente** der Kopfbewegung entlang der Raum-Vorwärtsachse
  mitgeführt (`_roomFollowOffset`, `GameManager.UpdateRoomFollow`, im `LateUpdate` vor
  `ApplyRoomAnchor`); seitliche Bewegung wird **nicht** gefolgt.
- **„Run and Play" (Menü Ein/Aus, persistent + persönlicher Regler):** koppelt die Spiel- UND
  Audiogeschwindigkeit an die physische Vorwärtsbewegung. AN erzwingt die Tiefen-Folge und
  **sperrt** die Menüzeile „Raum folgt Spieler"; AUS stellt den vorherigen Wert wieder her.
  Die Vorwärtsgeschwindigkeit wird über `GameConfig.RunPlaySpeedWindow` (0,3 s) gepuffert
  (`Core/RunAndPlay.cs`, allokationsfrei) und als geglätteter Durchschnitt ausgegeben.
  Drei Zustände (`RunAndPlayUpdate`): **Stillstand** → Song/Noten-Timeline komplett pausiert;
  **vorwärts** → `playRate = v × songFactor × comfort`, `songFactor = 1/(_roomScale × NJS)`,
  comfort = `PlayerProfile.RunComfort` (0,2…3,0 in 0,1er-Schritten, **persönlich**); bei
  `comfort = 1` stehen die Noten **exakt statisch im echten Raum** und der Spieler läuft auf
  sie zu (der Faktor fällt mit NJS — schnellere Songs = niedrigerer Multiplikator); **rückwärts**
  → Song stoppt SOFORT (rohes Vorzeichen), das Feld zieht per `UpdateRoomFollow` mit (Abstand
  konstant, kein Zurück-Schummeln). Tempo/„Pause" laufen über `AudioSource.pitch` +
  `AudioManager.SetRunPlayRate/SetRunPlayPaused`; die Menü-Pause bleibt unabhängig
  (`ApplyPlaybackState`). `AudioFinished` ist bei Lauf-Pause aus, damit die Score-Uhr im
  Stillstand nicht über die unscaledTime-Nachlaufuhr weiterläuft.
- **Noten-/Wandhöhe & Raum-Skalierung:** Die Grundhöhe kommt aus dem ArcViewer-Raster
  (`GameConfig.GridPosition`: Vanilla-Reihen; `ObstacleBounds`: Roh-Werte). Die
  Menüzeile **„Feldhöhe"** (`SaveData.FieldHeightMeters` → `GameConfig.FieldHeightOffsetMeters`,
  ±1,5 m in 0,05-m-Schritten, persistent) verschiebt **alle Objekte außer dem Boden** nach oben:
  Noten/Minen, schwebende Wände (komplett) UND bodenständige Wände (die nach UNTEN bis zum Boden
  verlängert werden, damit sie ihn weiter berühren) — angewandt **jeden Frame** in
  `NoteView`/`WallView` (wirkt **live**, auch mitten im Lied). **Der Boden bleibt unberührt.**
  Das **GESAMTE Spielfeld** (inkl. Wände) hebt/senkt dagegen der **linke Stick**
  (`_floorOffsetMeters` → `_gameRoot`). Die Treffer-/Score-Prüfung liest dieselbe Position/Größe
  (Notenbox = Anzeige), deshalb wandert die Score-Zone mit. Darüber liegt die globale Skalierung
  (`PlayerProfile.Scale`, Menüzeile „Skalierung"), die den gesamten Raum (`_gameRoot` +
  Schwerter) um den Weltursprung skaliert. HUD skaliert als Kind mit.
- **HUD raumfest:** Kind von `_gameRoot` bei (0, 0.08, 0.7), 45° geneigt; `_gameRoot` steht
  fest auf dem Weltursprung, das HUD beginnt also auf Bodenhöhe. Kein Billboard; folgt der
  Raum-Drehung.
- **Wände im ArcViewer-Look (hellblau):** Körper- und Kanten-Shader + Materialien stammen
  aus dem Referenz-Viewer (`Wall.mat`/`WallEdges.mat`, siehe §8). Farbe (hellblau wie das
  frühere Glas), Deckkraft und Helligkeit sowie die Kanten-Breite (`_WallScale`) laufen über
  einen wiederverwendeten `MaterialPropertyBlock`, nie über geklonte Materialien. Der frühere
  Laufzeit-Glasblock (URP/Lit, Alpha 0,30) ist nur noch Fallback, falls die Assets fehlen.
- **Schwert-Geometrie = SuperSaber-Referenz:** Klinge 0,10..1,00 m nach vorn (0,9 m lang),
  Griff ±0,10 m um den Controller — 1:1 aus `supersaber-master/src/index.html` (Macro `saber`),
  darüber die PICO-Ergonomie (aktuell 25° Hochneigung; Seiten-/Zurück-/Runter-Versatz **0**)
  in `SaberView.Build` in
  `SaberPitchUp/SaberBackShift/SaberDownShift/SaberRightSideShift` — sie liegt auf der WURZEL.
  Der frühere **+5-cm-Rechts-Versatz nur der rechten (blauen) Hand** (Quest-Ergonomie) ist
  **entfernt** (PICO-Portierungsfehler): Er verschob das rechte Schwert im Fernbereich für
  **beide Augen** ~5 cm nach rechts, das linke war korrekt. Asymmetrische Seiten-Versätze
  sind deshalb verboten; seitliche Ergonomie nur symmetrisch für beide Hände.
  Wichtig: Die Score-Geometrie (`Tip`/`BaseTrans`) sind Kinder der Klinge und wandern mit —
  Optik und Trefferfläche können nicht auseinanderlaufen.
- **Menü-Bedienung:** Index-Trigger = Bestätigen; Grip nur Easter Egg
  (Schwert ein-/ausfahren; eingefahren = keine Noten-Treffer).
- **NJS/Spawn (Song-Daten):** echte Maps nutzen NJS/HalfJump/NoteJumpStartBeatOffset;
  fehlt der NJS, gilt die Rang-Vorgabe (Easy/Normal/Hard 10, Expert 12, Expert+ 16). Die
  Bewegung ist KONTINUIERLICH mit NJS (`GameConfig.JumpZ`), Z jeden Frame aus
  `GameManager.VisualTime` (FEST = Score-Uhr; der frühere Noten-Offset ist entfernt); Spawn = Beat − 2 × ReactionTime. Die Z-Bewegung hat keine
  Phasen (kein schnell→langsam). **Positions-Effekte pro Note nur nach Muster:**
  Stapel gleiten hoch, überkreuzte Doppeln flippen; normale Noten fliegen gerade
  (siehe Invariante 3b). Eine globale Drehung/Einrotieren gibt es bewusst NICHT.
  **Chart-treuer Spawn:** Noten erscheinen auf ihrer echten Reihe (`_lineLayer`); die
  Pfeil-Zielausrichtung steht ab dem ersten Frame fest (kein Einrotieren).
- **Schnittrichtung (1:1 Clone `BoxView.CheckSliceDirection` + OpenSaberPlus `BeepCube`):** Die
  Richtung ist die **Schneide-Bewegung** (`cutVelocity` = geglättete Spitzen-Geschwindigkeit,
  siehe Invariante 6) im **Noten-Lokalsystem** (`NoteView.TrySlice`): dort zeigt der Pfeil immer
  nach **−Y**, gültig ist der XY-Winkel ≤ `GameConfig.CutAllowedAngle` (45° = Clone-Asset-Wert;
  OpenSaberPlus nutzt dot > 0,7 ≈ 45,6° — dasselbe). Unter `GameConfig.CutMinSpeed` (0,15) zählt
  die Berührung als „zu langsam" (kein Schnitt). Dot-Noten (8/9) sind aus jeder Richtung gültig.
  Die **Farbe (Hand)** wird zusätzlich geprüft — zusammen mit der Richtung ist sie **das**
  Korrektheits-Kriterium (Anlauf/Durchzug siehe Invariante 6). Die frühere Wurzelgeschwindigkeits-
  Regel (`DirectionAngleThreshold` 130°) und die Schwung-Umkehr-Erkennung (SuperSaber
  `StrokeReversalDot`) sind **entfernt**: für das Scoring gibt es keine Schwung-Objekte mehr,
  Anlauf/Durchzug sind reine Zeitfenster um den Treffer.
- **Pfeil-Rotationswerte (`NoteView.TargetRotationZ`):** Das Pfeil-Mesh zeigt bei 0°
  **nach unten** (aus dem FBX verifiziert). Unity dreht bei positiver Z-Rotation aus
  Spielersicht **gegen den Uhrzeigersinn** (Doku: +X→+Y CCW beim Blick entlang +Z), also
  0°=unten, 90°=rechts, 180°=oben, 270°=links. Daraus: 0→180, 1→0, 2→270, 3→90,
  4→225, 5→135, 6→315, 7→45. **Nicht** spiegeln (früher waren 2/3 und die Diagonalen
  horizontal vertauscht → Diagonalpfeile zeigten ~90° falsch).
- **Dot-Marker (pfeillose Noten):** Noten mit `_cutDirection` 8/9 tragen statt des Pfeils
  einen **weißen, gefüllten Kreis** mittig auf der Vorderfläche (`NoteView.BuildCircleMesh`
  + `BuildWhiteUnlitMaterial`, Kind in `NotePool.CreateNew`). **Durchmesser =
  `GameConfig.NoteSize / 4`** (ein Viertel der Box), Mesh-Radius 0,5 → `localScale =
  Durchmesser`. Die Note ist bei 8/9 ungedreht (Rotation 0°), der Kreis bleibt zentriert
  und blickt zum Spieler (−Z).
- **Noten-/Schnitt-Optik = 1:1 BeatSaberClone; fehlende Modelle im selben Stil:**
  Notenkörper = `…/Models/Box.fbx` (BeatSaberClone, Import auf **0,50 m**, `isReadable: 1`)
  mit den flachen `RedUnlit`/`BlueUnlit`-Graphen; Pfeil = `Arrow.fbx` + `RedEmission`/
  `BlueEmission`. Das **Zerschneiden** ist EzySlice (`NoteView.DoSlice`:
  `Slice` → `CreateUpperHull/LowerHull` → Rigidbody+MeshCollider → Auseinanderfliegen),
  Querschnittsmaterial = `RedEmission`/`BlueEmission` — exakt BeatSaberClone `BoxView.Sliced`.
  **WICHTIG:** Das Notenmesh MUSS `isReadable: 1` haben, sonst liefert `Slice` still `null`
  und die Schnitt-Optik verschwindet komplett (Ursache eines früheren Fehlers: ArcViewers
  `Cube.fbx` hat `isReadable: 0`). BeatSaberClone hat nur `Box`+`Arrow`; die übrigen Typen
  wurden ergänzt: Dot = weißer Kreis (`NoteView.BuildCircleMesh`), Chain-Kopf =
  `ChainHead.fbx`, Chain-Glied = `ChainLink.fbx` (0,50 × 0,09 × 0,50), Bogen = `ArcView`
  (LineRenderer). Chain-Kopf/-Glied nutzen dieselben `RedUnlit`/`BlueUnlit`-Materialien wie
  die Noten (Stil-Einheit). Die Bombe bleibt ArcViewers Stachel-Ball (`Bomb.obj` +
  `Bomb.mat`/`NoteShader`), da BeatSaberClone keine Bombe hat.
- **Minen-Optik = ArcViewer-Modell (Stachel-Ball):** Minen/Bomben (`_type` 3) tragen das
  Modell + Material des Referenz-Viewers **ArcViewer** (`…\ArcViewer-main`):
  `Assets/Resources/BeatSaberDemo/Models/Bomb.obj` (+ `.meta`),
  `…/Materials/Bomb.mat`, `…/Shaders/BombShader.shadergraph` (ArcViewers `NoteShader`)
  und `…/Textures/notecubemap.png` (Reflexions-Cubemap; GUIDs 1:1 übernommen).
  Das Mesh wird mit `Bomb.obj.meta` → `globalScale: 1` importiert (1:1 ArcViewer); die
  Welt-Bounds sind ≈ 0,63 × 0,60 × 0,53 m, MrSaber-Noten sind **0,50** (`Box.fbx`,
  `GameConfig.NoteSize`). Skalierung im
  Import statt `transform.localScale`, damit die Trefferbox
  (`GameManager.SegmentIntersectsNote` rechnet im **Noten-Lokalsystem**) und das Gameplay
  unverändert bleiben. Der Mesh-/Materialwechsel passiert je Spawn in `NoteView.Spawn`
  (Noten und Minen teilen sich denselben Pool); fehlen die Assets, greift automatisch die
  alte schwarze Würfel-Optik (`BlackMat`). **Nicht** übernommen: ArcViewers Outline-Shell
  (dort nur für die Replay-Fehler-Hervorhebung aktiv, standardmäßig aus).
- **Lichtschwert & Bögen = 1:1 ArcViewer (Reskin, Nutzer-Freigabe):** Klinge/Griff nutzen
  ArcViewers `Saber.fbx` (Blade + Handle; FBX-Knoten-Scale 100 → auf 1 gesetzt und auf
  MrSabers 0,9-m-Klinge gefittet) mit `SaberBlade.mat`/`SaberHandle.mat` (SaberBladeShader/
  SaberHandleShader, starke Emission → die GANZE Klinge leuchtet); Farbe über `_BaseColor`
  (Rot/Blau). Das Trail ist ein **prozedurales Mesh-Ribbon** (`SaberTrail`, 1:1 nach
  ArcViewer `SaberTrailMeshBuilder`): Band von Klingen-Basis zu Spitze über eine
  Pose-Historie, Material `SaberTrail.mat` + `DefaultTrail.png`. Es ersetzt den früheren
  Unity-`TrailRenderer` (nur Spitzen-Schweif). Einfahren (Easter Egg) skaliert weiter den
  Blade-Pivot; die Tip/Base-Score-Geometrie bleibt unverändert. Bögen nutzen
  `Arc.mat`/`ArcShader` + `Arc.png` mit Textur-Scroll (`_TextureOffset`,
  `GameConfig.ArcAnimationSpeed`). Die ArcViewer-ShaderGraphs sind mit 2022.3 kompatibel
  (MrSabers `BombShader`/`WallShader` sind byte-identisch mit ArcViewers `NoteShader`/
  `WallShader`).
- **Feinschliff-Runde (Nutzer-Feedback):** (1) Das Trail-Band rechnet die Sample-
  Weltpositionen jetzt via `transform.InverseTransformPoint` ins Trail-Objekt um (vorher
  lagen die Vertices in Weltkoordinaten unter dem skalierten Schwert → falsch platziert/
  verzerrt). (2) Wände: MrSabers `Wall.mat`/`WallEdges.mat` durch ArcViewers Originale
  ersetzt (`_Surface: 1`, `_SrcBlend: 5`, `_ZWrite: 0` → wirklich transparent);
  `GameConfig.WallOpacity` 0,30 → **0,20**. (3) Dot-Noten: Punkt-Durchmesser = **halbe**
  Notenkante (vorher Viertel). (4) Chain-Glieder tragen jetzt ebenfalls einen weißen Punkt
  (`ChainView`). (5) Noten **rotieren beim Anflug ein** (ArcViewer `rotationAnimationTime`
  0.3, `GameConfig.NoteRotationAnimationTime`) — eine zuvor fehlende Spawn-Variation.
  (6) Griff-Label weiter nach außen (`SaberView`-Anker z −0,030) und Kurvenradius an den
  ArcViewer-Griff angepasst (`GameManager.GripCurveRadius` 0,0225).
- **Noodle Extensions (per-Note-Custom-Data, Auszug):** `NoteModel._customData`
  (`NoteCustomData`) liest v3/v4 `customData` UND v2 `_customData` und wendet an:
  freie Position (`coordinates`/`_position`, ArcViewer `CalculateObjectPosition`: x +2,
  Einheit 0,6 m), freie Rotation (`worldRotation`/`localRotation` → überschreibt die
  Pfeilrichtung, kein Z-Einrotieren), `flip` (Spiegelung an X-Mitte) und `scale`
  (Trefferbox skaliert mit). Parsen über die DTOs in `BeatmapParser` (`V3Note.customData`),
  Anwenden in `GameManager.SpawnNote` (Position/Flip) und `NoteView.Spawn` (Rotation/Scale).
  **Noch NICHT umgesetzt** (großer Folgeschritt): das Noodle-ANIMATIONS-System
  (`_animation`/`_definitions`, Offset-/Definite-Position, Track-Parenting, Player-Track),
  per-Note-NJS/`noteJumpStartBeatOffset`, `disableNoteGravity`/`disableNoteLook`,
  Fake-/uninteractable-Noten. Referenz: `C:\Users\Riegholm\Downloads\NoodleExtensions-master`.
  **Hinweis:** Die Beispiel-Maps `374e3 (Radiant)` und `377fb (...Ready For It)` sind
  reine v3.3.0-Maps OHNE Noodle-Daten — dort ändert diese Erweiterung nichts.
- **Korrektur-Runde 2 (Nutzer-Feedback):** (1) Dot-Noten: Punkt-Durchmesser wieder auf
  ArcViewers Prefab-Wert **0,35 × Notenkante** (0,25 zu klein, 0,50 zu groß). (2) Das
  **globale Noten-Einrotieren ist ENTFERNT** — ArcViewers `doRotationAnimation` ist
  standardmäßig AUS; nur Noodle-Noten tragen eine freie Rotation. (3) Trail-Fade
  **umgedreht**: UV-u = 0 am NEUESTEN Sample (ArcViewer) — vorher war der alte Teil am
  hellsten („Schweif je weiter weg desto deutlicher"). (4) Wände: ArcViewers Wand-Shader
  ist **statisch** (keine Time-Nodes) — ergänzt wurde eine dezente, beat-synchrone
  Puls-Animation (Körper-Deckkraft + Kanten-Helligkeit, `WallView.PulseUpdate`).
  (5) Griff-Label-Anker exakt auf den Griff-Radius (z −0,025) und
  `GameManager.GripCurveRadius` = **0,01875** (tightere Rundung; vorher zu flach).
- **Korrektur-Runde 3 (Nutzer-Feedback):** (1) **Wand-Blinken entfernt** — die
  `PulseUpdate`-Animation ist wieder raus (sah „schrecklich" aus). (2) **Trail-Länge** auf
  ArcViewers `sabertraillength` = **0,1 s** gesetzt (vorher 0,35 → bei schnellen Schwüngen
  länger als die Klinge). (3) **Griff-Schrift FLACH** (kein Shader-Biegen): Der
  GripCurvedText-Shader bog jede Glyphen-Quad an ihren Ecken → jede Letter lag schief auf
  eigener Ebene. Flach am Anker z −0,025 liegt sie trotzdem knapp über der Rundung.
  (4) **Griff oben geschlossen** (`GripCap`-Scheibe an Root-Y 0.2), damit man im
  eingefahrenen Zustand nicht ins hohle Griffmodell sieht. (5) **Chain-Glieder**: jetzt
  **geschlossene, read/write-fähige Würfel** (ArcViewers `ChainLink.fbx` ist
  `isReadable: 0` → nicht schneidbar) und werden beim Treffer **per EzySlice zerschnitten**
  (`ChainView.SliceLink`, Hälften aus einer Kopie, BeatSaberClone-Optik).
- **Korrektur-Runde 4 (Nutzer-Feedback):** (1) **Klinge exakt auf das Score-Segment**
  gefittet (Root-Y 0,10..2,10 = Tip/Base) — vorher 0,20..2,00, dadurch ging der Trail
  vorne über die Klinge hinaus. Jetzt sind Klinge, Trail und Hitbox deckungsgleich.
  (2) **Griff-Schrift**: eigener, unterteilter `GripLabelMesh` (jede Glyphe in
  Höhenstreifen, um den Griff gewickelt → EINE glatte Rundung). Der alte Vertex-Shader bog
  nur die 4 Eckpunkte je Glyphe → schiefe Einzel-Ebenen. `GripLabelLayer` liest den Text
  des (unsichtbaren) TextMesh und baut bei Änderung neu. (3) **Griff-Kappe** als gewölbte
  Sphäre statt flacher Zylinder-Scheibe (wirkte als „Querbalken"). (4) **Chain-Glieder**
  wieder auf ArcViewers `ChainLink.fbx` (Look war besser); das Meta steht jetzt auf
  `isReadable: 1`, damit EzySlice sie zerschneiden kann (Slice-Animation bleibt).
- **MR-Toleranz-Funktionen (Nutzer-Freigabe, finaler Stand):** BEHALTEN/verstärkt:
  (1) **CCD** — `GameManager.PrepareSaberBox` vereinigt Vorframe-Pose UND
  `GameConfig.SaberSweepSamples` interpolierte Zwischen-Posen (Sweep-Test zwischen altem
  und neuem Frame gegen Durchglitschen). (2) **Trefferbox = Referenz-Kollisionsbox** —
  `GameConfig.NoteHitbox*` = 0,80 × 0,50 × 1,00 (Dot 0,80 hoch; Chain 0,20 hoch), um die
  **Notenmitte zentriert** (KEIN Vorversatz — die Score-Zone liegt exakt auf der Note) —
  identisch zu Beat Saber / BeatSaber-Web-Replays `beat.js toBigBox` und OpenSaberPlus
  `BeepCube.tscn` (CollisionBig) und zur v1.0-Referenz. Der Mitte-Score bleibt auf dem
  visuellen Mittelpunkt.
  (3) **Wand-Tiefpass** — `AudioManager.SetWallMuffle`
  (`AudioLowPassFilter` auf der Musik, `WallMuffleCutoffHz` = 400 Hz), sofort an/aus,
  solange `WallView.IsHeadInside` für den Kopf gilt. (4) **Wand-Brutzeln** —
  `UpdateContactHaptics` sendet Zufalls-Impulse 40..80 Hz
  (`WallSizzleHaptic*`) und `AudioManager.SetWallSizzle` spielt ein räumliches
  Hochpass-Geräusch an der Klingenspitze.
  ENTFERNT (bewusst, Nutzer-Freigabe): geschwindigkeitsabhängiges Hitbox-Vorziehen (NJS),
  Sub-Frame-Audio-Latenzausgleich, dynamische Klingen-Verdickung/-Verlängerung und
  Stereo-Fokus bei Kopfbewegungen. Die Klinge behält IMMER Form und Dicke; die Noten-Hitbox
  verschiebt sich NICHT dynamisch; der Treffersound wird NICHT vorgezogen; die Musik wird
  NICHT auf Mono geregelt.
- **Korrektur-Runde 5 (Nutzer-Feedback):** (1) **Chain-Schneiden repariert**: `SliceLink`
  baute die Hälften per `Object.Instantiate` eines zuvor **deaktivierten** Glieds — das
  ließ `Slice` ausfallen (Glied verschwand ohne Schnitt). Jetzt wird eine **frische Kopie**
  aus Mesh + Material erzeugt und geschnitten (Original bleibt in der Kette, nur
  ausgeblendet). (2) **Griff-Schrift zurück auf flach**: der experimentelle `GripLabelMesh`
  rendert fehlerhaft (falsche Dreiecke/UVs) → „farbige Balken"; die flache TextMesh-Variante
  ist wieder aktiv (`curved: false`, Root-Höhenstauchung zurück). `GripLabelMesh.cs` bleibt
  als ungenutzter Baustein liegen. (3) **Griff-Kappe**: flaches Unlit-Material
  (`SaberView.MakeFlatMaterial`) statt des SaberHandle-Shaders — dessen UV-Effekt erzeugte
  auf der Kugel farbige Querbalken.
- **Arcs: Klingen-„Kleben" + Score-Regel (Original-Nachbau):** `ArcView.DeformToSaber`
  zieht jeden Bogen-Punkt per kürzestem Abstand (Skalarprodukt) zur Saber-Klingenlinie
  (Griff→Spitze), je näher desto stärker (`GameConfig.ArcStickRadius`/`ArcStickStrength`) —
  mit **Farb-Validierung** (roter Bogen nur am linken, blauer nur am rechten Schwert, in
  `GameManager.SpawnArc`). Der Bogen gleitet so dynamisch an der Klinge (1:1
  SliderInteractionManager + Vertex-Deformation). **Gameplay unberührt:** Arcs haben keinen
  Collider → kein Miss, kein Combo-Bruch; die Kopf-/Endnoten sind normale colorNotes und werden
  wie jede andere Note gewertet (ein Vorschwung-/Nachschwung-Freibetrag `_isArcHead`/`_isArcTail`
  ist NICHT implementiert). Chains: Kopf max. 85 (kein Durchzug),
  Glieder fest 20 (×Multiplikator) — verpasste Glieder brechen die Combo NICHT (bereits so).
- **Arcs sind STATISCH im Raum, aber mit Saber-Interaktion:** Kein Per-Frame-Deform/Kleben
  an der Klinge (der frühere Deform war fehleranfällig/rechenintensiv und bleibt entfernt).
  Stattdessen Feedback wie im Original (`SliderInteractionManager`): Der `GameManager`
  prüft je Frame den Abstand der farblich zuständigen Saber-Klinge zur Bogen-Kurve
  (`ArcView.EvaluateInteraction`); bei Interaktion leuchtet das Band heller, es gibt
  Haptik + Spitzen-Partikel, beim Verlassen blendet es aus (`ApplyInteractionColor`).
  Die Arc-Optik (Bézier + Textur-Scroll) bleibt unverändert; **Punkte geben nur die
  Kopf-/Endnoten** (normale colorNotes, Standard-Wertung — die ArcViewer-Freibeträge
  `_isArcHead`/`_isArcTail` sind nicht implementiert).
  (Eine leichte, optionale Lenkung ließe sich später ergänzen.)
  Außerdem: Die Schnitt-Hälften (Noten/Chains) bekommen **keinen konvexen MeshCollider**
  mehr, sondern einen billigen **BoxCollider** (reine Optik; der Convex-Cook lief synchron
  auf dem Main-Thread und war die Ruckel-Quelle bei schnellen Schnitten).
- **Audio-Offset ENTFERNT (06.10, Nutzer-Freigabe):** Der frei einstellbare Audio-/Noten-
  Offset ist **sauber entfernt** — er ließ sich auf der PICO in keiner Variante zuverlässig
  umsetzen (Noten-Verschiebung, Start-Buffer, Audio-Seek, Live-Seek+Fallback). Es bleibt nur
  die feste Geräte-Latenz `GameConfig.AudioLatencyBaseMs` (190 ms) in `ScoreTime =
  AudioTime − Base`. Die Menüzeile, der `IdDelay`-Handler und die zugehörige Logik sind weg;
  `IdDelay (108)`/`IdVisual (114)` bleiben als reservierte IDs, `SaveData.SoundDelayMs`/
  `AudioBaseLatencyApplied`/`VisualOffsetMs` bleiben für alte Saves deklariert. Der letzte
  Code-Stand liegt in **`AudioOffset_entfernt_Sicherung.md`** (Projektwurzel).
  **Wichtig:** `GameManager._noteCursor` läuft weiterhin nur vorwärts — bereits
  zerschnittene/verpasste Noten tauchen nie wieder auf (kein Doppel-Score beim Pausieren).
  **Warnungen:** Die harmlosen DTO-/Template-Warnungen (CS0649/CS0169) sind in den
  betroffenen Dateien per `#pragma warning disable 0649, 0169` stummgeschaltet (kein
  `csc.rsp` — das hatte Compiler-Fehler verursacht); die eigenen Dateien sind ohnehin
  warnungsfrei.
- **Bad-Cut = sofort, kein Aushol-Schutz mehr (Stand 12.10):** Früher existierte
  `GameConfig.BadCutNearZ`: ein FALSCHER Kontakt (falsche Richtung) wurde weit vor der
  Treffebene IGNORIERT, damit der Aushol-Schwung die große Trefferbox nicht vorzeitig
  verbraucht. Nachteil: man konnte eine Note von der FALSCHEN SEITE anschneiden und sie
  danach trotzdem sauber treffen. Jetzt gilt wie in der Referenz (SuperSaber `wrongHit` /
  Beat Saber): ein Hieb in die falsche Richtung verbraucht die Note SOFORT als Bad Cut.
  Eine reine BERÜHRUNG ohne Schwung (< `CutMinSpeed`) ist weiterhin KEIN Schnitt (Note
  bleibt am Leben, läuft ggf. als MISS durch) — das ist kein „falsch angeschnitten".
  Die konstante `BadCutNearZ` ist entfernt. Der Floating-Score zeigt „MISS" nur bei echtem
  Fehler; ein GÜLTIGER 0-Punkte-Schnitt zeigt „0" (früher wurde jede 0 als „MISS" angezeigt,
  obwohl die Combo weiterlief).
- **ArcViewer-Angleichung 05.10 (4 visuelle Korrekturen, Prüfbericht):**
  (1) **Bogen-Enden-Fade = feste Distanz:** `ArcEndFadeMeters` 0,35 m / `ArcEndFadeStartMeters`
  0 (`ArcViewer ArcHandler.SetGradient` teilt STRECKEN durch die Kurvenlänge). Früher als
  Anteil interpretiert → lange Bögen blendeten über ~35 % aus. `ArcView` misst jetzt die
  echte Kurvenlänge und setzt den Gradient entsprechend (wiederverwendeter Gradient).
  (2) **Wände +0,1 m:** `GameConfig.WallContainerOffsetY` (ArcViewer-Container „Walls"
  y = 0,1) in `WallView.ApplyVerticalGeometry`/`CurrentCenterY` — Optik UND Kollision.
  (3) **Bogen-Anschluss-Versatz:** `ArcAttachOffset` (0,225 m) wird jetzt angewandt: In
  `GameManager.BuildSchedule` werden Bogen-Enden mit Note (gleiche Zeit + Rasterfeld,
  `NoteGridKey`) markiert; `ArcView` verschiebt Endpunkt + Kontrollpunkt in Bogenrichtung
  (ArcViewer ObjectManager `halfNoteOffset`). (4) **Ketten-Punkt:** `ChainView`-Dot auf
  Referenzwerte (z = −0,235, Ø 0,0375 statt 0,12).
  **Nachtrag 06.10:** (4) wurde ZURÜCKGENOMMEN — ArcViewers Dot sitzt im Loch des
  ringförmigen Glieds (Mesh 0,5 × 0,087 × 0,5) und ist nur aus der freien ArcViewer-Kamera
  sichtbar; in unserem Blick von VORN verdeckt ihn der vordere Ring-Rand. Der Dot sitzt
  wieder sichtbar bei z = −0,27 / Ø 0,12. **Arc-Bugfix:** `ArcView.Build` setzt die Position
  jetzt SOFORT (nicht erst im nächsten `Update`, das bei Order −100 schon vor dem Spawn in
  GameManager lief) — sonst blitzte ein aus dem Pool geholter Bogen einen Frame lang an
  seiner ALTEN Position auf („Bogen oben"). **Warnungen:** eigene Dateien sind jetzt
  warnungsfrei (`#pragma warning disable 0649` für die JSON-DTOs in `BeatmapParser.cs`;
  ungenutzte Pre-Roll-Felder + `_audioStartDelay` entfernt; SFX-Volumen-Guards als
  `if`-Block statt frühem `return` gegen CS0162). Die restlichen CS0649/CS0169 stammen aus
  `Assets/MRTemplateAssets` (Template-Sample, nicht unser Spiel) und sind harmlos.
- **Trefferbox = Referenz-Kollisionsbox + Klingen-SEGMENT-Test (Stand 12.10):** Die Note nutzt
  wieder die **exakte** Box aus Beat Saber / BeatSaber-Web-Replays (`toBigBox`) und
  OpenSaberPlus (`CollisionBig`): 0,80 × 0,50 × 1,00 (Dot 0,80 hoch; Chain 0,20 hoch), um die
  **Notenmitte zentriert** (`GameManager.NoteWorldBounds(note)`; KEIN Vorversatz mehr). Die Box
  ist bewusst GRÖSSER als das sichtbare 0,5er-Mesh — sie gibt die Treffer-Toleranz des
  Originals. Geprüft wird sie aber NICHT mehr per AABB-gegen-AABB, sondern mit dem
  Klingen-Segment (Basis→Spitze, inkl. Vorframe-Sweep): die Achsen-Box eines schnellen
  Schwungs nahm Noten mit, die die Klinge nie berührte (Treffer weit vor der Zone, „falsche
  Seite"). Chain-Glied nutzt denselben Segment-Test gegen `BoxWorldBounds` (Chain-Box); die
  sichtbare Note bleibt unverändert.
- **Performance-Runde 05.10 (nur sichere Optimierungen, KEIN sichtbarer Unterschied):**
  (1) **Schnitt-Trümmer**: konvexer `MeshCollider` → `BoxCollider` (`NoteView.AddPiecePhysics`,
  `ChainView.AddPiece`) — der Convex-Hull-Cook lief pro Hälfte synchron auf dem Main-Thread
  und war die Ruckel-Quelle bei schnellen Schnitten. (2) **Wand-Geometrie**
  (`WallView.ApplyVerticalGeometry`) nur noch bei geänderter „Feldhöhe" anwenden (vorher
  jeden Frame pro Wand Transform-Writes). (3) **Menü-Schnellpfad**
  (`HandMenu.TryUpdateValuesInPlace`): Bei gleicher Zeilenstruktur werden nur die Wert-Texte
  gesetzt, statt alle Zeilen zu zerstören/neu zu erzeugen — behebt den Ruckler bei
  „Skalierung ±" und Spielerpunkten; jede strukturelle Änderung fällt weiterhin auf den
  vollständigen Neuaufbau zurück. (4) `ControllerInput.GetDevice` nutzt einen
  wiederverwendeten Puffer (keine `List`-Allokation). Der **Saber-Trail baut sein Mesh
  bewusst weiter jeden Frame neu** (Mesh liegt im lokalen Raum des bewegten Schwerts — ein
  Dirty-Flag würde das Band verschieben); nicht „optimieren".
- **Warum die ArcViewer-NOTEN dunkel waren (wichtig):** ArcViewers `Note.mat` nutzt den
  Cubemap-`NoteShader` mit `_CubemapStrength` und hängt an **Umgebungs-/Reflection-Probes**
  (Skybox/Environment). MrSaber rendert in **Passthrough-MR ohne Umgebung** → die Reflexion
  ist schwarz, die Noten wirken dunkel. Es fehlte also nichts „beim Rüberkopieren"; der
  Shader braucht eine Umgebung, die es hier nicht gibt (ArcViewer hat dafür die Option
  `simplenotes`). Deshalb bleiben die Noten flach (`RedUnlit`/`BlueUnlit`, BeatSaberClone),
  während Klinge/Trail/Bögen **emissiv** sind und auch ohne Umgebung leuchten.
- **Sound + Partikel = Referenz BeatSaberClone; Wände = ArcViewer (hellblau):** Nach einem
  Zwischenstand mit dem Sounddesign/den Partikeln des *ArcViewers* wurde auf Wunsch wieder
  der Stand des **Referenzprojekts `Y:\Unity\BeatSaberClone-main - Kopie`** hergestellt:
  - **Sound:** `…/Resources/BeatSaberDemo/Slice.ogg` (GUID unverändert) als EINZIGER
    Treffer-Sound — er wird **gefiltert** eingesetzt (dumpf/leise beim Notentreffer,
    vorne abgeschnitten + schnell + dumpfer beim Schwert-Schalter; siehe
    „Soundeffekt-Lautstärken" oben). Kein Bad-Cut-/Bomben-Sound (so war die
    Referenz: nur der Schnitt-Sound). Clips statisch gecacht (`AudioManager`), nicht räumlich.
    Die früher probierten ArcViewer-Hitsounds liegen **außerhalb** des Projekts im Backup
    (`_Backup_2026-09-27_Minen-Modell\ArcViewer_Sounds\`) — nicht im Build.
  - **Partikel (wieder da, aus der Referenz):**
    - *Spawn-Partikel der Notenblöcke* — `Fx.PlayNoteSpawn` in `GameManager.SpawnNote`
      (Referenz: `BoxSpawner` → `ParticleSpawnL` für `_type 0`/Rot, `ParticleSpawnR` für
      Blau), Position = Noten-Erscheinungspunkt, **Minen bewusst ohne**.
    - *Feuer am Schwert bei JEDEM Treffer* — `Fx.PlayHit` in `GameManager.HandleSaberHit`
      (Referenz: `ObjectSlicer` → `ParticleRedFire`/`ParticleBlueFire` je Hand).
    - Beide Effekte sind **gecacht + gepoolt** (`View/Fx.cs`, kein `Resources.Load` und keine
      `Instantiate`-Allokation pro Aufruf; Pool wie die Referenz: Capacity 4 / Max 8),
      Rotation `GameConfig.ParticleRotation` = −90° X (Referenz `BoxSettings`).
  - **Wände:** Shader/Materialien bleiben die des ArcViewers (`WallShader`/`WallEdgeShader`,
    Körper + leuchtende Kanten), aber **hellblau wie das frühere Glas**:
    `GameConfig.WallColor` = (0,80 / 0,90 / 1,00), `WallOpacity` = 0,30 und
    `WallLightness` = 1,0 (ArcViewer-Default wäre 0,5 — der Faktor dunkelt sonst die Farbe).
    Farbe/Deckkraft/Helligkeit laufen über EINEN wiederverwendeten MaterialPropertyBlock,
    `_WallScale` je Wand hält die Kantenbreite konstant. Beide Materialien sind geteilt und
    texturfrei; der Körper ist 0,001 kleiner als die Kantenhülle (ArcViewer `WallHandler`),
    die Treffer-Geometrie rechnet weiter auf dem Körper. Fehlen die Assets, greift das
    frühere Glas-Material als Fallback.
  - **`SaberSide` wohnt jetzt in `SaberView.cs`** (lag früher in `Fx.cs`) — beim Entfernen
    von Dateien immer mit einem Symbol-Grep prüfen: `Fx.cs` enthielt neben der Klasse auch
    diese Enum (das verursachte einmal Compile-Fehler CS0246).

- **Runde 09.10 (dieser Stand, Baseline-Git-Commit `237f4978`):**
  (1) **Audio-Offset NEU** — Menü 108 wieder da, aber erstmals mit einem Mechanismus,
      der die MUSIK selbst hörbar verschiebt: DSP-Verzögerungskeller `AudioDelayFilter`
      (OnAudioFilterRead) auf dem Musik-Kind-Objekt `[Music]`; positiv = Musik später
      (Noten unberührt), negativ = Noten-Uhr-Kompensation (Audio kann nicht vorgezogen
      werden); ±300 ms in 5-ms-Schritten, live, klickfrei, OGG-/Streaming-sicher.
  (2) **Minen explodieren** bei Berührung (`Fx.PlayMineExplosion`, radialer Weißblitz)
      statt still zu verschwinden.
  (3) **Funken richtungstreu** — fliegen entlang der Schwunggeschwindigkeit (Streaks)
      statt des Kegel-Fontänen-Prefabs (ParticleRedFire/BlueFire ungenutzt).
  (4) **ArcViewer-Look** — `LightEmission` 6,5 (HDR-Grund), Bloom/Grading in ArcViewer-
      Werten, Bloomfog-Vollausbau (Capture Culling NUR Glow-Ebene 11, Shader lesen die
      Textur inkl. Fake-Reflexionen, NDC-Skybox-Quad als leuchtender Hintergrund),
      Editor = Gerät (Standalone-Quality 1 = Performant-Renderer mit Bloomfog).
  (5) **Perf** — `LightHandler` schreibt MaterialPropertyBlocks nur bei Farbänderung
      (vorher >180 Renderer-Writes/Frame im Modus „Animiert"); Capture-Kamera rendert
      nur die ~64 Glow-Quads statt der ganzen Szene. Passthrough bleibt UNBERÜHRT
      (Root/Capture/Post-FX dort aus — „ohne den Passthrough zu belasten").
- **Runde 09.10b (Nutzer-Feedback, 7 Punkte):**
  (1) **Hintergrund-Richtung**: Der **screen-locked Bloomfog-Skybox-Quad ist ENTFERNT**
      (ein NDC-Vollbild-Quad dreht nicht mit dem Raum = die gemeldete Richtungs-Diskrepanz;
      Hintergrund = richtungslose Kamera-Farbe, Tiefe/Glühen aus Geometrie + Shader-Nebel).
      **Achtung (Nachtrag):** Der zwischenzeitliche Versuch, die Umgebung FEST unter
      `_gameRoot` zu hängen (`AttachToRoom`), wurde per Gerätetest VERWORFEN — er ließ die
      Bühne mit der Raum-Skalierung mitskalieren und erzeugte einen Doppel-/Versatz-Effekt
      („zwei Stages"). Die Umgebung wird daher wieder separat per `SyncAnchor`
      (Position/Gier, OHNE Skalierung) geführt — das ist der stabile Weg.
  (2) **Warnungen**: eigene Lade-Diagnose (`LogHierarchyOrphans`) auf EINE zusammengefasste
      Warnung reduziert (vorher pro Renderer); LaserDataHandler-Caps sind jetzt `Debug.Log`.
      Startet nachweislich **warnungsfrei** (Batch-Compile). Verbleibende Warnungen auf dem
      Gerät stammen aus dem PICO-SDK/XRI-Template (Fremdcode) — Text zur Priorisierung nötig.
  (3) **Wand-Funken** (`Fx.PlayWallHit`): beim Klingenkontakt mit Wänden sprühen jetzt
      FONTÄNEN (bewusst das alte `ParticleRedFire/BlueFire`-Prefab, das beim Notenschnitt
      zu fontänenmäßig war), Position = echte Eintrittsstelle (`WallView.TryIntersectSegment`),
      pro Hand gedrosselt (`WallSparkInterval`).
  (4) **Funken dünner + Vorwärtsdrift**: `SparkSize` halbiert (0,006–0,010), Richtung =
      Schwung + `SparkForwardBias` (Raum-vorwärts) → fliegen „mehr nach vorne als gerade".
  (5) **Grifflabel auf dem Griff — Shader-Biegung (Runde 09.10f, geräteverifiziert):**
      Der frühere `GripLabelLayer`-Ansatz (TextMesh-Mesh im LateUpdate umbauen) war
      **grundsätzlich unmöglich**: Der adb-Gerätelog bewies, dass ein TextMesh-Objekt **kein
      `MeshFilter`** besitzt — das Mesh ist nicht auslesbar. Schlimmer: Das
      `[RequireComponent(typeof(MeshFilter))]` des `GripLabelLayer` ließ
      `AddComponent<GripLabelLayer>()` mit *„Can't add component 'MeshFilter' … conflicts
      with … TextMesh"* **null** zurückgeben → `layer.Radius = …` warf eine
      `NullReferenceException` → die ganze Label-Erzeugung brach ab („Grifflabel-Aufbau
      fehlgeschlagen") → **GAR KEINE Schrift** auf den Griffen.
      **Fix:** `GripLabelMesh.cs` entfernt; `MakeEngraveLayer` weist dem TextMesh-Renderer
      direkt ein Material mit dem bereits vorhandenen Shader **`BeatSaber/GripCurvedText`**
      zu. Der Shader wickelt die TextMesh-Vertices selbst um die Griffachse
      (`_Radius=GripCurveRadius`, `_HeightScale=1`, da kein Transform-Squash und die
      Vertices bereits in Metern sind). Fällt der Shader aus (null/nicht unterstützt),
      bleibt das normale Font-Material stehen — **flach, aber garantiert lesbar**.
      `UpdateLabels()` läuft weiterhin als LETZTES, gekapseltes Element in
      `GameManager.LateUpdate`. Compile 0 Fehler/0 Warnungen; Shader lädt + ist auf dem
      Zielgerät supported.
  (6) **Keine Minen-Kettenreaktion**: kurze Sperre nach einer Detonation
      (`GameConfig.MineChainGuardSeconds`) — weitere Minen in derselben Bewegung lösen
      nicht aus. **(Runde 10.11e wieder ENTFERNT: JEDE berührte Mine soll detonieren; die
      Sperre schluckte die Nachbar-Mine. Echte Kettenreaktion (durch die Explosion) gab es
      ohnehin nicht — die Explosion ist rein optisch.)**
  (7) **Farben + Default-Boden**: Umgebungsfarben werden jetzt ROBUST aus dem rohen
      info-JSON gelesen — Array UND Objekt-Form (`{r,g,b}`, ChroMapper, z. B. „Radiant"/
      374e3; die alte DTO-Lesung erkannte nur Arrays und übersah die Objekt-Form → falsche
      Farben). Modus **Default**: Bloomfog-Capture aus + Lightshow-Tint 0 → Bühnen-Boden
      matt-grau (leuchtet NICHT); Modus **Animiert**: Nebel + Fake-Reflexionen + Songfarben.

- **Runde 09.10g (Grifflabel-Recherche per Editor-Render, Nutzer-Feedback „Schrift im Griff
  und verschiedene Flächen"):** Statt weiter zu raten, wurde das Label in einer
  Batch-Mode-Szene über die **echten** `MakeEngraveLayer`/`CreateLabel`-Pfade gebaut, mit
  einer Kamera gerendert und als ASCII-Helligkeitsraster ausgewertet. Dabei kamen **drei
  echte Fehler** heraus:
  1. **Schrift ~7× zu groß.** TextMesh rechnet die sichtbare Höhe als
     `Höhe ≈ fontSize × characterSize × 0,1117` (LegacyRuntime.ttf). Mit `fontSize=64`
     und `characterSize=0,010` ergab das **7,15 cm** Zeilenhöhe — auf dem 3,8-cm-Griff
     wickelte sich die Schrift ~200° um den Griff und überlappte sich (die „verschiedenen
     Flächen"). **Fix:** `GripLabelCharSize = 0,002` → ≈ 1,43 cm Zeilenhöhe (≈ 1 cm Cap).
  2. **Nicht-homogene Welt-Skalierung.** Rotation (Z 90°) und Kehrwert der Saber-Skalierung
     lagen am **selben** Knoten; die Rotation tauscht die Achsen, also traf der Kehrwert die
     falschen → Welt (0,667 / 1,5 / 1,0): entlang des Griffs gestaucht, um den Griff
     gestreckt, Kurvenradius falsch. **Fix (`SaberView.Build`):** zuerst ein
     **achsenparalleler Rahmen** (`GripLabelFrame`, Rotation identity) mit dem Kehrwert,
     dann erst der Label-Anker darunter → Welt-Skalierung exakt 1, alle Längen = Meter.
  3. **Schrift zeigte nach innen (gespiegelt).** Der Anker dreht die Label-`+Z` in den Griff
     hinein; von außen liest man die Rückseite. **Fix (`CreateLabel`):** linke Seite
     `180°` um die Leseachse (X), rechte Seite `180°` um die Glyphen-Hoch-Achse (Y) —
     beide echte Drehungen, Fläche bleibt eben.
  **Verifikation (Editor-Render):** Der korrigierte flache Label stimmt **pixelgenau**
  (MSE 0,000000) mit einem garantiert korrekten Referenz-Billboard überein; der alte
  Zustand ist gespiegelt/außerhalb. Der gekrümmte Label rendert im ASCII klar als **„NAME"**
  auf dem Griff. Compile 0 Fehler/0 Warnungen. Farbe/Größe/Krümmung sind jetzt korrekt;
  verbleibt der **Gerätetest** (Build and Run).

- **Runde 09.10h (Grifflabel: CPU-gekrümmtes Mesh statt Vertex-Shader, geräteunabhängig):**
  Der 09.10f-Ansatz (TextMesh + Vertex-Shader `BeatSaber/GripCurvedText`) war auf dem Gerät
  zwar sichtbar, aber **grundsätzlich falsch**: Ein TextMesh liefert pro Glyphe nur EIN
  Viereck (4 Ecken). Biegt der Shader nur diese 4 Ecken, wird **jedes Zeichen eine schiefe,
  flache Einzel-Ebene** mit anderem Winkel — genau die gemeldeten „verschiedenen Flächen pro
  Buchstabe". Zusätzlich war die Ausrichtung gespiegelt.
  **Neuer Ansatz (`Assets/Scripts/BeatSaber/View/GripCurvedLabel.cs`):** Die Schrift wird als
  **CPU-Mesh** erzeugt. Jede Glyphe wird quer zur Leserichtung in `subdivisions` (8) Streifen
  unterteilt und die Leserichtung exakt auf den Zylinder um die Griffachse gewickelt
  (`arc = R·sin θ`, `z = R·(1 − cos θ)`, `R = GripCurveRadius = 0,01875 m`). Gerendert wird
  mit dem **eingebauten Font-Material** (GUI/Text Shader) über Vertex-Farben — **kein eigener
  Shader** mehr (`GripCurvedText.shader` gelöscht), also geräteunabhängig (Editor = PICO).
  **Lage:** Leserichtung +X um den Umfang, Glyphen-Hoch +Y entlang der Griffachse, Normale +Z
  zur Achse. Anker-Rotation jetzt **identity** (`SaberView.Build`), Root identity
  (`GameManager.CreateLabel`). Ergebnis: aufrecht, von außen **NICHT gespiegelt**.
  **Größe:** `GripLabelCapHeight = 0,008 m` (Versalhöhe); als Referenz dient die **enge
  `H`-Box** (nicht die Ober-/Unterlängen-Union, sonst zu klein). Lange Namen werden automatisch
  verkleinert, damit sie nur einen begrenzten Bogen (`GripLabelMaxArcDegrees = 150°`) legen.
  **Verifikation (Editor-Render, nach Y-Flip des RenderTexture-Readbacks kopfrichtig):** Das
  echte `GripCurvedLabel` rendert „B"/„F" **aufrecht und ungespiegelt, identisch zu einem
  flachen TextMesh**; „Riegholm" liegt als glatte, gewickelte Schrift am Griff. Die früheren
  09.10g-Layout-Rotationen (180° um X/Y) entfallen ersatzlos. Zweischichtige Gravur
  (Lichtkante + dunkle Gravur) bleibt; Compile **0 Fehler / 0 Warnungen**. Verbleibt:
  **Gerätetest** (Build and Run).
- **Runde 09.10i (Regression behoben: Hintergrund-Objekte im Passthrough):**
  `ddc6a940` hatte die Umgebung erneut als **Kind des Raum-Roots** (`AttachToRoom`)
  angehängt — derselbe Weg, der schon in Runde 09.10b im Gerätetest **verworfen** wurde
  (Doppel-/Versatz-Effekt „zwei Stages", Passthrough-Störung). Im Passthrough waren dadurch
  wieder **Hintergrund-Objekte sichtbar**. **Fix (= dokumentierter stabiler Weg):**
  `AttachToRoom` und das Flag `_attachedToRoom` **entfernt**; die Umgebung wird wieder
  separat per `SyncAnchor` auf **Position + Gier** (OHNE Raum-Skalierung) gestellt.
  **Der Richtungs-Fix bleibt erhalten:** Beim Laden wird die Szene mit
  `worldPositionStays = false` unter `_root` gehängt — dadurch folgt sie der Raum-Drehung
  (= Notenrichtung). Mit `true` würde die authored WELT-Drehung eingefroren und der
  Hintergrund drehte **nicht** mit (das war die frühere Richtungs-Diskrepanz). Compile
  **0 Fehler / 0 Warnungen**. Verbleibt: **Gerätetest** (Build and Run; Passthrough + Drehen
  + „Default/Animiert" prüfen).
- **Runde 09.10j (Nutzer-Feedback: Grifflabel flach auf dem Griff + Arena grau mit echten
  Reflexionen):** Referenz für den Soll-Look: die Original-Mod **Chroma** (Quest-Port,
  `Downloads/Chroma-main`) — sie färbt NICHT die ganze Bühne per Global-Tint ein, sondern
  setzt Farben/Materialien pro Objekt/Licht.
  (1) **Grifflabel:** Zurück zur **flachen** Schrift auf dem Griff. Die gekrümmte Variante
  wirkte wie eine schwebende Fläche über dem Griff. `GameManager.CreateLabel` setzt bei
  `onGrip` jetzt `root.localRotation = Euler(0,0, left ? 90 : 270)` und
  `root.localScale = (1, GripLabelHeightScale, 1)` mit `GripLabelHeightScale = 0,72`; die
  Ebenen werden `curved: false` gebaut, `GripLabelCapHeight = 0,010 m` (Versalhöhe wie am
  Anfang). Die Schrift läuft damit wieder die **Griff-Achse entlang** und liegt tangential
  bündig auf dem Zylinder. Der **skalenkorrekte Ankerrahmen** (`SaberView.GripLabelFrame`)
  bleibt — daher exakt dieselbe Größe/Position wie der alte TextMesh-Stand, aber **ohne**
  dessen nicht-homogene Verzerrung. Der CPU-Krümmungs-Code (`GripCurvedLabel`,
  `curveRadius > 0`) bleibt für spätere Tests erhalten.
  (2) **Arena-Look (`PlatformShader`/`DiffusePlatformShader`):** Der **globale**
  `_EnvironmentTint`-Zusatz (`col.rgb += _EnvironmentTint.rgb * _EnvironmentTint.a`) ist
  **entfernt** — er färbte JEDE Fläche mit derselben Ringfarbe ein („eine Lichtquelle färbt
  alles gleich") und ließ die Arena bei jedem Beat flächig die Farbe wechseln („blinkt").
  Farbe kommt jetzt nur noch über die (Fake-)Reflexion: der konstante
  Bloomfog-Reflexionsanteil sank von 0,35 auf **0,18** (Fresnel trägt 0,82) → die Arena
  bleibt aus der Nähe **grau**. Die echte **Reflection-Probe** (`unity_SpecCube0`) wird
  **schärfer** gesampelt (Mip 2 → **1**) und im Glanz angehoben
  (0,25+0,55·(1−fresnel) → **0,35+0,65·(1−fresnel)**) → sichtbare Neon-Reflexionen.
  `_LaserColor` (per-Objekt, lokal) bleibt. Compile **0 Fehler / 0 Warnungen**. Verbleibt:
  **Gerätetest** (Grifflabel-Größe/Lage; Arena grau + Reflexionen in „Default"/„Animiert").
- **Runde 09.10k (Nutzer-Klarstellung: Bühne skaliert MIT dem Raum):** Der Nutzer hat nie
  gesagt, dass der Hintergrund bei der Raum-Skalierung ausgenommen werden soll — im
  Gegenteil: „der Raum wird größer/kleiner mit allem, was darin ist"; Größe und Richtung
  sollen IMMER zusammenhängen. Die frühere Trennung (`SyncAnchor` nur Position + Gier) war
  eine Reaktion auf einen Gerätetest-Fehler, keine Nutzer-Anforderung.
  **Neu:** `EnvironmentDirector.SyncAnchor(position, rotation, roomScale)` spiegelt jetzt
  zusätzlich die **Raum-Skalierung** (lokal relativ zum Eltern-Maßstab gerechnet, damit die
  WELT-Größe exakt `roomScale` ist). Damit skalieren Bühne, Noten, Wände und HUD immer
  gemeinsam; die Gier bleibt identisch.
  Die **Hierarchie bleibt bewusst getrennt** (kein Eltern-Kind zum Raum-Root): Das frühere
  `AttachToRoom` ließ im Gerätetest Hintergrund-Objekte im Passthrough sichtbar werden
  (09.10b/09.10f). Da nur die Pose gespiegelt wird, bleiben Struktur und
  Passthrough-Abschaltung (`_root.SetActive`) unverändert. Compile **0 Fehler /
  0 Warnungen**. Verbleibt: **Gerätetest** (Raum-Skalierung im Menü ändern → Bühne
  wächst/schrumpft mit; Passthrough weiter sauber).
- **Runde 09.10l (Grifflabel: zurück zur Kopie-Geometrie — Nutzer-Feedback):** Der Vergleich
  mit dem als gut bezeichneten Stand `Y:\Unity\MrSaber - Kopie` (03.10.2026) ergab: Die Schrift
  unterschied sich **nur an zwei Stellen** — (1) `SaberView`-Label-Anker und (2) die
  Wurzel-Drehung in `GameManager.CreateLabel`. Der Anker ist jetzt wieder **EIN** Knoten mit
  Kehrwert-Skalierung **+ Z-Rotation 90°** an `localPosition (0,0,-0.025)`; `CreateLabel` dreht
  die Wurzel wieder `identity` (links) / `180°` (rechts). Der zwischenzeitliche
  `GripLabelFrame` (Skalenausgleich VOR der Rotation) hatte die Schrift auf ~2/3 verkleinert
  und quer zusammengestaucht → „ultra klein". Ergebnis: Größe/Position wie in der Kopie. Die
  flache (nicht gekrümmte) Darstellung entspricht 1:1 der Kopie. **Hinweis:** Batch-Compile
  nicht möglich, solange der Unity-Editor geöffnet ist (Library-Sperre); Editor kompiliert
  beim Fokussieren selbst.
- **Runde 09.10m (Grifflabel gekrümmt + korrekte Wickelachse — Nutzer-Entscheidung):** Auf Wunsch
  liegt die Schrift wieder **gekrümmt um den Griff**. Der Fehler der früheren „gekrümmt"-Variante
  war die **falsche Wickelachse**: sie legte die *Leserichtung* um den Umfang (wie ein
  Dosen-Etikett), weshalb die Schrift wie eine schwebende Fläche wirkte. Jetzt wird die
  **Schrifthöhe** um die Griffachse gewickelt (`GripCurvedLabel.Curved`: `phi = b/R`, Krümmung
  nach +Z), die Leserichtung bleibt gerade entlang des Griffs. Zusätzlich:
  - `SaberView`-Anker ist wieder **achsenparallel** (Rotation identity) mit dem Kehrwert der
    Schwert-Skalierung → unter dem Anker ist die Welt-Skalierung **isotrop 1**. Nur dann wird
    ein Kreis des Meshes in Welt auch ein Kreis (der 90°-Anker verzerrte ihn zum Ei).
  - `GameManager.CreateLabel`: Wurzel dreht 90°/270° (Leserichtung → Griffachse), quer
    komprimiert (`GripLabelWidthScale 0.72`), `curved: true`; Cap `0.011 m`, Wickelradius
    `GripCurveRadius 0.01875 m` (= Griffradius Ø 3,75 cm). Fallback ohne Schwert bleibt flach.
  - `GripLabelMesh.Build` unterteilt jetzt entlang der **Höhe** (Wickelrichtung) statt der
    Leserichtung; ungenutztes `rawW` entfernt (Warnung 0).
  - Der Wunsch „Environment behalten und schön machen" ist noch offen (nächster Schritt).
- **Runde 09.10n (Environment: „wie Original-Beat-Saber" — Nutzer-Entscheidung):** Der
  „Default"-Modus war auf ausdrücklichen früheren Wunsch matt-grau (kein Glühen, keine
  Reflexionen, dunkler Flachhintergrund) — das sieht NICHT wie das Original aus. Auf die
  Entscheidung „Soll wie Original-Beat-Saber" hin wurde der Sondermodus entfernt:
  - `EnvironmentDirector.ApplyMode`: Bloomfog (Nebel + Fake-Reflexionen + Neon-Glühen) und die
    Reflexions-Sonde sind jetzt in **Default UND Animiert** aktiv (`lit = mode != Passthrough`);
    beide Modi bekommen den vollen Original-Glow. „Default" = ruhige Blau/Rot-Standardpalette,
    „Animiert" = Song-Lightshow.
  - `EnvironmentLightManager.ApplyGlobalTint`: der „matt-grau"-Zweig ist entfernt — Hintergrund
    und Himmel sind in allen Modi aus den Licht-Farben gefärbt (Blau/Blau/Rot bzw. Grundpalette).
  - Passthrough bleibt unberührt (transparent, keine Umgebung, kein Post-FX).
  - Kommentare (Header + Modi + Hintergrund-Hinweis) aktualisiert.
  - Status: auf dem Gerät prüfen (Default UND Animiert); Feintuning (Helligkeit/Hintergrund)
    auf Wunsch.
- **Runde 09.10o (Geräte-Feedback: Label-Start-Bug + 2,5×-Größe + „dreht nicht alles mit"):**
  - **Label-Start-Bug (Ursache gefunden):** Beim ALLERERSTEN Build ist der dynamische
    LegacyRuntime-Font-Atlas noch nicht gerastert — `GetCharacterInfo` liefert leere Glyphen,
    das Mesh bleibt leer, und erst ein zweiter Build (z. B. nach „Spieler wechseln") zeigt die
    Schrift. Fix in `GripCurvedLabel`: `Font.textureRebuilt`-Hook (`OnEnable`/`OnDisable`
    subscriben) — sobald der Atlas fertig ist, wird RefreshAtlas + Rebuild nachgezogen
    (Standard-Muster für dynamische Fonts; Rekursions-Schutz `_onFontRebuild`).
  - **Größe 2,5×:** `GripLabelCapHeight` `0,011 → 0,0275 m`. Wickelt die Versalhöhe um Bogen
    `0,0275/0,01875 = 1,47 rad ≈ 84°` des Griff-Umfangs (Ø 3,75 cm) — deutlich lesbar, greift
    um die Rundung.
  - **„Es dreht sich nicht alles mit" — URSACHE GEFUNDEN (Nutzer-Regel: ALLES dreht mit bei
    Raum-Ausrichtung/Joystick, NICHTS bei Kopf-Bewegung):** Die Reflexions-Sonde wurde nur
    EINMAL pro Umgebungs-Laden gerendert und hing fest am Welt-Ursprung (aus 09.10o-Nachtrag:
    `TrackReflectionProbe` in `SyncAnchor` — Sonde folgt der Bühne, world-aligned, Neu-Render
    nur bei Pose-Änderung > 5 cm / 0,5°, Throttle 0,5 s). Der HAUPTVERURSACHER war der
    **blickgekoppelte Bloomfog**: Die Erfassungskamera hing am Auge und die Shader sampelten
    `_BloomfogTex` mit SCREEN-UVs — der Neon-Glanz auf dem Boden rutschte mit dem Blick und
    drehte nicht mit dem Raum. Fix (09.10o ergänzt): Erfassungskamera ist jetzt
    **RAUM-verankert** (`_roomAnchorPos/_roomAnchorRot` aus `SyncAnchor`, Höhe 2,0 m, Blick
    entlang Raum-Vorwärts); `BloomFog.cginc` projiziert jeden Weltpunkt mit
    `_BloomfogCaptureVP` (VP der raumfesten Erfassung) in das Capture-Sichtfeld
    (`GetFogCoord(worldPos)`, NDC->UV + `UNITY_UV_STARTS_AT_TOP`-Flip; `ConvertUV` ist bei
    Ratio ≈ 1 ein No-op). Beide `BloomFog.cginc`-Kopien (ArcViewerEnvironment + Environment)
    angepasst; `SampleBloomfog_float` folgt der neuen Signatur. Ergebnis: Raum-Drehung dreht
    Bühne + Noten + HUD + Sonde + Bloomfog MIT; Kopf-/Körperbewegung lässt alles stehen.
  - **Passthrough („Hintergrund-Elemente sichtbar"):** `ApplyMode` schaltet zusätzlich ALLE
    Renderer unter dem EnvironmentDirector ab (`SetEnvironmentRenderers(opaque)`), nicht nur
    `_root.SetActive` — damit bleiben auch zur Laufzeit außerhalb von `_root` erzeugte Objekte
    im Passthrough unsichtbar (Bügeleisen). `LogHierarchyOrphans` warnt weiterhin bei
    Renderern außerhalb des Raum-Ankers.
  - **STATUS:** Label + Größe + Raum-Verankerung committet; Gerätetest nötig (Strg+R im Editor
    wegen Auto-Refresh aus, dann Build+Run) für: Label ab Frame 1 korrekt & ~2,75 cm,
    Raum-Drehung dreht alles mit / Kopf-Regel, Passthrough ohne Hintergrund-Reste.
- **Runde 09.10p (Gerätetest-Rückmeldung: Schrift top, Passthrough zeigt weiter Hintergrund):**
  Der Nutzer meldete: **Grifflabel „so gut wie noch nie"** (Start-Fix + Größe greifen) — aber im
  **Passthrough** seien weiter „Hintergrund-Elemente" sichtbar („die gleichen Fehler, die schonmal
  behoben wurden"). Die komplette Abschalt-Kette wurde erneut geprüft und ist mechanisch korrekt:
  `SetEnvironment(String, ...)` kehrt im Passthrough vor dem Laden zurück (`Mode == Passthrough`),
  `ApplyMode(Passthrough)` setzt `_root.SetActive(false)` + Renderer-Sweep + Bloomfog/Probe aus,
  der **Lade-Abschluss** setzt `_root.SetActive(Mode != Passthrough)` (Linie 547 — auch ein spät
  beendeter Ladevorgang bleibt im Passthrough unsichtbar), `Tick` kehrt im Passthrough vor
  `_camera.backgroundColor` zurück (Zeile 278), und `SetCameraClear`/`PicoSeeThrough` setzen die
  Kamera transparent (SolidColor Alpha 0, `allowHDR` aus). `DefaultEnvironment.unity` enthält
  **keine eigene Kamera/Canvas** (reine Geometrie) — die Bühne ist also nur sichtbar, wenn
  `_root` AKTIV ist, der Umgebungs-Modus also NICHT Passthrough ist, WÄHREND die Kamera
  transparent ist.
  **URSACHE (Divergenz):** `PicoSeeThrough.Enabled` ist ein **eigener** Schalter (Default
  `true`), dessen `InitRoutine` Seethrough in einer Retry-Schleife einschaltet, **ohne `Enabled`
  erneut zu prüfen**. Der GameManager wendet den gespeicherten Hintergrund-Modus erst nach dem
  Save-Laden an (`Default/Animiert → SetEnabled(false)`) — läuft das während der Retry-Schleife,
  schaltet diese Seethrough trotzdem wieder AN ⇒ Kamera transparent, während die Umgebung
  (Modus Default/Animiert) sichtbar bleibt = **„Bühne/Ringe im Passthrough"**. Zusätzlich war
  der Renderer-Sweep in `ApplyMode` ein _einmaliger_ Lauf (ein spät beendeter Ladevorgang oder
  ein Re-Enabling blieb unentdeckt).
  **Fix 1 (Gürtel-und-Hosenträger, jeden Frame):** Neues **Passthrough-Sicherheitsnetz** im
  `EnvironmentDirector.Update`: Sobald der echte Raum durchkommt (`Mode == Passthrough` ODER
  `PicoSeeThrough.Enabled`), wird JEDE Frame erzwungen — `_root` inaktiv,
  `SetEnvironmentRenderers(false)` (ALLE Renderer unter dem Director, auch außerhalb `_root`),
  `SetBloomfogActive(false)`, Reflexions-Sonde aus + `_ProbeReflStrength = 0`, und die
  Hauptkamera transparent (`EnforceTransparentCamera`). Damit ist „Kamera transparent +
  Umgebung sichtbar" strukturell unmöglich, gleich wer den Zustand setzt.
  **Fix 2 (Race in der Seethrough-Kette):** `PicoSeeThroughBehaviour.InitRoutine` prüft
  `PicoSeeThrough.Enabled` jetzt in JEDEM Schleifendurchlauf und bricht ab, sobald der Schalter
  zwischenzeitlich auf „aus" steht — die Retry-Schleife kann Seethrough nach
  `SetEnabled(false)` nicht mehr wieder anschalten.
  **Batch-Compile nicht möglich (Editor offen) — Editor kompiliert beim Refresh (Strg+R).**
  Verbleibt: **Gerätetest** (Passthrough ohne Hintergrund-Reste; Default/Animiert mit voller
  Umgebung).

- **Runde 09.10q (Gerätetest-Rückmeldung 4 Punkte — AUSWERTUNG + Fixes):**
  Der Nutzer meldete: (1) Beim **zweiten** Start habe sich der **Hintergrund verdoppelt** und
  bleibe **sogar im Passthrough an**; (2) es wirke, als seien „nur 95 % des Hintergrunds"
  raumskaliert; (3) die **Wand-Funken** würden „mehrfach ausgelöst" statt an der Berührungs-
  stelle anzuhalten; (4) bei manchen Stages stehe „ein langer Querbalken am Anfang der Bühne,
  der falsch aussieht".
  **Analyse (1+2 = EINE Ursache):** Der „verdoppelte, nicht skalierende, im Passthrough
  bleibende" Hintergrund ist die Signatur einer **verwaisten, additiv geladenen Umgebungs-
  szene**: `SetEnvironment` brach einen laufenden Ladevorgang per `StopCoroutine` ab, obwohl
  die Szene im SceneManager **weiterlädt**. Die fertige Szene blieb dann am **Szenen-Root**
  hängen — ihre Objekte hingen weder unter `_root` (kein Raum-Anker, keine Skalierung →
  „nur 95 %") noch unter dem Director (Passthrough-Sweep erreicht sie nicht → „bleibt im
  Passthrough an"). Trigger: Umgebungswechsel/Liedwechsel, während ein Ladevorgang läuft
  (z. B. beim zweiten Start, wenn der Song aus dem Save vorgeladen wird). Punkt 4 ist sehr
  wahrscheinlich dasselbe Phänomen: die lange Front-/Seitenschiene (zb. `FrontBar` in
  NiceEnvironment, `LeftBar/RightBar/TrackSideBars` in BigMirror, Material guid
  `3ae45c05…` in fast allen Stages) steht doppelt und leicht versetzt → „falsch aussehender
  langer Querbalken".
  **Fix 1 (Doppel-Ladung unmöglich machen):** `SetEnvironment` stört einen laufenden Lade-
  vorgang **nie mehr per StopCoroutine** ab (er merkt nur den Wunsch). `SwitchEnvironment-
  Routine` prüft beim Abschluss, ob `_desiredEnvironment` inzwischen abweicht; dann wird die
  eigene Szene **sofort entladen** statt an den Root gehängt, und `TryStartPendingLoad()`
  zieht den aktuell gewünschten Namen nach. Zusätzlich entlädt `DestroyLoadedObjects` beim
  nächsten Ladevorgang **alle noch geladenen Umgebungsszenen** aus
  `EnvironmentParametersTable.Supported` (Sicherheitsnetz gegen Alt-Reste; korrekt unter
  `_root` gehängte Umgebungen haben ihre Szene bereits entladen und bleiben unberührt).
  **Fix 2 (Funken „anhalten, wo die Berührung ist"):** Statt gedrosselter Einzel-Bursts
  (`EmitWallSpark` entfernt) gibt es jetzt **EINE anhaltende Fontäne pro Kanal**
  (`Fx.SetWallContact` für links/rechts an der Wand-Eintrittsstelle, `Fx.SetClashContact`
  für Schwert↔Schwert): Loop-Prefab-Instanz mit Daueremission, World-Space, folgt im Update
  der aktuellen Kontaktstelle; beim Loslassen nur `StopEmitting` (Reste fallen aus).
  `SegmentDistance` liefert dafür zusätzlich den **Berührungspunkt** (Mittelpunkt der
  nächstgelegenen Streckenpunkte). `Fx.StopAllContacts()` beim Menü-Eintritt. Die bisherige
  Schwert-Clash-Haptik bleibt.
  **Querbalken (Punkt 4):** Wird mit Fix 1 geprüft (Doppel-Kopie verschwindet). Sollte die
  Schiene danach weiter „falsch" aussehen, braucht der Assistent Stage + Erscheinungsbild
  vom Gerätetest.
  **Batch-Compile nicht möglich (Editor offen) — Editor kompiliert beim Refresh (Strg+R).**
  Verbleibt: **Gerätetest** (keine Doppelung/kein Passthrough-Rest; Funken anhaltend an der
  Kontaktstelle; Querbalken-Check).

- **Runde 09.10s (Gerätetest-Rückmeldung 09.10r — 3 Punkte AUSWERTUNG + Fixes):**
  Der Nutzer meldete zum neuen Kontakt-Funken-Build (09.10r, `fb606fb3`): (1) „Die Schwerter
  machen schon Funken, **bevor** sie sich berühren" + Frage nach eigener Funken-Hitbox;
  (2) Funken „fliegen nur nach oben", sollen **erst in alle Richtungen** sprühen und dann
  durch die Schwerkraft fallen — und sie seien **zu dick**; (3) Minen hätten jetzt „diese
  Funken (auch zu dick)", **eine ältere Version hatte eher einen FEUERBALL** (besser).
  **Fix 1 (Clash zu früh):** Es gibt **keine separate Funken-Hitbox** — Haptik UND Funken
  löst dieselbe Klingen-Mittenabstands-Schwelle aus (`HapticSaberTouchDistance`). Die Klinge
  ist optisch nur ~3 cm dünn, die Schwelle lag bei 12 cm → Funken ~6–9 cm vor der sichtbaren
  Berührung. **0,12 → 0,06** gesetzt (Kontakt ab sichtbarer Berührung).
  **Fix 2 (Richtung/Dicke):** `CreateContactFountain` emittiert jetzt aus einer **Kugel**
  (Sphere statt Kegel nach oben, keine -90°-Drehung) mit **Gravitation 1,5** (sprüht radial,
  fällt dann herunter = Feuerwerk-Optik); Partikel auf **0,008–0,014 m** geschrumpft und
  Streckung auf **1,3/0,08** reduziert (dünne, kurze Streaks). Werte zentral in GameConfig
  (`ContactSpark*`, `ContactSparkGravity`).
  **Fix 3 (Minen-Feuerball):** `CreateMineTemplate` neu: weiche **Glow-Billboards** — neuer
  Shader `BeatSaber/ParticleGlow` (additiv, `_MainTex`-Textur) + prozedurale
  **Radialglüh-Textur** (64×64, `Fx.GlowTexture`, kein Asset nötig). Kugel-Form (Radius 0,16),
  langsames radiales Aufblähen (Speed 0,5–1,1), Wachstum `GrowCurve` (0,45→1,5→0,8), warme
  Farbrampe `FireGradient` (weißer Kern → Orange-Rand → ausglimmend), leichtes Absinken
  (MineGravity 0,15). `_Color` bleibt HDR-MPB wie gehabt.
  **Offen — Stages „nicht clean" (Geometrie):** Daten-Pass: alle 13 Stages nutzen konsistent
  nur Layer 9/10; die Szenen sind dünne Prefab-Instanzen (19–46 Objekte), die Geometrie liegt
  in `Assets/ArcViewerEnvironment/Prefabs/`. Die ArcViewer-„Vorlage" (Previewer-Szene) ist
  **nicht im Repo** — nur die konvertierten Szenen. Offene Frage an den Nutzer: **welche
  Stages** + **was genau** (Doppelbalken, Geometrie schneidet Spielbereich, fehlende Teile,
  Materialien) — dann gezielt in die betroffenen Prefabs.
  Verbleibt: **Gerätetest** (Clash-Funken erst bei Berührung + alle Richtungen + dünner;
  Minen-Feuerball) + Stage-Feedback.

- **Runde 09.10t (Gerätetest-Rückmeldung 09.10s — Nachschärfung):** „Die Hitbox der
  Schwerter ist immer noch größer als das Visuelle" + „die Funken sind auch noch zu dick".
  **Clash-Schwelle exakt = Klingen-Durchmesser:** `HapticSaberTouchDistance =
  SaberBladeRadius × 2` (0,034 Basis-Einheiten statt 0,06) — Funken + Haptik ab wirklichem
  Flächenkontakt beider Klingen (keine separate Funken-Hitbox, gleiche Schwelle wie gehabt).
  **Kontakt-Funken dünner/kürzer:** Partikelgröße 0,005–0,009 (Niveau der Schnitt-Funken,
  die der Nutzer nie als dick bemängelte), Streckung 1,0/0,05 (statt 1,3/0,08), Rate
  220→180 Partikel/s (weniger Überlagerung in einen Glühklumpen). Verbleibt: Gerätetest.

- **Runde 09.10u (Gerätetest-Rückmeldung 09.10t — Hitbox = echte Klinge):** „Die Hitbox
  steht noch leicht über das Schwert hinaus und ragt noch etwas in den Griff." Die Ursache:
  Die festen Clash-Segmente (Root-Y 0,1..2,1) und die pauschale Schwelle basierten auf
  **angenommenen** Mesh-Maßen — weicht das Blade-Mesh ab, ist die Trefferzone automatisch
  länger als die sichtbare Klinge. **Lösung: Clash-/Wand-Hitbox wird jetzt aus den ECHTEN
  Blade-Mesh-Bounds gemessen** (`SaberView.BladeClashRadiusBase` +
  `BladeClashBaseLocal`/`BladeClashTipLocal`): Root-Y = meshZ × 2 − 0,06 (Klinge entlang
  Mesh-Z, exakt dieselbe Formel wie der Mesh-Fit), Radius = Mittelwert aus Breite+Dicke
  × Node-Skala 1,125 (= äquivalenter Glow-Zylinder, kleiner als die alte pauschale
  `SaberBladeRadius`-Schwelle, die max. auch noch das Basis→Welt-Maß verwechselte).
  **Gemessen → kein Überstand über Spitze, kein Rest im Griff; Radius erst bei sichtbarer
  Berührung.** Dazu: Abstandsvergleich läuft im lokalen System des Links-Schwerts (beide
  Schwerter teilen dieselbe Raumskalierung) — Schwelle = Summe der Klingen-HALBbreiten,
  ohne das fehleranfällige `× _roomScale` auf ein pauschales Basis-Maß. `HapticSaberTouch
  Distance` (nun unbenutzt) entfernt; `BaseTrans`/`Tip` bleiben für Trail/Schnitt/Extend.
  Verbleibt: Gerätetest (Schwert↔Schwert an Spitze/Mitte: Funken erst bei Kontakt, kein
  Auslaufen in den Griff; Wand-Kontakt unverändert korrekt).

- **Runde 09.10v (Handlage — Rückfrage beantwortet):** „Damit sie besser in der Hand
  liegen, müssen die Schwerter auf ihrer aktuellen Achse 3 cm nach unten." Rückfrage
  geklärt: gemeint ist die Verschiebung **ENTLANG der Klingenachse** Richtung Griff/Hand
  (nicht vertikal). Gesetzt: `SaberBackShift 0 → 0,03` in `SaberView.Build` (lokale -Z =
  Klingenachse Richtung Griff; die Klinge ist 25° hochgeneigt, dadurch kommt die Spitze
  ~2,7 cm näher an den Spieler und ~1,3 cm tiefer). Optik UND Trefferfläche wandern
  gemeinsam (Tip/BaseTrans sind Kinder der Wurzel). `SaberDownShift`/`SaberRightSideShift`
  bleiben 0. Kommentare in Kopf + Build-Block aktualisiert. Verbleibt: Gerätetest
  (Handlage/Reichweite beim Schneiden: Noten nicht mehr „zu weit weg"?).
- **Runde 09.10w (Issue 1 — Hintergrund skaliert WIRKLICH mit, Ein-Root-Umbau):** Vom
  Nutzer verlangt: Raum-Skalierung rein optisch, „wirklich alles" wächst/schrumpft — auch
  Effekte. **Umbau auf EINE Skalierungs-Quelle** (`EnvironmentDirector.AttachToRoom`):
  Die komplette Bühne (Geometrie, Bloomfog-Erfassung, Reflexions-Sonde) hängt als **Kind
  an `_gameRoot`**; das pro-Frame Pose-/Skalierungs-Spiegeln (`SyncAnchor`) ist damit
  entfernt. Erfassungskamera unter `_root` (Höhe 2,0 lokal → 2,0 × roomScale) und Sonde
  unter `_root` (size/center lokal → Box skaliert mit) erben Position/Drehung/Skalierung
  automatisch. **Nebel skaliert mit:** neue Shader-Globals `_RoomScale`/`_RoomAnchorPos`
  im aktiven `BloomFog.cginc` — Höhen-Nebel-Band und Entfernungs-Nebel werden in
  RAUM-lokaler Metrik ausgewertet (bei Skalierung 1 / Raum-Y 0 exakt ArcViewer, sonst
  wächst/schrumpft der Nebel mit der Bühne). Neue `EnvironmentalDirector.LateUpdate`
  (`[DefaultExecutionOrder(1)]`, NACH GameManager → gleicher Frame wie die Geometrie)
  liest die Raum-Pose direkt vom Raum-Root: setzt die Nebel-Globals nur bei Änderung,
  aktualisiert die Welt→Capture-VP und lässt die Sonde bei Pose- ODER Skalierungs-Änderung
  neu rendern (Skalierungs-Trigger war vorher ein Latent-Bug: Sonde rendert erst bei
  Kontrast-Änderung nicht neu). **Warum Eltern-Kind jetzt sicher** (09.10i hatte es
  verworfen): Die Ursache damals waren verwaiste Szenen-/Hintergrund-Objekte außerhalb des
  Deaktivierungs-Pfads — die Passthrough-Sicherheitsnetze aus 09.10o/p/09.10q
  (`_root.SetActive` + Renderer-Sweep `SetEnvironmentRenderers` + Erfassung/Sonde aus)
  wirken unabhängig vom Elternbaum und greifen weiterhin. **Cleanup:** ungenutzter
  Duplikat-Ordner `Assets/Shaders/Environment/*` entfernt (die 13 Umgebungs-Szenen
  referenzieren GUID-treu `Assets/ArcViewerEnvironment/Shaders`; `Shader.Find` nutzt
  nirgends `Custom/…`). Verbleibt: **Gerätetest** (Build and Run; „Skalierung" live ändern
  → ALLES wächst/schrumpft: Bühne, Nebel, Reflexionen; Passthrough bleibt leer/transparent).

- **Runde 10.11a (Rückmeldung nach Version eins — zwei Effekt-Punkte):** Der Nutzer meldete
  zum Stand `43d298b`: (1) beim Live-Skalieren „verändern manche Flächen im Hintergrund
  leicht ihre Farbe"; (2) „die Minen haben aktuell einen Partikeleffekt, der so eigentlich
  nicht gewollt war" — er wünscht den früheren guten Feuerball.
  **Fix 1 (Farbwechsel beim Skalieren):** Zwei Ursachen, beide beseitigt.
  (a) `cameraDistance / _TextureDistance` in `PlatformShader`/`DiffusePlatformShader` war die
  ECHTE Welt-Entfernung — der Textur→Reflexions-Übergang wuchs NICHT mit der Bühne, deshalb
  kippten ferne Flächen bei großem Raum früher zur reinen (Fake-)Reflexion und färbten sich um.
  Jetzt raum-lokal (`cameraDistance / max(_RoomScale, 0.001)`) wie der Nebel: bei Skalierung 1
  exakt wie vorher, sonst dieselbe RELATIVE Optik (nichts verfärbt sich).
  (b) Die Reflexions-Sonde führte bei Pose-/Skalierungs-Änderung erst nach 0,5 s Throttle neu
  nach → während des Ziehens zeigte der Boden eine veraltete Reflexion. Throttle **0,5 → 0,15 s**
  (`EnvironmentDirector.TrackReflectionProbe`); im Stand weiterhin 0 Renders (nur bei Änderung).
  **Fix 2 (Minen-Feuerball):** Der „Feuerball" von 09.10s war noch als 22 WINZIGE Billboards
  (Startgröße 5–8,5 cm, Wurf 0,5–1,1 m/s) aufgebaut → auf dem Gerät weiter als Funken-
  Partikelstoß lesbar. Jetzt EIN kohärenter Ball: `MineBurstCount` 22 → **6**, `MineSize`
  0,05–0,085 → **0,22–0,32 m**, `MineSpeed` 0,5–1,1 → **0,1–0,35**, `MineGravity` 0,15 → **0,05**,
  `MineLifetime` 0,4–0,65 → **0,45–0,7**; Startkugel-Radius 0,16 → **0,12** (`Fx.CreateMineTemplate`).
  Die wenigen großen additiven Glow-Billboards (`BeatSaber/ParticleGlow` + prozedurale
  Radianglüh-Textur, KEIN Asset nötig) überlagern sich in der Mitte zum hellen Feuerball-Kern
  und blähen über `GrowCurve` auf ~150 % auf.
  Verbleibt: **Gerätetest** (Skalierung live ändern → kein Farbkippen der Flächen; Mine
  detonieren → ein Feuerball statt Partikelwolke).

- **Runde 10.11b (Minen-Feuerball wirkt „billig" — geschichteter, warmer Feuerball):**
  Nutzer-Rückmeldung: „jetzt sind es einfach nur ein paar Partikel … es war mal
  harmonischer, wärmer, jetzt sieht es billig aus."
  **Eigentliche Ursache gefunden:** Der `BeatSaber/ParticleGlow`-Shader multiplizierte die
  Partikelfarbe NIE in die Ausgabe (`i.color.rgb` kam nicht vor) — die bereits vorhandene
  warme `FireGradient`-Rampe war damit unsichtbar, und der Ball war eine flache, nur über
  `_Color` (MPB) eingefärbte Kugel → genau der „billige" Eindruck. **Fix im Shader:**
  `c.rgb *= t.rgb * i.color.rgb` (die Farbtemperatur-Rampe wirkt jetzt).
  **Zusätzlich neu aufgebaut** (Literaturrecherche: Unity-Handbuch „A Simple Explosion" +
  Schwarzkörper-Farbtip; Rezept = heller Kern, kurze „Pop-dann-hängen"-Bewegung, Farbverlauf
  hell→dunkel, Größen-Rampe): `Fx.CreateMineTemplate` ist jetzt ein MEHRSCHICHTIGER Ball aus
  drei additiven Glow-Schichten (gemeinsames Material/Textur + EIN HDR-Tint):
  * **Core** (2 Partikel): sehr kurzer, weißglühender Blitz (Alphafade 0,12–0,2 s).
  * **Fire** (9): Feuerkörper mit Schwarzkörper-Rampe heiß-weiß → Gold → Orange → dunkles
    Rot, Aufblähen auf ~145 % (`GrowCurve`) und **Limit Velocity** (limit 1,5 / dampen 0,5)
    → „Pop, dann hängen" statt Drift.
  * **Halo** (5): großer, weicher, tieforanger Saum (langsames Expandieren auf 180 %) —
    gibt Wärme und Harmonie nach außen.
  Dazu **Noise** (organisches Flackern statt glatter Kugel) und eine **prozedurale
  128×128-Glüh-Textur** mit hellerem Kern und leicht unregelmäßigem Rand (kein perfekter
  Kreis). Neuer, seltener Spiel-Pfad `Fx.PlayLayered` startet ALLE Kind-Systeme und setzt
  den Tint auf alle Renderer — der heiße Funken-Pfad `PlayBuilt` bleibt unverändert
  (ein System, keine Allokation). Die verstreuten Minen-Konstanten (`MineBurstCount`,
  `MineSpeed*`, `MineLifetime*`, `MineSize*`, `MineGravity`) wurden aus `GameConfig`
  entfernt; die Schicht-Parameter + Kurven liegen jetzt zentral in `Fx.CreateMineTemplate`.
  Verbleibt: **Gerätetest** (Mine detonieren → warmer, geschichteter Feuerball statt
  Partikelwolke).

- **Runde 10.11c (Minen-Detonation: keine Kettenreaktion, kleiner, kühler/cleaner):**
  Nutzer-Rückmeldung: „Minenexplosion: Darf keine Kettenreaktion auslösen und nicht ganz
  so groß sein. Sie soll auch nicht so warm wirken, sondern eher cleaner — ein einfacher,
  cooler Look, der in die Atmosphäre des sterilisierten Sci-Fi-Games passt."
  **Fix 1 (Kettenreaktion):** Der Ketten-Schutz `MineChainGuardSeconds` war mit 0,12 s zu
  kurz — ein Schwung durch ein Minen-Cluster detonierte mehrere Minen nacheinander (jede
  > 0,12 s nach der vorigen). Jetzt **0,5 s**: deckt eine komplette Schwungbewegung ab,
  es detoniert immer nur die ZUERST berührte Mine, die übrigen fliegen harmlos durch.
  **Fix 2 (kleiner):** Alle Schichtgrößen um ~30–40 % reduziert — Core 0,35–0,5 → **0,25–0,35**;
  Shell 0,22–0,4 → **0,14–0,24** (GrowCurve 145 % → 135 %); Halo 0,55–0,9 → **0,34–0,52**
  (HaloCurve 180 % → 155 %). Auch Lebensdauern/Speeds leicht gestrafft.
  **Fix 3 (kühl/clean statt warm):** Farbpalette von Schwarzkörper (Gold/Orange/Rot) auf
  einen kühlen, sterilen Sci-Fi-Look umgestellt: **Core** reinweiß, **Shell** (vormals
  „Fire") weiß → Eisblau → reines Blau, **Halo** weiches Eisblau → tiefes Blau. `MineFlashColor`
  (HDR-Tint) jetzt eisweiß-blau `(0,85, 0,92, 1)×2,6`. Noise stark reduziert (Shell 0,18 → 0,08,
  Halo 0,1 → 0) — ein klarer, unaufdringlicher Impuls statt organischer Feuerwolke.
  Verbleibt: **Gerätetest** (Mine detonieren → kleiner, kühler Energieblitz; durch ein
  Cluster schwingen → NUR EINE Detonation).

- **Runde 10.11d (Minen-Detonation: EINE warme Explosion aus der Minenmitte, simpel):**
  Nutzer-Rückmeldung: „Es sollte schon gelb, rot, orange und weiß sein und nicht aus
  mehreren Quellen bestehen, sondern immer nur aus einer Explosion, die von der Mitte der
  Mine ausgeht. Also die Mine verschwindet und es folgt eine schöne, keine schnelle
  Explosion. Es darf simpel sein."
  **Fix:** Der geschichtete 3-Schichten-Aufbau (10.11b/c, Core/Shell/Halo) wirkte wie
  mehrere Quellen und war zu kühl. Jetzt wieder **EIN einziges ParticleSystem** (simpler,
  Standard-Unity-Aufbau): ein t=0-Burst, ALLE Partikel starten praktisch punktuell in der
  Minenmitte (`MineStartRadius` 0,02 m) und bilden zusammen EINEN Ball. Die **warme
  Schwarzkörper-Rampe** (weiß → gelb → orange → rot, dann aus) liegt im Partikel-Farbverlauf
  `WarmGradient` (wirkt erst durch den 10.11b-Shader-Fix `i.color.rgb`). **Langsam & weich:**
  Lebensdauer 0,6–0,95 s, geringer Wurf (0,12–0,4 m/s), sanftes Aufblähen auf ~170 %
  (`GrowCurve`), kein Noise. `MineFlashColor` wieder warmes HDR-Weiß. Damit wurden
  `PlayLayered`, `BuildMineLayer`, `Core/Shell/HaloGradient`, `Bloom/HaloCurve` aus `Fx`
  entfernt (zurück auf den einfachen `PlayBuilt`-Pfad); die Minen-Tunables liegen als
  `private const` bei `MineKey`. Ketten-Schutz (0,5 s, Runde 10.11c) bleibt.
  Verbleibt: **Gerätetest** (Mine detonieren → EINE warme, weiche Explosion aus der
  Minenmitte; durch ein Cluster schwingen → NUR EINE Detonation).

- **Runde 10.11e (Minen: jede berührte Mine detoniert; Explosion kürzer/heller/sauberer):**
  Nutzer-Rückmeldung: „Die Explosionen sollen sauberer, heller und kürzer sein. Zudem soll
  jede Mine, die berührt wird, explodieren. Gerade schützt eine Minenexplosion die andere
  Mine daneben."
  **Fix 1 (Ketten-Schutz ENTFERNT):** Der in 10.11c eingeführte Ketten-Schutz
  (`GameConfig.MineChainGuardSeconds`, zuletzt 0,5 s) hatte unerwünscht die NACHBAR-Mine
  „geschützt" (geschluckt) — bei einem Schwung durch mehrere Minen detonierte nur die erste.
  Jetzt detoniert **JEDE berührte Mine**. Der Guard-Feld/-Check aus `GameManager`
  (`_lastMineDetonation`), die Konstante und der Guard-Block sind entfernt. Die Explosion
  ist rein optisch und löst selbst keine weiteren Minen aus → echte „Kettenreaktion"
  (Detonation durch Detonation) gibt es konstruktionsbedingt weiterhin nicht.
  **Fix 2 (kürzer/heller/sauberer):** Lebensdauer 0,6–0,95 s → **0,32–0,5 s**; Drift
  0,12–0,4 → **0,1–0,26 m/s**; Partikelzahl 14 → **12**; Größe 0,1–0,2 → **0,1–0,18 m**;
  Aufblähen 170 % → **135 %** (`GrowCurve`); `MineHoldSeconds` 1,2 → **0,75**. Der
  Partikel-Farbverlauf `WarmGradient` hält die helle Phase länger (weiß → hellgelb → orange
  → rot, sauber ausklingend) und die prozedurale Glüh-Textur ist jetzt ein **sauberer,
  heller Kern** (Mottling entfernt). `MineColorBoost` 2,6 → **3,6** (heller, Bloom kräftiger).
  Verbleibt: **Gerätetest** (Mine detonieren → kurze, helle, saubere Explosion; MEHRERE
  berührte Minen → JEDE detoniert).

- **Runde 10.11f (Minen-Explosion NEU INTERPRETIERT: rund & groß):**
  Nutzer-Rückmeldung: „Die Explosion muss neu interpretiert werden. Also, ich mag den Ansatz
  gerade nicht. Es ist jetzt zu klein und nicht rund genug."
  **Ursache „nicht rund":** Der bisherige Ansatz war ein Partikel-**SPRAY** — Partikel starteten
  punktuell und flogen mit zufälliger Geschwindigkeit (bis 0,26 m/s) auseinander. Dadurch
  zerfasert die Silhouette zu einem unförmigen, mehrlappigen Gebilde.
  **Neues Konzept (statt Spray → EIN runder Lichtimpuls):** Alle Partikel bleiben **praktisch
  konzentrisch** (`MineSpeedMin/Max` 0,02/0,08 m/s), nur die **GRÖSSE** blüht auf. Die additive
  Überlagerung mehrerer konzentrischer, weicher Billboards ergibt damit **EINEN runden, weich
  auslaufenden Ball** — die Expansion (GrowCurve) liefert die Bewegung, nicht der Wurf.
  Konkret: `MineCount` 12 → **5**; Lebensdauer 0,32–0,5 → **0,4–0,6 s**; Größe 0,1–0,18 →
  **0,3–0,45 m**; `GrowCurve` 0,7→1,35→1,0 auf **0,55→1,6→0,95** (Peak ~0,5–0,72 m
  Durchmesser, vorher ~0,24 m → klar größer); `MineHoldSeconds` 0,75 → **0,85**. Farbrampe
  (`WarmGradient`, weiß → gelb → orange → rot) und Größe/`MineColorBoost` (3,6) unverändert.
  Verbleibt: **Gerätetest** (Mine detonieren → EIN großer, RUNDER, weicher Feuerball aus der
  Minenmitte; nicht mehr klein/zerfasert).

- **Runde 10.11g (Minen-Explosion als „Gasexplosion" — recherchiert & neu aufgebaut):**
  Nutzer-Rückmeldung: „die darf nicht wieder kleiner werden und darf auch nicht so lang sein.
  Finde doch erst mal heraus, wie man saubere, runde, helle, rechenarme Gasexplosionen auslöst —
  nicht zu perfekt rund, aber man darf nicht mehrere Quellen sehen."
  **Recherche:** (a) Unity-Manual „Creating a simple explosion": *„Larger particles that don't
  move completely apart appear more like a fireball fed by a destroyed fuel tank."* → große,
  **nicht auseinanderfliegende** Partikel lesen als EIN Feuerball; außerdem Startgröße/-rotation
  randomisieren, um die sichtbare Textur-Wiederholung zu brechen. (b) Cyanilux „Fire/Flame
  Shader Breakdown": viele transparente Quads = Overdraw (teuer für VR/Mobile); prozedurale
  Noise-Verzerrung + per-Partikel-Seed sind der billigere Weg. (c) „Volumetric Explosions"-
  Referenz: Haupt-Feuerball = Form+Farbe über die Zeit.
  **Neues Konzept — EINE Quelle, GAStig, rechenarm:** EIN ParticleSystem, wenige **große**,
  praktisch **konzentrische** Billboards (`MineSpeedMin/Max` 0/0,02 m/s → fliegen nicht
  auseinander → EIN Ball). Die warme Glutfarbe steckt jetzt **räumlich in der Textur**
  (`MineFireballTexture`: weißglühender Kern → gelb → orange → rot) statt im Particle-Verlauf;
  der Color-Verlauf ist nur noch `FadeGradient` (Alpha). Der Textur-Rand wird mit **zwei
  Oktaven Perlin-Noise radial verzerrt** → rund, aber **nicht perfekt kreisrund**; **per-Partikel
  Startrotation (0–2π) + Eigenrotation (±1,2 rad/s, `MineSpinSpeed`)** lässt den unruhigen Rand
  gasig „kochen", sodass mehrere konzentrische Billboards klar wie EINE Quelle wirken.
  Kosten: ein System, eine 128²-Textur, eine Draw-Call, kein Distortion-Shader.
  Werte: `MineCount` 5 → **8**; Lebensdauer 0,4–0,6 → **0,3–0,45 s** (kürzer); Größe 0,3–0,45 →
  **0,45–0,65 m**, `GrowCurve` 0,55→1,6→0,95 (Peak **~0,7–1 m** Durchmesser → größer);
  `MineStartRadius` 0,02 → 0,05; `MineHoldSeconds` 0,85 → **0,65**. Umbenannt:
  `GlowTexture`→`MineFireballTexture`, `GlowMaterial`→`MineFireballMaterial`,
  `ConfigureGlowRenderer`→`ConfigureMineFireballRenderer`, `_glowTex/_glowMat`→`_mineFireball*`;
  der ungenutzt gewordene `WarmGradient` ist entfernt. Software/Hyphen-Aufräumen: unsichtbare
  Soft-Hyphens (U+00AD) aus `Fx.cs` und `ParticleGlow.shader` entfernt.
  Verbleibt: **Gerätetest** (Mine detonieren → EIN großer, heller, gasiger Feuerball; Rand leicht
  unruhig, keine mehreren Quellen sichtbar; kurz).

- **Runde 10.11h (Minen-Explosion komplett neu: ECHTE 3D-Gas-Explosion — Mesh + Volumen-Shader):**
  Nutzer-Rückmeldung: „Nach der Explosion darf es nicht wieder kleiner werden. Es darf nicht immer
  exakt gleich aussehen. Irgendwie wirkt es nicht so richtig 3D. Also: komplette Neuinterpretation."
  **Diagnose:** Der Billboards-Ansatz (auch konzentrisch) liest sich immer flach — es sind ebene
  Quads, kein Volumen. Zusätzlich war die Größen-Kurve nicht monoton und die Optik (Textur +
  konzentrische Quads) praktisch identisch bei jeder Detonation.
  **Neues Konzept — echtes Volumen:** Statt ParticleSystem jetzt ein **prozedurales Kugel-Mesh**
  (`Fx.MineSphereMesh`, Radius 0,5, ein Mesh; gecacht) mit einem neuen additiven **Volumen-Shader**
  `BeatSaber/MineFireball` (neu: `Assets/Resources/BeatSaber/MineFireball.shader`): **3D-Value-
  Noise (2 Oktaven fbm, rechenarm)** im OBJEKTRAUM, das über die Zeit strömt → gasiges Feuer; heiße
  weiße Mitte → gelb → orange → rot (warm); weicher Rand (kein harter Kreis); `Cull Off` (Vor- und
  Rückseite addieren → volumig). Animation steckt in `Fx.MineFireballFx` (MonoBehaviour, gepoolt
  wie die Schockwelle): **wächst monoton (ease-out) → wird NIE kleiner**, fadet am Ende weich aus
  (`_Fade`, kühlt dabei leicht nach rot), dreht langsam. `_Seed` + Zufalls-Reihenfolge (Größe/
  Dauer/Spin) + Zufalls-Rotation → **nicht immer gleich**. Kosten: ein Mesh, eine Draw-Call.
  Werte: `MineSizeMin/Max` **0,75–0,95 m** Startdurchmesser, `MineGrow` **1,4** (Peak **~1,05–1,33 m**,
  nur größer), Dauer **0,34–0,5 s**, `MineSpinSpeed` **0,9 rad/s**, `MineSeedRange` 60.
  Aufgeräumt: `CreateMineTemplate`/`PlayBuilt`-Pfad der Mine → `CreateMineFireball` + `_mineFree`-
  Pool; `MineFireballTexture`, `GrowCurve`, `ConfigureMineFireballRenderer`, `MineKey`/`MineCount`/
  `MineSpeed*`/`MineStartRadius`/`MineHoldSeconds` entfernt; der nun ungenutzte
  `Assets/Resources/BeatSaber/ParticleGlow.shader` (+ .meta) **gelöscht**. Soft-Hyphens (U+00AD)
  auch aus `GameConfig.cs` und `PROJECT_NOTES.md` entfernt.
  **Nachtrag 1 (Shader-Fix, Geräte-Feedback „Minen verschwinden nur"):** Der Shader rendert nur die
  Kugel-HÜLLE. Die Dichte kam zuvor aus `edge = (1-rn)²` mit `rn = length(obj)*2` — auf der Hülle ist
  `rn` aber überall exakt 1 ⇒ `edge = 0` ⇒ Alpha 0 ⇒ die Explosion war komplett unsichtbar. Jetzt
  kommt die Dichte aus der **Blickrichtung** (`ball = pow(saturate(dot(Normal, ViewDir)), …)`, im
  Objektraum über `unity_WorldToObject`/`_WorldSpaceCameraPos`): Mitte der Silhouette = dick/hell,
  Rand = dünn → rundes Volumen. Cull Off bleibt (Rückseiten tragen 0 bei).
  **Nachtrag 2 (Geräte-Feedback „viel zu große Kugel, muss teilweise durchsichtig sein, kürzer und
  schärfer"):** Vorher war es ein massiver, greller Ball.
  - **Kleiner:** `MineSizeMin/Max` **0,55–0,68 m**, `MineGrow` **1,4** → Peak **~0,77–0,95 m**
    (vorher 1,05–1,33).
  - **Kürzer & schärfer:** `MineLifetimeMin/Max` **0,22–0,32 s** (vorher 0,34–0,5); Wachstum jetzt
    `e = 1-(1-t)³` (schneller Knall-Start), Fade voll bis t≈0,1, dann schnell aus (vorher weicher);
    `MineSpinSpeed` **1,4 rad/s** (Lebendigkeit). `MineColorBoost` **3,6 → 2,2** (kein Zuschmieren
    durch Bloom).
  - **Teilweise durchsichtig / Flammen-Struktur (Shader):** Zwei Noise-Skalen (groß wabert, klein
    reißt auf), dann kontrastreich verrechnet: `flame = saturate((ball*(0.55+1.25*n) - 0.30)*2.6)`
    — dichte Stellen glühen, dazwischen **Lücken**. Alpha bewusst `< 1`: `a = flame*(0.30+0.8*core)`
    (heißer Kern `core = facing^3.2` trägt am meisten) → sieht durchscheinend aus, KEIN massiver Ball.
    `ball`-Exponent jetzt **1,35** (schärferer Rand), Farbstufen etwas satter.
  Verbleibt: **Gerätetest** (Mine detonieren → ein kurzes, scharfes 3D-Feuer mit sichtbaren Flammen/
  Lücken, das aus der Minenmitte aufblüht, NICHT schrumpft und etwas anders aussieht).

- **Version 1.0 (Dokumentation & Repository, 11.10.2026):** Ab hier ist der Stand als
  **Version eins (v1.0)** eingefroren. Ergänzt:
  - `README.md` — nur belegte Angaben: Voraussetzungen/Versionen, PICO-SDK-Bezug + lokaler
    `file:`-Pfad (§2), Release-Link der APK, Bedienung (§5), Lieder-Ordner (§6), Easter Egg,
    Build-Schritte, Herkunft + Lizenzen.
  - `LICENSE` — **GPL-3.0**, weil von **ArcViewer** (GPL-3.0) abgeleitete Umgebungs-Assets im
    Repo liegen (`Assets/ArcViewerEnvironmentScenes`); **BeatSaber-Web-Replays** und
    **SuperSaber** (jeweils MIT verifiziert) dienten nur als Referenz.
  - `MrSaber-v1.0.apk` — **NICHT** im Repo, sondern als Asset am **GitHub-Release `v1.0`**
    (`https://github.com/Riegelknight/MrSaber/releases/tag/v1.0`).
  - **Git-History bereinigt:** alle bisherigen Commits zu EINEM Commit „MrSaber v1.0"
    zusammengefasst, Tag `v1.0` — der jetzige Zustand IST v1.

---

## 9. Arbeitsweise: wer macht was (Workflow-Vereinbarung)

**Vereinbarung:** Nach Code-Änderungen übernimmt die KI/der Assistent die **Kompilierung
des Unity-Projekts und das automatische Beheben von Fehlern** — beim Benutzer verbleibt
ausschließlich der **„Build and Run"**-Teil (Android-Build auf die PICO 4 + Gerätetest).

1. **Kompilieren (Assistent):** Batch-Mode-Kompilierung ohne laufende Editor-Instanz
   (Editor-Pfad laut Windows-Registry „Location x64" = `Y:\Unity\2022.3.62f3`):

   ```powershell
   & 'Y:\Unity\2022.3.62f3\Editor\Unity.exe' -batchmode -quit -projectPath 'Y:\Unity\MrSaber' -logFile 'C:\Users\Riegholm\AppData\Local\Temp\opencode\mrsaber_compile.log'
   ```

   Ergebnis im Log: `Compilation succeeded` bzw. die Fehlerliste.
2. **Fehler beheben (Assistent):** Kompilier- und Logikfehler aus Log/Konsole werden
   eigenständig korrigiert; es wird kein Code übergeben, der nachweislich nicht
   kompiliert. Fehler, die erst auf dem Gerät sichtbar werden (z. B. Scoring, Raumfixierung),
   deckt der Gerätetest des Benutzers ab.
3. **„Build and Run" (Benutzer):** Nur das Android-Build samt Deployment auf die Brille
   und das Testen gehört dem Benutzer. Der Assistent stößt ausdrücklich kein Deployment
   auf das Gerät an (kein ADB, kein Android-Build nötig).
4. **Editor-Instanz geöffnet?** Ist der Unity-Editor gerade offen, kann der Batch-Mode die
   Projekt-Library nicht exklusiv sperren (er stürzt dann beim Start ab, Stacktrace
   `HandleProjectAlreadyOpenInAnotherInstance`) — dann arbeitet der Assistent über das
   offene Editor-Log weiter. **Wichtig (am 27.09.2026 verifiziert):** Bei diesem Setup ist
   die Unity-**Auto-Refresh aus** — Unity bemerkt externe Dateiänderungen dann NICHT beim
   Fokussieren des Fensters. Der Benutzer muss im Editor **Strg+R** (Assets → Refresh)
   drücken, oder es wird ein **Build** gestartet (der erzwingt den Refresh). Erst danach
   importieren neue Assets und kompilieren die Skripte. Der Build-Report im Editor-Log ist
   zugleich der beste Nachweis, dass die Änderungen wirklich im Player gelandet sind
   (Asset-Liste + `Build completed with a result of 'Succeeded'`).

Rahmenbedingungen (unverändert): kein Git-Repo (kein Rollback), keine ADB, Songs liegen
nur auf der Brille.

---

## 10. Leistung / Laufzeit-Optimierungen

Hot-Path-Regeln (Optimierungsrunde, danach neu hinzukommenden Code danach ausrichten):

- **Keine Allokationen pro Frame:** Der Frame-Loop ist allokationsfrei. XR-Device-Abfragen
  (Fallback-Anker, Haptik) nutzen die wiederverwendete **`_scratchDevices`**-Liste im
  GameManager. VelocityEstimator nutzt einen vorab angelegten Ringpuffer; Note-/Wall-/Saber-
  Loops erzeugen kein GC. Treffer-Erkennung läuft **rechenarm geometrisch** (Slab-Test
  Segment↔Notenbox, nur Noten im Beat-Fenster, kein Physik-Zugriff) — bewusst OHNE
  SphereCast; der frühere 2×-SphereCast-Ansatz liegt hinter `GameConfig.GeometricHitDetection`
  als Fallback still.
- **SFX-Clips einmal laden:** `AudioManager` cacht `Slice`/`UI_Confirm` statisch
  (`_sliceSfx`/`_uiSfx`). Kein `Resources.Load` je Treffer/UI-Klick. **Neue SFX ebenfalls
  cachen.**
- **Partikel gepoolt (wie die Referenz):** `View/Fx.cs` cacht die Effekt-Prefabs statisch und
  hält je Effekt einen Pool (Cap 4 / Max 8, Referenz `ParticleEffectSettings`). Beim
  Abspielen wird KEIN `Resources.Load`/keine `Instantiate`-Allokation mehr fällig; ein
  versteckter `FxRunner` gibt fällige Effekte zeitgesteuert zurück (eigener kleiner
  Update-Loop, keine Coroutine pro Treffer). **Neue Effekte ebenfalls so anbinden** — nicht
  wie der frühere `Fx.Burst`, der pro Treffer lud und instanziierte.
- **Charts gecacht:** `BeatmapParser.ParseChart` hält geparste Charts im `ChartCache`
  (Schlüssel = `.dat`-Pfad der Difficulty). Der Endlos-Loop parst wiederkehrende Songs
  nicht erneut von der Platte; Cache begrenzt (`ChartCacheMax = 128`, danach klar).
  Hinweis: `meta.NoteJumpMovementSpeed` (Chart-`_customData`) wird nur noch beim ersten
  Laden überschrieben — `SongMeta` behält den Wert (identisches Verhalten).
- **Save-I/O gebündelt:** `GameManager.Save()` schreibt die Datei höchstens einmal pro
  Sekunde (Werte stehen sofort im Speicher); garantiert geflusht wird in `LoadSong`
  (Songwechsel/Endlos-Loop/X), in `FlushSave()`-Aufrufen und bei `Quit`.
  `Directory.CreateDirectory` läuft nur 1× je Lauf (`_saveDirReady`).
- **Bereits gecacht (nicht anfassen):** Noten-Materialien/-Meshes (NoteView, statisch),
  Wand-Materialien + PropertyBlock (WallView, statisch), Treffer-/UI-Sounds (AudioManager,
  statisch), Partikel-Prefabs + Pools (Fx, statisch), UI-Sprites (BsUi, statisch), die
  Beat→Sekunden-Kurve (BuildBeatToSeconds: kumulierte Sekunden + binäre Suche).
- **Performance-Runde 07.10 (PICO-Optimierung, kein Verhalten geändert):**
  (1) **Fixed Foveated Rendering** über die PICO-API aktiviert
  (`PicoSeeThrough.ApplyFoveation` → `PXR_FoveationRendering.SetFoveationLevel(Med,false)`),
  beim Start und nach jedem Session-Wechsel — der stärkste GPU-Hebel auf der PICO 4.
  (2) **Bloomfog-Kamera gedrosselt** (`EnvironmentDirector.FogInterval` = 30 Hz statt jeden
  Frame) und **nur die Leucht-Renderer** (mit `LightHandler`) liegen auf dem Fog-Layer —
  die Fog-Kamera zeichnet damit nicht die ganze Umgebung doppelt.
  (3) **URP-Asset (Performant):** MSAA 4→2, **Schatten komplett aus**
  (`m_MainLightShadowsSupported/m_AnyShadowsSupported/m_SoftShadowsSupported = 0`) — das
  Spiel ist unlit/emissiv, Schatten waren reine Kosten. HDR bleibt AN (nötig fürs Glühen),
  aber nur außerhalb des Passthrough (`PicoSeeThrough.ApplyCameraState`).
  Android-Qualitätsstufe ist bereits „Performant" (QualitySettings `m_PerPlatformDefaultQuality`).
  (4) **PICO-Spitzenfeatures (sicher, gerätetestbar):** **90 Hz** (`PXR_Settings.systemDisplayFrequency
  = RefreshRate90`), **Late Latching** (`PXR_LateLatching` wird zur Laufzeit an die Kamera
  gehängt, da die Kamera erst zur Laufzeit entsteht; `PXR_ProjectSetting.latelatching = 1`),
  **Adaptive Resolution** (`PXR_Manager.adaptiveResolution = 1` + `PXR_ProjectSetting`) und
  **Foveation Stufe Med** (`PXR_Manager.foveationLevel`). Application SpaceWarp bleibt
  BEWUSST AUS (Artefaktrisiko, braucht Gerätetest).
  (5) **Umgebungs-Texturen:** Editor-Postprocessor (`Assets/Editor/ArcViewerAssetPostprocessor.cs`)
  erzwingt für `Assets/ArcViewerEnvironment/Textures/**` **ASTC 6×6 + max. 1024 px + Mipmaps**.
