using TavlaJules.App.Models;

namespace TavlaJules.App.Services;

public sealed class PhasePlanService
{
    public IReadOnlyList<PhaseItem> CreateDefaultPlan(ProjectSettings settings)
    {
        return
        [
            new PhaseItem
            {
                Order = 1,
                Title = "Hedef ve ayarlar",
                Description = $"Proje klasoru, Jules adresi ve OpenRouter modeli sabitlenir. Model: {settings.OpenRouterModel}",
                IsDone = true
            },
            new PhaseItem
            {
                Order = 2,
                Title = "Tavla kural motoru",
                Description = "Zar, pul hareketi, kirma, kapali hane, toplama ve kazanan kontrolu icin test edilebilir cekirdek."
            },
            new PhaseItem
            {
                Order = 3,
                Title = "Telefon arayuzu gorevleri",
                Description = "Jules'e verilecek mobil ekran, oyun akisi ve state yonetimi gorevleri parcalara ayrilir."
            },
            new PhaseItem
            {
                Order = 4,
                Title = "Jules otomasyon akisi",
                Description = "Gorev promptlari, beklenen dosya degisiklikleri ve teslim kontrol listesi tek ekrandan izlenir."
            },
            new PhaseItem
            {
                Order = 5,
                Title = "AI kontrol raporu",
                Description = "OpenRouter modeliyle kod, tavla kurallari ve mobil deneyim icin denetim raporu uretilir."
            },
            new PhaseItem
            {
                Order = 6,
                Title = "EXE paketleme",
                Description = "Kontrol paneli Windows icin publish edilir ve surum notu kayda gecirilir."
            }
        ];
    }
}
