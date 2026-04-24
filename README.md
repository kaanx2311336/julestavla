# TavlaJules

C# WinForms tabanli TavlaJules kontrol paneli.

Ilk hedef:
- Jules ile telefon icin tavla uygulamasi yaptirma surecini fazlara ayirmak.
- OpenRouter model ayarlarini tek yerde takip etmek.
- GitHub reposu `kaanx2311336/julestavla` uzerinden Jules remote session baslatmak.
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

## Jules

```powershell
jules new --repo kaanx2311336/julestavla "repoyu incele ve tavla kural motoru icin ilk uygulanabilir fazi raporla"
```
