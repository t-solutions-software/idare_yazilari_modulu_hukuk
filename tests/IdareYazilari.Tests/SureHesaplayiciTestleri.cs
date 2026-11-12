using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Services;
using static IdareYazilari.Tests.Ortak;

namespace IdareYazilari.Tests;

public class SureHesaplayiciTestleri
{
    [Fact]
    public void Takvim_gunu_teblig_gunu_sayilmadan_eklenir()
    {
        var s = Hesaplayici().SonGunHesapla(An(2026, 11, 10), new CevapSuresiIfadesi(CevapSuresiTuru.TakvimGunu, 15));
        Assert.Equal(new DateOnly(2026, 11, 25), s.SonGun);
        Assert.False(s.TeyitGerekli);
    }

    [Fact]
    public void Is_gunu_hafta_sonu_ve_resmi_tatilleri_atlar()
    {
        // 27.10.2026 Salı; 29 Ekim tatil → 28 Eki (1), 30 Eki (2), 2 Kas (3)
        var s = Hesaplayici().SonGunHesapla(An(2026, 10, 27), new CevapSuresiIfadesi(CevapSuresiTuru.IsGunu, 3));
        Assert.Equal(new DateOnly(2026, 11, 2), s.SonGun);
    }

    [Fact]
    public void Ek_tatil_yapilandirmadan_okunur()
    {
        // 26.05.2026 Salı + 1 iş günü; 27 Mayıs ek tatil → 28 Mayıs
        var s = Hesaplayici().SonGunHesapla(An(2026, 5, 26), new CevapSuresiIfadesi(CevapSuresiTuru.IsGunu, 1));
        Assert.Equal(new DateOnly(2026, 5, 28), s.SonGun);
    }

    [Fact]
    public void Ay_bazli_sure_ayin_son_gunune_kirpilir()
    {
        var s = Hesaplayici().SonGunHesapla(An(2027, 1, 31), new CevapSuresiIfadesi(CevapSuresiTuru.Ay, 1));
        Assert.Equal(new DateOnly(2027, 2, 28), s.SonGun);
    }

    [Fact]
    public void Sure_bulunamazsa_varsayilan_uygulanir_ve_teyit_istenir()
    {
        var s = Hesaplayici().SonGunHesapla(An(2026, 11, 10), CevapSuresiIfadesi.Bulunamadi);
        Assert.Equal(new DateOnly(2026, 11, 25), s.SonGun);
        Assert.True(s.TeyitGerekli);
    }

    [Fact]
    public void Tebligden_onceki_kesin_tarih_teyit_ister()
    {
        var s = Hesaplayici().SonGunHesapla(An(2026, 11, 10), new CevapSuresiIfadesi(CevapSuresiTuru.KesinTarih, Tarih: new DateOnly(2026, 11, 1)));
        Assert.True(s.TeyitGerekli);
    }

    [Theory]
    [InlineData(false, 21)] // 21.11.2026 Cumartesi
    [InlineData(true, 23)]  // ilk iş günü Pazartesi
    public void Tatile_denk_gelen_son_gun_ayara_gore_uzatilir(bool uzat, int beklenenGun)
    {
        var h = Hesaplayici(new SureAyarlari { SonGunTatildeIseUzat = uzat });
        var s = h.SonGunHesapla(An(2026, 11, 6), new CevapSuresiIfadesi(CevapSuresiTuru.TakvimGunu, 15));
        Assert.Equal(new DateOnly(2026, 11, beklenenGun), s.SonGun);
    }

    [Fact]
    public void Ara_tarihler_yuzde_50_ve_75_onceki_is_gunu_mesai_bitimine_yuvarlanir()
    {
        // 10.11 10:00 → 25.11 23:59:59. %50 ≈ 18.11 05:00 → 17.11 18:00. %75 ≈ 22.11 02:30 (Pazar) → 20.11 Cuma 18:00
        var a = Hesaplayici().AraTarihleriHesapla(An(2026, 11, 10), new DateOnly(2026, 11, 25));
        Assert.Equal(An(2026, 11, 17, 18), a.TaslakSonTarihi);
        Assert.Equal(An(2026, 11, 20, 18), a.HukukSonTarihi);
        Assert.Equal(SureHesaplayici.GunSonu(new DateOnly(2026, 11, 25)), a.CevapSonAni);
    }

    [Fact]
    public void Yuvarlama_kapaliysa_tam_oran_kullanilir()
    {
        var bas = An(2026, 11, 10, 0);
        var a = Hesaplayici(new SureAyarlari { AraTarihleriIsGununeCek = false }).AraTarihleriHesapla(bas, new DateOnly(2026, 11, 19));
        var toplam = SureHesaplayici.GunSonu(new DateOnly(2026, 11, 19)) - bas;
        Assert.Equal(bas + toplam * 0.5, a.TaslakSonTarihi);
        Assert.Equal(bas + toplam * 0.75, a.HukukSonTarihi);
    }

    [Fact]
    public void Cok_kisa_surede_ara_tarihler_baslangictan_once_olamaz()
    {
        var bas = An(2026, 11, 12, 10);
        var a = Hesaplayici().AraTarihleriHesapla(bas, new DateOnly(2026, 11, 12));
        Assert.True(a.TaslakSonTarihi > bas);
        Assert.True(a.TaslakSonTarihi <= a.HukukSonTarihi);
        Assert.True(a.HukukSonTarihi <= a.CevapSonAni);
    }
}
