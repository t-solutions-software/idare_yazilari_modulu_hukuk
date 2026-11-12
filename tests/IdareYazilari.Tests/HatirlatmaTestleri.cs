using IdareYazilari.Domain.Enums;
using static IdareYazilari.Tests.Ortak;

namespace IdareYazilari.Tests;

public class HatirlatmaTestleri
{
    [Fact]
    public void Atama_bekleyen_yazi_icin_proje_mudurune_hatirlatma_ve_son_gun_takibi_kurulur()
    {
        var y = YeniYazi();
        y.HatirlatmalariYenile(Planlayici(), y.OlusturmaTarihi);

        var atama = y.Hatirlatmalar.Where(h => h.Tur == HatirlatmaTuru.AtamaBekliyor).ToList();
        Assert.Equal(4, atama.Count);
        Assert.All(atama, h => Assert.Equal(AliciTipi.ProjeMuduru, h.AliciTipi));
        Assert.Equal(y.OlusturmaTarihi, atama.Min(h => h.Zaman));
        Assert.Contains(y.Hatirlatmalar, h => h.Tur == HatirlatmaTuru.CevapSonGunu && h.Zaman == new DateTimeOffset(2026, 11, 25, 9, 0, 0, Tr));
        Assert.Contains(y.Hatirlatmalar, h => h.Tur == HatirlatmaTuru.CevapSonGunuYaklasiyor && h.Zaman == new DateTimeOffset(2026, 11, 24, 9, 0, 0, Tr));
    }

    [Fact]
    public void Durum_degisince_gecersiz_hatirlatmalar_iptal_edilir_gonderilmisler_korunur()
    {
        var y = YeniYazi();
        y.HatirlatmalariYenile(Planlayici(), y.OlusturmaTarihi);
        var ilk = y.Hatirlatmalar.First(h => h.Tur == HatirlatmaTuru.AtamaBekliyor && h.Zaman == y.OlusturmaTarihi);
        ilk.GonderimZamani = y.OlusturmaTarihi;

        var t = An(2026, 11, 10, 12);
        y.SorumluAta(Sorumlu, Pm, t);
        y.HatirlatmalariYenile(Planlayici(), t);

        Assert.All(y.Hatirlatmalar.Where(h => h.Tur == HatirlatmaTuru.AtamaBekliyor && h != ilk), h => Assert.True(h.Iptal));
        Assert.False(ilk.Iptal);
        Assert.Contains(y.Hatirlatmalar, h => h.Bekliyor && h.Tur == HatirlatmaTuru.SorumluAtandi && h.AliciKullaniciId == Sorumlu);
        Assert.Contains(y.Hatirlatmalar, h => h.Bekliyor && h.Tur == HatirlatmaTuru.TaslakSuresiYaklasiyor && h.Zaman == y.TaslakSonTarihi.AddHours(-24));
        Assert.Contains(y.Hatirlatmalar, h => h.Bekliyor && h.Tur == HatirlatmaTuru.TaslakSuresiDoldu && h.AliciTipi == AliciTipi.ProjeMuduru);
        // Son gün hatırlatmaları artık sorumluya da gider
        Assert.Contains(y.Hatirlatmalar, h => h.Bekliyor && h.Tur == HatirlatmaTuru.CevapSonGunu && h.AliciKullaniciId == Sorumlu);
    }

    [Fact]
    public void Yeniden_planlama_ayni_hatirlatmalari_cogaltmaz()
    {
        var y = YeniYazi();
        y.HatirlatmalariYenile(Planlayici(), y.OlusturmaTarihi);
        var adet = y.Hatirlatmalar.Count;
        y.HatirlatmalariYenile(Planlayici(), y.OlusturmaTarihi.AddMinutes(5));
        Assert.Equal(adet, y.Hatirlatmalar.Count);
        Assert.DoesNotContain(y.Hatirlatmalar, h => h.Iptal);
    }

    [Fact]
    public void Hukuk_incelemesinde_hukuk_ekibine_hatirlatma_gider()
    {
        var y = YeniYazi();
        var t = An(2026, 11, 11);
        y.SorumluAta(Sorumlu, Pm, t);
        y.TaslakKaydet("v1", Sorumlu, KullaniciRolu.Personel, t);
        y.OnayaGonder(Sorumlu, false, t);
        y.HatirlatmalariYenile(Planlayici(), t);

        var hukuk = y.Hatirlatmalar.Where(h => h.Bekliyor && h.AliciTipi == AliciTipi.HukukEkibi).Select(h => h.Tur).ToList();
        Assert.Contains(HatirlatmaTuru.HukukIncelemesiBekliyor, hukuk);
        Assert.Contains(HatirlatmaTuru.HukukSuresiYaklasiyor, hukuk);
        Assert.Contains(HatirlatmaTuru.HukukSuresiDoldu, hukuk);
        Assert.DoesNotContain(y.Hatirlatmalar, h => h.Bekliyor && h.Tur == HatirlatmaTuru.TaslakSuresiDoldu);
    }

    [Fact]
    public void Gonderilen_yazinin_bekleyen_hatirlatmalari_iptal_edilir()
    {
        var y = YeniYazi();
        var t = An(2026, 11, 11);
        y.SorumluAta(Sorumlu, Pm, t);
        y.TaslakKaydet("v1", Sorumlu, KullaniciRolu.Personel, t);
        y.OnayaGonder(Sorumlu, false, t);
        y.HukukOnayla(Hukukcu, null, false, t);
        y.HatirlatmalariYenile(Planlayici(), t);
        y.CevapSayisiVer("GDN-1", "S-1", Pm, t);
        y.IdareyeGonder(Pm, null, null, t);
        y.HatirlatmalariYenile(Planlayici(), t);
        Assert.DoesNotContain(y.Hatirlatmalar, h => h.Bekliyor);
    }
}
