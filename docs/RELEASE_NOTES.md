# Release notes — RapidRAW plugin for Logi Options+

## 0.1.0 — 2026-09-06

First release. Requires the RapidRAW external-control build
(https://github.com/jiyang1018/RapidRAW/releases, `1.6.3-ctl.1` or later) and Logi Options+.

**Install:** run `RapidRawPlugin-Setup-0.1.0.exe` (per-user, no admin). It registers the
plugin with the Logi Plugin Service and asks Options+ to load it. Then start RapidRAW and
assign `RapidRAW → Develop` sliders to the Dialpad — the README has a screenshot
walkthrough.

### What's in it

* ~110 develop sliders (Basic, Color, HSL, Color grading, Details, Effects, Transform) as
  dial actions with live readouts; the RapidRAW preview follows the dial.
* Key actions: Undo, Redo, Before/after, Previous/Next image, Rate 0–5, colour labels,
  Zoom fit / 100 %, panel toggles, per-slider resets.
* **Reset (then turn dial)**: press the key, then move the dial you mean — that movement
  resets the slider (the MX dial can't be pressed, and Options+ has no "reset current
  dial" action).
* Exposure and Brightness step 0.05 EV per detent; everything else one native step.
* Original icon set for every action. Optional, user-run **Apply Lightroom icons** /
  **Restore original icons** Start Menu shortcuts borrow icons from a locally installed
  "Lightroom Classic by Logi" — nothing of Logitech's ships in this package.

### Known limits

* Windows only for the installer; macOS builds from source with `tools/build-plugin.sh`
  (untested on a Mac so far).
* Loupedeck CT/Live are declared but untested.
* Masks, curves and per-mask sliders are not exposed by the RapidRAW API yet.
