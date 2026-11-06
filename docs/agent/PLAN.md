# Plan – WinRinglight Performance

Stand: 2026-11-06 · Arbeitsgedächtnis: `docs/agent/ARBEITSGEDÄCHTNIS.md`

## Leitregel

Optik bleibt gleich. Wir ändern nur, **wie viel gerechnet wird**.
Jede Änderung ist einzeln abschaltbar und einzeln prüfbar.

---

## Phase 0 – Werkzeuge (Voraussetzung für alles)

| | Aufgabe | Status |
|---|---|---|
| 0.1 | GitHub-Actions-Workflow `CI` (windows-latest, build + Tests) | ☐ |
| 0.2 | Automatischer Kommentar in den PR mit Build-Ergebnis | ☐ |
| 0.3 | Pull Request für den Arbeitsbranch anlegen (Kommentarziel) | ☐ |
| 0.4 | Erster grüner Lauf als Beweis, dass der Kreislauf steht | ☐ |

## Phase 1 – Fenster auf die genutzte Fläche begrenzen  ← **jetzt**

Der größte Einzelposten. Fenster ist heute immer `VirtualScreen*` groß.

| | Aufgabe | Status |
|---|---|---|
| 1.1 | Bounding-Box der gewählten Monitore berechnen (in DIP) | ☐ |
| 1.2 | Fenster auf diese Box setzen statt auf den ganzen Desktop | ☐ |
| 1.3 | Border-Positionen relativ zur Box statt zum Desktop | ☐ |
| 1.4 | Cursor-Koordinaten in dieselbe Box umrechnen | ☐ |
| 1.5 | Fallback: bei Unsinn (leere Box) alles wie bisher | ☐ |
| 1.6 | Build grün + Selbsttest im Log | ☐ |

**Erwartung:** Bei „1 von 3 Monitoren" sinkt die Fensterfläche um Faktor 2–3.
Bei 2× 4K gespannt bleibt sie gleich – dort bringt Phase 2 den Gewinn.

## Phase 2 – Maske von der Wurzel auf die Ringe

| | Aufgabe | Status |
|---|---|---|
| 2.1 | `OpacityMask` je Border statt auf `MainGrid` (gleiche Koordinaten) | ☐ |
| 2.2 | Prüfen: sitzt das Loch noch exakt über der Maus? (Rechnung + Log) | ☐ |
| 2.3 | Nur noch die Bandfläche wird invalidiert (Messwert notieren) | ☐ |

**Erwartung:** Invalidierte Fläche von 100 % auf ~25 % der Fensterfläche.

## Phase 3 – Weichzeichner durch Verläufe ersetzen

| | Aufgabe | Status |
|---|---|---|
| 3.1 | Glow-Rand als `LinearGradientBrush` statt `BlurEffect` | ☐ |
| 3.2 | Optik-Vergleich (Werte: Kantenweichheit in px) | ☐ |
| 3.3 | `BitmapCache` prüfen/entfernen, wenn nutzlos | ☐ |
| 3.4 | Schalter `UseGradientGlow` in den Einstellungen | ☐ |

## Phase 4 – Nur rechnen, wenn sich etwas bewegt

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
