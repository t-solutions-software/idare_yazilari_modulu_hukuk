using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Dtos;
using IdareYazilari.Application.Services;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using IdareYazilari.Domain.Services;
using IdareYazilari.Infrastructure.ArkaPlan;
using IdareYazilari.Infrastructure.Belgeler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Api.Altyapi;

public static class YaziUclari
{
    private const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static void Esle(WebApplication app, string authMode)
    {
        var gelistirme = authMode.Equals("Dev", StringComparison.OrdinalIgnoreCase);
        var api = app.MapGroup("/api").RequireAuthorization();

        // Ekranın çalışma biçimi (kimlik modu, gönderim kanalları, onay zinciri).
        app.MapGet("/api/ortam", (IEnumerable<IGidenYaziGonderici> g, OnayAkisiAyarlari akis) => new
        {
            authMode,
            otomatikKanallar = g.Select(x => x.Kanal),
            akis.BirimOnayi,
            akis.ImzaYetkilisiOnayi,
            akis.ImzaliDosyaZorunlu,
        }).AllowAnonymous().WithTags("Genel");

        if (gelistirme)
            app.MapGet("/api/gelistirme/kullanicilar", async (IKullaniciDeposu d, CancellationToken ct) =>
                (await d.ListeleAsync(ct: ct)).Select(KullaniciDto)).AllowAnonymous().WithTags("Geliştirme");

        api.MapGet("/ben", async (IMevcutKullanici m, CancellationToken ct) => KullaniciDto(await m.GetirAsync(ct))).WithTags("Genel");
        api.MapGet("/projeler", async (IKullaniciDeposu d, CancellationToken ct) =>
            (await d.ProjeleriListeleAsync(ct)).Select(p => new ProjeDto(p.Id, p.Kod, p.Ad, p.ProjeMuduruId))).WithTags("Genel");
        api.MapGet("/birimler", async (Guid? projeId, IKullaniciDeposu d, CancellationToken ct) =>
            (await d.BirimleriListeleAsync(projeId, ct)).Where(b => b.Aktif).Select(BirimDto)).WithTags("Genel");
        api.MapGet("/kullanicilar", async (Guid? projeId, Guid? birimId, KullaniciRolu? rol, IKullaniciDeposu d, CancellationToken ct) =>
            (await d.ListeleAsync(projeId, rol, ct)).Where(k => k.Aktif && (birimId is null || k.BirimId == birimId)).Select(KullaniciDto)).WithTags("Genel");
        api.MapGet("/bildirimlerim", (int? gun, YaziSorguServisi s, CancellationToken ct) => s.BildirimlerimAsync(gun ?? 7, ct)).WithTags("Genel");

        var y = api.MapGroup("/yazilar").WithTags("İdare Yazıları");

        y.MapGet("/", ([FromQuery] YaziDurumu[]? durum, bool? sadeceAcik, string? arama, Guid? projeId, string? sozlesme, YaziSorguServisi s, CancellationToken ct) =>
            s.ListeleAsync(new YaziFiltresi(projeId, null, durum, sadeceAcik ?? false, arama, sozlesme), ct));

        y.MapGet("/{id:guid}", (Guid id, YaziSorguServisi s, CancellationToken ct) => s.DetayAsync(id, ct));

        y.MapPost("/", async (ElleYaziGirisi giris, YaziKayitServisi k, YaziSorguServisi s, CancellationToken ct) =>
        {
            var yazi = await k.ElleKaydetAsync(giris, [], ct);
            return Results.Created($"/api/yazilar/{yazi.Id}", await s.DetayOlusturAsync(yazi, ct));
        });

        // Ek dosyalı (PDF vb.) gelen evrak girişi: multipart/form-data
        y.MapPost("/dosyali", async (HttpRequest req, YaziKayitServisi k, YaziSorguServisi s, CancellationToken ct) =>
        {
            var form = await req.ReadFormAsync(ct);
            var ekler = await EkleriOku(form.Files, ct);
            string? Al(string ad) => string.IsNullOrWhiteSpace(form[ad]) ? null : form[ad].ToString();
            var giris = new ElleYaziGirisi(
                Enum.TryParse<YaziKaynagi>(Al("kaynak"), out var kay) ? kay : YaziKaynagi.Dogrudan,
                Guid.TryParse(Al("projeId"), out var pid) ? pid : null,
                Al("gonderenIdare"), Al("gelenSayi"),
                DateOnly.TryParse(Al("yaziTarihi"), out var yt) ? yt : null,
                DateTimeOffset.TryParse(Al("tebligTarihi"), out var tt) ? tt : null,
                Al("konu"), Al("metin") ?? "",
                DateOnly.TryParse(Al("cevapSonGunu"), out var sg) ? sg : null,
                Al("disReferans"), Al("lokasyon"), Al("sozlesmeReferansi"), Al("gonderenAdres"));
            var yazi = await k.ElleKaydetAsync(giris, ekler, ct);
            return Results.Created($"/api/yazilar/{yazi.Id}", await s.DetayOlusturAsync(yazi, ct));
        }).DisableAntiforgery();

        y.MapGet("/{id:guid}/ekler/{ekId:guid}", async (Guid id, Guid ekId, IYaziDeposu d, YetkiServisi yetki, IDosyaDeposu dosyalar, CancellationToken ct) =>
        {
            var b = await yetki.BaglamAsync(ct);
            var yazi = await d.GetirAsync(id, ct);
            if (yazi is null || !Yetki.GorebilirMi(b, yazi)) throw new BulunamadiHatasi("Yazı bulunamadı.");
            var ek = yazi.Ekler.FirstOrDefault(e => e.Id == ekId) ?? throw new BulunamadiHatasi("Ek bulunamadı.");
            var akim = await dosyalar.AcAsync(ek.DepoAnahtari, ct) ?? throw new BulunamadiHatasi("Dosya bulunamadı.");
            return Results.File(akim, ek.IcerikTuru, ek.DosyaAdi);
        });

        // Word belgeleri (kurum şablonundan)
        y.MapGet("/{id:guid}/taslak.docx", async (Guid id, BelgeServisi b, CancellationToken ct) =>
        {
            var (icerik, ad) = await b.TaslakBelgesiAsync(id, ct);
            return Results.File(icerik, Docx, ad);
        });
        y.MapGet("/{id:guid}/giden/{gidenId:guid}.docx", async (Guid id, Guid gidenId, BelgeServisi b, CancellationToken ct) =>
        {
            var (icerik, ad) = await b.GidenBelgesiAsync(id, gidenId, ct);
            return Results.File(icerik, Docx, ad);
        });

        // --- İş akışı adımları (hepsi güncel yazı detayını döner)
        Islem<BilgiDuzeltKomutu>(y, "bilgi-duzelt", (s, id, k, ct) => s.BilgileriDuzeltAsync(id, k, ct));
        Islem<ProjeAtaKomutu>(y, "proje-ata", (s, id, k, ct) => s.ProjeAtaAsync(id, k, ct));
        Islem<SorumluAtaKomutu>(y, "havale", (s, id, k, ct) => s.SorumluAtaAsync(id, k, ct));
        Islem<SorumluAtaKomutu>(y, "sorumlu-ata", (s, id, k, ct) => s.SorumluAtaAsync(id, k, ct));
        Islem<TaslakKomutu>(y, "taslak", (s, id, k, ct) => s.TaslakKaydetAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "onaya-gonder", (s, id, k, ct) => s.OnayaGonderAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "birim-onay", (s, id, k, ct) => s.BirimOnaylaAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "birim-iade", (s, id, k, ct) => s.BirimIadeAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "hukuk-onay", (s, id, k, ct) => s.HukukOnaylaAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "revizyon-talebi", (s, id, k, ct) => s.RevizyonTalepEtAsync(id, k, ct));
        Islem<RevizeKomutu>(y, "revize-et-onayla", (s, id, k, ct) => s.RevizeEtVeOnaylaAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "imza-iade", (s, id, k, ct) => s.ImzaIadeAsync(id, k, ct));
        Islem<GonderimKomutu>(y, "idareye-gonder", (s, id, k, ct) => s.IdareyeGonderAsync(id, k, ct));
        Islem<IstisnaBildirKomutu>(y, "istisna", (s, id, k, ct) => s.IstisnaBildirAsync(id, k, ct));
        Islem<AciklamaKomutu>(y, "istisna-reddet", (s, id, k, ct) => s.IstisnaReddetAsync(id, k, ct));
        Islem<EkSureTalepKomutu>(y, "ek-sure-talebi", (s, id, k, ct) => s.EkSureTalepEtAsync(id, k, ct));
        Islem<GecikmeBildirKomutu>(y, "gecikme-bildirimi", (s, id, k, ct) => s.GecikmeBildirAsync(id, k, ct));
        Islem<EkSureYanitKomutu>(y, "ek-sure-yaniti", (s, id, k, ct) => s.EkSureYanitiKaydetAsync(id, k, ct));
        Islem<IstisnaYazisiGonderKomutu>(y, "istisna-yazisi-gonder", (s, id, k, ct) => s.IstisnaYazisiGonderAsync(id, k, ct));
        Islem<GorevKomutu>(y, "gorevler", (s, id, k, ct) => s.GorevEkleAsync(id, k, ct));

        // İmza: isteğe bağlı e-imzalı dosya ile (multipart/form-data: surumNo, dosya)
        y.MapPost("/{id:guid}/imzala", async (Guid id, HttpRequest req, YaziIsAkisiServisi s, YaziSorguServisi q, CancellationToken ct) =>
        {
            int? surum = null;
            GelenEk? dosya = null;
            if (req.HasFormContentType)
            {
                var form = await req.ReadFormAsync(ct);
                surum = int.TryParse(form["surumNo"], out var sn) ? sn : null;
                dosya = (await EkleriOku(form.Files, ct)).FirstOrDefault();
            }
            return await q.DetayOlusturAsync(await s.ImzalaAsync(id, surum, dosya, ct), ct);
        }).DisableAntiforgery();

        // --- Yönetim (yalnız yönetici): kullanıcı, birim, proje tanımları ve Word şablonu
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
        yonetim.MapPost("/gorevleri-aktar", async (IMevcutKullanici m, GorevAktarimServisi g, CancellationToken ct) =>
        {
            await YoneticiKontrol(m, ct);
            return Results.Ok(new { aktarilan = await g.CalistirAsync(ct) });
        });
        yonetim.MapGet("/sablon.docx", async (IMevcutKullanici m, IOptions<BelgeAyarlari> ayar, CancellationToken ct) =>
        {
            await YoneticiKontrol(m, ct);
            var yol = ayar.Value.SablonYolu;
            var icerik = !string.IsNullOrWhiteSpace(yol) && File.Exists(yol) ? await File.ReadAllBytesAsync(yol, ct) : WordBelgeOlusturucu.VarsayilanSablon();
            return Results.File(icerik, Docx, "idare_yazisi_sablonu.docx");
        });
        yonetim.MapPut("/kullanicilar/{id:guid}", async (Guid id, KullaniciDto k, IMevcutKullanici m, IKullaniciDeposu d, CancellationToken ct) =>
        {
            await YoneticiKontrol(m, ct);
            var mevcut = await d.GetirAsync(id, ct);
            var varlik = mevcut ?? new Kullanici { Id = id };
            varlik.AdSoyad = k.AdSoyad; varlik.Eposta = k.Eposta.Trim().ToLowerInvariant(); varlik.Rol = k.Rol;
            varlik.ProjeId = k.ProjeId; varlik.BirimId = k.BirimId;
            await d.KaydetAsync(varlik, mevcut is null, ct);
            return KullaniciDto(varlik);
        });
        yonetim.MapDelete("/kullanicilar/{id:guid}", async (Guid id, IMevcutKullanici m, IKullaniciDeposu d, CancellationToken ct) =>
        {
            await YoneticiKontrol(m, ct);
            var k = await d.GetirAsync(id, ct) ?? throw new BulunamadiHatasi("Kullanıcı bulunamadı.");
            k.Aktif = false;
            await d.KaydetAsync(k, false, ct);
            return Results.NoContent();
        });
        yonetim.MapPut("/birimler/{id:guid}", async (Guid id, BirimDto b, IMevcutKullanici m, IKullaniciDeposu d, CancellationToken ct) =>
        {
            await YoneticiKontrol(m, ct);
            var mevcut = await d.BirimGetirAsync(id, ct);
            var varlik = mevcut ?? new Birim { Id = id };
            varlik.Kod = b.Kod; varlik.Ad = b.Ad; varlik.ProjeId = b.ProjeId; varlik.YoneticiId = b.YoneticiId;
            await d.KaydetAsync(varlik, mevcut is null, ct);
            return BirimDto(varlik);
        });
        yonetim.MapPut("/projeler/{id:guid}", async (Guid id, ProjeKaydi p, IMevcutKullanici m, IKullaniciDeposu d, CancellationToken ct) =>
        {
            await YoneticiKontrol(m, ct);
            var mevcut = await d.ProjeGetirAsync(id, ct);
            var varlik = mevcut ?? new Proje { Id = id };
            varlik.Kod = p.Kod; varlik.Ad = p.Ad; varlik.ProjeMuduruId = p.ProjeMuduruId; varlik.KepAdresi = p.KepAdresi; varlik.UetsAdresi = p.UetsAdresi;
            await d.KaydetAsync(varlik, mevcut is null, ct);
            return Results.Ok(varlik);
        });

        // --- Modüller arası entegrasyon (X-Api-Anahtari ile)
        var ent = app.MapGroup("/api/entegrasyon").RequireAuthorization("Entegrasyon").WithTags("Entegrasyon");
        // Hukuk / sözleşme modülü kartı: sözleşme referansına bağlı idare yazıları
        ent.MapGet("/sozlesmeler/{referans}/yazilar", (string referans, YaziSorguServisi s, IOptions<EntegrasyonAyarlari> a, CancellationToken ct) =>
            s.SozlesmeYazilariAsync(referans, a.Value.UygulamaAdresi, ct));
        // Operasyon modülü (çekme modu): aktarılmayı bekleyen görevler; SourceSystem = "IdareYazilari"
        ent.MapGet("/operasyon/gorevler", async (IYaziDeposu d, CancellationToken ct) =>
        {
            var gorevler = await d.GorevleriListeleAsync(null, ct);
            var sonuc = new List<GorevAktarimPaketi>();
            foreach (var g in gorevler.Where(g => g.AktarimDurumu != AktarimDurumu.Aktarildi))
            {
                var yazi = await d.GetirAsync(g.YaziId, ct);
                if (yazi is not null) sonuc.Add(GorevAktarimServisi.Paket(yazi.EvrakNo, yazi.GelenSayi, g));
            }
            return sonuc;
        });
        ent.MapPost("/operasyon/gorevler/alindi", async (GorevAlindiKomutu k, GorevAktarimServisi g, CancellationToken ct) =>
            await g.AlindiAsync(k.SourceKey, ct) ? Results.NoContent() : Results.NotFound());
    }

    private static async Task<List<GelenEk>> EkleriOku(IFormFileCollection dosyalar, CancellationToken ct)
    {
        var ekler = new List<GelenEk>();
        foreach (var f in dosyalar)
        {
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            ekler.Add(new GelenEk(f.FileName, string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType, ms.ToArray()));
        }
        return ekler;
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

    private static KullaniciDto KullaniciDto(Kullanici k) => new(k.Id, k.AdSoyad, k.Eposta, k.Rol, k.ProjeId, k.BirimId);
    private static BirimDto BirimDto(Birim b) => new(b.Id, b.Kod, b.Ad, b.ProjeId, b.YoneticiId);

    public sealed record ProjeKaydi(string Kod, string Ad, Guid? ProjeMuduruId, string? KepAdresi, string? UetsAdresi);
    public sealed record GorevAlindiKomutu(string SourceKey);
}
