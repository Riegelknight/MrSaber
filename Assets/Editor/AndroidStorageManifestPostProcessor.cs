// ==============================================================================
//  AndroidStorageManifestPostProcessor — ergänzt READ_EXTERNAL_STORAGE im
//  generierten Android-Manifest.
// ==============================================================================
//  Warum: MrSaber liest die Lieder aus dem SongCore-Ordner der offiziellen Beat Saber
//  (`/storage/emulated/0/ModData/…`) AUSSERHALB der App-Sandbox. Auf Android 10 ist
//  dafür das Lesen der externen Speicher-Berechtigung nötig. WRITE setzt Unity über
//  PlayerSettings.Android.forceSDCardPermission (ProjectSettings.asset:
//  ForceSDCardPermission: 1); READ ergänzt dieser Post-Processor, weil es dafür keinen
//  Player-Setting-Schalter gibt. Idempotent (fügt nur ein, wenn noch nicht vorhanden).
// ==============================================================================
using System.IO;
using UnityEditor.Android;

namespace BeatSaber.EditorTools
{
    /// <summary>
    /// Hängt nach dem Erzeugen des Gradle-Projekts die Lese-Berechtigung für externen
    /// Speicher ans App-Manifest (siehe Datei-Kopf).
    /// </summary>
    public sealed class AndroidStorageManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        // Nach Unitys eigener Manifest-Nachbearbeitung laufen (Standard 0 reicht,
        // höher schadet nicht).
        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest))
            {
                // Manche Unity-Versionen übergeben die Gradle-Wurzel statt des
                // unityLibrary-Moduls.
                manifest = Path.Combine(path, "unityLibrary", "src", "main", "AndroidManifest.xml");
                if (!File.Exists(manifest)) return;
            }

            string xml = File.ReadAllText(manifest);
            const string permission = "android.permission.READ_EXTERNAL_STORAGE";
            if (xml.Contains(permission)) return;

            // Vor dem <application>-Tag einfügen (gültig auf Manifest-Ebene).
            xml = xml.Replace("<application",
                "<uses-permission android:name=\"" + permission + "\" />\n    <application");
            File.WriteAllText(manifest, xml);
        }
    }
}
