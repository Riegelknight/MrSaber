# MrSaber - Mixed Reality Beat Saber for Pico 4 (+VR Mode)

**English** | [Deutsch](README.de.md)

<a href="https://youtu.be/XaOJp0y6dqE" target="_blank">
  <img width="480" height="270" alt="Inliner" src="https://github.com/user-attachments/assets/6f68cf4b-d548-4920-9054-88f4d70f5e92" />
</a>

https://youtu.be/XaOJp0y6dqE

## Installation

1. Download the APK using the Pico Browser, or transfer it from your PC into your Pico's Download folder: <https://github.com/Riegelknight/MrSaber/releases/latest>
2. Install via **File Manager** -> **Download** -> `MrSaber-v1.1.apk`.
3. The game will now appear in your library under **Unknown Sources**.

Demo song: "Death to Disco" by Freeek

### Adding Songs

1. Download a song via the Pico Browser, or transfer it from your PC into your Download folder: <https://beatsaver.com>.
2. Start MrSaber.

Wow, that's it?! Songs are automatically unpacked (if necessary) and moved to `/storage/emulated/0/ModData/MrSaber/CustomLevels`.

<img width="1920" height="1080" alt="arc" src="https://github.com/user-attachments/assets/84e6dc49-0374-4d8b-abbf-31d215a75a08" />
<img width="1920" height="1080" alt="combo" src="https://github.com/user-attachments/assets/89b5700c-bd06-46f1-b1ec-c8c0eb4d737e" />

### Disabling the Guardian

1. Unlock Developer Mode: Pico Settings → *General* → *About* → Click **7 times** on the **Software Version**.
2. Then go to Pico Settings → *Developer* → Turn off **Boundary (Guardian)**.

<img width="1920" height="1080" alt="lied auswahl" src="https://github.com/user-attachments/assets/9cd20cd8-0f4f-4382-865e-ecdee77781a8" />

## Controller Mapping

| Input | Action |
|---|---|
| Y / B | Open menu (+ Pause) / Close menu |
| X | Next song |
| A | Recenter room |
| Trigger (Right/Left) | Menu confirmation when pointing at it |
| Joystick (Both) | Scroll song list up/down by one entry |
| Left Joystick Up/Down | Raise/Lower play area |
| Right Joystick Left/Right | Rotate room |
| Y / B ≥ 2 s | Exit game |

<img width="1920" height="1080" alt="menü" src="https://github.com/user-attachments/assets/b4a5d3b9-cd0b-44fb-95cd-33b837637bef" />
<img width="1920" height="1080" alt="spieler rechts" src="https://github.com/user-attachments/assets/4615e169-afcb-4f81-8c5a-650e62aa898c" />
<img width="1920" height="1080" alt="spielerlinks" src="https://github.com/user-attachments/assets/c7a4d073-02e3-4f0a-b589-3708fd0de153" />

## Build Information

1. Unity 2022.3.62f3 with Android Build Support – Package: `com.unity.template.mixedreality` — <https://unity.com/releases/editor/archive>
2. `Packages/manifest.json` → Check PICO SDK path (SDK: <https://github.com/Pico-Developer/PICO-Unity-Integration-SDK>, Release `release_3.0.0`)
3. Bundle `1.1.0`, minSdk (Android) 29

## Origins & Credits

| Source | Adopted |
|---|---|
| [AllPoland/ArcViewer](https://github.com/AllPoland/ArcViewer) | Environment scenes + shaders, level grid, arcs/chains, score utils |
| [s4k10503/BeatSaberClone](https://github.com/s4k10503/BeatSaberClone) | Visual note slicing physics |
| [CoffeeAt0900/OpenSaberPlus](https://github.com/CoffeeAt0900/OpenSaberPlus) | Swing-angle measurement, scoring system |

**Other sources:** [SuperSaber](https://github.com/sidequestlegend/supersaber), [BeatSaber-Web-Replays](https://github.com/BeatLeader/BeatSaber-Web-Replays), [OpenSaberVR](https://github.com/dhcdht/OpenSaberVR)

This project was built using the open-source AI agent platform **OpenCode ([opencode.ai](https://opencode.ai/))**, primarily utilizing the BigPickle model (free).

## License

**GPL-3.0** – see [`LICENSE`](LICENSE). Free to use, modify, and distribute; derivative works must also be licensed under GPL-3.0.
