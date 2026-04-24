# TavlaJules

C# WinForms tabanli TavlaJules kontrol paneli.

Ilk hedef:
- Jules ile telefon icin tavla uygulamasi yaptirma surecini fazlara ayirmak.
- OpenRouter model ayarlarini tek yerde takip etmek.
- GitHub reposu `kaanx2311336/julestavla` uzerinden Jules remote session baslatmak.
- `openai/gpt-oss-120b:free` ajan modelini dakika basi Jules durumunu izlemek ve sonraki promptu tasarlamak icin kullanmak.
- Aiven `tavla_online` MySQL baglantisini `.env` icindeki `TAVLA_ONLINE_MYSQL` ile test etmek.
- EXE olarak Windows'ta calisan bir kontrol paneli uretmek.

## Calistirma

```powershell
dotnet run --project .\src\TavlaJules.App\TavlaJules.App.csproj
```

## EXE uretme

```powershell
dotnet publish .\src\TavlaJules.App\TavlaJules.App.csproj -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true /p:PublishDir=.\publish\
```

API anahtarini `.env` icinde `OPENROUTER_API_KEY` olarak tut.

`AJANLARIM_MYSQL` ornek bicim:

```text
mysql://USER:PASSWORD@HOST:PORT/ajanlarim?ssl-mode=REQUIRED
```

SQL rapor semasini WinForms acmadan kurmak:

```powershell
dotnet run --project .\src\TavlaJules.App\TavlaJules.App.csproj -- --db-setup
```

Tek ajan turunu WinForms acmadan calistirmak:

```powershell
dotnet run --project .\src\TavlaJules.App\TavlaJules.App.csproj -- --agent-once
```

## Dakikalik ajan

Paneldeki `Ajan baslat` dugmesi, her 60 saniyede:
- Jules session listesini okur.
- Izlenen session tamamlandiysa `jules remote pull --session <id>` ile sonucu ceker, uygulamaz.
- `prodetayi/` ve `yapilanlar/` hafizasini OpenRouter ajanina verir.
- Sonraki Jules promptunu tasarlar ve `agent_reports/` altina JSON raporu yazar.
- Aiven `ajanlarim` DB icinde `agent_runs` ve `agent_events` tablolarina rapor yazar.
- Otomatik yeni Jules gorevi acma kutusu isaretli degilse sadece onerir.

## Jules

```powershell
jules new --repo kaanx2311336/julestavla "repoyu incele ve tavla kural motoru icin ilk uygulanabilir fazi raporla"
```
