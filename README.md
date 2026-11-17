# Screen Capture

Standalone tray screen-capture tool for Windows (C# / .NET 8 / WinForms, per-monitor DPI aware, multi-monitor).
Clean-room implementation modeled on general screenshot-tool behavior.

## Run
`publish\ScreenCapture.exe` (self-contained, no .NET install needed). It lives in the system tray.

- **Ctrl+Shift+A** (default, changeable in Settings) or left-click the tray icon: region capture
- Tray menu: Region capture, Full screen capture, Open screenshots folder, Settings..., Exit

## Capture overlay
- Screen is frozen and dimmed across all monitors; magnifier shows cursor coordinates and pixel RGB
- Hover highlights a window; click to select it. Or drag to select a region
- Drag inside the selection to move, drag handles/edges to resize, arrow keys nudge 1px
- Esc cancels; right-click clears the selection (or cancels when nothing is selected)
- Toolbar: rectangle, ellipse, arrow, pen, text, mosaic, undo, 6 colors, 3 thicknesses, pin, save, cancel, copy
  (click a tool again to go back to move/resize mode)
- Enter / double-click / Ctrl+C: copy to clipboard. Ctrl+S: save. Ctrl+Z: undo
- Pinned image: drag to move, mouse wheel to zoom, Esc or double-click to close, right-click for copy/save

Files go to `Pictures\Screenshots` as `Screenshot yyyy-MM-dd HHmmss.png` by default.
Settings: `%APPDATA%\ScreenCapture\settings.json`; errors: `%APPDATA%\ScreenCapture\error.log`.

## Build
```
dotnet publish ScreenCapture.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

## Hidden test flags
- `--fullscreen-test <path>`: capture full virtual screen to PNG and exit (writes `<path>.info.txt`)
- `--overlay-selftest <path>`: render a sample selection with every annotation type to PNG and exit
- `--capture`: start and open the region overlay immediately
