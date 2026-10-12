// ==============================================================================
//  ArcViewerAssetPostprocessor — mobile-freundlicher Import der übernommenen
//  ArcViewer-Umgebungs-Assets (PICO/Android).
// ==============================================================================
//  Best Practice für Android/XR: Texturen komprimiert (ASTC 6×6) und in der Größe
//  gekappt → weniger Speicher & Bandbreite (flüssiger, weniger Hänger). Betrifft NUR
//  Assets/ArcViewerEnvironment/Textures/** (unsere Umgebungs-Texturen), nicht das
//  restliche Projekt. `GetVersion` erzwingt eine einmalige Neu-Import-Runde, wenn
//  diese Datei geändert wird.
//  Kontext: Architektur siehe PROJECT_NOTES.md (Projektwurzel).
// ==============================================================================
using UnityEditor;
using UnityEngine;

namespace BeatSaber.EditorTools
{
    public sealed class ArcViewerAssetPostprocessor : AssetPostprocessor
    {
        private const string TargetFolder = "Assets/ArcViewerEnvironment/Textures/";
        private const int MaxSize = 1024;

        /// <summary>Erzwingt Re-Import, wenn sich der Postprocessor ändert.</summary>
        public override uint GetVersion() => 2;

        private void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(TargetFolder, System.StringComparison.Ordinal)) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.maxTextureSize = Mathf.Min(importer.maxTextureSize, MaxSize);
            importer.mipmapEnabled = true;
            importer.anisoLevel = 1;

            // Android: ASTC 6×6 (Adreno/PICO unterstützt ASTC) — deutlich kleinere Texturen.
            var android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = MaxSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.Compressed;
            android.crunchedCompression = false;
            importer.SetPlatformTextureSettings(android);
        }
    }
}
