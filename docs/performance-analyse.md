# WinRinglight – Performance-Analyse & Bewertung der „Composition"-Idee

Stand: 2026-11-06 · Grundlage: `WinRinglight/MainWindow.xaml(.cs)`, `Config.cs`,
`SettingsWindow.xaml.cs`, `WinRinglight.csproj` sowie das Drive-Paket
`macos-ringlight-simulator-web-app.zip`.

---

## 1. Kurzantwort

**Ja, die Idee trägt – aber nicht so, wie der Beispielcode sie zeigt.**

Der Kern der Idee ist richtig und trifft genau den wunden Punkt:

> Der Ring ist ein *statisches* Bild. Pro Frame dürfen nur zwei Zahlen wandern
> (Mittelpunkt der Aussparung). Kein Vollbild-Repaint, kein CPU-Raster pro Frame,
> keine `UpdateLayeredWindow`-Bitmap pro Frame.

Genau das macht die App heute **nicht**. Sie rastert bei jeder Mausbewegung die
komplette Fensterfläche neu, und die Fensterfläche ist der gesamte virtuelle
Desktop.

Trotzdem ist die Reihenfolge falsch, wenn man sofort auf
`Windows.UI.Composition` umbaut:

1. Der Beispielcode ist in wesentlichen Punkten **nicht lauffähig** (Details in §4) –
   `mask.Mask = hole` mit einem Gradient-Brush wirft zur Laufzeit, und die
   verwendeten APIs sind erst ab Windows 10 **1903** verfügbar.
2. **~80 % des Gewinns** kommen aus vier kleinen Änderungen in der bestehenden
   WPF-App (§5, Aufwand ~1–2 Tage, kein Risiko für MSIX, Hide-from-Capture,
   Multi-Monitor, Hotkey, Webcam-Erkennung).
3. Der Composition-Umbau (§6) ist danach die Kür für „Idle ≈ 0 % CPU" – mit
   einem echten Risiko beim Feature `HideFromCapture`, das getestet werden muss.

---

## 2. Wo die Kosten heute tatsächlich entstehen

Fünf Stellen im Code, in der Reihenfolge ihres Beitrags:

### 2.1 Fenster = kompletter virtueller Desktop (auch wenn nur 1 Monitor leuchtet)

```csharp
// MainWindow.xaml.cs:146-147 und 566-567
this.Width  = SystemParameters.VirtualScreenWidth;
this.Height = SystemParameters.VirtualScreenHeight;
```

Das Fenster ist **immer** so groß wie alle Monitore zusammen – auch bei
`SelectedMonitors = {0}`. Bei zwei 4K-Displays @150 % sind das
7680 × 2160 = 66 MB pro BGRA-Puffer. Ein `AllowsTransparency`-Fenster ist ein
per-Pixel-Alpha-Layered-Window; davon existieren mindestens zwei Puffer
(Front/Back). Dazu kommt: Layered Windows werden von DWM vollflächig geblendet,
selbst wenn 96 % der Pixel transparent sind.

### 2.2 OpacityMask auf dem Wurzel-Grid → Vollflächen-Invalidierung pro Mausbewegung

```xml
<!-- MainWindow.xaml:8-16 -->
<Grid x:Name="MainGrid">
  <Grid.OpacityMask>
    <RadialGradientBrush x:Name="CursorCutoutMask" MappingMode="Absolute" .../>
  </Grid.OpacityMask>
</Grid>
```

```csharp
// MainWindow.xaml.cs:456-457
CursorCutoutMask.Center = relMouse;
CursorCutoutMask.GradientOrigin = relMouse;
```

Das ist der eigentliche Kostentreiber. Eine `OpacityMask` auf einem Container
zwingt WPF, den **kompletten Teilbaum in eine Zwischenfläche zu rendern** und
diese mit der Maske zu multiplizieren (DestIn). Ändert sich der Mittelpunkt des
Brushes, ist die Maske über die *gesamte* Fensterfläche anders → die gesamte
Zwischenfläche plus der gesamte Layered-Window-Puffer sind ungültig. Pro
Mausbewegung, bis 60×/s.

Die Ringbänder selbst sind nur ~20–35 % der Fläche (§3), aber die Maske liegt
über 100 %.

### 2.3 `CompositionTarget.Rendering` bleibt dauerhaft abonniert

