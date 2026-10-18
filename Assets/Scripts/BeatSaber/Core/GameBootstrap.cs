using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BeatSaber
{
    /// <summary>
    /// Startet das Beat-Saber-Spiel automatisch in der SampleScene (kein Szenen-YAML-Eingriff nötig):
    /// erzeugt den GameManager; die vorhandene Passthrough-Kette (PicoSeeThrough) bleibt unangetastet.
    /// </summary>
    public static class GameBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            try
            {
                string scene = SceneManager.GetActiveScene() != null
                    ? SceneManager.GetActiveScene().name
                    : "";
                if (string.IsNullOrEmpty(scene) || !scene.StartsWith("SampleScene", StringComparison.Ordinal))
                {
                    Debug.Log("[BeatSaber] Keine SampleScene -> Boot übersprungen.");
                    return;
                }

                ProjectInfo.LogVersions();

                var existing = GameObject.Find("[BeatSaberGame]");
                if (existing != null) return;

                var go = new GameObject("[BeatSaberGame]");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<GameManager>();
                Debug.Log("[BeatSaber] Gestartet.");
            }
            catch (Exception ex)
            {
                Debug.LogError("[BeatSaber] Boot fehlgeschlagen: " + ex);
            }
        }
    }
}
// refresh-touch
