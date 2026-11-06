# Datejust 36 — skeuomorphe Web-App

Eine vollständig funktionierende, rein vektorielle Nachbildung der Datejust aus
dem Referenzfoto: smaragdgrünes Zifferblatt mit Sonnenschliff, geriffelte
Weißgold-Lünette, Jubilé-Armband, Datumsfenster mit Lupe (Cyclops) bei 3 Uhr.

Alles wird zur Laufzeit als SVG erzeugt — kein einziges Bild der Uhr, keine
Netzwerkzugriffe, keine externen Bibliotheken.

## Starten

`web/` ist eine statische Seite ohne Build-Schritt:

```bash
python3 -m http.server 8080 --directory web
# http://localhost:8080/index.html
```

Ein Doppelklick auf `index.html` funktioniert ebenfalls (`file://`), weil alle
Assets relativ eingebunden sind.

## Bedienung

| Element | Wirkung |
| --- | --- |
| **Krone ziehen** (Knopf „Krone“ oder Krone an der Uhr) | Sekundenzeiger friert ein, Zustand „Zeiger stellen“ |
| **Mausrad / Ziehen am Gehäuse** (bei gezogener Krone) | Stunden und Minuten stellen, Datum läuft über Mitternacht mit |
| **Krone drücken** | Uhr läuft mit dem korrigierten Offset weiter |
| **Datum − / +** | Datumsscheibe dreht sich animiert, auch unter der Lupe |
| **Heute** | springt auf das Systemdatum, **Auf Systemzeit stellen** setzt die Zeiger |
| **Licht** (Drehknopf) | Lichtstärke 20–100 %, steuert Glanz, Lampenschein und Reflexe |
| **Nacht** | Super-LumiNova leuchtet, Metall tritt zurück, Zifferblatt dunkelt ab |
| **Werk** | Tickgeräusch (WebAudio, synthetisiert) |
| **Maus über der Uhr** | die Uhr neigt sich leicht, der Glanzpunkt wandert |

Tastatur: `Tab` erreicht alle Bedienelemente, die Knöpfe haben `aria-pressed` /
`aria-checked`, der Lichtknopf ist ein `role="slider"`.

## Aufbau

```
index.html      Skelett: Bühne + Bedienpanel
css/style.css   Papier-/Metall-Optik, Lampenlicht, Nachtmodus, responsive Raster
js/watch.js     erzeugt das komplette SVG: Band, Gehäuse, Lünette, Zifferblatt,
                Aufdruck, Datumsfenster, Lupe, Zeiger, Krone
js/app.js       Zeitmodell, Interaktion, Datumsanimation, Licht/Nacht/Ton
assets/         Schriften (OFL), Körnung, Bürstentextur
preview/        gerenderte Ansichten (siehe unten)
```

### Wie die Realistik entsteht

* **Lünette** — 60 Flanken, jede aus zwei Facetten; eine radiale Gradient-Sonde
  bildet die Wölbung ab, eine Haarlinie markiert den Grat.
* **Jubilé-Band** — pro Reihe ein durchgehendes satiniertes Außenglied mit
  polierter Randfase, dazwischen drei Reihen flacher polierter „Ziegel“ mit
  dunklen Fugen; darüber eine Zylinderabschattung und ein AO-Verlauf zum Gehäuse.
* **Zifferblatt** — 360 Strahlen für den Sonnenschliff, grüner Verlauf,
  Flansch mit geprägtem „ROLEX“-Aufdruck, weißgoldene Indizes mit Lume-Einlage.
* **Lupe** — eigener Clip mit 1,75× Vergrößerung des Zifferblatts *und* eine
  zweite, mitvergrößerte Datumskarte; darunter wird das echte Fenster per Maske
  ausgeblendet. Zeiger erscheinen unter der Lupe ebenfalls vergrößert.
* **Zeiger** — Weißgold-Rahmen mit vertieftem Lume-Kanal, harte Kontur,
  Spekular-Kante.

## Vorschauen

`preview/` enthält die Bilder, die aus dem echten SVG der App gerendert wurden:

| Datei | Inhalt |
| --- | --- |
| `00-vergleich-vorlage.jpg` | Referenzfoto neben der App |
| `01-datejust.jpg` | die ganze Uhr |
| `02-zifferblatt.jpg` | Zifferblatt und Lünette |
| `03-armband.jpg` | Jubilé-Band |
| `04-datum-cyclops.jpg` | Datumsfenster mit Lupe |
| `05-nachtmodus.jpg` | Nachtmodus |

## Hinweis

Inoffizielles Fan-Projekt zu Studienzwecken — keine Verbindung zur Rolex SA.
„Rolex“, „Oyster Perpetual“, „Datejust“ und die Krone sind Marken der Rolex SA.
