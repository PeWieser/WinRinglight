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
- Windows-Workflow-Lauf 42289755839 für Commit 9b42ff6: Build und Publish erfolgreich, Artefakt `WinRinglight-test-win-x64` hochgeladen. Kein automatisierter GUI- oder Performance-Test; Nutzergerät-Prüfung steht aus. GitHub meldet eine Node-20-Action-Deprecation-Warnung, Lauf erfolgreich.

## Nutzer-Messung und nächster Vergleich
- Ausgangsversion mit 16-ms-Cursor-Timer: Nutzer berichtet auf GTX 1060 6 GB ca. 20 % GPU im Stillstand (inkl. übriges Windows), fast 60 % bei Mausbewegung; CPU ca. 0,1 %. Last des anderen Branches laut Nutzer selbst bei Bewegung nahe Idle. Zahlen sind keine isolierten Prozess- oder identischen Szenariotests und dürfen nicht als exakter App-Anteil interpretiert werden.
- Anderer Branch drosselt Updates auf 33 ms und 2 DIP und ersetzt den Blur durch Glow-Geometrie. Sein RenderBench verwendet RenderTargetBitmap/Software und sein früher Vier-Streifen-Prototyp ist nicht mit dem späteren Code identisch; der Bench beweist keine tatsächliche GPU-Beschleunigung oder identische Optik.
- Isolierter nächster Test: 33-ms-Timer ohne tote Zone, sonst unveränderte Ausgangsoptik. Vergleich auf Nutzergerät nötig; hohe Last kann weiterhin bestehen. Danach getrennt eine optisch korrekte und messbare Alternative für den vollflächigen Blur untersuchen, ohne fehlerhafte Pfadgeometrie zu übernehmen.

## 60 Hz und experimenteller Glow
- Nutzerfeedback: 30 Hz deutlich zu ruckelig, 60 Hz erforderlich. 16-ms-Cursor-Timer wiederhergestellt; DispatcherTimer und Rechnerlast garantieren keine echte Bildfrequenz.
- Glow-Versuch: ursprünglichen Bildschirm-Blur und BitmapCache ersetzt durch zwölf transparente, konzentrische, gestrichene WPF-Rundrechtecke (`GlowBorder`). Der ursprüngliche Border-Kern bleibt unverändert. Das vermeidet die fehlerhafte gekoppelte Innen-/Außen-PathFigure aus EXE 13 und den BlurEffect. Optik und GPU-Gewinn sind NICHT verifiziert; die diskrete Schichtung kann sichtbare Stufen/Kanten verursachen.
- Windows-Workflow ergänzt `GlowCheck`: einfache Rasterprüfung für leere Mitte, Ring und Innensaum bei zwei Eckenradien. Kein Nachweis für einheitlichen Glow, echte Overlay-Ausgabe oder GPU-Leistung.
- Workflow 42291643738 schlug wegen mehrdeutigem `Pen` fehl; durch vollqualifizierten WPF-Typ behoben. Workflow 42291922913 für 0e912e9: Build, einfacher Glow-Rastercheck und Testpaket erfolgreich. Nutzer- und GPU-Test weiterhin offen.

## Feedback und Korrektur der Ecken
- Nutzergerät GTX 1060: experimenteller Glow, 16 ms: GPU ca. 0 % ruhend, 12 % bei Bewegung; CPU ca. 0 % auf 3 %. Nutzer meldet: Glow bei runden Ecken außen vom Ring abgesetzt, innen unter Ring verschwunden; Screenshot-Upload derzeit blockiert. Kein standardisiertes Profiling, daher Ursache der CPU-Zunahme offen.
- Gegen WPF-Referenzcode `dotnet/wpf` Border.OnRender geprüft: bei gleichmäßiger Dicke und SolidColorBrush zeichnet WPF auf dem um die halbe Dicke eingerückten Rechteck mit `CornerRadius` direkt als Mittelpunkt-Radius. GlowBorder hatte stattdessen `CornerRadius - halbe Dicke` verwendet. Behoben; Rendercheck misst jetzt auch den Innensaum der Rundung gegen die gerade Kante. Kein Ersatz für Nutzer-Screenshot oder GPU-/CPU-Messung.

