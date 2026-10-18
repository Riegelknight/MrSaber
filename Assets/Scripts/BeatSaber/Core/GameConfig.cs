using UnityEngine;

namespace BeatSaber
{
    /// <summary>
    /// Zentrale, anpassbare Spiel-Konstanten (Geschwindigkeiten, Raster, Einstellungen).
    /// Raster, Offsets und Sprung-Timing stammen 1:1 aus dem Referenz-Viewer ArcViewer.
    /// </summary>
    public static class GameConfig
    {
        // ---------- Layers (siehe ProjectSettings/TagManager.asset) ----------
        public const int NoteLayer = 7;
        public const int SaberLayer = 8;
        public const int WallLayer = 9;

        // ---------- Entsorgung hinter dem Spieler ----------
        /// <summary>Entsorgungslinie hinter der Schnittebene (Basis-Einheiten): Sobald eine
        /// Note/Wand diesen Wert hinter sich lässt, wird sie eingesammelt (Grundlage der
        /// „verpasst"-Meldung). Bewusst nur die Rückseite der Szene, kein Positions-Offset.</summary>
        public const float DestroyZ = -5f;

        public const float NoteMissZ = -2f;

        // ---------- Grid (Grundlage aus ArcViewer: ObjectManager + Szenen-Container) ----------
        // Die Reihenhöhen sind ABSOLUTE lokale Y-Werte. Es gibt KEINEN Container-Offset
        // und keine Kopplung an die Head-Set-Höhe.
        /// <summary>Linke Kante des Rasters in X (ArcViewer ObjectManager.GridBottomLeft).</summary>
        public const float GridLeftX = -0.9f;
        /// <summary>Spaltenabstand: 4 Spalten à 0.6 m (x = -0.9..0.9).</summary>
        public const float LaneWidth = 0.6f;
        /// <summary>Wandhöhe je Rastereinheit (ArcViewer ObjectManager.WallHScale).</summary>
        public const float WallHScale = 0.6f;
        /// <summary>Vertikaler Versatz des Wand-Containers (ArcViewer-Szene: Container „Walls"
        /// y = 0.1). Die Wand-Mitte wird aus Rasterzeile × WallHScale + Höhe/2 gebildet; dieser
        /// Versatz kommt zusätzlich dazu (WallManager.UpdateVisual).</summary>
        public const float WallContainerOffsetY = 0.1f;
        /// <summary>Kleinste Wandgröße (ArcViewer WallManager.MinWallSize, Meter).</summary>
        public const float MinWallSize = 0.06f;
        public static readonly float[] RowHeights = { 0.85f, 1.40f, 1.95f };

        // ---------- „Feldhöhe" (vertikaler Versatz für alles außer dem Boden) ----------
        // Einstellbar über die Menüzeile „Feldhöhe" (persistent in SaveData.FieldHeightMeters).
        // Hebt Noten/Minen UND alle Wände über den Spielfeld-Boden (Noten in NoteView, Wände in
        // WallView; jeden Frame → live). Der Boden bleibt; bodenständige Wände werden nach unten bis
        // zum Boden verlängert. Das GESAMTE Spielfeld (inkl. Wände) hebt/senkt dagegen der linke
        // Stick (GameManager._floorOffsetMeters).
        /// <summary>Vertikaler Versatz aller Objekte über dem Spielfeld-Boden (Meter).</summary>
        public static float FieldHeightOffsetMeters = 0f;
        /// <summary>Schrittweite der Menüzeile „Feldhöhe" (Meter).</summary>
        public const float FieldHeightStepMeters = 0.05f;
        /// <summary>Untere/obere Grenze der „Feldhöhe" (Meter).</summary>
        public const float FieldHeightMinMeters = -1.5f;
        public const float FieldHeightMaxMeters = 1.5f;

        // ---------- Sprung-/Spawn-Timing (1:1 aus ArcViewer: JumpManager/BeatmapManager) ----------
        /// <summary>Default-HalfJumpDuration in Beats (ArcViewer BeatmapManager.DefaultHJD).</summary>
        public const float HalfJumpDurationDefault = 4f;
        /// <summary>Grenzwert der Sprungdistanz (Meter): Der Default-HJD wird halbiert, bis
        /// die Distanz darunter liegt (ArcViewer: `GetJumpDistance > 35.998`).</summary>
        public const float MaxJumpDistance = 35.998f;
        /// <summary>Untergrenze der HalfJumpDuration nach dem Map-Offset (Beats).</summary>
        public const float MinHalfJumpDuration = 0.25f;

        // ---------- Noten-Bewegungs-EFFEKTE aus dem Muster (ArcViewer/SuperSaber) ----------
        // NICHT jede Note bekommt einen Effekt: Normale Noten fliegen gerade. Nur wenn das
        // Muster es vorgibt (mehrere Noten gleicher Zeit/Spalte = Stapel; überkreuzte
        // Doppel = rot rechts/blau links), ändert die Note ihre Position beim Anflug.
        // 1:1 ArcViewer (ObjectManager.GetStartY/GetFlipYHeights + NoteManager.UpdateVisual).
        /// <summary>Höhenabstand gestapelter Noten beim Spawn (ArcViewer `StartYSpacing`).</summary>
        public const float StackSpacing = 0.6f;
        /// <summary>Seiten-Flip: vertikaler Versatz der „oberen" Note (ArcViewer `flipYHeightOver`).</summary>
        public const float FlipYOver = 0.45f;
        /// <summary>Seiten-Flip: vertikaler Versatz der „unteren" Note (ArcViewer `flipYHeightUnder`).</summary>
        public const float FlipYUnder = -0.15f;

