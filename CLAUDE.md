# pincushionedSpring2026

Unity 6 (6000.3.7f1), URP project.

## Graphics API — MUST USE D3D11
This project must run under **Direct3D 11**, not D3D12. D3D12 causes unrecoverable GPU device loss (TDR crash) due to conflicts between Unity's D3D12 device and the native D3D11 plugins (Adobe Substance, KlakHap). This is already enforced in `ProjectSettings/ProjectSettings.asset` (`WindowsStandaloneSupport` → D3D11 only). If crashes return, add `-force-d3d11` to Unity Hub → project → Advanced Settings → Additional command line arguments.

## Scene Loading — Open Manually
The main scene (`pincushionededed.unity`) is heavy. **Always open Unity first to an empty scene**, then open the main scene manually. If Unity starts auto-loading the heavy scene and crashes, delete `Library/LastSceneManagerSetup.txt` and relaunch. An 8-floor vertical building where each floor contains a different isometric scene, navigated via MIDI controllers.

## Project Structure

```
Assets/
  Scenes/pincushionededed.unity          Main scene (MidiSceneBootstrapper on "GameObject")
  midiSupport/                           MIDI package + sample scripts
    midiFighterForUnity-.../Runtime/     Package runtime (MidiEventManager, routers, input maps)
    Samples/TestScene/                   Active scripts (bootstrapper, controllers, spawners)
    Samples/Resources/                   ScriptableObject assets (build-safe)
  proceduralPincushioning/               GPU-instanced pin scatter system (see its CLAUDE.md)
  Plugins/Demigiant/DOTween/             DOTween Pro (DLL, no asmdef, globally accessible)
3DObjectProcessing/                      Python scan2unity pipeline (see its CLAUDE.md)
```

## MIDI Hardware

- **Akai MIDI Mix** — 8-channel mixer (24 knobs, 8+1 faders, 24 buttons)
- **DJ Tech Tools Midi Fighter 64** — 8x8 button grid (64 buttons)
- Both connected via USB; WinMM exclusive access (close DAWs before Play)
- Use a **powered USB 3.0 multi-TT hub** for both devices simultaneously

## Floor Navigation

- 8 floors, each 8 units tall (floor 0 = Y 0, floor 7 = Y 56)
- Single Main Camera, scene-placed; only Y position changes between floors
- MF64 column 8 (rightmost): row 1 (top) = floor 7, row 8 (bottom) = floor 0
- DOTween InOutCubic ease, 0.6s default duration
- Camera X, Z, rotation are fixed

## Key Scripts (Assets/midiSupport/Samples/TestScene/)

| Script | Role |
|--------|------|
| `MidiSceneBootstrapper.cs` | Creates all core components on Awake |
| `FloorCameraController.cs` | MF64 col-8 -> DOTween vertical camera movement |
| `MidiFighterInteriorSpawner.cs` | MF64 cols 1-7 -> toggle interior prefab instances |
| `MidiMixCloner.cs` | MIDI Mix Row2 knobs -> DrawMeshInstanced cloning |
| `MidiMixDataVisualizer.cs` | MIDI Mix Row3-Ch1 -> TMP label cloud density |
| `MidiDebugUI.cs` | MIDI device status + raw event log overlay |

## Subsystem Docs

Each major subsystem has its own CLAUDE.md with detailed docs:
- `Assets/midiSupport/midiFighterForUnity-claude-add-midi-test-scene-vtbqj/CLAUDE.md` — MIDI package internals, event flow, control mappings, note layouts
- `Assets/proceduralPincushioning/CLAUDE.md` — GPU scatter system, bake pipeline, render pass
- `3DObjectProcessing/CLAUDE.md` — Python mesh processing, Smithsonian API, Blender bridge

## Conventions

- New standalone scripts preferred over bloating existing ones
- 1-based everywhere user-facing (row, col, channel); 0-based only in internal arrays
- Namespace: `MidiFighter64` (Runtime), `MidiFighter64.Samples` (Samples)
- Always add `using MidiFighter64;` explicitly in Samples files
- ScriptableObject + `[InitializeOnLoad]` + Resources.Load pattern for build-safe asset refs
