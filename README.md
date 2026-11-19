# SnapKit

SnapKit is a small Windows screenshot tool for people who write docs. It sits in the system tray, grabs a region, and lets you mark it up before you copy it.

Nothing is saved to disk unless you ask. Captures stay in memory.

## Download

Windows 10 or 11, 64-bit. The file host blocks a raw `.exe`, so this is a zip of that one file.

[SnapKit-win-x64.zip](https://filebin.net/snapkit-win-x64/SnapKit-win-x64.zip)

The link lasts until **26 Nov 2026**. The first visit shows a warning page. Click **Proceed to download**. Unzip, then run `SnapKit.exe`.

Windows SmartScreen will warn on an unsigned app. Choose **More info**, then **Run anyway**.

## Use it

Left-click the tray icon to capture an area. Right-click for the menu.

You drag a box around a dialog, press `T`, type "Click Save", then `Ctrl+C`. The picture is on the clipboard. A file appears only if you press `Ctrl+S`.

### Tray menu

- Area
- Window
- Full screen (the monitor under the cursor)
- All monitors
- Scrolling capture
- Delayed capture (3 seconds or 5 seconds)
- OCR (drag an area, text goes to the clipboard)
- Recent captures (last 10, memory only)
- Settings / Shortcuts
- Exit

### While you are selecting

- Drag to pick an area. A size readout and a magnifier follow the cursor.
- Hover a window and click to grab that window.
- `Space` cycles area, window, full screen.
- `Enter` grabs the current monitor in full-screen mode.
- `Esc` or right-click cancels.

A selection stays on one monitor. Use **All monitors** for a combined shot.

### Editor

The toolbar sits next to the selection.

| Key | Tool |
| --- | --- |
| `V` | Select / move |
| `P` | Pen |
| `L` | Line |
| `A` | Arrow |
| `R` | Rectangle |
| `E` | Ellipse |
| `H` | Highlighter |
| `T` | Text |
| `C` | Callout |
| `N` | Numbered step (1, 2, 3, …) |
| `B` | Blur |
| `S` | Spotlight |
| `X` | Crop |
| `I` | Color picker (hex) |
| `[` `]` | Thinner / thicker stroke |
| `Shift` while drawing | Lock to 45° or a square |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` or `Ctrl+Shift+Z` | Redo |
| `Delete` | Remove the selected mark |
| `Ctrl+C` | Copy the picture |
| `Ctrl+S` | Save as PNG or JPG |
| `Esc` | Discard the edits and close |

Double-click text or a callout to edit it. `Ctrl+Enter` commits the text. `Esc` while typing cancels that edit.

**Pin** floats the picture always-on-top. Drag it. Double-click or `Ctrl+C` copies it. `Ctrl+S` saves it. `Esc` closes it.

OCR of the whole capture is the OCR button on the toolbar. The text is copied and shown in a small panel. OCR uses the Windows language already installed on the PC. No cloud.

### Global shortcuts

These are the defaults. Change them in Settings. They are stored in `%APPDATA%\SnapKit\settings.json`.

| Shortcut | Action |
| --- | --- |
| `PrtSc` | Area |
| `Shift+PrtSc` | Window |
| `Alt+PrtSc` | Full screen |
| `Ctrl+Shift+PrtSc` | Scrolling capture |
| `Ctrl+Shift+O` | OCR |

Windows 11 often gives `PrtSc` to the Snipping Tool. Turn that off in Windows Settings, then set SnapKit's keys again if a shortcut fails to register.

Settings also has **start with Windows**, a short white flash after a capture, and a shutter sound. Flash is on. Sound is off.

## Change the name

The product name lives in `brand.name` at the repo root. Edit that one line and rebuild. The exe name, tray label, settings folder, and single-instance lock all follow it. The C# namespace stays `SnapKit`.

## Build

You need the .NET 8 SDK. The project sets `EnableWindowsTargeting`, so this command also works on Linux. It does not run there.

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

The publish folder contains one file:

`bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/SnapKit.exe`

Core tests, which run anywhere:

```bash
dotnet test tests/SnapKit.Core.Tests/SnapKit.Core.Tests.csproj -c Release
```

## Not in v1

GIF and MP4 recording is left for v2. The hook is `IScreenRecorder` in `app/Capture/IScreenRecorder.cs`. The intended stack is Windows.Graphics.Capture plus FFmpeg.

## Limits

- Recent captures keep the last 10 shots in memory. They are gone when SnapKit exits.
- Closing, copying, saving, or pinning bakes the marks into the recent item. Reopening it is a flat image.
- `Esc` in the editor throws away the marks. The original crop stays in Recent.
- Scrolling capture moves the mouse onto the window, scrolls with the wheel, and stitches by overlap. It stops at the end of the page, on `Esc`, or at 16,000 pixels. A window that does not scroll with the wheel, or a page that keeps animating, can stop the stitch early.
- The scrollbar column is ignored. A fixed header and footer are kept from the first frame.
- Cross-monitor drag is not supported.
- This build was compiled and unit-tested here. The window, hotkeys, OCR, and scrolling were not run on a Windows desktop.
