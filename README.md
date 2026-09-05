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

## Set up the console

1. Start RapidRAW and open an image in the editor. The plugin's status in Options+
   (Plugins → RapidRAW) should flip from "RapidRAW not reachable" to Normal within ~2 s.
   `%TEMP%\logi-rapidraw-plugin.log` says `Connected to RapidRAW.` and
   `Parameter table received: N params.`
2. In Options+, select the MX Creative Console, pick the **RapidRAW** profile (it is
   created automatically for the app and activates whenever RapidRAW is in front), or add
   the actions to any profile.
3. **Dialpad**: drag `RapidRAW → Develop → Exposure` (or any of the ~110 sliders, grouped
   Basic / Color / HSL / Color grading / Details / Effects / Transform) onto the dial or
   roller. Turn: the RapidRAW preview follows at low resolution while you turn and renders
   full quality when you stop; the slider in RapidRAW's panel moves with it. Press: reset
   to default. The dial readout shows the live value (`+0.35 EV`, `-12`, `180°`), `--`
   with no image open, `off` when RapidRAW isn't running.
4. **Keypad / Actions Ring**: `RapidRAW → Actions` — Undo, Redo, Before/after, Previous /
   Next image, Rate 0–5, Label red…purple, Zoom fit / 100 %, panel toggles. Rating keys
   show the current stars. Every action is the same handler RapidRAW's own keyboard
   shortcut uses, guards included (Next image does nothing in the library view, and the
   plugin log notes it as `ignored`).
5. Exposure and Brightness move 0.05 EV per detent, everything else one native step; a
   fast spin gets the SDK's acceleration on top. Change `StepsPerDetent` in
   `src/RapidRawAdjustments.cs` if that feels wrong for a given slider.

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
src/package/                   LoupedeckPackage.yaml, icon, action symbols
tools/build-plugin.ps1         prerequisite checks + build + sideload
tools/smoke-test.ps1           protocol test over the socket
```

Protocol notes the client relies on: `step {param, ticks}` for dials (coalesced per 20 ms
pump), `reset {param}` on press, `action {id}` for keys, `get_params` at connect for the
authoritative ranges, and the pushed `state` snapshot (`params`, `image`, `canUndo`…) for
readouts. The correlation field is `ref`; `id` on an `action` is the action name.

## License

MIT. Communicates with RapidRAW (AGPL-3.0) over a socket only; no code linkage.
