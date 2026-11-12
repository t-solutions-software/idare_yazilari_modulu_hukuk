using IdareYazilari.Application.Analiz;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using static IdareYazilari.Tests.Ortak;

namespace IdareYazilari.Tests;

public class AnalizTestleri
{
    private static readonly DateOnly Teblig = new(2026, 11, 10);

    [Theory]
    [InlineData("yazımızın tebliğinden itibaren 15 (on beş) gün içinde bildirilmesi", CevapSuresiTuru.TakvimGunu, 15)]
    [InlineData("on beş gün içerisinde gönderilmesi", CevapSuresiTuru.TakvimGunu, 15)]
    [InlineData("otuz gün zarfında", CevapSuresiTuru.TakvimGunu, 30)]
    [InlineData("7 iş günü içinde iletilmesi", CevapSuresiTuru.IsGunu, 7)]
    [InlineData("YEDİ İŞ GÜNÜ İÇİNDE", CevapSuresiTuru.IsGunu, 7)]
    [InlineData("1 hafta içinde", CevapSuresiTuru.TakvimGunu, 7)]
    [InlineData("1 (bir) ay içinde", CevapSuresiTuru.Ay, 1)]
    [InlineData("10 günlük süre içinde", CevapSuresiTuru.TakvimGunu, 10)]
    public void Sure_ifadeleri_taninir(string metin, CevapSuresiTuru tur, int gun)
    {
        var s = KuralTabanliAnalizci.SureBul(metin, Teblig);
        Assert.Equal(tur, s.Tur);
        Assert.Equal(gun, s.Gun);
    }

    [Theory]
    [InlineData("en geç 20.11.2026 tarihine kadar")]
    [InlineData("20/11/2026 tarihine kadar")]
    [InlineData("20 Kasım 2026 tarihine kadar")]
    [InlineData("20.11.2026 günü mesai bitimine kadar")]
    public void Kesin_tarih_taninir(string metin)
    {
        var s = KuralTabanliAnalizci.SureBul(metin, Teblig);
        Assert.Equal(CevapSuresiTuru.KesinTarih, s.Tur);
        Assert.Equal(new DateOnly(2026, 11, 20), s.Tarih);
    }

    [Fact]
    public void Birden_fazla_sure_varsa_en_kisasi_secilir()
    {
        var s = KuralTabanliAnalizci.SureBul("Nihai raporun 30 gün içinde, ön bilginin ise 5 gün içinde iletilmesi", Teblig);
        Assert.Equal(5, s.Gun);
    }

    [Fact]
    public void Kelime_icindeki_sayi_kelimeleri_yanlis_eslesmez()
    {
        var s = KuralTabanliAnalizci.SureBul("Sözleşmenin ilgili maddesi uyarınca idare onay içinde değil; bunun gün içinde yapılması", Teblig);
        Assert.Equal(CevapSuresiTuru.Bulunamadi, s.Tur);
    }

    [Fact]
    public void Ivedi_ifadesi_raporlanir_ama_sure_bulunamadi_sayilir()
    {
        var s = KuralTabanliAnalizci.SureBul("raporun ivedilikle gönderilmesi", Teblig);
        Assert.Equal(CevapSuresiTuru.Bulunamadi, s.Tur);
        Assert.Equal("ivedi", s.KaynakMetin);
    }

    [Fact]
    public async Task Resmi_yazidan_konu_sayi_gonderen_ve_ozet_cikarilir()
    {
        const string metin = """
            T.C.
            SAĞLIK BAKANLIĞI

            Sayı : E-123-000-2026/1001
            Konu : Tıbbi cihaz bakımları hk.

            PROJE ŞİRKETİNE

            İlgi : 01.10.2026 tarihli yazı.
            Hastanedeki cihazların bakımlarının aksadığı bildirilmiştir. Hava bugün güzel.
            Nedenlerin 15 gün içinde bildirilmesi gerekmektedir.

            Daire Başkanı
            """;
        var a = await new KuralTabanliAnalizci().AnalizEtAsync(new AnalizGirdisi(metin, null, An(2026, 11, 10), []));
        Assert.NotNull(a);
        Assert.Equal("Tıbbi cihaz bakımları hk.", a!.Konu);
        Assert.Equal("E-123-000-2026/1001", a.GelenSayi);
        Assert.Equal("SAĞLIK BAKANLIĞI", a.GonderenIdare);
        Assert.StartsWith("Hastanedeki cihazların", a.Ozet);
        Assert.Contains("15 gün içinde", a.Ozet);
        Assert.DoesNotContain("İlgi", a.Ozet);
        Assert.DoesNotContain("Hava bugün", a.Ozet);
        Assert.Equal(15, a.CevapSuresi.Gun);
    }

