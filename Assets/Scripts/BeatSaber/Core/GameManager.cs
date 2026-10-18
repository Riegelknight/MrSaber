// GameManager: Zentraler Orchestrator (Lieder, Scheduler, Treffer, Punkte, Menü, Raum).
// Aufgeteilt in Partial-Dateien GameManager.*.cs nach Verantwortung.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace BeatSaber
{
    public enum GamePhase { Boot, Playing, Menu }

    /// <summary>
    /// Zentraler Orchester: Lieder + Charts, Scheduler, Noten-/Wand-Spawn,
    /// Punktesystem (115er-Schema + x1..x8), Endlos-Loop, Hand-Menü,
    /// Spielerverwaltung, Raum-Ausrichtung/Quit und Template-Aufräumen.
    /// </summary>
    public sealed partial class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public PlayerProfile Player => _player;
        public SaveData Settings => _save;

        /// <summary>
        /// FESTE SCORE-Uhr (Sekunden) — die Referenz für Hitboxen und Saber. Wird von
        /// KEINEM Menü-Offset bewegt (siehe PROJECT_NOTES §4, drei Ebenen).
        /// </summary>
        public static float ScoreTime => Instance != null && Instance._audio != null
            ? Instance._audio.ScoreTime : 0f;

        /// <summary>
        /// DARSTELLUNGS-Uhr (Sekunden) = Score-Uhr + Noten-Offset. Nur für die Anzeige
        /// (Noten/Wände/Effekte); Treffer-Erkennung und Saber nutzen die Score-Uhr.
        /// </summary>
        public static float VisualTime => Instance != null && Instance._audio != null
            ? Instance._audio.VisualTime : 0f;

        // Komponenten
        private AudioManager _audio;
        private ControllerInput _input;
        private HudDisplay _hud;
        private HandMenu _menu;

        // Lieder / Charts
        private List<SongMeta> _songs = new List<SongMeta>();
        private int _songIndex = -1;
        private SongMeta _current;
        private BeatmapChart _chart;
        // Zeitplan je Note inkl. der musterabhängigen Positions-Effekte (Stapel/Flip),
        // damit sie NICHT pro Frame berechnet werden müssen (BuildSchedule → Spawn).
        // startYRel ist RELATIV zur Zielreihe gespeichert (Feldhöhen-unabhängig), damit
        // eine Änderung der „Feldhöhe" auch bereits vorbereitete Noten automatisch mitnimmt.
        private List<(float spawn, NoteModel note, float startYRel, float flipStartX, float flipYHeight)> _noteSchedule
            = new List<(float, NoteModel, float, float, float)>();
        private int _noteCursor;
        private List<(float spawn, ObstacleModel obs, float durSec)> _wallSchedule = new List<(float, ObstacleModel, float)>();
        private int _wallCursor;
        private readonly List<WallView> _activeWalls = new List<WallView>();
        // Aktive Noten für die geometrische Treffer-Erkennung (rechenarm): beim Spawn
        // befüllt, beim Verbrauch/Durchflug ausgedünnt. Kein Physik-Zugriff nötig.
        private readonly List<NoteView> _activeNotes = new List<NoteView>();

        // Bögen (Arcs, reine Optik) und Ketten (Chains, Optik + Glieder-Treffer).
        private List<(float spawn, ArcModel arc)> _arcSchedule = new List<(float, ArcModel)>();
        private int _arcCursor;
        private readonly List<ArcView> _activeArcs = new List<ArcView>();
        private List<(float spawn, ChainModel chain)> _chainSchedule = new List<(float, ChainModel)>();
        private int _chainCursor;
        private readonly List<ChainView> _activeChains = new List<ChainView>();

        // Sprung-/Spawn-Parameter des aktuellen Lieds — aus den Song-Daten (NJS/HJD/
        // NoteJumpStartBeatOffset). Die Bewegung ist KONTINUIERLICH mit NJS; Spawn-Zeit =
        // Beat − 2 × ReactionTime (volle Sprungdauer). Die musterabhängigen Positions-Effekte
        // (Stapel/Flip) berechnet BuildSchedule je Note; NoteView führt sie zeitbasiert aus.
        private float _njs;              // Map-Tempo (Basis-Einheiten/s)
        private float _reactionTime;     // Sekunden bis zur halben Sprungdistanz
        private float _halfJumpDistance; // Meter (Basis-Einheiten), für die Stapel-Parabel
        private float _spawnLeadTime;    // Sekunden Spawn -> Beat (2 × ReactionTime)
        // Beat->Sekunden der aktuellen Map (respektiert den BPM-Verlauf: _BPMChanges/bpmEvents)
        private System.Func<float, float> _toSeconds;

        // Persistenz
        private SaveData _save = new SaveData();
        private PlayerProfile _player;

        // Save-Performance (siehe PROJECT_NOTES.md §10): Die Werte stehen sofort im
        // Speicher, der Datei-Write wird gebündelt (max. 1× pro Sekunde) und beim
        // Liedende/Quit explizit geflusht — kein Disk-I/O + JSON-Serialisierung bei
        // jedem Rekord-Schnitt (TrySaveBestImmediately ruft Save() je Schnitt auf).
        private static bool _saveDirReady;   // Directory.CreateDirectory nur 1× je Lauf
        private float _lastSaveTime = float.MinValue;
        private bool _savePending;
        private const float SaveThrottleSeconds = 1f;

        // Score
        private int _combo;
        // Multiplikator-Stufen-Index (0..3 -> x1/x2/x4/x8) + Treffer innerhalb der Stufe.
        // 1:1 wie Original/ArcViewer (ScoringUtils.ManuallyAdvanceCombo) — bewusst vom
        // Combo-Zähler ENTKOPPELT, damit nach einem Fehler die Stufe nur EINE Stufe fällt
        // (z. B. x8 -> x4) und von dort mit eigenem Fortschritt weiterläuft.
        private int _multTier;
        private int _comboProgress;
        private long _score;

        // Floating Score (Referenz: InGameUiView.DisplaySliceScore — Score/"MISS" an der Noten-Position)
        private readonly Stack<TMPro.TextMeshPro> _scoreTextPool = new Stack<TMPro.TextMeshPro>();
        private const float ScoreSpawnForwardOffset = 2.5f;  // Referenz: _spawnForwardOffset
        private const float ScoreSpawnDownwardOffset = 1f;   // Referenz: _spawnDownwardOffset
        private const float ScoreFloatDistance = 1.5f;       // Referenz: _floatForwardDistance
        private const float ScoreDisplayDuration = 1f;       // Referenz: _sliceScoreDisplayDuration

        // Rig & Welt
        private Camera _cam;
        private Transform _cameraOffset;
        private Transform _leftAnchor, _rightAnchor;
        // Raum-Verankerung: Der Raum/Noten stehen während des Spiels FEST auf dem
        // Weltursprung — Kopfbewegungen verschieben die Spielwelt nicht. Der Boden kommt
        // über den Tracking-Origin „Floor" (ermittelt beim Spielstart), nicht aus einer
        // Kopfhöhen-Rechnung.
        //  - _roomYawDeg: Gierwinkel des Raums. Rein visuell und SITZUNGSBEZOGEN. Rechter
        //    Stick dreht kontinuierlich; Taste A setzt ihn auf die aktuelle Blickrichtung.
        //  - _floorOffsetMeters: SESSION-Bodenversatz (linker Stick hoch/runter). Startet bei
        //    JEDEM Spielstart bei 0 und wird NICHT gespeichert — Taste A ändert die Höhe nicht.
        //    Größere Totzone gegen versehentliches Auslenken (GameConfig.FloorOffsetDeadzone).
        //  - _roomFollowOffset: Welt-X/Z des SPIELER-Ankers (0/0 beim Start). Der Raum-Ursprung
        //    (Score-Zone) liegt PlayerDistanceBehindZone davor (ApplyRoomAnchor). Taste A setzt
        //    ihn auf die Kopf-X/Z (Spieler an der hinteren Position); bei aktiver „Raum folgt Spieler"-
        //    Tiefen-Folge (Menü) laufen zusätzlich Kopf-Deltas entlang der Raum-Vorwärtsachse
        //    zu. _lastFollowHead merkt die Kopfposition des Vorframes.
        private float _roomYawDeg;
        private float _floorOffsetMeters;
        private Vector3 _roomFollowOffset;
        private Vector3 _lastFollowHead;
        private bool _followInit;

        // Run and Play: puffert die Vorwärtsgeschwindigkeit des Raums und liefert Zustand +
        // geglättete Geschwindigkeit (0,3 s). Aktiv nur im Zustand Playing ohne Menü.
        // `_roomFollowBeforeRun` merkt den „Raum folgt Spieler"-Wert, damit er beim
        // Ausschalten des Modus wiederhergestellt werden kann (der Modus erzwingt/sparrt ihn).
        private readonly RunAndPlay _runAndPlay = new RunAndPlay();
        private bool _roomFollowBeforeRun;
        private readonly List<InputDevice> _scratchDevices = new();
        // Wiederverwendete Subsystem-Liste (Boden-/Recenter-Abfragen): allokationsfrei.
        private readonly List<XRInputSubsystem> _subsystemScratch = new(4);
        private Transform _gameRoot;
        private Environment.EnvironmentDirector _environment;
        private SaberView _saberL, _saberR;
        private GripCurvedLabel _labelName, _labelBest;
        private readonly Dictionary<SaberSide, InputDevice> _devices = new Dictionary<SaberSide, InputDevice>();
        // Kontakt-Haptik (Schwert↔Schwert, Schwert↔Wand): Drossel je Hand. Während des
        // Kontakts wird in kurzen Abständen ein leichter Impuls gesendet (anhaltendes
        // Vibrieren), ohne jeden Frame zu senden; endet der Kontakt, wird der Timer genullt.
        private readonly float[] _contactHapticNext = new float[2];
        // Arc-Interaktion: Drossel je Hand (Haptik + Spitzen-Partikel), Index = (int)SaberSide.
        private readonly float[] _arcHapticNext = new float[2];
        private readonly float[] _arcParticleNext = new float[2];
        // Saber-Clash: kanten-getriggert (nur beim Übergang „kein Kontakt -> Kontakt").
        private bool _saberClashing;

        // Menü-Zustand
        private int _menuSide; // 0 zu, -1 links, +1 rechts
        private GamePhase _phaseBeforeMenu;
        private bool _inPlayersMenu;
        private bool _awaitingName;
        private bool _songListOpen;        // scrollbares Songlisten-Untermenü (via Klick auf Songtitel)
        private bool _songSelectionQueued; // im Menü Song/Difficulty geändert => beim Schließen neu laden
        private float _menuInputLockUntil; // verhindert Doppel-Auslösung nach Ansichtswechsel (Kurz-Sperre)
        private bool _allowNewPlayer = true; // verhindert Doppel-Öffnen der Namenseingabe
        private int _stickScrollDir;   // Joystick-Blättern in der Songliste (0 = kein Stick)
        private float _stickScrollTimer;
        // Beim Öffnen des Menüs fahren BEIDE Lichtschwerter automatisch
        // ein. Dadurch bleibt als einziger „Beam" der Laser/Cursor der Gegenhand sichtbar –
        // die Menüseite leuchtet nicht mehr (kein dauerhaft sichtbarer schwacher Beam).
        // Der Cursor erscheint unabhängig vom Schwertstatus. Beim Schließen fährt ein Schwert
        // nur dann automatisch wieder aus, wenn sein Status während des Menüs NICHT manuell
        // (Grip-Doppelklick) geändert wurde. Index = (int)SaberSide (Left=0, Right=1).
        private readonly bool[] _menuAutoRetracted = new bool[2];  // je Seite automatisch eingefahren
        private readonly bool[] _menuManualChanged = new bool[2];  // je Seite manuell geändert
        private string _pendingDelete;
        private bool _confirmingDelete;   // modales Lösch-Popup aktiv (Menü eingefroren)
        private bool _confirmingSongDelete; // das laufende Popup löscht ein LIED (nicht einen Spieler)
        private int _pendingSongDeleteIndex = -1; // Zeilenindex des Lieds im Lösch-Dialog
        private bool _importInFlight;     // Lied-Import läuft; Ende wird in Update ausgewertet
        private TouchScreenKeyboard _keyboard;
        private bool _keyboardWasVisible;   // Tastatur war mindestens einmal sichtbar (kein Fehl-Commit beim Öffnen)
        private int _keyboardStatusLog = -1; // zuletzt geloggter Tastatur-Status (Diagnose)
        private string _pendingName = "";   // zuletzt getippter Name (Fallback, falls 'text' beim Done leer ist)
        private string _shownName = "";     // zuletzt im Menü angezeigter Name (Live-Vorschau "Name: …")
        private bool _songEnded;
        private Coroutine _audioLoad;
        // Zuletzt angezeigte Grifflabel-Texte (Vergleich statt Dirty-Flag: Rekorde und
        // Spielerwechsel wirken sofort, ohne Songwechsel abzuwarten).
        private string _labelNameText = "";
        private string _labelBestText = "";

        private const int IdPlayers = 103, IdHeight = 106,
                          IdDelay = 108,
                          IdClose = 109, IdDiff = 110, IdSongList = 120,
                          // Entwickler-Schalter: PICO-Passthrough an/aus (global, persistent).
                          IdPassthrough = 113,
                          IdVisual = 114,
                          // Vertikaler Versatz für Noten/Minen + bodenfreie Wände (persistent).
                          IdFieldHeight = 111,
                          // Tiefen-Folge: Raum bewegt sich mit der Vorwärts-/Rückwärtsbewegung (persistent).
                          IdRoomFollow = 112,
                          // Run and Play (global, persistent) + persönlicher Lauf-Tempo-Regler.
                          IdRunPlay = 115, IdRunComfort = 116,
                          // Sichtprüfung der Tracking-Posen (Session, NICHT persistent).
                          IdControllerDebug = 117,
                          IdNew = 250, IdDelete = 251, IdBack = 252,
                          // Basis-ID der Lied-Zeilen in der Songliste (Lösch-„X" je Zeile).
                          IdSongBase = 300;

        /// <summary>Blauer Balken hinter dem aktiven Spieler (Spielerverwaltung).</summary>
        private static readonly Color ActivePlayerBg = new Color(0.16f, 0.42f, 0.95f, 0.45f);

        // ======================================================================
        // Lebenszyklus
        // ======================================================================
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            _input = new ControllerInput();
            _audio = gameObject.AddComponent<AudioManager>();
        }

        private void Start()
        {
            SleepTemplateUi();

            _cam = Camera.main;
            if (_cam == null) _cam = FindObjectOfType<Camera>();
            _cameraOffset = _cam != null ? _cam.transform.parent : null;
            // Anker robust auflösen — egal welches Rig aktiv ist (XRI-Controller,
            // MRTK-Hand-Controller oder das Hands-Rig der Vorlage). Fallback liest
            // die Pose direkt aus den physischen PICO-Controllern (XRNode).
            _leftAnchor = FindControllerAnchor("Left");
            _rightAnchor = FindControllerAnchor("Right");
            if (_leftAnchor == null) _leftAnchor = CreateXrPoseAnchor("SaberAnchorL", XRNode.LeftHand);
            if (_rightAnchor == null) _rightAnchor = CreateXrPoseAnchor("SaberAnchorR", XRNode.RightHand);

            _gameRoot = new GameObject("BeatSaberWorld").transform;
            // Skalierungs-Basis explizit setzen: Der Raum-Root startet JEDEN Lauf mit 1,0.
            // Es gibt KEINEN gespeicherten „zuletzt verwendeten" Basisfaktor — immer erst
            // laden (EnsurePlayer -> ApplyRoomScale), dann einmal absolut anwenden.
            _gameRoot.localScale = Vector3.one;
            NotePool.SetRoot(_gameRoot);

            // Hintergrund-Umgebung (ArcViewer „Default"): wird nur bei Bedarf gebaut
            // (Modi Default/Animiert) — im Passthrough-Modus fallen keine Kosten an.
            var envGo = new GameObject("[Environment]");
            envGo.transform.SetParent(transform, false);
            _environment = envGo.AddComponent<Environment.EnvironmentDirector>();
            _environment.AttachToRoom(_gameRoot);

            HideControllerModels();
            StartCoroutine(HideControllerModelsRoutine());

            BuildSabers();
            // Diagnose: WELCHER Anker die Schwerter wirklich treibt (das Rig verschachtelt
            // mehrere Kandidaten: „Left/Right Controller", „Left/Right Hand").
            Debug.Log($"[BeatSaber] Controller-Anker: links={AnchorPath(_leftAnchor)}, " +
                      $"rechts={AnchorPath(_rightAnchor)}");
            // Debug-Controller (Tracking-Sichtprüfung/Kalibrierung; Menüzeile „Controller anzeigen").
            ControllerDebugView.Attach(SaberSide.Left, _leftAnchor);
            ControllerDebugView.Attach(SaberSide.Right, _rightAnchor);
            BuildUi();

            LoadSave();
            EnsurePlayer();
            MigrateAudioOffset(); // Alt-Offset (−85) einmalig in die Basis-Kalibrierung überführen
            RefreshAudioOffset(); // gespeicherten Audio-Offset auf die Musik anwenden
            ApplyBackgroundSetting(); // gespeicherte Hintergrund-Option (Passthrough/Default/Animiert) anwenden

            // Bodenniveau beim SPIELSTART ermitteln (nicht bei jedem Lied): Tracking-Origin
            // „Floor" setzen und wiederholen, bis die Runtime ihn bestätigt.
            StartCoroutine(EnsureFloorOrigin(mustSucceed: true));

            // Lieder laden (auf Android ggf. erst die externe Speicher-Berechtigung für den
            // SongCore-Ordner einholen).
            StartCoroutine(BootSongsRoutine());
        }

        /// <summary>
        /// Holt auf Android ggf. die externe Speicher-Berechtigung (SongCore-Ordner der
        /// offiziellen Beat Saber, Download-Ordner, MrSaber-Ordner liegen ALLE außerhalb der
        /// App-Sandbox) und lädt erst dann Liederliste + erstes Lied. Davor wird der
        /// Download-Ordner nach Liedern abgesucht (SongInbox) — so sind frisch kopierte
        /// Lieder direkt in der Liste.
        /// </summary>
        private IEnumerator BootSongsRoutine()
        {
            yield return EnsureStoragePermission();
            yield return ImportSongsAtBootRoutine();   // Download-Ordner -> MrSaber-Ordner

            _songs = BeatmapParser.LoadSongList();
            if (_songs.Count == 0)
            {
                Debug.LogError("[BeatSaber] Keine Lieder gefunden!");
                yield break;
            }
            RestoreDifficultyChoices();   // gespeicherte Schwierigkeits-Wahl je Lied anwenden
            MigrateLegacyScores();        // alte Highscores (ohne Difficulty-Teil) umziehen
            ShuffleSongsAtStart(); // Song-Reihenfolge zufällig (sonst immer dieselbe Reihenfolge)
            LoadSong(0);
        }

        /// <summary>
        /// Android: Die Lieder liegen außerhalb der App-Sandbox. Dafür wird einmalig die
        /// externe Speicher-Berechtigung angefordert — READ (Lieder lesen) und WRITE
        /// (Lieder aus dem Download-Ordner verschieben/entpacken, siehe SongInbox). Beide
        /// stehen im Manifest (WRITE über das Player-Setting „Write Permission = External",
        /// READ über AndroidStorageManifestPostProcessor). Auf Nicht-Android/Editor passiert
        /// nichts. Auf Android 10 sind READ/WRITE eine gemeinsame Gruppe — die zweite
        /// Anfrage entfällt dann automatisch.
        /// </summary>
        private IEnumerator EnsureStoragePermission()
        {
            if (Application.platform != RuntimePlatform.Android) yield break;
            yield return RequestPermissionIfMissing(UnityEngine.Android.Permission.ExternalStorageRead, true);
            yield return RequestPermissionIfMissing(UnityEngine.Android.Permission.ExternalStorageWrite, false);
        }

        /// <summary>
        /// Fordert eine einzelne Berechtigung an, falls sie noch fehlt, und wartet auf die
        /// Antwort (der Dialog läuft im Main-Loop), max. 30 s. <paramref name="warn"/> steuert,
        /// ob eine Ablehnung als Warnung geloggt wird.
        /// </summary>
        private IEnumerator RequestPermissionIfMissing(string permission, bool warn)
        {
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission)) yield break;

            bool? granted = null;
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => granted = true;
            callbacks.PermissionDenied += _ => granted = false;
            callbacks.PermissionDeniedAndDontAskAgain += _ => granted = false;
            UnityEngine.Android.Permission.RequestUserPermission(permission, callbacks);

            float waited = 0f;
            while (granted == null && waited < 30f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (granted != true && warn)
                Debug.LogWarning("[BeatSaber] Externe Speicher-Berechtigung nicht erteilt — " +
                                 "die Lieder im SongCore-/Download-Ordner fehlen dann " +
                                 "(App-Einstellungen → Berechtigungen).");
        }

        private void Update()
        {
            _input.Update();
            PollSongImport(); // Ende eines Lied-Imports -> Liederliste aktualisieren

            // Rechter Stick links/rechts = Raum drehen; linker Stick hoch/runter = Bodenversatz.
            // Nur außerhalb der Songliste (dort blättert der Stick durch die Lieder).
            UpdateRoomAdjustment();
            // ---- Quit: Y / B ≥ 2 s halten (ohne Rückfrage) ----
            if (_input.LeftYHeldLong || _input.RightBHeldLong)
            {
                Quit();
                return;
            }

            // ---- Taste A (rechts, kurz): Raum unter Head-Set + in Blickrichtung drehen ----
            // Nur X/Z und Gier; Höhe/Y bleibt; kein Tracking-/Boden-Eingriff.
            if (Phase != GamePhase.Menu && _input.RightAWasPressedThisFrame)
            {
                RecenterRoomOnHead();
            }

            // ---- Easter Egg: Grip-Doppelklick fährt das jeweilige Lichtschwert ein/aus ----
            // Jederzeit möglich (auch im Menü/Pause); solange eingefahren, keine Noten-Treffer.
            if (_input.LeftGripDoubleClicked) ToggleSaberRetract(SaberSide.Left);
            if (_input.RightGripDoubleClicked) ToggleSaberRetract(SaberSide.Right);

            // ---- Nächster Song: X (links) kurz — während des Spiels ----
            if (Phase == GamePhase.Playing && _input.LeftXWasPressedThisFrame)
            {
                LoadSong(_songIndex + 1);
            }

            // ---- Sicherung: Ohne geöffnetes Menü darf nichts vom Menü übrig bleiben.
            //      Vor allem der Laser (LineRenderer liegt außerhalb des Canvas) darf dann
            //      NICHT stehenbleiben — er existiert nur, solange das Menü offen ist.
            if (_menuSide == 0 && _menu != null && _menu.Visible) _menu.Hide();

            // ---- Menü öffnen/schließen ----
            if (_menuSide == 0)
            {
                if (_input.LeftYReleasedShort) OpenMenu(-1);
                else if (_input.RightBReleasedShort) OpenMenu(1);
            }
            else
            {
                // Lösch-Bestätigung: Die Menütaste bricht NUR die Bestätigung ab (zurück zur
                // Spielerverwaltung, Löschen verworfen). Ein ERNEUTES Drücken schließt dann
                // das Menü über die normale Logik darunter.
                if (_confirmingDelete)
                {
                    if (_input.LeftYReleasedShort || _input.RightBReleasedShort)
                    {
                        CancelDeletePopup();
                        _audio.PlayUiSfx();
                    }
                }
                // Menü offen: kurzes Loslassen derselben Taste schließt,
                // die ANDERE Menütaste springt zum Menü der anderen Hand.
                else if (_menuSide == -1)
                {
                    if (_input.LeftYReleasedShort) CloseMenu();
                    else if (_input.RightBReleasedShort) SwitchMenuSide(1);
                }
                else
                {
                    if (_input.RightBReleasedShort) CloseMenu();
                    else if (_input.LeftYReleasedShort) SwitchMenuSide(-1);
                }
            }

            // ---- Bedienung / Spiel ----
            if (_menuSide != 0)
            {
                HandleMenuInput();
            }
            else if (Phase == GamePhase.Playing)
            {
                GameplayUpdate();
            }
        }

        private void LateUpdate()
        {
            // „Raum folgt Spieler" (Tiefen-Folge) anwenden, BEVOR der Raum verankert wird.
            UpdateRoomFollow();
            // Den Spielraum verankern — ausdrücklich NICHT das XR-Rig: Kamera und
            // deren Eltern-Transform gehören dem PICO-Tracking und dürfen nicht
            // überschrieben werden (siehe Datei-Header).
            ApplyRoomAnchor();

            // Lightshow-Uhr + (nur Modus „Animiert") Ring-/Laser-Animation.
            if (_environment != null) _environment.Tick(ScoreTime, 0f);

            if (Phase == GamePhase.Playing && _menuSide == 0)
                GeometricHitUpdate();

            // Arc-Interaktion (Original `SliderInteractionManager`): Bogen folgt der
            // Saber-Klinge (Haptik + Partikel + Intensität). Unabhängig vom Treffer-Test.
            if (Phase == GamePhase.Playing && _menuSide == 0 && _activeArcs.Count > 0)
                UpdateArcInteraction();

            try { UpdateLabels(); }
            catch (Exception ex) { Debug.LogWarning("[BeatSaber] Grifflabel-Update: " + ex.Message); }
        }

    }
}
