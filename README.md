# SnapKit

SnapKit is a small Windows screenshot tool for people who write docs. It sits in the system tray, grabs a region, and lets you mark it up before you copy it.

Nothing is saved to disk unless you ask. Captures stay in memory.

## Download

Windows 10 or 11, 64-bit. The file host blocks a raw `.exe`, so this is a zip of that one file.

[SnapKit-win-x64.zip](https://qu.ax/Wiucr)

Open that page and click **Download**. The file is `SnapKit-win-x64.zip` (63 MB). The link lasts until **18 Dec 2026, 8:00 PM ET**. Unzip it and run `SnapKit.exe` once.

That first run installs SnapKit for the current user. The program is copied to `%LOCALAPPDATA%\Programs\SnapKit\`, a Start menu shortcut is created, and an entry appears in Settings → Apps so it can be uninstalled. The copy you launched from the zip then closes, and the installed copy opens in the tray. Running a newer zip later updates that same install. Quit SnapKit from the tray first if Windows says the file is in use.

Windows SmartScreen will warn on an unsigned app. Choose **More info**, then **Run anyway**.

`Ctrl+Alt+Shift+Esc` quits SnapKit immediately, even if a window is stuck on top of the desktop. Esc during a capture cancels it. Esc in the editor closes that window only.

## Use it

Left-click the tray icon to capture an area. Right-click for the menu.

You drag a box around a dialog, press `T`, type "Click Save", then right-click the picture and choose **Copy**. Paste it into chat, email, or a ticket. A file appears only if you press `Ctrl+S`.

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

Move the pointer over a folder, a browser tab, a window, or the empty desktop. SnapKit outlines that target and lists the choices beside it, from the smallest pane up to the whole screen. Scroll, or press Up and Down, to move between them. Click the outline or a row in the list to capture it. Drag when you want a box of your own.

- `Space` cycles area, window, and full screen.
- `Enter` captures the highlighted target, or the monitor in full-screen mode.
- `Esc` or right-click cancels.

The shot stays where you took it. The rest of that screen stays dimmed behind it, so arrows and callouts point at the real window. `Esc` closes the editor and the dimmed screen. `Ctrl+Alt+Shift+Esc` quits SnapKit if a window is stuck.

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
| Right-click | Copy the marked-up picture |
| `Ctrl+C` | Copy the picture |
| `Ctrl+S` | Save as PNG or JPG |
| `Esc` | Discard the edits and close |

Double-click text or a callout to edit it. `Ctrl+Enter` commits the text. `Esc` while typing cancels that edit.

**Pin** floats the picture always-on-top. Drag it. Right-click and choose **Copy**, or double-click, or press `Ctrl+C`. `Ctrl+S` saves it. `Esc` closes it.

OCR of the whole capture is the OCR button on the toolbar. The text is copied and shown in a small panel. `Esc` closes that panel. If OCR fails, the message stays in the panel and the capture stays editable. OCR uses the Windows language already installed on the PC. No cloud.

### Global shortcuts

These are the defaults. Change them in Settings. They are stored in `%APPDATA%\SnapKit\settings.json`.

| Shortcut | Action |
| --- | --- |
| `PrtSc` | Area |
| `Shift+PrtSc` | Window |
| `Alt+PrtSc` | Full screen |
| `Ctrl+Shift+PrtSc` | Scrolling capture |
| `Ctrl+Shift+O` | OCR |
| `Ctrl+Alt+Shift+Esc` | Quit SnapKit immediately |

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
- After you finish a selection, that screen stays dimmed with the shot sitting on the spot you captured. The toolbar is next to the shot. `Esc` closes both.
- `Ctrl+Alt+Shift+Esc` and the tray **Exit** command end the process from outside the window that draws the capture.
- This build was compiled and unit-tested here. The window, hotkeys, OCR, and install were not run on a Windows desktop. OCR now decodes a PNG through the Windows runtime instead of casting a COM buffer.
