# Screen Capture

Standalone tray screen-capture tool for Windows (C# / .NET 8 / WinForms, per-monitor DPI aware, multi-monitor).
Clean-room implementation modeled on general screenshot-tool behavior.

## Run
`publish\ScreenCapture.exe` (self-contained, no .NET install needed). It lives in the system tray.

- **Ctrl+Shift+A** (default, changeable in Settings) or left-click the tray icon: region capture
- Tray menu: Region capture, OCR capture (extract text), Full screen capture, OCR 설정 (문단으로 합치기 / 결과 창 표시 / OCR 언어: installed PaddleOCR languages, 언어 추가…, 언어 삭제…), Open screenshots folder, Settings..., Exit
- OCR (toolbar button, Ctrl+T, tray OCR capture) has no window: the tray icon turns amber (crosshair + dot) and the tooltip says '인식 중…' while recognizing, then the text is copied to the clipboard silently (in the current 문단으로 합치기 mode); no notification is shown, failures go to error.log. Turn on 'OCR 설정 → 결과 창 표시' to also open the editable result window. Clipboard writes run on the UI (STA) thread and retry for ~1 s if another app holds the clipboard
- OCR languages: Korean (PP-OCRv5) is bundled in models\. 언어 추가… downloads other official PaddleOCR recognition models (RapidAI/RapidOCR ModelScope repo, SHA-256 verified) into %LOCALAPPDATA%\ScreenCapture\models\<id>\: ch (Chinese + English + Japanese, v5), en, japan (v4), latin, cyrillic, eslav, el, arabic, devanagari, th, ta, te. 언어 삭제… removes downloaded ones (not the active one, not the bundled baseline, never the last one).

## Capture overlay
- Screen is frozen and dimmed across all monitors; magnifier shows cursor coordinates and the pixel color as #RRGGBB (palette tooltips show hex too); press **C** (no modifier) while the magnifier is visible to copy that #RRGGBB to the clipboard and close the overlay (cancels the capture; Ctrl+C still copies the image, and C types normally in the text tool)
- Hover highlights a window; click to select it. Or drag to select a region
- Drag inside the selection to move, drag handles/edges to resize, arrow keys nudge 1px
- Esc cancels; right-click clears the selection (or cancels when nothing is selected)
- Toolbar: rectangle, ellipse, arrow, pen, text, mosaic, undo, 6 colors, 3 thicknesses, pin, save, cancel, copy
  (click a tool again to go back to move/resize mode)
- OCR button / Ctrl+T: extract text from the selection (offline PaddleOCR; Windows OCR is only an automatic fallback - see below). Result window is editable and the text is auto-copied; small selections are upscaled 2-3x. The "문단으로 합치기" checkbox (default on, remembered) joins wrapped lines into paragraphs using OCR line boxes (right edge vs column edge, indentation, list markers, Korean particle/ending heuristics); off = 원본 줄바꿈
- Enter / double-click / Ctrl+C: copy to clipboard. Ctrl+S: save. Ctrl+Z: undo
- Pinned image: drag to move, mouse wheel to zoom, Esc or double-click to close, right-click for copy/save

Saving, copying and full screen capture are silent (no notifications); errors go to error.log.
Files go to `Pictures\Screenshots` as `Screenshot yyyy-MM-dd HHmmss.png` by default.
Settings: `%APPDATA%\ScreenCapture\settings.json`; errors: `%APPDATA%\ScreenCapture\error.log`.

## Build
```
dotnet publish ScreenCapture.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

## Hidden test flags
- `--fullscreen-test <path>`: capture full virtual screen to PNG and exit (writes `<path>.info.txt`)
- `--overlay-selftest <path>`: render a sample selection with every annotation type to PNG and exit
- `--ocr-test <image> [--out file] [--engine paddle|windows]`: print available OCR languages + recognized text (raw and merged; `--boxes` adds line boxes, `--scale N` forces upscale) to stdout and `<image>.ocr.txt`)
- `--ocr-bench <image> [--engine paddle|windows] [--runs N]`: print memory before / peak during / after each OCR run plus latency (also `<image>.bench.txt`)
- `--ocr-copy-test <image> [--engine paddle|windows] [--merge on|off] [--lang id] [--lock-ms N]`: run the window-less tray OCR path, verify the clipboard equals the OCR text
- `--lang-list` / `--lang-add <id>` / `--lang-delete <id>`: manage PaddleOCR languages from the command line; `--ocr-test` also accepts `--lang id`, test the locked-clipboard retry, restore the previous clipboard text
- `--magnifier-selftest <png>`: render the magnifier at (200,200), check the #RRGGBB format, check Ctrl+C leaves the clipboard alone and plain C copies the color and closes the overlay; results in `<png>.info.txt`
- `--settings-test`: print the effective OCR settings after loading (engine is always paddle)
- `--icon-test <dir>`: load the embedded tray icons (normal + OCR busy) at several sizes and save PNGs
- `--capture`: start and open the region overlay immediately

## OCR engines
- **PaddleOCR (the only engine; no engine choice in the UI, old `OcrEngine: windows` settings are migrated to paddle)**: PP-OCRv5 mobile detection + PP-OCRv5 Korean mobile recognition (Korean + English + digits), run with Microsoft.ML.OnnxRuntime (CPU). Own pre/post-processing (DB detection, CTC decoding), no OpenCV.
  Models are loaded **only while an OCR request runs**: sessions are created per request on a background thread, then disposed, followed by a compacting GC, heap compaction and working-set trim, so the idle app does not keep them in memory.
  Models live next to the exe in `models\` (`det.onnx` 4.8 MB, `rec_korean.onnx` 13.5 MB, `korean_dict.txt` 47 KB; ~18.4 MB). They are not in git: run `powershell -ExecutionPolicy Bypass -File tools\get-models.ps1` (downloads from the RapidAI/RapidOCR ModelScope repo - official PaddleOCR models converted to ONNX - and verifies SHA-256). Build/publish copy `models\` to the output.
  If the models are missing or Paddle fails, the app falls back to Windows OCR (noted in the result window status bar).
- **Windows OCR (internal fallback only)**: used automatically when the PaddleOCR models are missing or Paddle fails. Windows.Media.Ocr, Korean preferred, then user-profile language, then English; faster (~0.35 s) but less accurate on small Korean text.
## OCR languages (Windows OCR)
Windows OCR uses installed language packs. Check with `Get-WindowsCapability -Online -Name 'Language.OCR*'` (admin). Install e.g. Korean: `Add-WindowsCapability -Online -Name 'Language.OCR~~~ko-KR~0.0.1.0'`, English: `Language.OCR~~~en-US~0.0.1.0`.


## No white flash
Overlay, pin and OCR windows are created DWM-cloaked with show/hide transitions disabled, painted synchronously (frozen screenshot first), then uncloaked; WM_ERASEBKGND is suppressed.

## Icon
`app.ico` (exe + tray) and `app_busy.ico` (tray while OCR runs: crosshair and corner dot turn amber #FFB81C) are drawn by `tools/make-icon.py` (Python + Pillow, 8x supersampling; 16 and 20 px use a hand-placed 1-px crosshair, 24/32 use pixel-aligned 2-px strokes; sizes 16, 20, 24, 32, 40, 48, 64, 256). A faint light rim keeps the black square visible on a dark taskbar. Regenerate with `python3 tools/make-icon.py` in the project folder.
