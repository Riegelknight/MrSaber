using System;
using System.Collections;
using UnityEngine;

namespace BeatSaber
{
    /// <summary>
    /// Spielt das aktuelle Lied (Demo aus Resources, echte Songs von der Brille via
    /// UnityWebRequest). Liefert die FESTE Score-Uhr (feste Referenz für Hitboxen/Saber)
    /// und die Darstellungs-Uhr (Score + Noten-Offset); der Audio-Ebenen-Offset verschiebt
    /// nur die Musikwiedergabe. Dazu kleine Soundeffekte (ohne Offset).
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        private AudioSource _music;
        // DSP-Verzögerungskeller der Musik (Audio-Offset) — liegt mit der Quelle auf dem
        // Kind-Objekt [Music], damit ausschließlich die Musik verzögert wird.
        private AudioDelayFilter _delay;

        // Drei getrennte Effekt-Quellen, weil die beiden Einsätze des Slice-Clips
        // unterschiedlich GEKLOPPT werden (Tiefpass + Pitch gelten pro Quelle):
        //   _sfx    — UI-Klick, ungefiltert (Originalton)
        //   _hit    — Schwert↔Note: dumpf, tiefer, leise (direktes Treffergefühl)
        //   _toggle — Schwert aus-/einfahren: VORDERER ABSCHNITT, beschleunigt, dumpfer
        // Jede Effekt-Quelle liegt auf einem EIGENEN Kind-Objekt: ein AudioLowPassFilter
        // wirkt auf ALLE AudioSources desselben GameObjects — ohne Trennung würde der
        // dumpfe Schnitt-Ton auch Musik und UI-Klick mitfiltern.
        private AudioSource _sfx;
        private AudioSource _hit;
        private AudioSource _toggle;
        // (3) Musik-Tiefpass bei Kopf↔Wand-Kontakt; (4) Brutzel-Quelle.
        private AudioLowPassFilter _wallMuffle;
        private AudioSource _sizzle;

        /// <summary>Aktueller Audio-Offset in Sekunden (Menü „Audio-Offset", −300…+300).
        /// Verschiebt AUSSCHLIESSLICH die hörbare Musik — die Noten-Uhr bewegt sich NIE
        /// (die Basis-Verzögerung der Delay-Line gleicht den Spielraum nach unten aus).
        /// Standard = 0: es gibt KEINEN harten Offset mehr — die am PICO 4 kalibrierten
        /// 85 ms stecken in der Noten-Uhr (<see cref="GameConfig.AudioLatencyBaseMs"/>).
        /// Synchron ist das Spiel bei Offset == AudioLatencyBaseMs.</summary>
        public float AudioOffsetSeconds { get; private set; }

        /// <summary>Setzt den Audio-Offset (Sekunden; ±300 ms im Menü, 5-ms-Schritte).
        /// Wirkt AUSSCHLIESSLICH auf die hörbare Musik (AudioDelayFilter.SetOffset);
        /// die Score-Uhr trägt die konstante Basis-Kompensation (siehe ScoreTime).
        /// Kein Seek, kein Clip-Eingriff, keine Noten-Bewegung.</summary>
        public void SetAudioOffset(float seconds)
        {
            if (seconds > GameConfig.AudioOffsetMaxMs / 1000f) seconds = GameConfig.AudioOffsetMaxMs / 1000f;
            if (seconds < GameConfig.AudioOffsetMinMs / 1000f) seconds = GameConfig.AudioOffsetMinMs / 1000f;
            AudioOffsetSeconds = seconds;
            if (_delay != null)
                _delay.SetOffset(seconds);
        }

        private bool _paused;
        // Run-and-Play: eigene Pause (Stillstand/Rückwärts) und Wiedergaberate. Beide
        // Zustände werden mit der Menü-Pause KOMBINIERT (ApplyPlaybackState) — keine
        // Ebene überschreibt die andere.
        private bool _runPaused;
        private float _runRate = 1f;

        // Nachlauf der Audio-Uhr: Läuft die Datei natürlich aus, zählt die Uhr weiter.
        // Sonst stünde sie bei "length - Delay" still und der Song-Ende-Schalter ließe
        // bei positivem Delay nie auslösen (Endlos-Hängen).
        private bool _clockPastEnd;
        private float _clockPastEndBase;
        private float _clockPastEndWall;

        private void ResetEndlessClock()
        {
            _clockPastEnd = false;
        }

