# RapidRAW plugin for Logi Options+ (MX Creative Console)

Drives [RapidRAW](https://github.com/CyberTimon/RapidRAW)'s develop sliders live from the
MX Creative Console Dialpad, with undo/redo, image navigation, star ratings, colour labels
and view toggles on the Keypad / Actions Ring. Talks to the **external control API** added
in the RapidRAW fork (`docs/EXTERNAL_CONTROL_API.md` there): TCP `127.0.0.1:47820`,
newline-delimited JSON, loopback only.

Built on the same Logi Actions SDK scaffolding as the Adobe Bridge plugin, minus everything
XMP: RapidRAW pushes state, so the dials show live values and the preview follows the dial.

## What you need

1. **RapidRAW with external control** — the fork build. Its log
   (Settings → Data → View Application Logs) must say
   `External control: listening on 127.0.0.1:47820`.
2. **Logi Options+** with the Logi Plugin Service (installed with Options+; provides
   `PluginApi.dll`). Options+ 6.4 runs on .NET 10, so:
3. **.NET 10 SDK** (the SDK, not the runtime). `tools\build-plugin.ps1` reads the exact
   version the service runs on and tells you which SDK to install if it is missing.

## Build and sideload

```
powershell -ExecutionPolicy Bypass -File tools\build-plugin.ps1
```

The script checks every prerequisite with a fix-it message, builds, copies the package
into `bin\Debug\`, writes `%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\RapidRawPlugin.link`
pointing at it, and asks the service to reload the plugin. Re-run it after any C# change —
Options+ picks the new build up without a restart (if it doesn't, quit Options+ from the
tray and reopen). `dotnet clean src\RapidRawPlugin.csproj` removes the sideload.

### macOS

Same prerequisites (Logi Options+ with the plugin service, and the .NET SDK matching the
service's runtime). Then:

```
bash tools/build-plugin.sh            # Debug build + sideload
bash tools/build-plugin.sh --release
bash tools/build-plugin.sh --check-only
```

It finds `PluginApi.dll` inside the LogiPluginService app bundle, writes
`~/Library/Application Support/Logi/LogiPluginService/Plugins/RapidRawPlugin.link`, and
reloads the plugin. The macOS paths (bundle location, `GetBundleName` returning
`io.github.CyberTimon.RapidRAW`) are taken from Tauri's config and the Windows plugin's
csproj comments; they have not yet been exercised on a Mac, so the first run is the test.
`use-logi-icons.ps1` is Windows-only for now.

## Set up the console

1. Start RapidRAW and open an image in the editor. The plugin's status in Options+
   (Plugins → RapidRAW) should flip from "RapidRAW not reachable" to Normal within ~2 s.
   `logs\logi-rapidraw-plugin.log` in this repo (or `%TEMP%` if not running from the sideload)
   says `Connected to RapidRAW.` and
   `Parameter table received: N params.`
2. In Options+, select the MX Creative Console, pick the **RapidRAW** profile (it is
   created automatically for the app and activates whenever RapidRAW is in front), or add
   the actions to any profile.
3. **Dialpad**: drag `RapidRAW → Develop → Exposure` (or any of the ~110 sliders, grouped
   Basic / Color / HSL / Color grading / Details / Effects / Transform) onto the dial or
   roller. Turn: the RapidRAW preview follows at low resolution while you turn and renders
   full quality when you stop; the slider in RapidRAW's panel moves with it. The dial
   readout shows the live value (`+0.35 EV`, `-12`, `180°`), `--` with no image open, `off`
   when RapidRAW isn't running. The MX dial cannot be pressed and Options+ never tells a
   plugin which Actions Ring item is highlighted (verified 2026-09-05: no encoder, value,
   label or image request reaches the plugin on hover — only on rotation), so reset is
   **press, then turn**: put `RapidRAW → Actions → Edit → Reset (then turn dial)` on a
   dialpad button. Press it (the key reads `RESET / turn dial`), then move the dial you
   mean; that movement resets the slider instead of adjusting it. Nothing within 5 s, or a
   second press, cancels. `Actions → Reset → Basic/Color/Details` has fixed per-slider reset
   keys as well. On devices whose dials do press (Loupedeck CT/Live), the press resets too.
4. **Keypad / Actions Ring**: `RapidRAW → Actions` — Undo, Redo, Before/after, Previous /
   Next image, Rate 0–5, Label red…purple, Zoom fit / 100 %, panel toggles. Rating keys
   show the current stars. Every action is the same handler RapidRAW's own keyboard
   shortcut uses, guards included (Next image does nothing in the library view, and the
   plugin log notes it as `ignored`).
5. Exposure and Brightness move 0.05 EV per detent, everything else one native step; a
   fast spin gets the SDK's acceleration on top. Two knobs in `src/RapidRawAdjustments.cs`:
   `SdkDiffPerDetent` (how much `diff` the SDK reports per physical click of the dial —
   the log records every raw value as `dial exposure diff=+2`, set the constant to the
   smallest magnitude a slow single click produces) and per-slider `StepsPerDetent`.

## Test without the hardware

```
powershell -ExecutionPolicy Bypass -File tools\smoke-test.ps1
```

Connects to RapidRAW, prints the greeting and parameter table, nudges Exposure +0.5 and
back, rates the open image 3 stars and clears it, and prints the final state. If the
preview moved and the stars toggled, the API works and the plugin will too; if it does not
connect, the problem is on the RapidRAW side, not in Options+.

## Layout

```
src/RapidRawPlugin.cs          Plugin entry: status, client lifetime
src/RapidRawApplication.cs     binds the profile to the RapidRAW.exe process
src/RapidRawClient.cs          persistent socket, reconnect, tick coalescing, state cache
src/RapidRawAdjustments.cs     dial actions (PluginDynamicAdjustment), parameter table
src/RapidRawCommands.cs        key actions (PluginDynamicCommand)
src/Diag.cs                    file + service log
src/package/                   LoupedeckPackage.yaml, icon, actionsymbols/, actionicons/
tools/build-plugin.ps1         prerequisite checks + build + sideload (Windows)
tools/build-plugin.sh          same, for macOS
tools/smoke-test.ps1           protocol test over the socket
tools/make-icons.py            generates the plugin's own action icons (see Icons)
tools/use-logi-icons.ps1       optional, end-user: borrow icons from Lightroom Classic by Logi
```

## Icons

Options+ uses an action's SVG as an alpha mask: only *filled* paths are recoloured
(white on the Actions Ring, dark in the action list); strokes are drawn as-is, which is
why the first build showed black dots. The convention, taken from the stock Adobe plugins,
is one 32x32 filled-path SVG per action in `src/package/actionsymbols/` (list / picker)
and `src/package/actionicons/` (device / ring), named
`Loupedeck.RapidRawPlugin.<Class>___<parameter>.svg`.

**The plugin ships only its own icons.** `tools/make-icons.py --install` generates the
155 original glyphs into the package (and a copy into `tools/icons-original/`;
`tools/icons-contact-sheet.png` previews them). Nothing from Logitech is in the repo or
the package.

### Optional: use the Lightroom Classic by Logi icons

If you prefer the Actions Ring to look exactly like Logitech's Adobe plugins, you can
copy their icons over on your own machine. Logitech's icon sets are proprietary
("Lightroom Classic by Logi" declares `license: Proprietary`), so this is done by you,
locally, from a plugin you installed - never bundled here.

1. In Logi Options+, open the Marketplace and install **Lightroom Classic by Logi**.
   Lightroom itself is not needed, and the Logi plugin can be uninstalled afterwards.
2. Run, from this repo (or wherever you saved the script):
   ```
   powershell -ExecutionPolicy Bypass -File tools\use-logi-icons.ps1
   ```
   It finds the installed RapidRAW plugin (Marketplace install or sideload `.link`),
   backs up its icons to `<plugin>\icons-backup\`, copies the matching Lightroom icon
   over 136 of the 155 actions (Exposure, Contrast, every HSL band, colour grading,
   vignette, grain, perspective, undo/redo, ratings, colour labels, zoom, the per-slider
   resets) and keeps the plugin's own icon for the 19 without a counterpart, then asks
   Options+ to reload the plugin.
3. To undo: `tools\use-logi-icons.ps1 -Restore`.

A rebuild (`build-plugin.ps1`) or a plugin update puts the original icons back, so re-run
the script afterwards if you want the Lightroom look again.

## License

MIT. Communicates with RapidRAW (AGPL-3.0) over a socket only; no code linkage.
