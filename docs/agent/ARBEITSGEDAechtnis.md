# Arbeitsgedächtnis: WinRinglight-Performance

Stand: 2026-11-06. Vor weiterer Arbeit aktualisieren. Nutzer möchte laufend über Vorgehen, Ergebnisse und nächste Schritte informiert werden; bei Unsicherheiten nachfragen.

## Ausgangslage
- Arbeitsbranch: `arena/feece2ba-winringlight`, Ausgangspunkt master `977bb76`. Keine Arbeit auf anderen Branches.
- Nutzergerät: ein Full-HD-Monitor, Windows-Skalierung 100 %. EXE 13 aus `arena/c0ca9d72-winringlight` zeigt große diagonale leuchtende Flächen; frühere EXEs hatten diese Glitches nicht, aber der Glow war schon falsch.
- Ursprüngliche WPF-Version: transparentes Overlay über virtuellem Desktop; globale RadialGradientBrush-OpacityMask; BlurEffect auf der Ring-Border. Bewegung der Maske kann großflächige Updates auslösen.
- Drive-Paket `macos-ringlight-simulator-web-app.zip` enthält Web-Demo und Konzept für Electron/Windows Composition. Keine fertige Windows-Implementierung; Leistungszahlen nicht verifiziert.
- Anderer Branch: WPF-Maske bleibt, ersetzt u. a. Blur durch neue Geometrie. In `CoreRingGeometry` werden Innen- und Außenkontur in eine PathFigure geschrieben, was Verbindungssegmente erzeugen kann – plausible Ursache der diagonalen Flächen, nicht visuell verifiziert. CI für bf13240 fehlgeschlagen. Diesen Branch nicht unbesehen übernehmen.
- Die `watch-studio/`-Dateien stammen aus dem vorherigen, unabhängigen Nutzertest; nicht ungefragt entfernen oder in WinRinglight integrieren.

## Schritt 1: Cursor-Render-Takt
- Auf dem Ausgangscode `CompositionTarget.Rendering` durch 16-ms-DispatcherTimer ersetzt, der nur bei eingeschaltetem Licht läuft. Beim Einschalten Cursor vor Fade-in aktualisieren; beim Beenden Timer stoppen.
- Ring, Blur und Maske unverändert. Das ist keine Lösung der Kosten pro Mausbewegung; bei eingeschaltetem Licht läuft das Polling weiterhin. Keine quantifizierten Verbesserungen behaupten.
- Keine Windows-WPF-Laufzeit und kein dotnet in dieser Umgebung; Build, Darstellung und Verbrauch sind nicht lokal verifiziert.

## Nächste Schritte / Prüfplan
1. Auf Windows bauen und prüfen: Ein-/Ausschalten (Tray, Hotkey, Webcam), erste Cursorposition nach Wiedereinschalten, ruhender und bewegter Cursor, Fade und Beenden. Ausgangsversion und Änderung vergleichen; CPU/GPU sowie DWM beobachten.
2. Referenzbild und Einstellungen für ursprünglichen Glow aufnehmen, beide Stilvarianten und diverse Dicken. Keine Glownersatz-Implementierung als visuell korrekt bezeichnen, bevor tatsächlich verglichen.
3. Für Last bei Mausbewegung separaten nativen Rendering-Prototyp untersuchen; Messungen vor Umbau. Multi-Monitor, gemischte DPI, Vollbild/Taskleiste, Capture-Ausschluss, Farbtemperatur und Webcam als Regressionen beachten.
4. Falls Zuständigkeiten oder gewünschte Optik unklar sind, Nutzer fragen statt Annahmen zu treffen.

## Optische Referenz und Windows-Tests
- Nutzerreferenz: Screenshot einer alten Version mit hellem, durchgehendem Ring und sehr dezentem Glow; die in macOS integrierte Ringlichtdarstellung dient als zusätzliche Orientierung, nicht als bereits pixelgenau vermessene Vorlage.
- Nutzer wünscht Windows-Tests über GitHub Workflows. `windows-test.yml` baut die WPF-App und veröffentlicht eine selbstständige Windows-x64-Testversion als Actions-Artefakt. Ein erfolgreicher Build beweist weder die Optik noch die tatsächliche Performance auf dem Nutzergerät.