```csharp
// MainWindow.xaml.cs:230
CompositionTarget.Rendering += OnRendering;
```

Dieses Event feuert **jeden Frame, auch wenn der Cursor steht** – es hält den
Render-/Animationstakt der UI-Thread-Schleife dauerhaft bei 60 Hz wach. Damit
schläft die App nie richtig (Batterie, Idle-CPU). Der eigentliche Poll
(`GetCursorPos`) ist billig – das Problem ist, dass er immer läuft und bei jeder
Änderung sofort die Vollflächen-Invalidierung aus 2.2 nach sich zieht.

### 2.4 Z-Order-Walk alle 50 ms

```csharp
// MainWindow.xaml.cs:156
_fullscreenTimer.Interval = TimeSpan.FromMilliseconds(50);  // 20×/s
// :342-434 – pro Tick: kompletter Top-Level-Fenster-Walk mit
//   GetClassName + GetWindowThreadProcessId + DwmGetWindowAttribute + GetWindowRect
```

`DwmGetWindowAttribute` ist ein prozessübergreifender Aufruf nach `dwm.exe`.
20×/s × Anzahl Top-Level-Fenster (typisch 100–300) = tausende IPC-Roundtrips pro
Sekunde, nur um „ist ein Vollbildfenster da?" zu beantworten.

### 2.5 Kleinigkeiten

* `ApplySettingsLive` wird vom Webcam-Timer **alle 2 s** neu aufgerufen, sobald
  `AutoTemperature` an ist (`:331`) – setzt `BorderThickness`, `BorderBrush` und
  Blur-Radius für alle Borders neu → voller Repaint alle 2 s, obwohl sich die
  Temperatur kaum ändert.
* `BlurEffect` + `BitmapCache(RenderAtScale = 0.25)` je Band (`:623-629`): WPF-Blur
  ist der teuerste Effekt der Pipeline; der Cache hilft nur, solange nichts
  invalidiert.
* `PackageReference WPF.CornerSmoothing 1.0.7` wird nirgends benutzt.

---

## 3. Größenordnung (gerechnet, nicht gemessen)

Flächen der vier Ringbänder (60 DIP Dicke + je 30 DIP Glow-Rand) gegenüber der
Fensterfläche:

| Setup | Fensterfläche | 4 Bänder | Anteil | Vollfläche/Frame @60 Hz |
|---|---|---|---|---|
| 1× 1080p @100 % | 8,3 MB | 2,9 MB | 35 % | 0,50 GB/s |
| 1× 1440p @100 % | 14,7 MB | 3,8 MB | 26 % | 0,88 GB/s |
| 1× 4K @150 % | 33,2 MB | 8,6 MB | 26 % | 1,99 GB/s |
| 2× 4K @150 % gespannt | 66,4 MB | 14,2 MB | 21 % | 3,98 GB/s |

Die Zahl, die zählt, ist die letzte Spalte: so viel Speicherbandbreite muss
mindestens bewegt werden, wenn pro Mausbewegung die ganze Fläche neu
zusammengesetzt wird. Bei 2× 4K sind das ~4 GB/s **pro Fenster** – zusätzlich zu
allem, was DWM ohnehin macht. Das erklärt, warum es auf großen Setups weh tut und
auf 1080p kaum auffällt.

