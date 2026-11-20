# Fermatigo vstupenky s QR kódem

Plugin pro nopCommerce 4.80, autor Tom Tomčo. Verze 1.11.0.

Prodej vstupenek na akce přes e-shop, akcí může být libovolně mnoho. Každá zakoupená vstupenka dostane vlastní QR kód.
Zákazník ho dostane e-mailem v PDF k tisku a může si ho stáhnout i jako obrázek do telefonu.
U vstupu obsluha QR kód načte mobilem, uvidí, na koho vstupenka je a jestli je zaplacená,
a jedním tlačítkem ji odbaví. Podruhé už stejný kód projít nejde.

## Jak to funguje

1. **Akce a vstupenka jako produkt.** V konfiguraci pluginu se založí akce (název, datum, místo, poznámka)
   a zadá se ID produktu (nebo více produktů), který je vstupenkou na tuto akci. Akcí může být víc,
   jeden produkt ale patří jen jedné akci. Cena, počet kusů jako kapacita a text produktu se nastavují
   v katalogu jako obvykle. Vstupenka si pamatuje svou akci, PDF, e-mail i stránka u vstupu ukazují údaje její akce.
   Když si zákazník v jedné objednávce koupí vstupenky na více akcí, dostane jeden e-mail se všemi.
   **VIP vstupenky:** u akce se v poli „ID produktů VIP vstupenek“ zadají produkty VIP vstupného. Obsluha po načtení
   QR kódu VIP vstupenky uvidí nahoře velký zlatý pruh „★ VIP ★“ a zlatý rámeček kolem stavu (platí pro platnou, odbavenou
   i už použitou vstupenku). VIP je vidět i v PDF (orámovaný nadpis „VIP VSTUPENKA“), na stránce zákazníka,
   v seznamu vstupenek v administraci a ve sloupci VIP v exportu a v záloze do Google Tabulky. Kdo není obsluha,
   VIP po načtení QR kódu nevidí.
2. **Po zaplacení objednávky** plugin ke každému kusu vstupenky vytvoří samostatnou vstupenku
   s náhodným kódem o 5 znacích, například AFV4X (přes 28 milionů kombinací, bez snadno zaměnitelných znaků 0/O a 1/I/L). Vstupenky vydané ve verzích 1.0 a 1.1 mají kód o 20 znacích a platí dál. Platí to pro platbu kartou
   i pro ruční označení objednávky jako zaplacené v administraci.
3. **E-mail zákazníkovi** s PDF v příloze, jedna vstupenka na stránku A4 s QR kódem, jménem,
   číslem objednávky a údaji o akci. V e-mailu je i odkaz na stránku se vstupenkami
   (`/vstupenky/<GUID objednávky>`), kde jde stáhnout PDF znovu a každý QR kód zvlášť jako obrázek.
4. **Odbavení u vstupu.** QR kód obsahuje adresu `/vstupenka/<kód>`. Obsluha ho načte běžným
   fotoaparátem v mobilu, otevře se stránka s velkým barevným stavem:
   - modrá **Platná vstupenka** se jménem, číslem vstupenky, objednávkou a stavem platby, tlačítko **Odbavit**,
   - zelená **Odbaveno** po kliknutí,
   - červená **Už použitá** s časem a jménem toho, kdo ji odbavil,
   - červená **Neplatná vstupenka** (storno, vrácení peněz, ruční zneplatnění) nebo **Neznámá vstupenka**.

   Odbavení se zapíše podmíněným příkazem v databázi, takže ani dva telefony najednou
   stejnou vstupenku dvakrát neodbaví.
5. **Kdo smí odbavovat:** přihlášený zákazník s rolí **Odbavení vstupenek** (plugin ji při instalaci
   založí) nebo administrátor. Kdokoli jiný, včetně zákazníka, který si načte svůj vlastní kód,
   uvidí jen obecnou stránku bez osobních údajů.
