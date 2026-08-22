# RapidRAW plugin for Logi Options+ (Logi Actions SDK) — skeleton

Bridges the Logitech MX Creative Console Dialpad (and Keypad / Actions Ring /
Loupedeck devices) to RapidRAW via its proposed local Control Surface API
(`ws://127.0.0.1:43917`). Dial-assignable adjustments + button commands, same
UX as the official Lightroom integration.

## Status

Skeleton only. It compiles against the Loupedeck-heritage SDK API surface
(`Plugin`, `PluginDynamicAdjustment`, `PluginDynamicCommand`), but you should
regenerate the project shell with the official tool and diff — Logitech has
been evolving the SDK:

```
dotnet tool install -g LogiPluginTool
logiplugintool generate RapidRaw
```

Then replace the generated stub classes with `src/*.cs` here, and check:

1. Exact base-class/override names in the generated template (e.g. whether
   `ApplyAdjustment` takes a device id parameter in your SDK version).
2. How the template wires application detection — bind the profile to the
   `RapidRAW` process name so Options+ auto-switches profiles when the app
   gains focus.
3. `.link` file deployment path for dev iteration
   (`%LocalAppData%\Logi\LogiPluginService\Plugins` on Windows).

## Files

- `src/RapidRawPlugin.cs` — plugin entry, connection status surfacing
- `src/RapidRawClient.cs` — WebSocket client, auto-reconnect, protocol
- `src/RapidRawAdjustments.cs` — every RapidRAW slider as a dial target;
  parameter list self-populates from RapidRAW's `list_adjustments` reply
- `src/RapidRawCommands.cs` — undo/redo, culling, rating, before/after

## Dependencies

.NET (per SDK template), `System.Text.Json`, `System.Net.WebSockets.Client` —
no third-party packages.

## License

MIT. Communicates with RapidRAW (AGPL-3.0) over a socket only; no code linkage.