Der zweite Faktor ist der Layered-Window-Overhead selbst: laut WPF-Team
(Dwayne Need, „Transparent windows in WPF") kostet ein *dauerhaft aktualisiertes*
Vollbild-Layered-Window ~3 % CPU auf XP und **bis zu 30 % auf Vista+** – die Zahl
ist alt, das Prinzip nicht.

> Diese Werte sind aus dem Code abgeleitet. Für die Bestätigung auf dem Zielgerät
> siehe Messplan in §7.

---

## 4. Der Beispielcode aus Drive: was nicht funktioniert

Das Paket ist eine Vite/React-Demo (`src/components/RingLight.tsx`,
`src/components/WindowsGuide.tsx`) – eine **Designstudie plus Code-Skizze**, kein
Port von WinRinglight.

### 4.1 Die Web-Demo selbst: gut, aber nicht die Lösung

`RingLight.tsx` macht genau das Richtige: ein statisches Div mit
`border` + `border-radius` + `filter: blur()`, darüber eine CSS-`mask-image`
(`radial-gradient`), deren Position über zwei CSS-Variablen gesetzt wird –
**ohne React-Rerender**, gedrosselt über `requestAnimationFrame`. Das ist die
richtige Architektur für einen Browser.

Für WinRinglight ist Weg A (Electron, `WindowsGuide.tsx`) trotzdem keine Option:
+100 MB RAM durch Chromium, ein zweiter Runtime-Stack im MSIX-Paket, und
`HideFromCapture`, Webcam-Erkennung, Hotkey und Tray müssten neu gebaut werden.

### 4.2 Weg B (`CS_COMPOSITION`) – 6 konkrete Fehler

| # | Code | Problem |
|---|---|---|
| 1 | `mask.Mask = hole;` | **Wirft.** `CompositionMaskBrush.Mask` akzeptiert nur `CompositionSurfaceBrush` oder `CompositionNineGridBrush` – ein `CompositionRadialGradientBrush` ist nicht erlaubt. Das ist der zentrale Punkt der ganzen Skizze, und er geht so nicht. |
| 2 | `var hole = c.CreateRadialGradientBrush();` | `CompositionRadialGradientBrush` gibt es erst ab Windows 10 **1903** (UniversalApiContract v8). Das Manifest erlaubt aktuell `MinVersion 10.0.14393`. |
| 3 | `c.CreateVisualSurfaceBrush(ringVisual)` | Diese API existiert nicht. Es wäre `c.CreateSurfaceBrush(visualSurface)` mit einer `CompositionVisualSurface` – und die gibt es ebenfalls erst ab **1903**. |
| 4 | `ringShape.StrokeThickness` + `CreatePathGeometry(SquirclePath(...))` | `CreatePathGeometry` braucht eine `IGeometrySource2D`-Implementierung, praktisch Win2D (`CanvasGeometry`) → zusätzliche Abhängigkeit + NuGet im MSIX. |
| 5 | `hole.EllipseCenter = p.X / screenW` | `GetCursorPos` liefert **physische Pixel**, der Compositor arbeitet in **DIP**. Bei 150 %/200 % Skalierung sitzt das Loch um Faktor 1,5–2 falsch. Gleiches Problem wie im heutigen Code (siehe §8). |
| 6 | GaussianBlur als EffectBrush über eine VisualSurface | Genau die Konstruktion, die man vermeiden wollte: eine (nahezu) vollflächige Zwischenfläche plus Blur-Pass. Für einen geraden Lichtstreifen ist ein **vorgeblurrtes 9-Grid-Textur-Band** billiger, älter (ab 1607) und exakt. |

**Fazit zum Code:** Die Richtung (`Compositor`-Visual-Tree, statischer Ring,
nur der Maskenursprung bewegt sich) ist richtig. Die konkrete Umsetzung ist eine
Skizze, die beim ersten Start abstürzt.

### 4.3 Der eine inhaltliche Denkfehler

Die Anleitung sagt „pro Frame ändern sich nur zwei Floats – der Compositor macht
den Rest". Das stimmt nur, wenn die Maske *im Compositor* liegt und sich per
Animation verschieben lässt. Eine Surface-Maske (Punkt 1) lässt sich zwar in der
Tat animieren (`SurfaceBrush.Offset`/`Scale` laufen auf dem Compositor-Thread),
aber nur als **Verschiebung eines fertigen Bildes**. Damit die Aussparung
*überall* sitzen kann, müsste die Maske so groß wie der Ring sein – also wieder
eine Bildschirm-große Textur.

Sauber geht es nur über einen Custom-Pixel-Shader-Effekt
(`PixelShaderEffect` mit zwei animierbaren Uniforms `centerX/centerY`,
`Properties.StartAnimation(...)`). Der läuft dann tatsächlich auf dem
Compositor-Thread: die UI-Thread-Kosten pro Frame gehen gegen **null**, die
GPU-Kosten sind ein Vollbild-Alpha-Pass (auf 4K ~0,1–0,3 ms). Das ist der
ehrliche Preis – nicht „kostenlos", aber um Größenordnungen billiger als heute.

---

## 5. Stufe 1 – was ich zuerst machen würde (WPF, ohne Umbau)

Ziel: gleiche Optik, ~5–20× weniger Arbeit pro Cursorbewegung. Risiko: niedrig.
Alle vier Punkte sind lokal.

1. **Fenster auf die genutzte Fläche begrenzen.**
   `this.Left/Top/Width/Height` = Bounding-Box der `targetRects` aus
   `UpdateMonitorSetup()` statt `VirtualScreen*`. Liegt bei „nur Monitor 1 von 3"
   sofort Faktor 2–3. (Sind die gewählten Monitore nicht zusammenhängend, wäre
   ein Fenster je Monitor die nächste Stufe.)
2. **Maske vom Grid auf die einzelnen Borders verschieben.**
   `OpacityMask` je `glowBorder`/`coreBorder` statt einmal auf `MainGrid`. Dann
   ist die invalierte Fläche das Band (~20–35 % statt 100 %), und die
   Zwischenfläche ist bandgroß. Bild identisch, weil die Maske absolut
   koordiniert bleibt (`MappingMode="Absolute"` + gleicher Mittelpunkt).
3. **`BlurEffect` durch Verläufe ersetzen.**
   Ein gerader Lichtstreifen braucht keinen Gauß: je Kante drei `Rectangle` mit
   `LinearGradientBrush` (innen transparenter Glow → Kern → außen Glow). Kosten
   ~0, kein Zwischensurface, kein Cache. Der Blur ist danach der einzige
   verbleibende „teure" Effekt – und er ist weg.
4. **Frame-Takt an die Bewegung koppeln.**
   `CompositionTarget.Rendering` abbestellen; stattdessen `DispatcherTimer`
   (30 Hz) nur solange `_isRinglightOn`, und nur aktualisieren, wenn sich die
   Position um ≥ 2 px geändert hat. Idle-CPU geht damit gegen 0.
5. **Vollbild-Erkennung event-getrieben.**
   `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` + 1 s-Fallback statt 20 Hz
   Z-Walk. (Der Z-Walk bleibt als seltener Nachlauf, z. B. alle 2 s.)
6. **Auto-Temperatur drosseln.** In `WebcamTimer_Tick` nur neu anwenden, wenn
   sich die gerundete Kelvin-Zahl um > 50 K geändert hat.
7. Aufräumen: `WPF.CornerSmoothing` aus dem `.csproj` (ungenutzt).

Erwartung nach Stufe 1 (aus §3 abgeleitet): bei 2× 4K sinkt die pro Bewegung
neu zusammengesetzte Fläche von 66 MB auf ~2–3 MB – also ~20×. Das sollte den
gefühlten „frisst Ressourcen"-Effekt bereits beseitigen.

---

## 6. Stufe 2 – der Composition-Weg, richtig gemacht

Nur wenn Stufe 1 nicht reicht oder „Idle = 0 %" ein Produktversprechen werden
soll.

**Architektur:**

* Ein `HWND` je Monitor, `WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT |
  WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, `Compositor` + `DispatcherQueueController`,
  `ICompositorDesktopInterop::CreateDesktopWindowTarget(hwnd, isTopmost, ...)`.
* **Ring:** vorgeblurrte Bandtextur, als `CompositionNineGridBrush` gestreckt
  (funktioniert ab 1607, kein Blur-Pass, keine Zwischenfläche). Ecken als
  eigener kleiner 9-Grid bzw. als `SpriteShape` mit Stroke – dann aber ohne Blur.
* **Aussparung:** `PixelShaderEffect` (HLSL, `fxc` → `.cso`, Win2D
  `PixelShaderEffect`) auf dem Ring-Bild; zwei animierbare Uniforms
  `Center.XY`. Bewegung per `Properties.StartAnimation` → **Compositor-Thread,
  UI-Thread sieht pro Frame nichts**.
  Alternative ohne Shader: Maske als bandgroße Surface + `SurfaceBrush.Offset`
  animieren – billiger zu bauen, aber die Aussparung muss dann bandweise
  zugeschnitten werden.
* **Cursor:** `GetCursorPos` in einem Timer (15–30 Hz ist genug; die Bewegung
  wird per Keyframe-Interpolation weichgezeichnet), Position mit
  `TransformFromDevice`-Äquivalent in DIP umrechnen.

**Kosten:** 3–5 Tage inkl. Shader und Test. **Risiken:**

1. **`HideFromCapture` (`WDA_EXCLUDEFROMCAPTURE`) muss getestet werden.**
   Es gibt dokumentierte Fälle (u. a. Qt-Overlays unter Windows 10), in denen
   `SetWindowDisplayAffinity` auf transparenten/layered Fenstern mit
   `ERROR_NOT_ENOUGH_MEMORY (8)` scheitert; ein `NOREDIRECTIONBITMAP`-Fenster hat
   zusätzlich gar keine Redirection-Surface mehr. Das Feature ist per Default
   **an** und für Streamer das Verkaufsargument – wenn es wegbricht, ist der
   Umbau ein Rückschritt.
2. **Mindest-Windows-Version.** 9-Grid + MaskBrush + EffectBrush gehen ab 1607/1511;
   `RadialGradientBrush`/`VisualSurface` erst 1903. Das Manifest müsste angehoben
   werden (oder Fallback-Pfad).
3. **MSIX.** Win2D + ggf. Windows App SDK vergrößern das Paket deutlich.
4. **Fullscreen-Spiele.** Ein Composition-Fenster verhält sich gegenüber
   exklusivem Vollbild nicht anders als heute (bleibt unsichtbar) – die
   bestehende Vollbild-Logik muss also mit.

---

## 7. Messplan (10 Minuten, vor und nach jeder Stufe)

1. Task-Manager → Details: `WinRinglight.exe` CPU **im Stillstand** (Cursor
   steht, 60 s) und **bei Kreisbewegung** (30 s). Zwei Zahlen, mehr braucht es
   für die A/B-Referenz nicht.
2. `PresentMon --process_name WinRinglight.exe --output_csv pm.csv` → Spalten
   `msBetweenPresents`, `GPU`/`CPU`-Zeit. Zeigt, ob wirklich pro Frame
   präsentiert wird.
3. `dotnet-counters monitor -p <pid> --counters System.Runtime` → GC- und
   Alloc-Rate; ein hoher Wert deutet auf neu erzeugte Geometrien/Brushes pro
   Frame hin.
4. WPF Performance Suite (Perforator): „Dirty Rect Size" und
   „Frame Rate" – damit sieht man schwarz auf weiß, dass eine Cursorbewegung
   heute die volle Fensterfläche als dirty markiert.

Zielwerte: Stillstand < 1 % CPU, Kreisbewegung < 5 % auf 2× 4K, keine
Dirty-Rect-Sprünge über die Bandfläche hinaus.

---

## 8. Nebenbefund: die Aussparung sitzt auf HiDPI falsch

```csharp
// MainWindow.xaml.cs:440-457
GetCursorPos(ref _mousePt);              // physische Pixel
ScreenToClient(hwnd, ref localPt);       // weiterhin physische Pixel
CursorCutoutMask.Center = relMouse;      // WPF erwartet DIPs
```

Bei 150 %/200 % Skalierung wird der Mittelpunkt um Faktor 1,5/2 zu weit
außen liegen. Korrekt wäre:

```csharp
var m = PresentationSource.FromVisual(this).CompositionTarget.TransformFromDevice;
var dip = m.Transform(new Point(localPt.X, localPt.Y));
```

Das ist kein Performance-Thema, aber es fällt beim Nachmessen sowieso auf – und
der Composition-Weg hat exakt denselben Fallstrick (Fehler 5 in §4.2).

---

## 9. Empfehlung

| | Stufe 1 (WPF aufräumen) | Stufe 2 (Composition) |
|---|---|---|
| Aufwand | 1–2 Tage | 3–5 Tage |
| Gewinn | ~20× weniger Arbeit pro Bewegung, Idle ~1 % | Idle ~0 %, UI-Thread pro Frame idle |
| Risiko | niedrig, keine Feature-Änderung | mittel–hoch (`HideFromCapture`, Min-Version, Paketgröße) |
| Optik | unverändert | unverändert möglich |

**Vorgehen:** Stufe 1 umsetzen, mit §7 messen. Nur wenn die Zahlen danach noch
nicht reichen, Stufe 2 – und dort zuerst einen 30-Zeilen-Spike bauen, der
**nur** testet: `NOREDIRECTIONBITMAP`-Fenster + `SetWindowDisplayAffinity` +
OBS-Aufnahme. Fällt dieser Test durch, ist der Composition-Weg für dieses
Produkt gestorben, egal wie schnell er ist.

Die Drive-Idee ist damit **als Konzept bestätigt, als Code nicht übernehmbar**.
Der wertvollste Satz daraus ist Regel 1 der Anleitung – „nie pro Frame neu
rastern" – und genau die ist in Stufe 1 schon erfüllt.
