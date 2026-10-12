# MrSaber - Mixed-Reality-Beat-Saber für Pico 4 (+VR-Modus)

<a href="https://youtu.be/XaOJp0y6dqE" target="_blank">
  <img width="480" height="270" alt="Inliner" src="https://github.com/user-attachments/assets/6f68cf4b-d548-4920-9054-88f4d70f5e92" />
</a>

https://youtu.be/XaOJp0y6dqE

## Downloads
- <https://github.com/Riegelknight/MrSaber/releases/tag/v1.0>
- Installation: `adb install -r MrSaber-v1.0.apk`
  
<img width="1920" height="1080" alt="arc" src="https://github.com/user-attachments/assets/84e6dc49-0374-4d8b-abbf-31d215a75a08" />
<img width="1920" height="1080" alt="combo" src="https://github.com/user-attachments/assets/89b5700c-bd06-46f1-b1ec-c8c0eb4d737e" />

## Installation & Setup
1. **APK installieren:**
   - Am PC: Kopiere `MrSaber.apk` in den **Download**-Ordner deiner Pico (Drag and Drop möglich).
   - In der Pico: Gehe in den **Download**-Ordner und installiere die APK.
   - Das Spiel kann nun aus deiner Bibliothek unter **Unbekannte Quellen** gestartet werden.
2. **Lieder einfügen:**
   - Lieder herunterladen: <https://beatsaver.com>
   - Einfügen in: `/storage/emulated/0/ModData/MrSaber/CustomLevels`
     oder `/storage/emulated/0/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels` (vom Originalspiel)
   - Die Ordner liegen außerhalb der App-Sandbox → Externe Speicherberechtigung einmal erforderlich.
   - Demo-Lied: „Death to Disco" von Freeek
3. **Guardian deaktivieren:** 
   - Entwicklermodus freischalten: Pico Einstellungen → *Allgemein* → *Über das Gerät* → **7-mal** auf die **Software-Version** klicken.
   - Danach unter Pico Einstellungen → *Entwickler* → **Sicherheitsgrenze (Boundary / Guardian)** ausschalten, um dich frei im Raum zu bewegen.
     
<img width="1920" height="1080" alt="lied auswahl" src="https://github.com/user-attachments/assets/9cd20cd8-0f4f-4382-865e-ecdee77781a8" />

## Controller-Belegung
| Eingabe | Aktion |
|---|---|
| Y/B | Menü öffnen (+ Pause) / schließen |
| X | Nächster Song |
| A | Raum neu ausrichten |
| Trigger (rechts/links) | Menübestätigung beim Daraufzeigen |
| Joystick (beide) | Songliste einen Eintrag hoch-/runterblättern |
| Linker Joystick hoch/runter | Spielfeld anheben/absenken |
| Rechter Joystick links/rechts | Raum drehen |
| Y / B ≥ 2 s | Spiel beenden |

<img width="1920" height="1080" alt="menü" src="https://github.com/user-attachments/assets/b4a5d3b9-cd0b-44fb-95cd-33b837637bef" />
<img width="1920" height="1080" alt="spieler rechts" src="https://github.com/user-attachments/assets/4615e169-afcb-4f81-8c5a-650e62aa898c" />
<img width="1920" height="1080" alt="spielerlinks" src="https://github.com/user-attachments/assets/c7a4d073-02e3-4f0a-b589-3708fd0de153" />

## Build Informationen
1. Unity 2022.3.62f3 mit Android Build Support – Package: `com.unity.template.mixedreality` — <https://unity.com/releases/editor/archive>
2. `Packages/manifest.json` → PICO-SDK-Pfad prüfen (SDK: <https://github.com/Pico-Developer/PICO-Unity-Integration-SDK>, Release `release_3.0.0`)
3. Bundle `1.0.0`, minSdk (Android)29

## Herkunft & Credits
| Quelle | Übernommen |
|---|---|
| [AllPoland/ArcViewer](https://github.com/AllPoland/ArcViewer) | Umgebungs-Szenen (`Assets/ArcViewerEnvironmentScenes`) + Shader |
| [s4k10503/BeatSaberClone](https://github.com/s4k10503/BeatSaberClone) | Noten-Schnittphysik |
| [sidequestlegend/supersaber](https://github.com/sidequestlegend/supersaber) | Punkte berechnung|

**Weitere Referenzen:**
- [BeatLeader/BeatSaber-Web-Replays](https://github.com/BeatLeader/BeatSaber-Web-Replays)
- [dhcdht/OpenSaberVR](https://github.com/dhcdht/OpenSaberVR)
- [CoffeeAt0900/OpenSaberPlus](https://github.com/CoffeeAt0900/OpenSaberPlus)

Dieses Projekt wurde mit der Open-Source-KI-Agentenplattform **OpenCode ([opencode.ai](https://opencode.ai/))** realisiert, hauptsächlich mit dem BigPickle-Modell (kostenlos).

## Lizenz
**GPL-3.0** – siehe [`LICENSE`](LICENSE). Frei verwendbar, änderbar, weitergebbar; abgeleitete Werke müssen ebenfalls unter der GPL-3.0 stehen.
