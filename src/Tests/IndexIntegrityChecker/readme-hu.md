# IndexIntegrityChecker

[English](readme.md) | Magyar | [Fejlesztői dokumentáció](readme-hu-for-dev.md)

Kísérleti parancssori eszköz sensenet **SQL Server-adatbázis és Lucene 2.9-index** ellenőrzésére, majd az index kézi megerősítéssel történő javítására. A sensenet repository indítása nélkül működik. Az SQL-t csak olvassa; a javítás a kiválasztott indexet helyben módosítja.

## Indítás előtt

- Telepítsd a .NET 8 futtatókörnyezetet, és tartsd együtt a teljes `publish` könyvtárat. A tartós futáshoz (`-w`) Windows szükséges. A fordítás leírása a fejlesztői dokumentációban található.
- Biztosíts SQL- és indexolvasási jogosultságot, valamint írható ideiglenes/munkakönyvtárat, elegendő hellyel az adatbázis- és indexpillanatképeknek, riportoknak. Javításhoz indexírási jogosultság is kell.
- Készíts indexmentést. Az ellenőrzés kezdetétől a javítás végéig más folyamat ne módosítsa az SQL-t és a kiválasztott indexet, a futások közötti szünetben sem. Állítsd le az ezeket író folyamatokat; az eszköz nem készít indexmentést.
- A javítás előkészítése és megerősítése alatt is legyen elérhető mindkét forrás. Az SQL-olvasás zárakat tarthat fenn, és várakoztathatja az írókat.

## Ellenőrzés indítása

Az eszköz könyvtárából így indíthatsz minden eredményt megőrző, folytatható futást:

```powershell
.\publish\IndexIntegrityChecker.exe -d "Server=localhost;Database=sensenet;Integrated Security=true;Encrypt=true" -i "D:\Indexes\LocalIndex" -w "D:\Checks\check-001"
```

Cseréld ki a kapcsolati beállításokat és az útvonalakat. Minden új ellenőrzéshez új munkakönyvtár kell; ha nem létezik, létrejön. Önaláírt tanúsítványú helyi fejlesztői SQL Serverhez szükség esetén add hozzá a `TrustServerCertificate=true` beállítást.

| Paraméter | Aliasok | Jelentés |
| --- | --- | --- |
| `--connection-string` | `--database`, `--db`, `-d` | SQL-kapcsolati karakterlánc; új ellenőrzéshez kötelező. |
| `--index` | `-i` | Indexkönyvtár; új ellenőrzéshez kötelező. |
| `--working-directory` | `--workdir`, `-w` | Eredmények megőrzése és folytathatóság a javítás indítása előtt. |
| `--max-console-differences` | — | Kiírási limit `-w` nélkül: 1–2147483647, alapértéke 20. `-w` mellett nem használható. |

A kapcsolók sorrendje tetszőleges, nevükben a kis- és nagybetűk nem számítanak; az értékek betűmérete változatlan marad. Az értékeket tedd idézőjelbe, minden kapcsolót csak egyszer adj meg, az aliasokat és az eltérő betűméretű alakokat is beleértve. A `--option "value"` és a `--option="value"` alak is működik. A relatív útvonalak az indítás könyvtárához képest értendők.

Az indexútvonal közvetlenül a `segments_*` fájlokat tartalmazó könyvtárra vagy annak szülőjére mutathat. Szülőkönyvtár esetén a név szerint utolsó, érvényes indexet tartalmazó közvetlen alkönyvtárat választja. Konkrét mentéshez a pontos indexkönyvtárat add meg.

**`-w` nélkül** a futás ideiglenes fájlokat használ, kiírja az első 20 eltérést és a teljes összesítést, majd törli a fájljait. Nem folytatható. A kiírási limitet például a `--max-console-differences 50` kapcsolóval módosíthatod. A stdout átirányítása nem szünteti meg a limitet; minden eltérés megőrzéséhez használj `-w` kapcsolót.

## Javítás megerősítése vagy elutasítása

Az összehasonlítás után a `Preparing index repair...` sor, majd a javítás előnézete jelenik meg: törlendő dokumentumok, beírandó verziók és a végső activityazonosító. Ha van teendő, az eszköz ezt kérdezi:

```text
Perform the repair? [yes/NO]
```

A **`yes` vagy `y`** válasz indítja a javítást, kis- és nagybetűtől, illetve környező szóközöktől függetlenül. Minden más válasz, az üres Enter, a bemenet vége vagy a nem interaktív/átirányított stdin elutasítás. Ha csak ellenőrizni szeretnél, utasítsd el a javítást.

A javítás a teljes comparison riport alapján helyreállítja az érintett verziókat az SQL indexelési adataiból, és eltávolítja a felesleges dokumentumokat. Az index utolsó activityazonosítóját az adatbázis értékére állítja, a gaplistát kiüríti. Kizárólag az activity-metaadatok miatt akkor is szükséges lehet javítás, ha nincs dokumentumeltérés.

`-w` mellett az elutasítás után az előkészített munkafolyamat később folytatható. Sikeres javítás után **új munkakönyvtárban új ellenőrzéssel** igazold az eredményt.

## Folytatás és megszakítás

A folytatáshoz csak ugyanazt a munkakönyvtárat add meg:

```powershell
.\publish\IndexIntegrityChecker.exe -w "D:\Checks\check-001"
```