6. **Záloha do Google Tabulky:** seznam všech vstupenek (objednávka, jméno, e-mail, produkt, kód, stav,
   čas vytvoření a odbavení, kdo odbavil) se posílá do Google Tabulky ve Vašem účtu Google. Kdyby e-shop
   nešel, vstupenky jde zkontrolovat a odbavit podle kódu v tabulce. Viz [Záloha do Google Tabulky](#záloha-do-google-tabulky).
7. **Storno a vrácení peněz:** při stornování objednávky, zrušení platby nebo plném vrácení peněz
   se vstupenky samy zneplatní. Do poznámek objednávky se zapíše, co se stalo.

## Administrace

Konfigurace → Místní pluginy → Fermatigo vstupenky s QR kódem → Konfigurovat.

- **Přehled:** počet platných, odbavených, zbývajících a zneplatněných vstupenek.
- **Akce:** seznam akcí s produkty a počty platných, odbavených a zneplatněných vstupenek, přidání,
  úprava a smazání akce, odkaz na vstupenky akce. Akci bez vstupenek stačí potvrdit. Akci se vstupenkami
  jde smazat jen i se všemi jejími vstupenkami, po opsání přesného názvu akce a s povinným důvodem smazání
  (obojí se kontroluje i na serveru).
- **Nastavení:** zapnutí e-mailu s PDF po zaplacení.
- **Objednávka:** podle čísla objednávky vytvoří chybějící vstupenky (pro objednávky zaplacené
  před nastavením pluginu) nebo pošle e-mail se vstupenkami znovu, volitelně s jiným předmětem.
- **Poslat znovu vybraným:** v seznamu vstupenek se zaškrtnou vybrané (nebo všechny na stránce) a e-mail se pošle
  jen jejich objednávkám, jeden za objednávku se všemi jejími platnými vstupenkami, volitelně s vlastním předmětem.
- **Poslat vstupenky znovu všem:** každá objednávka s platnými vstupenkami dostane jeden e-mail se svými
  platnými vstupenkami v PDF (například připomínka před akcí). Předmět jde zadat vlastní, s proměnnými
  `{akce}` a `{objednavka}`, prázdný znamená výchozí předmět. Zneplatněné vstupenky a stornované objednávky se přeskočí.
- **Vstupenky:** hledání podle jména, e-mailu, čísla objednávky nebo kódu, ruční odbavení,
  zrušení odbavení, zneplatnění a obnovení vstupenky, nevratné smazání vstupenky (potvrzovací dialog
  s povinným důvodem smazání),
  export do Excelu (CSV). Smazání vstupenky nebo akce se zapíše do poznámek objednávky (čísla a kódy smazaných
  vstupenek, kdo je smazal a proč) a do protokolu. QR kód smazané vstupenky u vstupu ukáže „Neznámá vstupenka“.
  Když je zapnutá záloha do Google Tabulky, po smazání přijde méně vstupenek, než v tabulce je, a skript
  aktuální stav uloží na zvláštní list, původní list zůstane jako záloha.

- **Záloha do Google Tabulky:** adresa skriptu a tajný klíč, stav, čas poslední zálohy a interval, volba
  intervalu zálohy, tlačítko Zálohovat teď, obnova z tabulky, návod a hotový skript s tajným klíčem ke zkopírování.

## Záloha do Google Tabulky

Plánovaná úloha nopCommerce (Systém → Plánované úlohy, „Fermatigo vstupenky: záloha do Google Tabulky“)
v nastaveném intervalu (výchozí jednou denně) zjistí, jestli se vstupenky od minulé zálohy změnily (nová vstupenka, odbavení, zrušení odbavení,
zneplatnění, obnovení). Když ano, pošle celý seznam do Google Tabulky. Jednou za den ho pošle i beze změny,
takže čas poslední zálohy v tabulce ukazuje, že e-shop běží.

**Interval zálohy** se nastavuje přímo v pluginu (karta Záloha → Interval zálohy): každou minutu, 5, 15
nebo 30 minut, každou hodinu, 3, 6 nebo 12 hodin, jednou denně. **Výchozí je jednou denně.** Plugin interval zapíše
i do sekund plánované úlohy (jednou denně = 86 400 s), takže je vidět v Systém → Plánované úlohy. nopCommerce nový čas
úlohy převezme po restartu aplikace (tlačítko Restartovat aplikaci v administraci). Do restartu plugin sám hlídá, aby
se záloha neposílala častěji, než je nastaveno. Aktualizace pluginu restart dělá, takže po ní interval platí hned.
Interval nastavujte v pluginu: ruční změnu sekund v plánovaných úlohách plugin při uložení intervalu nebo při aktualizaci
přepíše. Tlačítko Zálohovat teď pošle zálohu hned, bez ohledu na interval.

Nastavení (jednou):

1. Na Google Disku vytvořit novou Google Tabulku.
2. V ní Rozšíření → Apps Script, vložit skript z konfigurace pluginu (je v něm tajný klíč tohoto e-shopu), uložit.
3. Nasadit → Nové nasazení → Webová aplikace, spustit jako Já, přístup Kdokoli, povolit přístup k účtu.
4. Adresu webové aplikace (`https://script.google.com/macros/s/.../exec`) vložit do konfigurace pluginu
   a kliknout na Uložit a vyzkoušet.

Co skript dělá:

- list **Vstupenky** přepíše aktuálním stavem e-shopu (všechny buňky jako text, tučná a ukotvená hlavička),
- list **Archiv vstupenek** podle kódu aktualizuje a doplňuje, ale **nikdy z něj nic nemaže**: je v něm každá vstupenka,
  která kdy v e-shopu byla. Sloupec „V e-shopu“ říká, jestli v e-shopu pořád je, „Naposledy v e-shopu“ kdy tam byla naposledy,
- list **Akce** uchovává údaje akcí (ID, název, datum, místo, poznámka, produkty), také se z něj nemaže,
- list **Info** ukazuje čas poslední zálohy, počet vstupenek, odbavených, vstupenek z archivu chybějících v e-shopu a přehled po akcích,
- požadavek bez správného tajného klíče odmítne.

Kromě sloupců pro lidi posílá plugin i technické sloupce pro obnovu (ID objednávky, položky, produktu a akce,
časy v UTC, zneplatnění).

## Obnova vstupenek z Google Tabulky

Kdyby vstupenky z e-shopu zmizely (chyba, ruční zásah do databáze, nová instalace e-shopu), obnoví se z archivu:

1. Konfigurace pluginu → Záloha do Google Tabulky → **Obnovit z Google Tabulky**.
2. Plugin načte list Archiv vstupenek a porovná ho s e-shopem. Ukáže, kolik vstupenek je v archivu, kolik v e-shopu
   a seznam chybějících (akce, objednávka, jméno, kód, stav, kdy byla naposledy v e-shopu).
3. Chybějící jsou předem zaškrtnuté. Vstupenky, které někdo smazal ručně v administraci (podle poznámky objednávky),
   jsou označené „smazaná ručně“ a předem nezaškrtnuté, aby se omylem nevrátily.
4. **Obnovit vybrané** vloží vstupenky zpět s **původním kódem** (QR kódy v e-mailech a PDF znovu platí), číslem,
   jménem, e-mailem, objednávkou, stavem odbavení a zneplatnění. Existující vstupenky se nikdy nepřepíšou.
   Akci plugin dohledá podle názvu. Když v e-shopu chybí, založí ji z listu Akce (jen s produkty, které nepatří jiné akci).
5. Do poznámek objednávky se zapíše, které vstupenky kdo obnovil, a obnova jde do protokolu.

Po nové instalaci pluginu adresa skriptu a tajný klíč zůstávají (odinstalace je nesmaže). Po nové instalaci celého
e-shopu stačí vložit adresu webové aplikace a tajný klíč ze skriptu (řádek `const SECRET`) do konfigurace pluginu.
Vstupenky obnovené bez objednávky v e-shopu jdou odbavit, jen u nich obsluha neuvidí stav platby.

Bezpečnost a soukromí:

- Přístup „Kdokoli“ znamená jen to, že skript přijme požadavek bez přihlášení. Zapisovat umí jen ten, kdo zná
  tajný klíč (48 znaků, vzniká při instalaci pluginu). Tabulka zůstává soukromá ve Vašem účtu Google.
- V tabulce jsou osobní údaje kupujících a kódy vstupenek. Tabulku sdílejte jen lidem, kteří vstupenky odbavují,
  a Google uveďte v zásadách ochrany osobních údajů jako zpracovatele.
- Chyba zálohy nikdy nezastaví e-shop ani odbavení. Zobrazí se v konfiguraci pluginu a jednou se zapíše do protokolu.

## Instalace

1. Konfigurace → Místní pluginy → Nahrát plugin nebo motiv, vybrat ZIP.
2. U pluginu Instalovat a restartovat aplikaci.
3. V konfiguraci přidat akci s ID produktu vstupenky, uložit.
4. Lidem u vstupu přidat roli Odbavení vstupenek (Zákazníci → zákazník → Role zákazníka).
   Na telefonu se přihlásí do e-shopu a pak už jen načítají QR kódy fotoaparátem.

## Poznámky

- **Internet u vstupu je nutný,** stav vstupenky se ověřuje v e-shopu.
- **Data:** vstupenky jsou v tabulce `FmEventTicket` (kód, akce, objednávka, jméno a e-mail kupujícího,
  čas odbavení a kdo odbavil), akce v tabulce `FmEvent`.
- **Vstupenky se při aktualizaci nikdy nemažou.** Aktualizace (nahrání nového ZIPu a restart) jen doplní, co nová verze
  potřebuje (nová tabulka nebo sloupec, převod dat), nic nemaže ani nepřepisuje. Od verze 1.7.1 tabulky vstupenek
  a akcí nesmaže ani odinstalace pluginu: po nové instalaci jsou vstupenky i akce zpátky. Při odinstalaci se smaže jen
  nastavení pluginu (e-mail s PDF), role Odbavení vstupenek a plánovaná úloha. Adresa a tajný klíč zálohy do Google
  Tabulky zůstanou, aby šly vstupenky po nové instalaci hned obnovit.
  Vstupenky mažou jen ruční akce v administraci (smazání vstupenky nebo akce s potvrzením a důvodem).
- **Jméno na vstupence** je jméno z fakturační adresy objednávky, u všech vstupenek objednávky stejné.
- **E-mail** jde přes výchozí e-mailový účet obchodu a frontu zpráv (Systém → Fronta zpráv).
- **PDF přílohy e-mailů** ukládá nopCommerce do databáze (tabulka Download) a po odeslání je nemaže. Plugin je proto
  sám uklízí: jednou za čas (s plánovanou úlohou zálohy, nejvýš jednou za 6 hodin) smaže PDF vstupenek z e-mailů
  odeslaných před více než 7 dny. E-maily čekající na odeslání nechává (30 dnů). Zákazník o nic nepřijde, PDF si
  kdykoli stáhne ze stránky vstupenek, kde vzniká vždy nové.
- **Export do Excelu** chrání před vzorci: hodnota začínající `=`, `+`, `-` nebo `@` (jméno zadává zákazník) dostane
  na začátek apostrof, aby ji Excel nespustil jako vzorec.
- **Souběh:** vstupenky se vytvářejí vždy jen pro jednu objednávku najednou, takže souběžná platba a ruční doplnění
  nevytvoří vstupenky dvakrát.
- **QR kódy a PDF** vznikají přímo na serveru e-shopu, žádná externí služba je negeneruje. Podrobnosti v části
  [Knihovny](#knihovny).
- Stránky `/vstupenky/...` a `/vstupenka/...` mají `noindex` a nejsou v mezipaměti.

## Knihovny

Plugin používá jen dvě skupiny knihoven:

| Knihovna | Verze | Odkud | K čemu |
|---|---|---|---|
| nopCommerce (Nop.Core, Nop.Data, Nop.Services, Nop.Web.Framework) | 4.80 | z e-shopu, v ZIPu **nejsou** | objednávky, zákazníci, e-maily, nastavení, plánované úlohy, administrace |
| FluentMigrator | 6.2.0 | z e-shopu, v ZIPu není | tabulky `FmEvent` a `FmEventTicket` a jejich úpravy při aktualizaci |
| QuestPDF (+ SkiaSharp) | 2022.12.15 | z e-shopu, v ZIPu není | PDF vstupenek, písmo FreeSerif z `App_Data/Pdf` |
| .NET 9 a ASP.NET Core | 9.0 | z e-shopu | stránky, HttpClient pro zálohu do Google Tabulky, SHA-256, náhodné kódy |
| **QRCoder** | **1.4.3** | **přibalená v ZIPu** (`QRCoder.dll`), NuGet, licence MIT | obrázek QR kódu (PNG) |

Jediná cizí knihovna v ZIPu je tedy `QRCoder.dll`. Ostatní poskytuje nopCommerce. V projektu `.csproj` jsou
odkazované s `Private=false`, takže se do ZIPu nekopírují a plugin vždy použije přesně ty, které má e-shop.
Záloha do Google Tabulky žádnou knihovnu Google nepoužívá: je to obyčejný požadavek HTTPS na Váš skript
Google Apps Script.

Verzi QRCoder určuje sestavovací skript `tools/build-plugins-4.80.py` (slovník `PACKAGES`). Projekt
`Fermatigo.EventTickets.csproj` je v instalačním i zdrojovém ZIPu, sestavení ze zdrojového ZIPu:
`dotnet build -c Release -p:NopLibs=/cesta/k/app` (`NopLibs` = složka s knihovnami nopCommerce 4.80, obsah `/app`).

Z QRCoder plugin volá jen `QRCodeGenerator.CreateQrCode(text, ECCLevel.M)` a `PngByteQRCode.GetGraphic(12)`
(funkce `QrPng` v `Services/EventTicketService.cs`). Tyhle funkce jsou stejné ve verzích 1.4 až 1.6, proto jde verze
bez úprav kódu změnit.

## Konflikt knihoven s Google Authenticatorem

**Proč může vzniknout.** Oficiální plugin nopCommerce **Google Authenticator** (dvoufázové přihlášení,
`Plugins/MultiFactorAuth.GoogleAuthenticator`) má vlastní `QRCoder.dll`. nopCommerce načítá všechny pluginy do jednoho
procesu a knihovnu se stejným názvem načte jen jednou. Když dva pluginy přinesou různé verze, nopCommerce to při startu
ohlásí jako kolizi sestav (upozornění v Místních pluginech a v protokolu) a druhý plugin dostane verzi toho prvního.
Často to funguje, ale může to skončit chybou při vytváření QR kódu nebo při přihlášení přes Google Authenticator.

**Jak je to teď.** Vstupenky mají QRCoder 1.4.3, stejnou verzi jako Google Authenticator v nopCommerce 4.80.
Kolize proto nevzniká. (Vstupenky 1.0.0 až 1.0.2 měly QRCoder 1.6.0 a kolize vznikala, opraveno ve verzi 1.0.3.)

**Co dělat, když se kolize objeví** (typicky po aktualizaci nopCommerce, která Google Authenticatoru verzi QRCoder změní):

1. Zjistit verzi: Konfigurace → Místní pluginy ukáže upozornění s názvem knihovny a oběma verzemi. Případně
   na serveru vlastnosti souboru `Plugins/MultiFactorAuth.GoogleAuthenticator/QRCoder.dll`.
2. V `tools/build-plugins-4.80.py` přepsat ve slovníku `PACKAGES` verzi QRCoder na verzi z Google Authenticatoru.
3. Zvýšit verzi pluginu (oprava, např. 1.10.0 → 1.10.1) v `plugin.json`, doplnit changelog, sestavit
   `python3 tools/build-plugins-4.80.py Fermatigo.EventTickets`.
4. Nahrát nový ZIP vstupenek a restartovat aplikaci. Upozornění na kolizi po restartu zmizí.
5. Ověřit: otevřít v administraci objednávku se vstupenkou nebo stránku `/vstupenky/<GUID objednávky>`, QR kód
   se musí zobrazit.

Rychlé dočasné řešení, když Google Authenticator nepoužíváte: odinstalovat ho a restartovat aplikaci. Vstupenky pak
používají svou knihovnu a kolize zmizí. Na vstupenky ani na zálohu do Google Tabulky to nemá vliv, Google Authenticator
s nimi nijak nesouvisí.

Kontrola po každé aktualizaci nopCommerce: Místní pluginy, žádné upozornění na kolizi u vstupenek ani u Google Authenticatoru.

## Changelog

### 1.11.0
Audit bezpečnosti, paměti a výkonu.
- Bezpečnost: export do Excelu už nepustí vzorec ze jména nebo e-mailu zákazníka (CSV injection).
- Bezpečnost: obnova z Google Tabulky bere jen kódy ve tvaru vstupenky (5 nebo 20 znaků) a texty zkracuje na délku sloupců, upravená tabulka tak nezpůsobí chybu ani nevloží nesmyslný kód.
- Bezpečnost: vlastní předmět e-mailu je vždy jeden řádek, nejvýš 200 znaků.
- Spolehlivost: vstupenky se vytvářejí jedna objednávka po druhé, souběh platby a ručního doplnění je nezdvojí.
- Databáze: úklid PDF příloh už odeslaných e-mailů se vstupenkami (dřív zůstávaly v databázi napořád, při hromadném posílání rychle rostla).
- Výkon: záloha a export načítají objednávky po dávkách 500 místo dotazu na každou objednávku zvlášť.
- Výkon: převod ze staré verze s jednou akcí se kontroluje jednou za běh aplikace, ne při každém otevření administrace, časové pásmo se hledá jen jednou.

### 1.10.1
- Interval zálohy se zapisuje i do sekund plánované úlohy (Systém → Plánované úlohy), dřív tam zůstávalo 60 s.
- Výchozí interval zálohy je jednou denně (nová instalace i aktualizace, kde interval ještě nebyl uložený).
- Denní záloha bez změn se posílá spolehlivě každý den (rezerva 5 minut).

### 1.10.0
- Záloha do Google Tabulky: interval zálohy jde nastavit přímo v pluginu (od každé minuty po jednou denně), není potřeba chodit do plánovaných úloh. Interval se ukazuje i ve stavu zálohy.

### 1.9.1
- PDF vstupenky v tiskové podobě: jen černá na bílé, bez barevných ploch a ozdob. Nahoře obchod, akce, datum a místo, pod čarou nadpis VSTUPENKA (u VIP orámovaný „VIP VSTUPENKA“), QR kód a kód, pod čarou jméno, vstupenka a objednávka, poznámka a krátké pokyny.

### 1.9.0
- VIP vstupenky: u akce pole „ID produktů VIP vstupenek“. Obsluha po načtení QR kódu VIP vstupenky hned vidí velký zlatý pruh „★ VIP ★“ a zlatý rámeček kolem stavu. VIP je také v PDF, na stránce zákazníka, v seznamu vstupenek, v exportu do Excelu a v záloze do Google Tabulky (sloupec VIP).
- Databáze: u akcí nový sloupec VipProductIds (doplní se sám při aktualizaci, nic se nemaže).

### 1.8.2
- Vstupenka už neukazuje pořadí „1 z 2“: na stránce u vstupu je jen název vstupenky (produkt), v PDF a na stránce zákazníka nadpis „Vstupenka“.

### 1.8.1
- Srozumitelná zpráva při obnově, když je v Google Tabulce ještě starý skript bez obnovy (místo „TypeError“ návod, jak skript vyměnit a nasadit novou verzi).

### 1.8.0
- Obnova vstupenek z Google Tabulky: tlačítko Obnovit z Google Tabulky porovná archiv s e-shopem a chybějící vstupenky vrátí s původními kódy a stavem. Ručně smazané jsou označené a předem nezaškrtnuté, existující se nikdy nepřepíšou, chybějící akce se založí z archivu.
- Nový skript pro Google Tabulku: list Archiv vstupenek (nic se z něj nemaže, sloupce V e-shopu a Naposledy v e-shopu), list Akce a technické sloupce pro obnovu. Starý list „Méně vstupenek“ už nevzniká. Skript je potřeba do Apps Script vložit znovu a nasadit novou verzi.
- Tajný klíč zálohy jde v konfiguraci zadat ručně (po nové instalaci e-shopu opsat ze skriptu). Adresa a klíč zálohy přežijí odinstalaci pluginu.

### 1.7.1
- Odinstalace pluginu už nesmaže tabulky vstupenek a akcí (dřív je zpětná migrace nopCommerce smazala). Nová instalace existující tabulky převezme, vstupenky i akce zůstanou.

### 1.7.0
- Povinný důvod smazání u vstupenky i u akce se vstupenkami. Důvod se zapíše do poznámky objednávky a do protokolu, server bez něj nic nesmaže.
- Mazání přes potvrzovací dialog místo okna prohlížeče: popis následků, pole pro název akce (u akce se vstupenkami) a pole Důvod smazání.

### 1.6.0
- Smazání akce i se vstupenkami: potvrzení opsáním přesného názvu akce, kontrola i na serveru. Akci bez vstupenek stačí potvrdit.
- Nevratné smazání jednotlivé vstupenky s potvrzením (tlačítko s košem v seznamu vstupenek).
- Smazané vstupenky se zapíšou do poznámek objednávky (číslo, kód, kdo smazal) a smazání do protokolu.
- Vstupenky stornované objednávky mají v seznamu, v exportu do Excelu i v záloze do Google Tabulky stav „storno“ (dřív „neplatná“).

### 1.5.1
- Když formulář akce odmítne uložení (chybí název nebo produkt patří jiné akci), vyplněné údaje zůstanou ve formuláři.

### 1.5.0
- Více akcí: seznam akcí v konfiguraci, přidání, úprava a smazání akce, každá s vlastními produkty vstupenek. Vstupenka si pamatuje svou akci a PDF, e-mail, stránka zákazníka i stránka u vstupu ukazují údaje její akce. Objednávka se vstupenkami na více akcí dostane jeden e-mail s přehledem akcí.
- Při aktualizaci se dosavadní akce z nastavení převede na první akci a stávající vstupenky se k ní přiřadí.
- Seznam vstupenek, export do Excelu a hromadné opětovné odeslání jdou filtrovat podle akce. Export i záloha do Google Tabulky mají nový první sloupec Akce, list Info v tabulce ukazuje přehled po akcích (skript z konfigurace je potřeba vložit znovu).
- Stránka u vstupu ukazuje obsluze akci načtené vstupenky. Ostatním ukazuje jedinou akci e-shopu, při více akcích obecně „Vstupenka“, aby podle stránky nešlo poznat, jestli kód existuje.

### 1.4.0
- Poslat vstupenky znovu jen vybraným: zaškrtávací políčka v seznamu vstupenek (i výběr všech na stránce), počet vybraných na tlačítku, potvrzení a volitelný vlastní předmět. Každá vybraná objednávka dostane jeden e-mail.

### 1.3.0
- Hromadné opětovné odeslání vstupenek všem zákazníkům s platnými vstupenkami, jeden e-mail na objednávku, s potvrzením.
- Vlastní předmět e-mailu při opětovném odeslání (hromadném i u jedné objednávky), s proměnnými `{akce}` a `{objednavka}`.

### 1.2.1
- Stránka vstupenky pro nepřihlášené vypadá stejně pro existující i neexistující kód (název obchodu se ukazuje vždy), takže podle ní nejde poznat, jestli kód existuje.

### 1.2.0
- Kód nových vstupenek má 5 znaků místo 20 (například AFV4X), jde snadno přečíst a přepsat. Už vydané vstupenky s 20místným kódem platí dál.
- Obrázek QR kódu jde stáhnout jen přes odkaz objednávky (`/vstupenky/<GUID objednávky>/qr/<kód>.png`). Původní adresa `/vstupenka/qr/<kód>.png` prozrazovala, jestli kód existuje, a krátké kódy by šlo zkoušet naslepo. Stránka vstupenky po načtení QR kódu dál ukazuje údaje jen obsluze.
- Nový kód se před uložením kontroluje, aby se neopakoval.

### 1.1.0
- Záloha vstupenek do Google Tabulky přes Google Apps Script: každou minutu při změně a jednou denně, s ochranou tajným klíčem a pojistkou proti přepsání dat menším seznamem. Nastavení, stav, návod a skript ke zkopírování jsou v konfiguraci pluginu.

### 1.0.3
- Knihovna QRCoder ve verzi 1.4.3, stejné jako v pluginu Google Authenticator. Na e-shopu s nainstalovaným Google Authenticatorem už nevzniká kolize sestav.

### 1.0.2
- Stránka se vstupenkami zákazníka místo čísla objednávky a počtu vstupenek ukazuje název vstupenky podle produktu.

### 1.0.1
- Opravené ukládání nastavení akce: prázdná nepovinná pole (například poznámka) už nezablokují tlačítko Uložit.

### 1.0.0
- První verze: vstupenky s vlastním QR kódem po zaplacení, PDF v e-mailu, stažení QR kódu do telefonu,
  odbavení načtením QR kódu, zneplatnění při stornu a vrácení peněz, přehled a export v administraci.
