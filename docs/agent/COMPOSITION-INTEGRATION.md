# Integration von Windows Composition – Arbeitsplan und Status

## Ziel und Status
Der separat getestete Prototyp (9d1c54f) und die integrierte Ein-Monitor-Version wurden vom Nutzer auf einem Full-HD-Monitor getestet. Für die Integration wurden bei aktivem Composition unter 10 % GPU gemeldet. Dies ist weder ein Mehrmonitor- noch ein Spiele- oder Kompatibilitätsnachweis.

Composition bleibt freiwillig, experimentell und sitzungsbezogen im Dev-Tab. Beim Start ist weiterhin WPF aktiv. Bei Initialisierungs-, Render- oder Capture-Affinity-Fehlern wird zu WPF zurückgefallen und der Status angezeigt. Capture-Ausschluss wird vor dem Anzeigen jedes neuen Composition-HWND gesetzt. 60 FPS Cursor-Ziel und keine Totzone bleiben erhalten.

## Aktueller Funktionsumfang
- Ein Composition-Fenster pro ausgewähltem Monitor; im gespannten Modus erhält jeder Monitor obere und untere Kanten auf Höhe seines eigenen Arbeitsbereichs. Innere Seitenkanten benachbarter, vertikal überlappender Monitore werden nicht gezeichnet; äußere Seitenkanten bleiben. Keine Auswahl erzeugt kein sichtbares Overlay, lässt aber den Dev-Modus aktiv.
- Monitor-Auswahl, Vollbildzustand, Display- und DPI-Änderungen konfigurieren die Szene neu, statt Composition absichtlich zu verlassen. Display-Ereignisse werden von SystemEvents und DPI-Nachrichten ausgelöst.
- Die Cursorposition wird in physische lokale Koordinaten jedes Overlays umgerechnet; negative Bildschirmkoordinaten sind im Layout berücksichtigt. Ringdicke und Stileinzug verwenden die DPI des jeweiligen Overlay-HWND. Der Cursor-Radius folgt der physischen Höhe des Hauptmonitors, entsprechend dem bisherigen Hauptmonitor-basierten Ansatz.
- Farbe/Temperatur, Dicke, Cursor-Takt, Capture-Einstellung und Ein-/Ausschalten wirken auf alle Fenster. Der Stilwechsel wird mit einer Composition-Geometrieanimation umgesetzt. Tray, Hotkey und Webcam verwenden weiterhin die vorhandene Zustandssteuerung.
- Bei gleichbleibenden Monitorfenstern (insbesondere Vollbild/Arbeitsbereich) wird die Ringgeometrie wie im WPF-Pfad über 0,4 s mit quadratischem Ein-/Auslauf animiert. Ein-/Ausblenden verwendet dieselbe Dauer und mathematische Kurve. Änderungen der Monitorfensteranzahl oder -position erstellen Fenster neu und können springen. Frame-genaue und visuelle Gleichwertigkeit ist noch nicht praktisch abgenommen.
- Der Composition-Spannmodus nutzt die jeweilige volle Arbeitsflächenhöhe auch bei unterschiedlich hohen Monitoren; versetzte Ober-/Unterkanten enden ohne Verbindung. Bei übereinanderliegenden Monitoren bleiben Seitenkanten erhalten; komplexe/diagonale Aufbauten sind noch nicht abgenommen. Bei erkanntem Vollbild auf mindestens einem Monitor wechseln alle gespannten Segmente auf die vollen Monitorgrenzen; andernfalls werden Arbeitsflächen verwendet. Der WPF-Fallback verwendet weiterhin den älteren gemeinsamen Spannbereich und ist diesbezüglich nicht gleichwertig. Jeder Monitor verwendet seine eigene DPI; die Optik bei unterschiedlichen Skalierungen ist noch nicht praktisch geprüft. Auch bei einzelmonitorigen Fenstern sind reale Misch-DPI- und Hotplug-Tests erforderlich. Jeder Ausschnitt hat eine eigene monitor-große Oberfläche; GPU-Speicher und Leistung bei mehreren oder hochauflösenden Monitoren sind noch nicht gemessen.
- Die WPF-Cache-/Glow-Experimente betreffen weiterhin nur WPF. Das Windows-SDK-Target ist `net10.0-windows10.0.19041.0` mit `SupportedOSPlatformVersion=10.0.17763.0`; Composition wird ab Windows 10 1903 versucht. Ältere Windows-Kompatibilität bleibt gesondert zu prüfen.

