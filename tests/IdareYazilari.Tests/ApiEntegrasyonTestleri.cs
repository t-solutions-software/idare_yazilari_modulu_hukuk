using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Dtos;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IdareYazilari.Tests;

public sealed class ApiFabrikasi : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"idare_test_{Guid.NewGuid():N}.db");
    public readonly string KepGiden = Path.Combine(Path.GetTempPath(), $"idare_kep_{Guid.NewGuid():N}");
    public const string ApiAnahtari = "test-anahtari";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:IdareYazilari", $"Data Source={_db}");
        builder.UseSetting("ArkaPlan:Etkin", "false");
        builder.UseSetting("Auth:Mode", "Dev");
        builder.UseSetting("KepGonderim:Etkin", "true");
        builder.UseSetting("KepGonderim:GidenKlasor", KepGiden);
        builder.UseSetting("Entegrasyon:ApiAnahtarlari:0", ApiAnahtari);
        builder.UseSetting("OrnekVeri", "true");
        builder.UseSetting("GelenYaziKaynaklari:0:Etkin", "false");
        builder.UseSetting("GelenYaziKaynaklari:1:Etkin", "false");
        builder.UseSetting("Dosyalar:Klasor", Path.Combine(Path.GetTempPath(), "idare_test_ekler"));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_db); } catch { /* yoksay */ }
    }
}

