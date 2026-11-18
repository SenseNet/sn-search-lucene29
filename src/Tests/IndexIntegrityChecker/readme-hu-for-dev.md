# IndexIntegrityChecker — fejlesztői dokumentáció

[English](readme-for-dev.md) | Magyar | [Felhasználói útmutató](readme-hu.md)

Önálló kísérleti konzolos eszköz egy sensenet SQL Server-adatbázis és egy Lucene 2.9-index összehasonlításához, majd kézi megerősítéssel az index javításához. A pillanatképeket munkakönyvtárban dolgozza fel. Alapértelmezésben a könyvtár ideiglenes, és a futás után törlődik; a `-w` megadásával a mentési pontok megmaradnak, és a javítás indításáig a munkafolyamat folytatható. Az SQL mindig csak olvasható; a megerősített javítás a kiválasztott indexet helyben módosítja.

## Használat

Új ellenőrzéshez connection string és indexútvonal szükséges. A munkakönyvtár opcionális. A névvel ellátott paraméterek sorrendje tetszőleges:

| Paraméter | Elsődleges név | Aliasok |
| --- | --- | --- |
| SQL Server connection string | `--connection-string` | `--database`, `--db`, `-d` |
| Lucene-index elérési útja | `--index` | `-i` |
| Tartós munkakönyvtár (opcionális) | `--working-directory` | `--workdir`, `-w` |
| A stdout-ra kiírt eltérések maximuma (csak ideiglenes módban, alapérték 20) | `--max-console-differences` | |

Futtatás perzisztencia nélkül:

```powershell
.\publish\IndexIntegrityChecker.exe --db "Server=localhost;Database=sensenet;Integrated Security=true;Encrypt=true;TrustServerCertificate=true" --index "D:\Indexes\LocalIndex"
```

A pillanatképek, mentési pontok és a riport megőrzése:

```powershell
.\publish\IndexIntegrityChecker.exe --db "Server=localhost;Database=sensenet;Integrated Security=true;Encrypt=true;TrustServerCertificate=true" --index "D:\Indexes\LocalIndex" --workdir "D:\Checks\check-001"
```

Ugyanazon munkafolyamat folytatásához elegendő a munkakönyvtár:

```powershell
.\publish\IndexIntegrityChecker.exe -w "D:\Checks\check-001"
```

Minden tartós munkakönyvtár egy munkafolyamathoz tartozik. Új tartós ellenőrzéshez másik könyvtárat kell megadni. Ha a munkafolyamat befejeződött, a `-w` ellenőrzi a mentett fájlokat, és újra felhasználja a mentett összehasonlítást, a rögzített activityállapottal együtt. Befejezett javítás esetén a javítás összesítése is megjelenik az aktuális SQL- és Lucene-activityállapottal. Induláskor a standard hibakimeneten megjelenik a kiválasztott mód.

Az értékeket célszerű idézőjelbe tenni. A `--option=value` forma is támogatott, például `--workdir="D:\Checks\check-001"`. A paraméternevekben a kis- és nagybetűk nem számítanak; normalizálásuk független az aktuális kultúrától, az értékek betűmérete változatlan marad. Minden paraméter egyszer adható meg; ugyanazon paraméter több aliasa vagy eltérő betűméretű alakja is ismétlésnek számít. A hiányzó érték, ismeretlen opció vagy ismétlés használati tájékoztatót és 2-es kilépési kódot eredményez. `-w` nélkül mindkét forrásparaméter kötelező. `-w` mellett új munkafolyamathoz mindkét forrás szükséges, meglévő folytatásához elegendő a `-w`. Az ismét megadott forrásbeállításoknak egyezniük kell a mentettekkel.

A `--max-console-differences N` 1 és 2147483647 közötti egész számot fogad el, alapértéke 20. Kizárólag tartós munkakönyvtár nélkül használható. A `-w`, `--workdir` vagy `--working-directory` opcióval együtt argumentumhibát eredményez, folytatáskor is. A limit eltérésrekordokat számol, így egy dokumentum több eltérése külön-külön számít.

A példa helyi fejlesztői SQL Serverhez használja a `TrustServerCertificate=true` beállítást; érvényes tanúsítványú szervernél ezt hagyd el. Az eszköz nem naplózza a connection stringet vagy a jelszót.

Az index útvonala mutathat a `segments_*` fájlokat tartalmazó könyvtárra vagy annak szülőjére. Szülőkönyvtár esetén az eszköz a közvetlen, érvényes indexet tartalmazó alkönyvtárak közül a név szerinti rendezésben utolsót választja. Ez támogatja a sensenet időbélyeggel elnevezett könyvtárait. Tartós módban a kiválasztott könyvtár és a commit verziója bekerül a `workflow.json` fájlba; a folytatás ugyanezt az indexet használja.

A megadott munkakönyvtár létrejön, ha hiányzik. A relatív index- és munkakönyvtár-útvonalak az indítási könyvtárhoz képest értendők; a folyamat aktuális könyvtára nem változik. A tartós munkakönyvtár a mentés után áthelyezhető, de a rögzített forrásindex-útvonal abszolút marad.

## Ideiglenes futás

A `-w` elhagyásakor egyedi könyvtár jön létre a `Path.GetTempPath()/IndexIntegrityChecker/<GUID>` útvonalon. Windowson ez rendszerint a felhasználó ideiglenes könyvtára alatt található. Az exportok és a lemezalapú metaadatok feldolgozása ugyanúgy folyamatos, ezért az ideiglenes futáshoz is szükséges szabad hely az adatfájloknak és a riportnak.