        private void Awake()
        {
            // Musik auf einem EIGENEN Kind-Objekt [Music]: Dort liegen Quelle, Wand-Tiefpass
            // und der Audio-Offset-Delay-Filter. Ein Audio-Filter wirkt nur auf AudioSources
            // DESSSELBEN GameObjects — dadurch wird ausschließlich die Musik verzögert/
            // gefiltert, die SFX-Kinder (eigene Objekte) klingen unverändert sofort.
            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(transform, false);
            _music = musicGo.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = false;
            _music.spatialBlend = 0f;

            // Audio-Offset: samplegenaue Verzögerung nur der Musik (beide Richtungen).
            _delay = musicGo.AddComponent<AudioDelayFilter>();
            _delay.SetOffset(AudioOffsetSeconds);

            _sfx = MakeSfxSource("SfxUi");        // UI-Klick: ungefiltert
            _hit = MakeSfxSource("SfxHit");       // Notentreffer: dumpf
            _toggle = MakeSfxSource("SfxToggle"); // Schalter-Klick: dumpf + schnell

            AddLowpass(_hit, GameConfig.HitSfxLowpassHz, GameConfig.HitSfxLowpassQ);
            AddLowpass(_toggle, GameConfig.SaberToggleLowpassHz, GameConfig.SaberToggleLowpassQ);

            // (3) Musik-Tiefpass (auf dem [Music]-Objekt): Kopf in Wand = dumpf.
            _wallMuffle = musicGo.AddComponent<AudioLowPassFilter>();
            _wallMuffle.cutoffFrequency = GameConfig.WallMuffleCutoffHz;
            _wallMuffle.enabled = false;

            // (4) Wand-Brutzeln: räumliches, leises Dauer-Geräusch am Kontaktpunkt (der
            // Slice-Clip läuft als Schleife mit Hochpass = „elektrisches" Knistern).
            _sizzle = MakeSfxSource("SfxSizzle");
            _sizzle.spatialBlend = 1f;
            _sizzle.loop = true;
            _sizzle.volume = 0f;
            _sizzle.minDistance = 0.15f;
            _sizzle.maxDistance = 3f;
            var sizzleHp = _sizzle.gameObject.AddComponent<AudioHighPassFilter>();
            sizzleHp.cutoffFrequency = 1200f;
        }

        // ---------- (3) Kopf↔Wand-Tiefpass, (4) Brutzel, (8) Stereo-Fokus ----------

        /// <summary>(3) Musik sofort dumpf schalten, solange der Kopf in einer Wand steckt.</summary>
        public void SetWallMuffle(bool on)
        {
            if (_wallMuffle != null) _wallMuffle.enabled = on;
        }

        /// <summary>(4) Wand-Brutzeln an/aus; Quelle sitzt am Kontaktpunkt (räumlich).</summary>
        public void SetWallSizzle(bool on, Vector3 worldPos)
        {
            if (_sizzle == null) return;
            if (on)
            {
                EnsureSfxCached();
                if (_sliceSfx != null && _sizzle.clip != _sliceSfx) _sizzle.clip = _sliceSfx;
                _sizzle.transform.position = worldPos;
                if (!_sizzle.isPlaying) _sizzle.Play();
                _sizzle.volume = GameConfig.WallSizzleVolume;
            }
            else
            {
                _sizzle.volume = 0f;
            }
        }

        /// <summary>
        /// Effekt-Quelle auf einem eigenen Kind-Objekt, nicht räumlich, Quell-Lautstärke 1
        /// (die Effekt-Lautstärken stehen einzeln in GameConfig — ArcViewer spielt
        /// Hitsounds ebenfalls NICHT räumlich, Volume dort 0,5).
        /// </summary>
        private AudioSource MakeSfxSource(string childName)
        {
            var child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            var src = child.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.volume = 1f;
            return src;
        }

        /// <summary>Tiefpass auf eine Effekt-Quelle hängen (dumpfer Klang). Die
        /// Grenzfrequenz wirkt direkt — `cutoffFrequency` genügt (ein `customCutoff`-
        /// Schalter existiert in dieser Unity-API nicht).</summary>
        private static void AddLowpass(AudioSource src, float cutoffHz, float q)
        {
            var filter = src.gameObject.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = cutoffHz;
            filter.lowpassResonanceQ = q;
            filter.enabled = true;
        }

