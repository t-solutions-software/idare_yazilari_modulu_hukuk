using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using static IdareYazilari.Tests.Ortak;

namespace IdareYazilari.Tests;

public class IsAkisiTestleri
{
    [Fact]
    public void Yeni_yazi_atama_bekler_ve_ara_tarihler_hesaplanir()
    {
        var y = YeniYazi();
        Assert.Equal(YaziDurumu.AtamaBekliyor, y.Durum);
        Assert.Equal(An(2026, 11, 17, 18), y.TaslakSonTarihi);
        Assert.Equal(An(2026, 11, 20, 18), y.HukukSonTarihi);
        Assert.Single(y.Hareketler);
    }

    [Fact]
    public void Ana_akis_atama_taslak_revizyon_onay_gonderim()
    {
        var y = YeniYazi();
        var t = An(2026, 11, 11);
        y.SorumluAta(Sorumlu, Pm, t);
        Assert.Equal(YaziDurumu.TaslakHazirlaniyor, y.Durum);

        y.TaslakKaydet("v1", Sorumlu, KullaniciRolu.Personel, t);
        y.HukukaGonder(Sorumlu, t);
        Assert.Equal(YaziDurumu.HukukIncelemesinde, y.Durum);

        y.RevizyonTalepEt(Hukukcu, "madde 12'ye atıf ekleyin", t);
        Assert.Equal(YaziDurumu.RevizyonBekleniyor, y.Durum);

        y.TaslakKaydet("v2", Sorumlu, KullaniciRolu.Personel, t);
        y.HukukaGonder(Sorumlu, t);
        y.RevizeEtVeOnayla(Hukukcu, "v3 (hukuk)", "küçük düzeltme", t);
        Assert.Equal(YaziDurumu.HukukOnayladi, y.Durum);
        Assert.Equal(3, y.GuncelTaslak!.VersiyonNo);
        Assert.Equal(KullaniciRolu.Hukuk, y.GuncelTaslak.HazirlayanRol);
        Assert.Equal([HukukKarari.RevizyonTalebi, HukukKarari.RevizeEdilerekOnay], y.HukukIncelemeleri.Select(i => i.Karar));

        y.IdareyeGonder(Pm, "2026/77", "KEP", t);
        Assert.Equal(YaziDurumu.Gonderildi, y.Durum);
        Assert.True(y.Kapali);
    }

    [Fact]
    public void Hukuk_onayi_olmadan_gonderilemez()
    {
        var y = YeniYazi();
        y.SorumluAta(Sorumlu, Pm, An(2026, 11, 11));
        y.TaslakKaydet("v1", Sorumlu, KullaniciRolu.Personel, An(2026, 11, 11));
        Assert.Throws<IsKuraliHatasi>(() => y.IdareyeGonder(Pm, null, null, An(2026, 11, 11)));
    }

    [Fact]
    public void Taslaksiz_hukuka_gonderilemez_ve_revizyon_aciklamasiz_olamaz()
    {
        var y = YeniYazi();
        y.SorumluAta(Sorumlu, Pm, An(2026, 11, 11));
        Assert.Throws<IsKuraliHatasi>(() => y.HukukaGonder(Sorumlu, An(2026, 11, 11)));
        y.TaslakKaydet("v1", Sorumlu, KullaniciRolu.Personel, An(2026, 11, 11));
        y.HukukaGonder(Sorumlu, An(2026, 11, 11));
        Assert.Throws<IsKuraliHatasi>(() => y.RevizyonTalepEt(Hukukcu, " ", An(2026, 11, 11)));
    }

    [Fact]
    public void Istisna_reddedilirse_onceki_asamaya_donulur_ve_sure_degismez()
    {
        var y = YeniYazi();
        y.SorumluAta(Sorumlu, Pm, An(2026, 11, 11));
        y.SuresindeCevapVerilemezBildir("veri bekleniyor", new DateOnly(2026, 12, 10), Sorumlu, An(2026, 11, 12));
        Assert.Equal(YaziDurumu.IstisnaHukukIncelemesinde, y.Durum);

        y.IstisnaReddet(Hukukcu, "mevcut verilerle cevap verilebilir", An(2026, 11, 12, 15));
        Assert.Equal(YaziDurumu.TaslakHazirlaniyor, y.Durum);
        Assert.Equal(new DateOnly(2026, 11, 25), y.CevapSonGunu);
        Assert.False(y.SureUzatildi);
    }