A kapcsolati beállítások és a munkafolyamat állapota memóriában maradnak: nem készül `workflow.json` vagy `.dpapi` fájl. Minden indítás új ellenőrzést kezd, és mindkét forrás megadását igényli. Az ideiglenes futás nem folytatható.

Az eszköz sikeres futás, talált eltérések, kezelt hibák vagy Ctrl+C után törli a saját generált könyvtárát. A közös ideiglenes szülőkönyvtár és a többi futás megmarad. Kényszerített folyamatleállítás után maradhatnak ideiglenes fájlok. Sikertelen takarításkor az eszköz kiírja a megmaradt útvonalat, és az egyébként sikeres futás kilépési kódját (0 vagy 1) 2-re módosítja; megszakításkor megmarad a 130-as kód.

Az első N eltérés folyamatosan a standard kimenetre kerül, majd egy olvasható összehasonlítási összesítés következik; N alapértéke 20. A kiírási limit elérése után az ellenőrzés az összes további rekordot is feldolgozza. Az első elhagyott eltérésnél a standard hibakimeneten egyszer megjelenik a limitről és az összes eltérés `-w` használatával történő megőrzéséről szóló tájékoztatás. Legfeljebb N eltérésnél nincs ilyen jelzés. Az összesítés minden számlálója a teljes ellenőrzésre vonatkozik.

Egyedi kiírási limit használata:

```powershell
.\publish\IndexIntegrityChecker.exe -d "<connectionstring>" -i "D:\Indexes\LocalIndex" --max-console-differences 50
```

A limit átirányított stdout esetén is érvényes. Az alábbi parancs legfeljebb N eltérést és a teljes összesítést menti:

```powershell
.\publish\IndexIntegrityChecker.exe -d "<connectionstring>" -i "D:\Indexes\LocalIndex" > result.txt
```

Az összes eltérés megőrzéséhez tartós futást kell indítani:

```powershell
.\publish\IndexIntegrityChecker.exe -d "<connectionstring>" -i "D:\Indexes\LocalIndex" -w "D:\Checks\check-001"
```

## Munkafolyamat és folytatás

A mentési pontok üzenete `Checkpoint: <állapot>.` formában kerül a standard hibakimenetre, konzolon zöld állapotnévvel. Folytatáskor a kiírás `Resuming workflow: <állapot>.`, sárga állapotnévvel. Átirányított kimenet esetén egyszerű szöveg jelenik meg, színkódok nélkül.

`-w` megadásakor a `workflow.json` fájl `phase` tulajdonsága tartalmazza az utolsó befejezett mentési pontot:

| Állapot | Mentett eredmény | Következő lépés folytatáskor |
| --- | --- | --- |
| `Initialized` | A forrásindex azonosító adatai és a titkosított SQL-kapcsolati beállítások | Az adatbázis exportálása |
| `DatabaseSaved` | Rendezett SQL-rekordok | A rögzített index exportálása |
| `IndexSaved` | Rendezett indexrekordok, mezőhibák és a történeti útvonalak metaadatai | A mentett fájlok összehasonlítása |
| `ComparisonSaved` | Teljes riport és összesítés | Javítás előkészítése a riportból és az élő forrásokból |
| `Completed` | Végleges eredmény | A mentett eredmény visszaadása |
| `RepairPrepared` | Teljes riport és javítási előnézet | Olvasási előellenőrzés megismétlése és új kézi megerősítés |
| `RepairStarted` | Az első indexírás előtt tartósan mentett indításjelző | Folytatás tiltva; új teljes ellenőrzés szükséges |
| `RepairCompleted` | Sikeres javítás összesítése | Eredmény visszaadása a riport újbóli alkalmazása nélkül |

A folytatás **fázishatároktól** működik. A félbeszakadt adatbázis-export, indexexport vagy összehasonlítás az adott fázis elejétől újraindul. A korábbi, befejezett fázisok eredményeit újra felhasználja. Az exportokon és az összehasonlításon belül nincsenek további mentési pontok.

Az adatbázis-exporthoz SQL, az indexexporthoz az eredeti index szükséges. Az összehasonlítás és összesítése a mentett pillanatképeket használja. A javítás előkészítéséhez és végrehajtásához ismét szükséges mindkét forrás és a mentett kapcsolati beállítások. Az összehasonlítás összesítésének megjelenítése nem igényli a források elérését. A `RepairCompleted` javítási összesítéséhez szükséges mindkét forrás és a mentett kapcsolati beállítások az aktuális activityállapot kiolvasásához.

A kimeneti fájlok `.partial` utótaggal készülnek, tartalmuk lemezre íródik, majd elkészüléskor kapják meg végleges nevüket. Az állapotfájl atomi cserével frissül, miután a mentési ponthoz tartozó összes fájl elkészült. A hibás vagy megszakított fázis megőrzi az előző mentési pontot. A nem véglegesített fájlokat az eszköz figyelmen kívül hagyja, és a fázis újrafuttatásakor felülírja.

Folytatáskor az eszköz ellenőrzi a befejezett mentési pontokhoz tartozó összes fájl hosszát és SHA-256 ellenőrzőösszegét. Ehhez sorban végigolvassa a mentett adatokat, a kész eredmény visszaadásakor is. A hiányzó, csonkolt vagy módosított fájl hibát okoz; az eszköz nem generálja újra észrevétlenül egy esetleg megváltozott forrásból.

Futás közben az eszköz kizárólagosan megnyitva tartja a `workflow.lock` fájlt. Két folyamat nem használhatja egyszerre ugyanazt a munkakönyvtárat. A fájl kilépés után megmarad, de az operációs rendszer által kezelt zárolás megszűnik; nem kell törölni.

