# App MySQL Integration

`TavlaJules.App` katmani, `TavlaJules.Data` icindeki `MySqlGameRepository` sinifini WinForms/CLI orchestration tarafindan kullanilabilir hale getirir.

## Bilesenler
- `MySqlConnectionFactory`, `IDbConnectionFactory` uygular ve `.env` icinden once `TAVLA_ONLINE_MYSQL`, yoksa `AJANLARIM_MYSQL` degerini okur.
- `GamePersistenceService`, `MySqlGameRepository` uzerindeki `SaveSnapshotAsync`, `LoadSnapshotAsync`, `SaveMoveSequenceAsync` ve `SaveDiceRollAsync` metotlarini app katmani icin sarar.
- `Program.cs`, headless DB setup akisi sirasinda repository zincirinin kurulabilir oldugunu kontrol eder.

## Mimari
- `TavlaJules.Engine` saf kural motoru olarak kalir ve Data/App dependency almaz.
- Connection string veya gizli degerler kaynak koda yazilmaz; yalnizca `.env` uzerinden okunur.
