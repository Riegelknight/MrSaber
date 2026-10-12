using UnityEngine;

namespace BeatSaber
{
    /// <summary>Bewegungszustand des Spielers im Run-and-Play-Modus.</summary>
    public enum RunPlayState
    {
        /// <summary>Absoluter Stillstand: Song pausiert, Noten frieren ein.</summary>
        Still,
        /// <summary>Vorwärts: Audio + Noten-Timeline laufen synchron zur Laufgeschwindigkeit.</summary>
        Forward,
        /// <summary>Rückwärts: Gameplay stoppt sofort, das Feld wird mitgezogen.</summary>
        Backward
    }

    /// <summary>
    /// Puffert die Vorwärtsgeschwindigkeit des Raums und leitet daraus den
    /// Run-and-Play-Zustand und die geglättete Geschwindigkeit ab. Reine Logik,
    /// keine Unity-Abhängigkeit außer <see cref="Mathf"/>; allokationsfrei.
    /// </summary>
    public sealed class RunAndPlay
    {
        // Ringpuffer: je Frame (Vorwärts-Versatz [m], dt [s]). 256 Plätze reichen bei
        // 0,3 s Fenster auch für sehr hohe Bildraten deutlich (60er-Teilung = ~18).
        private const int Capacity = 256;
        private readonly float[] _delta = new float[Capacity];
        private readonly float[] _dt = new float[Capacity];
        private int _head;   // nächster Schreibplatz
        private int _tail;   // ältester Eintrag
        private int _count;
        private float _deltaSum;
        private float _dtSum;

        // Letzter roher Frame (für die sofortige Rückwärts-Erkennung).
        private float _rawSpeed;

        /// <summary>Geglättete Vorwärtsgeschwindigkeit (m/s, signed; + = vorwärts).</summary>
        public float SmoothedSpeed => _dtSum > 1e-6f ? _deltaSum / _dtSum : 0f;

        public RunPlayState State
        {
            get
            {
                if (_rawSpeed < -GameConfig.RunPlayStandstillSpeed) return RunPlayState.Backward;
                return SmoothedSpeed > GameConfig.RunPlayStandstillSpeed
                    ? RunPlayState.Forward
                    : RunPlayState.Still;
            }
        }

        /// <summary>Einen Frame einspeisen: signed Vorwärts-Versatz (Meter) und dt (Sekunden).</summary>
        public void Push(float signedForwardDelta, float dt)
        {
            if (dt <= 1e-6f) return;
            _rawSpeed = signedForwardDelta / dt;

            _delta[_head] = signedForwardDelta;
            _dt[_head] = dt;
            _deltaSum += signedForwardDelta;
            _dtSum += dt;
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
            else
            {
                // Voll: ältesten Eintrag verdrängen.
                _deltaSum -= _delta[_tail];
                _dtSum -= _dt[_tail];
                _tail = (_tail + 1) % Capacity;
            }

            // Fenster einhalten: solange der älteste Eintrag außerhalb des Fensters liegt,
            // ihn entfernen — aber mindestens EINEN Eintrag behalten (sonst dtSum = 0).
            while (_count > 1 && _dtSum - _dt[_tail] > GameConfig.RunPlaySpeedWindow)
            {
                _deltaSum -= _delta[_tail];
                _dtSum -= _dt[_tail];
                _tail = (_tail + 1) % Capacity;
                _count--;
            }
        }

        /// <summary>Puffer und letzten Frame verwerfen (Songwechsel, Menü, Moduswechsel).</summary>
        public void Reset()
        {
            _head = _tail = _count = 0;
            _deltaSum = 0f;
            _dtSum = 0f;
            _rawSpeed = 0f;
        }

        public static float ComputePlayRate(float speed, float njs, float roomScale, float comfort)
        {
            float denom = Mathf.Max(0.01f, njs * Mathf.Max(0.01f, roomScale));
            return speed * comfort / denom;
        }
    }
}