        /// <summary>
        /// Spawn-Parabel der Stapel-Animation (ArcViewer `JumpManager.SpawnParabola`): Die
        /// Note startet auf <paramref name="baseHeight"/> (unter ihrer Reihe) und erreicht
        /// <paramref name="targetHeight"/> genau beim Beat, wenn t (Z-Position) auf 0 läuft.
        /// Für baseHeight == targetHeight ist das Ergebnis konstant die Zielhöhe — normale
        /// Noten fliegen dadurch gerade (kein Effekt).
        /// </summary>
        public static float SpawnParabola(float targetHeight, float baseHeight, float halfJumpDistance, float t)
        {
            float d = halfJumpDistance > 0.0001f ? halfJumpDistance : 0.0001f;
            float range = targetHeight - baseHeight;
            return -(range / (d * d)) * (t * t) + targetHeight;
        }

        /// <summary>NJS-Vorgabe je Schwierigkeitsrang (1..5), wenn die Map keinen NJS
        /// angibt — ArcViewer BeatmapManager: Easy/Normal/Hard 10, Expert 12, Expert+ 16.</summary>
        public static float DefaultNjsForRank(int rank)
        {
            switch (rank)
            {
                case 4: return 12f;
                case 5: return 16f;
                default: return 10f;
            }
        }

        /// <summary>
        /// Z-Position einer Note/eines Wand-Vorderrands zur Liedzeit — KONTINUIERLICHER
        /// Flug mit dem Map-Tempo (NJS aus den Song-Daten). Beim Beat ist Z exakt 0;
        /// vor dem Beat positiv (weit weg), danach negativ (hinter dem Spieler).
        /// Es gibt bewusst KEINE schnelle Anflug-Phase mehr (kein MoveZ/MoveTime):
        /// Die Noten fliegen gleichmäßig, allein durch NJS/HJD der Map bestimmt.
        /// </summary>
        public static float JumpZ(float hitTime, float songTime, float njs)
        {
            return (hitTime - songTime) * njs;
        }

        /// <summary>Zeit zwischen Spawn und Beat (Sekunden): die volle Sprungdauer
        /// (2 × ReactionTime), damit die Note genau auf der Beat-Position ankommt.</summary>
        public static float SpawnLeadTime(float reactionTime) => 2f * reactionTime;

        // ---------- Raum-Skalierung (EINZIGE Größen-Einstellung) ----------
        /// <summary>Untere/obere Grenze der Raum-Skalierung (1 = Referenzgröße).</summary>
        public const float ScaleMin = 0.5f;
        public const float ScaleMax = 2f;
        /// <summary>Schrittweite im Menü („Skalierung").</summary>
        public const float ScaleStep = 0.05f;
        /// <summary>Bezugsgröße für die Migration alter Saves: 1,75 m ≙ Skalierung 1,0.</summary>
        public const float ScaleBaseHeightCm = 175f;

        public static float HeightCmToScale(float heightCm)
        {
            if (heightCm <= 1f) return 1f;
            return Mathf.Clamp(heightCm / ScaleBaseHeightCm, ScaleMin, ScaleMax);
        }

        // ---------- Raum-Ausrichtung ----------
        // Rechter Stick links/rechts = Raum-Gierwinkel; linker Stick hoch/runter =
        // Session-Bodenversatz. Beides nur, solange die Songliste NICHT offen ist.
        // Der Bodenversatz ist SESSION-only: startet bei jedem Spielstart bei 0, wird NICHT
        // gespeichert und von Taste A NICHT angefasst (A setzt nur Raum-X/Z unter den Kopf
        // und die Drehung auf die Blickrichtung — die Höhe bleibt, siehe RecenterRoomOnHead).
        /// <summary>Drehgeschwindigkeit des Raums bei voller Stick-Auslenkung (Grad/Sekunde).</summary>
        public const float RoomYawDegPerSecond = 90f;
        /// <summary>Hub-/Senkgeschwindigkeit des Session-Bodenversatzes bei voller
        /// Stick-Auslenkung (Meter/Sekunde).</summary>
        public const float FloorOffsetMetersPerSecond = 1.2f;
        /// <summary>Totzone des rechten Sticks für die Raum-Drehung.</summary>
        public const float RoomAdjustDeadzone = 0.15f;
        /// <summary>Totzone des LINKEN Sticks für den Bodenversatz — bewusst GRÖSSER, damit
        /// ein versehentliches/leichtes Auslenken nichts verschiebt.</summary>
        public const float FloorOffsetDeadzone = 0.35f;

