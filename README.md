# WinRinglight

**WinRinglight** is a lightweight Windows utility that transforms your monitor(s) into a virtual ring light. It is designed for streamers, remote workers, and content creators who need professional lighting without extra hardware.

## Features

- **Virtual Ring Light:** Overlays a customizable light ring on your screen.
- **Smart Temperature:** Automatically adjusts color temperature based on the time of day to match ambient light.
- **Multi-Monitor Support:** Span the light across all monitors or select specific ones. In System settings, spanning can optionally follow the monitors’ actual outer outline, including steps for mismatched positions and sizes; the original spanning display remains the default.
- **Webcam Detection:** Automatically turns on the ring light when your webcam becomes active.
- **Global Hotkeys:** Toggle the light instantly with customizable combinations (e.g., `Ctrl + Alt + R`).
- **Cursor Cutout:** Dynamically hides the light around your mouse cursor to ensure UI visibility and prevent clicking issues.
- **Visual Themes:** Choose between classic circular and modern rounded-square designs.

## Installation

1. Download the latest release from the [Releases](https://github.com/PeWieser/WinRinglight/releases) page.
2. Run the self-contained x64 `WinRinglight.exe` (no separate .NET installation required).
3. Configure your preferences in the Settings window (accessible via the System Tray icon).

The CI workflow also produces an **unsigned x64 MSIX bundle** as a workflow artifact (not a release download) for later signing or Store submission. It cannot be installed normally until it has been signed with a trusted certificate matching its publisher identity. Store acceptance and packaged installation require separate validation. The default renderer uses tiled Composition with 2× Direct2D smoothing, falling back to vector Composition or WPF when necessary.

## Usage

- **Toggle:** Use your configured Hotkey or right-click the tray icon.
- **Settings:** Adjust thickness, color temperature, and webcam automation.
- **Autostart:** Enable "Start with Windows" in the System tab to have your light ready at boot.

## Requirements

- Windows 10 or 11
- x64 Windows; the portable release includes .NET 10

---
*Developed by Melo*