- A javítás indítása előtt a befejezett fázisok eredménye megmarad; a félbeszakadt fázis újraindul. Az előkészített javítást újra ellenőrzi, és ismét megerősítést kér. Az SQL és az index a szünet alatt sem változhatott.
- **`RepairStarted` után a javítás nem folytatható.** Megszakítás vagy hiba esetén új munkakönyvtárban új teljes ellenőrzés kell, akkor is, ha az index már részben vagy teljesen módosulhatott.
- Befejezett munkafolyamat újranyitása a mentett eredményt mutatja; nem végez új ellenőrzést és nem ismétli meg a javítást. Ha az üzenet szerint csak a befejezett eredmény megjelenítése hibás, a befejezés érvényes marad; állítsd helyre a források elérését, majd nyisd meg újra.

A teljes munkakönyvtárat őrizd meg változatlanul: a mentett fájlokat ellenőrzi, hiányuk vagy módosulásuk hibát okoz. Folytatáskor a források eléréséhez ugyanaz a Windows-felhasználó és gép szükséges, mert a mentett kapcsolati karakterláncot DPAPI titkosítja. A forrásútvonalak abszolútak maradnak. Egy munkakönyvtárat egyszerre csak egy folyamat használhat.

## Az eredmény értelmezése

A `COMPARISON SUMMARY` megmutatja a verzió- és dokumentumszámokat, az összes eltérés számát (`differences`) és az érintett dokumentumok számát (`differentDocuments`). Egy dokumentum több hibája több eltérésnek, de egy érintett dokumentumnak számít; minden indexből hiányzó SQL-verzió is egy érintett dokumentum. A `COUNTS BY KIND` csak eltérés esetén jelenik meg.

| Eltérés | Jelentés |
| --- | --- |
| `NotInIndex` / `NotInDatabase` | Hiányzó indexdokumentum / az indexelhető SQL-verziók között nem szereplő dokumentum. |
| `MoreDocument` | További dokumentum ugyanazzal a verzióazonosítóval. |
| `DifferentNodeId` | Hibás node-azonosító. |
| `DifferentNodeTimestamp` / `DifferentVersionTimestamp` | Hibás node-/verzió-időbélyeg. |
| `DifferentLastPublicFlag` / `DifferentLastDraftFlag` | Hibás legutolsó publikus/piszkozat verziójelző. |
| `MissingField` / `InvalidField` / `MultipleFieldValues` | Kötelező mező hiányzik, érvénytelen vagy több különböző indexelt értéke van. |

A `LAST ACTIVITY` mindig megmutatja az adatbázis és az index activityazonosítóját, valamint az esetleges index-gapeket. A `COMPARISON SUMMARY` az összehasonlított pillanatképekkel rögzített valós értékeket mutatja, újranyitás és javítás után is változatlanul. Legfeljebb öt gapazonosító látszik; a `... N total` a teljes elemszámot jelzi. A mentett riportba is csak öt azonosító és a teljes elemszám kerül.

Javítás után a `REPAIR SUMMARY` a törölt dokumentumok és beírt verziók számát, valamint a frissen kiolvasott activityértékeket mutatja. Ennek megjelenítéséhez SQL- és indexelérés szükséges. A comparison összesítés továbbra is a javítás előtti állapotot írja le.

`-w` mellett a `Report: ...` sor mutatja a teljes `comparison-<id>.jsonl` fájlt (neve esetenként `comparison-<id>-summary.jsonl`). Eltéréseket, összesítést és sikeres javítás után `repair_completed` rekordot tartalmaz. Megőrzéséhez tartsd meg a munkakönyvtárat. A pillanatképek és riportok nem titkosítottak. Az előrehaladás és a hibák a stderr-re, az olvasható összesítések a stdout-ra kerülnek.

| Kilépési kód | Jelentés |
| --- | --- |
| `0` | Nincs javítási teendő, vagy a megerősített javítás befejeződött. |
| `1` | Eltérés vagy activitykorrekció maradt; a javítást nem erősítették meg. |
| `2` | Hibás paraméter, ellenőrzési, előkészítési, javítási, megjelenítési vagy fájlhiba. |
| `130` | Megszakítás Ctrl+C-vel. |

PowerShellben a kilépési kódot a `$LASTEXITCODE` változóból olvashatod ki.

## Hatókör és hibaelhárítás

Az eszköz az indexelt metaadatokat és a verziók meglétét ellenőrzi. Egyező időbélyegeknél az üzleti mezőket helyesnek tekinti; nem hasonlít össze minden üzleti mezőt. Régebbi verzió node-időbélyege eltérhet, ha az útvonala egyezik az aktuális piszkozatéval. A letiltott indexelésű tartalomtípusok nem szerepelnek az elvárt SQL-verziók között.

Ha a források megváltoztak, az index zárolt vagy a mentett fájlok sérültek, szüntesd meg az okot; megváltozott forrásokkal új teljes ellenőrzés kell. Ne szerkeszd a mentési pontok fájljait és ne kényszeríts index-unlockot. Hiányzó/hibás SQL-indexelési adat, külső tárolású tartalomtípus-definíció vagy nem támogatott egyedi indexelési beállítás esetén a művelet a javítás írásai előtt leáll; a konkrét hiba és a [fejlesztői dokumentáció](readme-hu-for-dev.md) segít a kivizsgálásban. Ha egy régebbi munkafolyamatban nincs mentett activityállapot, új teljes ellenőrzés szükséges.