        // ---------- „Run and Play" (Lauf-/Tempokopplung) ----------
        // Optionaler Modus: Die Spiel- UND Audiogeschwindigkeit wird an die physische
        // Vorwärtsbewegung des Raums gekoppelt. „Raum folgt Spieler" ist dann zwangsweise
        // an und gesperrt. Formel (siehe GameManager/`RunAndPlay`):
        //     playRate = v_puffer × songFactor × comfort
        //     songFactor = 1 / (_roomScale × NJS)
        // Bei comfort = 1 sind die Noten EXAKT statisch im echten Raum (der Spieler läuft
        // auf sie zu), für jedes Lied und jede Raum-Skalierung; der Faktor fällt mit der
        // Notengeschwindigkeit (schnellere Songs = niedrigerer Multiplikator).
        /// <summary>Totzone der Vorwärtsgeschwindigkeit (m/s): darunter gilt „Stillstand"
        /// und der Song pausiert vollständig (Noten frieren ein).</summary>
        public const float RunPlayStandstillSpeed = 0.12f;
        /// <summary>Pufferfenster (Sekunden) für den geglätteten Durchschnitt der
        /// Raumgeschwindigkeit (Konzept: 0,3 s) — verhindert abrupte Tempowechsel.</summary>
        public const float RunPlaySpeedWindow = 0.3f;
        public const float RunComfortMin = 0.2f;
        public const float RunComfortMax = 3.0f;
        public const float RunComfortDefault = 1.0f;
        /// <summary>Schrittweite des Menü-Reglers „Lauf-Tempo" (0,1er-Schritte).</summary>
        public const float RunComfortStep = 0.1f;
        /// <summary>Grenzen der tatsächlichen Wiedergaberate (AudioSource.pitch). Die
        /// Untergrenze hält den Ton knapp hörbar; die Obergrenze ist das Unity-pitch-Limit.</summary>
        public const float RunPlayMinRate = 0.05f;
        public const float RunPlayMaxRate = 3.0f;
        /// <summary>Totzone (Meter) für die Raum-Folge im Run-and-Play-Modus: darunter
        /// „friert" auch der Raum ein, damit Stillstand wirklich still ist.</summary>
        public const float RunPlayDeadzoneMeters = 0.002f;

        // ---------- Schnitt-Einstellungen ----------
        // Richtungs-Regel nach BeatSaberClone (`BoxView.CheckSliceDirection`) und
        // OpenSaberPlus (`BeepCube.cut`): Der Winkel zwischen der geglätteten
        // Schwertspitzen-Geschwindigkeit (`SaberView.TipVelocity`, 5-Frame-Mittel an der
        // SPITZE — bei Handgelenksschwüngen bewegt sich die Wurzel kaum) und der
        // ZIELRICHTUNG muss unter `CutAllowedAngle` bleiben, sonst Bad Cut. Zielrichtung = die sichtbar
        // angezeigte Pfeilrichtung der Note (lokal −Y; die Note trägt die
        // Anzeigen-Rotation `NoteView.TargetRotationZ` — der Score-Layer liest exakt
        // dieselbe Drehung wie das Auge, die 45°-Diagonalen ergeben sich daraus von
        // selbst). WIE OPEN-SABER-PLUS wird die TIEFE WEGPROJIZIERT (nur das lokale x/y
        // der Bewegung zählt): der Anteil IN die Note hinein darf nicht bestraft werden.
        // Dot-Noten (8/9): jede Richtung gültig. Bewegung unter `CutMinSpeed` =
        // Berührung ohne Schnitt (Note bleibt am Leben). Farbe (Hand) wird separat geprüft.
        /// <summary>Maximaler Winkel (Grad) zwischen Schwert-Geschwindigkeit und
        /// Zielrichtung, ab dem ein Hieb als FALSCH gilt (Bad Cut).</summary>
        /// <remarks>
        /// 90° = Vorzeichen-Regel wie in der Referenz (Beat Saber `noteCutDirection` ·
        /// `saberSwingDirection` > 0; SuperSaber `strokeDirection[cutDirection]` = reine
        /// Vorzeichen-Abfrage der Zugrichtung). Damit ist die FALSCHE SEITE (Gegenrichtung,
        /// &gt; 90° zur Pfeilrichtung) immer ein Bad Cut, ein nur leicht schräg geführter
        /// Hieb aber nicht: Der frühere Wert 45° erzeugte auf dem Gerät gehäuft Bad Cuts
        /// (im Log 41 von 58) bei 46–90° — genau an der Schwelle abgelesener, in Wahrheit
        /// gültiger Schnitte, die als „MISS" angezeigt wurden.
        /// </remarks>
        public const float CutAllowedAngle = 90f;
        /// <summary>Untergrenze der Schwert-Geschwindigkeit (m/s, Welten), unter der eine
        /// Kollision als „Berührung ohne Schnitt" (Bad Cut) gilt. Die Clone-Referenz hat
        /// hier keine Schranke (ihr Kegel würde bei velocity≈0 beliebig); 0,15 m/s ist
        /// Rausch-Abstand, deutlich unter jedem echten Hieb.</summary>
        public const float CutMinSpeed = 0.15f;
        public const float SlicePieceSpeed = 2.2f;
        /// <summary>Eigendrehung (Taumeln) der Schnitt-Hälften in rad/s (leicht zufällig).</summary>
        public const float SlicePieceSpin = 6f;
        public const float DestroyDelayTime = 0.3f;   // Lebensdauer der Schnitt-Hälften (Sekunden)
        public const float NoteSize = 0.5f;

        public const float SaberBladeRadius = 0.017f;
        public const float HitBoxPadding = SaberBladeRadius;
        public const float HitGateForward = 1.7f;
        // Diagnose: [Hit]/[Miss]/[Ignore]-Zeilen in logcat (pro Treffer). Für den nächsten
        // Geräte-Test AN; danach wieder aus (etwas Log-Overhead).
        public static readonly bool LogHitDiagnostics = true;