## Nutzertest Glow und Temperaturübergang
- Nutzer bestätigt die Glow-Optik für beide Varianten in Version e153c12. GPU beim Start kurz ca. 31 %, danach bei Mausbewegung stabil ca. 11 %, CPU bis ca. 3,3 % auf Ryzen 5 2600X / GTX 1060 6 GB. Prozentwerte/Task-Manager-Methode nicht genauer bekannt; kein Beleg für Leistung auf iGPU oder in Spielen. Keine Null-Auswirkungen versprechen.
- 500-ms-Farbtemperaturübergang beim Umschalten der Tageszeit-Automatik; ein wiederholtes Umschalten startet beim gerade sichtbaren Wert, manueller Regler bleibt direkt. Kein laufender Übergangstimer im Ruhezustand. Auto-Temperatur wurde vorher nur bei aktivierter Webcam-Automatik regelmäßig aktualisiert; periodische Aktualisierung jetzt unabhängig von Webcam-Automatik. Der mathematische Verlauf wird in GlowCheck geprüft, WPF-Interaktion und Erscheinung müssen auf dem Nutzergerät geprüft werden.

## Aktuelle Anforderungen
- Nutzer bestätigt den 0,5-s-Temperaturübergang. Temperaturslider soll bei Auto-Modus immer verschiebbar bleiben, die gespeicherte manuelle Temperatur jedoch erst bei deaktiviertem Auto-Modus anwenden.
- FPS-Grenze nach Rückfrage nur für Cursor-Aussparung: Zahleneingabe und +/- bzw. Pfeiltasten, 1–240, Standard 60, unbegrenzt mit Warnung; unbegrenzt bedeutet WPF-Rendering-Takt, keine garantierte unbegrenzte FPS. Einstellungen speichern. Andere Animationen nicht beeinflussen.
- Bei maximaler Dicke meldet Nutzer ca. 45 % GPU. Experiment: statt 12 über die ganze Dicke reichenden Halo-Strichen nur dünne Striche an Innen- und Außenkante; Kern bleibt original. Visuelle und GPU-Effekte müssen verglichen werden, das ist kein garantierter Leistungsgewinn. Im anderen Branch: 33 ms und 2-DIP-Totzone, globale OpacityMask weiterhin; 30 Hz wurde vom Nutzer ausdrücklich abgelehnt.
- Windows-Lauf 42310374946 schlug wegen mehrdeutigem KeyEventArgs fehl; WPF-Typ explizit gemacht. Lauf 42310499624 (d32e98f) baut erfolgreich, Renderchecks samt grobem Pixelvergleich bestehen, Artefakt vorhanden. Keine GPU-Messung/echte GUI-Interaktion in CI; Nutzertest und FPS-Überprüfung stehen aus.

## Rückmeldung zu d32e98f
- Nutzer bestätigt Temperaturregler; System-Tab durch neue FPS-Karte überladen und scrollbar. FPS-Karte in eigenen Tab „Leistung“ verschoben, bisheriges System-Layout ohne ScrollViewer wiederhergestellt. Auf kleineren Auflösungen/DPI nicht visuell verifiziert.
- GPU bei maximaler Dicke nach schmalen Glow-Konturen: bei starkem Hin- und Herwackeln bis ca. 34 %, moderat ca. 15 % (Nutzerangaben). Zuvor max. ca. 45 %, Messung nicht standardisiert. Nutzer lehnt Drosselung unter 60 Hz wegen Ruckeln ab. Keine Totzone einbauen, ohne sie zu besprechen.

