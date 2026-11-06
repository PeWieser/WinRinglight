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

## 3b. Was inzwischen gebaut ist (Stand: nach Phase 1)

* **CI steht** (`.github/workflows/ci.yml`): baut App + Tests auf
  `windows-latest`, schreibt einen PR-Kommentar mit Build-Status, Testzahlen,
  Warnungen und einer Vorher/Nachher-Tabelle der Fensterfläche.
  Kommentar lesen: `gh api repos/PeWieser/WinRinglight/issues/1/comments --jq '.[-1].body'`
* **PR-Ziel ist #1** (Branch `arena/c0ca9d72-winringlight` → `master`).
* `WinRinglight/ScreenLayout.cs`: reine Rechenklasse, ohne UI-Abhängigkeit.
  - `Compute(monitors, selected, spanned, fullscreen, scale) -> WindowLayout`
  - `ToWindowDip(cursorPx, windowPx, scale) -> Point`
  - Wichtig: Der Glow-Rand wird auf die **echte Bildschirmfläche** geklemmt.
* `WinRinglight.Tests`: 22 Tests (xUnit), decken Randfälle ab:
  leere Auswahl, ungültiger Index, kaputte Skalierung (0/NaN/-1), negativer
  Monitor, Taskleiste, Vollbild, DPI, harte Nicht-Verschlechtern-Garantie.
* `tools/LayoutReport`: kleines Konsolenprogramm, das die Vorher/Nachher-Tabelle
  erzeugt – Ausgabe landet im PR-Kommentar. **Das ist das Messinstrument.**
* `MainWindow.xaml.cs`: `UpdateMonitorSetup` nutzt jetzt `ScreenLayout`,
  setzt das Fenster per `SetWindowPos` in **physischen Pixeln**, und rechnet
  Cursor-Koordinaten in DIP um. Schalter: `Config.ShrinkWindowToUsedArea`.

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
* **`Point` ist mehrdeutig**: das Projekt nutzt WPF *und* WinForms, also gibt es
  `System.Drawing.Point` und `System.Windows.Point`. Immer voll qualifizieren,
  wenn die Datei auch von WinForms-Seite erreichbar ist.
* **GitHub-Ausdrücke sind nicht PowerShell:** `-eq` im `if:` einer Workflow-Datei
  ist ein Syntaxfehler, der die **ganze Datei** ungültig macht („workflow file
  issue"). Richtig ist `==`. → Vor jedem Push YAML prüfen.
* **YAML vor dem Push validieren:** `python3 -c "import yaml;yaml.safe_load(...)"`.
  Zwei Läufe wurden dadurch verbrannt. `pyyaml` ist installiert
  (`pip install --break-system-packages pyyaml`).
* **CI-Artefakte sind aus der Sandbox nicht ladbar** (Blob-Storage blockiert).
  Diagnose deshalb immer direkt in den PR-Kommentar schreiben, nicht als
  Artifact.
* **`Console.WriteLine` aus xUnit landet nicht im Log.** Für sichtbare Ausgabe
  ein eigenes Konsolenprogramm (`tools/LayoutReport`) verwenden.
* **`dotnet test` formatiert Zahlen anders** als erwartet
  (`Failed: 0, Passed: 22`). Nicht raten – TRX-Datei parsen.
* **Der Glow-Rand über den Bildschirmrand hinaus kostete mehr, als er brachte**
  (Faktor 0,91×, also schlechter). Rand immer auf die Bildschirmfläche klemmen.
* **Kein `sed -i` mit Sonderzeichen in YAML/Code** – stattdessen ein
  Python-Skript schreiben und mit `assert` prüfen, ob das Muster gefunden wurde.
  (Ein `sed`-Befehl hat hier still nichts geändert.)

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
