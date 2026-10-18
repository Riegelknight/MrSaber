using UnityEngine;

namespace BeatSaber
{
    public static class ProjectInfo
    {
        // ---------- Unity / Render Pipelines (siehe ProjectVersion.txt, manifest.json) ----------
        public const string UnityVersion = "2022.3.62f3";       // 96770f904ca7
        public const string UrpVersion = "14.0.12";             // com.unity.render-pipelines.universal

        // ---------- XR-Stapel (siehe manifest.json) ----------
        public const string XrManagementVersion = "4.5.1";      // com.unity.xr.management
        public const string OpenXrVersion = "1.14.3";           // com.unity.xr.openxr
        public const string XrInteractionToolkitVersion = "3.1.2"; // com.unity.xr.interaction.toolkit
        public const string XrHandsVersion = "1.5.1";           // com.unity.xr.hands
        public const string InputSystemVersion = "1.14.0";      // com.unity.inputsystem
        public const string TextMeshProVersion = "3.0.9";       // com.unity.textmeshpro

        // ---------- PICO SDK (com.unity.xr.picoxr) ----------
        /// <summary>Package-Version aus manifest.json (file:-Referenz auf den SDK-Ordner).</summary>
        public const string PicoxrScopedVersion = "3.0.4";
        /// <summary>Release-Name des lokalen SDK-Ordners (PICO-Unity-Integration-SDK).</summary>
        public const string PicoxrReleaseName = "PICO Unity Integration SDK release 3.0.0";

        public const string DeviceName = "PICO 4";
        public const string PicoFirmwareVersion = "unbekannt — auf dem Gerät ablesen und eintragen";

        /// <summary>
        /// Loggt alle Versionsstände beim Spielstart (wird in GameBootstrap.Boot gerufen).
        /// Auf Android wird zusätzlich die native PICO-SDK-Version abgefragt.
        /// </summary>
        public static void LogVersions()
        {
            Debug.Log(
                $"[BeatSaber] Versionen — Unity {UnityVersion} | URP {UrpVersion} | " +
                $"XRI {XrInteractionToolkitVersion} | OpenXR {OpenXrVersion} | " +
                $"XR Management {XrManagementVersion} | XR Hands {XrHandsVersion} | " +
                $"Input System {InputSystemVersion} | TMPro {TextMeshProVersion} | " +
                $"PICO-Package {PicoxrScopedVersion} ({PicoxrReleaseName}) | " +
                $"Gerät {DeviceName} | Firmware {PicoFirmwareVersion}");

            // Laufzeit-Hardware: für die Passthrough-Frage entscheidend —
            // PICO 4 = EINE Farbkamera (Mono-Passthrough), PICO 4 Ultra = ZWEI RGB-Kameras.
            Debug.Log($"[BeatSaber] Hardware (Unity) — Modell {SystemInfo.deviceModel} | " +
                      $"Name {SystemInfo.deviceName} | OS {SystemInfo.operatingSystem} | " +
                      $"GPU {SystemInfo.graphicsDeviceName}");

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                string sdk = Unity.XR.PXR.PXR_System.GetSDKVersion();
                string product = Unity.XR.PXR.PXR_System.GetProductName();
                Debug.Log($"[BeatSaber] PICO nativ — SDK {sdk} | Produkt {product}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[BeatSaber] PICO-SDK-Version nicht abrufbar: " + ex.Message);
            }
#endif
        }
    }
}
