using UnityEngine;

namespace BeatSaber
{
    /// <summary>
    /// Verzögert das Audiosignal seines GameObjects um eine einstellbare Anzahl
    /// Millisekunden (Audio-Offset). Siehe Klassendokumentation oben.
    /// </summary>
    public sealed class AudioDelayFilter : MonoBehaviour
    {
        /// <summary>Slew-Rate: max. Verschiebung des Lesezeigers je Audio-Callback
        /// (Sekunden). Unity ruft OnAudioFilterRead ~alle 20 ms auf (~50/s) → 0,02 s je
        /// Callback ≈ 1 s Drift pro Sekunde — eine Änderung ist in Bruchteilen einer
        /// Sekunde hörbar, ohne Klick.</summary>
        private const float MaxSlewSecondsPerCallback = 0.02f;

        /// <summary>Pufferreserve über der größten Offset-Einstellung (Sekunden).</summary>
        private const float BufferHeadroomSeconds = 0.2f;

        private float[] _ring;             // interleaved Ringpuffer (frames × channels)
        private int _channels;
        private int _sampleRate;
        private int _ringFrames;
        private int _writeFrame;           // Schreibzeiger (Frame-Index im Ring)
        private float _delaySamples;       // aktueller Leseabstand (fraktional, Samples)
        private float _targetSeconds;      // Ziel-Verzögerung (Hauptthread → Audithread)

        public void SetOffset(float offsetSeconds)
        {
            float baseDelay = GameConfig.AudioOffsetBaseMs / 1000f;
            float delay = baseDelay + offsetSeconds;
            if (delay < 0f) delay = 0f;
            float maxDelay = (GameConfig.AudioOffsetBaseMs + GameConfig.AudioOffsetMaxMs) / 1000f
                             + BufferHeadroomSeconds;
            if (delay > maxDelay) delay = maxDelay;
            _targetSeconds = delay;
            if (Mathf.Abs(delay - baseDelay) > 0.0005f)
                Debug.Log($"[BeatSaber] Audio-Offset = {offsetSeconds * 1000f:0} ms " +
                          $"(Musik-Verzögerung {delay * 1000f:0} ms; Noten unangetastet)");
        }

        private void OnEnable()
        {
            // Sample-Rate vom Audio-System lesen (PICO: üblich 48 kHz); die Kanalzahl
            // liefert erst der Callback — der Puffer entsteht dann einmalig.
            _sampleRate = AudioSettings.outputSampleRate;
            if (_sampleRate <= 0) _sampleRate = 48000;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            int sampleRate = _sampleRate;

            // Puffer einmalig anhand der echten Kanalzahl anlegen (keine Unity-API nötig).
            if (_ring == null || _channels != channels || _ringFrames == 0)
            {
                _channels = channels;
                int maxFrames = Mathf.CeilToInt(
                    (GameConfig.AudioOffsetBaseMs + GameConfig.AudioOffsetMaxMs) / 1000f
                    * sampleRate + BufferHeadroomSeconds * sampleRate);
                if (maxFrames < 64) maxFrames = 64;
                _ringFrames = maxFrames;
                _ring = new float[maxFrames * channels];
                if (_writeFrame >= maxFrames) _writeFrame = 0;
            }

            int ch = channels;
            int ring = _ringFrames;
            int write = _writeFrame;
            int frameCount = data.Length / ch;

            // Der Ring wird IMMER mitgeschrieben (billiges Kopieren) — so ist er beim
            // Umschalten von Durchreichen auf Verzögern bereits „warm" und es entsteht
            // keine Lücke/Garbage am Übergang.
            for (int f = 0; f < frameCount; f++)
            {
                int w = (write + f) % ring;
                int baseW = w * ch;
                for (int c = 0; c < ch; c++)
                    _ring[baseW + c] = data[f * ch + c];
            }

            float delay = _delaySamples;
            float target = _targetSeconds * sampleRate;

            // Neutraler Durchreich-Modus: nur schreiben (Ring bleibt warm), Ausgabe unverändert.
            if (delay < 0.5f && target < 0.5f)
            {
                _writeFrame = (write + frameCount) % ring;
                _delaySamples = 0f;
                return;
            }

            // Ziel-Annäherung (Slew) — Klick-freies Nachziehen bei Menü-Änderungen.
            float maxStep = MaxSlewSecondsPerCallback * sampleRate;
            if (target - delay > maxStep) delay += maxStep;
            else if (delay - target > maxStep) delay -= maxStep;
            else delay = target;

            // Sicherheitsklemmung: Leseabstand bleibt stets ≥ 1 Frame unter der
            // Ringgröße (sonst liese sich der fraktionale Lesezeiger überholen).
            int maxDelaySamples = ring - 2;
            if (delay > maxDelaySamples) delay = maxDelaySamples;

            // Verzögert zurücklesen: Leseposition = write − delay (fraktional → linearer
            // Lerp der zwei Nachbarrames). delay ≥ 0 ⇒ der Lesezeiger liegt nie vor dem
            // Schreibzeiger; Offsets ≤ 0,3 s sind weit unter der Ringgröße, die Samples
            // wurden also bereits geschrieben.
            int delayInt = (int)delay;
            float frac = delay - delayInt;
            for (int f = 0; f < frameCount; f++)
            {
                int w = (write + f) % ring;
                int readIdx0 = w - delayInt;
                int readIdx1 = readIdx0 - 1;
                if (readIdx0 < 0) readIdx0 += ring;
                if (readIdx1 < 0) readIdx1 += ring;
                int base0 = readIdx0 * ch;
                int base1 = readIdx1 * ch;
                for (int c = 0; c < ch; c++)
                {
                    float s0 = _ring[base0 + c];
                    float s1 = _ring[base1 + c];
                    data[f * ch + c] = s0 + (s1 - s0) * frac;
                }
            }

            _writeFrame = (write + frameCount) % ring;
            _delaySamples = delay;
        }
    }
}
