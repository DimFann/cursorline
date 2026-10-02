# CursorLine

A transparent, click-through, always-on-top line overlay for the primary monitor. The line runs from the monitor's bottom-right corner to the cursor hotspot and uses a configurable solid color and width.

## Run

```powershell
dotnet run
```

The overlay starts immediately with a CursorLine icon in the notification area. Double-click the icon, or right-click it and choose **Show overlay** or **Hide overlay** to toggle the line. Choose **Customize** to open the appearance settings. Choose **Exit** in the tray menu to close the app completely. Closing the overlay window hides it to the tray.

Only one CursorLine instance runs per Windows session; additional launches exit without creating another tray icon.

## Configure

Use **Customize** in the tray menu to choose the display, line color, width, and starting corner. Toggle **Draw main line** to show or hide the line independently of the cursor circle and big cursor. You can also enable a circle around the cursor and set its radius. To use a custom cursor, import a PNG, enable **Enable big cursor**, and set its maximum image dimension from 16 to 512 pixels with the live-updating scale slider. Adjust the rotation from 0° to 360° with the rotation slider; the image rotates around its top-left corner, which stays pinned to the cursor tip. The image keeps its aspect ratio. An unset or unavailable display selection falls back to the primary monitor. Enable **Only show on whitelisted applications** and add executable process names (with or without `.exe`) to restrict visibility to those foreground apps. This uses foreground-change notifications, not render-loop polling. Valid changes preview immediately; **Save** persists them in `%LOCALAPPDATA%\CursorLine\settings.json`, while **Cancel** or closing the window reverts the preview. Width is 1-100 pixels; circle radius is 1-2000 pixels.
The line, optional cursor circle, and optional big cursor render only while the cursor is within the selected display's bounds.

## Distribution

Run the PowerShell publish script:

```powershell
# CursorLine

CursorLine draws a thin, click-through line from a chosen corner of a monitor to the cursor. It runs quietly in the Windows notification area.

## Start

Run `dist\CursorLine.exe`. It is a self-contained Windows x64 app and does not require a separate .NET installation. Only one instance runs at a time.

Use the CursorLine tray icon to show or hide the overlay, customize it, or exit. Closing the overlay hides it to the tray; choose **Exit** from the tray menu to quit.

## Customize

Choose **Customize** from the tray menu. Available options:

- Select which connected display to use. If the saved display is unavailable, CursorLine uses the primary display.
- Choose any corner as the line's starting point.
- Choose a solid line color and width from 1 to 100 pixels.
- Optionally draw a circle around the cursor with a radius from 1 to 2000 pixels.
- Import a PNG as a big cursor, toggle it with **Enable big cursor**, and set its maximum dimension from 16 to 512 pixels. The image is scaled proportionally regardless of its source resolution, with the image's top-left corner tracking the cursor tip.
- Optionally enable an application whitelist. Add process names such as `Photoshop` or `Photoshop.exe`; the overlay appears only while a listed app is in the foreground.

Valid changes preview as you edit. Choose **Save** to keep them; **Cancel** or closing the window reverts them. Settings are saved in `%LOCALAPPDATA%\CursorLine\settings.json`.

The line and circle are hidden while the cursor is outside the selected display. For tablet pen tracking, use a CursorLine-bridge-enabled VoiDPlugins WindowsInk build. With that plugin, set Windows Ink `Sync` on and `ForcedSync` off to avoid duplicate mouse movement. The stock plugin tracks through the Windows mouse cursor and may conflict with mouse-remapping tools during pen strokes.

## Build

Run `publish.ps1` from PowerShell to create or update the self-contained executable in `dist\CursorLine.exe`. The app icon and default settings are embedded; the `dist` folder needs only that executable.

## Credits

Developed with assistance from GitHub Copilot.