        // ---------- Controller-Haptik (Vibrations-Feedback) ----------
        // Relative Intensitäten: leicht < Noten < Minen. Alle Werte hier zentral tunebar.
        // Hinweis: Amplituden bewusst deutlich über der Wahrnehmungsschwelle — kleine Werte
        // (< ~0,2) werden auf der PICO 4 kaum spürbar übertragen. Schwert↔Note ist die
        // wichtigste Rückmeldung (jeder Schnitt) und daher am stärksten ausgelegt (0,95).
        /// <summary>LEICHT: Schwert↔Schwert und Schwert↔Wand (während des Kontakts).</summary>
        public const float HapticLightAmplitude = 0.35f;
        /// <summary>NOTENBLÖCKE: deutlich stärker als „leicht" (Treffer-Primärfeedback).</summary>
        public const float HapticNoteAmplitude = 0.95f;
        public const float HapticNoteDuration = 0.14f;
        /// <summary>MINEN (Bomben): kräftig + kurze Nachvibration.</summary>
        public const float HapticMineAmplitude = 1.0f;
        public const float HapticMineDuration = 0.20f;
        public const float HapticMineAfterDelay = 0.10f;    // Abstand bis zur Nachvibration
        public const float HapticMineAfterAmplitude = 0.60f;
        public const float HapticMineAfterDuration = 0.10f;
        /// <summary>Kontakt-Vibration (Schwert↔Schwert/Wand): Impulsdauer und Mindestabstand zweier Impulse.</summary>
        public const float HapticContactPulseDuration = 0.10f;
        public const float HapticContactInterval = 0.06f;

        /// <summary>Basis-Kompensation der Score-/Noten-Uhr gegenüber der Audio-Uhr (ms).
        /// Sie verzögert die NOTEN relativ zur Musik (Gegenstück zum Audio-Offset, der die
        /// MUSIK verschiebt). Synchron ist das Spiel, wenn
        /// <c>AudioOffset(ms) == AudioLatencyBaseMs</c> ist — beide Seiten verschieben ihre
        /// Quelle um denselben Betrag, <see cref="AudioOffsetBaseMs"/> (beide gemeinsam)
        /// kürzt sich dabei heraus. Der Regler „Audio-Offset" startet deshalb bei 0, und die
        /// am PICO 4 kalibrierten 85 ms stecken hier (nicht mehr als harter Offset):
        /// früher Base 190 + Offset −85 ⇒ 190 − (−85) = 275.</summary>
        public const float AudioLatencyBaseMs = 275f;

        public const float AudioOffsetMinMs = -300f;
        /// <summary>Größtmöglicher Audio-Offset (ms).</summary>
        public const float AudioOffsetMaxMs = 300f;
        /// <summary>Schrittweite der Menüzeile pro (−)/(+)-Trigger (ms).</summary>
        public const float AudioOffsetStepMs = 5f;
        public const float AudioOffsetBaseMs = 300f;

        public const int SparkCount = 18;
        /// <summary>Emissionskegel (Grad) um die Schwungrichtung.</summary>
        public const float SparkSpreadAngle = 22f;
        /// <summary>Startgeschwindigkeit der Funken (m/s, × Raum-Skalierung).</summary>
        public const float SparkSpeedMin = 1.4f, SparkSpeedMax = 2.6f;
        /// <summary>Lebensdauer der Funken (s).</summary>
        public const float SparkLifetimeMin = 0.2f, SparkLifetimeMax = 0.4f;
        /// <summary>Partikelgröße (m) — kleine, helle Streaks.</summary>
        public const float SparkSizeMin = 0.006f, SparkSizeMax = 0.010f;
        /// <summary>Gravitation der Funken (1 = Erdbeschleunigung; bewusst fast schwerelos).</summary>
        public const float SparkGravity = 0.15f;
        /// <summary>HDR-Boost der Funkenfarbe (damit der URP-Bloom sie glühen lässt).</summary>
        public const float SparkColorBoost = 2.5f;
        public const float SparkForwardBias = 0.42f;

        public const float WallSparkRate = 180f;
        public const float ContactSparkLifetimeMin = 0.25f, ContactSparkLifetimeMax = 0.55f;
        /// <summary>Startgeschwindigkeit der Kontakt-Funken (m/s, × Raum-Skalierung).</summary>
        public const float ContactSparkSpeedMin = 1.2f, ContactSparkSpeedMax = 2.6f;
        public const float ContactSparkSizeMin = 0.005f, ContactSparkSizeMax = 0.009f;
        /// <summary>Gravitation der Kontakt-Funken (1 = Erde): zieht sie nach dem radialen Sprühen nach unten.</summary>
        public const float ContactSparkGravity = 1.5f;

        public const float MineColorBoost = 2.2f;
        /// <summary>Funkenfarbe ROT (HDR: Notenfarbe × <see cref="SparkColorBoost"/>) —
        /// übersteuert 1,0, damit der URP-Bloom die Streaks glühen lässt.</summary>
        public static readonly Color SparkColorRed = new Color(0.7843f, 0.0784f, 0.0784f, 1f) * SparkColorBoost;
        /// <summary>Funkenfarbe BLAU (HDR, wie oben).</summary>
        public static readonly Color SparkColorBlue = new Color(0.1569f, 0.5569f, 0.8235f, 1f) * SparkColorBoost;
        /// <summary>Minen-Detonation: warmes HDR-Weiß als Grundtint; die Glut-Stufen
        /// (weiß → gelb → orange → rot) erzeugt der Volumen-Shader in
        /// <c>Fx.CreateMineFireball</c>.</summary>
        public static readonly Color MineFlashColor = new Color(1f, 0.95f, 0.9f, 1f) * MineColorBoost;
        /// <summary>Schwert-Clash (Schwert auf Schwert): weiße Funken-Fontäne (HDR, Bloom).</summary>
        public static readonly Color ClashSparkColor = Color.white * SparkColorBoost;