## A munkakönyvtár tartalma

Tartós futáskor az alábbi fájlok megmaradnak. Az ideiglenes futás az adat-, riport-, zár- és munkafájlokat feldolgozás közben használja, majd a teljes saját könyvtárát törli; állapot- és kapcsolati fájlt nem készít.

| Fájl | Tartalom |
| --- | --- |
| `workflow.json` | Munkafolyamat-azonosító, állapot, a forrásindex azonosító adatai, fájlnevek, rekordszámok, hosszak, ellenőrzőösszegek és eredményösszesítés |
| `workflow.lock` | A munkakönyvtár kizárólagos zárolása |
| `connection-<id>.dpapi` | Titkosított SQL-kapcsolati beállítások |
| `database-<id>.tsv` | `VersionId` szerint rendezett SQL-mentés |
| `index-<id>.tsv` | `VersionId` szerint rendezett indexmentés, a duplikátumok megőrzésével |
| `index-issues-<id>.jsonl` | Az indexexport során talált mezőhibák |
| `comparison-<id>.jsonl` vagy `comparison-<id>-summary.jsonl` | Az összes eltérés, az összehasonlítás összesítése és a sikeres javítás rekordja; az aktív riport útvonala a `workflow.json` fájlban szerepel |
| `index-scratch-<id>.map` | Lucene DocId szerint címzett ideiglenes metaadatok |
| `comparison-scratch-<id>.bits` | A már jelentett hiányzó draftútvonalak ideiglenes nyilvántartása |
| `different-documents-<id>.bits` | Az eltéréssel érintett különböző indexdokumentumok ideiglenes nyilvántartása |

Tartós módban a connection stringet a Windows DPAPI titkosítja az aktuális felhasználóhoz kötve, a munkafolyamat azonosítóját kiegészítő entrópiaként használva. Folytatáskor az adatbázis-exporthoz, a javítás előkészítéséhez és az aktuális activityállapot megjelenítéséhez ugyanaz a Windows-felhasználó és gép szükséges. A kapcsolati beállítások nem kerülnek nyílt szövegként az állapotfájlba. Az adatmentések és a riportok maguk nem titkosítottak.

A munkafájlok a fázis szabályos befejezésekor vagy kezelt hibával történő kilépésekor törlődnek. Tartós módban kényszerített leállítás után maradhatnak munkafájlok vagy `.partial` fájlok; az adott fázis következő futása felülírja őket. A tartós futás befejezett adatmentései megmaradnak az ellenőrizhetőség és a folytatás érdekében.

## Az adatmentések formátuma

Mindkét adatmentés UTF-8 kódolású, tabulátorral tagolt fájl (`.tsv`), BOM nélkül, LF sorvégekkel. Az első sor oszlopfejléc; minden további sor egy rekordot tartalmaz. A rekordok `VersionId` szerint rendezettek, akkor is, ha az első oszlop a `nodeId`. Az írás és olvasás folyamatosan, rekordonként történik.

Az adatbázismentés oszlopsorrendje és egy példasor (az elválasztók valódi TAB karakterek):

```text
nodeId	versionId	nodeTimestamp	versionTimestamp	lastMajorVersionId	lastMinorVersionId
1	10	100	200		10
```

Az indexmentés oszlopsorrendje és egy példasor:

```text
nodeId	versionId	nodeTimestamp	versionTimestamp	isLastPublic	isLastDraft	docId	presentFields	draftDocId	historicalPathMatches	missingOwnPath	missingDraftPath
1	10	100	200	0	1	0	63	0	0	0	0
```

A számok kultúrától független, tízes számrendszerű alakban szerepelnek; az időbélyegek megőrzik teljes, 64 bites egész pontosságukat. A logikai értékeknél a hamis `0`, az igaz `1`. A `lastMajorVersionId` és `lastMinorVersionId` SQL null hivatkozásait üres mező jelöli; a numerikus nulla `0` marad. A sorvégi üres mezők is megmaradnak. Az adatmezők nem tartalmaznak szabad szöveget, így nem igényelnek idézőjelezést vagy escape-elést.

A `presentFields` tízes számrendszerben kiírt bitmaszk, amely az indexben megtalált mezőket jelöli:

| Bitérték | Mező |
| --- | --- |
| 1 | `NodeId` |
| 2 | `NodeTimestamp` |
| 4 | `VersionTimestamp` |
| 8 | `IsLastPublic` |
| 16 | `IsLastDraft` |
| 32 | `VersionId` |

A `63` érték mind a hat mező jelenlétét jelenti. A drafthivatkozás és az útvonaljelzők a történeti időbélyegek ellenőrzéséhez szükséges adatokat tartalmazzák, teljes útvonalakat nem.

Az adatrekordok számát (fejléc nélkül), a bájtban mért hosszt és az SHA-256 ellenőrzőösszeget az eszköz futás közben nyilvántartja, tartós módban pedig a `workflow.json` fájlba menti. Az olvasás ellenőrzi a fejlécet és az oszlopsorrendet, elutasítja a hibás oszlopszámot vagy értékeket, és ellenőrzi az adatsorok elvárt számát.

A munkakönyvtár bináris és JSONL-mentéseit az eszköz a `-w` alapú folytatáskor automatikusan TSV-re alakítja, a források lekérdezése nélkül. Ellenőrzi a mentett ellenőrzőösszegeket, folyamatosan új fájlokba írja a rekordokat, és az átalakított bemenetek törlése előtt rögzíti a frissített állapotot. Ha az átalakítás az állapotfrissítés előtt hibával leáll, a bemeneti mentési pont megmarad az újrapróbáláshoz. Az átalakítás idejére a bemeneteknek és a TSV-kimeneteknek is kell szabad hely. A már elkészült összehasonlítási riport megmarad.

