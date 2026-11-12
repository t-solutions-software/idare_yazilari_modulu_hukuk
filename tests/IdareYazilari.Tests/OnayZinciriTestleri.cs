using System.Net;
using System.Text;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Services;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using IdareYazilari.Domain.Services;
using IdareYazilari.Infrastructure.Belgeler;
using IdareYazilari.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using static IdareYazilari.Tests.Ortak;

namespace IdareYazilari.Tests;

public class OnayZinciriTestleri
{
    private static IdareYazisi TaslakHazir()
    {
        var y = YeniYazi();
        var t = An(2026, 11, 11);
        y.SorumluAta(Sorumlu, Pm, t, Guid.NewGuid());
        y.TaslakKaydet("v1", Sorumlu, KullaniciRolu.Personel, t);
        return y;
    }

    [Fact]
    public void Birim_onayi_ile_tam_zincir_birim_hukuk_imza_gonderim()
    {
        var y = TaslakHazir();
        var t = An(2026, 11, 12);
        y.OnayaGonder(Sorumlu, birimOnayiGerekli: true, t);
        Assert.Equal(YaziDurumu.BirimOnayinda, y.Durum);

        y.BirimOnayla(Pm, "uygun", t);
        Assert.Equal(YaziDurumu.HukukIncelemesinde, y.Durum);
        Assert.Equal(Pm, y.BirimOnaylayanId);

        y.HukukOnayla(Hukukcu, null, imzaGerekli: true, t);
        Assert.Equal(YaziDurumu.ImzaBekliyor, y.Durum);
        Assert.Throws<IsKuraliHatasi>(() => y.IdareyeGonder(Pm, "KEP", null, t));

        var imzaci = Guid.NewGuid();
        var giden = y.Imzala(imzaci, "GDN-2026-000007", "SH-01-2026/00003", null, t);
        Assert.Equal(YaziDurumu.GonderimeHazir, y.Durum);
        Assert.Equal("v1", giden.Icerik);
        Assert.Equal(imzaci, giden.ImzalayanId);
        Assert.Same(giden, y.BekleyenCevap);

        y.IdareyeGonder(Pm, "KEP", "<m1@kep>", t);
        Assert.Equal(YaziDurumu.Gonderildi, y.Durum);
        Assert.Null(y.BekleyenCevap);
    }

    [Fact]
    public void Birim_iade_ederse_revizyona_doner_ve_gerekce_zorunlu()
    {
        var y = TaslakHazir();
        y.OnayaGonder(Sorumlu, true, An(2026, 11, 12));
        Assert.Throws<IsKuraliHatasi>(() => y.BirimIade(Pm, "", An(2026, 11, 12)));
        y.BirimIade(Pm, "rakamları kontrol edin", An(2026, 11, 12));
        Assert.Equal(YaziDurumu.RevizyonBekleniyor, y.Durum);
    }

    [Fact]
    public void Imza_yetkilisi_iade_ederse_hukuka_doner()
    {
        var y = TaslakHazir();
        var t = An(2026, 11, 12);
        y.OnayaGonder(Sorumlu, false, t);
        y.HukukOnayla(Hukukcu, null, true, t);
        y.ImzaIade(Guid.NewGuid(), "üslup düzeltilsin", t);
        Assert.Equal(YaziDurumu.HukukIncelemesinde, y.Durum);
        Assert.Empty(y.GidenYazilar);
    }

    [Fact]
    public void Gonderim_hatasi_kaydedilir_yazi_gonderime_hazir_kalir()
    {
        var y = TaslakHazir();
        var t = An(2026, 11, 12);
        y.OnayaGonder(Sorumlu, false, t);
        y.HukukOnayla(Hukukcu, null, false, t);
        var g = y.CevapSayisiVer("G-1", "S-1", Pm, t);
        y.GonderimHatasi(g.Id, Pm, "KEP adresi yok", t);
        Assert.Equal(YaziDurumu.GonderimeHazir, y.Durum);
        Assert.Equal(GonderimDurumu.Hata, g.Durum);
        // Tekrar sayı istendiğinde aynı giden yazı döner (sayı iki kez verilmez)
        Assert.Same(g, y.CevapSayisiVer("G-2", "S-2", Pm, t));
        y.HatirlatmalariYenile(Planlayici(), t);
        Assert.Contains(y.Hatirlatmalar, h => h.Bekliyor && h.Tur == HatirlatmaTuru.GonderimHatasi);
    }

