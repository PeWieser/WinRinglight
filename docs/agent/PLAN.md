# Plan – WinRinglight Performance

Stand: 2026-11-06 · Arbeitsgedächtnis: `docs/agent/ARBEITSGEDÄCHTNIS.md`

## Leitregel

Optik bleibt gleich. Wir ändern nur, **wie viel gerechnet wird**.
Jede Änderung ist einzeln abschaltbar und einzeln prüfbar.

---

## Phase 0 – Werkzeuge (Voraussetzung für alles)

| | Aufgabe | Status |
|---|---|---|
| 0.1 | GitHub-Actions-Workflow `CI` (windows-latest, build + Tests) | ✅ |
| 0.2 | Automatischer Kommentar in den PR mit Build-Ergebnis | ✅ |
| 0.3 | Pull Request für den Arbeitsbranch anlegen (Kommentarziel) | ✅ PR #1 |
| 0.4 | Erster grüner Lauf als Beweis, dass der Kreislauf steht | ✅ Lauf 42238446769 |

## Phase 1 – Fenster auf die genutzte Fläche begrenzen  ← **✅ erledigt**

Der größte Einzelposten. Fenster ist heute immer `VirtualScreen*` groß.

| | Aufgabe | Status |
|---|---|---|
| 1.1 | Bounding-Box der gewählten Monitore berechnen (in DIP) | ✅ `ScreenLayout.Compute` |
| 1.2 | Fenster auf diese Box setzen statt auf den ganzen Desktop | ✅ `SetWindowPos` |
| 1.3 | Border-Positionen relativ zur Box statt zum Desktop | ✅ |
| 1.4 | Cursor-Koordinaten in dieselbe Box umrechnen | ✅ `ToWindowDip` |
| 1.5 | Fallback: bei Unsinn (leere Box) alles wie bisher | ✅ |
| 1.6 | Build grün + Selbsttest im Log | ✅ 22 Tests |

**Erwartung:** Bei „1 von 3 Monitoren" sinkt die Fensterfläche um Faktor 2–3.
Bei 2× 4K gespannt bleibt sie gleich – dort bringt Phase 2 den Gewinn.

## Phase 2 – ~~Maske von der Wurzel auf die Ringe~~ ❌ **VERWORFEN**

**Warum verworfen (nachgerechnet, vor dem Bauen):**
Eine `OpacityMask` wirkt in WPF auf die **Bounds** eines Elements, nicht auf die
gemalten Pixel. Eine `Border`, die den ganzen Monitor umrahmt, hat
Bounds = ganzer Monitor. Die Maske dorthin zu verschieben bringt **0 %**.
Auch das Aufteilen in 4 Segmente pro Ring ändert die Summe der Bounds nicht.

**Nebenbefund, der wichtiger ist:** Der Masken-Verlauf ist mit Radius 432 px
(40 % der Bildschirmhöhe) so groß, dass der Ring nur in einem 888 × 48 px
großen Streifen (2 % der Fläche) *nicht* berührt wird. Die Maske wirkt also
praktisch immer – sie lässt sich in WPF nicht verkleinern, nur seltener
aktualisieren. Das ist Phase 2 (neu).

## Phase 2 (neu) – seltener neu zeichnen, Dauerläufer schlafen legen  ← **✅ erledigt**

| | Aufgabe | Status |
|---|---|---|
| 2a.1 | `CursorThrottle`: Mindestabstand 33 ms + tote Zone 2 DIP | ✅ |
| 2a.2 | In `OnRendering` eingebaut, ersetzt die alte 16-ms-Sperre | ✅ |
| 2a.3 | Beim Einschalten sofort zeichnen (`Reset()`) | ✅ |
| 2b.1 | `IdlePolicy`: Timer läuft nur, wenn er etwas bewirken kann | ✅ |
| 2b.2 | Teurer Z-Order-Walk max. 1×/s statt 20×/s | ✅ |
| 2b.3 | Zustand wird nur von einem echten Walk überschrieben | ✅ |
| 2b.4 | 45 Tests grün | ✅ |

**Gemessen (nicht geschätzt):** 60 Eingaben/s → 20 Neuzeichnungen (−67 %).
Im Leerlauf 20 Timer-Ticks/s → 1 Tick/s (−95 %). Bei 30 Hz Eingabe geht
nichts verloren.

