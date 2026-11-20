# NopCommerce-Plugins

Pluginy pro nopCommerce 4.80, autor Tom Tomčo.

| Plugin | Verze | Popis |
|---|---|---|
| [Fermatigo.EventTickets](Fermatigo.EventTickets/) | 1.11.0 | Vstupenky na akce s QR kódem: PDF v e-mailu, odbavení mobilem u vstupu, VIP vstupenky, záloha a obnova z Google Tabulky |

Úplný popis, nastavení a changelog jsou v [README pluginu](Fermatigo.EventTickets/README.md).

## Instalace hotového pluginu

Ve složce [instalace](instalace/) je hotový ZIP aktuální verze, sestavený pro nopCommerce 4.80:
[Fermatigo.EventTickets-4.80-1.11.0.zip](instalace/Fermatigo.EventTickets-4.80-1.11.0.zip).

1. V administraci Konfigurace → Místní pluginy → Nahrát plugin nebo motiv, vybrat ZIP.
2. U pluginu kliknout na Instalovat (při aktualizaci tento krok odpadá) a restartovat aplikaci.

Aktualizace vstupenky ani nastavení nemaže.

## Sestavení

Potřebujete .NET SDK 9 a knihovny nopCommerce 4.80 (obsah složky `/app` z instalace e-shopu).

```bash
cd Fermatigo.EventTickets
dotnet build -c Release -p:NopLibs=/cesta/k/nopcommerce/app
```

Výsledek je ve složce `out/`. Pro nahrání v administraci (Konfigurace → Místní pluginy → Nahrát plugin nebo motiv)
zabalte obsah `out/` do ZIPu s jednou kořenovou složkou `Fermatigo.EventTickets/`.

## Knihovny

Jediná knihovna, kterou plugin nese s sebou, je **QRCoder 1.4.3** (NuGet, licence MIT). Verze je v souboru
`Fermatigo.EventTickets.csproj` (`PackageReference`) a musí být stejná jako v oficiálním pluginu nopCommerce
Google Authenticator, jinak nopCommerce hlásí kolizi sestav. Postup při kolizi je v README pluginu, v tomto
repozitáři se verze mění přímo v `.csproj` (zmínky o skriptu `tools/build-plugins-4.80.py` se týkají interního
sestavovacího nástroje autora).

## Changelog repozitáře

- 2026-11-20: Fermatigo.EventTickets 1.11.0, přidán instalační ZIP
