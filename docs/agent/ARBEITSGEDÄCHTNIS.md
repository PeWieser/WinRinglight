# Arbeitsgedächtnis – WinRinglight Performance

Diese Datei ist mein Gedächtnis für die Arbeit. Sie wird nach jeder Phase
aktualisiert. **Vor dem Anfang einer Phase immer zuerst hier lesen.**

---

## 1. Umgebung (wichtig, sonst Zeitverlust)

| Sache | Stand |
|---|---|
| Lokales `dotnet` | **nicht installiert** (`dotnet: command not found`) |
| NuGet von der Sandbox | **blockiert** (`api.nuget.org` → HTTP 000) |
| Kompilieren | **nur über GitHub Actions** (windows-latest) möglich |
| GUI/Browser in der Sandbox | nicht nutzbar (kein Chromium, kein WPF) |
| Git/gh | funktioniert, eingeloggt als `PeWieser` |
| Zeit pro CI-Lauf | ~3–6 Minuten |

**Konsequenz:** Jede Code-Änderung muss klein sein. Erst kompilieren lassen,
dann die nächste Änderung. Niemals fünf Umbauten auf einmal pushen, sonst ist
der Fehler nicht zuordenbar.

## 2. Wie ich CI-Ergebnisse lese (der Kreislauf)

1. Änderung committen und pushen:
   `git add -A && git commit -m "..." && git push origin arena/c0ca9d72-winringlight`
2. Lauf abwarten:
   `gh run list --branch arena/c0ca9d72-winringlight --limit 3`
   `gh run watch <run-id> --exit-status`
3. Ergebnis aus dem **PR-Kommentar** lesen (so vom Nutzer gewünscht):
   `gh pr view <nr> --comments | tail -80`
   bzw. maschinell: `gh api repos/PeWieser/WinRinglight/issues/<nr>/comments --jq '.[-1].body'`
4. Bei Fehler: `gh run view <run-id> --log-failed | tail -120`

Der Workflow heißt `CI` (`.github/workflows/ci.yml`) und schreibt nach jedem Lauf
einen Kommentar mit Build-Status, Fehler-/Warnungszahl und Testergebnis.

## 3. Was ich über den Code gelernt habe

* **`AllowsTransparency=True`** (MainWindow.xaml:4) macht das Fenster zu einem
  Per-Pixel-Alpha-Layered-Window. Das ist die Wurzel der meisten Kosten.
* **Fenster ist immer `VirtualScreenWidth × VirtualScreenHeight`**
  (MainWindow.xaml.cs:146, 566) – auch wenn nur ein Monitor gewählt ist.
* **`OpacityMask` liegt auf dem Wurzel-Grid** (MainWindow.xaml:8). Jede
  Mausbewegung invalidiert damit die *gesamte* Fensterfläche.
  → Das ist der größte Einzelposten.
* **`CompositionTarget.Rendering`** (Zeile 230) feuert jeden Frame, auch im
  Stillstand. Ersetzt durch Dispatcher-Timer (Phase 1).
* **Z-Order-Walk** alle 50 ms mit `DwmGetWindowAttribute` (Zeile 156, 347 ff.).
* **Einheiten-Mischung:** `Screen.Bounds` liefert **physische Pixel**,
  `SystemParameters.VirtualScreen*` liefert **DIPs**. Der Code subtrahiert beides
  direkt voneinander (Zeile 561-562) → bei Skalierung ≠ 100 % versetzt.
* **Cursor:** `GetCursorPos` + `ScreenToClient` liefern physische Pixel, werden
  aber ungeprüft als DIP in `CursorCutoutMask.Center` gesteckt (Zeile 456).
* Farbe: `t = (kelvin-2000)/4500`, `r=255-20t`, `g=170+75t`, `b=50+205t`.

## 4. Fehler, die ich schon gemacht habe (nicht wiederholen)

* **CI-Workflow, der eine WPF-App unter `ubuntu-latest` baut** → geht nicht.
  Immer `windows-latest`.
* **`.NET Framework` annehmen** – das Projekt ist `net10.0-windows`.
  Die README behauptet „.NET Framework 10", das ist falsch.
* **`CompositionMaskBrush.Mask = GradientBrush`** → wirft zur Laufzeit.
  (Steht im Drive-Beispiel falsch drin; nicht abschreiben.)
* **Drive-Datei landet im Repo:** der Drive-Download legte
  `WinRinglight/google_drive/*.zip` an. Wieder gelöscht. Download-Ziel künftig
  `/home/user/drive-*`, nie in den Repo-Baum.
* **`record struct` / C#-Features:** Projekt nutzt `ImplicitUsings` +
  `Nullable`; `net10.0` kann C# 13. Unkritisch.
* **Nicht lokal kompilierbar** – also keine API erfinden. Bei Unsicherheit
  lieber die vorhandene API benutzen (z. B. `Rect` statt neuer Typen).

## 5. Regeln für diese Arbeit

1. Optik darf sich **nicht** ändern. Nur die Arbeit dahinter.
2. Jede Verhaltensänderung bekommt einen Schalter in `Config` (JSON), damit der
   Nutzer sie ohne neuen Build abschalten kann.
3. Immer Fallback: wenn eine neue Rechnung Unsinn ergibt (NaN, Fläche ≈ 0),
   auf das alte Verhalten zurückfallen – nie ein unsichtbares Ringlicht.
4. Nach jeder Phase: Zahlen in `docs/agent/MESSUNGEN.md` eintragen.
5. Kein Feature anfassen, das der Nutzer nicht genannt hat (Tray, Hotkey,
   Webcam, MSIX).

## 6. Offene Fragen an den Nutzer (erst stellen, wenn nötig)

* Wie viele Monitore, welche Auflösung, welche Skalierung?
  (Ohne das sind Performance-Zahlen nicht vergleichbar.)
* Wird die Streamer-Funktion „nicht aufnehmen" wirklich gebraucht?
  (Entscheidet, ob der Composition-Umbau überhaupt in Frage kommt.)