## Az ellenőrzés hatóköre

- Az adatbázisban szereplő, de az indexből hiányzó verzió: `NotInIndex`.
- Az ellenőrzött SQL-verziók között nem szereplő indexdokumentum: `NotInDatabase`.
- Azonos `VersionId`-hoz tartozó több dokumentum: minden további dokumentum `MoreDocument` eltérést eredményez; a duplikált dokumentumok adatai is ellenőrzésre kerülnek.
- A `NodeId`, `VersionTimestamp` és `NodeTimestamp` értékek egyezése.
- `IsLastPublic == (VersionId == LastMajorVersionId)` és `IsLastDraft == (VersionId == LastMinorVersionId)`.
- Hiányzó vagy érvénytelen kötelező mezők, valamint több különböző értékkel indexelt kötelező mezők.

Az a verzió, amelynek indexelt `IsLastDraft` értéke nem `yes`, eltérhet az SQL-től `NodeTimestamp` tekintetében, ha a tárolt útvonala megegyezik az indexben ugyanahhoz a node-hoz talált első aktuális draft útvonalával. Az indexexport ehhez csak a tárolt `Path` mezőt tölti be, és elmenti az egyezés, illetve a hiányzó útvonalak adatait. Az összehasonlítás ezt a kivételt csak eltérő időbélyegek esetén alkalmazza. Hiányzó útvonal nem fedhet el időbélyeg-eltérést.

A törölt Lucene-dokumentumok és a `$#COMMIT` dokumentumok kimaradnak. A `VersionId` nélküli dokumentumokat az eszköz külön ellenőrzi. A kötelező logikai értékek a sensenet `yes` és `no` termjeit használják.

Az eszköz a numerikus mezők és jelzők **kereshető, indexelt értékeit** ellenőrzi. Nem hasonlít össze minden üzleti mezőt, nem ellenőrzi külön a tárolt és indexelt reprezentációkat, és nem végez teljes összehasonlítást az SQL-oldali útvonalakkal. Egyező timestampek mellett az üzleti mezőket helyesnek tekinti. Nem indít sensenet repositoryt; a javítás csak az érintett verziókat és a helyreállításukhoz szükséges dokumentumokat írja át.

## Nagy adathalmazok és teljesítmény

Az adatbázis-export egyetlen rendezett verziólekérdezésből, folyamatosan írja TSV-fájlba a `NodeId`, `VersionId`, `NodeTimestamp`, `VersionTimestamp`, `LastMajorVersionId` és `LastMinorVersionId` adatokat.

Az indexexport közvetlenül a termeken és a hozzájuk tartozó dokumentumlistákon keresztül olvassa az `Id`, `NodeTimestamp`, `VersionTimestamp`, `IsLastPublic` és `IsLastDraft` mezőket, csak a teljes pontosságú numerikus termeket használva. A metaadatokat lemezalapú, memóriába leképezett fájlban, DocId szerint címezve tárolja. Az `Id` dokumentumlistáinak bejárása meghatározza node-onként az aktuális draftot, és előkészíti a történeti útvonalak metaadatait. A teljes pontosságú `VersionId` termek már numerikus sorrendet adnak, így további rendezés nem szükséges.

Az összehasonlítás sorban olvasva összefésüli a mentett adatfolyamokat, csak az aktuális rekordokat tartja meg, az eltéréseket pedig azonnal kiírja. Az összehasonlítás nem tartja managed memóriában a teljes SQL-eredményt, indexeredményt vagy eltéréslistát. A dokumentummetaadatok és a hiányzó útvonalak nyilvántartása lemezes tárolást használ. A javítás a riport eltéréseit, az érintett DocId/VersionId halmazokat és a verziónkénti ellenőrzőösszegeket memóriában tartja; az SQL-dokumentumok tartalmát verziónként tölti be. A Lucene belső struktúrái és az operációs rendszer gyorsítótárai továbbra is fogyasztanak memóriát; a rezidens memóriaigény nem állandó. Nagy indexekhez 64 bites folyamatot használj.

Lemezterületigény:

- Adatbázismentés: egy fejlécsor és SQL-verziónként hat, tabulátorral elválasztott mező; bájtmérete a számértékektől és az üres null hivatkozásoktól függ.
- Indexmentés: egy fejlécsor és exportált VersionId/DocId páronként tizenkét, tabulátorral elválasztott mező; bájtmérete a számértékektől függ. Minden logikai érték egy karaktert foglal.
- Az indexexport munkafájlja: Lucene `MaxDoc` helyenként 32 bájt, de legalább egy bájt. A törölt dokumentumok helyei is beleszámítanak a `MaxDoc` értékbe.
- Az összehasonlítás munkafájljai: két, egyenként `ceil(MaxDoc / 8)` bájtos bittömb a hiányzó draftútvonalak és az eltéréssel érintett különböző indexdokumentumok nyilvántartásához.
- Indexhibák és összehasonlítási riportok: változó méret, a jelentett eltérések számával arányosan.

Indexexport közben mindkét mentésnek, a metaadatok munkafájljának és a hibafájlnak is kell hely. A történeti verziók további tárolt-`Path` olvasásokat jelentenek exportáláskor. A futási időt az SQL-rendezés, a Lucene-bejárás, a lemezsebesség, az útvonalak olvasása és az eltérések száma határozza meg. A folyamatos összehasonlítást 100 000 rekordos teszt ellenőrzi; üzemi méretű gyorsulási mérés nem készült.

