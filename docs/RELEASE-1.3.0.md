# WinRinglight 1.3.0

- The default renderer uses tiled Composition with 2× Direct2D smoothing, with vector Composition and WPF fallbacks.
- Optional spanning follows the actual outer display contour, including steps; the original spanning layout remains the default.
- Optional text-caret and selection cutouts, cursor update-rate controls, and smoother temperature transitions.
- The developer experiment controls are not shown in this release.

## Downloads

- `WinRinglight.exe`: self-contained x64 Windows application; no separate .NET runtime installation is required.
- `WinRinglight-1.3.0-x64-unsigned.msix`: **unsigned** x64 package for later signing or Store submission. It cannot be installed normally until signed with a trusted certificate matching its publisher identity. Store certification and installation of a signed package have not been tested.

Automated Windows tests include desktop pixel probes and synthetic multi-window monitor seams. Real multi-monitor arrangements, mixed DPI and integrated-GPU performance still require hardware validation. Firefox-to-Chrome caret switching was not fully tested on the CI runner.