    [Fact]
    public void Planlayici_birim_imza_ve_eskalasyon_hatirlatmalarini_kurar()
    {
        var y = TaslakHazir();
        var t = An(2026, 11, 12);
        y.OnayaGonder(Sorumlu, true, t);
        y.HatirlatmalariYenile(Planlayici(), t);
        Assert.Contains(y.Hatirlatmalar, h => h.Bekliyor && h.Tur == HatirlatmaTuru.BirimOnayiBekliyor && h.AliciTipi == AliciTipi.BirimYoneticisi);
        var esk = y.Hatirlatmalar.Where(h => h.Bekliyor && h.Tur == HatirlatmaTuru.Eskalasyon).ToList();
        Assert.Contains(esk, h => h.AliciTipi == AliciTipi.UstYonetim);
        Assert.All(esk, h => Assert.Equal(SureHesaplayici.GunSonu(y.CevapSonGunu).AddHours(-48), h.Zaman));

        y.BirimOnayla(Pm, null, t);
        y.HukukOnayla(Hukukcu, null, true, t);
        y.HatirlatmalariYenile(Planlayici(), t);
        Assert.Contains(y.Hatirlatmalar, h => h.Bekliyor && h.Tur == HatirlatmaTuru.ImzaBekliyor && h.AliciTipi == AliciTipi.ImzaYetkilileri);
        Assert.DoesNotContain(y.Hatirlatmalar, h => h.Bekliyor && h.Tur == HatirlatmaTuru.BirimOnayiBekliyor);
    }

    [Fact]
    public void Gorev_uretilir_dis_anahtar_benzersizdir_ve_eposta_zorunludur()
    {
        var y = YeniYazi();
        var g = y.GorevEkle("Rapor", null, "Ali@Kurum.com", null, null, Pm, An(2026, 11, 11));
        Assert.Equal("ali@kurum.com", g.SorumluEposta);
        Assert.Equal($"{y.Id:N}/{g.Id:N}", g.DisAnahtar);
        Assert.Throws<IsKuraliHatasi>(() => y.GorevEkle("x", null, "gecersiz", null, null, Pm, An(2026, 11, 11)));
        Assert.Throws<IsKuraliHatasi>(() => y.GorevEkle("x", null, "a@b.c", null, new DateOnly(2026, 1, 1), Pm, An(2026, 11, 11)));
    }

    private static KullaniciBaglami B(KullaniciRolu rol, Guid? projeId = null, params Guid[] birimler) =>
        new(new Kullanici { Id = Guid.NewGuid(), Rol = rol, ProjeId = projeId }, birimler.ToHashSet());

    [Fact]
    public void Yetki_birim_yoneticisi_ve_imza_yetkilisi()
    {
        var y = TaslakHazir();
        var birimYon = B(KullaniciRolu.Personel, null, y.BirimId!.Value);
        var baskaPersonel = B(KullaniciRolu.Personel, Ortak.Proje);
        Assert.True(Yetki.GorebilirMi(birimYon, y));
        Assert.False(Yetki.GorebilirMi(baskaPersonel, y));

        y.OnayaGonder(Sorumlu, true, An(2026, 11, 12));
        Assert.Contains(Islemler.BirimOnayla, Yetki.YapilabilirIslemler(birimYon, y));
        Assert.DoesNotContain(Islemler.BirimOnayla, Yetki.YapilabilirIslemler(B(KullaniciRolu.ProjeMuduru, Ortak.Proje), y)); // birim varken PM onaylamaz

        y.BirimOnayla(Pm, null, An(2026, 11, 12));
        y.HukukOnayla(Hukukcu, null, true, An(2026, 11, 12));
        var genelImza = B(KullaniciRolu.ImzaYetkilisi);
        var baskaProjeImza = B(KullaniciRolu.ImzaYetkilisi, Guid.NewGuid());
        Assert.Contains(Islemler.Imzala, Yetki.YapilabilirIslemler(genelImza, y));
        Assert.False(Yetki.GorebilirMi(baskaProjeImza, y));
    }