## Kimenet és kilépési kódok

Az összehasonlítási riport `type: "difference"` rekordokat tartalmaz, majd egy `type: "summary"` rekord következik a számlálókkal, az összehasonlítás futási idejével és a `lastActivity` objektummal. Utóbbi `database` és `index` mezőt tartalmaz; az indexen belül `lastActivityId`, `gapPreview` (legfeljebb öt azonosító) és `gapCount` (teljes elemszám) szerepel. Az értékek az összehasonlított pillanatképekkel együtt beolvasott valós SQL identity- és Lucene commit-metaadatokból származnak. Ugyanez az activity-összesítés a workflow comparison mentési pontjába is bekerül. A javítás sikeres commitja után az aktív comparison fájl végére egy `type: "repair_completed"` JSON-rekord kerül `workflowId`, `deletedDocuments`, `writtenVersions`, `lastActivityId` mezőkkel és üres `gaps` listával. A korábbi riportbájtok megmaradnak. A hossz, a sorszám és az SHA-256 ellenőrzőösszeg a `RepairCompleted` mentése előtt frissül; a befejezett workflow újranyitása nem ír újabb rekordot. `-w` mellett a riport megmarad. Ideiglenes módban ugyanez a rekord elkészül a munkakönyvtár takarítása előtt.

A standard kimeneten `COMPARISON SUMMARY:` fejlécű, igazított szöveges blokk jelenik meg az állapottal és az összehasonlítás teljes számlálóival. A `COUNTS BY KIND` csak eltérés esetén látható, a nem nulla számlálókkal. A `LAST ACTIVITY` szekció és a záróvonal mindig megjelenik. Ez a szekció az összehasonlításhoz mentett activityállapotot használja: az adatbázis értéke az SQL-indexelési activity exportkor utoljára kiosztott identity értéke; az index értéke az exportált Lucene-commitból származik. A nem üres gaplistából legfeljebb öt azonosító látszik; hosszabb listánál a végén `... N total` szerepel, ahol N a gapek teljes száma.

A `COMPARISON SUMMARY` activityállapota újranyitás és javítás után is változatlan; megjelenítése nem olvassa újra a forrásokat. A hiányzó `lastActivity` mezőt a tool a riporthoz mentett pillanatképek metaadataiból pótolja, megőrizve az esetleges javítási zárórekordot és frissítve a riport ellenőrzőösszegét. Ha nincs elmentett teljes activityállapot, új teljes ellenőrzés szükséges; a jelenlegi értékekből a múltbeli állapot nem rekonstruálható. A `REPAIR SUMMARY` újra kiolvassa az SQL-t és az aktuális Lucene-commitot. Ez a lekérdezés kizárólag SELECT; a Lucene-metaadatok olvasása nem ír az indexbe és nem tölti be a repository sémáját. Ha a javítás összesítéséhez valamelyik forrás nem olvasható, a tool hibát jelez, és 2-es kilépési kóddal leáll, helyettesítő javítási összesítés kiírása nélkül.

`-w` nélkül legfeljebb N JSON-eltérésrekord előzi meg a szöveges összesítést (alapérték 20, a `--max-console-differences` paraméterrel állítható). Átirányításkor is ugyanez az olvasható összesítés készül, ezért ideiglenes módban a stdout JSON-rekordokat és szöveget vegyesen tartalmaz. A limit nem állítja le az ellenőrzést és nem korlátozza az összesítés számlálóit. Hibás vagy megszakított összehasonlítás után nem készül összesítés. `-w` mellett a teljes, géppel feldolgozható összehasonlítás a mentett JSONL-riportban található. A mód, a haladás, a riport útvonala, a kiírási limitről szóló tájékoztatás, a mentési pontok, a takarítás üzenetei és a futási hibák a standard hibakimenetre kerülnek.
```text
COMPARISON SUMMARY:
  status:                      differences
  databaseVersions:             182
  indexDocuments:               177
  matchedDocuments:             177
  differences:                    9
  differentDocuments:             7
  COUNTS BY KIND:
    DifferentVersionTimestamp:    2
    DifferentNodeTimestamp:       2
    NotInIndex:                   5
  LAST ACTIVITY:
    database:                  1950
    index:                     1897 (1800, 1801, 1802, 1803, 1804, ... 8 total)
-----------------------------------------------
```

A mentett JSON comparison összesítés és a konzolos `COMPARISON SUMMARY` tartalmazza a `differences` értéket, vagyis a jelentett eltérésrekordok számát, és a `differentDocuments` értéket, vagyis az érintett különböző dokumentumok számát:

- Minden eltérésben hivatkozott indexdokumentum egyszer számít, `DocId` alapján, akkor is, ha több mezőhibája van, több verzióazonosító alatt szerepel, vagy az indexexport és az összehasonlítás is jelenti.
- Minden indexből hiányzó SQL-verzió (`NotInIndex`) egy elvárt dokumentumként számít, `VersionId` alapján.
- Ugyanazon node különböző verziói és a különálló duplikált indexdokumentumok külön dokumentumok. Csak az a dokumentum számít bele, amelyre eltérés hivatkozik.

Például két dokumentum két-két eltérő időbélyegmezője `"differences": 4` és `"differentDocuments": 2` eredményt ad. Ha egy indexdokumentumnak nincs használható verzióazonosítója, nem párosítható egy hiányzó SQL-verzióval; a két pár nélküli bejegyzés külön számít.

