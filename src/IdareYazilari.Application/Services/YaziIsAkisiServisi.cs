using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Dtos;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using IdareYazilari.Domain.Services;
using Microsoft.Extensions.Logging;

namespace IdareYazilari.Application.Services;

/// <summary>
/// Yazı üzerindeki tüm iş akışı adımları: havale, taslak, birim onayı, hukuk, imza, sayı verme,
/// gönderim, istisnalar ve görev üretimi.
/// </summary>
public class YaziIsAkisiServisi(
    IYaziDeposu depo,
    IKullaniciDeposu kullanicilar,
    IDosyaDeposu dosyalar,
    IEvrakNumaratoru numarator,
    BelgeServisi belgeler,
    IEnumerable<IGidenYaziGonderici> gondericiler,
    SureHesaplayici hesaplayici,
    HatirlatmaPlanlayici planlayici,
    OnayAkisiAyarlari akis,
    YetkiServisi yetki,
    ISaat saat,
    ILogger<YaziIsAkisiServisi> log)
{
    public Task<IdareYazisi> BilgileriDuzeltAsync(Guid id, BilgiDuzeltKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.BilgiDuzelt, (b, y, simdi) =>
        {
            y.BilgileriDuzelt(k.Konu, k.Ozet, k.CevapSonGunu, b.Id, hesaplayici, simdi, k.SozlesmeReferansi, k.Lokasyon);
            return Task.CompletedTask;
        }, ct);

    public Task<IdareYazisi> ProjeAtaAsync(Guid id, ProjeAtaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.ProjeAta, async (b, y, simdi) =>
        {
            _ = await kullanicilar.ProjeGetirAsync(k.ProjeId, ct) ?? throw new BulunamadiHatasi("Proje bulunamadı.");
            y.ProjeAta(k.ProjeId, b.Id, simdi);
        }, ct);

    /// <summary>Birime ve/veya kişiye havale. Kişi verilmezse birim yöneticisi sorumlu olur.</summary>
    public Task<IdareYazisi> SorumluAtaAsync(Guid id, SorumluAtaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.SorumluAta, async (b, y, simdi) =>
        {
            Birim? birim = null;
            if (k.BirimId is { } birimId)
            {
                birim = await kullanicilar.BirimGetirAsync(birimId, ct) ?? throw new BulunamadiHatasi("Birim bulunamadı.");
                if (!birim.Aktif || (birim.ProjeId is not null && birim.ProjeId != y.ProjeId))
                    throw new IsKuraliHatasi("Birim bu projeye ait değil veya pasif.");
            }
            var hedefBirim = birim ?? (y.BirimId is { } mevcutBirim ? await kullanicilar.BirimGetirAsync(mevcutBirim, ct) : null);

            // Birim yöneticisi yalnız kendi birimi içinde havaleyi değiştirebilir.
            if (!Yetki.ProjeYetkilisi(b, y) && (hedefBirim is null || !b.YonettigiBirimler.Contains(hedefBirim.Id)))
                throw new YetkiHatasi("Yalnız yöneticisi olduğunuz birim içinde havale yapabilirsiniz.");

            var sorumluId = k.SorumluId ?? birim?.YoneticiId
                ?? throw new IsKuraliHatasi("Sorumlu kişi seçilmeli (birimin tanımlı yöneticisi yok).");
            var sorumlu = await kullanicilar.GetirAsync(sorumluId, ct) ?? throw new BulunamadiHatasi("Kullanıcı bulunamadı.");
            var kapsamda = hedefBirim is not null
                ? sorumlu.BirimId == hedefBirim.Id || hedefBirim.YoneticiId == sorumlu.Id
                : sorumlu.ProjeId == y.ProjeId;
            if (!sorumlu.Aktif || sorumlu.Rol is not (KullaniciRolu.Personel or KullaniciRolu.ProjeMuduru) || !kapsamda)
                throw new IsKuraliHatasi(hedefBirim is null
                    ? "Sorumlu, yazının ait olduğu projenin aktif personeli veya proje müdürü olmalıdır."
                    : "Sorumlu, havale edilen birimin aktif personeli veya yöneticisi olmalıdır.");
            y.SorumluAta(sorumlu.Id, b.Id, simdi, birim?.Id);
        }, ct);

    public Task<IdareYazisi> TaslakKaydetAsync(Guid id, TaslakKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.TaslakKaydet, (b, y, simdi) =>
        {
            y.TaslakKaydet(k.Icerik, b.Id, b.Rol, simdi);
            if (k.OnayaGonder) y.OnayaGonder(b.Id, Yetki.BirimOnayiGerekli(akis, b, y), simdi);
            return Task.CompletedTask;
        }, ct);

    public Task<IdareYazisi> OnayaGonderAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.OnayaGonder, (b, y, simdi) =>
        {
            y.OnayaGonder(b.Id, Yetki.BirimOnayiGerekli(akis, b, y), simdi);
            return Task.CompletedTask;
        }, ct);

    public Task<IdareYazisi> BirimOnaylaAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.BirimOnayla, (b, y, simdi) => { y.BirimOnayla(b.Id, k.Aciklama, simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> BirimIadeAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.BirimIade, (b, y, simdi) => { y.BirimIade(b.Id, k.Aciklama ?? "", simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> HukukOnaylaAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.HukukOnayla, (b, y, simdi) =>
        {
            y.HukukOnayla(b.Id, k.Aciklama, akis.ImzaYetkilisiOnayi, simdi);
            return Task.CompletedTask;
        }, ct);

    public Task<IdareYazisi> RevizyonTalepEtAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.RevizyonTalepEt, (b, y, simdi) => { y.RevizyonTalepEt(b.Id, k.Aciklama ?? "", simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> RevizeEtVeOnaylaAsync(Guid id, RevizeKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.RevizeEtVeOnayla, (b, y, simdi) =>
        {
            y.RevizeEtVeOnayla(b.Id, k.Icerik, k.Aciklama, akis.ImzaYetkilisiOnayi, simdi);
            return Task.CompletedTask;
        }, ct);

    /// <summary>İmza yetkilisi imzalar; sayı ve evrak no verilir. İsteğe bağlı e-imzalı dosya eklenir.</summary>
    public Task<IdareYazisi> ImzalaAsync(Guid id, int? surumNo, GelenEk? imzaliDosya, CancellationToken ct = default) =>
        Uygula(id, surumNo, Islemler.Imzala, async (b, y, simdi) =>
        {
            if (akis.ImzaliDosyaZorunlu && imzaliDosya is null)
                throw new IsKuraliHatasi("İmzalı dosya (e-imzalı PDF / .imz) yüklenmelidir.");
            Guid? ekId = null;
            if (imzaliDosya is not null)
            {
                var anahtar = await dosyalar.KaydetAsync(y.Id, imzaliDosya.DosyaAdi, imzaliDosya.Icerik, ct);
                var ek = new Ek { DosyaAdi = imzaliDosya.DosyaAdi, IcerikTuru = imzaliDosya.IcerikTuru, Boyut = imzaliDosya.Icerik.LongLength, DepoAnahtari = anahtar };
                y.EkEkle(ek);
                ekId = ek.Id;
            }
            var (evrakNo, sayi) = await numarator.GidenNoAsync(await ProjeKoduAsync(y, ct), simdi, ct);
            y.Imzala(b.Id, evrakNo, sayi, ekId, simdi);
        }, ct);

    public Task<IdareYazisi> ImzaIadeAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.ImzaIade, (b, y, simdi) => { y.ImzaIade(b.Id, k.Aciklama ?? "", simdi); return Task.CompletedTask; }, ct);

    /// <summary>
    /// Cevabı idareye gönderir. Sayı verilmemişse (imza adımı kapalı) şimdi verilir. Kanal için otomatik gönderici
    /// tanımlıysa (ör. KEP) yazı o kanaldan gönderilir; gönderim başarısız olursa hata kaydedilir ve yazı gönderime hazır kalır.
    /// </summary>
    public Task<IdareYazisi> IdareyeGonderAsync(Guid id, GonderimKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.IdareyeGonder, async (b, y, simdi) =>
        {
            var giden = y.BekleyenCevap;
            if (giden is null)
            {
                var (evrakNo, sayi) = await numarator.GidenNoAsync(await ProjeKoduAsync(y, ct), simdi, ct);
                giden = y.CevapSayisiVer(evrakNo, sayi, b.Id, simdi);
            }
            var sonuc = await KanaldanGonderAsync(y, giden, k.Kanal, k.AliciAdres, ct);
            if (sonuc is { Basarili: false })
                y.GonderimHatasi(giden.Id, b.Id, sonuc.Hata ?? "Bilinmeyen hata", simdi);
            else
                y.IdareyeGonder(b.Id, k.Kanal, sonuc?.DisMesajId, simdi);
        }, ct);

    public Task<IdareYazisi> IstisnaBildirAsync(Guid id, IstisnaBildirKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.IstisnaBildir, (b, y, simdi) => { y.SuresindeCevapVerilemezBildir(k.Neden, k.OnerilenTarih, b.Id, simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> IstisnaReddetAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.IstisnaReddet, (b, y, simdi) => { y.IstisnaReddet(b.Id, k.Aciklama ?? "", simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> EkSureTalepEtAsync(Guid id, EkSureTalepKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.EkSureTalepEt, async (b, y, simdi) =>
        {
            var (evrakNo, sayi) = await numarator.GidenNoAsync(await ProjeKoduAsync(y, ct), simdi, ct);
            var giden = y.EkSureTalepEt(b.Id, k.TalepYazisiMetni, k.TalepEdilenTarih, evrakNo, sayi, k.Aciklama, simdi);
            await IstisnaYazisiniGonderAsync(y, giden, k.Kanal, k.AliciAdres, b.Id, simdi, ct);
        }, ct);

    public Task<IdareYazisi> GecikmeBildirAsync(Guid id, GecikmeBildirKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.GecikmeBildir, async (b, y, simdi) =>
        {
            var (evrakNo, sayi) = await numarator.GidenNoAsync(await ProjeKoduAsync(y, ct), simdi, ct);
            var giden = y.GecikmeBildir(b.Id, k.GerekceYazisiMetni, k.TaahhutTarihi, evrakNo, sayi, k.Aciklama, hesaplayici, simdi);
            await IstisnaYazisiniGonderAsync(y, giden, k.Kanal, k.AliciAdres, b.Id, simdi, ct);
        }, ct);

    public Task<IdareYazisi> EkSureYanitiKaydetAsync(Guid id, EkSureYanitKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.EkSureYanitiKaydet, (b, y, simdi) =>
        {
            y.EkSureYanitiKaydet(k.Yanit, k.VerilenTarih, b.Id, hesaplayici, simdi);
            return Task.CompletedTask;
        }, ct);

    public Task<IdareYazisi> GorevEkleAsync(Guid id, GorevKomutu k, CancellationToken ct = default) =>
        Uygula(id, null, Islemler.GorevEkle, (b, y, simdi) =>
        {
            y.GorevEkle(k.Baslik, k.Aciklama, k.SorumluEposta, k.Lokasyon, k.Termin, b.Id, simdi);
            return Task.CompletedTask;
        }, ct);

    /// <summary>Sayı verilmiş ama henüz gönderilmemiş (veya gönderimi hatalı) ek süre / gecikme yazısını gönderir.</summary>
    public Task<IdareYazisi> IstisnaYazisiGonderAsync(Guid id, IstisnaYazisiGonderKomutu k, CancellationToken ct = default) =>
        Uygula(id, null, Islemler.IstisnaYazisiGonder, async (b, y, simdi) =>
        {
            var giden = y.GidenYazilar.FirstOrDefault(g => g.Id == k.GidenYaziId && g.Tur != GidenYaziTuru.Cevap)
                        ?? throw new BulunamadiHatasi("Giden yazı bulunamadı.");
            if (giden.Durum == GonderimDurumu.Gonderildi) throw new IsKuraliHatasi("Bu yazı zaten gönderildi.");
            if (string.IsNullOrWhiteSpace(k.Kanal)) throw new IsKuraliHatasi("Gönderim kanalı seçilmelidir.");
            await IstisnaYazisiniGonderAsync(y, giden, k.Kanal, k.AliciAdres, b.Id, simdi, ct);
        }, ct);

    // ------------------------------------------------------------------ Yardımcılar

    private async Task IstisnaYazisiniGonderAsync(IdareYazisi y, GidenYazi giden, string? kanal, string? adres,
        Guid kullaniciId, DateTimeOffset simdi, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(kanal)) return; // Elle gönderilecek; sayı verildi.
        var sonuc = await KanaldanGonderAsync(y, giden, kanal, adres, ct);
        if (sonuc is { Basarili: false })
            y.GonderimHatasi(giden.Id, kullaniciId, sonuc.Hata ?? "Bilinmeyen hata", simdi);
        else
            y.GidenYaziGonderildi(giden.Id, kullaniciId, kanal, sonuc?.DisMesajId, simdi);
    }

    /// <summary>Kanal için otomatik gönderici varsa gönderir; yoksa null döner (elle gönderildi kabul edilir).</summary>
    private async Task<GonderimSonucu?> KanaldanGonderAsync(IdareYazisi y, GidenYazi giden, string? kanal, string? adres, CancellationToken ct)
    {
        var gonderici = gondericiler.FirstOrDefault(g => string.Equals(g.Kanal, kanal, StringComparison.OrdinalIgnoreCase));
        if (gonderici is null) return null;

        var ekler = new List<GelenEk>
        {
            new($"{DosyaAdi(giden.Sayi)}.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                await belgeler.GidenYaziBelgesiAsync(y, giden, ct)),
        };
        if (giden.ImzaliEkId is { } imzaliId && y.Ekler.FirstOrDefault(e => e.Id == imzaliId) is { } imzali &&
            await dosyalar.AcAsync(imzali.DepoAnahtari, ct) is { } akim)
        {
            await using (akim)
            {
                using var ms = new MemoryStream();
                await akim.CopyToAsync(ms, ct);
                ekler.Add(new GelenEk(imzali.DosyaAdi, imzali.IcerikTuru, ms.ToArray()));
            }
        }

        var proje = y.ProjeId is { } pid ? await kullanicilar.ProjeGetirAsync(pid, ct) : null;
        try
        {
            return await gonderici.GonderAsync(new GonderimTalebi(y.Id, giden, proje?.KepAdresi,
                string.IsNullOrWhiteSpace(adres) ? y.GonderenAdres : adres.Trim(),
                $"Sayı: {giden.Sayi} – {giden.Konu}", ekler), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Giden yazı gönderilemedi: {Sayi}", giden.Sayi);
            return new GonderimSonucu(false, null, ex.Message);
        }
    }

    private static string DosyaAdi(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));

    private async Task<string?> ProjeKoduAsync(IdareYazisi y, CancellationToken ct) =>
        y.ProjeId is { } pid ? (await kullanicilar.ProjeGetirAsync(pid, ct))?.Kod : null;

    /// <summary>
    /// Ortak işlem iskeleti: yazıyı yükle → görünürlük + yetki kontrolü → eşzamanlılık kontrolü →
    /// işlemi uygula → hatırlatmaları yeniden planla → kaydet.
    /// </summary>
    private async Task<IdareYazisi> Uygula(Guid id, int? beklenenSurum, string islem,
        Func<KullaniciBaglami, IdareYazisi, DateTimeOffset, Task> uygula, CancellationToken ct)
    {
        var baglam = await yetki.BaglamAsync(ct);
        var yazi = await depo.GetirAsync(id, ct);
        if (yazi is null || !Yetki.GorebilirMi(baglam, yazi))
            throw new BulunamadiHatasi("Yazı bulunamadı.");
        if (beklenenSurum is { } s && s != yazi.SurumNo)
            throw new EszamanlilikHatasi();

        Yetki.Gerekli(baglam, yazi, islem);

        var simdi = saat.Simdi;
        await uygula(baglam, yazi, simdi);
        yazi.HatirlatmalariYenile(planlayici, simdi);
        yazi.SurumArtir();
        await depo.KaydetAsync(ct);
        return yazi;
    }
}
