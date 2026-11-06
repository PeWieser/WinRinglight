# Integration von Windows Composition – Arbeitsplan

## Ziel und Status
Der separat getestete Prototyp (9d1c54f) wurde vom Nutzer auf einem Full-HD-Hauptmonitor getestet: Darstellung, Klickdurchleitung und Aufnahme-Ausschluss funktionieren; gemeldete GPU-Last max. ca. 9 %. Dies ist weder ein Mehrmonitor- noch ein Spiele- oder Kompatibilitätsnachweis.

## Leitlinien
- Bestehende WPF-Einstellungen, Tray, Hotkey, Webcam und Konfiguration bleiben Quelle der Wahrheit. Kein Eingriff in gespeicherte Nutzerdaten.
- Composition zunächst **freiwillig und sitzungsbezogen**, nicht automatisch als Standard. WPF bleibt Fallback, auch bei Laufzeitfehlern.
- Sicherheitsrelevante Capture-Einstellung auf jedem neu erzeugten Overlay-HWND anwenden und Rückgabewert prüfen. Bei Fehler kein unsicheres Composition-Overlay anzeigen.
- Ein Rendering-Backend darf nicht gleichzeitig mit dem anderen sichtbar sein. 60 FPS Cursor-Ziel, ein gemeinsamer Cursor-Takt; keine Totzone.

## Funktionsmatrix / Etappen
1. **Grundlage und Fallback:** Backend an die bestehende App und Dev-UI anschließen. Nur ein physischer Monitor und nicht gespannter Modus für Composition freigeben; andere Aufbauten bleiben auf WPF. Fehler und Umschaltung transparent anzeigen. Overlay-HWND und Composition-Objekte sicher aufräumen.
2. **Funktionen des Ein-Monitor-Pfads:** Ring ein/aus und Fade, Farbe und 0,5-s-Übergang, beide Stile, Dicke, FPS-Einstellung, Capture-Ausschluss, Cursor-Position und Fensterposition. Tray, Hotkey und Webcam sollen unverändert dieselbe Zustandssteuerung benutzen. Taskleisten-/Vollbildwechsel und Style-Animation dürfen nicht stillschweigend verloren gehen: bis gleichwertig implementiert, in diesen Fällen WPF-Fallback.
3. **Windows-Tests:** Build beider Projekte, automatisierte Tests für Auswahl-/Fallbacklogik und Temperatur; manuelle Tests des echten Desktop-Overlays: Klickdurchleitung, Escape/Beenden, Neustart, Capture ein/aus, Schalterwechsel, Farben, Dicke, Tastenkürzel, Webcam und Taskleiste/Vollbild. GPU/CPU/Frametimes vergleichen.
4. **Mehrmonitor und DPI:** negative Koordinaten, ausgewählte und gespannte Displays, unterschiedliche Auflösungen und Skalierung, Hotplug. Koordinatenmodelle explizit testen. Bis dahin WPF-Fallback.
5. **Abnahme und Standard:** Erst nach Praxistests auf mehreren Geräten, Spielen und Capture-Apps entscheiden, ob Composition als Standard aktiviert und Dev-Schalter entfernt wird.

WPF hat heute selbst Grenzfälle bei gemischter DPI; Fallback bewahrt vorhandenes Verhalten, garantiert aber nicht dessen Fehlerfreiheit. Kein Claim „alle Funktionen erhalten“, bis die obige Matrix tatsächlich getestet ist.

## Integrationsschritt 1 (experimentell)
- Dev-Schalter aktiviert Composition nur bei Windows >= 1903, genau einem ausgewählten Monitor, 100 % DPI, nicht gespannt, nicht Vollbild. Keine automatische Speicherung; Start bleibt WPF.
- Composition erhält WPF-Cursor-Takt und -Einstellungen, Ringdicke, Stil beim Aktivieren, aktuelle und animierte Temperatur, Ein-/Aus-Animation sowie Capture-Einstellung. Bei Monitor-/Vollbildänderung oder Stilwechsel wird ausdrücklich zu WPF zurückgeschaltet, um die vorhandenen Übergänge zu bewahren; der Schalter muss danach erneut aktiviert werden. Status steht im Dev-Tab.
- Bei Initialisierungs-, Render- oder Capture-Affinity-Fehler WPF-Fallback. Capture-Ausschluss wird vor dem Anzeigen des Composition-Fensters gesetzt. Eingeschaltete WPF-Caching-/Glow-Diagnosen betreffen weiterhin nur den WPF-Renderer.
- Einschränkung: Mit dem Windows-SDK-Target `net10.0-windows10.0.19041.0` und `SupportedOSPlatformVersion=10.0.17763.0` ist ältere Windows-Kompatibilität ein gesonderter Abnahmepunkt. Auf Desktop, auch beim Toggle und schnellen Wechseln von Capture/Einstellungen, noch nicht getestet.