Tartós módban a számláló a `workflow.json` fájlba is bekerül. Az indexdokumentumok egyediségét mindkét módban lemezalapú bittömb tartja nyilván, a hiányzó SQL-verziók pedig rendezett sorrendben dolgozódnak fel, növekvő memóriabeli halmaz nélkül.

Ha egy mentett összesítésből hiányzik a `differentDocuments`, a folytatás a mentett eltérésrekordok folyamatos olvasásával kiszámítja. Megőrzi ezeket a rekordokat és az összesítés meglévő adatait, a források lekérdezése és az összehasonlítás megismétlése nélkül. Az állapot csak a teljes csere-riport elkészülte után vált át arra, majd a bemeneti riport törlődik. A további folytatások a kiegészített összesítést használják.

```powershell
.\publish\IndexIntegrityChecker.exe -w "D:\Checks\check-001" > summary.txt
$LASTEXITCODE
```

| Kilépési kód | Jelentés |
| --- | --- |
| 0 | Nincs javítási teendő, vagy a megerősített javítás sikeresen befejeződött. |
| 1 | Eltérés vagy activityállapot-javítás maradt; a javítást nem erősítették meg. |
| 2 | Hibás paraméterek, sérült fájlok, megváltozott források vagy sikertelen ellenőrzés/előkészítés/javítás. |
| 130 | Az ellenőrzést vagy javítást Ctrl+C szakította meg. |

## Letiltott indexelésű tartalomtípusok

Az eszköz beolvassa a `NodeTypes` hierarchiát és a `/Root/System/Schema/ContentTypes` alatti tartalomtípusok legutolsó draftjához tartozó `Binary` XML-definíciókat. Kiértékeli az `AllowIndexing` beállítást az örökléssel együtt, és kizárja a letiltott típusokat az SQL-verziólistából. Az ilyen típusokhoz megmaradt indexdokumentumokat eltérésként jelenti.

A definícióknak a `Files.Stream` mezőből olvashatónak kell lenniük. Külső blob provider vagy hiányzó definícióstream esetén kifejezett futási hiba keletkezik, mert nincsenek megadva külső blobtároló-elérési beállítások. Az eszköz nem használja a `Versions.IndexDocument IS NOT NULL` feltételt verziók kizárására, mert az elrejthetné a hiányzó indexelési adatokat.

## Konzisztens ellenőrzés futtatása

Összetartozó adatbázis- és indexpillanatképeket használj, vagy az exportok készítése alatt szüneteltesd az írásokat és az indexelést. A Lucene és az SQL nem osztozik közös tranzakción. A folytatás nem tud egymástól függetlenül rögzített forrásokat konzisztenssé tenni.

Az SQL-adatok olvasása az adatbázis-export alatt egyetlen tranzakcióban történik, engedélyezett snapshot izoláció esetén azt, egyébként `Serializable` izolációt használva. A Serializable az adatbázis-export végéig tarthat megosztott és tartományzárakat, és blokkolhatja az írókat. A tranzakció az indexexport és az összehasonlítás előtt lezárul. Az eszköz nem módosít adatbázis-beállításokat, és nem használ `NOLOCK`-ot. A lekérdezések Ctrl+C-vel megszakíthatók.

Az inicializáláskor rögzített indexcommitnak a javítás indításáig elérhetőnek és változatlannak kell maradnia. Az eszköz exportkor, előkészítéskor és közvetlenül írás előtt ellenőrzi. Megváltozott commit esetén új teljes ellenőrzés szükséges. Az ellenőrzés olvasókat, a javítás normál Lucene-írási zárakat használ; nincs kényszerített unlock vagy másik író zárának törlése.

## Offline IndexOnly javítás

Az összehasonlítás után a tool a teljes `comparison-<id>.jsonl` riportból (vagy az aktív, összesítéssel kiegészített riportból) készíti elő a javítást. A meglévő `Versions.IndexDocument` értékeket és az aktuális metaadatokat SELECT-ekkel olvassa. Az SQL-dokumentumokat, tartalmakat, activitysorokat és feldolgozási jelzőket soha nem módosítja. Hiányzó vagy hibás SQL-indexdokumentum és nem támogatott mezőbeállítás esetén az első indexírás előtt megáll.

Az operátor készít indexmentést/másolatot, és biztosítja, hogy az SQL és az index az ellenőrzéstől a javítás végéig más folyamatból ne változzon, a futások közötti várakozás alatt sem. A tool nem másol indexet. A megerősítés alatt is szükségesek a források: az olvasási SQL-tranzakció a prompt idejére nyitva marad.

Az előkészítés kiírása `Preparing index repair...`. A prompt megmutatja a dokumentumtörlések és verzióbeírások számát, valamint a végső activityazonosítót, majd ezt kérdezi: `Perform the repair? [yes/NO]`. Az interaktívan megadott `yes` és `y` válasz indít javítást, kis- és nagybetűtől függetlenül, a környező szóközöket figyelmen kívül hagyva. Minden más válasz, EOF, üres bemenet vagy átirányított/nem interaktív stdin elutasításnak számít. `-w` mellett a `RepairPrepared` állapot ugyanazzal a paranccsal és munkakönyvtárral folytatható; az előkészítés megismétlődik, és új megerősítés szükséges. Ideiglenes módban a könyvtár a prompt/javítás után törlődik, folytatás nincs.