## Dev-Experimente (temporär)
- Nutzer möchte mögliche Änderungen testweise per Dev-Tab aktivieren, ohne Framerate-Drosselung. Drei abgeschaltete Sitzungs-Schalter: BitmapCache auf Ring- und Glow-Elementen, BitmapCache auf der gesamten maskierten Gruppe, Glow aus (Diagnose). Optik und GPU-Speicherbedarf können sich ändern, Ergebnisse sind hardwareabhängig. Zustand gilt sofort und wird beim Beenden verworfen; neue Monitorelemente übernehmen ihn. Keine Totzone und kein versteckter 30-Hz-Modus.
- Das sind bewusst WPF-Experimente und keine vollständige Umsetzung aller denkbaren Optimierungen: ein echter Windows-Composition-Renderer braucht ein gesondertes Prototyp-Fenster und Tests für Capture, Klickdurchleitung, Monitor/DPI und Optik. Nicht behaupten, dieser sei hier eingebaut.
- Testverfahren: gleiches Setup, 60 FPS, maximale Dicke, gleiche Cursorbewegung, mindestens 30–60 s. Alle aus als Referenz, dann jeden Schalter separat (insbesondere Glow aus nur als Diagnosetest). GPU/CPU inkl. DWM und Erscheinung festhalten; optional Kombinationen im Anschluss.

## Messergebnis Dev-Experimente
- Nutzer: Glow abschalten macht fast keinen Lastunterschied. Cache einzelner Ring-/Glow-Elemente brachte das beste Ergebnis, GPU-Maximum ca. 27 % gegenüber vorher ca. 45 % laut Nutzer. Frühere Messung der optimierten Version mit maximaler Dicke: ca. 34 % bei starkem Wackeln, 15 % moderat; die Vergleichsbedingungen/Baseline der 45 % sind nicht im Detail standardisiert. Keine Kausalität oder exakten Einsparungsfaktor behaupten.
- Nutzer vermutet Cursor-Aussparung als Hauptlast und fragt nach Windows Composition. Keine Entscheidung zum Umbau getroffen. Die drei Dev-Optionen bleiben temporär, nicht automatisch zum Standard machen; das Caching muss auf Optik/anderen PCs/GPU-Speicher und Gameplay geprüft werden.

## Windows-Composition-Prototyp
- Nutzer hat einen separaten WPF-Prototyp beauftragt. Plan und Testgrenzen stehen in `tools/CompositionPrototype/README.md`. Normale App bleibt unverändert, x64-Prototyp wird separat gebaut. Ein nativer NOREDIRECTIONBITMAP-Top-Level-HWND soll Composition-Visuals hosten; das Steuerfenster ist WPF.
- Ring als ShapeVisual und Cursorgradient als SpriteVisual; beide in CompositionVisualSurface, CompositionMaskBrush erhält eine SurfaceBrush-Maske. 16-ms-Cursor-Timer (Ziel 60 Hz), keine Totzone. Nur Hauptmonitor, feste Farbtemperatur, Ringdicke/Form einstellbar, Capture-Ausschluss zum Test optional; keine Produktivfunktionen behaupten.
- Zurzeit sind Darstellung, Cursor-Aussparung, Click-through, Capture-Ausschluss, GPU/CPU und tatsächliche Bildrate auf echtem Windows-Gerät unbestätigt. CI-Build ist kein Laufzeitnachweis. Prototyp nicht zusammen mit normaler App starten; bei Problemen Steuerfenster schließen oder Prozess beenden.
- Windows-Lauf 42313342572: normaler Build erfolgreich, Prototyp-Build wegen mehrdeutigem `Color` fehlgeschlagen. Windows-Lauf 42313489598 (988a081): beide Projekte gebaut, normale Renderchecks bestanden, separate x64-Artefakte hochgeladen. Prototyp nicht auf interaktivem Windows-Desktop ausgeführt; Start, Darstellung und Leistung bleiben offen.

## Composition-Prototyp: Klickdurchleitung
- Nutzer hat Prototyp 988a081 auf eigener Hardware getestet: Optik und Capture-Ausschluss funktionieren, GPU maximal etwa 9 %, aber Klicks gehen nicht durch. Nutzermessung, keine standardisierte FPS-/DWM-Messung.
- Versuch: Overlay-HWND erhält zusätzlich WS_EX_LAYERED; SetLayeredWindowAttributes mit voller globaler Deckkraft aktiviert Layered-Verhalten, WS_EX_TRANSPARENT soll Hit-Tests an darunterliegende Fenster durchreichen. Kombination mit WS_EX_NOREDIRECTIONBITMAP und Composition ist experimentell; erfolgreiche Kompilierung belegt weder Klickdurchleitung noch Transparenz, Performance oder Capture-Ausschluss. Falls sie scheitert, Architektur separat untersuchen statt als behoben ausgeben.
