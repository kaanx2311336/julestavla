# TavlaJules.Data & Persistence
Tavla oyununun online persistence katmanidir. ORM (EF Core) kullanilmaz, parametreli SQL sorgulari yazilarak `MySqlConnector` araciligi ile oyun verisi Aiven MySQL veritabanina kaydedilir.

## Bilesenler
- **TavlaJules.Data.csproj:** MySQL erisimi icin `MySqlConnector` paketi referans alir.
- **GameStateRepository:** Oyun anlik snapshot bilgilerini (board, bar, turn vs) JSON formatinda tablolara isler.
- **Migrations:** `migrations/` dizininde raw, idempotent SQL dosyalari olarak (.sql) sema guncellemeleri yer alir (ornek: `001_initial_schema.sql`).