        /// <summary>Lädt und startet ein Lied (Audio-Ebene mit ihrem Offset).</summary>
        public IEnumerator PlaySong(SongMeta meta, Action onLoaded)
        {
            _music.Stop();
            _music.clip = null;
            ResetEndlessClock();
            _paused = false;
            _runPaused = false;
            _runRate = 1f;
            _music.pitch = 1f;
            if (meta == null)
            {
                onLoaded?.Invoke();
                yield break;
            }

            if (meta.IsDemo)
            {
                var clip = Resources.Load<AudioClip>(GameConfig.DemoTrackResource);
                if (clip != null)
                {
                    _music.clip = clip;
                    BeginMusic();
                }
                else
                {
                    Debug.LogError("[BeatSaber] Demo-Audio fehlt!");
                }
            }
            else if (!string.IsNullOrEmpty(meta.AudioPath))
            {
                AudioClip loaded = null;
                using (var uwr = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(
                           ToFileUri(meta.AudioPath), UnityEngine.AudioType.OGGVORBIS))
                {
                    yield return uwr.SendWebRequest();
                    if (uwr.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        loaded = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(uwr);
                        _music.clip = loaded;
                        BeginMusic();
                    }
                    else
                    {
                        Debug.LogError($"[BeatSaber] Audio laden fehlgeschlagen: {uwr.error} ({meta.AudioPath})");
                    }
                }
            }

            onLoaded?.Invoke();
        }

        /// <summary>Startet die Musik ab Position 0 (kein Seek/Offset mehr).</summary>
        private void BeginMusic()
        {
            ApplyPlaybackState();
        }

        /// <summary>
        /// Wendet die kombinierte Pause (Menü UND Run-and-Play) an und setzt die
        /// Wiedergaberate. Zentraler Punkt, damit sich die Ebenen nicht überschreiben:
        /// `PauseSong`/`ResumeSong` (Menü) und `SetRunPlayPaused`/`SetRunPlayRate`
        /// (Lauf-Kopplung) ändern nur ihren eigenen Zustand und rufen DIESE Methode.
        /// </summary>
        private void ApplyPlaybackState()
        {
            if (_music == null) return;
            _music.pitch = _runRate;
            if (_paused || _runPaused)
            {
                _music.Pause();
                return;
            }
            _music.Play();
        }

        /// <summary>Run-and-Play: Stillstand/Rückwärts pausiert den Song (Score-Uhr friert
        /// ein → Noten/Timeline stehen). Unabhängig von der Menü-Pause.</summary>
        public void SetRunPlayPaused(bool paused)
        {
            if (_runPaused == paused) return;
            _runPaused = paused;
            ApplyPlaybackState();
        }

        /// <summary>Run-and-Play: setzt die an die Laufgeschwindigkeit gekoppelte
        /// Wiedergaberate (AudioSource.pitch). Wird auf die GameConfig-Grenzen geklemmt.</summary>
        public void SetRunPlayRate(float rate)
        {
            rate = Mathf.Clamp(rate, GameConfig.RunPlayMinRate, GameConfig.RunPlayMaxRate);
            if (Mathf.Approximately(rate, _runRate)) return;
            _runRate = rate;
            if (_music != null) _music.pitch = rate;
        }

        /// <summary>Beendet die Lauf-Kopplung: Rate zurück auf 1, Lauf-Pause aufheben
        /// (die Menü-Pause bleibt unberührt und greift weiter über ApplyPlaybackState).</summary>
        public void ClearRunPlay()
        {
            _runPaused = false;
            _runRate = 1f;
            ApplyPlaybackState();
        }

        public void StopSong()
        {
            _music.Stop();
            _paused = false;
            _runPaused = false;
            _runRate = 1f;
            _music.pitch = 1f;
            ResetEndlessClock();
        }

        public void PauseSong()
        {
            _paused = true;
            ApplyPlaybackState();
        }

        public void ResumeSong()
        {
            _paused = false;
            ApplyPlaybackState();
        }

