using IdareYazilari.Domain.Enums;

namespace IdareYazilari.Domain.Services;

public sealed record AraTarihler(DateTimeOffset TaslakSonTarihi, DateTimeOffset HukukSonTarihi, DateTimeOffset CevapSonAni);

public sealed record SonGunSonucu(DateOnly SonGun, bool TeyitGerekli, string Aciklama);

public class SureHesaplayici(SureAyarlari ayarlar, ITatilTakvimi takvim)
{
    public SureAyarlari Ayarlar => ayarlar;
    public ITatilTakvimi Takvim => takvim;

    /// <summary>Verilen günün Türkiye saatiyle 23:59:59 anı.</summary>
    public static DateTimeOffset GunSonu(DateOnly gun) =>
        new(gun.ToDateTime(new TimeOnly(23, 59, 59)), SureAyarlari.TurkiyeSaatFarki);

    public static DateOnly TurkiyeTarihi(DateTimeOffset an) =>
        DateOnly.FromDateTime(an.ToOffset(SureAyarlari.TurkiyeSaatFarki).DateTime);

    /// <summary>
    /// Tebliğ tarihi ve çıkarılan ifadeye göre cevap son gününü hesaplar.
    /// Tebliğ günü süreye dahil edilmez (süre ertesi gün başlar).
    /// </summary>
    public SonGunSonucu SonGunHesapla(DateTimeOffset tebligTarihi, CevapSuresiIfadesi ifade)
    {
        var teblig = TurkiyeTarihi(tebligTarihi);
        switch (ifade.Tur)
        {
            case CevapSuresiTuru.KesinTarih when ifade.Tarih is { } tarih:
                return tarih < teblig
                    ? new SonGunSonucu(tarih, true, $"Metindeki son tarih ({tarih:dd.MM.yyyy}) tebliğ tarihinden önce; teyit edilmeli.")
                    : new SonGunSonucu(tarih, false, $"Metinde kesin tarih: {tarih:dd.MM.yyyy}");

            case CevapSuresiTuru.TakvimGunu when ifade.Gun is > 0 and var gun:
            {
                var son = teblig.AddDays(gun);
                var aciklama = $"Tebliğden itibaren {gun} takvim günü";
                if (ayarlar.SonGunTatildeIseUzat && !takvim.IsGunuMu(son))
                {
                    son = SonrakiIsGunu(son);
                    aciklama += " (son gün tatile denk geldiği için ilk iş gününe uzatıldı)";
                }
                return new SonGunSonucu(son, false, aciklama);
            }

            case CevapSuresiTuru.Ay when ifade.Gun is > 0 and var ay:
            {
                var son = teblig.AddMonths(ay);
                var aciklama = $"Tebliğden itibaren {ay} ay";
                if (ayarlar.SonGunTatildeIseUzat && !takvim.IsGunuMu(son))
                {
                    son = SonrakiIsGunu(son);
                    aciklama += " (son gün tatile denk geldiği için ilk iş gününe uzatıldı)";
                }
                return new SonGunSonucu(son, false, aciklama);
            }

            case CevapSuresiTuru.IsGunu when ifade.Gun is > 0 and var isGunu:
                return new SonGunSonucu(IsGunuEkle(teblig, isGunu), false, $"Tebliğden itibaren {isGunu} iş günü");

            default:
            {
                var varsayilan = ayarlar.VarsayilanCevapSuresiGun;
                var ek = string.IsNullOrWhiteSpace(ifade.KaynakMetin) ? "" : $" Metinde \"{ifade.KaynakMetin}\" ifadesi var.";
                return new SonGunSonucu(teblig.AddDays(varsayilan), true,
                    $"Metinde cevap süresi bulunamadı; varsayılan {varsayilan} gün uygulandı. Proje müdürü teyit etmeli.{ek}");
            }
        }
    }

    /// <summary>
    /// %50 (taslak) ve %75 (hukuk) ara tarihlerini, süreç başlangıcı ile cevap son gününün sonu arasındaki
    /// toplam süre üzerinden hesaplar. Ara tarih tatile denk gelirse önceki iş gününün sonuna çekilir (ihtiyatlı).
    /// </summary>
    public AraTarihler AraTarihleriHesapla(DateTimeOffset baslangic, DateOnly cevapSonGunu)
    {
        var bitis = GunSonu(cevapSonGunu);
        if (bitis <= baslangic)
            return new AraTarihler(bitis, bitis, bitis);

        var toplam = bitis - baslangic;
        var taslak = baslangic + toplam * ayarlar.TaslakOrani;
        var hukuk = baslangic + toplam * ayarlar.HukukOrani;

        if (ayarlar.AraTarihleriIsGununeCek)
        {
            taslak = OncekiIsGununeCek(taslak, baslangic);
            hukuk = OncekiIsGununeCek(hukuk, taslak);
        }

        return new AraTarihler(taslak, hukuk, bitis);
    }

    /// <summary>
    /// Ara tarihi ihtiyatlı biçimde geriye yuvarlar: en yakın önceki iş gününün mesai bitimi.
    /// Sonuç alt sınırın (süreç başlangıcı / önceki ara tarih) gerisine düşerse orijinal an korunur.
    /// </summary>
    private DateTimeOffset OncekiIsGununeCek(DateTimeOffset an, DateTimeOffset altSinir)
    {
        var yerel = an.ToOffset(SureAyarlari.TurkiyeSaatFarki);
        var gun = DateOnly.FromDateTime(yerel.DateTime);
        if (TimeOnly.FromDateTime(yerel.DateTime) < ayarlar.MesaiBitisSaati) gun = gun.AddDays(-1);
        while (!takvim.IsGunuMu(gun)) gun = gun.AddDays(-1);
        var aday = new DateTimeOffset(gun.ToDateTime(ayarlar.MesaiBitisSaati), SureAyarlari.TurkiyeSaatFarki);
        return aday > altSinir ? aday : an;
    }

    public DateOnly SonrakiIsGunu(DateOnly gun)
    {
        while (!takvim.IsGunuMu(gun)) gun = gun.AddDays(1);
        return gun;
    }

    public DateOnly IsGunuEkle(DateOnly baslangic, int isGunu)
    {
        var gun = baslangic;
        var eklenen = 0;
        while (eklenen < isGunu)
        {
            gun = gun.AddDays(1);
            if (takvim.IsGunuMu(gun)) eklenen++;
        }
        return gun;
    }
}
