using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Services;
using Microsoft.Extensions.Logging;

namespace IdareYazilari.Application.Analiz;

/// <summary>
/// Yazıyı analiz eder: yapay zekâ yapılandırılmışsa önce onu dener, kural tabanlı analizi her zaman
/// çapraz kontrol için çalıştırır. İki yöntem farklı son gün bulursa ihtiyatlı olarak erken tarihi seçer
/// ve süre teyidi ister.
/// </summary>
public class YaziAnalizServisi(
    KuralTabanliAnalizci kural,
    IEnumerable<IYapayZekaAnalizcisi> yapayZeka,
    SureHesaplayici hesaplayici,
    ILogger<YaziAnalizServisi> log)
{
    public async Task<AnalizSonucu> AnalizEtAsync(AnalizGirdisi girdi, CancellationToken ct = default)
    {
        HamAnaliz? ai = null;
        foreach (var analizci in yapayZeka)
        {
            try
            {
                ai = await analizci.AnalizEtAsync(girdi, ct);
                if (ai is not null) break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Yapay zekâ analizi başarısız; kural tabanlı analize düşülüyor.");
            }
        }

        // Metin yalnızca ekteydi ve yapay zekâ çıkardıysa kural analizini o metinle çalıştır.
        var kuralGirdisi = string.IsNullOrWhiteSpace(girdi.Metin) && !string.IsNullOrWhiteSpace(ai?.CikarilanMetin)
            ? girdi with { Metin = ai.CikarilanMetin! }
            : girdi;
        var kuralSonucu = await kural.AnalizEtAsync(kuralGirdisi, ct);

        if (ai is null && kuralSonucu is null)
        {
            return new AnalizSonucu(
                girdi.KonuIpucu ?? "(Konu tespit edilemedi)",
                "Yazı metni okunamadı (taranmış görüntü veya desteklenmeyen ek olabilir). Lütfen ekleri inceleyip bilgileri elle girin.",
                hesaplayici.SonGunHesapla(girdi.TebligTarihi, CevapSuresiIfadesi.Bulunamadi),
                AnalizKaynagi.Kural, null, null, null, null);
        }

        var asil = ai ?? kuralSonucu!;
        var sonGun = hesaplayici.SonGunHesapla(girdi.TebligTarihi, asil.CevapSuresi);

        if (ai is not null && kuralSonucu is not null && kuralSonucu.CevapSuresi.Tur != CevapSuresiTuru.Bulunamadi)
        {
            var kuralSonGun = hesaplayici.SonGunHesapla(girdi.TebligTarihi, kuralSonucu.CevapSuresi);
            if (ai.CevapSuresi.Tur == CevapSuresiTuru.Bulunamadi)
            {
                sonGun = kuralSonGun;
            }
            else if (kuralSonGun.SonGun != sonGun.SonGun)
            {
                var erken = kuralSonGun.SonGun < sonGun.SonGun ? kuralSonGun : sonGun;
                sonGun = new SonGunSonucu(erken.SonGun, true,
                    $"Yapay zekâ ({sonGun.SonGun:dd.MM.yyyy}: {ai.CevapSuresi.KaynakMetin}) ile kural analizi " +
                    $"({kuralSonGun.SonGun:dd.MM.yyyy}: {kuralSonucu.CevapSuresi.KaynakMetin}) farklı son gün buldu; " +
                    "erken tarih seçildi, teyit edilmeli.");
            }
        }

        return new AnalizSonucu(
            Konu: asil.Konu,
            Ozet: asil.Ozet,
            SonGun: sonGun,
            Kaynak: asil.Kaynak,
            GonderenIdare: asil.GonderenIdare ?? kuralSonucu?.GonderenIdare,
            GelenSayi: asil.GelenSayi ?? kuralSonucu?.GelenSayi,
            YaziTarihi: asil.YaziTarihi ?? kuralSonucu?.YaziTarihi,
            CikarilanMetin: ai?.CikarilanMetin);
    }
}
