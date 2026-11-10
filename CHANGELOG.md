# Changelog

**[English](#english) · [Magyar](#magyar)**

Format: [Keep a Changelog](https://keepachangelog.com/). Versions follow the plugin assembly version.

---

## English

### [0.3.0] – 2026-11-10
#### Changed
- New look, in line with StreamJelly: Jellyfin blue, quiet colours, large posters and backdrops, chips and pill labels.
- Every running job is a card like an expanded StreamJelly session: the item it works on in the header (backdrop, poster, clickable title, operation, elapsed time), the job's progress below, then the other items it works on in parallel as small cards. A library scan also lists the libraries being scanned.
- Library cards: library image in the header with the indexed percentage, one row per check with a *complete* or *N missing* label (click it for the list), and the item counts at the bottom.
- The missing lists show small posters and the season / episode under the title.
- Library tasks and *Recently processed* are folded into one panel; task results are coloured labels.
- On a phone the check rows are compact, and file paths are left out of the missing lists.

### [0.2.1] – 2026-11-07
#### Changed
- The page refreshes every 2 seconds while something is being indexed (setting: *while indexing*) and at the normal interval otherwise. Elapsed times and the "counted … ago" line tick every second in the browser.
- The library counts are recounted right after an indexing task or library scan finishes, and at most every minute while one runs (longer only if counting itself is slow), instead of every 5 minutes.
- *Other activity* is now called *Outside scheduled tasks*, with a short explanation (manual refresh, real-time monitoring of new files, other ffmpeg work).
- The library cards are only rebuilt when they change, so they no longer flicker on every refresh.

### [0.2.0] – 2026-11-07
#### Added
- Job details, Plex style: under every running task, the items it is working on right now, with a thumbnail, a clickable title that opens the item's page, what is being done (reading media info, generating trickplay images, extracting an image, extracting keyframes, measuring loudness…) and the elapsed time. Folders being scanned (e.g. a series) show their own progress.
- *Other activity* (*Outside scheduled tasks* since 0.2.1): work that does not belong to a running task, e.g. a manual metadata refresh.
- *Recently processed* feed: the last items added, refreshed, updated or removed, with links.
- API: `activity` in `GET /IndexJelly/Status` (`refreshing`, `processes`, `recent`).

#### Notes
- The current items come from Jellyfin's refresh and library events (attached at server start, so restart once after installing) and from the running ffmpeg / ffprobe processes, matched to library items by file path. Reading the processes works on Linux (Docker) only. Playback transcodes are left out.

### [0.1.0] – 2026-11-07
#### Added
- First version for Jellyfin 12.4 (.NET 10).
- Admin dashboard page *IndexJelly*: running library scans and indexing tasks with live progress; per library the share of items with metadata refresh, media info, primary image, overview, provider ids, trickplay and chapter images.
- Missing lists per library and check, with links to the items.
- Buttons to recount, scan one library, scan all libraries and run library tasks.
- Background counting with a configurable cache age; trickplay and chapter data read through runtime lookups, with graceful fallback.
- `GET /IndexJelly/Status` and `GET /IndexJelly/Missing` JSON endpoints (admin only).
- English and Hungarian interface, following the Jellyfin display language.
- Build and release scripts, plugin repository manifest.

---

## Magyar

### [0.3.0] – 2026-11-10
#### Változott
- Új megjelenés a StreamJelly stílusában: Jellyfin-kék, visszafogott színek, nagy poszterek és háttérképek, chipek és pill-címkék.
- Minden futó feladat egy kártya, mint egy kinyitott StreamJelly-munkamenet. Fent az éppen feldolgozott elem (háttérkép, poszter, kattintható cím, művelet, eltelt idő), alatta a feladat haladása, lent kis kártyákon a párhuzamosan feldolgozott elemek. A könyvtárszkennelés a szkennelt könyvtárakat is felsorolja.
- Könyvtár-kártyák: fent a könyvtár képe és az indexeltség aránya, ellenőrzésenként egy sor *kész* vagy *N hiányzik* címkével (kattintásra lenyílik a lista), lent az elemszámok.
- A hiánylistákban kis poszter látszik, a cím alatt az évad és a rész.
- A könyvtári feladatok és a *Nemrég feldolgozva* lista egy összecsukható panelbe került, a feladatok eredménye színes címke.
- Telefonon tömörebbek az ellenőrzések sorai, és a hiánylistákból kimarad a fájlútvonal.

### [0.2.1] – 2026-11-07
#### Változott
- Indexelés közben az oldal 2 másodpercenként frissül (beállítás: *indexelés közben*), egyébként a megadott gyakorisággal. Az eltelt idők és a „számolva … ezelőtt” sor a böngészőben másodpercenként frissül.
- A könyvtári számok egy indexelési feladat vagy szkennelés befejezése után azonnal újraszámolódnak, futás közben pedig legfeljebb percenként (hosszabb időközzel csak akkor, ha maga a számolás lassú), az eddigi 5 perc helyett.
- Az *Egyéb tevékenység* új neve *Feladaton kívüli munka*, rövid magyarázattal (kézi frissítés, új fájlok valós idejű figyelése, egyéb ffmpeg munka).
- A könyvtár-kártyák csak akkor épülnek újra, ha változtak, így nem villognak minden frissítéskor.

### [0.2.0] – 2026-11-07
#### Új
- Feladat-részletek a Plexhez hasonlóan: minden futó feladat alatt látszik, mely elemeken dolgozik éppen. Mindegyiknél ott a bélyegkép, a kattintható cím (az elem adatlapjára visz), a végzett művelet (mediainfó olvasása, trickplay képek készítése, kép kinyerése, keyframe-ek kinyerése, hangerő mérése…) és az eltelt idő. A szkennelt mappák (pl. egy sorozat) saját haladást is mutatnak.
- *Egyéb tevékenység*: olyan munka, amely nem tartozik futó feladathoz, például egy kézi metaadat-frissítés.
- *Nemrég feldolgozva* lista: az utoljára hozzáadott, frissített, módosított vagy törölt elemek, linkkel.
- API: `activity` a `GET /IndexJelly/Status` válaszban (`refreshing`, `processes`, `recent`).

#### Megjegyzés
- Az éppen feldolgozott elemeket a Jellyfin frissítési és könyvtári eseményeiből, valamint a futó ffmpeg / ffprobe folyamatokból olvassa ki, utóbbiakat fájlútvonal alapján párosítja a könyvtár elemeivel. Az eseményekre a szerver indulásakor iratkozik fel, ezért telepítés után egyszer újra kell indítani a szervert. A folyamatok olvasása csak Linuxon (Dockerben) működik. A lejátszáshoz tartozó átkódolások nem jelennek meg.

### [0.1.0] – 2026-11-07
#### Új
- Első verzió Jellyfin 12.4-hez (.NET 10).
- *IndexJelly* vezérlőpult oldal: futó könyvtárszkennelések és indexelési feladatok élő haladással; könyvtáranként a metaadat-frissítéssel, mediainfóval, elsődleges képpel, leírással, provider azonosítóval, trickplay-jel és fejezetképekkel rendelkező elemek aránya.
- Hiánylisták könyvtáranként és ellenőrzésenként, linkkel az elemekre.
- Gombok: újraszámolás, egy könyvtár szkennelése, összes könyvtár szkennelése, könyvtári feladatok futtatása.
- Háttérben futó számolás állítható gyorsítótár-idővel; a trickplay- és fejezetadatok futásidejű kereséssel, hiba esetén nélkülük is működik.
- `GET /IndexJelly/Status` és `GET /IndexJelly/Missing` JSON végpont (csak rendszergazdának).
- Angol és magyar felület, a Jellyfin megjelenítési nyelve szerint.
- Fordító és kiadó szkriptek, plugin-adattár manifest.
