// GameManager: Hand-Menü, Spielerverwaltung, Songliste.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber
{
    public sealed partial class GameManager
    {
        // ======================================================================
        // Menü
        // ======================================================================
        private void OpenMenu(int side)
        {
            if (_menu == null) return;
            _phaseBeforeMenu = Phase;
            _inPlayersMenu = false;
            _awaitingName = false;
            _confirmingDelete = false;
            _confirmingSongDelete = false;
            _pendingSongDeleteIndex = -1;
            _songListOpen = false;
            _menuInputLockUntil = 0f;
            _allowNewPlayer = true; // neue Menü-Öffnung => wieder ein „+ Neuer Spieler" erlaubt
            _pendingDelete = null;

            Phase = GamePhase.Menu;
            _menuSide = side;
            AutoRetractSabersForMenu(); // beide Schwerter einfahren (nur Cursor bleibt sichtbar)

            if (_phaseBeforeMenu == GamePhase.Playing)
            {
                _audio.PauseSong();
                Time.timeScale = 0f;
            }
            // Run-and-Play-Kopplung für die Menüdauer lösen (Rate 1); die Menü-Pause bleibt.
            if (_save.RunAndPlay) _audio.ClearRunPlay();
            SetSaberTrails(false); // sonst "malt" der Trail bei eingefrorener Zeit in die Luft
            Fx.StopAllContacts(); // Kontakt-Funken (Wand/Clash) beim Menü-Eintritt ausklingen lassen

            var anchor = side == -1 ? _leftAnchor : _rightAnchor;
            if (anchor != null) _menu.SetAnchor(anchor);

            BuildMenuContent();
            _audio.PlayUiSfx();
        }

        private void CloseMenu()
        {
            if (_menu != null) _menu.Hide();
            _menuSide = 0;
            _inPlayersMenu = false;
            _awaitingName = false;
            _confirmingDelete = false;
            _confirmingSongDelete = false;
            _pendingSongDeleteIndex = -1;
            _pendingDelete = null;
            DismissKeyboard(); // hängende OS-Tastatur zwingend schließen
            _keyboard = null;
            _songListOpen = false;

            RestoreSabersAfterMenu(); // ohne manuelle Änderung wieder ausfahren

            Time.timeScale = 1f;
            SetSaberTrails(true);

            // Im Menü wurde ein anderes Lied oder eine andere Schwierigkeit gewählt:
            // dann beim Schließen (nicht fortsetzen, sondern) neu laden und abspielen.
            if (_songSelectionQueued)
            {
                _songSelectionQueued = false;
                LoadSong(_songIndex);
                return;
            }

            if (_phaseBeforeMenu == GamePhase.Playing) _audio.ResumeSong();
            Phase = _phaseBeforeMenu == GamePhase.Boot ? GamePhase.Boot : GamePhase.Playing;
        }

        /// <summary>
        /// Menü von einer Hand zur anderen springen lassen (kurzer Druck der anderen Menütaste):
        /// bleibt offen und pausiert — nur Anker, Pointer-Hand und Ansicht wechseln.
        /// </summary>
        private void SwitchMenuSide(int newSide)
        {
            if (_menu == null || newSide == _menuSide) return;
            _menuSide = newSide;
            // Beide Schwerter sind bereits eingefahren (OpenMenu) — beim Seitenwechsel nur
            // die Pointer-Seite (Laser/Cursor) wechseln.
            var anchor = newSide == -1 ? _leftAnchor : _rightAnchor;
            if (anchor != null) _menu.SetAnchor(anchor);
            _confirmingDelete = false;
            _confirmingSongDelete = false;
            _pendingSongDeleteIndex = -1;
            _pendingDelete = null;
            if (_menu != null) _menu.HideConfirm();
            BuildMenuContent();
            _audio.PlayUiSfx();
        }

        private List<HandMenu.Row> MenuRows()
        {
            var rows = new List<HandMenu.Row>();

            // ---- Scrollbares Songlisten-Untermenü (nur die Songliste, ohne Kopfzeile) ----
            // Das AKTUELLE Lied bekommt nur den blauen Hintergrund (Hervorhebung, KEINE
            // Auswahl). Die grüne Markierung kommt ausschließlich vom Laser (Hover).
            if (_songListOpen)
            {
                for (int i = 0; i < _songs.Count; i++)
                {
                    bool cur = i == _songIndex;
                    // Voller Name (keine Kappung): Die Anzeige beschneidet auf die Zeilenbreite
                    // und lässt den Namen unter dem Laser als Laufschrift durchlaufen.
                    // Löschbar sind nur MrSaber-eigene Ordner (nie das Demo-Lied und nie der
                    // SongCore-Ordner der offiziellen Beat Saber). Solche Zeilen zeigen beim
                    // Anvisieren rechts ein „X" — ein Trigger darauf öffnet den Lösch-Dialog.
                    rows.Add(new HandMenu.Row
                    {
                        Text = _songs[i].SongName,
                        Id = IdSongBase + i,
                        Selectable = true,
                        Deletable = CanDeleteSong(_songs[i]),
                        Background = cur ? ActivePlayerBg : (Color?)null
                    });
                }
                return rows;
            }

            // ---- Namenseingabe: NUR die Eingabe-Zeilen zeigen (Vor der Titel-/Schwierigkeitszeile!) ----
            // WICHTIG: Während der Namenseingabe dürfen Titel- und Schwierigkeitszeile NICHT
            // erscheinen. Beide sind auswählbar und wurden vom Bestätigungs-Trigger sonst
            // versehentlich getroffen: statt den Namen zu übernehmen, öffnete sich die
            // Songliste bzw. die Schwierigkeit wechselte — das Namensfeld „kam wieder".
            // Die erste auswählbare Zeile ist absichtlich „Eingabe bestätigen", damit der
            // Standard-Cursor direkt darauf steht und ein Trigger ohne Zielen übernimmt.
            if (_awaitingName)
            {
                string shown = string.IsNullOrEmpty(_pendingName) ? "…" : Shorten(_pendingName, 16);
                rows.Add(new HandMenu.Row { Text = "Neuer Spieler", Id = -1, Selectable = false, Title = true });
                rows.Add(new HandMenu.Row { Text = "Name: " + shown, Id = -1, Selectable = false, FontSize = 44 });
                rows.Add(new HandMenu.Row { Text = "Eingabe bestätigen", Id = IdNew, Selectable = true, Button = true });
                rows.Add(new HandMenu.Row { Text = "Abbrechen", Id = IdClose, Selectable = true, Button = true });
                return rows;
            }

            // Titel (Klick öffnet die Songliste) + Schwierigkeit (Pfeile links/rechts)
            // — erscheinen in JEDER normalen Ansicht direkt untereinander. Beide Ansichten
            // stehen als eigene Methoden bereit, damit die Songliste die Höhe des
            // Hauptmenüs übernehmen kann (MainMenuPanelHeight).
            return _inPlayersMenu ? PlayersMenuRows() : MainMenuRows();
        }

        /// <summary>Spielerverwaltung: Titel + Schwierigkeit oben, dann Spielerliste und Aktionen.</summary>
        private List<HandMenu.Row> PlayersMenuRows()
        {
            var rows = new List<HandMenu.Row> { TitleRow(), DifficultyRow() };

            // Hier die Highscores je Spieler für DIESEN Song und DIESE Schwierigkeit.
            int i = 0;
            int bestAll = 0;
            foreach (var p in _save.Players)
                bestAll = Math.Max(bestAll, _current != null ? p.GetBest(_current.BestKey) : 0);
            foreach (var p in _save.Players)
            {
                bool isActive = p.Name == _save.SelectedPlayer;
                string topMark = (p.GetBest(_current != null ? _current.BestKey : "") == bestAll && bestAll > 0) ? " ★" : "";
                int best = _current != null ? p.GetBest(_current.BestKey) : 0;
                rows.Add(new HandMenu.Row
                {
                    Text = $"{Shorten(p.Name, 14)}{topMark}",
                    Value = best.ToString("N0"),
                    Id = 200 + i,
                    Selectable = true,
                    RightValue = true,
                    Background = isActive ? ActivePlayerBg : (Color?)null
                });
                i++;
            }

            // „Neuer Spieler": während einer laufenden Namenseingabe deaktiviert
            // (Doppel-Schutz); danach wieder wählbar, damit mehrere Spieler direkt
            // hintereinander angelegt werden können.
            rows.Add(_allowNewPlayer
                ? new HandMenu.Row { Text = "Neuer Spieler", Id = IdNew, Selectable = true, Button = true }
                : new HandMenu.Row { Text = "Neuer Spieler", Id = -1, Selectable = false, Button = true });
            // Lösch-Zeile auch dann anzeigen, wenn SelectedPlayer (aus einem alten Save)
            // leer/null sein sollte — der Name wird beim Öffnen der Bestätigung
            // notfalls aus dem ersten Profil abgeleitet (siehe ShowDeleteConfirm).
            if (_save.Players.Count > 0)
                rows.Add(new HandMenu.Row { Text = "Spieler löschen", Id = IdDelete, Selectable = true, Button = true });
            rows.Add(new HandMenu.Row { Text = "Zurück", Id = IdBack, Selectable = true, Button = true });
            return rows;
        }

        /// <summary>Höhe des Hauptmenü-Panels (für eine gleich hohe Songliste).</summary>
        private float MainMenuPanelHeight()
        {
            float h = 16f;
            foreach (var r in MainMenuRows()) h += HandMenu.HeightOf(r);
            return h;
        }

        /// <summary>Hauptmenü: Titel + Schwierigkeit, Spielerverwaltung und alle Regler.</summary>
        private List<HandMenu.Row> MainMenuRows()
        {
            var rows = new List<HandMenu.Row> { TitleRow(), DifficultyRow() };

            rows.Add(new HandMenu.Row { Text = "Spielerverwaltung", Id = IdPlayers, Selectable = true, Button = true });
            float sc = _player != null ? Mathf.Clamp(_player.Scale, GameConfig.ScaleMin, GameConfig.ScaleMax) : 1f;
            string scShow = sc.ToString("F2").Replace('.', ',');
            rows.Add(new HandMenu.Row
            {
                Text = "Skalierung",
                Value = $"{scShow}×",
                Id = IdHeight,
                Selectable = true,
                MultiControl = true
            });
            // Vertikaler Versatz ALLER Objekte (Noten/Minen/Wände) über dem Boden (persistent).
            // Der Boden bleibt; bodenständige Wände werden bis zum Boden verlängert. Der linke
            // Stick hebt/senkt dagegen das GESAMTE Feld.
            float fh = _save.FieldHeightMeters;
            string fhShow = fh.ToString("F2").Replace('.', ',');
            rows.Add(new HandMenu.Row
            {
                Text = "Feldhöhe",
                Value = $"{fhShow} m",
                Id = IdFieldHeight,
                Selectable = true,
                MultiControl = true
            });
            // Tiefen-Folge: Raum an die Vorwärts-/Rückwärtsbewegung koppeln (Ein/Aus, persistent).
            // Ein/Aus-Zeilen sind eine einzige Interaktionsfläche im Format „Name: Status";
            // ein Trigger irgendwo auf der Zeile schaltet um (KEINE Felder links/rechts).
            // Im Run-and-Play-Modus ist die Zeile GESPERRT (die Mitnahme ist dort Pflicht).
            bool runPlay = _save.RunAndPlay;
            rows.Add(new HandMenu.Row
            {
                Text = "Raum folgt Spieler",
                Value = runPlay ? "AN (Run&Play)" : (_save.RoomFollowsPlayer ? "AN" : "AUS"),
                Id = IdRoomFollow,
                Selectable = !runPlay,
                Card = true,
                RightValue = true
            });
            // Run and Play: Spiel-/Audiogeschwindigkeit an die physische Vorwärtsbewegung
            // koppeln (global, persistent). AN erzwingt „Raum folgt Spieler".
            rows.Add(new HandMenu.Row
            {
                Text = "Run and Play",
                Value = runPlay ? "AN" : "AUS",
                Id = IdRunPlay,
                Selectable = true,
                Card = true,
                RightValue = true
            });
            // Persönlicher Multiplikator (nur im aktiven Modus): 0,2…3,0 in 0,1er-Schritten.
            if (runPlay)
            {
                float rc = _player != null
                    ? Mathf.Clamp(_player.RunComfort, GameConfig.RunComfortMin, GameConfig.RunComfortMax)
                    : GameConfig.RunComfortDefault;
                string rcShow = rc.ToString("F1").Replace('.', ',');
                rows.Add(new HandMenu.Row
                {
                    Text = "Lauf-Tempo",
                    Value = $"{rcShow}×",
                    Id = IdRunComfort,
                    Selectable = true,
                    MultiControl = true
                });
            }
            rows.Add(new HandMenu.Row
            {
                Text = "Audio-Offset",
                Value = $"{_save.SoundDelayMs:0} ms",
                Id = IdDelay,
                Selectable = true,
                MultiControl = true
            });
            rows.Add(new HandMenu.Row
            {
                Text = "Hintergrund",
                Value = BackgroundModeText(_save.BackgroundMode),
                Id = IdPassthrough,
                Selectable = true,
                Card = true,
                RightValue = true
            });
            // Debug (Session, nicht persistent): zeigt beide Tracking-Anker samt Achsen
            // und dem PICO-4-Controller-Modell aus dem SDK — Kalibrierung virtueller vs.
            // physischer Controller (siehe ControllerDebugView).
            rows.Add(new HandMenu.Row
            {
                Text = "Controller anzeigen",
                Value = ControllerDebugView.Visible ? "AN" : "AUS",
                Id = IdControllerDebug,
                Selectable = true,
                Card = true,
                RightValue = true
            });
            // Lieder aus dem Download-Ordner holt MrSaber AUTOMATISCH bei JEDEM App-Start
            // (BootSongsRoutine) — es gibt dafür bewusst KEINE Menüzeile. Während die App
            // bereits läuft, wird NICHT erneut importiert (kein Resume-Import).
            return rows;
        }

        /// <summary>Titel-Zeile: Klick öffnet das scrollbare Songlisten-Untermenü.</summary>
        private HandMenu.Row TitleRow()
        {
            string name = _current != null ? Shorten(_current.SongName, 18) : "—";
            return new HandMenu.Row
            {
                Text = name,
                Id = IdSongList,
                Selectable = true,
                Title = true,
                Height = 96f
            };
        }

        private HandMenu.Row DifficultyRow()
        {
            string diff = _current != null && _current.CurrentDifficulty != null
                ? _current.CurrentDifficulty.DisplayName : "—";
            return new HandMenu.Row
            {
                Text = "Schwierigkeit",
                Value = diff,
                Id = IdDiff,
                Selectable = true,
                MultiControl = true,
                Cycle = true
            };
        }

        private void BuildMenuContent()
        {
            if (_menu == null) return;
            if (_songListOpen)
            {
                // Songliste: endloses Fenster auf das aktuelle Lied ZENTRIEREN. Das aktuelle
                // Lied ist nur per Hintergrund hervorgehoben (nicht angewählt); die grüne
                // Markierung erscheint erst dort, wo der Laser hinzeigt.
                // Zielhöhe = Höhe des Hauptmenüs: dadurch ist die Liste GENAU so hoch wie das
                // Hauptmenü und zeigt entsprechend mehr Lieder.
                _menu.Show(MenuRows(), true, _songIndex, MainMenuPanelHeight());
                return;
            }
            _menu.Show(MenuRows());
        }

        /// <summary>
        /// Pointer-Strahl: kommt aus dem Controller der anderen Hand (Anker-Position +
        /// Vorwärtsrichtung) und zeigt auf die Tafel; ein Treffer bewegt den Cursor direkt
        /// zur gezeigten Zeile. Der Laser ist bewusst NICHT an der Schwertklinge ausgerichtet —
        /// das Schwert behält seine eigene (Referenz-)Orientierung am Controller-Anker.
        /// </summary>
        private void UpdateMenuPointer()
        {
            if (_menu == null || _menuSide == 0) return;
            Transform pointer = _menuSide == -1 ? _rightAnchor : _leftAnchor;
            if (pointer == null) return;
            _menu.UpdatePointer(pointer.position, pointer.forward);
        }

        private void HandleMenuInput()
        {
            UpdateMenuPointer();

            // ---- X im Menü: nächstes Lied VORMERKEN, das Menü aber OFFEN lassen ----
            // Das Lied wird erst beim Verlassen des Menüs neu (an)gespielt. X springt damit
            // wie gewohnt ein Lied weiter, schließt das Menü aber nicht mehr.
            if (_menuSide != 0 && _input.LeftXWasPressedThisFrame
                && !_inPlayersMenu && !_awaitingName && !_songListOpen)
            {
                if (_songs.Count > 0)
                {
                    _songIndex = (_songIndex + 1) % _songs.Count;
                    _current = _songs[_songIndex];
                    _songSelectionQueued = true; // beim Schließen neu laden + starten
                    BuildMenuContent();
                    _audio.PlayUiSfx();
                }
                return;
            }

            // A im Menü: Raum unter Head-Set stellen und in Blickrichtung drehen (Y bleibt)
            if (_input.RightAWasPressedThisFrame) RecenterRoomOnHead();

            bool act = _menuSide == -1
                ? _input.RightMainTriggerWasPressedThisFrame
                : _input.LeftMainTriggerWasPressedThisFrame;

            // ---- Modale Lösch-Bestätigung: Das Fenster der Spielerverwaltung bleibt sichtbar,
            //      der Dialog liegt mittig darüber. Der Trigger bestätigt nur die Auswahl im
            //      Dialog (Ja/Nein); die Zeilen darunter sind eingefroren. ----
            if (_confirmingDelete)
            {
                if (act && Time.unscaledTime >= _menuInputLockUntil)
                {
                    if (_menu != null && _menu.ConfirmVisible)
                    {
                        if (_menu.ConfirmChoice == 0)
                        {
                            // „Ja": je nach Dialog ein LIED oder einen SPIELER löschen.
                            if (_confirmingSongDelete) ExecuteSongDelete();
                            else ExecuteDelete();
                        }
                        else if (_confirmingSongDelete) CancelSongDelete(); // „Nein"
                        else CancelDeletePopup();                          // „Nein"
                    }
                    _audio.PlayUiSfx();
                }
                PollKeyboard();
                return;
            }

            if (act)
            {
                if (Time.unscaledTime >= _menuInputLockUntil)
                {
                    // Während die OS-Tastatur offen ist, gehört der Trigger der Tastatur:
                    // NICHT das Menü auslösen. Sonst wird beim Tippen z. B. „Eingabe
                    // bestätigen" mitausgelöst und der Name ist leer. Bestätigt wird über
                    // die Tastatur (Done) oder die Menüzeile, NACHDEM die Tastatur zu ist.
                    if (!(_awaitingName && KeyboardOffen()))
                    {
                        // Songliste: Steht der Laser direkt auf dem Lösch-„X" der anvisierten
                        // Zeile, öffnet der Trigger den Lösch-Dialog statt das Lied zu wählen.
                        // Sonst wird die Zeile unter dem Laser ausgewählt (Songliste wird NUR
                        // über die Joysticks geblättert).
                        if (_songListOpen && _menu.PointerOnSongDeleteIcon)
                        {
                            ShowSongDeleteConfirm();
                        }
                        else
                        {
                            int id = _menu.CurrentRowId();
                            OnMenuAction(id);
                        }
                        _audio.PlayUiSfx();
                    }
                }
            }

            // Joystick bewegt die Songliste um einen Eintrag nach oben/unten (gedrückt
            // halten = wiederholen). Die Liste ist endlos: am Ende geht es vorne weiter.
            if (_songListOpen && _menu.Visible)
            {
                // Beide Joy-Cons dürfen blättern — unabhängig davon, auf welcher Seite
                // das Menü aktiv ist. Es zählt der stärker ausgelenkte der beiden Sticks.
                float stickL = _input.LeftThumbY;
                float stickR = _input.RightThumbY;
                float stick = Mathf.Abs(stickL) >= Mathf.Abs(stickR) ? stickL : stickR;
                int dir = stick > 0.5f ? -1 : (stick < -0.5f ? 1 : 0);
                if (dir != 0)
                {
                    if (dir != _stickScrollDir)
                    {
                        _stickScrollDir = dir;
                        _stickScrollTimer = 0f;
                        _menu.ScrollList(dir);
                        _audio.PlayUiSfx();
                    }
                    else
                    {
                        _stickScrollTimer += Time.unscaledDeltaTime;
                        if (_stickScrollTimer >= 0.24f)
                        {
                            _stickScrollTimer = 0f;
                            _menu.ScrollList(dir);
                        }
                    }
                }
                else
                {
                    _stickScrollDir = 0;
                    _stickScrollTimer = 0f;
                }
            }

            PollKeyboard();
        }

        private void OnMenuAction(int id)
        {
            if (_songListOpen)
            {
                if (id >= IdSongBase && id < IdSongBase + _songs.Count)
                {
                    int idx = id - IdSongBase;
                    if (idx >= 0 && idx < _songs.Count)
                    {
                        // Jede Auswahl — auch des bereits geladenen Lieds — startet das Lied
                        // beim Schließen des Menüs neu. „Keine Auswahl" = weiterlaufen lassen.
                        _songIndex = idx;
                        _current = _songs[idx];
                        _songSelectionQueued = true; // beim Schließen des Menüs neu laden
                    }
                    _songListOpen = false;
                    LockMenuInput();
                    BuildMenuContent();
                }
                return;
            }

            // Klick auf den Songtitel => scrollbares Songlisten-Untermenü
            // (funktioniert im Haupt- UND im Spielerverwaltungsmenü)
            if (id == IdSongList)
            {
                _songListOpen = true;
                LockMenuInput();
                BuildMenuContent();
                return;
            }

            if (id == IdDiff)
            {
                ChangeDifficulty();
                BuildMenuContent();
                return;
            }

            if (id < 0 && !_awaitingName) return;

            if (_awaitingName)
            {
                if (id == IdNew) TryCommitName();
                else if (id == IdClose) CancelName();
                return;
            }

            // Spieler-Zeile (gilt in der Spielerverwaltung):
            // aktiven Spieler wechseln und in der aktuellen Ansicht bleiben (blaue Markierung).
            if (id >= 200 && id < 250)
            {
                int idx = id - 200;
                if (idx >= 0 && idx < _save.Players.Count)
                {
                    _save.SelectedPlayer = _save.Players[idx].Name;
                    _player = _save.Players[idx];
                    ApplyRoomScale();
                    _pendingDelete = null;
                    Save();
                    _songSelectionQueued = true; // Spielerwechsel startet das aktuelle Lied neu
                }
                LockMenuInput();
                BuildMenuContent();
                return;
            }

            if (_inPlayersMenu)
            {
                if (id == IdNew)
                {
                    if (_allowNewPlayer)
                    {
                        BeginNewPlayer();
                        LockMenuInput();
                    }
                }
                else if (id == IdDelete)
                {
                    ShowDeleteConfirm(); // öffnet den modalen Dialog (Ja/Nein) über der Liste
                    return;
                }
                else if (id == IdBack)
                {
                    _inPlayersMenu = false;
                    LockMenuInput();
                }
                BuildMenuContent();
                return;
            }

            switch (id)
            {
                case IdPlayers:
                    _inPlayersMenu = true;
                    _pendingDelete = null;
                    LockMenuInput();
                    BuildMenuContent();
                    break;
                // Einzelne (−)/(+)-Zeile: linker Strahl = Minus, rechter Strahl = Plus
                case IdHeight:
                    if (_player != null)
                    {
                        bool hPlus = _menu != null && _menu.LastPointerX >= 0f;
                        _player.Scale = Mathf.Clamp(
                            _player.Scale + (hPlus ? GameConfig.ScaleStep : -GameConfig.ScaleStep),
                            GameConfig.ScaleMin, GameConfig.ScaleMax);
                        ApplyRoomScale(); // Live: gesamter Raum + Schwerter skalieren sofort
                        Save();
                    }
                    BuildMenuContent();
                    break;
                // Feldhöhe: linker Strahl = Minus, rechter Strahl = Plus (persistent).
                case IdFieldHeight:
                    {
                        bool fPlus = _menu != null && _menu.LastPointerX >= 0f;
                        _save.FieldHeightMeters = Mathf.Clamp(
                            _save.FieldHeightMeters + (fPlus ? GameConfig.FieldHeightStepMeters : -GameConfig.FieldHeightStepMeters),
                            GameConfig.FieldHeightMinMeters, GameConfig.FieldHeightMaxMeters);
                        ApplyFieldHeight();
                        Save();
                        BuildMenuContent();
                    }
                    break;
                // Tiefen-Folge: Raum folgt der Vor-/Rückwärtsbewegung des Spielers (Ein/Aus).
                // Ganze Zeile ist die Interaktionsfläche -> ein Trigger schaltet um.
                // Im Run-and-Play-Modus gesperrt (die Mitnahme ist dort Pflicht).
                case IdRoomFollow:
                    if (_save.RunAndPlay) break;
                    _save.RoomFollowsPlayer = !_save.RoomFollowsPlayer;
                    Save();
                    BuildMenuContent();
                    break;
                // Run and Play ein-/ausschalten (erzwingt/sparrt „Raum folgt Spieler").
                case IdRunPlay:
                    SetRunAndPlay(!_save.RunAndPlay);
                    break;
                // Persönlicher Lauf-Tempo-Multiplikator (linker Strahl = −, rechter = +).
                case IdRunComfort:
                    if (_player != null)
                    {
                        bool cPlus = _menu != null && _menu.LastPointerX >= 0f;
                        _player.RunComfort = Mathf.Clamp(
                            _player.RunComfort + (cPlus ? GameConfig.RunComfortStep : -GameConfig.RunComfortStep),
                            GameConfig.RunComfortMin, GameConfig.RunComfortMax);
                        Save();
                    }
                    BuildMenuContent();
                    break;
                // Audio-Offset (IdDelay = 108): linker Strahl = Minus, rechter = Plus. Positiv =
                // Musik (hörbar) später, negativ = Noten-Uhr später (Kompensation; ein
                // Audiosignal kann nicht in die Zukunft verschoben werden). 0 = der
                // getunte Standardstand.
                case IdDelay:
                    {
                        bool dPlus = _menu != null && _menu.LastPointerX >= 0f;
                        _save.SoundDelayMs = Mathf.Clamp(
                            _save.SoundDelayMs + (dPlus ? GameConfig.AudioOffsetStepMs : -GameConfig.AudioOffsetStepMs),
                            GameConfig.AudioOffsetMinMs, GameConfig.AudioOffsetMaxMs);
                        Save();
                        if (_audio != null) _audio.SetAudioOffset(_save.SoundDelayMs / 1000f);
                        BuildMenuContent();
                    }
                    break;
                case IdPassthrough:
                    // Hintergrund-Option zyklisch weiterschalten (persistiert) und sofort
                    // anwenden. Ganze Zeile = Interaktionsfläche -> ein Trigger schaltet um.
                    // PicoSeeThrough respektiert den daraus abgeleiteten Seethrough-Zustand
                    // auch bei Watchdog/Resume/Fokus (aus bleibt aus).
                    _save.BackgroundMode = (_save.BackgroundMode + 1) % 3;
                    ApplyBackgroundSetting();
                    Save();
                    BuildMenuContent();
                    break;
                // Debug-Controller ein-/ausblenden (Session, NICHT persistent): sichtbare
                // Tracking-Nullpunkte + PICO-4-Modell zum Abgleich mit dem echten Controller.
                case IdControllerDebug:
                    ControllerDebugView.Toggle();
                    BuildMenuContent();
                    break;
            }
        }

        /// <summary>
        /// Schwierigkeit wechseln (nur Standard-Characteristic; Sondermodi sind nie geladen).
        /// Die Wahl wird global persistiert und beim Schließen des Menüs angewendet.
        /// </summary>
        private void ChangeDifficulty()
        {
            if (_current == null || _current.Difficulties.Count <= 1) return;
            bool dPlus = _menu != null && _menu.LastPointerX >= 0f;
            int n = _current.Difficulties.Count;
            _current.SelectedDifficulty = dPlus
                ? (_current.SelectedDifficulty + 1) % n
                : (_current.SelectedDifficulty - 1 + n) % n;
            _current.ApplySelectedDifficulty();
            _songSelectionQueued = true; // beim Schließen wird mit der neuen Chart neu geladen
            SaveDifficultyChoice();
        }

        /// <summary>
        /// Kurz-Sperre nach Ansichtswechseln: verhindert, dass ein zweiter Tastendruck
        /// sofort eine jetzt anders positionierte Zeile auslöst (z. B. doppelte Namenseingabe).
        /// Werte-Zeilen (Größe/Verzögerung/Schwierigkeit) bleiben davon unberührt (schnelles Klicken).
        /// </summary>
        private void LockMenuInput() => _menuInputLockUntil = Time.unscaledTime + 0.4f;

        /// <summary>
        /// Wendet die gespeicherte „Feldhöhe" (<see cref="SaveData.FieldHeightMeters"/>) als
        /// vertikalen Versatz ALLER Objekte über dem Spielfeld-Boden an (NoteView/WallView). Der
        /// Boden bleibt; bodenständige Wände werden bis zum Boden verlängert. Die Treffer-/Score-
        /// Berechnung liest dieselbe Position (Notenbox = Anzeige).
        /// </summary>
        private void ApplyFieldHeight()
        {
            GameConfig.FieldHeightOffsetMeters = _save != null ? _save.FieldHeightMeters : 0f;
        }

        /// <summary>Kurztext eines Hintergrund-Modus für die Menüzeile.</summary>
        private static string BackgroundModeText(int mode)
        {
            switch ((Environment.BackgroundMode)Mathf.Clamp(mode, 0, 2))
            {
                case Environment.BackgroundMode.Default: return "Default";
                case Environment.BackgroundMode.Animated: return "Animiert";
                default: return "Passthrough";
            }
        }

        /// <summary>
        /// Wendet den gespeicherten Audio-Offset auf die Musik an (Start + nach Laden).
        /// Siehe AudioManager.SetAudioOffset / AudioDelayFilter (DSP-Delay der Musik).
        /// </summary>
        private void RefreshAudioOffset()
        {
            if (_audio != null) _audio.SetAudioOffset(_save.SoundDelayMs / 1000f);
        }

        /// <summary>
        /// Übernimmt die persistente Hintergrund-Option (Passthrough / Default / Animiert):
        /// Passthrough = Video-Seethrough an; Default/Animiert = Seethrough aus, Kamera
        /// opak und die ArcViewer-Default-Umgebung sichtbar (Animiert spielt zusätzlich die
        /// Song-Lightshow). Wird beim Start und bei jeder Menü-Umschaltung aufgerufen;
        /// PicoSeeThrough wendet den Seethrough-Zustand auch nach Session-/Resume-/Fokus-
        /// Wechseln erneut an.
        /// </summary>
        private void ApplyBackgroundSetting()
        {
            var mode = (Environment.BackgroundMode)Mathf.Clamp(_save.BackgroundMode, 0, 2);
            bool seethrough = mode == Environment.BackgroundMode.Passthrough;

            // Seethrough-Kette (Kamera + PXR) — steuert auch den Kamera-Hintergrund.
            PICO.PicoSeeThrough.SetEnabled(seethrough);

            if (_environment == null) return;
            // Zuerst die Song-Umgebung wählen (setzt Parameter + gewünschte Szene),
            // dann den Modus anwenden (lädt die Szene nur bei Default/Animiert).
            if (mode != Environment.BackgroundMode.Passthrough && _current != null)
                _environment.SetEnvironment(_current.EnvironmentName, _toSeconds);
            _environment.ApplyMode(mode);
            // Beim Moduswechsel die Lightshow der aktuellen Map neu übergeben
            // (Default → ruhig, Animiert → Events der gewählten Difficulty).
            if (_current != null && _toSeconds != null && _chart != null)
                _environment.LoadLightshow(_chart, _current, _toSeconds);
        }

        // ---- Spielerverwaltung (Bildschirmtastatur) ----
        private void BeginNewPlayer()
        {
            // Doppel-Schutz: sofort verriegeln — selbst ein zweiter Aufruf in derselben
            // Menü-Öffnung (z. B. durch hängende Tastatur-Events) kann nichts mehr öffnen.
            if (!_allowNewPlayer) return;
            _allowNewPlayer = false;
            _awaitingName = true;
            _keyboardWasVisible = false;
            _pendingName = "";
            _shownName = "";
            _keyboard = TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default, false,
                false, false, false, "Spielername");
            _keyboardStatusLog = -1;
            Debug.Log($"[BeatSaber] Namenseingabe gestartet — Tastatur {( _keyboard == null ? "NULL (nicht geöffnet)" : "geöffnet")}.");
            BuildMenuContent();
        }

        /// <summary>true, solange die OS-Tastatur sichtbar/aktiv ist (dann gehört der Trigger ihr).</summary>
        private bool KeyboardOffen()
        {
            return _keyboard != null &&
                   (_keyboard.active || _keyboard.status == TouchScreenKeyboard.Status.Visible);
        }

        private void CancelName()
        {
            if (!_awaitingName) return;
            DismissKeyboard(); // hängende OS-Tastatur zwingend schließen
            _awaitingName = false;
            _keyboard = null;
            _shownName = "";
            _allowNewPlayer = true; // weitere Spieler direkt hintereinander möglich
            LockMenuInput();
            BuildMenuContent();
        }

        /// <summary>OS-Tastatur zwangsweise schließen — verhindert, dass nach dem Committen ein
        /// „zweiter Eingabeprompt" hängen bleibt und weiter getippt werden kann.
        /// (Unity 2022.3: 'active' ist eine Instanz-Eigenschaft; false beendet die Eingabe.)
        /// Bewusst OHNE „active"-Prüfung: auf PICO/Android kann 'active' bereits false melden,
        /// während die Tastatur noch angezeigt wird — der Aufruf ist dann gefahrlos.</summary>
        private void DismissKeyboard()
        {
            try
            {
                if (_keyboard != null) _keyboard.active = false;
            }
            catch { /* Plattform ohne Unterstützung: ignorieren */ }
        }

        private void PollKeyboard()
        {
            if (_keyboard == null) return;

            // Diagnose: Status-/Textwechsel protokollieren (PICO meldet „Done"/„LostFocus"
            // teils verzögert oder gar nicht — damit ist der Ablauf im Log nachvollziehbar).
            int status = (int)_keyboard.status;
            if (status != _keyboardStatusLog)
            {
                _keyboardStatusLog = status;
                Debug.Log($"[BeatSaber] Tastatur-Status={_keyboard.status}, aktiv={_keyboard.active}, " +
                          $"sichtbar={_keyboardWasVisible}, text='{_keyboard.text}'.");
            }

            // Text JEDEN Frame mitschreiben (nicht nur im Status „Visible"): Android/PICO
            // hängt beim Status teils hinterher. So geht der getippte Name nie verloren und
            // die Live-Vorschau („Name: …") bleibt aktuell.
            string typed = _keyboard.text;
            if (!string.IsNullOrEmpty(typed) && typed != _pendingName)
            {
                _pendingName = typed;
                _keyboardWasVisible = true;
                if (_awaitingName && typed != _shownName)
                {
                    _shownName = typed;
                    BuildMenuContent(); // Vorschauzeile aktualisieren
                }
            }
            if (_keyboard.status == TouchScreenKeyboard.Status.Visible || _keyboard.active)
                _keyboardWasVisible = true;

            switch (_keyboard.status)
            {
                case TouchScreenKeyboard.Status.Done:
                    TryCommitName();
                    return;
                case TouchScreenKeyboard.Status.LostFocus:
                    // PICO/Android schließt die Tastatur beim Bestätigen teils mit „LostFocus"
                    // statt „Done". Nur als Bestätigung werten, wenn die Tastatur zuvor wirklich
                    // offen war ODER bereits Text getippt wurde — so entsteht kein Fehl-Commit
                    // direkt nach dem Öffnen.
                    if (_keyboardWasVisible || !string.IsNullOrEmpty(_pendingName))
                        TryCommitName();
                    return;
                case TouchScreenKeyboard.Status.Canceled:
                    CancelName();
                    return;
            }

            // Kein Done/Canceled/LostFocus, aber die Tastatur war sichtbar und ist jetzt
            // geschlossen: als Bestätigung werten (PICO schließt teils ohne klaren Status).
            if (_keyboardWasVisible && !_keyboard.active)
                TryCommitName();
        }

        private void TryCommitName()
        {
            // Guard: Keyboard-Done und Trigger auf „Eingabe bestätigen" können im selben
            // Augenblick landen — nur der erste Commit zählt (sonst entstünde „Spieler N+1").
            if (!_awaitingName) return;

            // Namen VOR dem Schließen der Tastatur lesen: 'text' ist beim Done-Status bzw.
            // nach dem Ausblenden oft leer, daher auf den gepufferten Tipp-Text zurückfallen.
            string typed = _keyboard != null && !string.IsNullOrEmpty(_keyboard.text)
                ? _keyboard.text
                : _pendingName;
            string name = (typed ?? "").Trim();

            Debug.Log($"[BeatSaber] Name übernehmen: keyboardText='{(_keyboard != null ? _keyboard.text : "")}', " +
                      $"pending='{_pendingName}', status={(_keyboard != null ? _keyboard.status.ToString() : "-")}.");

            DismissKeyboard(); // Tastatur zwingend schließen, damit keine zweite Eingabe folgt
            _keyboard = null;
            _awaitingName = false;
            _shownName = "";
            _allowNewPlayer = true; // weitere Spieler direkt hintereinander möglich

            if (string.IsNullOrEmpty(name)) name = "Spieler " + (_save.Players.Count + 1);

            foreach (var p in _save.Players)
            {
                if (p.Name == name)
                {
                    _save.SelectedPlayer = p.Name;
                    _player = p;
                    ApplyRoomScale();
                    Save();
                    _songSelectionQueued = true; // aktiver Spieler gewechselt -> Lied neu starten
                    _inPlayersMenu = true; // zurück zur Spielerliste (Name dort sichtbar)
                    LockMenuInput();
                    BuildMenuContent();
                    return;
                }
            }

            var fresh = new PlayerProfile { Name = name };
            _save.Players.Add(fresh);
            _save.SelectedPlayer = name;
            _player = fresh;
            ApplyRoomScale();
            Save();
            _songSelectionQueued = true; // neuer aktiver Spieler -> Lied neu starten
            _inPlayersMenu = true; // zurück zur Spielerliste (neuer Spieler sichtbar)
            LockMenuInput();
            BuildMenuContent();
        }

        /// <summary>
        /// Öffnet die Lösch-Bestätigung als MODALEN Dialog MITTIG über der Spielerverwaltung.
        /// Das aktuelle Fenster BLEIBT sichtbar (es wird nicht durch die Bestätigungszeilen
        /// ersetzt); nur der Dialog fängt den Trigger ab. „Ja"/„Nein" werden über dieselbe
        /// Laser-Markierung wie das übrige Menü gewählt. Zuvor wird eine evtl. noch offene
        /// Bildschirmtastatur geschlossen, damit sie den Dialog nicht verdeckt.
        /// </summary>
        private void ShowDeleteConfirm()
        {
            _pendingDelete = _save.SelectedPlayer;
            // Absicherung gegen alte Saves, in denen SelectedPlayer leer ist: dann den
            // ersten vorhandenen Spieler zur Bestätigung heranziehen.
            if (string.IsNullOrEmpty(_pendingDelete) && _save.Players.Count > 0)
                _pendingDelete = _save.Players[0].Name;
            _confirmingDelete = true;
            DismissKeyboard(); // keine hängende OS-Tastatur über dem Dialog
            LockMenuInput();
            Debug.Log($"[BeatSaber] Lösch-Dialog öffnen für '{_pendingDelete}'.");
            if (_menu != null) _menu.ShowConfirm("Spieler löschen?", Shorten(_pendingDelete, 18));
        }

        /// <summary>
        /// Bricht die Lösch-Bestätigung ab und kehrt zur Spielerverwaltung zurück (das Fenster
        /// ist ohnehin nie verschwunden — hier wird nur der Dialog wieder ausgeblendet).
        /// </summary>
        private void CancelDeletePopup()
        {
            _confirmingDelete = false;
            _confirmingSongDelete = false;
            _pendingDelete = null;
            if (_menu != null) _menu.HideConfirm();
            LockMenuInput();
        }

        /// <summary>Löscht den in der Bestätigung freigegebenen Spieler; bleibt danach in der Spielerverwaltung.</summary>
        private void ExecuteDelete()
        {
            string name = _pendingDelete;
            _pendingDelete = null;
            _confirmingDelete = false;
            if (_menu != null) _menu.HideConfirm();

            _save.Players.RemoveAll(p => p.Name == name);
            if (_save.Players.Count == 0)
            {
                _save.Players.Add(new PlayerProfile { Name = "Spieler 1" });
            }
            _save.SelectedPlayer = _save.Players[0].Name;
            _player = _save.Players[0];
            ApplyRoomScale();
            Save();
            _songSelectionQueued = true; // Löschen des aktiven Spielers startet das Lied neu
            LockMenuInput();
            BuildMenuContent();
            // In der Spielerverwaltung bleiben (kein Wechsel ins Hauptmenü).
        }

    }
}
