# RefScrn Screenshot Tool

RefScrn is a lightweight, efficient screenshot tool built with .NET 8.0 and WPF, designed for Windows. It features a modern overlay, rich annotation capabilities, and integrated OCR/translation services.

## ? Key Features

- **Quick Capture**: Global hotkey (`Alt + A`) to start capturing instantly.
- **Smart Selection**: Drag to select, with support for resizing from **all 4 edges and corners**.
- **Rich Annotation**: 
  - **Rectangle/Ellipse**: Standard shape tools.
  - **Arrow**: **WeChat-style solid arrow** for clear indication.
  - **Brush**: Freehand drawing.
  - **Mosaic**: Pixelate sensitive information.
  - **Text**: Add text with a **WeChat-style green caret** and borderless editing experience.
- **OCR & Translation**: 
  - **Text Recognition**: Extract text from potential areas directly.
  - **Auto-Translation**: Automatically translates recognized text in a dedicated window.
  - **Clean UI**: Minimalist result window with "Original" and "Translation" tabs.
- **Undo/Redo**: Full history support for annotations.

## ? Getting Started

1. **Launch**: Run `RefScrn.exe`. The app will minimize to the system tray.
2. **Capture**: Press `Alt + A` (default) to enter capture mode.
3. **Select**: Click and drag to select a screen area.
   - *Tip*: You can fine-tune the selection by dragging any edge or corner.
4. **Annotate**: Use the toolbar below the selection to draw, add text, or apply mosaic.
   - *Tip*: Right-click the mouse to cancel the current drawing or selection.
5. **OCR**: Click the "OCR" button in the toolbar to recognize text and open the translation window.
6. **Finish**: 
   - **Copy**: Double-click selection or press `Enter` to copy image to clipboard.
   - **Save**: Click the "Save" (download icon) button to save to file.
   - **Close**: Press `Esc` to exit capture mode.

## ? Project Structure

- **RefScrn**: Main WPF application project.
- **Services**: Contains logic for OCR (`OcrService`) and Translation (`TranslationService`).
- **Assets**: Icons and resources.

## ?? Configuration

Right-click the system tray icon to access **Settings**:
- **General**: Toggle auto-start on boot.
- **Hotkeys**: Customize the global capture hotkey.
- **Save Path**: Set the default directory for saved screenshots.

## ? Requirements

- **OS**: Windows 10/11 (Required for Windows OCR API)
- **Runtime**: .NET 8.0 Desktop Runtime

---

*Built with ?? by Antigravity*
