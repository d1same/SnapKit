# SnapKit

SnapKit is a small Windows screenshot tool for people who write docs. It sits in the system tray, grabs a region, and lets you mark it up before you copy it.

Nothing is saved to disk unless you ask. Captures stay in memory.

## Download

Windows 10 or 11, 64-bit.

[SnapKit 0.1.3](https://github.com/d1same/SnapKit/releases/tag/v0.1.3)

Download `SnapKit-win-x64.zip` from that release. Unzip it and run `SnapKit.exe` once.

That first run installs SnapKit for the current user. A window stays on screen while it copies, so the install is not a silent pause. The program is copied to `%LOCALAPPDATA%\Programs\SnapKit\`, a Start menu shortcut is created, and an entry appears in Settings → Apps so it can be uninstalled. The copy you launched from the zip then closes, and the installed copy opens beside the clock. It also starts with Windows. Turn that off in Settings if you do not want it. Running a newer zip shows **Updating SnapKit**, closes whatever SnapKit is already running, and replaces it.

This build is **0.1.3**. After it is running it checks [d1same/SnapKit](https://github.com/d1same/SnapKit) for a newer GitHub release. If one exists, an update window names both versions. **Update** downloads the zip, and the new copy shows the same install window while it replaces the old one. Settings also has **Check for updates**. A release is published by pushing a `v*` tag. GitHub Actions builds `SnapKit-win-x64.zip` and attaches it to that release. The repository has to be public for that check to see the release, because the app does not ship a GitHub token.

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

A bar stays at the top of the screen: the tab or folder, the window, and **Entire screen**. The dashed outline eases to the choice under the pointer instead of jumping. Rest on a window and it settles. Move onto **Entire screen**, press `F`, or scroll to that choice when you want the whole monitor. Click a choice to capture it. Drag when you want your own box. A drag captures only that box.

- `Space` cycles area, window, and full screen.
- `Enter` captures the highlighted target, or the monitor in full-screen mode.
- `Esc` or right-click cancels.

The shot stays where you took it. The rest of that screen is darkened, and one dashed line stays on the edge of the shot until you press `Esc` or click outside it. Turn click-away off in Settings if you want a click there to leave the shot open. `Ctrl+Alt+Shift+Esc` quits SnapKit if a window is stuck.

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

OCR reads the capture on the PC, then highlights each word on the picture. The highlight sits on the word at 100%, 125%, 150%, and 200% scaling, including a monitor that is not at the top-left of the desktop. Drag across the words you want and that text is copied in reading order. `Ctrl+A` copies all of it. `Esc` closes the highlighter and leaves the capture editable. If OCR fails, the message stays in a small window with **Copy error**. OCR uses the Windows language already installed. No cloud.

The color row and the hex box apply to the pen, line, arrow, rectangle, ellipse, highlighter, text, callout, step number, and spotlight. A selected mark changes with them. Stroke width changes the pen, line, arrow, shapes, highlighter, and blur. Text size changes text, callouts, and step numbers. Blur stays a pixelate; it has no tint.

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

Settings also has **start with Windows** (on), **close the shot when you click outside it** (on), a short white flash after a capture, a shutter sound, and **Check for updates**. Flash is on. Sound is off. The tray tooltip shows the version. On Windows 11 the tray icon is placed next to the clock. If it still lands in the overflow, open Settings → Personalization → Taskbar → Other system tray icons and turn SnapKit on.

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
- Scrolling capture focuses the page, moves the mouse onto it, hides its own status while the wheel is sent, waits for the scroll animation to finish, then stitches by overlap. A maximized window stays maximized. It stops at the end of the page, on `Esc`, or at 16,000 pixels. A window that does not scroll with the wheel, a page that keeps animating, or a list that recycles rows instead of moving pixels can stop the stitch early.
- The scrollbar column is ignored. A fixed header and footer are kept from the first frame.
- Cross-monitor drag is not supported.
- After you finish a selection, that screen stays dimmed with the shot sitting on the spot you captured. One dashed line stays on the edge of the shot until `Esc` or a click outside. The toolbar is next to the shot. `Esc` closes both.
- `Ctrl+Alt+Shift+Esc` and the tray **Exit** command end the process from outside the window that draws the capture.
- This build was compiled and unit-tested on Linux. The window, hotkeys, OCR, scrolling capture, and install window were not run on a Windows desktop from here. No Windows machine is connected to this session. Word boxes are mapped from the bitmap Windows OCR actually saw, then onto the picture at the display scale.
