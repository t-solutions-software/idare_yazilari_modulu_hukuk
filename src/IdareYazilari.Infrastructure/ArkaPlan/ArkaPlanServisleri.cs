using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Infrastructure.ArkaPlan;

public class ArkaPlanAyarlari
{
    public int ToplamaAraligiSaniye { get; set; } = 120;
    public int HatirlatmaAraligiSaniye { get; set; } = 60;
    public int GorevAktarimAraligiSaniye { get; set; } = 120;
}

/// <summary>UETS / KEP kaynaklarını periyodik olarak tarar ve yeni yazıları kaydeder.</summary>
public class GelenYaziToplamaServisi(IServiceScopeFactory kapsam, IOptions<ArkaPlanAyarlari> ayar, ILogger<GelenYaziToplamaServisi> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var zamanlayici = new PeriodicTimer(TimeSpan.FromSeconds(ayar.Value.ToplamaAraligiSaniye));
        do
        {
            try { await BirTurAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Gelen yazı toplama turu başarısız"); }
        } while (await zamanlayici.WaitForNextTickAsync(ct));
    }

    public async Task BirTurAsync(CancellationToken ct)
    {
        using var scope = kapsam.CreateScope();
        foreach (var kaynak in scope.ServiceProvider.GetServices<IGelenYaziKaynagi>())
        {
            foreach (var (anahtar, paket) in await kaynak.YenileriGetirAsync(ct))
            {
                // Her yazı ayrı kapsamda (ayrı DbContext) kaydedilir; biri hata verirse diğerleri etkilenmez.
                using var yaziKapsami = kapsam.CreateScope();
                try
                {
                    await yaziKapsami.ServiceProvider.GetRequiredService<YaziKayitServisi>().KaydetAsync(paket, ct);
                    await kaynak.IslendiAsync(anahtar, true, null, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await kaynak.IslendiAsync(anahtar, false, ex.Message, ct);
                }
            }
        }
    }
}

/// <summary>Zamanı gelen hatırlatmaları periyodik olarak gönderir.</summary>
public class HatirlatmaZamanlayici(IServiceScopeFactory kapsam, IOptions<ArkaPlanAyarlari> ayar, ILogger<HatirlatmaZamanlayici> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var zamanlayici = new PeriodicTimer(TimeSpan.FromSeconds(ayar.Value.HatirlatmaAraligiSaniye));
        do
        {
            try
            {
                using var scope = kapsam.CreateScope();
                var adet = await scope.ServiceProvider.GetRequiredService<HatirlatmaGonderimServisi>().CalistirAsync(ct);
                if (adet > 0) log.LogInformation("{Adet} hatırlatma gönderildi", adet);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Hatırlatma turu başarısız"); }
        } while (await zamanlayici.WaitForNextTickAsync(ct));
    }
}

/// <summary>Yazılardan üretilen görevleri periyodik olarak operasyon modülüne aktarır (Http modunda).</summary>
public class GorevAktarimZamanlayici(IServiceScopeFactory kapsam, IOptions<ArkaPlanAyarlari> ayar, ILogger<GorevAktarimZamanlayici> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var zamanlayici = new PeriodicTimer(TimeSpan.FromSeconds(ayar.Value.GorevAktarimAraligiSaniye));
        do
        {
            try
            {
                using var scope = kapsam.CreateScope();
                var adet = await scope.ServiceProvider.GetRequiredService<GorevAktarimServisi>().CalistirAsync(ct);
                if (adet > 0) log.LogInformation("{Adet} görev operasyon modülüne aktarıldı", adet);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Görev aktarım turu başarısız"); }
        } while (await zamanlayici.WaitForNextTickAsync(ct));
    }
}
