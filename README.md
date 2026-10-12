# MrSaber - Mixed-Reality Beat Saber for Pico 4 (+VR Mode)

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
1. **Install APK:**
   - With your PC: Copy `MrSaber.apk` to the **Download** folder of your Pico (drag and drop is possible).
   - In your Pico: Go to the **Download** folder and install the APK.
   - Game can now be launched from your library under **Unknown Sources**.
2. **Add Songs:**
   - Download songs: <https://beatsaver.com>
   - Place them in: `/storage/emulated/0/ModData/MrSaber/CustomLevels`
     or `/storage/emulated/0/ModData/com.beatgames.beatsaber/Mods/SongCore/CustomLevels` (from the original game)
   - These folders are located outside the app sandbox → External storage permission is required once.
   - Demo song: "Death to Disco" by Freeek
3. **Disable Guardian:** 
   - Enable Developer Mode: Pico Settings → *General* → *About* → Click **Software Version** **7 times**.
   - Then go to Pico Settings → *Developer* → Turn off **Boundary / Guardian** to move freely around your space.
     
<img width="1920" height="1080" alt="lied auswahl" src="https://github.com/user-attachments/assets/9cd20cd8-0f4f-4382-865e-ecdee77781a8" />

## Controller Mapping
| Input | Action |
|---|---|
| Y / B | Open menu (+ Pause) / Close |
| X | Next song |
| A | Recenter room |
| Trigger (right/left) | Menu confirmation when pointing at an item |
| Joystick (both) | Scroll song list up/down by one entry |
| Left Joystick Up/Down | Move playfield up/down |
| Right Joystick Left/Right | Rotate room |
| Y / B ≥ 2 s | Exit game |

<img width="1920" height="1080" alt="menü" src="https://github.com/user-attachments/assets/b4a5d3b9-cd0b-44fb-95cd-33b837637bef" />
<img width="1920" height="1080" alt="spieler rechts" src="https://github.com/user-attachments/assets/4615e169-afcb-4f81-8c5a-650e62aa898c" />
<img width="1920" height="1080" alt="spielerlinks" src="https://github.com/user-attachments/assets/c7a4d073-02e3-4f0a-b589-3708fd0de153" />

## Build details
1. Unity 2022.3.62f3 with Android Build Support – Package: `com.unity.template.mixedreality` — <https://unity.com/releases/editor/archive>
2. `Packages/manifest.json` → Check PICO SDK path (SDK: <https://github.com/Pico-Developer/PICO-Unity-Integration-SDK>, Release `release_3.0.0`)
3. Bundle `1.0.0`, minSdk (Android) 29

## Origins & Credits
| Source | Adopted / Used |
|---|---|
| [AllPoland/ArcViewer](https://github.com/AllPoland/ArcViewer) | Environment scenes (`Assets/ArcViewerEnvironmentScenes`) + Shaders |
| [s4k10503/BeatSaberClone](https://github.com/s4k10503/BeatSaberClone) | Note-cutting physics |
| [sidequestlegend/supersaber](https://github.com/sidequestlegend/supersaber) | Score calculation |

**Additional References:**
- [BeatLeader/BeatSaber-Web-Replays](https://github.com/BeatLeader/BeatSaber-Web-Replays)
- [dhcdht/OpenSaberVR](https://github.com/dhcdht/OpenSaberVR)
- [CoffeeAt0900/OpenSaberPlus](https://github.com/CoffeeAt0900/OpenSaberPlus)

This project was created with the open-source AI agent platform **OpenCode ([opencode.ai](https://opencode.ai/))**, primarily utilizing the BigPickle model (free).

## License
**GPL-3.0** – see [`LICENSE`](LICENSE). Free to use, modify, and distribute; derivative works must also be licensed under GPL-3.0.
