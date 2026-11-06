# Empfehlung in einfacher Sprache

Diese Datei erklärt in einfachen Worten, was ich empfehle.
Die genaue technische Version steht in `performance-analyse.md`.

---

## Das Problem in einfachen Worten

Dein Ringlicht-Fenster ist **immer so groß wie alle Bildschirme zusammen**.
Auch dann, wenn nur ein Bildschirm leuchten soll.

In diesem Riesenfenster steckt der Ring. Der Ring selbst ist klein – nur ein
Streifen am Rand. Aber wenn du die Maus bewegst, muss der Computer **das ganze
Riesenfenster neu durchrechnen**. Nicht nur den Streifen. Alles.

Das ist so, als würdest du jedes Mal, wenn du eine Lampe verrückst, das ganze
Zimmer neu tapezieren.

Dazu kommt: Der Computer rechnet **die ganze Zeit weiter**, auch wenn die Maus
still steht. Und er fragt 20-mal in der Sekunde bei Windows nach, ob gerade ein
Spiel im Vollbild läuft. Das kostet extra Kraft.

Deshalb wird der Lüfter laut und der Akku leer.

---

## Was ich empfehle – in drei Schritten

### Schritt 1: Aufräumen, ohne etwas Neues zu bauen

Das ist der wichtigste Schritt. Er bringt den größten Teil der Verbesserung.

1. **Das Fenster nur so groß machen, wie es sein muss.**
   Wenn nur ein Bildschirm leuchten soll, ist das Fenster auch nur so groß.
   Statt „alle Bildschirme" → nur der Bildschirm, der leuchtet.

2. **Den „Loch-Ausschnitt" für die Maus nur auf den Ring legen, nicht auf
   das ganze Fenster.**
   Dann muss beim Bewegen der Maus nur der Ring neu gerechnet werden,
   nicht das ganze Fenster.

3. **Die weiche Kante ohne den teuren Weichzeichner machen.**
   Der Weichzeichner (Blur) ist der teuerste Teil. Für einen geraden Streifen
   brauchst du ihn nicht. Ein Verlauf sieht genauso aus und kostet fast nichts.

4. **Nur rechnen, wenn sich wirklich etwas bewegt.**
   Steht die Maus still, soll die App auch still sein. Kein Dauerlauf.

5. **Nicht 20-mal pro Sekunde fragen, ob ein Spiel läuft.**
   Windows kann der App Bescheid sagen, wenn sich das Fenster vorne ändert.
   Dann muss die App nicht dauernd selbst nachschauen.

6. **Die Farbtemperatur nicht alle 2 Sekunden neu setzen.**
   Nur dann, wenn sie sich wirklich sichtbar geändert hat.

Diese sechs Punkte ändern **nichts an der Optik**. Der Ring sieht hinterher
genau gleich aus. Nur die Arbeit dahinter wird viel kleiner.

Aufwand: etwa 1 bis 2 Tage. Risiko: sehr klein.
Weil wir nur aufräumen, funktioniert alles andere weiter wie bisher:
Task-Symbol, Tastenkürzel, Webcam-Erkennung, mehrere Bildschirme,
„nicht aufnehmen" für Streamer.

### Schritt 2: Erst messen, dann entscheiden

Bevor wir irgendetwas Großes umbauen, schauen wir nach, ob Schritt 1 gereicht hat.

Dafür reichen zwei Zahlen aus dem Task-Manager:

- Wie viel Prozent Prozessor braucht die App, wenn die Maus **still steht**?
- Wie viel Prozent, wenn du die Maus **im Kreis bewegst**?

Ziel: unter 1 % im Stillstand, unter 5 % bei Bewegung.

Wenn diese Zahlen gut sind, ist die Arbeit fertig. Dann brauchen wir Schritt 3
nicht.

### Schritt 3: Der große Umbau – nur wenn Schritt 1 und 2 nicht reichen

Hier würde die App nicht mehr mit dem normalen Windows-Zeichnen arbeiten,
sondern direkt mit der Grafikkarte. Das ist die Idee aus deiner Datei in Google
Drive, und die Richtung ist richtig.