## Phase 3 – Weichzeichner durch Verläufe ersetzen  ← **✅ erledigt**

| | Aufgabe | Status |
|---|---|---|
| 3.1 | Glow-Rand als `LinearGradientBrush` statt `BlurEffect` | ✅ 4 Kantenflächen |
| 3.2 | Optik-Vergleich (Profil gegen Gauß geprüft) | ✅ Abweichung < 0,011 |
| 3.3 | `BitmapCache` entfernt (war messbar schädlich) | ✅ |
| 3.4 | Glow nur bei sichtbaren Änderungen neu bauen | ✅ `GlowRebuildPolicy` |
| 3.5 | Glow und Kern animieren synchron | ✅ `AnimateGlowToRect` |

**Gemessen im CI-Benchmark (Software-Rasterizer, ohne GPU):**

| Setup | vorher | nachher | Faktor |
|---|---|---|---|
| 1 Monitor 1080p | 220,9 ms/Bild | **38,4 ms** | **5,8×** |
| 1 von 3 (1080p) | 234,5 ms | **40,4 ms** | **5,8×** |
| 1 von 2 (4K @150 %) | 899,7 ms | **154,3 ms** | **5,8×** |
| 2 gespannt (4K) | 1760,9 ms | **279,2 ms** | **6,3×** |

**Warum der Blur so teuer war:** Ein `BlurEffect` wirkt auf die **Bounds** des
Elements. Die Border ist monitorgroß, also wurden alle 2 Mio. Pixel verwischt –
für einen 60 px breiten Streifen. Der `BitmapCache` half nicht, sondern machte
es 18 % teurer.

**Warum es flüssig bleibt:** Der Glow hängt an Größe/Dicke/Farbe, nicht an der
Mausposition. `GlowRebuildPolicy` verhindert den Neubau bei Bewegung (7 Tests).

## Phase 4 – Nur rechnen, wenn sich etwas bewegt  ← **als Nächstes**

| | Aufgabe | Status |
|---|---|---|
| 4.1 | `CompositionTarget.Rendering` abbestellen | ☐ |
| 4.2 | `DispatcherTimer` 30 Hz, nur bei aktivem Ring | ☐ |
| 4.3 | Tote Zone: erst ab ±2 px neu zeichnen | ☐ |
| 4.4 | Auto-Temperatur drosseln (nur bei > 50 K Änderung) | ☐ |

**Erwartung:** Stillstand ≈ 0 % CPU.

## Phase 5 – Vollbild-Erkennung ohne Dauerfragen

| | Aufgabe | Status |
|---|---|---|
| 5.1 | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` einbauen | ☐ |
| 5.2 | Z-Order-Walk auf 1–2 s Fallback reduzieren | ☐ |
| 5.3 | Verhalten prüfen: Vollbild an/aus, Startmenü, Taskleiste | ☐ |

## Phase 6 – Aufräumen & Messen

| | Aufgabe | Status |
|---|---|---|
| 6.1 | Ungenutztes Paket `WPF.CornerSmoothing` entfernen | ☐ |
| 6.2 | `FlushMemory()`-Aufrufe prüfen (GC.Collect bei jedem Start?) | ☐ |
| 6.3 | `docs/agent/MESSUNGEN.md` mit Zahlen füllen | ☐ |
| 6.4 | README-Abschnitt „Performance" ergänzen | ☐ |

## Phase 7 – Optional: Composition (nur falls nötig)

Erst nach Messung entscheiden. Vorher 30-Zeilen-Spike:
`NOREDIRECTIONBITMAP` + `WDA_EXCLUDEFROMCAPTURE` + OBS-Test.
Fällt der Spike durch, entfällt diese Phase.

---

## Definition of Done für Phase 1

1. CI grün, keine neuen Warnungen.
2. Selbsttest im Log zeigt: Fenstergröße = Bounding-Box der gewählten Monitore.
3. Bei einem Monitor und drei Monitoren im System: Fenster kleiner als vorher.
4. Ring sitzt an derselben Stelle wie vorher (Rechnung stimmt).
5. Kein Absturz, wenn `SelectedMonitors` leer ist.
