# Messungen

Jede Zeile ist ein CI-Lauf. Quelle: PR-Kommentar oder `tools/LayoutReport`.
Zielgerät-Zahlen (CPU) trägt der Nutzer ein – die kann keine CI liefern.

## Fensterfläche (von der CI gemessen, Commit `fe7b486`)

| Setup | vorher | nachher | Faktor |
|---|---|---|---|
| 1 Monitor 1080p @100 % | 1920 × 1080 | 1920 × 1080 | 1,00× |
| 1 von 2 Monitoren (1080p) | 3840 × 1080 | 1968 × 1080 | **1,95×** |
| 1 von 3 Monitoren (1080p) | 5760 × 1080 | 2016 × 1080 | **2,86×** |
| 1 von 2 Monitoren (4K @150 %) | 7680 × 2160 | 3912 × 2160 | **1,96×** |
| 2 Monitore gespannt (4K @150 %) | 7680 × 2160 | 7680 × 2160 | 1,00× |

**Lesart:** Weniger Fensterfläche = weniger Pixel, die bei jeder Mausbewegung neu
zusammengesetzt werden. Faktor 2 heißt: halb so viel Arbeit an dieser Stelle.

Der letzte Fall (gespannt) ist erwartungsgemäß unverändert – dort *ist* die
Nutzfläche der ganze Desktop. Dafür kommt Phase 2.

## Warum 1,00× und nicht kleiner

Ein einzelner Monitor lässt sich nicht unter seine eigene Fläche drücken. Der
Gewinn kommt erst, wenn mehr Bildschirme da sind als genutzt werden. Das ist
der Normalfall (Laptop + externer Monitor).

Erster Versuch hatte hier **0,91×** – also *schlechter*. Ursache: der Glow-Rand
wurde über den Bildschirmrand hinaus addiert. Behoben durch Clamp auf die echte
Bildschirmfläche (`Rect.Intersect`), plus Test `FensterIstNieGroesserAlsVorher`.

## Phase 2 – Neuzeichnungen und Timer-Ticks (aus Tests, Commit `424cb9e`)

| Größe | vorher | nachher | Faktor |
|---|---|---|---|
| Neuzeichnungen bei 60 Eingaben/s | 60 | **20** | −67 % |
| Neuzeichnungen bei 30 Eingaben/s | 30 | **30** | 0 % (nichts verloren) |
| Timer-Ticks im Leerlauf (Ring aus) | 20/s | **1/s** | −95 % |
| Z-Order-Walks pro Sekunde | 20 | **1** | −95 % |

Die ersten beiden Zeilen sind aus `CursorThrottleTests`, die letzten beiden aus
`IdlePolicyTests`. Beides sind Rechenwerte, keine Messungen am Gerät – aber sie
sind exakt, weil die Logik deterministisch ist.

## Verworfener Plan

Phase 2 sollte ursprünglich die Loch-Maske auf die Ringe verschieben.
**Nachgerechnet: 0 % Gewinn**, weil `OpacityMask` auf die Bounds wirkt und die
Bounds einer rahmenfüllenden `Border` der ganze Monitor sind.
Siehe `PLAN.md`, Phase 2 (verworfen).

## CPU am Zielgerät

| Zustand | vorher | nachher | Datum | Gerät |
|---|---|---|---|---|
| Stillstand | _offen_ | _offen_ | | |
| Maus im Kreis | _offen_ | _offen_ | | |

Messung: Task-Manager → Details → `WinRinglight.exe`, 60 s beobachten.

## CI-Läufe

| Lauf | Commit | Ergebnis | Tests |
|---|---|---|---|
| 42236217482 | 62478f8 | ❌ Build (Point mehrdeutig) | – |
| 42236559766 | ca68da9 | ❌ Tests | 2 rot |
| 42236962788 | 9c98523 | ❌ Tests | 2 rot |
| 42237396783 | 490d23c | ✅ | alle grün |
| 42238001924 | 5395f79 | ❌ Workflow-Datei (`-eq` statt `==`) | – |
| 42238104184 | e6dc1e7 | ✅ | 15 grün, Report zeigt 0,91× |
| 42238446769 | fe7b486 | ✅ | **22 grün, 1,95–2,86×** |
| 42240812794 | 0750769 | ❌ | 32 grün, 3 rot (Throttle-Logik) |
| 42241094037 | 7aae4a4 | ✅ | 35 grün |
| 42241553898 | bd38211 | ❌ Build | CS0133 `const` aus Methodenaufruf |
| 42241780193 | cfd6c66 | ❌ | 43 grün, 1 rot (`ShouldWalk(0,0)`) |
| 42242012846 | 424cb9e | ✅ | **45 grün** |