        // Punktverteilung = Original-Beat-Saber-Schema: 70 Anlauf + 30 Durchzug + 15 Mitte
        // = 115 Grundpunkte je Note. GEMESSEN wird aber wie in OpenSaberPlus (`LightSaber.gd`,
        // ±200-ms-Fenster der KLINGENRICHTUNG) statt über SuperSabers gesättigte Ebenen-Winkel
        // — deshalb zählen gerade und diagonale Schnitte gleicher Qualität gleich.
        public const float PreSwingMax = 70f;         // Anlauf
        public const float FollowThroughMax = 30f;    // Durchzug
        public const float CenterMax = 15f;           // Mitte (exakt durch die Notenmitte)
        /// <summary>Anlauf: volle Punkte ab 100° Änderung der Klingenrichtung
        /// (OpenSaberPlus `TARGET_PRESWING_ANGLE`).</summary>
        public const float PreSwingFullAngle = 100f;
        /// <summary>Durchzug: volle Punkte ab 60° (OpenSaberPlus `TARGET_FOLLOWTHROUGH_ANGLE`).</summary>
        public const float FollowThroughFullAngle = 60f;
        /// <summary>Messfenster vor/nach dem SCHNITTMOMENT (Klingenkontakt), in dem die größte
        /// Änderung der Klingenrichtung gezählt wird (OpenSaberPlus `preswing_time`/
        /// `followthrough_time` = 200 ms). Gemessen relativ zur Klinge im Schnittmoment.</summary>
        public const float SwingMeasureWindow = 0.2f;
        /// <summary>Abstand Notenmitte ↔ Klingenlinie (Basis-Einheiten), ab dem der
        /// Mitte-Anteil 0 wird (davor linear voll → 0). Verhältnis 0,3 × Notengröße
        /// (Beat Saber `cutDistanceToCenter`).</summary>
        public const float CenterDistanceMax = NoteSize * 0.3f;

        /// <summary>Glied einer Kette: fester Grundwert 20 (× Combo-Multiplikator), ohne
        /// Winkel-/Mitte-Bewertung (ArcViewer `ScoringUtils.MaxChainLinkScore`).</summary>
        public const float ChainLinkScore = 20f;

        // ---------- Arcs & Chains — Optik (1:1 ArcViewer ArcManager/ChainManager) ----------
        /// <summary>Breite des Bogen-Bandes in Basis-Einheiten (ArcViewer `arcwidth` 0.5 / 2).</summary>
        public const float ArcWidth = 0.25f;
        /// <summary>Punkte pro Sekunde Bogenlänge (ArcViewer `arcdensity` = 60), begrenzt auf
        /// sinnvolle Ober-/Untergrenzen.</summary>
        public const int ArcSegmentsPerSecond = 60;
        public const int ArcMinPoints = 30;
        public const int ArcMaxPoints = 300;
        /// <summary>Abstand der Bézier-Kontrollpunkte vom Kopf-/Endpunkt bei Multiplizierer 1
        /// (ArcViewer `defaultControlOffset` = 2.5).</summary>
        public const float ArcControlOffset = 2.5f;
        public const float ArcEndFadeStartMeters = 0f;
        public const float ArcEndFadeMeters = 0.35f;
        /// <summary>Scroll-Geschwindigkeit der Bogen-Textur (ArcViewer `arcAnimationSpeed` = 0.75).</summary>
        public const float ArcAnimationSpeed = 0.75f;
        /// <summary>FALLBACK-Kantenlänge eines Chain-Glieds (nur wenn das Referenz-Mesh
        /// ChainLink.fbx fehlt). Mit dem Referenz-Mesh (0,50 × 0,09 × 0,50, globalScale 1)
        /// wird die Trefferbox aus den World-Bounds des Renderers genommen — exakt ArcViewer.</summary>
        public const float ChainLinkSize = 0.22f;
        /// <summary>Abstand, um den ein Bogen-Endpunkt zur anliegenden Notenmitte verschoben
        /// wird, wenn dort eine Note/Bombe sitzt (ArcViewer `halfNoteOffset` = 0.225).</summary>
        public const float ArcAttachOffset = 0.225f;

        // ---------- Arc-Interaktion (Original `SliderInteractionManager`) ----------
        // Der Bogen reagiert auf die Saber-Klinge: folgt sie dem Band, leuchtet es stärker
        // (Haptik + Spitzen-Partikel), verlässt sie es, blendet der Bogen aus. Score geben
        // weiterhin NUR Kopf-/Endnote (wie im Original).
        /// <summary>Fangradius um die Bogen-Kurve (Meter, × Raum-Skalierung), innerhalb dessen
        /// die Klinge als „im Bogen" gilt.</summary>
        public const float ArcInteractRadius = 0.20f;
        /// <summary>Maximale Helligkeits-Erhöhung des Bandes bei voller Interaktion (1 = +100 %).</summary>
        public const float ArcInteractIntensityBoost = 0.9f;
        /// <summary>Rest-Helligkeit eines verlassenen Bogens (blendet auf diesen Wert aus).</summary>
        public const float ArcInteractFadedIntensity = 0.15f;
        /// <summary>Fade-Geschwindigkeit (Einheiten/s) für den Interaktions-Übergang.</summary>
        public const float ArcInteractFadeSpeed = 5f;

