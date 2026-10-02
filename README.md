# CursorLine

A transparent, click-through, always-on-top line overlay. The line runs from the selected monitor's bottom-right corner to the cursor hotspot, and includes some customization options.

## Install

1) [Download](https://github.com/DimFann/cursorline/releases/download/latest/CursorLine.zip)
2) Extract CursorLine.exe from zip.
3) Run CursorLine.exe

## Customize

Choose **Customize** from the tray menu. Available options:

- Select which connected display to use. If the saved display is unavailable, CursorLine uses the primary display.
- Choose any corner as the line's starting point.
- Choose a solid line color and width from 1 to 100 pixels.
- Optionally draw a circle around the cursor with a radius from 1 to 2000 pixels.
- Import a PNG as a big cursor, toggle it with **Enable big cursor**, and set its maximum dimension from 16 to 512 pixels. The image is scaled proportionally regardless of its source resolution, with the image's top-left corner tracking the cursor tip.
- Optionally enable an application whitelist. Add process names such as `Photoshop` or `Photoshop.exe`; the overlay appears only while a listed app is in the foreground.

Valid changes preview as you edit. Choose **Save** to keep them; **Cancel** or closing the window reverts them. Settings are saved in `%LOCALAPPDATA%\CursorLine\settings.json`.

The line and circle are hidden while the cursor is outside the selected display.

## Compatibility

### Wacom/Huion/XP-Pen Proprietary Drivers

Didn't test/validate these.

### OpenTabletDriver

Some applications like AutoHotkey may compete for tablet input positions resulting in noticeable delay, at least with OpenTabletDriver. Modified VoiDTools's WindowsInk plugin to include a bridge for cursorline to get around this. Provided Build + Modified src in release.
[VoiDPlugins by Kuuuube](https://github.com/Kuuuube/VoiDPlugins)

If you're experiencing these issues while using Kuuuube's WindowsInk plugin in Absolute Mode:

1) [Download](https://github.com/DimFann/cursorline/releases/download/latest/OpenTabletDriver_WindowsInk-CursorLineBridge.zip) and Install the modified Windows Ink plugin from the latest release.
2) Modify `%LOCALAPPDATA%\OpenTabletDriver\settings.json`, setting Sync=true, and ForcedSync = false
```json
      "OutputMode": {
        "Path": "VoiDPlugins.OutputMode.WinInkAbsoluteMode",
        "Settings": [
          {
            "Property": "Sync",
            "Value": true
          },
          {
            "Property": "ForcedSync",
            "Value": false
          }
        ],
        "Enable": true
      },
```
This should allow CursorLine to share the cursor position with the tablet driver directly, eliminating the need for a duplicate input stream/conflict with AHK, etc.

### etc.

No guarantee anti-cheat or whatever in games won't mind this, best recommendation is to close the software if you're not drawing.

## Build

Run the PowerShell publish script:

```powershell
./publish.ps1
```
This will generate a self-contained exe under /dist.

## Credits

Developed with assistance from GitHub Copilot.

