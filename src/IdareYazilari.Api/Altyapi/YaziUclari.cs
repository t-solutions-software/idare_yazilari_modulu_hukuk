using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Dtos;
using IdareYazilari.Application.Services;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using IdareYazilari.Infrastructure.ArkaPlan;
using Microsoft.AspNetCore.Mvc;

namespace IdareYazilari.Api.Altyapi;

public static class YaziUclari
{
    public static void Esle(WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // Geliştirme ekranındaki kullanıcı seçici için (kimlik doğrulamasız).
        app.MapGet("/api/gelistirme/kullanicilar", async (IKullaniciDeposu d, CancellationToken ct) =>
            (await d.ListeleAsync(ct: ct)).Select(KullaniciDto)).AllowAnonymous().WithTags("Geliştirme");

        api.MapGet("/ben", async (IMevcutKullanici m, CancellationToken ct) => KullaniciDto(await m.GetirAsync(ct))).WithTags("Genel");
        api.MapGet("/projeler", async (IKullaniciDeposu d, CancellationToken ct) =>
            (await d.ProjeleriListeleAsync(ct)).Select(p => new ProjeDto(p.Id, p.Kod, p.Ad, p.ProjeMuduruId))).WithTags("Genel");
        api.MapGet("/kullanicilar", async (Guid? projeId, KullaniciRolu? rol, IKullaniciDeposu d, CancellationToken ct) =>
            (await d.ListeleAsync(projeId, rol, ct)).Where(k => k.Aktif).Select(KullaniciDto)).WithTags("Genel");
        api.MapGet("/bildirimlerim", (int? gun, YaziSorguServisi s, CancellationToken ct) => s.BildirimlerimAsync(gun ?? 7, ct)).WithTags("Genel");

        var y = api.MapGroup("/yazilar").WithTags("İdare Yazıları");

        y.MapGet("/", ([FromQuery] YaziDurumu[]? durum, bool? sadeceAcik, string? arama, Guid? projeId, YaziSorguServisi s, CancellationToken ct) =>
            s.ListeleAsync(new YaziFiltresi(projeId, null, durum, sadeceAcik ?? false, arama), ct));

        y.MapGet("/{id:guid}", (Guid id, YaziSorguServisi s, CancellationToken ct) => s.DetayAsync(id, ct));

        y.MapPost("/", async (ElleYaziGirisi giris, YaziKayitServisi k, YaziSorguServisi s, CancellationToken ct) =>
        {
            var yazi = await k.ElleKaydetAsync(giris, [], ct);
            return Results.Created($"/api/yazilar/{yazi.Id}", await s.DetayOlusturAsync(yazi, ct));
        });

        // Ek dosyalı (PDF vb.) doğrudan giriş: multipart/form-data
        y.MapPost("/dosyali", async (HttpRequest req, YaziKayitServisi k, YaziSorguServisi s, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var ekler = new List<GelenEk>();
            foreach (var f in form.Files)
            {
                using var ms = new MemoryStream();
                await f.CopyToAsync(ms, ct);
                ekler.Add(new GelenEk(f.FileName, string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType, ms.ToArray()));
            }
            string? Al(string ad) => string.IsNullOrWhiteSpace(form[ad]) ? null : form[ad].ToString();
            var giris = new ElleYaziGirisi(
                Enum.TryParse<YaziKaynagi>(Al("kaynak"), out var kay) ? kay : YaziKaynagi.Dogrudan,
                Guid.TryParse(Al("projeId"), out var pid) ? pid : null,
                Al("gonderenIdare"), Al("gelenSayi"),
                DateOnly.TryParse(Al("yaziTarihi"), out var yt) ? yt : null,
                DateTimeOffset.TryParse(Al("tebligTarihi"), out var tt) ? tt : null,
                Al("konu"), Al("metin") ?? "",
                DateOnly.TryParse(Al("cevapSonGunu"), out var sg) ? sg : null,
                Al("disReferans"));
            var yazi = await k.ElleKaydetAsync(giris, ekler, ct);
            return Results.Created($"/api/yazilar/{yazi.Id}", await s.DetayOlusturAsync(yazi, ct));
        }).DisableAntiforgery();

        y.MapGet("/{id:guid}/ekler/{ekId:guid}", async (Guid id, Guid ekId, IYaziDeposu d, IMevcutKullanici m, IDosyaDeposu dosyalar, CancellationToken ct) =>
        {
            var k = await m.GetirAsync(ct);
            var yazi = await d.GetirAsync(id, ct);
            if (yazi is null || !Yetki.GorebilirMi(k, yazi)) throw new BulunamadiHatasi("Yazı bulunamadı.");
            var ek = yazi.Ekler.FirstOrDefault(e => e.Id == ekId) ?? throw new BulunamadiHatasi("Ek bulunamadı.");
            var akim = await dosyalar.AcAsync(ek.DepoAnahtari, ct) ?? throw new BulunamadiHatasi("Dosya bulunamadı.");
            return Results.File(akim, ek.IcerikTuru, ek.DosyaAdi);
        });

        // --- İş akışı adımları (hepsi güncel yazı detayını döner)
        Islem<BilgiDuzeltKomutu>(y, "bilgi-duzelt", (s, id, k, ct) => s.BilgileriDuzeltAsync(id, k, ct));
        Islem<ProjeAtaKomutu>(y, "proje-ata", (s, id, k, ct) => s.ProjeAtaAsync(id, k, ct));
        Islem<SorumluAtaKomutu>(y, "sorumlu-ata", (s, id, k, ct) => s.SorumluAtaAsync(id, k, ct));
        Islem<TaslakKomutu>(y, "taslak", (s, id, k, ct) => s.TaslakKaydetAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "hukuka-gonder", (s, id, k, ct) => s.HukukaGonderAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "hukuk-onay", (s, id, k, ct) => s.HukukOnaylaAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "revizyon-talebi", (s, id, k, ct) => s.RevizyonTalepEtAsync(id, k, ct));
        Islem<RevizeKomutu>(y, "revize-et-onayla", (s, id, k, ct) => s.RevizeEtVeOnaylaAsync(id, k, ct));
        Islem<GonderimKomutu>(y, "idareye-gonder", (s, id, k, ct) => s.IdareyeGonderAsync(id, k, ct));
        Islem<IstisnaBildirKomutu>(y, "istisna", (s, id, k, ct) => s.IstisnaBildirAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "istisna-reddet", (s, id, k, ct) => s.IstisnaReddetAsync(id, k, ct));
        Islem<EkSureTalepKomutu>(y, "ek-sure-talebi", (s, id, k, ct) => s.EkSureTalepEtAsync(id, k, ct));
        Islem<GecikmeBildirKomutu>(y, "gecikme-bildirimi", (s, id, k, ct) => s.GecikmeBildirAsync(id, k, ct));
        Islem<EkSureYanitKomutu>(y, "ek-sure-yaniti", (s, id, k, ct) => s.EkSureYanitiKaydetAsync(id, k, ct));

        // --- Yönetim
        var yonetim = api.MapGroup("/yonetim").WithTags("Yönetim");
        yonetim.MapPost("/kaynaklari-tara", async (IMevcutKullanici m, IServiceProvider sp, CancellationToken ct) =>
        {
            await YoneticiKontrol(m, ct);
            var servis = sp.GetService<GelenYaziToplamaServisi>() ?? throw new IsKuraliHatasi("Gelen yazı toplama servisi devre dışı.");
            await servis.BirTurAsync(ct);
            return Results.NoContent();
        });
        yonetim.MapPost("/hatirlatmalari-calistir", async (IMevcutKullanici m, HatirlatmaGonderimServisi h, CancellationToken ct) =>
        {
            await YoneticiKontrol(m, ct);
            return Results.Ok(new { gonderilen = await h.CalistirAsync(ct) });
        });
    }

    private static void Islem<TKomut>(RouteGroupBuilder g, string yol,
        Func<YaziIsAkisiServisi, Guid, TKomut, CancellationToken, Task<IdareYazisi>> islem) =>
        g.MapPost($"/{{id:guid}}/{yol}", async (Guid id, TKomut komut, YaziIsAkisiServisi s, YaziSorguServisi q, CancellationToken ct) =>
            await q.DetayOlusturAsync(await islem(s, id, komut, ct), ct));

    private static async Task YoneticiKontrol(IMevcutKullanici m, CancellationToken ct)
    {
        if ((await m.GetirAsync(ct)).Rol != KullaniciRolu.Yonetici)
            throw new YetkiHatasi("Bu işlem yalnızca yönetici tarafından yapılabilir.");
    }

    private static KullaniciDto KullaniciDto(Kullanici k) => new(k.Id, k.AdSoyad, k.Eposta, k.Rol, k.ProjeId);
}
