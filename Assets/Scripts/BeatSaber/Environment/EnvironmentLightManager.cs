// ==============================================================================
//  EnvironmentLightManager — Lightshow-Auswertung (Port aus ArcViewer LightManager.cs).
// ==============================================================================
//  Übernommen 1:1 aus ArcViewer (Assets/__Scripts/Previewer/MapControl/Lightshow/
//  LightManager.cs): berechnet je Frame die Lichtfarbe der Event-Typen
//  (0 Rückenlaser, 1 Ringe, 2 linke Laser, 3 rechte Laser, 4 Mittellichter),
//  die Laser-Drehgeschwindigkeit (12/13) und den Color-Boost. Die Farbwerte
//  werden per statischem Event an alle LightHandler geschickt (wie im Original).
//
//  Anpassungen: Zeitquelle = EnvironmentClock; Farbsatz = EnvironmentColorManager;
//  statische Liste statt Szenen-Komponente. `StaticLights = true` zeigt die
//  ruhige Standard-Beleuchtung (Modus „Default"), `false` spielt die Song-Lightshow
//  (Modus „Animiert").
//  Kontext: Architektur siehe PROJECT_NOTES.md (Projektwurzel).
// ==============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber.Environment
{
    public static class EnvironmentLightManager
    {
        public const float FlashIntensity = 1.2f;

        public static float LightEmission = 6.5f;

        /// <summary>Aktuelle Umgebungsparameter (Ringe/Laser) — Default-Umgebung.</summary>
        public static EnvironmentLightParameters CurrentParameters = new EnvironmentLightParameters();

        private static bool _staticLights = true;
        public static bool StaticLights
        {
            get => _staticLights;
            set
            {
                if (_staticLights == value) return;
                _staticLights = value;
                OnStaticLightsChanged?.Invoke();
            }
        }

        private static bool _boostActive;
        public static bool BoostActive
        {
            get => _boostActive && !StaticLights;
            set => _boostActive = value;
        }

        public static event Action<LightingPropertyEventArgs> OnLightPropertiesChanged;
        public static event Action<LaserSpeedEvent, LightEventType> OnLaserRotationsChanged;
        public static event Action OnStaticLightsChanged;

        private static EnvironmentColorPalette Colors => EnvironmentColorManager.CurrentColors;
        private static Color LightColor1 => BoostActive ? Colors.BoostLightColor1 : Colors.LightColor1;
        private static Color LightColor2 => BoostActive ? Colors.BoostLightColor2 : Colors.LightColor2;
        private static Color WhiteLightColor => BoostActive ? Colors.BoostWhiteLightColor : Colors.WhiteLightColor;

        public static readonly MapElementList<LightEvent> BackLaserEvents = new MapElementList<LightEvent>();
        public static readonly MapElementList<LightEvent> RingEvents = new MapElementList<LightEvent>();
        public static readonly MapElementList<LightEvent> LeftLaserEvents = new MapElementList<LightEvent>();
        public static readonly MapElementList<LightEvent> RightLaserEvents = new MapElementList<LightEvent>();
        public static readonly MapElementList<LightEvent> CenterLightEvents = new MapElementList<LightEvent>();
        public static readonly MapElementList<LaserSpeedEvent> LeftLaserSpeedEvents = new MapElementList<LaserSpeedEvent>();
        public static readonly MapElementList<LaserSpeedEvent> RightLaserSpeedEvents = new MapElementList<LaserSpeedEvent>();
        public static readonly MapElementList<BoostEvent> BoostEvents = new MapElementList<BoostEvent>();

        private static readonly LightingPropertyEventArgs[] lightPropertyEventArgs =
        {
            new LightingPropertyEventArgs { type = LightEventType.BackLasers },
            new LightingPropertyEventArgs { type = LightEventType.Rings },
            new LightingPropertyEventArgs { type = LightEventType.LeftRotatingLasers },
            new LightingPropertyEventArgs { type = LightEventType.RightRotatingLasers },
            new LightingPropertyEventArgs { type = LightEventType.CenterLights }
        };

        private static readonly MapElementList<BoostEvent>.CheckInRangeDelegate boostInRange = e => e.Time <= EnvironmentClock.CurrentTime;
        private static readonly MapElementList<LightEvent>.CheckInRangeDelegate lightInRange = e => e.Time <= EnvironmentClock.CurrentTime;
        private static readonly MapElementList<LaserSpeedEvent>.CheckInRangeDelegate laserSpeedInRange = e => e.Time <= EnvironmentClock.CurrentTime;

        /// <summary>Wird jeden Frame von der EnvironmentDirector aufgerufen (Modus „Animiert").</summary>
        public static void UpdateLights()
        {
            if (StaticLights) return;

            int lastBoostEvent = BoostEvents.GetLastIndex(EnvironmentClock.CurrentTime, boostInRange);
            BoostActive = lastBoostEvent >= 0 && BoostEvents[lastBoostEvent].Value;

            EnvironmentRingManager.UpdateRings();

            UpdateLightEventType(LightEventType.BackLasers, BackLaserEvents);
            UpdateLightEventType(LightEventType.Rings, RingEvents);
            UpdateLightEventType(LightEventType.LeftRotatingLasers, LeftLaserEvents);
            UpdateLightEventType(LightEventType.RightRotatingLasers, RightLaserEvents);
            UpdateLightEventType(LightEventType.CenterLights, CenterLightEvents);

            UpdateLaserSpeedEventType(LightEventType.LeftRotationSpeed, LeftLaserSpeedEvents);
            UpdateLaserSpeedEventType(LightEventType.RightRotationSpeed, RightLaserSpeedEvents);

            ApplyGlobalTint();
        }

        private static void UpdateLightEventType(LightEventType type, MapElementList<LightEvent> events)
        {
            int lastIndex = events.GetLastIndex(EnvironmentClock.CurrentTime, lightInRange);
            bool foundEvent = lastIndex >= 0;
            LightEvent currentEvent = foundEvent ? events[lastIndex] : null;
            bool hasNextEvent = lastIndex + 1 < events.Count;
            LightEvent nextEvent = hasNextEvent ? events[lastIndex + 1] : null;
            UpdateLightEvent(type, currentEvent, nextEvent, events, lastIndex);
        }

        private static void UpdateLightEvent(LightEventType type, LightEvent lightEvent, LightEvent nextEvent,
            MapElementList<LightEvent> events, int eventIndex)
        {
            Color eventColor = GetEventColor(lightEvent, nextEvent);
            LightingPropertyEventArgs eventArgs = lightPropertyEventArgs[(int)type];
            eventArgs.eventList = events;
            eventArgs.lightEvent = lightEvent;
            eventArgs.nextEvent = nextEvent;
            eventArgs.type = type;
            eventArgs.eventIndex = eventIndex;
            eventArgs.eventColor = eventColor;
            eventArgs.laserColor = GetLaserColor(eventColor);
            eventArgs.glowColor = GetLaserGlowColor(eventColor);
            OnLightPropertiesChanged?.Invoke(eventArgs);
        }

        private static void UpdateLaserSpeedEventType(LightEventType type, MapElementList<LaserSpeedEvent> events)
        {
            int lastIndex = events.GetLastIndex(EnvironmentClock.CurrentTime, laserSpeedInRange);
            LaserSpeedEvent lastEvent = lastIndex >= 0 ? events[lastIndex] : null;
            OnLaserRotationsChanged?.Invoke(lastEvent, type);
        }

        public static Color GetLaserColor(Color baseColor)
        {
            baseColor.a *= LightEmission;
            return baseColor;
        }

        public static Color GetLaserGlowColor(Color baseColor) => baseColor;

        public static Color GetEventColor(LightEvent lightEvent, LightEvent nextEvent)
        {
            if (lightEvent == null) return Color.clear;
            switch (lightEvent.Value)
            {
                case LightEventValue.RedOn:
                case LightEventValue.RedTransition:
                case LightEventValue.BlueOn:
                case LightEventValue.BlueTransition:
                case LightEventValue.WhiteOn:
                case LightEventValue.WhiteTransition:
                case LightEventValue.Off:
                    return GetStandardEventColor(lightEvent, nextEvent);
                case LightEventValue.RedFlash:
                case LightEventValue.WhiteFlash:
                case LightEventValue.BlueFlash:
                    return GetFlashColor(lightEvent);
                case LightEventValue.RedFade:
                case LightEventValue.BlueFade:
                case LightEventValue.WhiteFade:
                    return GetFadeColor(lightEvent);
                default:
                    return Color.clear;
            }
        }

        private static Color GetEventBaseColor(LightEvent lightEvent)
        {
            Color baseColor;
            // Chroma: per-Event gesetzte Farbe hat Vorrang vor der Rot/Blau/Weiß-Semantik
            // (Ausnahme „Off" ist immer transparent — siehe GetEventColor).
            if (lightEvent.HasCustomColor && lightEvent.Value != LightEventValue.Off)
            {
                baseColor = lightEvent.CustomColor;
            }
            else switch (lightEvent.Value)
            {
                case LightEventValue.RedOn:
                case LightEventValue.RedTransition:
                case LightEventValue.RedFlash:
                case LightEventValue.RedFade:
                    baseColor = LightColor1; break;
                case LightEventValue.BlueOn:
                case LightEventValue.BlueTransition:
                case LightEventValue.BlueFlash:
                case LightEventValue.BlueFade:
                    baseColor = LightColor2; break;
                case LightEventValue.WhiteOn:
                case LightEventValue.WhiteTransition:
                case LightEventValue.WhiteFlash:
                case LightEventValue.WhiteFade:
                    baseColor = WhiteLightColor; break;
                default:
                    baseColor = Color.clear; break;
            }
            baseColor.a *= lightEvent.FloatValue;
            return baseColor;
        }

        private static Color GetStandardEventColor(LightEvent lightEvent, LightEvent nextEvent)
        {
            Color baseColor = GetEventBaseColor(lightEvent);
            if (nextEvent != null && nextEvent.IsTransition)
            {
                Color transitionColor = GetEventBaseColor(nextEvent);
                if (lightEvent.Value == LightEventValue.Off)
                {
                    baseColor = transitionColor;
                    baseColor.a = 0f;
                }
                float transitionTime = nextEvent.Time - lightEvent.Time;
                float t = transitionTime > 0f
                    ? Mathf.Clamp01((EnvironmentClock.CurrentTime - lightEvent.Time) / transitionTime)
                    : 1f;
                t = Easings.EasingFromType(lightEvent.TransitionEasing, t);
                baseColor = Color.LerpUnclamped(baseColor, transitionColor, t);
            }
            return baseColor;
        }

        private static Color GetFlashColor(LightEvent lightEvent)
        {
            const float fadeTime = 0.6f;
            Color baseColor = GetEventBaseColor(lightEvent);
            float timeDifference = EnvironmentClock.CurrentTime - lightEvent.Time;
            if (timeDifference < fadeTime)
            {
                float t = timeDifference / fadeTime;
                baseColor.a *= Mathf.Lerp(FlashIntensity, 1f, Easings.Cubic.Out(t));
            }
            return baseColor;
        }

        private static Color GetFadeColor(LightEvent lightEvent)
        {
            const float fadeTime = 1.5f;
            Color baseColor = GetEventBaseColor(lightEvent);
            float timeDifference = EnvironmentClock.CurrentTime - lightEvent.Time;
            if (timeDifference >= fadeTime) baseColor.a = 0f;
            else
            {
                float t = timeDifference / fadeTime;
                baseColor.a *= Mathf.Lerp(FlashIntensity, 0f, Easings.Expo.Out(t));
            }
            return baseColor;
        }

        /// <summary>Ruhige Standardbeleuchtung (Modus „Default" bzw. Maps ohne Lightshow).</summary>
        public static void SetStaticLayout()
        {
            var back = new LightEvent(0f, LightEventType.BackLasers, LightEventValue.BlueOn, 1f);
            var rings = new LightEvent(0f, LightEventType.Rings, LightEventValue.BlueOn, 1f);
            var left = new LightEvent(0f, LightEventType.LeftRotatingLasers, LightEventValue.RedOn, 1f);
            var right = new LightEvent(0f, LightEventType.RightRotatingLasers, LightEventValue.RedOn, 1f);
            var center = new LightEvent(0f, LightEventType.CenterLights, LightEventValue.BlueOn, 1f);

            UpdateLightEvent(LightEventType.BackLasers, back, null, new MapElementList<LightEvent>(), -1);
            UpdateLightEvent(LightEventType.Rings, rings, null, new MapElementList<LightEvent>(), -1);
            UpdateLightEvent(LightEventType.LeftRotatingLasers, left, null, new MapElementList<LightEvent>(), -1);
            UpdateLightEvent(LightEventType.RightRotatingLasers, right, null, new MapElementList<LightEvent>(), -1);
            UpdateLightEvent(LightEventType.CenterLights, center, null, new MapElementList<LightEvent>(), -1);

            OnLaserRotationsChanged?.Invoke(null, LightEventType.LeftRotationSpeed);
            OnLaserRotationsChanged?.Invoke(null, LightEventType.RightRotationSpeed);
            EnvironmentRingManager.SetStaticRings();
            ApplyGlobalTint();
        }

        /// <summary>
        /// Setzt die GLOBALEN Farben (Shader globals für Hintergrund/Reflexionen der
        /// Bloomfog-Skybox und der Umgebungs-Shader): Ring- + Rückenlaser-Farbe, plus
        /// dezente Nebel-Werte.
        /// </summary>
        private static void ApplyGlobalTint()
        {
            Color rings = lightPropertyEventArgs[(int)LightEventType.Rings].eventColor;
            Color back = lightPropertyEventArgs[(int)LightEventType.BackLasers].eventColor;
            // Fallback (z. B. Maps ohne Licht-Events): Grundpalette, damit der Hintergrund
            // trotzdem gefärbt ist (nicht schwarz).
            if (rings.a <= 0.001f && back.a <= 0.001f)
            {
                Color c1 = Colors.LightColor1, c2 = Colors.LightColor2;
                rings = new Color(c1.r, c1.g, c1.b, 0.35f);
                back = new Color(c2.r, c2.g, c2.b, 0.35f);
            }

            float strength = BoostActive ? 0.9f : 0.6f;
            Shader.SetGlobalColor("_EnvironmentTint", new Color(rings.r, rings.g, rings.b, rings.a * strength));
            Shader.SetGlobalColor("_EnvironmentBackColor", new Color(back.r, back.g, back.b, back.a * strength));

            // Hintergrundfarbe (Kamera-Clear) — dunkle Mischung aus Ring- + Rückenlaser-Farbe.
            CurrentBackground = new Color(
                (rings.r * rings.a + back.r * back.a) * 0.5f * strength,
                (rings.g * rings.a + back.g * back.a) * 0.5f * strength,
                (rings.b * rings.a + back.b * back.a) * 0.5f * strength,
                1f);
        }

        /// <summary>Aktuelle Hintergrundfarbe (Kamera-Clear) für den EnvironmentDirector.</summary>
        public static Color CurrentBackground = new Color(0.015f, 0.015f, 0.02f, 1f);

        public static void UpdateLightParameters()
        {
            if (StaticLights) SetStaticLayout();
            else UpdateLights();
        }

        // ---------- Aufbau je Song ----------

        public static void ClearEvents()
        {
            BackLaserEvents.Clear();
            RingEvents.Clear();
            LeftLaserEvents.Clear();
            RightLaserEvents.Clear();
            CenterLightEvents.Clear();
            LeftLaserSpeedEvents.Clear();
            RightLaserSpeedEvents.Clear();
            BoostEvents.Clear();
            EnvironmentRingManager.ClearEvents();
        }

        /// <summary>
        /// Befüllt die Event-Listen aus den geparsten Song-Daten (Beat-Saber-Typen).
        /// </summary>
        public static void LoadEvents(List<LightEventType> types, List<float> beats,
            List<int> values, List<float> floatValues, List<bool> hasCustomColor, List<Color> customColors,
            List<float> boostBeats, List<bool> boostValues)
        {
            ClearEvents();

            for (int i = 0; i < types.Count && i < beats.Count; i++)
            {
                var le = new LightEvent(beats[i], types[i], (LightEventValue)values[i],
                    i < floatValues.Count ? floatValues[i] : 1f);
                if (hasCustomColor != null && i < hasCustomColor.Count && hasCustomColor[i]
                    && customColors != null && i < customColors.Count)
                {
                    le.HasCustomColor = true;
                    le.CustomColor = customColors[i];
                }
                switch (types[i])
                {
                    case LightEventType.BackLasers: BackLaserEvents.Add(le); break;
                    case LightEventType.Rings: RingEvents.Add(le); break;
                    case LightEventType.LeftRotatingLasers: LeftLaserEvents.Add(le); break;
                    case LightEventType.RightRotatingLasers: RightLaserEvents.Add(le); break;
                    case LightEventType.CenterLights: CenterLightEvents.Add(le); break;
                    case LightEventType.LeftRotationSpeed: LeftLaserSpeedEvents.Add(new LaserSpeedEvent(le)); break;
                    case LightEventType.RightRotationSpeed: RightLaserSpeedEvents.Add(new LaserSpeedEvent(le)); break;
                    case LightEventType.RingSpin:
                    {
                        var spin = new RingRotationEvent(le);
                        spin.SetDefaultSpeedAndProp(false);
                        EnvironmentRingManager.AllRingRotationEvents.Add(spin);
                        var small = new RingRotationEvent(spin);
                        EnvironmentRingManager.SmallRingRotationEvents.Add(small);
                        var big = new RingRotationEvent(spin);
                        big.SetDefaultSpeedAndProp(true);
                        EnvironmentRingManager.BigRingRotationEvents.Add(big);
                        break;
                    }
                    case LightEventType.RingZoom:
                        EnvironmentRingManager.RingZoomEvents.Add(new RingZoomEvent(le));
                        break;
                }
            }

            if (boostBeats != null && boostValues != null)
            {
                for (int i = 0; i < boostBeats.Count && i < boostValues.Count; i++)
                    BoostEvents.Add(new BoostEvent(boostBeats[i], boostValues[i]));
            }

            BackLaserEvents.SortElementsByBeat();
            RingEvents.SortElementsByBeat();
            LeftLaserEvents.SortElementsByBeat();
            RightLaserEvents.SortElementsByBeat();
            CenterLightEvents.SortElementsByBeat();
            LeftLaserSpeedEvents.SortElementsByBeat();
            RightLaserSpeedEvents.SortElementsByBeat();
            BoostEvents.SortElementsByBeat();
            EnvironmentRingManager.AllRingRotationEvents.SortElementsByBeat();
            EnvironmentRingManager.SmallRingRotationEvents.SortElementsByBeat();
            EnvironmentRingManager.BigRingRotationEvents.SortElementsByBeat();
            EnvironmentRingManager.RingZoomEvents.SortElementsByBeat();

            PopulateLaserRotationEventData();
            EnvironmentRingManager.PopulateRingEventData();

            // Wie ArcViewer: JEDES Basis-Event (auch Ring-Spin 8/9, Laser-Speed 12/13)
            // schaltet die Animation an — nicht nur die Farb-Events 0–4. Sonst bleiben
            // Maps, die nur Ring-/Laser-Events haben, fälschlich starr.
            bool hasEvents = types.Count > 0;
            StaticLights = !hasEvents;
            if (StaticLights) UpdateLightParameters();
        }

        private static void PopulateLaserRotationEventData()
        {
            int laserCount = (int)CurrentParameters.RotatingLaserCount;
            for (int i = 0; i < LeftLaserSpeedEvents.Count; i++)
            {
                LaserSpeedEvent previous = i > 0 ? LeftLaserSpeedEvents[i - 1] : null;
                LeftLaserSpeedEvents[i].PopulateRotationData(laserCount, previous, CurrentParameters);
            }
            for (int i = 0; i < RightLaserSpeedEvents.Count; i++)
            {
                LaserSpeedEvent previous = i > 0 ? RightLaserSpeedEvents[i - 1] : null;
                RightLaserSpeedEvents[i].PopulateRotationData(laserCount, previous, CurrentParameters);
            }
        }

        public static void ResetAll()
        {
            ClearEvents();
            StaticLights = true;
            _boostActive = false;
        }
    }

    public class LightingPropertyEventArgs
    {
        public MapElementList<LightEvent> eventList;
        public LightEvent lightEvent;
        public LightEvent nextEvent;
        public LightEventType type;
        public int eventIndex;
        public Color eventColor;
        public Color laserColor;
        public Color glowColor;
    }
}