## Tests und noch offene Abnahme
Windows-Lauf 42386487654: Build und Publish von App und Prototyp sowie Glow-/Temperatur- und synthetische Mehrmonitor-Layouttests erfolgreich. Der interaktive Runner prüfte u. a. zwei Composition-HWNDs, Cursorlokalisation, Capture-Affinität und Fehlerbereinigung, Auswahl/Spannen, Stil-, Temperatur- und FPS-Einstellungen sowie eine externe Vollbild-Testanwendung mit echter Vollbilderkennung. Auch der Klick-Hit-Test an einem Punkt bestand.

Der Runner ersetzt keine Prüfung auf echten mehreren Monitoren oder gemischter DPI, keine sichtbare Aufnahmeprüfung, kein Webcam-/Hotkey-End-to-End, keine Spieltests, keine optische Abnahme und keine GPU-/Frametime-Messung. Das tatsächliche Verhalten von exklusivem Vollbild hängt zudem von Windows und dem jeweiligen Spiel ab. Alle Funktionen als vollständig gleichwertig zu bezeichnen wäre verfrüht.

## Aktuelle Mehrmonitor-Nachbesserung
Vollbilderkennung bewertet Fenster und Taskleisten nun pro Monitor. Offene Einstellungen laden die Monitorliste nach Windows-Displayereignissen neu. Glow-Konturen beschränken Überzeichnung auf die Kanten; optische Gleichwertigkeit und die Wirkung auf die Richtung rund→eckig müssen noch auf dem Nutzergerät gemessen werden. Die zusätzlichen Kantenkonturen sind eine Annäherung und keine Garantie für treppenfreie Darstellung.

CI-Lauf 42391559690 prüfte das neue Spannlayout mit unterschiedlich hohen, künstlichen Bereichen: Pixel an beiden Unterkanten waren sichtbar und an den inneren Seitenkanten nicht. Dies ist kein Test realer unterschiedlicher Monitore oder Skalierungen.

## Ringauflösung und Spann-Vollbild
- Im Dev-Tab ist optional höher aufgelöstes Composition-Raster (bis 2×, für große Flächen begrenzt) zuschaltbar. Standard bleibt native Auflösung und 60-Hz-Cursorziel. Die zusätzliche Oberfläche kann deutlich mehr GPU-Speicher und Leistung beanspruchen.
- Windows-CI 42393487945: tatsächlicher Vollbild-Testprozess in normalem und gespanntem Modus erfolgreich; Schalter 1×/2×, Auswahl, Capture, Hit-Test und Cursorpfade bestanden. Eine Rasterprobe gegen WPF auf dem CI-Desktop zeigte bei 1× und 2× identische Stichproben (je mittlerer RGB-Kanalfehler 2,676663); eine Qualitätsverbesserung ist damit ausdrücklich nicht nachgewiesen. Reale Monitor-, GPU- und optische Abnahme steht aus.

## Cursorlast bei mehreren Monitoren
Composition prüft für jedes Monitorfenster, ob der Cursor-Aussparungskreis dessen Fläche erreicht. Außerhalb wird der unveränderte Ring direkt statt über eine MaskBrush verwendet und dessen Gradient nicht bei Cursorbewegungen geändert. An Kanten, Ecken und bei überlappenden Monitoren bleiben alle betroffenen Masken aktiv. Keine Totzone und keine FPS-Absenkung. Der DWM kann trotzdem weitere Flächen verarbeiten; echte GPU-Ersparnis ist hardwareabhängig.

Windows-Lauf 42405275097: Tests für Kreis/Rechteck-Grenzen, negative Koordinaten, unbeeinflussten Monitor, Überlappung am Übergang und 1-Pixel-Bewegung erfolgreich; optischer Desktop-Pixelvergleich zwischen direktem Ring und vollständig deckender Maske ohne Abweichung in der Stichprobe. Keine Messung der GPU-Last.
