# SnapKit

SnapKit is a small, zero-dependency Windows screenshot and tutorial-recording tool for people who write docs, ship code, and share bug reports. It sits in the system tray, grabs a region, and lets you mark it up — or record an annotated MP4/GIF — before you copy it straight to the clipboard.

Nothing is saved to disk unless you ask. Captures stay in memory.

### Everyday ideas

- **Step-by-step client guides** — Pick **Step-by-step guide…** from the tray, click through any fix, edit the 1-line captions, and **Copy as Image** or **Copy for Email / Docs (HTML)** straight into a ticket reply or Outlook email.
- **PR & changelog walkthroughs** — Record a 5-second **GIF** (`Ctrl+Alt+G`) or **MP4** (`Ctrl+Alt+V`) with a `3-2-1` countdown, click highlight rings, live mic meter, and live arrows/blur.
- **One-click privacy protection** — Press `B` or right-click → **Auto-blur sensitive info** to automatically mask emails, IP addresses, phone numbers, credit cards, API tokens, and labeled passwords offline.
- **Magnify tiny UI buttons** — Press `M` to drop a circular **2× Magnifier Loupe** so clients see both the full window context and the exact button up close.
- **Before / After & multi-step replies** — Pick **Combine recent shots…** to stitch 2–6 captures side-by-side or vertically with `Before / After` or `Step 1, 2…` labels.
- **Audit / SLA proof** — Toggle **Stamp** in the Frame pill to print the local timestamp (and optional ticket # or hostname) cleanly into the footer.

## Download

Windows 10 or 11, 64-bit.

[SnapKit 0.5.0](https://github.com/d1same/SnapKit/releases/tag/v0.5.0)

Download `SnapKit-win-x64.zip` from that release. Unzip it and run `SnapKit.exe` once.

That first run installs SnapKit for the current user. A non-blocking progress window stays responsive while it copies, so the install is never a silent pause. The program is copied to `%LOCALAPPDATA%\Programs\SnapKit\`, a Start menu shortcut is created, and an entry appears in Settings → Apps so it can be uninstalled. The copy you launched from the zip then closes, and the installed copy opens beside the clock. It also starts with Windows. Turn that off in Settings if you do not want it. Running a newer zip shows **Updating SnapKit**, closes whatever SnapKit is already running, and replaces it.

This build is **0.5.0**. After it is running it periodically checks [d1same/SnapKit](https://github.com/d1same/SnapKit) for a newer GitHub release. If one exists, a badge appears on the tray icon and menu, and an update window shows both versions along with the GitHub release notes. **Update now** streams the download into a versioned temp folder with live `%` and MB progress, then the new copy shows a non-blocking install window while replacing the old one and relaunches with an `--updated` toast and automatic temp cleanup. Both the tray menu and Settings also have **Check for updates…**. A release is published by pushing a `v*` tag. GitHub Actions builds `SnapKit-win-x64.zip` and attaches it to that release. The repository has to be public for that check to see the release, because the app does not ship a GitHub token.

Windows SmartScreen will warn on an unsigned app. Choose **More info**, then **Run anyway**.

`Ctrl+Alt+Shift+Esc` quits SnapKit immediately, even if a window is stuck on top of the desktop. Esc during a capture cancels it. Esc in the editor closes that window only.

## Use it

Left-click the tray icon to capture an area. Right-click for the organized menu.

You drag a box around a dialog, press `T`, type "Click Save", then press the crisp white **Copy** button on the floating rail. Paste it into chat, email, or a ticket. Right-clicking any capture opens a cleanly grouped menu (**Share & Save**, **Smart Tools** with OCR, Auto-blur sensitive info, and Pin, and **Appearance** with Frame presets and Ticket proof stamp). A file appears only if you press `Ctrl+S`, `Ctrl+Shift+S`, or Save.

### Tray menu

Organized into three calm sections so first-time users aren't overwhelmed:

- **Capture**: **Capture area**, **Capture text / table (OCR)**, and **More captures ▸** (*Window, Full screen, All monitors, Scrolling capture, Repeat last area, Delayed capture 3s / 5s / 10s*)
- **Tutorials & Recording**: **Record video (MP4)**, **Record GIF**, and **Step-by-step guide…** (*click-by-click client SOP builder*)
- **Tools**: **Combine recent shots…** (*Before / After or multi-step stitcher*) and **Recent captures ▸** (*last 10, memory only*)
- **Settings & Shortcuts**, **Check for updates…**, and **Exit**

### While you are selecting

A bar stays at the top of the screen: the tab or folder, the window, and **Entire screen**. The dashed outline eases to the choice under the pointer instead of jumping. Rest on a window and it settles. Move onto **Entire screen**, press `F`, or scroll to that choice when you want the whole monitor. Click a choice to capture it. Drag when you want your own box. A drag captures only that box.

- `Space` cycles area, window, and full screen.
- `Enter` captures the highlighted target, or the monitor in full-screen mode.
- `Esc` or right-click cancels.

The shot stays where you took it. Drag any corner or mid-edge (`N`/`S`/`E`/`W`) handle to resize it, or drag the edge to move it, before you draw or copy. The rest of that screen is darkened, and one dashed line stays on the edge of the shot until you copy it, save it, press `Esc`, or click outside it. Turn click-away off in Settings if you want a click there to leave the shot open. `Ctrl+Alt+Shift+Esc` quits SnapKit if a window is stuck.

A selection stays on one monitor. Use **All monitors** for a combined shot.

### Editor

The ultra-minimal floating rail keeps all tools on one slim row (icons with subtle shortcut letters, Undo/Redo/Clear, OCR/Pin/Save, live zoom controls in EditorWindow, and a crisp white **Copy** button on the right), with a compact detached options pill underneath showing only the contextual controls for the active tool or selection (color swatches + `#HEX`, stroke dots, shape fill toggle, text size stepper, or **Auto-blur PII** when Blur/Redact is active, plus **Frame**, **Stamp**, and live `W×H` pixel dimensions). Tool, color, stroke, text size, shape fill mode, docs backdrop frame, and ticket stamp defaults persist automatically between captures.

| Key | Tool / Action |
| --- | --- |
| `V` | Select / move / drag endpoint & corner handles |
| `P` | Pen |
| `A` | Arrow |
| `R` | Rectangle (Outline / Tint / Solid fill) |
| `E` | Ellipse (Outline / Tint / Solid fill) |
| `H` | Highlighter |
| `T` | Text |
| `C` | Callout |
| `N` | Numbered step (1, 2, 3, … + Reset #1) |
| `B` | Blur (pixelate) + contextual **Auto-blur PII** |
| `D` | Redact (blackout box) + contextual **Auto-redact PII** |
| `S` | Spotlight |
| `M` | Magnifier (2× circular zoom loupe) |
| `X` | Crop |
| `I` | Eyedropper (picks color & auto-copies `#HEX`) |
| `[` `]` | Thinner / thicker stroke |
| `Shift` while drawing/resizing | Lock to 45° or a square/circle |
| Arrow keys (`Shift` = 10px) | Nudge selected mark |
| `Ctrl+D` | Duplicate selected mark |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` or `Ctrl+Shift+Z` | Redo |
| `Delete` / `Backspace` | Remove the selected mark |
| `Ctrl++` / `Ctrl+-` / `Ctrl+0` / `Ctrl+1` | Zoom in / out / fit / 100% (Editor window) |
| Right-click | Organized Share & Save, Smart Tools (OCR, Auto-blur PII, Pin), and Appearance (Frame, Ticket Stamp) menu |
| `Ctrl+C` | Copy the picture and close the shot |
| `Ctrl+Shift+S` | Quick save PNG directly to your Quick Save folder and close |
| `Ctrl+S` | Save as PNG or JPG, then close the shot |
| `Esc` | Discard the edits and close |

Double-click text or a callout to edit it. `Ctrl+Enter` commits the text. `Esc` while typing cancels that edit. Drag the circular handles on any selected arrow, callout, box, circle, blur, redact, spotlight, or magnifier loupe to resize or re-aim endpoints directly. Choose **Clean**, **Dark**, or **Slate** under **Frame** to wrap the export in a rounded documentation backdrop with a soft drop shadow, and toggle **Stamp** to add a subtle timestamp and optional ticket # or hostname proof bar.

**Pin** floats the picture always-on-top with a hover action bar (**Copy**, **Save**, opacity badge, close). Drag to move, scroll wheel to resize, `Ctrl`+scroll wheel to adjust opacity (`20%–100%`), double-click or `Ctrl+C` to copy (with a green `"Copied!"` toast), `Ctrl+Shift+S` to quick save, `Ctrl+S` to save as, `Ctrl+0` to reset, right-click for the context menu, and `Esc` to close.

OCR reads the capture on the PC, then highlights each word on the picture. The highlight sits on the word at 100%, 125%, 150%, and 200% scaling, including a monitor that is not at the top-left of the desktop. Drag across the words you want and that text is copied in reading order, or switch to **Table** mode (`Tab`, auto-selected automatically whenever a multi-column grid is detected) to see vertical column guides and copy tab-separated cells that paste straight into **Excel** or **Google Sheets** with rows and columns intact (`Ctrl+A` copies all, `Ctrl+Shift+C` always copies as a table). `Esc` closes the highlighter and leaves the capture editable. If OCR fails, the message stays in a small window with **Copy error**. OCR uses the Windows language already installed. No cloud.

### Recording (MP4 & GIF)

Choose **Record video (MP4)** (`Ctrl+Alt+V`) or **Record GIF** (`Ctrl+Alt+G`), drag an area or click a window (`Space` switches mode), then use the floating HUD to toggle **MP4 / GIF**, **Cursor** (pointer + click highlight rings), **Audio** (system playback via WASAPI loopback) / **Mic** (with a live mic activity dot while recording), and **3-2-1** countdown. Click **Record** (or press `Enter`) to start, **Pause / Resume** anytime, and **Stop** (or press the record hotkey again) to finish. Both the dashed frame, countdown badge, and floating HUD are excluded from the captured recording.

While recording, click **Draw** on the floating HUD to sketch live **arrows, pen marks, rectangles, circles, step badges, Live Blur (real-time mosaic pixelation), or Live Redact (blackout)** right over the capture region — with optional **Auto-fade** for temporary pointers. Live Blur pixelates pixels directly in the recording pipeline so secrets never touch the exported file.

When finished, click **Annotate / Edit** to open the **Post-Recording Tutorial Video Editor** (timeline scrubbing, trim handles, and timed arrows, callouts, step badges, Soft/Med/Strong pixelate blur, blackout redact, and spotlight overlays), or use **Copy file** (`Ctrl+C`) to copy the `.mp4` or `.gif` directly to the clipboard so `Ctrl+V` pastes straight into Slack, Teams, Discord, GitHub, or Explorer; **Quick Save** (`Ctrl+Shift+S`); **Save as…** (`Ctrl+S`); or **Play**. Zero external installs or FFmpeg required.

### Global shortcuts

These are the defaults. Change them in Settings. They are stored in `%APPDATA%\SnapKit\settings.json`.

| Shortcut | Action |
| --- | --- |
| `PrtSc` | Area |
| `Shift+PrtSc` | Window |
| `Alt+PrtSc` | Full screen |
| `Ctrl+Shift+PrtSc` | Scrolling capture |
| `Ctrl+Shift+O` | OCR |
| `Ctrl+Alt+PrtSc` | Repeat last area |
| `Ctrl+Alt+V` | Record video (MP4) |
| `Ctrl+Alt+G` | Record GIF |
| `Ctrl+Alt+Shift+Esc` | Quit SnapKit immediately |

On Windows 11, SnapKit automatically intercepts `PrtSc` via a low-level keyboard hook so the built-in Snipping Tool does not steal it. Settings also includes a one-click toggle to turn off Windows' **Use the Print screen key to open screen capture** setting permanently.

Settings also configures **Start with Windows** (on), **Close shot on click-away** (on), **Capture flash** (on), **Shutter sound** (off), recording defaults (cursor & click rings, system audio, microphone), your **Quick Save** folder (default `Pictures\SnapKit`), and **Check for updates**. The tray tooltip shows the version (and update status). On Windows 11 the tray icon is placed next to the clock. If it still lands in the overflow, open Settings → Personalization → Taskbar → Other system tray icons and turn SnapKit on.

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

## Limits

- Recent captures keep the last 10 shots in memory. They are gone when SnapKit exits.
- Closing, copying, saving, or pinning bakes the marks into the recent item. Reopening it is a flat image.
- `Esc` in the editor throws away the marks. The original crop stays in Recent.
- Scrolling capture focuses the page, moves the mouse onto it, excludes its floating status HUD from capture, waits for the scroll animation to settle, then stitches by contrast-weighted overlap. A maximized window stays maximized. It stops at the end of the page, on `Esc`, or at 20,000 pixels. A window that does not scroll with the wheel, a page that keeps animating, or a list that recycles rows instead of moving pixels can stop the stitch early.
- The scrollbar column is ignored. A fixed header and footer are kept from the first frame.
- Cross-monitor drag is not supported.
- After you finish a selection, that screen stays dimmed with the shot sitting on the spot you captured. Drag a corner to resize it or the edge to move it. One dashed line stays on the edge until you copy, save, press `Esc`, or click outside. The toolbar next to the shot names each tool, and a large Copy button copies the picture and closes the shot. `Esc` closes both.
- `Ctrl+Alt+Shift+Esc` and the tray **Exit** command end the process from outside the window that draws the capture.
- This build was compiled and unit-tested on Linux. The window, hotkeys, OCR, scrolling capture, and install window were not run on a Windows desktop from here. No Windows machine is connected to this session. Word boxes are mapped from the bitmap Windows OCR actually saw, then onto the picture at the display scale.
