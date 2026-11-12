using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IdareYazilari.Application.Services;

/// <summary>Zamanı gelen hatırlatmaları alıcılarına (kullanıcı / proje müdürü / hukuk ekibi) gönderir.</summary>
public class HatirlatmaGonderimServisi(
    IYaziDeposu depo,
    IKullaniciDeposu kullanicilar,
    IEnumerable<IBildirimGonderici> gondericiler,
    ISaat saat,
    ILogger<HatirlatmaGonderimServisi> log)
{
    public async Task<int> CalistirAsync(CancellationToken ct = default)
    {
        var simdi = saat.Simdi;
        var gonderilen = 0;
        foreach (var yaziId in await depo.HatirlatmasiGelenYazilarAsync(simdi, ct))
        {
            var yazi = await depo.GetirAsync(yaziId, ct);
            if (yazi is null) continue;

            foreach (var h in yazi.Hatirlatmalar.Where(h => h.Bekliyor && h.Zaman <= simdi).OrderBy(h => h.Zaman))
            {
                foreach (var alici in await AlicilariCozAsync(h, yazi, ct))
                foreach (var gonderici in gondericiler)
                {
                    try
                    {
                        await gonderici.GonderAsync(new Bildirim(alici, $"İdare Yazısı: {yazi.Konu}", h.Mesaj, yazi.Id), ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        log.LogError(ex, "Hatırlatma gönderilemedi: {Gonderici} → {Alici}", gonderici.GetType().Name, alici.Eposta);
                    }
                }
                h.GonderimZamani = simdi;
                gonderilen++;
            }
            await depo.KaydetAsync(ct);
        }
        return gonderilen;
    }

    private async Task<IReadOnlyList<Kullanici>> AlicilariCozAsync(Hatirlatma h, IdareYazisi yazi, CancellationToken ct)
    {
        switch (h.AliciTipi)
        {
            case AliciTipi.Kullanici when h.AliciKullaniciId is { } id:
                return await kullanicilar.GetirAsync(id, ct) is { Aktif: true } u ? [u] : [];

            case AliciTipi.HukukEkibi:
                return (await kullanicilar.ListeleAsync(rol: KullaniciRolu.Hukuk, ct: ct)).Where(u => u.Aktif).ToList();

            case AliciTipi.ProjeMuduru:
                if (yazi.ProjeId is null)
                    return (await kullanicilar.ListeleAsync(rol: KullaniciRolu.Yonetici, ct: ct)).Where(u => u.Aktif).ToList();
                var proje = await kullanicilar.ProjeGetirAsync(yazi.ProjeId.Value, ct);
                if (proje?.ProjeMuduruId is { } pmId && await kullanicilar.GetirAsync(pmId, ct) is { Aktif: true } pm)
                    return [pm];
                return (await kullanicilar.ListeleAsync(yazi.ProjeId, KullaniciRolu.ProjeMuduru, ct)).Where(u => u.Aktif).ToList();

            default:
                return [];
        }
    }
}