A javítás a writer indítása előtt az eredeti DocId-k szerint töröl, majd beírja a szükséges egyedi verziókat. A több verzióazonosítót tartalmazó hibás dokumentum javítása minden érintett verzióra és duplikátumra kiterjed. Kizárt/törölt verzióknál nincs visszaírás. A sikeres záró commit az SQL identity utolsó activityazonosítóját és üres gaplistát ment (a korábbi `Gaps` kulcsot eltávolítja), az egyéb user-data megőrzésével. A csak metaadatot rendező javítás a `$#COMMIT` jelölő új `$#DATA` GUID-dal történő cseréjével kényszeríti ki a commitot.

A javítás nem atomi, és `RepairStarted` után nem folytatható. Megszakítás után új munkakönyvtárban új teljes ellenőrzés szükséges, akkor is, ha a leállás az első tényleges indexírás előtt vagy a végső indexcommit után történt. A köztes commitok a korábbi activityállapotot őrzik. A sikeres végső commitot a `repair_completed` rekord aktív comparison riporthoz írása, majd a `RepairCompleted` mentése követi. Ha a befejezés rögzítése hibával leáll, a workflow `RepairStarted` állapotban marad, és új teljes ellenőrzés szükséges. A konzolon az összehasonlítás összesítésének stílusában szöveges `REPAIR SUMMARY` blokk jelenik meg az állapottal, a törölt dokumentumok és visszaírt verziók számával, friss `LAST ACTIVITY` szekcióval és záróvonallal. A JSON-zárórekord csak a riportba kerül. Az összehasonlítás eltérései és számlálói továbbra is a javítás előtti eredményt mutatják. Ha csak a befejezett eredmény megjelenítése hibás, a zárórekord és a mentési pont érvényes marad, és az üzenet megjelenítési hibát jelez. A konzisztenciát külön friss ellenőrzéssel kell igazolni.

A mezőmódok, tárolás, termvektorok, numerikus kódolás és analyzerek az MSSQL/Lucene-indexelő kód helyi adaptációját követik. A CTD-k, az SQL-ben tárolt aspect/listadefiníciók, a támogatott standard handlerek alapértékei és az explicit Keyword/Standard/Whitespace analyzerek offline feloldhatók. A `Sharing` és beépített indexhandlerei alapértelmezésben Keyword analyzert használnak; a javítás az SQL-indexdokumentumban tárolt megosztási tokentömböt állítja helyre az összetett kifejezésekkel együtt, repository-megosztási objektumok betöltése nélkül. Az `_Text` Standard analyzert használ. Fel nem oldható dinamikus mező, egyedi handler/konfiguráció vagy ütköző beállítás esetén nincs indexírás. A kísérleti API-elemek `TODO(refactor)` kommentet kapnak; a másolatok az eredeti forrást és commitot is jelölik. A sensenet repository-k nem módosulnak.

## Fordítás és tesztelés

### A forráskód felépítése

| Feladat | Fő fájlok |
| --- | --- |
| Paraméterek, megerősítés, konzolkimenet és kilépési kódok | [CommandLineOptions.cs](src/IndexIntegrityChecker/CommandLineOptions.cs), [Program.cs](src/IndexIntegrityChecker/Program.cs) |
| Fázisváltások, munkakönyvtár-kezelés és mentett állapot | [WorkflowRunner.cs](src/IndexIntegrityChecker/WorkflowRunner.cs), [WorkflowWorkspace.cs](src/IndexIntegrityChecker/WorkflowWorkspace.cs), [WorkflowState.cs](src/IndexIntegrityChecker/WorkflowState.cs) |
| SQL-olvasás, indexelési kizárások és pillanatkép-export | [SqlVersionSource.cs](src/IndexIntegrityChecker/SqlVersionSource.cs), [ContentTypePolicy.cs](src/IndexIntegrityChecker/ContentTypePolicy.cs), [WorkflowOperations.cs](src/IndexIntegrityChecker/WorkflowOperations.cs) |
| Lucene-kiolvasás és összehasonlítás | [LuceneSnapshot.cs](src/IndexIntegrityChecker/LuceneSnapshot.cs), [IntegrityChecker.cs](src/IndexIntegrityChecker/IntegrityChecker.cs), [SavedComparison.cs](src/IndexIntegrityChecker/SavedComparison.cs) |
| Pillanatképek/riportok mentése és kompatibilitása | [CheckpointFiles.cs](src/IndexIntegrityChecker/CheckpointFiles.cs), [SnapshotFiles.cs](src/IndexIntegrityChecker/SnapshotFiles.cs), [WorkflowSnapshotUpgrade.cs](src/IndexIntegrityChecker/WorkflowSnapshotUpgrade.cs), [ComparisonSummaryUpgrade.cs](src/IndexIntegrityChecker/ComparisonSummaryUpgrade.cs) |
| Javítás tervezése és a források ellenőrzése | [ComparisonRepairer.cs](src/IndexIntegrityChecker/ComparisonRepairer.cs), [MsSqlRepairSource.cs](src/IndexIntegrityChecker/MsSqlRepairSource.cs) |
| SQL-indexdokumentum konverziója, séma/analyzerek és Lucene-írás | [RepairDocuments.cs](src/IndexIntegrityChecker/RepairDocuments.cs), [LuceneRepairWriter.cs](src/IndexIntegrityChecker/LuceneRepairWriter.cs) |

A projekt közvetlenül MSSQL-re és Lucene 2.9-re épül. Nincs futó repository-függősége vagy általános providerrétege. Az `IWorkflowOperations`, a javítópéldányt létrehozó és megerősítő callbackek, valamint a javító sor- és activity-lekérő delegate-jei teszthelyettesítési pontok. Az `InternalsVisibleTo("IndexIntegrityChecker.Tests")` a tesztassembly számára elérhetővé teszi a belső szerződéseket.

