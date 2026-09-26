# Synthwave Pack — przykładowa paczka modów Vizstrapa

Pokazuje wszystko, co paczka `.vzmod` może zawierać. Można ją zainstalować tak, jak jest, albo skopiować
i przerobić na własną.

| Folder | Co robi |
| --- | --- |
| `files\` | Neonowy kursor myszy (`content\textures\Cursors\KeyboardMouse\ArrowCursor.png` i `ArrowFarCursor.png`, 64×64, czubek strzałki na środku obrazka). Pliki mają ten sam układ co folder Modifications. |
| `effects\` | Trzy shadery HLSL z suwakami: **Synthwave** (fioletowe cienie, błękitne światła, różowa poświata), **Toon outlines** (kreskówkowe kontury z jasności i z głębi AI), **Retro TV** (zakrzywiony ekran, rozszczepienie kolorów, linie). |
| `loading-themes\Synthwave\` | Motyw okna ładowania w formacie Bloxstrapa: słońce nad neonową siatką. |
| `plugin\plugin.ps1` | Wtyczka w PowerShellu: po wejściu do gry powiadomienie „Grasz w: …” z nazwą gry (z publicznego API Robloxa), po wyjściu „Grałeś X min w …”. |

## Instalacja

1. W Vizstrapie otwórz **Mody → Paczki modów → Zainstaluj paczkę → Plik** i wybierz `Synthwave Pack.vzmod`.
   Możesz też wybrać **Folder** i wskazać ten folder.
2. Zapisz ustawienia. Vizstrap zapyta, czy uruchamiać program z paczki (wtyczkę).
3. Shadery włączasz w zakładce **Shadery → Z paczek modów**. Motyw ładowania wybierasz w **Wygląd → Styl → Motyw XML**.

## Przerabianie

- **Kolory shadera:** zmień `violet` i `cyan` w `effects\synthwave.hlsl`. Kolory są w świetle liniowym (0…1).
- **Suwaki:** opisuje je plik `.json` obok shadera (najwyżej 8), a w HLSL czyta się je jako `Param0`…`Param7`.
- **Kursor:** podmień PNG. Czubek strzałki musi być na środku obrazka 64×64.
- **Wtyczka:** czyta zdarzenia (`gameJoined`, `gameLeft`, `stopping`) po jednym JSON-ie na linię ze stdin,
  a polecenia (`notify`, `setLook`, `log`) wypisuje na stdout. Może być w dowolnym języku. Wtedy ustaw
  `run` i `args` w `vizmod.json`.
- **Pakowanie:** **Mody → Zrób własną → Spakuj folder** robi z folderu plik `.vzmod` do wysłania innym.
