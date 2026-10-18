// ==============================================================================
//  EnvironmentLightshow — Lightshow-Datenmodell (Port aus ArcViewer-main).
// ==============================================================================
//  Übernommen 1:1 aus ArcViewer (Assets/__Scripts/Previewer/MapControl/Lightshow
//  + Maps/MapElement.cs), damit die Song-Lightshows (v2/v3/v4) exakt wie im
//  Referenz-Viewer ausgewertet werden: Event-Farben (On/Flash/Fade/Transition),
//  Ringspin/-zoom, Laser-Drehgeschwindigkeit und Color-Boost.
//
//  Anpassungen gegenüber ArcViewer:
//   - Zeitquelle ist EnvironmentClock (vom GameManager aus der Score-Uhr gespeist)
//     statt TimeManager.
//   - Farben kommen aus EnvironmentColorManager (Default Rot/Blau; spätere
//     Map-/Environment-Farben können dort ergänzt werden).
//   - Die komplette Lightshow hängt an der Klasse EnvironmentLightManager.
//  Kontext: Architektur siehe PROJECT_NOTES.md (Projektwurzel).
// ==============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BeatSaber.Environment
{
    /// <summary>
    /// Zeitquelle der Lightshow: Sekunden (Score-Uhr) UND Beats des laufenden Songs.
    /// Der GameManager setzt <see cref="ToSeconds"/> je Song (BPM-Verlauf) und schreibt
    /// jeden Frame <see cref="CurrentTime"/>/<see cref="CurrentBeat"/>.
    /// </summary>
    public static class EnvironmentClock
    {
        /// <summary>Liedzeit in Sekunden (Score-Uhr des GameManagers).</summary>
        public static float CurrentTime;

        /// <summary>Aktueller Beat (aus <see cref="ToSeconds"/> abgeleitet).</summary>
        public static float CurrentBeat;

        /// <summary>Beat→Sekunden der aktuellen Map (BPM-Verlauf), vom GameManager gesetzt.</summary>
        public static Func<float, float> ToSeconds = b => b;

        /// <summary>Setzt die Umrechnung für den aktuellen Song (ToSeconds + Inverse).</summary>
        public static void SetConverter(Func<float, float> toSeconds)
        {
            ToSeconds = toSeconds ?? (b => b);
        }
    }

    /// <summary>Basis aller zeitbehafteten Lightshow-Elemente (Beat + Sekunden).</summary>
    public class MapElement
    {
        private float _beat;
        public float Beat
        {
            get => _beat;
            set { _beat = value; _time = EnvironmentClock.ToSeconds(value); }
        }

        private float _time;
        public float Time
        {
            get => _time;
            set { _time = value; _beat = EnvironmentClock.CurrentBeat; }
        }
    }

    /// <summary>
    /// Sortierte Event-Liste mit Inkrement-Suche (Port aus ArcViewer MapElementList):
    /// GetLastIndex wandert nur vorwärts und findet das letzte Element „in range".
    /// </summary>
    public class MapElementList<T> : IEnumerable<T> where T : MapElement
    {
        public bool IsSorted { get; protected set; }
        protected List<T> Elements;
        protected int lastStartIndex;
        protected float lastStartTime;

        public MapElementList()
        {
            Elements = new List<T>();
            IsSorted = true;
            lastStartIndex = 0;
            lastStartTime = 0f;
        }

        public int Count => Elements.Count;
        public T Last => Elements.Count > 0 ? Elements[Elements.Count - 1] : null;
        public delegate bool CheckInRangeDelegate(T element);

        public static implicit operator List<T>(MapElementList<T> elementList) => elementList.Elements;

        public void Add(T item)
        {
            IsSorted = Count == 0 || (IsSorted && item.Time >= Last.Time);
            Elements.Add(item);
        }

        public void Clear()
        {
            Elements.Clear();
            lastStartIndex = 0;
            IsSorted = true;
        }

        public T this[int i] { get => Elements[i]; }

        IEnumerator IEnumerable.GetEnumerator() => Elements.GetEnumerator();
        public IEnumerator<T> GetEnumerator() => Elements.GetEnumerator();

        public void SortElementsByBeat()
        {
            if (!IsSorted) { Elements.Sort((x, y) => x.Time.CompareTo(y.Time)); IsSorted = true; }
        }

        /// <summary>Index des letzten Elements, das die Range-Prüfung erfüllt (-1 = keins).</summary>
        public int GetLastIndex(float currentTime, CheckInRangeDelegate checkMethod)
        {
            if (!IsSorted) SortElementsByBeat();
            if (Elements.Count == 0) return -1;

            if (currentTime < lastStartTime) lastStartIndex = 0;
            lastStartTime = currentTime;

            for (int i = lastStartIndex; i < Elements.Count; i++)
            {
                if (!checkMethod(Elements[i])) return i - 1;
                lastStartIndex = i;
            }
            return lastStartIndex;
        }
    }

    /// <summary>Lightshow-Event-Typen (Werte MÜSSEN zu den Map-Prefabs passen).</summary>
    public enum LightEventType
    {
        BackLasers = 0,
        Rings = 1,
        LeftRotatingLasers = 2,
        RightRotatingLasers = 3,
        CenterLights = 4,
        LeftSideExtra = 6,
        RightSideExtra = 7,
        RingSpin = 8,
        RingZoom = 9,
        BillieLeftLasers = 10,
        BillieRightLasers = 11,
        LeftRotationSpeed = 12,
        RightRotationSpeed = 13,
        InterscopeHydraulicsDown = 16,
        InterscopeHydraulicsUp = 17,
        GagaLeftTowerHeight = 18,
        GagaRightTowerHeight = 19
    }

    /// <summary>Event-Werte (Farben) — Reihenfolge = Beat-Saber-Map-Konvention.</summary>
    public enum LightEventValue
    {
        Off,
        BlueOn,
        BlueFlash,
        BlueFade,
        BlueTransition,
        RedOn,
        RedFlash,
        RedFade,
        RedTransition,
        WhiteOn,
        WhiteFlash,
        WhiteFade,
        WhiteTransition
    }

    /// <summary>Ein Basis-Lichtevent (aus der Map).</summary>
    public class LightEvent : MapElement
    {
        public LightEventType Type;
        public LightEventValue Value;
        public float FloatValue = 1f;
        public Easings.EasingType TransitionEasing = Easings.EasingType.Linear;

        /// <summary>Chroma: per-Event gesetzte Farbe (`customData.color`); überschreibt Rot/Blau/Weiß.</summary>
        public bool HasCustomColor = false;
        public Color CustomColor = Color.white;

        public LightEvent() { }

        public LightEvent(float beat, LightEventType type, LightEventValue value, float floatValue)
        {
            Beat = beat;
            Type = type;
            Value = value;
            FloatValue = floatValue;
        }

        public bool IsTransition => Value == LightEventValue.RedTransition
                                 || Value == LightEventValue.BlueTransition
                                 || Value == LightEventValue.WhiteTransition;
    }

    /// <summary>Laser-Drehgeschwindigkeits-Event (Typ 12/13).</summary>
    public class LaserSpeedEvent : LightEvent
    {
        public const float RotationSpeedMult = 20f;

        public float RotationSpeed;
        public bool LockRotation = false;
        public int Direction = -1;
        public List<LaserRotationData> RotationValues = new List<LaserRotationData>();

        public struct LaserRotationData
        {
            public float startPosition;
            public bool direction;
        }

        public LaserSpeedEvent() { }

        public LaserSpeedEvent(LightEvent src)
        {
            Beat = src.Beat;
            Type = src.Type;
            Value = src.Value;
            FloatValue = src.FloatValue;
            RotationSpeed = (int)Value * RotationSpeedMult;
        }

        public void PopulateRotationData(int laserCount, LaserSpeedEvent previous, EnvironmentLightParameters env)
        {
            RotationValues.Clear();
            bool randomize = env == null || env.RandomizeRotatingLasers;
            float startAngle = UnityEngine.Random.Range(0f, 360f);
            bool unifiedDirection = UnityEngine.Random.value >= 0.5f;

            for (int i = 0; i < laserCount; i++)
            {
                var newData = new LaserRotationData();
                if ((int)Value > 0)
                {
                    if (Direction < 0)
                        newData.direction = randomize ? UnityEngine.Random.value >= 0.5f : unifiedDirection;
                    else newData.direction = Direction == 1;

                    if (randomize) newData.startPosition = UnityEngine.Random.Range(0f, 360f);
                    else
                    {
                        float step = (env != null ? env.RotatingLaserStep : 0f) * (newData.direction ? 1 : -1);
                        newData.startPosition = (startAngle + (step * i)) % 360f;
                    }
                }
                else newData.startPosition = 0f;

                if (newData.startPosition < 0) newData.startPosition += 360f;
                if (LockRotation) newData.startPosition = previous?.GetLaserRotation(Time, i) ?? 0f;
                RotationValues.Add(newData);
            }
        }

        public float GetLaserRotation(float currentTime, int laserID)
        {
            if (laserID >= RotationValues.Count) return 0f;
            float timeDifference = currentTime - Time;
            LaserRotationData data = RotationValues[laserID];
            float rotationAmount = RotationSpeed * timeDifference;
            if (data.direction) rotationAmount = -rotationAmount;
            return (data.startPosition + rotationAmount) % 360;
        }

        public float GetLaserRotationWithStep(float currentTime, bool direction, float step, int laserID)
        {
            if (RotationValues.Count == 0) return 0f;
            float timeDifference = currentTime - Time;
            LaserRotationData data = RotationValues[0];
            float rotationAmount = RotationSpeed * timeDifference;
            if (direction) rotationAmount = -rotationAmount;
            float startPosition = data.startPosition + (step * laserID);
            return (startPosition + rotationAmount) % 360;
        }
    }

    /// <summary>Color-Boost-Event.</summary>
    public class BoostEvent : MapElement
    {
        public bool Value;
        public BoostEvent() { }
        public BoostEvent(float beat, bool value) { Beat = beat; Value = value; }
    }

    /// <summary>Farbsatz der Lightshow (Default = ArcViewer `ColorManager.DefaultColors`).</summary>
    public class EnvironmentColorPalette
    {
        public Color LightColor1 = new Color(0.85f, 0.085f, 0.085f, 1f);
        public Color LightColor2 = new Color(0.1882353f, 0.675294f, 1f, 1f);
        public Color WhiteLightColor = new Color(1f, 1f, 1f, 1f);
        public Color BoostLightColor1 = new Color(0.85f, 0.085f, 0.085f, 1f);
        public Color BoostLightColor2 = new Color(0.1882353f, 0.675294f, 1f, 1f);
        public Color BoostWhiteLightColor = new Color(1f, 1f, 1f, 1f);
    }

    /// <summary>Hält den aktuellen Farbsatz (Default Rot/Blau; Map-Farben später möglich).</summary>
    public static class EnvironmentColorManager
    {
        public static EnvironmentColorPalette CurrentColors = new EnvironmentColorPalette();

        public static void SetPalette(EnvironmentColorPalette palette)
        {
            CurrentColors = palette ?? new EnvironmentColorPalette();
        }
    }

    /// <summary>Minimal-Easings (ArcViewer-Namen; Default Linear).</summary>
    public static class Easings
    {
        public enum EasingType { Linear }

        public static float EasingFromType(EasingType type, float t) => t;

        public static class Cubic
        {
            public static float Out(float t) => 1f - Mathf.Pow(1f - t, 3f);
        }

        public static class Expo
        {
            public static float Out(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
        }
    }

    /// <summary>Umgebungs-Parameter (Port aus ArcViewers EnvironmentLightParameters).</summary>
    [Serializable]
    public class EnvironmentLightParameters
    {
        public string EnvironmentName = "DefaultEnvironment";

        public float RotatingLaserCount = 4;
        public bool RandomizeRotatingLasers = true;
        public float RotatingLaserStep = 0f;

        public float SmallRingRotationSpeed = 2f;
        public float SmallRingRotationAmount = 90f;
        public float SmallRingRotationProp = 1f;
        public float SmallRingMaxStep = 5f;
        public float SmallRingStartAngle = -45f;
        public float SmallRingStartStep = 3f;

        public float BigRingRotationSpeed = 2f;
        public float BigRingRotationAmount = 45f;
        public float BigRingRotationProp = 1f;
        public float BigRingMaxStep = 5f;
        public float BigRingStartAngle = -45f;
        public float BigRingStartStep = 0f;

        public float ZoomSpeed = 2f;
        public float CloseZoomStep = 2f;
        public float FarZoomStep = 5f;
        public bool StartRingZoomParity = true;
    }
}