### Módosításkor megőrzendő szabályok

- Az üzemi SQL kizárólag olvasható. A meglévő SQL `IndexDocument` adatokat használd; ne generáld őket repository-objektumok betöltésével, és ne módosítsd az eredeti sensenet repository-kat. Minden kísérleti API-elemhez `TODO(refactor)` komment kell; másolt/adaptált kódnál az eredeti forrást és commitot is jelöld.
- Az `IntegrityChecker` és a `SavedComparison` összehasonlítása maradjon azonos viselkedésű, a régi verziók útvonalait, kizárt típusokat és hibás/duplikált dokumentumokat is beleértve. Egy eltéréstípus módosítása a riport feldolgozását és a javítás tervezését is érinti.
- A DocId a rögzített Lucene-commithoz tartozik. Módosítás előtt oldd fel az érintett dokumentumokat és verziókat; ne használd az eredeti DocId-kat, miután a writer merge-ei átszámozhatták őket. Tartsd tiszteletben a Lucene írási zárát.
- A `RepairStarted` az első indexmódosítás előtt legyen elmentve. A sorrend maradjon: sikeres végső indexcommit, `repair_completed` hozzáfűzése és a riport metaadatainak frissítése, majd `RepairCompleted` mentése. Az indításjelző utáni hiba új teljes ellenőrzést igényel; ne vezess be dokumentumonkénti folytatást.
- A comparison activityállapota a riportban és a mentési pontban is az összehasonlított pillanatképekhez tartozzon. A konzolos comparison ezt mutatja; csak a javítás összesítése olvas élő activityadatot. A gapelőnézet legfeljebb öt azonosítót és a teljes elemszámot tartalmazza. A végső javítási commit üres gaplistát és az utolsó kiosztott SQL activityazonosítót ment, csak metaadatot érintő javításnál is.
- Ahol támogatott, a riportok/mentési pontok mentése legyen atomi; maradjon meg a hossz, rekordszám és SHA-256 ellenőrzése, valamint a befejezett javítás rekordja az összesítések bővítésekor. A mentési szerződések kompatibilisen bővüljenek, vagy kapjanak explicit, ellenőrzött migrációt. Hiányzó forrásmetaadatból ne állíts elő kitalált sikeres eredményt.

### Fordítási és tesztparancsok

A fordításhoz .NET 8 SDK vagy újabb, a futtatáshoz .NET 8 futtatókörnyezet szükséges. Tartós munkafolyamat létrehozásához Windows kell, mert a mentett kapcsolati beállításokat Windows DPAPI védi. Az ideiglenes mód nem használ DPAPI-t. A függőségek NuGet-csomagok, helyi sensenet-forrásútvonalakra mutató hivatkozások nélkül.

```powershell
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-restore
dotnet publish .\src\IndexIntegrityChecker\IndexIntegrityChecker.csproj -c Release --no-restore -o .\publish
```

A tesztek lefedik az ideiglenes kiírási limiteket és összesítést, a CLI-ellenőrzést, a takarítást, a mentési pontok folytatását, a fájlsérülést, a Windows-kapcsolatvédelmet, az összehasonlítás egyezését, a mentési formátumokat és a 100 000 rekordos folyamatos összehasonlítást. Valódi ideiglenes Lucene-indexeken tesztelt mind a 11 javítandó eltéréstípus, a többverziós dokumentumok feloldása, a régi verzió elkülönített javítása, a séma/analyzer-konverzió, a csak metaadatot mentő commit, az előkészített folytatás, az indításjelző mentési hibája és a javítás megszakítása. A javítási tesztek memóriabeli SQL-rekordokat használnak; az élő MSSQL-javítóforrást külön környezeti validációval kell ellenőrizni.

Az opcionális SQL-integrációs tesztek az ellenőrző lekérdezéseit, a javítás sor-konverzióját és az activitytakarítás után megmaradó utolsó identityértéket vizsgálják. Csak kapcsolathoz kötött `#` ideiglenes táblákat hoznak létre; nem készítenek állandó táblákat vagy adatbázisokat, és nem módosítanak meglévő adatokat:

```powershell
$env:INDEX_CHECKER_TEST_SQL_CONNECTION = 'Server=localhost;Database=tempdb;Integrated Security=true;Encrypt=true;TrustServerCertificate=true'
dotnet test -c Release --filter TestCategory=SqlIntegration
```

Javítást érintő módosításnál a teljes tesztkészlet előtt célzottan is futtathatók a kapcsolódó tesztek:

```powershell
dotnet test -c Release --no-restore --filter "FullyQualifiedName~RepairTests|FullyQualifiedName~RepairWorkflowTests"
```

A mentett formátumok és összesítések módosításához a workflow-, pillanatképformátum-, érintettdokumentum- és ideiglenesmód-tesztek is szükségesek. Az `INDEX_CHECKER_TEST_SQL_CONNECTION` nélkül az SQL-integrációs tesztek kimaradnak; az általános tesztek nem igazolják az élő SQL-forrás működését egy adott környezetben. A destruktív tesztadatok ideiglenes tesztindexekre és kapcsolathoz kötött SQL-ideiglenes táblákra korlátozódjanak.

A [readme.md](readme.md) és [readme-hu.md](readme-hu.md) maradjon rövid felhasználói útmutató, ez a fájl és a [readme-for-dev.md](readme-for-dev.md) pedig részletes fejlesztői referencia. Minden érintett célközönségnél mindkét nyelvet frissítsd, a parancssori példákat és korlátozásokat is beleértve.