        /// <summary>
        /// Baut eine gültige file://-URL aus einem lokalen Pfad. Pfadbestandteile werden
        /// URL-maskiert (Leerzeichen, &amp; ...), sonst schlägt das Laden auf Android bei
        /// Ordnernamen wie "Runaway (U &amp; I) - Tpenny68" oder "Dance Monkey.egg" fehl.
        /// </summary>
        private static string ToFileUri(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            string norm = path.Replace('\\', '/');
            var parts = norm.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = Uri.EscapeDataString(parts[i]);
            }
            return "file://" + string.Join("/", parts);
        }

        /// <summary>
        /// Audio-Position der Datei in Sekunden. Läuft die Wiedergabe NATÜRLICH aus, läuft
        /// die Uhr ab da weiter (Nachlauf): <c>_music.time</c> bliebe sonst bei
        /// <c>clip.length</c> stehen — die um den Offset verkürzte Spieluhr käme nie ans
        /// Ende, und ab ca. 0,4 s Delay würde der Song nicht mehr wechseln. Bei einer Pause
        /// (Menü) endet die Datei nicht, daher friert die Uhr dort ganz normal ein.
        /// </summary>
        public float AudioTime
        {
            get
            {
                if (_music == null || _music.clip == null) return 0f;
                float t = _music.time;
                if (!AudioFinished) { _clockPastEnd = false; return t; }
                if (!_clockPastEnd)
                {
                    _clockPastEnd = true;
                    _clockPastEndBase = t;
                    _clockPastEndWall = Time.unscaledTime;
                }
                return _clockPastEndBase + (Time.unscaledTime - _clockPastEndWall);
            }
        }

        /// <summary>True, wenn die Datei physisch fertig gespielt ist (Ende-Schalter).
        /// Bei Menü- ODER Lauf-Pause NIE „fertig": sonst würde die Nachlauf-Uhr
        /// (unscaledTime) weiterlaufen und die Score-Uhr im Stillstand weiterzählen.</summary>
        public bool AudioFinished => _music != null && _music.clip != null
            && !_paused && !_runPaused
            && !_music.isPlaying
            && _music.time >= _music.clip.length - 0.02f;

        public float ScoreTime
        {
            get
            {
                float totalSec = (GameConfig.AudioLatencyBaseMs + GameConfig.AudioOffsetBaseMs) / 1000f;
                return AudioTime - totalSec;
            }
        }

        public float VisualTime => ScoreTime;

        public float ClipDuration => _music.clip != null ? _music.clip.length : 0f;

        // SFX-Clips einmal laden und im Speicher halten (Performance): Resources.Load je
        // Aufruf wäre bei JEDEM Schnitt und jedem UI-Klick ein Dictionary-Lookup + erneutes
        // Clip-Handling im Hot Path. Die Clips sind klein und bleiben ohnehin unverändert —
        // einmal laden genügt (siehe PROJECT_NOTES.md §10).
        private static AudioClip _sliceSfx, _uiSfx;
        private static bool _sfxCached;

        private static void EnsureSfxCached()
        {
            if (_sfxCached) return;
            _sfxCached = true;
            _sliceSfx = Resources.Load<AudioClip>(GameConfig.SliceSfxResource);
            _uiSfx = Resources.Load<AudioClip>(GameConfig.UISfxResource);
        }

        /// <summary>
        /// Schwert ↔ Note: der Slice-Sound, TIEFPASS-gefiltert (dumpf), tiefer im Ton und
        /// LEISE — das „direkte dumpfe Geräusch" beim Treffer. Tonhöhe, Grenzfrequenz und
        /// Lautstärke stehen in `GameConfig` (`HitSfxPitch`/`HitSfxLowpassHz`/`HitSfxVolume`).
        /// `HitSfxVolume` = 0 schaltet den Ton bewusst stumm, ohne dass der Aufruf im
        /// Trefferpfad verschwindet (kein Code-Rauschen, kein `PlayOneShot` auf stumm).
        /// </summary>
        public void PlayHitSfx()
        {
            if (GameConfig.HitSfxVolume > 0f)
            {
                EnsureSfxCached();
                if (_sliceSfx != null)
                {
                    _hit.pitch = GameConfig.HitSfxPitch;
                    _hit.PlayOneShot(_sliceSfx, GameConfig.HitSfxVolume);
                }
            }
        }

        /// <summary>
        /// Easter-Egg-Schalter (Schwert aus- UND einfahren): derselbe Slice-Sound, aber
        /// vorne abgeschnitten, beschleunigt und dumpfer gefiltert — dadurch wirkt er direkt
        /// wie ein Schalter-Klick statt wie ein Schlag. Nur `Play()` kann einen Startversatz
        /// setzen (`timeSamples`); `Slice.ogg` ist als „Decompress On Load" im Speicher
        /// (`loadType: 0` in der .meta), deshalb ist der Sample-Versatz zuverlässig — bei
        /// Streaming-Clips ginge er nicht. Alles per `GameConfig` tunebar.
        /// </summary>
        public void PlaySaberToggleSfx()
        {
            if (GameConfig.SaberToggleSfxVolume > 0f)
            {
                EnsureSfxCached();
                var clip = _sliceSfx;
                if (clip != null)
                {
                    int skip = Mathf.Clamp(
                        Mathf.RoundToInt(GameConfig.SaberToggleSfxSkipMs * clip.frequency / 1000f),
                        0, Mathf.Max(0, clip.samples - 1));
                    _toggle.clip = clip;
                    _toggle.volume = GameConfig.SaberToggleSfxVolume;
                    _toggle.pitch = GameConfig.SaberToggleSfxPitch;
                    _toggle.timeSamples = skip;
                    _toggle.Play();
                }
            }
        }

        public void PlayUiSfx()
        {
            EnsureSfxCached();
            if (_uiSfx != null) _sfx.PlayOneShot(_uiSfx, GameConfig.UISfxVolume);
        }
    }
}