public class ApiEntegrasyonTestleri(ApiFabrikasi fabrika) : IClassFixture<ApiFabrikasi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private HttpClient Istemci(Guid kullanici)
    {
        var c = fabrika.CreateClient();
        c.DefaultRequestHeaders.Add("X-Kullanici-Id", kullanici.ToString());
        c.DefaultRequestHeaders.Add("X-Idare", "1");
        return c;
    }

    private static async Task<YaziDetayDto> Post(HttpClient c, string yol, object govde)
    {
        var r = await c.PostAsJsonAsync(yol, govde, Json);
        Assert.True(r.IsSuccessStatusCode, $"{yol}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<YaziDetayDto>(Json))!;
    }

    [Fact]
    public async Task Kimliksiz_istek_reddedilir()
    {
        var r = await fabrika.CreateClient().GetAsync("/api/yazilar");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Elle_giris_ve_tam_onay_zinciri_http_uzerinden_calisir()
    {
        var pm = Istemci(OrnekVeri.Pm1);
        var personel = Istemci(OrnekVeri.Personel1);
        var birimYon = Istemci(OrnekVeri.TeknikMudur);
        var hukuk = Istemci(OrnekVeri.Hukuk1);
        var imza = Istemci(OrnekVeri.GenelMudur);

        // Proje müdürü doğrudan gelen yazıyı girer; süre metinden çıkarılır, evrak no verilir.
        var r = await pm.PostAsJsonAsync("/api/yazilar", new ElleYaziGirisi(YaziKaynagi.EPosta, null, "Valilik", "E-55", null,
            new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.FromHours(3)), null,
            "Konu: Yangın tatbikatı\nTatbikat raporunun 7 iş günü içinde iletilmesi rica olunur.", null, null,
            "Örnek Şehir Hastanesi", "SOZ-2025-099", "valilik@hs01.kep.tr"), Json);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var d = (await r.Content.ReadFromJsonAsync<YaziDetayDto>(Json))!;
        var id = d.Ozet.Id;
        Assert.Equal("Yangın tatbikatı", d.Ozet.Konu);
        Assert.Matches(@"^GLN-\d{4}-\d{6}$", d.Ozet.EvrakNo);
        Assert.Equal(new DateOnly(2026, 11, 19), d.Ozet.CevapSonGunu);
        Assert.Equal("SOZ-2025-099", d.Ozet.SozlesmeReferansi);

        // Atanmadan önce personel göremez; hukuk erken onaylayamaz.
        Assert.Equal(HttpStatusCode.NotFound, (await personel.GetAsync($"/api/yazilar/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hukuk.PostAsJsonAsync($"/api/yazilar/{id}/hukuk-onay", new { }, Json)).StatusCode);

        // Teknik birime ve Mehmet'e havale
        d = await Post(pm, $"/api/yazilar/{id}/havale", new { birimId = OrnekVeri.TeknikBirim, sorumluId = OrnekVeri.Personel1 });
        Assert.Equal(YaziDurumu.TaslakHazirlaniyor, d.Ozet.Durum);
        Assert.Equal("Teknik İşler (Şehir Hastanesi)", d.Ozet.BirimAdi);
        // Birim yöneticisi havale edilen yazıyı görür
        Assert.Equal(HttpStatusCode.OK, (await birimYon.GetAsync($"/api/yazilar/{id}")).StatusCode);

        // Word taslak belgesi üretilebilir
        d = await Post(personel, $"/api/yazilar/{id}/taslak", new { icerik = "Tatbikat raporu ektedir.\nBilgilerinize arz ederiz.", onayaGonder = true, surumNo = d.Ozet.SurumNo });
        Assert.Equal(YaziDurumu.BirimOnayinda, d.Ozet.Durum);
        var docx = await personel.GetAsync($"/api/yazilar/{id}/taslak.docx");
        Assert.Equal(HttpStatusCode.OK, docx.StatusCode);
        var bayt = await docx.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'P', bayt[0]); // zip/docx imzası
        Assert.Contains("Tatbikat raporu ektedir.", BelgeMetni(bayt));

        // Birim onayı → hukuk → imza
        Assert.Equal(HttpStatusCode.Forbidden, (await personel.PostAsJsonAsync($"/api/yazilar/{id}/birim-onay", new { }, Json)).StatusCode);
        d = await Post(birimYon, $"/api/yazilar/{id}/birim-onay", new { aciklama = "Uygun" });
        Assert.Equal(YaziDurumu.HukukIncelemesinde, d.Ozet.Durum);

        // Eski sürüm numarasıyla işlem → 409
        var eski = await hukuk.PostAsJsonAsync($"/api/yazilar/{id}/hukuk-onay", new { surumNo = d.Ozet.SurumNo - 1 }, Json);
        Assert.Equal(HttpStatusCode.Conflict, eski.StatusCode);

        d = await Post(hukuk, $"/api/yazilar/{id}/hukuk-onay", new { aciklama = "Uygun", surumNo = d.Ozet.SurumNo });
        Assert.Equal(YaziDurumu.ImzaBekliyor, d.Ozet.Durum);
        Assert.Equal(HttpStatusCode.OK, (await imza.GetAsync($"/api/yazilar/{id}")).StatusCode);

        var form = new MultipartFormDataContent { { new StringContent(d.Ozet.SurumNo.ToString()), "surumNo" } };
        form.Add(new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("application/pdf") } }, "dosya", "imzali.pdf");
        var ir = await imza.PostAsync($"/api/yazilar/{id}/imzala", form);
        Assert.Equal(HttpStatusCode.OK, ir.StatusCode);
        d = (await ir.Content.ReadFromJsonAsync<YaziDetayDto>(Json))!;
        Assert.Equal(YaziDurumu.GonderimeHazir, d.Ozet.Durum);
        var giden = d.GidenYazilar.Single();
        Assert.Matches(@"^SH-01-\d{4}/\d{5}$", giden.Sayi);
        Assert.Matches(@"^GDN-\d{4}-\d{6}$", giden.EvrakNo);
        Assert.Equal("Genel Müdür (İmza Yetkilisi)", giden.Imzalayan);
        Assert.NotNull(giden.ImzaliEkId);

        // KEP ile gönderim: .eml giden kutusuna bırakılır
        d = await Post(pm, $"/api/yazilar/{id}/idareye-gonder", new { kanal = "KEP" });
        Assert.Equal(YaziDurumu.Gonderildi, d.Ozet.Durum);
        Assert.Equal(GonderimDurumu.Gonderildi, d.GidenYazilar.Single().Durum);
        var eml = Path.Combine(fabrika.KepGiden, $"{giden.EvrakNo}.eml");
        Assert.Equal(eml, Directory.GetFiles(fabrika.KepGiden).Single()); // gönderimde yeni sayı üretilmemeli
        var mesaj = await MimeKit.MimeMessage.LoadAsync(eml);
        Assert.Equal("valilik@hs01.kep.tr", mesaj.To.Mailboxes.Single().Address);
        Assert.Equal("sehirhastanesi@hs01.kep.tr", mesaj.From.Mailboxes.Single().Address);
        Assert.Equal(2, mesaj.Attachments.Count()); // docx + imzalı pdf
        Assert.Equal(["BilgiDuzelt", "GorevEkle"], d.YapilabilirIslemler);

        // Diğer projenin müdürü yazıyı göremez.
        Assert.Equal(HttpStatusCode.NotFound, (await Istemci(OrnekVeri.Pm2).GetAsync($"/api/yazilar/{id}")).StatusCode);

        // Sözleşme kartı entegrasyonu: API anahtarıyla
        var ent = fabrika.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await ent.GetAsync("/api/entegrasyon/sozlesmeler/SOZ-2025-099/yazilar")).StatusCode);
        ent.DefaultRequestHeaders.Add("X-Api-Anahtari", ApiFabrikasi.ApiAnahtari);
        var kart = await ent.GetFromJsonAsync<List<SozlesmeYaziDto>>("/api/entegrasyon/sozlesmeler/SOZ-2025-099/yazilar", Json);
        Assert.Equal(id, Assert.Single(kart!).Id);
        Assert.Equal(giden.Sayi, kart![0].GidenSayi);
    }

    [Fact]
    public async Task Degistiren_istekler_csrf_basligi_ister()
    {
        var c = fabrika.CreateClient();
        c.DefaultRequestHeaders.Add("X-Kullanici-Id", OrnekVeri.Pm1.ToString());
        var r = await c.PostAsJsonAsync("/api/yazilar", new { metin = "x" }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Yazidan_gorev_uretilir_ve_operasyon_modulu_cekebilir()
    {
        var pm = Istemci(OrnekVeri.Pm2);
        var id = (await pm.GetFromJsonAsync<List<YaziOzetDto>>("/api/yazilar", Json))!.First().Id;
        var d = await Post(pm, $"/api/yazilar/{id}/gorevler", new
        {
            baslik = "Bakım raporunu hazırla", sorumluEposta = "Teknik@Example.com", termin = DateOnly.FromDateTime(DateTime.Today.AddDays(5)),
        });
        var gorev = Assert.Single(d.Gorevler);
        Assert.Equal("teknik@example.com", gorev.SorumluEposta);
        Assert.Equal(AktarimDurumu.Bekliyor, gorev.AktarimDurumu);

        var ent = fabrika.CreateClient();
        ent.DefaultRequestHeaders.Add("X-Api-Anahtari", ApiFabrikasi.ApiAnahtari);
        var bekleyen = await ent.GetFromJsonAsync<List<GorevAktarimPaketi>>("/api/entegrasyon/operasyon/gorevler", Json);
        var paket = Assert.Single(bekleyen!, p => p.SourceKey == gorev.DisAnahtar);
        Assert.Equal("IdareYazilari", paket.SourceSystem);
        Assert.Contains(d.Ozet.EvrakNo, paket.Description);

        var ack = await ent.PostAsJsonAsync("/api/entegrasyon/operasyon/gorevler/alindi", new { sourceKey = gorev.DisAnahtar }, Json);
        Assert.Equal(HttpStatusCode.NoContent, ack.StatusCode);
        bekleyen = await ent.GetFromJsonAsync<List<GorevAktarimPaketi>>("/api/entegrasyon/operasyon/gorevler", Json);
        Assert.DoesNotContain(bekleyen!, p => p.SourceKey == gorev.DisAnahtar);
    }

    private static string BelgeMetni(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(ms, false);
        return doc.MainDocumentPart!.Document.InnerText;
    }

    [Fact]
    public async Task Proje_muduru_yalnizca_kendi_projesinin_yazilarini_listeler()
    {
        var liste = await Istemci(OrnekVeri.Pm2).GetFromJsonAsync<List<YaziOzetDto>>("/api/yazilar", Json);
        Assert.NotNull(liste);
        Assert.NotEmpty(liste!);
        Assert.All(liste!, y => Assert.Equal(OrnekVeri.Proje2, y.ProjeId));
        // Örnek "ivedilikle" yazısı süre teyidi ister.
        Assert.Contains(liste!, y => y.SureTeyitGerekli);
    }

    [Fact]
    public async Task Istisna_akisi_http_uzerinden_calisir()
    {
        var pm = Istemci(OrnekVeri.Pm1);
        var hukuk = Istemci(OrnekVeri.Hukuk2);
        var liste = (await pm.GetFromJsonAsync<List<YaziOzetDto>>("/api/yazilar?durum=AtamaBekliyor", Json))!;
        var id = liste.First(y => y.Kaynak == YaziKaynagi.Uets).Id;
        var sonGun = liste.First(y => y.Id == id).CevapSonGunu;

        await Post(pm, $"/api/yazilar/{id}/havale", new { sorumluId = OrnekVeri.Personel2 });
        await Post(Istemci(OrnekVeri.Personel2), $"/api/yazilar/{id}/istisna", new { neden = "Hastaneden veri bekleniyor" });
        var d = await Post(hukuk, $"/api/yazilar/{id}/gecikme-bildirimi", new
        {
            gerekceYazisiMetni = "Gerekçeli yazı",
            taahhutTarihi = sonGun.AddDays(20),
        });
        Assert.Equal(YaziDurumu.TaslakHazirlaniyor, d.Ozet.Durum);
        Assert.True(d.Ozet.SureUzatildi);
        Assert.Equal(sonGun.AddDays(20), d.Ozet.CevapSonGunu);
        Assert.Contains(d.Hatirlatmalar, h => h.Tur == HatirlatmaTuru.TaahhutEdilenSure);
        Assert.Equal(IstisnaDurumu.GecikmeBildirildi, d.Istisnalar.Single().Durum);
        // Gecikme yazısına otomatik sayı verildi; kanal seçilmediği için gönderim bekliyor
        var giden = Assert.Single(d.GidenYazilar);
        Assert.Equal(GidenYaziTuru.GecikmeBildirimi, giden.Tur);
        Assert.Equal(GonderimDurumu.Bekliyor, giden.Durum);
        Assert.Contains("IstisnaYazisiGonder", d.YapilabilirIslemler);
    }
}
