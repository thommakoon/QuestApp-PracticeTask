# QuestApp-PracticeTask

Unity Fitts-law practice app for **Meta Quest**, used in the gazeGait study. Receives mapped eye gaze from the PC **OpenEye** GUI over TCP and supports eye / head / hand cursor conditions with dwell and pinch selection.

Part of [gazeGait](https://github.com/thommakoon/gaze-gait-process) as a git submodule. OpenEye (calibration + gaze streaming) lives in [thommakoon/OpenEye](https://github.com/thommakoon/OpenEye).

## Publication

> **TODO:** Add paper citation and DOI when available.

## Android package

| Item | Value |
|------|--------|
| Package | `com.PracticeMG.MRstressPRACTICE` |
| Product name | `MRstressPRACTICE` |
| Unity | 2022.2.9f1 |

## Study flow (runtime)

1. **PREP** — start button (dwell on center target).
2. **BEFORE_TRIAL** — condition shown.
3. **TRIAL** — Fitts ring (11 targets) or menu grid; data logged.
4. **AFTER_TRIAL** — JSON saved; next condition.

**Conditions** (order counterbalanced by subject id): `EyeDwell`, `EyePinch`, `HeadDwell`, `HeadPinch`.

| Parameter | Value | Source |
|-----------|--------|--------|
| Dwell to select | 1.0 s | `TargetBehaviour.MAX_DWELL_TIME` |
| Per-target timeout | 5.0 s | `FittsLaw.TIMEOUT` |
| Fitts ring targets | 11 | `ControlTargets` / `FittsLaw(11)` |

## OpenEye integration

- **TCP client:** `OpenEyeGazeReceiver` → PC port **5051** (`gazeVisual`, `launchApp`).
- **Eye gaze:** `EyeGazeProvider` (source = OpenEye) maps PC gaze to a head-fixed ray.
- **Handoff to OpenEye calib:** `OpenEyeHandoff` on PC **Recalibrate** (`launchApp` → `org.MixedRealityToolkit.MRTK3Sample`).

Set the PC IP on the `OpenEyeGazeReceiver` component in the scene (or default in script).

Requires OpenEye Quest APK installed for recalibration handoff. Android `<queries>` for OpenEye package is in `Assets/Plugins/Android/AndroidManifest.xml`.

## Trial logging (fixed rate)

During `SCENE.TRIAL`, `GameManager` samples at a fixed rate (default **100 Hz**), not every Unity frame.

| Inspector field | Default |
|-----------------|--------|
| `GameManager` → **Trial Log Rate Hz** | `100` |

Each sample stores wall-clock time (`unixTimeMilliseconds`), head pose, cursor ray, target index (`start_num` / `end_num`), and `sample_seq`.

### Output location (Quest)

```
/storage/emulated/0/Android/data/com.PracticeMG.MRstressPRACTICE/files/<sub_num>-<subsub_num>/
```

### JSON format

Files use a metadata envelope (not a bare array):

```json
{
  "file_name": "subject0_subsubNum0_cursorEye_SelectionDwell_repetition0_...OOOO",
  "sub_num": 0,
  "subsub_num": 0,
  "log_sample_rate_hz": 100,
  "data": [
    {
      "timestamp": 142.318,
      "unixTimeMilliseconds": 1751961234567,
      "sample_seq": 0,
      "head_origin": { "x": 0, "y": 0, "z": 0 },
      "cursorData": { "cursor_type": "EYE", "origin": {}, "direction": {} },
      "target_position": {},
      "cursor_angular_distance": 12.3,
      "start_num": 5,
      "end_num": 0,
      "step_num": 5
    }
  ]
}
```

Per-target success (`O` / `X`) is appended to the filename after the block ends.

**Not logged yet (planned):** explicit events (`target_on`, `dwell_start`, `hit`, `timeout`).

## Build

1. Open project in **Unity 2022.2.9f1** (or compatible 2022.2.x).
2. Open scene `Assets/Scenes/PracticeScenes.unity`.
3. **File → Build Settings → Android** → build APK or deploy to Quest.
4. Install alongside OpenEye APK when running eye conditions with PC gaze.

## Quick test: verify sample rate

After a short trial on device:

```bash
adb shell "ls /storage/emulated/0/Android/data/com.PracticeMG.MRstressPRACTICE/files/*/*/"
adb pull /storage/emulated/0/Android/data/com.PracticeMG.MRstressPRACTICE/files/0-0/ ./practice_json/
```

```python
import json, numpy as np
from pathlib import Path

rec = json.loads(next(Path("practice_json").glob("*.json")).read_text())
frames = rec["data"]
dt = np.diff([f["unixTimeMilliseconds"] for f in frames])
print("nominal Hz:", rec["log_sample_rate_hz"])
print("median dt (ms):", np.median(dt))
print("expected ms:", 1000 / rec["log_sample_rate_hz"])
```

At 100 Hz, median Δt should be ~**10 ms**.

## Key scripts

| File | Role |
|------|------|
| `GameManager.cs` | Scenes, fixed-rate logging, save |
| `StudyDesign.cs` | Conditions, `FittsLaw`, `FrameData`, JSON envelope |
| `TargetBehaviour.cs` | Dwell / click, trial advance |
| `CursorController.cs` | Eye / head / hand ray |
| `EyeGazeProvider.cs` | OpenEye gaze ray |
| `OpenEyeGazeReceiver.cs` | TCP client |
| `OpenEyeHandoff.cs` | Launch OpenEye for recalibration |

## Related repos

| Repo | Role |
|------|------|
| [thommakoon/OpenEye](https://github.com/thommakoon/OpenEye) | PC GUI + Quest calib APK, gaze map, TCP server |
| [thommakoon/gaze-gait-process](https://github.com/thommakoon/gaze-gait-process) | Parent monorepo (analysis, motorola, submodules) |