        // ---------- Multiplikator (1:1 wie Original/ArcViewer, ScoringUtils.cs) ----------
        // Stufen x1, x2, x4, x8; je Stufe sind HitsNeededForComboIncrease Treffer nötig
        // (x1->x2 nach 2, x2->x4 nach 4, x4->x8 nach 8 Treffern). Die Note, die eine Stufe
        // erreicht, zählt bereits mit der NEUEN Stufe. Ein Fehler halbiert die Stufe
        // (x8->x4->x2->x1, int/2) und setzt den Fortschritt zurück — er springt NICHT auf x1
        // (Ausnahme: Wand-Kopftreffer, bewusste eigene Regel).
        public static readonly int[] ComboMultipliers = { 1, 2, 4, 8 };
        public static readonly int[] HitsNeededForComboIncrease = { 2, 4, 8 };

        // ---------- MR-Toleranz (nur die 4 erlaubten Kernfunktionen) ----------
        // (2) Unsichtbare Trefferbox der Noten — 1:1 die Referenz-KOLLISIONSBOX.
        //     BeatSaber-Web-Replays `beat.js toBigBox` und OpenSaberPlus
        //     `BeepCube.tscn` (CollisionBig) sind identisch: 0,80 × 0,50 × 1,00
        //     (Dot: 0,80 hoch; Chain: 0,20 hoch); die Referenzen setzen sie um +0,25 zur
        //     Spielerseite — hier bewusst NICHT mehr. Die Score-Zone liegt exakt auf der
        //     Note (Versatz 0, Position (0, 0)): Die Box ist um die Notenmitte zentriert,
        //     Noten und Lichteffekte des Lieds (beide an der Beat-Zeit bei Z=0) bleiben
        //     dadurch perfekt synchron. Der Spieler steht stattdessen um
        //     PlayerDistanceBehindZone hinter der Zone.
        //     Die Punktewertung (Anlauf/Durchzug + Mitte) wird NICHT beim ersten Box-Kontakt
        //     abgelesen (der liegt wegen der größeren Box oft am Rand), sondern im Moment,
        //     in dem die Note die Score-Zone (Z=0) erreicht (siehe GameManager.ScoreAtScoreZoneAsync).
        //     HINWEIS: Die Box bleibt fest an der Note (eine Ebene, kein Bug-Using) —
        //     KEIN dynamisches Vorziehen/Verformen. Geprüft wird sie mit dem Klingen-
        //     SEGMENT (Segment/Box-Slab-Test, siehe GameManager.TryGeometricHit) — nicht
        //     mit der aufgeblasenen Schwung-AABB.
        public const float NoteHitboxWidth = 0.8f;
        public const float NoteHitboxHeight = 0.5f;
        public const float NoteHitboxHeightDot = 0.8f;
        public const float NoteHitboxHeightChain = 0.2f;
        public const float NoteHitboxDepth = 1.0f;
        /// <summary>Abstand (Meter bei Skalierung 1) zwischen Spieler und Score-Zone/Ebene Z=0.
        /// Der Spieler steht damit HINTER der Zone (Raum-Z = −Wert), statt auf ihr. Früher 0,65
        /// (ArcViewer `PlayerCutPlaneDistance`) — auf 0,30 reduziert, weil sich die Noten mit
        /// 0,65 m zu weit weg anfühlten (nicht mehr bequem erreichbar). Skaliert mit der
        /// Raum-Skalierung (Schwert und Noten wachsen ebenfalls mit).</summary>
        public const float PlayerDistanceBehindZone = 0.3f;
        // (3) Musik-Tiefpass bei Kopf↔Wand-Kontakt (Hz) — sofort dumpf „unter Wasser".
        public const float WallMuffleCutoffHz = 400f;
        // (4) Wand-Brutzeln am Schwert: Zufalls-Impulsrate (Hz) und Amplitude.
        public const float WallSizzleHapticMinHz = 40f;
        public const float WallSizzleHapticMaxHz = 80f;
        public const float WallSizzleHapticAmplitude = 0.6f;
        public const float WallSizzleVolume = 0.18f;
        // CCD: Anzahl interpolierter Sweep-Posen zwischen Vorframe und Jetzt-Frame
        // (Sweep-Test gegen schnelles Durchglitschen).
        public const int SaberSweepSamples = 4;

        public const string DemoFolderName = "Death to Disco";
        public const string DemoSongName = "Death to Disco";
        public const string DemoSongAuthor = "by Freeek";
        public const float DemoBpm = 115f;
        /// <summary>Resource-Ordner des eingebauten Lieds; zugleich Präfix der Beatmap-JSONs.</summary>
        public const string DemoMapResourcePrefix = "BeatSaberDemo/DeathToDisco/";
        /// <summary>info.json des eingebauten Lieds in Resources.</summary>
        public const string DemoInfoResource = DemoMapResourcePrefix + "info";
        /// <summary>Audio des eingebauten Lieds in Resources — Load-Name OHNE Endung.</summary>
        public const string DemoTrackResource = DemoMapResourcePrefix + "DeathToDisco";
        /// <summary>Dateiname derselben Audio (nur Anzeige/Info; die Wiedergabe lädt
        /// <see cref="DemoTrackResource"/>).</summary>
        public const string DemoTrackFileName = "DeathToDisco.ogg";
        /// <summary>Nur noch für die Migration uralter Demo-Highscores (Key ohne Difficulty)
        /// und als Notfall-Schwierigkeit, falls die Demo-info nicht lesbar ist.</summary>
        public const string DemoDifficultyName = "Expert";

