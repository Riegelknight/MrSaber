// ==============================================================================
//  OcclusionMarker — No-op-Ersatz für ArcViewers Occlusion-Skript.
// ==============================================================================
//  Wird von ArcViewers LaserDataHandler (GPU-Laser-Buffer für den
//  DiffusePlatformShader) genutzt. MrSabers DiffusePlatformShader nutzt diese
//  Buffer nicht mehr — die Datei existiert nur, damit die GUID-treue
//  Szenen-Referenz sauber bindet (sonst „missing script …"-Warnung).
//  Felder 1:1 wie im Original, damit Szenenwerte binden.
// ==============================================================================
using UnityEngine;

namespace BeatSaber.Environment
{
    public class OcclusionMarker : MonoBehaviour
    {
        [System.NonSerialized] public bool Dirty = true;
        [SerializeField] public float intensity = 1f;
        [SerializeField] public float brightnessCap = 0f;
        [SerializeField] public float falloff = 10f;
        [SerializeField] public float falloffSteepness = 1f;

        private void OnEnable()
        {
            // Nach Aktivierung muss der LaserDataHandler den Marker neu berechnen.
            Dirty = true;
        }
    }
}