Dann würde die App im Leerlauf fast gar keine Prozessor-Leistung mehr brauchen,
weil die Grafikkarte die Arbeit macht.

Aber: Das ist viel mehr Arbeit (etwa 3 bis 5 Tage) und hat ein echtes Risiko.
Der Grund steht im nächsten Abschnitt.

---

## Das eine Risiko, das wir vorher testen müssen

Die App hat eine Funktion, die für Streamer sehr wichtig ist:
**Der Ring soll in der Bildschirmaufnahme nicht zu sehen sein.**
Sie ist standardmäßig eingeschaltet.

Beim großen Umbau könnte genau diese Funktion kaputtgehen. Es gibt Berichte,
dass Windows sie bei manchen Fensterarten ablehnt.

Deshalb gilt: **Wenn wir Schritt 3 machen, testen wir zuerst nur das.**
Ein kleines Testprogramm, 30 Zeilen, eine halbe Stunde Arbeit:

1. Leeres Fenster in der neuen Technik bauen.
2. Die Funktion „nicht aufnehmen" einschalten.
3. Mit OBS aufnehmen und nachschauen, ob der Ring fehlt.

Wenn das **nicht** funktioniert, machen wir Schritt 3 nicht.
Dann ist Schritt 1 die Lösung. Punkt.

---

## Was ich ausdrücklich **nicht** empfehle

**Die Web-Version aus der Zip-Datei nicht als Windows-App verpacken.**
Also nicht den Weg mit Electron.

Warum:

- Ein Browser im Hintergrund braucht etwa **100 MB Arbeitsspeicher**.
  Das ist mehr, als deine App heute insgesamt braucht.
- Deine App müsste dann zwei Programme enthalten, die beide gepflegt werden müssen.
- Task-Symbol, Tastenkürzel, Webcam-Erkennung und „nicht aufnehmen"
  müssten alle neu gebaut werden.

Die Web-Seite ist eine schöne Idee zum Ausprobieren. Aber sie ist nicht
die Lösung für deine Windows-App.

**Auch nicht empfehle ich, den Beispielcode aus der Anleitung einfach
abzuschreiben.** Er sieht fertig aus, ist es aber nicht. Er würde beim
ersten Start abstürzen. Die genauen Fehler stehen in `performance-analyse.md`,
Abschnitt 4.

---

## Zusammenfassung in drei Sätzen

1. Deine Idee ist richtig: Der Ring soll einmal gezeichnet werden und dann nur
   noch verschoben werden. Genau das macht die App heute nicht.
2. Der größte Teil des Problems lässt sich aber schon mit einfachem Aufräumen
   lösen – ohne neue Technik, ohne Risiko, und ohne dass sich die Optik ändert.
3. Den großen Umbau machen wir nur, wenn das Aufräumen nicht reicht. Und vorher
   testen wir eine halbe Stunde lang, ob die Streamer-Funktion dabei kaputtgeht.

---

## Fachwörter – kurz erklärt

| Fachwort | Was es bedeutet |
|---|---|
| Layered Window | Ein Fenster, das Windows Stück für Stück durchsichtig zeichnen kann. Nötig für ein Overlay, aber teurer als ein normales Fenster. |
| Opacity Mask / Maske | Eine Art Schablone. Sie sagt: Hier darf man durchschauen, hier nicht. Damit entsteht das Loch um die Maus. |
| Dirty Rect | Der Bereich, den Windows neu zeichnen muss. Je größer, desto mehr Arbeit. |
| Blur / Weichzeichner | Die weiche Kante. Sieht schön aus, ist aber der teuerste Teil der Zeichnung. |
| Compositor | Die Grafikkarte zeichnet selbst. Die App sagt nur noch, *was* wo liegen soll. |
| DIP | Die Maßeinheit, mit der Windows Oberflächen rechnet. Bei 150 % Bildschirmskalierung ist 1 DIP = 1,5 echte Pixel. |
| WDA_EXCLUDEFROMCAPTURE | Der Windows-Befehl für „dieses Fenster nicht aufnehmen". |
