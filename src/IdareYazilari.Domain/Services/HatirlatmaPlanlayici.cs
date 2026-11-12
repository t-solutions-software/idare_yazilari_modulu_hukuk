using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;

namespace IdareYazilari.Domain.Services;

public sealed record PlanlananHatirlatma(
    HatirlatmaTuru Tur,
    DateTimeOffset Zaman,
    AliciTipi AliciTipi,
    Guid? AliciKullaniciId,
    string Mesaj);

/// <summary>
/// Yazının mevcut durumuna göre hangi hatırlatmaların hangi zamanda kime gitmesi gerektiğini belirler.
/// Zamanlar yazının sabit alanlarından türetilir (deterministik), böylece yeniden planlama aynı kayıtları üretir.
/// </summary>
public class HatirlatmaPlanlayici(SureAyarlari ayarlar)
{
    private static readonly TimeOnly SabahSaati = new(9, 0);

    public IEnumerable<PlanlananHatirlatma> Planla(IdareYazisi y)
    {
        if (y.Kapali) yield break;

        var on = TimeSpan.FromHours(ayarlar.OnHatirlatmaSaat);
        var baslik = $"[{y.GonderenIdare}] {Kisalt(y.Konu, 80)}";
        var sonGunSabahi = new DateTimeOffset(y.CevapSonGunu.ToDateTime(SabahSaati), SureAyarlari.TurkiyeSaatFarki);
        var sorumlu = y.SorumluId;

        IEnumerable<PlanlananHatirlatma> PmVeSorumlu(HatirlatmaTuru tur, DateTimeOffset zaman, string mesaj)
        {
            yield return new(tur, zaman, AliciTipi.ProjeMuduru, null, mesaj);
            if (sorumlu is { } s) yield return new(tur, zaman, AliciTipi.Kullanici, s, mesaj);
        }

        if (y.SureTeyitGerekli)
            yield return new(HatirlatmaTuru.SureTeyidiGerekli, y.OlusturmaTarihi, AliciTipi.ProjeMuduru, null,
                $"{baslik}: cevap süresi metinden kesin tespit edilemedi, lütfen son günü teyit edin. ({y.SureAciklamasi})");

        // Bir istisna bu durum değişikliğiyle sonuçlandıysa (ret / ek süre yanıtı / gecikme bildirimi) ilgilileri bilgilendir.
        var sonIstisna = y.Istisnalar.MaxBy(i => i.IdareYanitTarihi ?? i.KararTarihi ?? DateTimeOffset.MinValue);
        if (sonIstisna is not null && (sonIstisna.IdareYanitTarihi ?? sonIstisna.KararTarihi) == y.DurumTarihi
            && y.Durum != YaziDurumu.EkSureYanitiBekleniyor)
        {
            var sonuc = (sonIstisna.Durum, sonIstisna.IdareYaniti) switch
            {
                (IstisnaDurumu.Reddedildi, _) => $"hukuk süre istisnasını uygun bulmadı ({sonIstisna.HukukAciklamasi}); asıl süre geçerli",
                (IstisnaDurumu.EkSureTalepEdildi, EkSureYaniti.Kabul) => "idare ek süreyi kabul etti",
                (IstisnaDurumu.EkSureTalepEdildi, EkSureYaniti.Ret) => "idare ek süre talebini REDDETTİ; asıl süre geçerli",
                (IstisnaDurumu.GecikmeBildirildi, _) => "gecikme gerekçesi idareye bildirildi",
                _ => "süre istisnası sonuçlandı",
            };
            foreach (var h in PmVeSorumlu(HatirlatmaTuru.IstisnaSonuclandi, y.DurumTarihi,
                         $"{baslik}: {sonuc}. Cevap son günü {y.CevapSonGunu:dd.MM.yyyy}, taslak son tarihi {y.TaslakSonTarihi:dd.MM.yyyy HH:mm}."))
                yield return h;
        }

        switch (y.Durum)
        {
            case YaziDurumu.AtamaBekliyor:
                for (var i = 0; i < 4; i++)
                {
                    var zaman = y.DurumTarihi + TimeSpan.FromHours(ayarlar.AtamaHatirlatmaSaat * i);
                    if (zaman >= y.TaslakSonTarihi) break;
                    yield return new(HatirlatmaTuru.AtamaBekliyor, zaman, AliciTipi.ProjeMuduru, null,
                        $"{baslik}: yeni idare yazısı sorumlu ataması bekliyor. Cevap son günü {y.CevapSonGunu:dd.MM.yyyy}.");
                }
                break;

            case YaziDurumu.TaslakHazirlaniyor:
                if (sorumlu is { } s1)
                {
                    if (y.AtamaTarihi is { } atama)
                        yield return new(HatirlatmaTuru.SorumluAtandi, atama, AliciTipi.Kullanici, s1,
                        $"{baslik}: cevap taslağı hazırlamanız bekleniyor. Taslak son tarihi {y.TaslakSonTarihi:dd.MM.yyyy HH:mm}.");
                    yield return new(HatirlatmaTuru.TaslakSuresiYaklasiyor, y.TaslakSonTarihi - on, AliciTipi.Kullanici, s1,
                        $"{baslik}: taslak süresi {y.TaslakSonTarihi:dd.MM.yyyy HH:mm} tarihinde doluyor.");
                    foreach (var h in PmVeSorumlu(HatirlatmaTuru.TaslakSuresiDoldu, y.TaslakSonTarihi,
                                 $"{baslik}: taslak süresi doldu, taslak hâlâ hukuka gönderilmedi."))
                        yield return h;
                }
                break;

            case YaziDurumu.RevizyonBekleniyor:
                if (sorumlu is { } s2)
                {
                    yield return new(HatirlatmaTuru.RevizyonTalepEdildi, y.DurumTarihi, AliciTipi.Kullanici, s2,
                        $"{baslik}: hukuk taslak için revizyon talep etti. Hukuk süresi {y.HukukSonTarihi:dd.MM.yyyy HH:mm}.");
                    yield return new(HatirlatmaTuru.HukukSuresiYaklasiyor, y.HukukSonTarihi - on, AliciTipi.Kullanici, s2,
                        $"{baslik}: revize taslağın hukuka dönmesi gereken süre yaklaşıyor.");
                }
                break;

            case YaziDurumu.HukukIncelemesinde:
                yield return new(HatirlatmaTuru.HukukIncelemesiBekliyor, y.DurumTarihi, AliciTipi.HukukEkibi, null,
                    $"{baslik}: taslak hukuk incelemesine geldi. Hukuk son tarihi {y.HukukSonTarihi:dd.MM.yyyy HH:mm}.");
                yield return new(HatirlatmaTuru.HukukSuresiYaklasiyor, y.HukukSonTarihi - on, AliciTipi.HukukEkibi, null,
                    $"{baslik}: hukuk inceleme süresi {y.HukukSonTarihi:dd.MM.yyyy HH:mm} tarihinde doluyor.");
                yield return new(HatirlatmaTuru.HukukSuresiDoldu, y.HukukSonTarihi, AliciTipi.HukukEkibi, null,
                    $"{baslik}: hukuk inceleme süresi doldu.");
                yield return new(HatirlatmaTuru.HukukSuresiDoldu, y.HukukSonTarihi, AliciTipi.ProjeMuduru, null,
                    $"{baslik}: hukuk inceleme süresi doldu.");
                break;

            case YaziDurumu.HukukOnayladi:
                foreach (var h in PmVeSorumlu(HatirlatmaTuru.GonderimBekliyor, y.DurumTarihi,
                             $"{baslik}: hukuk onayı verildi, cevabın {y.CevapSonGunu:dd.MM.yyyy} tarihine kadar idareye gönderilmesi gerekiyor."))
                    yield return h;
                break;

            case YaziDurumu.IstisnaHukukIncelemesinde:
                yield return new(HatirlatmaTuru.IstisnaIncelemesiBekliyor, y.DurumTarihi, AliciTipi.HukukEkibi, null,
                    $"{baslik}: süresinde cevap verilemeyeceği bildirildi; neden incelenmeli ve ek süre talebi / gecikme bildirimi yapılmalı. Cevap son günü {y.CevapSonGunu:dd.MM.yyyy}.");
                break;

            case YaziDurumu.EkSureYanitiBekleniyor:
                yield return new(HatirlatmaTuru.EkSureYanitiTakibi, y.DurumTarihi + TimeSpan.FromDays(2), AliciTipi.HukukEkibi, null,
                    $"{baslik}: idareden ek süre talebine yanıt geldi mi? Yanıt sisteme kaydedilmeli.");
                yield return new(HatirlatmaTuru.EkSureYanitiTakibi, sonGunSabahi, AliciTipi.HukukEkibi, null,
                    $"{baslik}: ek süre talebine yanıt alınmadı ve asıl cevap son günü bugün!");
                yield return new(HatirlatmaTuru.EkSureYanitiTakibi, sonGunSabahi, AliciTipi.ProjeMuduru, null,
                    $"{baslik}: ek süre talebine yanıt alınmadı ve asıl cevap son günü bugün!");
                break;
        }

        // Tüm açık durumlarda cevap son günü takibi
        var yaklasiyorTur = y.SureUzatildi ? HatirlatmaTuru.TaahhutEdilenSureYaklasiyor : HatirlatmaTuru.CevapSonGunuYaklasiyor;
        var sonGunTur = y.SureUzatildi ? HatirlatmaTuru.TaahhutEdilenSure : HatirlatmaTuru.CevapSonGunu;
        var etiket = y.SureUzatildi ? "idareye taahhüt edilen cevap tarihi" : "cevap son günü";

        foreach (var h in PmVeSorumlu(yaklasiyorTur, sonGunSabahi.AddDays(-1),
                     $"{baslik}: {etiket} yarın ({y.CevapSonGunu:dd.MM.yyyy})."))
            yield return h;
        if (y.Durum != YaziDurumu.EkSureYanitiBekleniyor)
            foreach (var h in PmVeSorumlu(sonGunTur, sonGunSabahi,
                         $"{baslik}: {etiket} BUGÜN ({y.CevapSonGunu:dd.MM.yyyy}). Durum: {y.Durum}."))
                yield return h;
    }

    private static string Kisalt(string metin, int uzunluk) =>
        metin.Length <= uzunluk ? metin : metin[..(uzunluk - 1)] + "…";
}
