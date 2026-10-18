// ==============================================================================
//  MainSceneAutoLoad — No-op-Ersatz für ArcViewers Utility-Skript.
// ==============================================================================
//  Die übernommenen Umgebungs-SZENEN tragen dieses Skript am Szenen-Wurzelobjekt
//  (ArcViewer lädt damit beim Direktspielen einer Umgebung die Hauptszene). In
//  MrSaber ist das nicht nötig — die Datei existiert nur, damit die GUID-treue
//  Szenen-Referenz sauber bindet (sonst „missing script …"-Warnung).
// ==============================================================================
using UnityEngine;

namespace BeatSaber.Environment
{
    public class MainSceneAutoLoad : MonoBehaviour
    {
    }
}
