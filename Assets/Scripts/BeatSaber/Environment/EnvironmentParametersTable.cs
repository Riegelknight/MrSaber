// ==============================================================================
//  EnvironmentParametersTable — Umgebungs-Parameter je ArcViewer-Umgebung.
// ==============================================================================
//  Werte 1:1 aus ArcViewers EnvironmentManager (`EnvironmentParameters`-Array in
//  Assets/_Scenes/Previewer.unity): Ring-Rotation/Zoom und Laser-Anzahl. Werden
//  beim Umgebungswechsel in EnvironmentLightManager.CurrentParameters gesetzt.
//  Kontext: Architektur siehe PROJECT_NOTES.md (Projektwurzel).
// ==============================================================================
using System.Collections.Generic;

namespace BeatSaber.Environment
{
    public static class EnvironmentParametersTable
    {
        public const string DefaultEnvironmentName = "DefaultEnvironment";

        /// <summary>Alle unterstützten Umgebungs-Szenen (Build Settings).</summary>
        public static readonly string[] Supported =
        {
            "DefaultEnvironment",
            "NiceEnvironment",
            "TriangleEnvironment",
            "KDAEnvironment",
            "MonstercatEnvironment",
            "BigMirrorEnvironment",
            "OriginsEnvironment",
            "DragonsEnvironment",
            "PanicEnvironment",
            "FitBeatEnvironment",
            "KaleidoscopeEnvironment",
            "TimbalandEnvironment",
            "LinkinParkEnvironment",
        };

        private static readonly Dictionary<string, EnvironmentLightParameters> Table = Build();

        /// <summary>Liefert die Parameter der Umgebung (Fallback: Default).</summary>
        public static EnvironmentLightParameters Get(string environmentName)
        {
            if (!string.IsNullOrEmpty(environmentName) && Table.TryGetValue(environmentName, out var p))
                return p;
            return Table[DefaultEnvironmentName];
        }

        /// <summary>Nennt eine Umgebungs-Szene gültig bzw. fällt auf Default zurück.</summary>
        public static string Normalize(string environmentName)
        {
            if (!string.IsNullOrEmpty(environmentName) && Table.ContainsKey(environmentName))
                return environmentName;
            return DefaultEnvironmentName;
        }

        private static Dictionary<string, EnvironmentLightParameters> Build()
        {
            var t = new Dictionary<string, EnvironmentLightParameters>
            {
                // name,                laser, rnd, step, sSpd, sAmt, sProp, sMax, sAng, sStep, bSpd, bAmt, bProp, bMax, bAng, bStep, zSpd, zClose, zFar, zParity
                { "DefaultEnvironment",     P(4,  true,  0, 2, 90, 1f,    5, -45, 3, 2, 45, 1f,    5, -45, 0, 2, 2,     5,    true) },
                { "NiceEnvironment",        P(4,  true,  0, 2, 90, 1f,    5, -45, 3, 2, 45, 1f,    5, -45, 0, 2, 1,     5,    true) },
                { "TriangleEnvironment",    P(7,  true,  0, 2, 90, 1f,    7,  45, 4, 2, 45, 1f,    5, -45, 0, 2, 5,     10,   true) },
                { "KDAEnvironment",         P(7,  true,  0, 0,  0, 0f,    0,   0, 0, 0,  0, 0f,    0,   0, 0, 0, 0,     0,    true) },
                { "MonstercatEnvironment",  P(5,  true,  0, 2, 90, 1f,    5, -45, 3, 2, 45, 1f,    5, -45, 0, 2, 7,     7,    true) },
                { "BigMirrorEnvironment",   P(4,  true,  0, 2, 90, 1f,    5, -45, 3, 2, 45, 1f,    5, -45, 0, 2, 2,     5,    true) },
                { "OriginsEnvironment",     P(5,  true,  0, 2,  0, 1f,    0,   0, 0, 2, 90, 1f,    3, -45, 0, 0, 5,     5,    true) },
                { "DragonsEnvironment",     P(5,  true,  0, 3, 90, 0.5f, 15,  90, 5, 2, 45, 1f,    5, -45, 0, 4, 0.25f, 1.75f,true) },
                { "PanicEnvironment",       P(7,  true,  0, 2, 90, 1f,   20,   0, 5, 2, 45, 1f,    5, -45, 0, 2, 0.25f, 1.5f, true) },
                { "FitBeatEnvironment",     P(8,  false,25, 3, 90, 0.5f, 15,  90, 5, 2, 90, 1f,    5,  45, 0, 4, 0.25f, 1.75f,true) },
                { "KaleidoscopeEnvironment",P(0,  true,  0, 6, 90, 0.75f,18,   0,45, 6, 90, 0.75f,10,   0,45, 4, 1,     3,    true) },
                { "TimbalandEnvironment",   P(10, false,25, 2, 45, 1f,    3,   0, 0, 2,  0, 1f,    0,   0, 0, 2, 2,     4,    true) },
                { "LinkinParkEnvironment",  P(18, true,  5, 2, 90, 1f,    5, -45, 3, 2, 45, 1f,    5, -45, 0, 2, 2,     5,    true) },
            };
            return t;
        }

        private static EnvironmentLightParameters P(
            int laserCount, bool randomize, float step,
            float sSpeed, float sAmount, float sProp, float sMaxStep, float sStartAngle, float sStartStep,
            float bSpeed, float bAmount, float bProp, float bMaxStep, float bStartAngle, float bStartStep,
            float zoomSpeed, float closeStep, float farStep, bool zoomParity)
        {
            return new EnvironmentLightParameters
            {
                RotatingLaserCount = laserCount,
                RandomizeRotatingLasers = randomize,
                RotatingLaserStep = step,
                SmallRingRotationSpeed = sSpeed,
                SmallRingRotationAmount = sAmount,
                SmallRingRotationProp = sProp,
                SmallRingMaxStep = sMaxStep,
                SmallRingStartAngle = sStartAngle,
                SmallRingStartStep = sStartStep,
                BigRingRotationSpeed = bSpeed,
                BigRingRotationAmount = bAmount,
                BigRingRotationProp = bProp,
                BigRingMaxStep = bMaxStep,
                BigRingStartAngle = bStartAngle,
                BigRingStartStep = bStartStep,
                ZoomSpeed = zoomSpeed,
                CloseZoomStep = closeStep,
                FarZoomStep = farStep,
                StartRingZoomParity = zoomParity,
            };
        }
    }
}