        // ---------- Soundeffekte (Referenz BeatSaberClone) ----------
        // EIN einziger Slices-Clip (Slice.ogg) bedient zwei völlig unterschiedlich
        // GEKLOPFTE Einsätze: den Notentreffer (direkt, dumpf, leise hörbar) und den
        // Schalter-Klick des Easter Eggs (vorne abgeschnitten, beschleunigt, hell).
        // Die Klangfärbung entsteht zur LAUFZEIT über Tiefpass (`AudioLowPassFilter`)
        // + `AudioSource.pitch` + Startversatz (`timeSamples`) — je ein eigener
        // AudioSource, siehe `AudioManager`. Alles hier ist ohne Code-Änderung tunebar;
        // ein anderer Klang = einfach den Resource-Pfad bzw. die Zahlenwerte tauschen.

        /// <summary>Slice-Sound der Referenz (`Resources/Slice.ogg`) — Quelle beider Effekte.</summary>
        public const string SliceSfxResource = "BeatSaberDemo/Slice";

        public const float HitSfxVolume = 0.20f;
        /// <summary>Tonhöhe des Schnitt-Sounds — &lt; 1 macht tiefer/schwerer.</summary>
        public const float HitSfxPitch = 0.85f;
        /// <summary>Tiefpass-Grenzfrequenz in Hz — je kleiner, desto dumpfer. Bereich 10…22000.
        /// 1000 statt 850: dumpf bleibt, aber der Ton verliert nicht seine Substanz.</summary>
        public const float HitSfxLowpassHz = 1000f;
        public const float HitSfxLowpassQ = 1.0f;

        // --- Schwert aus-/einfahren (Easter Egg): vorne abgeschnitten + beschleunigt, hell ---
        public const float SaberToggleSfxVolume = 0.25f;
        /// <summary>Beschleunigung — &gt; 1 macht schneller UND höher (wirkt direkter).</summary>
        public const float SaberToggleSfxPitch = 1.9f;
        /// <summary>Wie viele Millisekunden am ANFANG des Slice-Sounds übersprungen werden.</summary>
        public const float SaberToggleSfxSkipMs = 45f;
        /// <summary>Tiefpass des Schalter-Klicks. 4000 statt 1800 Hz — der Klick war dadurch
        /// „zu dunkel" und klang verstopft; hell genug, um als Klick zu lesen.</summary>
        public const float SaberToggleLowpassHz = 4000f;
        public const float SaberToggleLowpassQ = 1.0f;

        /// <summary>UI-Klick (unverändert).</summary>
        public const string UISfxResource = "BeatSaberDemo/UI_Confirm";
        public const float UISfxVolume = 0.20f;

        // ---------- Partikel (Referenz BeatSaberClone: BoxSpawner + ObjectSlicer) ----------
        // Beide Effekte werden statisch GECACHT und GEPOOLT (wie das Referenz-ParticleEffectHandler-
        // Pooling: Capacity 4 / Max 8 je Effekt) — kein Resources.Load und keine Instantiate-
        // Allokation pro Treffer/Spawn (siehe PROJECT_NOTES §10).
        /// <summary>Spawn-Partikel der Notenblöcke: Rot (links/`_type 0`) bzw. Blau.</summary>
        public const string NoteSpawnParticleRedResource = "BeatSaberDemo/Particles/ParticleSpawnL";
        public const string NoteSpawnParticleBlueResource = "BeatSaberDemo/Particles/ParticleSpawnR";
        public static readonly Quaternion ParticleRotation = Quaternion.Euler(-90f, 0f, 0f);
        /// <summary>Obergrenze des Pools je Effekt (Referenz `ParticleEffectSettings`: 8).
        /// Darüber hinausgehende Instanzen werden beim Zurückgeben zerstört.</summary>
        public const int ParticlePoolMaxSize = 8;

        public static readonly Color WallColor = new Color(0.80f, 0.90f, 1.00f, 1f);
        public const float WallOpacity = 0.20f;
        public const float WallLightness = 1f;

        // ---------- Spieler-Laufzeitpfad ----------
        // Song-Quelle: der SongCore-Ordner der OFFIZIELLEN Beat Saber auf der PICO
        // (`ModData/.../SongCore/CustomLevels`) — derselbe Liedbestand wird von beiden
        // Spielen genutzt. Der Ordner liegt AUSSERHALB der App-Sandbox, daher braucht
        // Android die externe Speicher-Berechtigung (GameManager.EnsureStoragePermission).
        public const string ExternalSongsPath =
            "/storage/emulated/0/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels";
        /// <summary>Song-Quelle 2: der MrSaber-eigene Liedordner auf der PICO
        /// (`ModData/MrSaber/CustomLevels`). Dort landen Lieder, die NUR MrSaber nutzen soll —
        /// so bleiben sie aus dem Custom-Songs-Bestand der offiziellen Beat Saber heraus.
        /// MrSaber führt beide Ordner zusammen (siehe BeatmapParser.LoadSongList).</summary>
        public const string MrSaberSongsPath =
            "/storage/emulated/0/ModData/MrSaber/CustomLevels";
        public static string SongsRootPath =>
            System.IO.Path.Combine(Application.persistentDataPath, "Songs");
        /// <summary>Lied-Ordner: 1) SongCore (offizielle Beat Saber), 2) MrSaber-eigener
        /// Ordner, 3) app-eigener Songs-Ordner. MrSaber liest ALLE vorhandenen Ordner und
        /// führt sie zusammen (Dubletten werden aussortiert).</summary>
        public static string[] SongsRootCandidates =>
            new[] { ExternalSongsPath, MrSaberSongsPath, SongsRootPath };
        /// <summary>Download-Ordner der Brille: dorthin laden Browser/PC Lied-ZIPs und
        /// Lied-Ordner. MrSaber holt sie AUTOMATISCH bei JEDEM App-Start selbst ab und
        /// verschiebt/entpackt sie in den eigenen Liedordner (siehe SongInbox) — es gibt
        /// dafür keinen Menüpunkt, und während die App läuft wird nicht erneut importiert.</summary>
        public const string DownloadPath = "/storage/emulated/0/Download";

