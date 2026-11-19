# WinRinglight 1.3.1

## Deutsch
- Die in Version 1.3.0 noch einsprachigen Beschriftungen wurden auf Deutsch und Englisch ergänzt.
- Der gekachelte Composition-Renderer mit 2×-Glättung ist standardmäßig aktiv; bei Problemen wird auf Vektor-Composition oder WPF zurückgefallen.
- Der Spannmodus bietet optional die tatsächliche Außenkontur mit Stufen. Die bisherige Darstellung bleibt Standard.
- Die EXE ist für x64 und enthält die .NET-Laufzeit. Entwicklereinstellungen werden nicht angezeigt.
- Das unsignierte x64-MSIX-Bundle ist ausschließlich als Workflow-Artefakt verfügbar, nicht in diesem Release. Es muss vor normaler Installation passend signiert werden; Store-Abnahme und Installation eines signierten Pakets sind nicht getestet.
- Echte Mehrmonitor-/Misch-DPI-Konfigurationen und die iGPU-Last müssen weiterhin auf Hardware geprüft werden.

## English
- Labels that remained monolingual in version 1.3.0 now support both German and English.
- Tiled Composition with 2× smoothing is enabled by default, falling back to vector Composition or WPF when necessary.
- Spanning can optionally follow the actual stepped outer display contour; the previous layout remains the default.
- The x64 EXE is self-contained. Developer controls are hidden.
- The unsigned x64 MSIX bundle is available only as a workflow artifact, not in this release. Normal installation requires a matching trusted signature; Store acceptance and signed-package installation have not been tested.
- Real multi-monitor/mixed-DPI configurations and integrated-GPU performance still need hardware validation.
