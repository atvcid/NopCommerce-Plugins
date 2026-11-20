# NopCommerce-Plugins

Pluginy pro nopCommerce 4.80, autor Tom Tomčo.

| Plugin | Verze | Popis | Zdrojový kód | Instalační ZIP |
|---|---|---|---|---|
| Fermatigo vstupenky s QR kódem | 1.11.0 | Vstupenky na akce s QR kódem: PDF v e-mailu, odbavení mobilem u vstupu, VIP vstupenky, záloha a obnova z Google Tabulky | [Fermatigo.EventTickets](Fermatigo.EventTickets/) | [ZIP](instalace/Fermatigo.EventTickets-4.80-1.11.0.zip) |

Úplný popis, nastavení a changelog každého pluginu jsou v jeho vlastním README
([vstupenky](Fermatigo.EventTickets/README.md)).

## Uspořádání repozitáře

- **Každý plugin má vlastní složku** pojmenovanou podle systémového názvu (například `Fermatigo.EventTickets/`).
  V ní jsou zdrojové kódy, projekt `.csproj` a README s popisem a changelogem pluginu.
- **Složka `instalace/`** obsahuje hotové ZIPy k nahrání do nopCommerce, od každého pluginu jen aktuální verzi.
  Název ZIPu je `<SystémovýNázev>-<verze nopCommerce>-<verze pluginu>.zip`.
- **Tabulka nahoře** je přehled všech pluginů s aktuální verzí a odkazy.
- **Verze** se značí tagem `<SystémovýNázev>-v<verze>` (první vydání vstupenek má ještě tag `v1.11.0`).

## Instalace hotového pluginu

Ve složce [instalace](instalace/) jsou hotové ZIPy aktuálních verzí, sestavené pro nopCommerce 4.80.

1. V administraci Konfigurace → Místní pluginy → Nahrát plugin nebo motiv, vybrat ZIP.
2. U pluginu kliknout na Instalovat (při aktualizaci tento krok odpadá) a restartovat aplikaci.

Aktualizace vstupenky ani nastavení nemaže.

## Sestavení

Potřebujete .NET SDK 9 a knihovny nopCommerce 4.80 (obsah složky `/app` z instalace e-shopu).

```bash
cd Fermatigo.EventTickets   # složka pluginu
dotnet build -c Release -p:NopLibs=/cesta/k/nopcommerce/app
```

Výsledek je ve složce `out/`. Pro nahrání v administraci (Konfigurace → Místní pluginy → Nahrát plugin nebo motiv)
zabalte obsah `out/` do ZIPu s jednou kořenovou složkou pojmenovanou jako plugin (například `Fermatigo.EventTickets/`).

## Knihovny

Vstupenky: jediná knihovna, kterou plugin nese s sebou, je **QRCoder 1.4.3** (NuGet, licence MIT). Verze je v souboru
`Fermatigo.EventTickets.csproj` (`PackageReference`) a musí být stejná jako v oficiálním pluginu nopCommerce
Google Authenticator, jinak nopCommerce hlásí kolizi sestav. Postup při kolizi je v README pluginu, v tomto
repozitáři se verze mění přímo v `.csproj` (zmínky o skriptu `tools/build-plugins-4.80.py` se týkají interního
sestavovacího nástroje autora).

## Changelog repozitáře

- 2026-11-20: Fermatigo.EventTickets 1.11.0, přidán instalační ZIP, popsáno uspořádání repozitáře