        /// <summary>true, wenn ein Liedordner von MrSaber verwaltet wird und deswegen
        /// GELÖSCHT werden darf: der eigene MrSaber-Ordner oder der app-eigene Songs-Ordner.
        /// Der SongCore-Ordner der offiziellen Beat Saber bleibt bewusst tabu (gemeinsamer
        /// Liedbestand beider Spiele).</summary>
        public static bool IsDeletableSongFolder(string folderPath)
            => IsUnder(folderPath, MrSaberSongsPath) || IsUnder(folderPath, SongsRootPath);

        /// <summary>Liegt <paramref name="path"/> in <paramref name="root"/> (oder ist es selbst)?</summary>
        private static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            string p = path.Replace('\\', '/').TrimEnd('/');
            string r = root.Replace('\\', '/').TrimEnd('/');
            return p.Equals(r, System.StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(r + "/", System.StringComparison.OrdinalIgnoreCase);
        }

        public static string SaveRootPath =>
            System.IO.Path.Combine(Application.persistentDataPath, "BeatSaberSave");
        public static string ProfilesPath =>
            System.IO.Path.Combine(SaveRootPath, "Profiles.json");

        // ---------- Grid-Hilfsfunktionen ----------
        /// <summary>
        /// Position eines Rasterfelds (lineIndex 0..3, lineLayer 0..2) in lokalen
        /// Basis-Einheiten des Raum-Roots. Y ist die ABSOLUTE Reihenhöhe (RowHeights); der
        /// einstellbare „Feldhöhen"-Versatz (<see cref="FieldHeightOffsetMeters"/>) wird
        /// jeden Frame in NoteView/WallView addiert (so wirkt er auch live). Die Skalierung
        /// macht der Root; Z aus dem Spawn-Timing.
        /// </summary>
        public static Vector3 GridPosition(int lineIndex, int lineLayer)
        {
            int lane = Mathf.Clamp(lineIndex, 0, 3);
            int row = Mathf.Clamp(lineLayer, 0, RowHeights.Length - 1);
            float x = GridLeftX + lane * LaneWidth;
            float y = RowHeights[row];
            return new Vector3(x, y, 0f);
        }

        /// <summary>Pfeilrichtung als Z-Gradwinkel — 1:1 ArcViewer `ObjectManager.DirectionAngles`
        /// (0:180, 1:0, 2:-90, 3:90, 4:-135, 5:135, 6:-45, 7:45, 8:0 = Dot).</summary>
        public static float NoteAngleDegrees(int cutDirection)
        {
            switch (Mathf.Clamp(cutDirection, 0, 8))
            {
                case 0: return 180f;
                case 1: return 0f;
                case 2: return -90f;
                case 3: return 90f;
                case 4: return -135f;
                case 5: return 135f;
                case 6: return -45f;
                case 7: return 45f;
                default: return 0f;
            }
        }

        /// <summary>Blickrichtungsvektor eines Winkels in der XY-Ebene — 1:1 ArcViewer
        /// (`ObjectManager.DirectionVector`: (sin θ, −cos θ)).</summary>
        public static Vector2 DirectionVector(float angleDegrees)
        {
            float r = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), -Mathf.Cos(r));
        }

        /// <summary>
        /// Vertikale Ausdehnung eines Hindernisses in Basis-Einheiten: Unterkante und Höhe —
        /// 1:1 ArcViewers Wand-Konstruktor (Rastereinheit 0.6 m, WallHScale, Höhe auf
        /// 5 - layer begrenzt, Mindestgröße MinWallSize). v3/v4 nutzen die Map-Werte
        /// (<c>_lineLayer</c>/<c>_height</c>); v2 leitet beides aus <c>_type</c> ab
        /// (0 = volle Boden-Wand 5 Einheiten, 1 = schwebende Kauer-Wand ab Reihe 2 mit 3).
        /// Liefert die ROH-Werte. Die „Feldhöhe" (<see cref="FieldHeightOffsetMeters"/>) verschiebt
        /// alles nach oben; bodenständige Wände werden in WallView bis zum Boden verlängert.
        /// </summary>
        public static void ObstacleBounds(ObstacleModel obs, out float bottom, out float height)
        {
            float layer, layers;
            if (obs._height >= 1)
            {
                layer = Mathf.Clamp(obs._lineLayer, 0, 4);
                layers = Mathf.Min(obs._height, 5f - layer);
            }
            else
            {
                // v2-Fallback: Kauer-Wand = schwebend ab Zeile 2 (Höhe 3), sonst volle Wand (5).
                bool crouch = obs._type == 1;
                layer = crouch ? 2f : 0f;
                layers = crouch ? 3f : 5f;
            }
            // Roh-Werte; den „Feldhöhen"-Versatz (nur für Wände ohne Bodenkontakt) addiert
            // WallView jeden Frame, damit die Änderung live wirkt.
            bottom = layer * WallHScale;
            height = Mathf.Max(MinWallSize, layers * WallHScale);
        }
    }
}
