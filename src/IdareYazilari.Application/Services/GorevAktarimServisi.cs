using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace IdareYazilari.Application.Services;

/// <summary>
/// Yazılardan üretilen görevleri operasyon modülüne aktarır. Operasyon modülünde görevler
/// SourceSystem = "IdareYazilari" ve SourceKey = görevin dış anahtarı ile tekil tutulur; tekrar aktarım güvenlidir.
/// </summary>
public class GorevAktarimServisi(IYaziDeposu depo, IGorevAktarici aktarici, ISaat saat, ILogger<GorevAktarimServisi> log)
{
    public const string KaynakSistem = "IdareYazilari";
    public const int AzamiDeneme = 10;

    public async Task<int> CalistirAsync(CancellationToken ct = default)
    {
        if (!aktarici.Etkin) return 0;
        var adet = 0;
        foreach (var yaziId in await depo.AktarilacakGorevYazilariAsync(AzamiDeneme, ct))
        {
            var y = await depo.GetirAsync(yaziId, ct);
            if (y is null) continue;
            foreach (var g in y.Gorevler.Where(g => g.AktarimDurumu != AktarimDurumu.Aktarildi && g.DenemeSayisi < AzamiDeneme))
            {
                try
                {
                    await aktarici.AktarAsync(Paket(y.EvrakNo, y.GelenSayi, g), ct);
                    g.AktarimDurumu = AktarimDurumu.Aktarildi;
                    g.AktarimTarihi = saat.Simdi;
                    g.Hata = null;
                    adet++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    g.AktarimDurumu = AktarimDurumu.Hata;
                    g.DenemeSayisi++;
                    g.Hata = ex.Message;
                    log.LogWarning(ex, "Görev operasyon modülüne aktarılamadı: {Anahtar}", g.DisAnahtar);
                }
            }
            await depo.KaydetAsync(ct);
        }
        return adet;
    }

    /// <summary>Çekme (pull) entegrasyonunda operasyon modülü görevi aldığını bildirir.</summary>
    public async Task<bool> AlindiAsync(string disAnahtar, CancellationToken ct = default)
    {
        var parca = disAnahtar.Split('/');
        if (parca.Length != 2 || !Guid.TryParse(parca[0], out var yaziId)) return false;
        var y = await depo.GetirAsync(yaziId, ct);
        var g = y?.Gorevler.FirstOrDefault(x => x.DisAnahtar == disAnahtar);
        if (g is null) return false;
        g.AktarimDurumu = AktarimDurumu.Aktarildi;
        g.AktarimTarihi = saat.Simdi;
        g.Hata = null;
        await depo.KaydetAsync(ct);
        return true;
    }

    public static GorevAktarimPaketi Paket(string evrakNo, string? gelenSayi, Domain.Entities.YaziGorevi g) => new(
        KaynakSistem, g.DisAnahtar, g.Baslik,
        string.Join("\n", new[] { g.Aciklama, $"İdare yazısı: {evrakNo}" + (gelenSayi is null ? "" : $" (sayı {gelenSayi})") }.Where(x => x is not null)),
        g.SorumluEposta, g.Lokasyon, g.Termin, evrakNo);
}
