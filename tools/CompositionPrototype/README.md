# Composition-Prototyp (experimentell)

Ein separates WPF-Steuerfenster erstellt ein natives, rahmenloses Overlay-Fenster und einen Windows.UI.Composition-Visual-Tree. Das produktive WinRinglight-Projekt und dessen gespeicherte Einstellungen bleiben unverändert.

## Plan und Messung

1. Ring als ShapeVisual zeichnen, getrennt davon radialen Cursorverlauf als SpriteVisual. Beide werden über CompositionVisualSurface und CompositionMaskBrush kombiniert. MaskBrush.Mask benötigt einen SurfaceBrush; der Gradient wird deshalb nicht direkt zugewiesen.
2. Cursorposition mit 16-ms-DispatcherTimer (Ziel ~60 Hz) aktualisieren. Nur Positionsänderungen setzen den Mittelpunkt; kein explizites CPU-Raster pro Cursorbewegung. WPF/Windows können trotzdem Frames auslassen oder große Flächen neu zusammensetzen.
3. Auf echter Hardware mit identischen Bedingungen vergleichen: normale App vollständig beenden, Prototyp starten, bei gleicher Dicke und Form jeweils 30–60 s GPU/CPU und Flüssigkeit messen. Nicht nur Prozesswerte: DWM und GPU-Engine berücksichtigen. Bei Spielen FPS und Frametimes mit und ohne Overlay vergleichen.
4. Optik, Klickdurchleitung, Aufnahme-Ausschluss mit tatsächlich verwendeter Aufnahme-App sowie Randfälle separat überprüfen. Erst danach Integration erwägen.

## Ausführen

Windows 10 Version 1903 oder neuer erforderlich (Projekt zielt auf Windows 10 2004 SDK), x64-Artefakt aus GitHub Actions herunterladen und entpacken. Normale WinRinglight-App vorher vollständig beenden. Steuerfenster starten, „Overlay starten“ drücken. „Overlay stoppen“ oder Fenster schließen beendet das Overlay. Bei Problemen kann der Prozess im Task-Manager beendet werden. Prototyp ist nur für einen Hauptmonitor und enthält keine Tray-, Webcam-, Hotkey- oder Mehrmonitorfunktionen. Der Ring ist eine Annäherung; keine identische Optik garantiert. Die relative Bildschirmfläche und Aussparung unterscheiden sich möglicherweise von der App.

Capture-Ausschluss ist explizit optional; ein erfolgreicher Win32-Aufruf beweist nicht, dass jede Aufnahmesoftware das Overlay auslässt. Tests können fehlschlagen, insbesondere durch Treiber, Desktop-Komposition, Windows-Version oder Fensterverwaltung. Der CI-Build kann Laufzeitdarstellung und GPU-Leistung nicht bestätigen.