    private sealed class SahteYapayZeka(HamAnaliz? sonuc) : IYapayZekaAnalizcisi
    {
        public Task<HamAnaliz?> AnalizEtAsync(AnalizGirdisi g, CancellationToken ct = default) => Task.FromResult(sonuc);
    }

    private sealed class HataliYapayZeka : IYapayZekaAnalizcisi
    {
        public Task<HamAnaliz?> AnalizEtAsync(AnalizGirdisi g, CancellationToken ct = default) => throw new HttpRequestException("ağ hatası");
    }

    private static YaziAnalizServisi Servis(params IYapayZekaAnalizcisi[] ai) =>
        new(new KuralTabanliAnalizci(), ai, Hesaplayici(), NullLogger<YaziAnalizServisi>.Instance);

    [Fact]
    public async Task Yapay_zeka_ve_kural_farkli_son_gun_bulursa_erken_tarih_secilir_ve_teyit_istenir()
    {
        var ai = new HamAnaliz("AI konu", "AI özet", new CevapSuresiIfadesi(CevapSuresiTuru.TakvimGunu, 30, KaynakMetin: "30 gün"), AnalizKaynagi.YapayZeka);
        var s = await Servis(new SahteYapayZeka(ai)).AnalizEtAsync(new AnalizGirdisi("Konu: X\nBilgilerin 10 gün içinde iletilmesi.", null, An(2026, 11, 10), []));
        Assert.Equal("AI özet", s.Ozet);
        Assert.Equal(new DateOnly(2026, 11, 20), s.SonGun.SonGun);
        Assert.True(s.SonGun.TeyitGerekli);
        Assert.Equal(AnalizKaynagi.YapayZeka, s.Kaynak);
    }

    [Fact]
    public async Task Yapay_zeka_hata_verirse_kural_tabanli_analize_dusulur()
    {
        var s = await Servis(new HataliYapayZeka()).AnalizEtAsync(new AnalizGirdisi("Konu: X\nBilgilerin 10 gün içinde iletilmesi.", null, An(2026, 11, 10), []));
        Assert.Equal(AnalizKaynagi.Kural, s.Kaynak);
        Assert.Equal(new DateOnly(2026, 11, 20), s.SonGun.SonGun);
    }

    [Fact]
    public async Task Metin_ve_yapay_zeka_yoksa_varsayilan_sure_ile_teyit_istenir()
    {
        var s = await Servis().AnalizEtAsync(new AnalizGirdisi("", "KEP konusu", An(2026, 11, 10),
            [new GelenEk("yazi.pdf", "application/pdf", [1, 2, 3])]));
        Assert.Equal("KEP konusu", s.Konu);
        Assert.True(s.SonGun.TeyitGerekli);
    }
}

public class KlasorKaynagiTestleri
{
    [Fact]
    public async Task Json_paketi_ekleriyle_okunur_ve_ekler_ayri_yazi_sayilmaz()
    {
        var klasor = Directory.CreateTempSubdirectory("uets_").FullName;
        await File.WriteAllTextAsync(Path.Combine(klasor, "paket.json"), """
            { "disReferans": "B-1", "projeKodu": "SH-01", "tebligTarihi": "2026-11-10T10:00:00+03:00",
              "metin": "Konu: X\n5 gün içinde", "ekler": ["tebligat.pdf"] }
            """);
        await File.WriteAllBytesAsync(Path.Combine(klasor, "tebligat.pdf"), [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(klasor, "baska.txt"), "Konu: Y\n3 gün içinde");

        var kaynak = new IdareYazilari.Infrastructure.Kaynaklar.KlasorGelenYaziKaynagi(
            new() { Ad = "UETS", Kaynak = YaziKaynagi.Uets, Etkin = true, Klasor = klasor },
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        var paketler = await kaynak.YenileriGetirAsync();
        Assert.Equal(2, paketler.Count);
        var json = paketler.Single(p => p.Paket.DisReferans == "B-1").Paket;
        Assert.Equal(YaziKaynagi.Uets, json.Kaynak);
        Assert.Single(json.Ekler);

        foreach (var (anahtar, _) in paketler) await kaynak.IslendiAsync(anahtar, true, null);
        Assert.Empty(Directory.GetFiles(klasor));
        Assert.Equal(3, Directory.GetFiles(Path.Combine(klasor, "islendi")).Length);
        Assert.Empty(await kaynak.YenileriGetirAsync());
    }
}
