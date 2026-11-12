using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using IdareYazilari.Application.Dtos;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IdareYazilari.Tests;

public sealed class ApiFabrikasi : WebApplicationFactory<Program>
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"idare_test_{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:IdareYazilari", $"Data Source={_db}");
        builder.UseSetting("ArkaPlan:Etkin", "false");
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
    public async Task Elle_giris_ve_tam_akis_http_uzerinden_calisir()
    {
        var pm = Istemci(OrnekVeri.Pm1);
        var personel = Istemci(OrnekVeri.Personel1);
        var hukuk = Istemci(OrnekVeri.Hukuk1);

        // Proje müdürü doğrudan gelen yazıyı girer; süre metinden çıkarılır.
        var r = await pm.PostAsJsonAsync("/api/yazilar", new ElleYaziGirisi(YaziKaynagi.Dogrudan, null, "Valilik", "E-55", null,
            new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.FromHours(3)), null,
            "Konu: Yangın tatbikatı\nTatbikat raporunun 7 iş günü içinde iletilmesi rica olunur.", null, null), Json);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var d = (await r.Content.ReadFromJsonAsync<YaziDetayDto>(Json))!;
        var id = d.Ozet.Id;
        Assert.Equal("Yangın tatbikatı", d.Ozet.Konu);
        Assert.Equal(new DateOnly(2026, 11, 19), d.Ozet.CevapSonGunu);
        Assert.Equal(OrnekVeri.Proje1, d.Ozet.ProjeId);
        Assert.Contains("SorumluAta", d.YapilabilirIslemler);

        // Atanmadan önce personel göremez; hukuk erken onaylayamaz.
        Assert.Equal(HttpStatusCode.NotFound, (await personel.GetAsync($"/api/yazilar/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await hukuk.PostAsJsonAsync($"/api/yazilar/{id}/hukuk-onay", new { }, Json)).StatusCode);

        d = await Post(pm, $"/api/yazilar/{id}/sorumlu-ata", new { sorumluId = OrnekVeri.Personel1 });
        Assert.Equal(YaziDurumu.TaslakHazirlaniyor, d.Ozet.Durum);

        d = await Post(personel, $"/api/yazilar/{id}/taslak", new { icerik = "Cevap taslağı", hukukaGonder = true, surumNo = d.Ozet.SurumNo });
        Assert.Equal(YaziDurumu.HukukIncelemesinde, d.Ozet.Durum);

        // Eski sürüm numarasıyla işlem → 409
        var eski = await hukuk.PostAsJsonAsync($"/api/yazilar/{id}/hukuk-onay", new { surumNo = d.Ozet.SurumNo - 1 }, Json);
        Assert.Equal(HttpStatusCode.Conflict, eski.StatusCode);

        d = await Post(hukuk, $"/api/yazilar/{id}/hukuk-onay", new { aciklama = "Uygun", surumNo = d.Ozet.SurumNo });
        Assert.Equal(YaziDurumu.HukukOnayladi, d.Ozet.Durum);

        d = await Post(pm, $"/api/yazilar/{id}/idareye-gonder", new { gidenSayi = "2026/1", kanal = "KEP" });
        Assert.Equal(YaziDurumu.Gonderildi, d.Ozet.Durum);
        Assert.Empty(d.YapilabilirIslemler);

        // Diğer projenin müdürü yazıyı göremez.
        Assert.Equal(HttpStatusCode.NotFound, (await Istemci(OrnekVeri.Pm2).GetAsync($"/api/yazilar/{id}")).StatusCode);
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

        await Post(pm, $"/api/yazilar/{id}/sorumlu-ata", new { sorumluId = OrnekVeri.Personel2 });
        await Post(Istemci(OrnekVeri.Personel2), $"/api/yazilar/{id}/istisna", new { neden = "Hastaneden veri bekleniyor" });
        var d = await Post(hukuk, $"/api/yazilar/{id}/gecikme-bildirimi", new
        {
            gerekceYazisiMetni = "Gerekçeli yazı",
            taahhutTarihi = sonGun.AddDays(20),
            gidenSayi = "2026/9",
        });
        Assert.Equal(YaziDurumu.TaslakHazirlaniyor, d.Ozet.Durum);
        Assert.True(d.Ozet.SureUzatildi);
        Assert.Equal(sonGun.AddDays(20), d.Ozet.CevapSonGunu);
        Assert.Contains(d.Hatirlatmalar, h => h.Tur == HatirlatmaTuru.TaahhutEdilenSure);
        Assert.Equal(IstisnaDurumu.GecikmeBildirildi, d.Istisnalar.Single().Durum);
    }
}