    [Fact]
    public void Birim_onaycisi_kendisi_gonderirse_birim_adimi_atlanir()
    {
        var y = TaslakHazir();
        var birimYon = B(KullaniciRolu.Personel, null, y.BirimId!.Value);
        Assert.False(Yetki.BirimOnayiGerekli(new OnayAkisiAyarlari(), birimYon, y));
        Assert.True(Yetki.BirimOnayiGerekli(new OnayAkisiAyarlari(), B(KullaniciRolu.Personel, Ortak.Proje), y));
        Assert.False(Yetki.BirimOnayiGerekli(new OnayAkisiAyarlari { BirimOnayi = false }, B(KullaniciRolu.Personel, Ortak.Proje), y));
    }

    [Theory]
    [InlineData("GLN-{yil}-{no:000000}", null, "GLN-2026-000042")]
    [InlineData("{proje}-{yil}/{no:00000}", "SH-01", "SH-01-2026/00042")]
    [InlineData("{no}/{yil}", null, "42/2026")]
    public void Numara_bicimlendirme(string format, string? proje, string beklenen) =>
        Assert.Equal(beklenen, EfEvrakNumaratoru.Bicimle(format, 2026, 42, proje));

    [Fact]
    public void Word_sablonu_yer_tutuculari_doldurur_metni_satirlara_boler_bos_ilgiyi_siler()
    {
        var o = new WordBelgeOlusturucu(Options.Create(new BelgeAyarlari()));
        var bayt = o.Olustur(new GidenYaziBelgeModeli("Örnek Proje", "SH-01-2026/00001", An(2026, 11, 12), "Sağlık Bakanlığı",
            "Bakım hk.", null, "Birinci paragraf.\nİkinci paragraf.", "Genel Müdür", false));
        using var ms = new MemoryStream(bayt);
        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(ms, false);
        var paragraflar = doc.MainDocumentPart!.Document.Body!.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Select(p => p.InnerText).ToList();
        var metin = string.Join("|", paragraflar);
        Assert.DoesNotContain("{{", metin);
        Assert.Contains("Sayı  : SH-01-2026/00001", paragraflar);
        Assert.Contains("Tarih : 12.11.2026", paragraflar);
        Assert.Contains("SAĞLIK BAKANLIĞINE", paragraflar);
        Assert.Contains("Birinci paragraf.", paragraflar);
        Assert.Contains("İkinci paragraf.", paragraflar);
        Assert.DoesNotContain(paragraflar, p => p.StartsWith("İlgi"));
        Assert.DoesNotContain("TASLAK", metin);
    }

    private sealed class SahteGraph(Func<HttpRequestMessage, HttpResponseMessage> yanit) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Istekler { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Istekler.Add(r);
            return Task.FromResult(yanit(r));
        }
    }

    [Fact]
    public async Task SharePoint_deposu_token_alir_yukler_ve_indirir()
    {
        static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
        var graph = new SahteGraph(r => r.RequestUri!.Host switch
        {
            "login.microsoftonline.com" => Json("""{"access_token":"tok","expires_in":3600}"""),
            _ when r.Method == HttpMethod.Put => Json("""{"id":"ITEM1"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 8, 7]) },
        });
        var depo = new SharePointDosyaDeposu(new HttpClient(graph), Options.Create(new SharePointAyarlari
        {
            TenantId = "t", ClientId = "c", ClientSecret = "s", SiteId = "site", DriveId = "DRV", KokKlasor = "IdareYazilari",
        }));

        var anahtar = await depo.KaydetAsync(Guid.Empty, "yazı #1.pdf", [1, 2, 3]);
        Assert.Equal("sp:DRV:ITEM1", anahtar);
        var put = graph.Istekler.Single(i => i.Method == HttpMethod.Put);
        Assert.Equal("Bearer tok", put.Headers.Authorization!.ToString());
        Assert.StartsWith("https://graph.microsoft.com/v1.0/drives/DRV/root:/IdareYazilari/", put.RequestUri!.AbsoluteUri);
        Assert.DoesNotContain("#", put.RequestUri.AbsolutePath);

        await using var akim = await depo.AcAsync(anahtar);
        using var ms = new MemoryStream();
        await akim!.CopyToAsync(ms);
        Assert.Equal(new byte[] { 9, 8, 7 }, ms.ToArray());
        Assert.Single(graph.Istekler, i => i.RequestUri!.Host == "login.microsoftonline.com"); // belirteç önbellekte
        Assert.Null(await depo.AcAsync("yerel/anahtar"));
    }
}