    [Fact]
    public void Ek_sure_kabul_edilirse_yeni_son_gun_ve_yeni_ara_tarihler_uygulanir()
    {
        var y = YeniYazi();
        y.SorumluAta(Sorumlu, Pm, An(2026, 11, 11));
        y.SuresindeCevapVerilemezBildir("denetçi raporu bekleniyor", null, Sorumlu, An(2026, 11, 12));
        y.EkSureTalepEt(Hukukcu, "Ek süre talebimiz...", new DateOnly(2026, 12, 15), "2026/90", null, An(2026, 11, 12, 15));
        Assert.Equal(YaziDurumu.EkSureYanitiBekleniyor, y.Durum);
        Assert.Equal(new DateOnly(2026, 11, 25), y.CevapSonGunu); // yanıt gelene kadar asıl süre geçerli

        var karar = An(2026, 11, 16, 10);
        y.EkSureYanitiKaydet(EkSureYaniti.Kabul, new DateOnly(2026, 12, 7), Hukukcu, Hesaplayici(), karar);
        Assert.Equal(YaziDurumu.TaslakHazirlaniyor, y.Durum);
        Assert.Equal(new DateOnly(2026, 12, 7), y.CevapSonGunu);
        Assert.Equal(new DateOnly(2026, 11, 25), y.IlkCevapSonGunu);
        Assert.True(y.SureUzatildi);
        Assert.Equal(karar, y.SurecBaslangici);
        Assert.True(y.TaslakSonTarihi > karar && y.TaslakSonTarihi < y.HukukSonTarihi);
    }

    [Fact]
    public void Ek_sure_reddedilirse_asil_sure_gecerli_kalir()
    {
        var y = YeniYazi();
        y.SorumluAta(Sorumlu, Pm, An(2026, 11, 11));
        y.SuresindeCevapVerilemezBildir("neden", null, Sorumlu, An(2026, 11, 12));
        y.EkSureTalepEt(Hukukcu, "talep", new DateOnly(2026, 12, 15), null, null, An(2026, 11, 12));
        y.EkSureYanitiKaydet(EkSureYaniti.Ret, null, Hukukcu, Hesaplayici(), An(2026, 11, 14));
        Assert.Equal(YaziDurumu.TaslakHazirlaniyor, y.Durum);
        Assert.Equal(new DateOnly(2026, 11, 25), y.CevapSonGunu);
        Assert.False(y.SureUzatildi);
    }

    [Fact]
    public void Gecikme_bildiriminde_taahhut_tarihi_yeni_son_gun_olur_ve_hatirlatma_kurulur()
    {
        var y = YeniYazi();
        y.SorumluAta(Sorumlu, Pm, An(2026, 11, 11));
        y.TaslakKaydet("v1", Sorumlu, KullaniciRolu.Personel, An(2026, 11, 11));
        y.HukukaGonder(Sorumlu, An(2026, 11, 11));
        y.SuresindeCevapVerilemezBildir("bilirkişi raporu", new DateOnly(2026, 12, 20), Hukukcu, An(2026, 11, 12));

        var simdi = An(2026, 11, 13);
        y.GecikmeBildir(Hukukcu, "Gerekçeli yazı...", new DateOnly(2026, 12, 18), "2026/91", "uygun", Hesaplayici(), simdi);
        y.HatirlatmalariYenile(Planlayici(), simdi);

        Assert.Equal(YaziDurumu.HukukIncelemesinde, y.Durum); // bildirildiği aşamaya döner
        Assert.Equal(new DateOnly(2026, 12, 18), y.CevapSonGunu);
        var bekleyen = y.Hatirlatmalar.Where(h => h.Bekliyor).ToList();
        Assert.Contains(bekleyen, h => h.Tur == HatirlatmaTuru.TaahhutEdilenSure && h.Zaman == new DateTimeOffset(2026, 12, 18, 9, 0, 0, Tr));
        Assert.Contains(bekleyen, h => h.Tur == HatirlatmaTuru.TaahhutEdilenSureYaklasiyor);
        Assert.Contains(bekleyen, h => h.Tur == HatirlatmaTuru.IstisnaSonuclandi && h.AliciTipi == AliciTipi.ProjeMuduru);
        Assert.DoesNotContain(bekleyen, h => h.Tur == HatirlatmaTuru.CevapSonGunu);
    }

    [Fact]
    public void Gecmis_veya_mevcut_son_gunden_once_tarihle_istisna_cozulemez()
    {
        var y = YeniYazi();
        y.SorumluAta(Sorumlu, Pm, An(2026, 11, 11));
        Assert.Throws<IsKuraliHatasi>(() => y.SuresindeCevapVerilemezBildir("", null, Sorumlu, An(2026, 11, 12)));
        y.SuresindeCevapVerilemezBildir("neden", null, Sorumlu, An(2026, 11, 12));
        Assert.Throws<IsKuraliHatasi>(() => y.EkSureTalepEt(Hukukcu, "t", new DateOnly(2026, 11, 20), null, null, An(2026, 11, 12)));
        Assert.Throws<IsKuraliHatasi>(() => y.GecikmeBildir(Hukukcu, "t", new DateOnly(2026, 11, 12), null, null, Hesaplayici(), An(2026, 11, 12)));
    }

    [Fact]
    public void Ek_sure_sonrasi_son_gun_elle_degistirilemez()
    {
        var y = YeniYazi();
        y.SorumluAta(Sorumlu, Pm, An(2026, 11, 11));
        y.SuresindeCevapVerilemezBildir("neden", null, Sorumlu, An(2026, 11, 12));
        y.GecikmeBildir(Hukukcu, "t", new DateOnly(2026, 12, 18), null, null, Hesaplayici(), An(2026, 11, 12));
        Assert.Throws<IsKuraliHatasi>(() => y.BilgileriDuzelt(null, null, new DateOnly(2026, 12, 30), Pm, Hesaplayici(), An(2026, 11, 13)));
    }
}
