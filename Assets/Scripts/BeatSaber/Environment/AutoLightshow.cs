// ==============================================================================
//  AutoLightshow — erzeugt eine Standard-Lichtshow für Maps OHNE eigene
//  Lightshow-Dateien (die meisten Custom-Maps haben keine _lightshowEvents).
// ==============================================================================
//  Vorbild SuperSaber: deren Songs haben eine feste, pro Song generierte
//  Event-Tabelle. Hier bauen wir ein geschmackvolles 16/32-Beat-Muster aus den
//  vorhandenen Palette-Farben (EnvironmentColorTable + Map-Env-Farben):
//   - Ringe/Tunnel:      Farbwechsel Rot/Blau alle 8 Beats
//   - Rückenlaser:       gegenphasig alle 8 Beats (bewegter Übergang)
//   - Laser links/rechts: alle 4 Beats Flash, Seiten versetzt
//   - Mitte:             Flash auf 16er-Akzent
//   - Laser-Drehung:     alle 16 Beats neuer Richtungs-/Stop-Mix
//   - Boost:             jede 2. Phrase (16 Beats an, 16 aus) → Twister-Puls
//
//  Alles in BEATS — die Umrechnung in Sekunden übernimmt EnvironmentClock.
//  Kontext: Architektur siehe PROJECT_NOTES.md (Projektwurzel).
// ==============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber.Environment
{
    public static class AutoLightshow
    {
        /// <summary>Füllt die parallelen Listen im Stil des Map-Parsers.</summary>
        public static void Populate(List<LightEventType> types, List<float> beats,
                                    List<int> values, List<float> floats,
                                    List<bool> hasColor, List<Color> colors,
                                    List<float> boostBeats, List<bool> boostVals,
                                    float totalBeats)
        {
            const int kRed  = (int)LightEventValue.RedOn;
            const int kBlue = (int)LightEventValue.BlueOn;
            const int kRedF  = (int)LightEventValue.RedFlash;
            const int kBlueF = (int)LightEventValue.BlueFlash;
            const int kOff   = (int)LightEventValue.Off;

            float end = Mathf.CeilToInt(Mathf.Max(totalBeats, 64f) / 16f) * 16f + 32f;
            for (float b = 0f; b < end; b += 4f)
            {
                bool half = (b % 8f) >= 4f;          // Phasenlage innerhalb von 8
                bool sect = ((b % 16f) >= 8f);       // Phasenlage innerhalb von 16
                int main = sect ? kRed : kBlue;
                int anti = sect ? kBlue : kRed;

                // Tunnel/Ringe: alle 8 Beats klare Farbe.
                if (b % 8f < 0.01f)
                    Add(types, beats, values, floats, hasColor, colors,
                        LightEventType.Rings, b, main);

                // Rückenlaser: gegenphasig zu den Ringen, weicher Übergang.
                if (b % 8f < 0.01f)
                    Add(types, beats, values, floats, hasColor, colors,
                        LightEventType.BackLasers, b, anti);

                // Laser-Fans: Flash-Viertel versetzt links/rechts.
                Add(types, beats, values, floats, hasColor, colors,
                    LightEventType.LeftRotatingLasers, b, half ? kBlueF : kRedF);
                Add(types, beats, values, floats, hasColor, colors,
                    LightEventType.RightRotatingLasers, b + 2f, half ? kRedF : kBlueF);

                // Mitte: nur auf 16er-Akzent.
                if (b % 16f < 0.01f)
                    Add(types, beats, values, floats, hasColor, colors,
                        LightEventType.CenterLights, b, kRedF);

                // Laser-Drehung: alle 16 Beats neuer Mix (Richtung/Stop).
                if (b % 16f < 0.01f)
                {
                    int spin = ((b / 16f) % 3f) < 0.5f ? kOff : (sect ? kRed : kBlue);
                    Add(types, beats, values, floats, hasColor, colors,
                        LightEventType.LeftRotationSpeed, b, spin);
                    Add(types, beats, values, floats, hasColor, colors,
                        LightEventType.RightRotationSpeed, b, spin == kOff ? kBlue : kOff);
                }

                // Boost: 16 Beats an, 16 Beats aus → Twister-Schübe.
                if (b % 32f < 0.01f)
                {
                    boostBeats.Add(b);        boostVals.Add(true);
                    boostBeats.Add(b + 16f);  boostVals.Add(false);
                }
            }
        }

        private static void Add(List<LightEventType> types, List<float> beats,
            List<int> values, List<float> floats, List<bool> hasColor,
            List<Color> colors, LightEventType type, float beat, int value)
        {
            types.Add(type);
            beats.Add(beat);
            values.Add(value);
            floats.Add(1f);
            hasColor.Add(false);
            colors.Add(Color.clear);
        }
    }
}
