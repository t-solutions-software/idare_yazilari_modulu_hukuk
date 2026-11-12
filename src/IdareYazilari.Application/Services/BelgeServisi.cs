using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Exceptions;

namespace IdareYazilari.Application.Services;

/// <summary>Taslak ve giden yazıların Word (.docx) belgesini kurum şablonundan üretir.</summary>
public class BelgeServisi(IBelgeOlusturucu olusturucu, IKullaniciDeposu kullanicilar, IYaziDeposu depo, YetkiServisi yetki, ISaat saat)
{
    /// <summary>Güncel taslağın (sayısız, "TASLAK" ibareli) Word belgesi.</summary>
    public async Task<(byte[] Icerik, string DosyaAdi)> TaslakBelgesiAsync(Guid yaziId, CancellationToken ct = default)
    {
        var b = await yetki.BaglamAsync(ct);
        var y = await depo.GetirAsync(yaziId, ct);
        if (y is null || !Yetki.GorebilirMi(b, y)) throw new BulunamadiHatasi("Yazı bulunamadı.");
        var taslak = y.GuncelTaslak ?? throw new IsKuraliHatasi("Henüz taslak yok.");
        var model = await ModelAsync(y, null, taslak.Icerik, saat.Simdi, true, null, ct);
        return (olusturucu.Olustur(model), $"{y.EvrakNo}_taslak_v{taslak.VersiyonNo}.docx");
    }

    /// <summary>Giden yazının (sayılı) Word belgesi.</summary>
    public async Task<(byte[] Icerik, string DosyaAdi)> GidenBelgesiAsync(Guid yaziId, Guid gidenId, CancellationToken ct = default)
    {
        var b = await yetki.BaglamAsync(ct);
        var y = await depo.GetirAsync(yaziId, ct);
        if (y is null || !Yetki.GorebilirMi(b, y)) throw new BulunamadiHatasi("Yazı bulunamadı.");
        var giden = y.GidenYazilar.FirstOrDefault(g => g.Id == gidenId) ?? throw new BulunamadiHatasi("Giden yazı bulunamadı.");
        return (await GidenYaziBelgesiAsync(y, giden, ct), $"{giden.EvrakNo}.docx");
    }

    public async Task<byte[]> GidenYaziBelgesiAsync(IdareYazisi y, GidenYazi giden, CancellationToken ct = default)
    {
        var imzalayan = giden.ImzalayanId is { } iid ? (await kullanicilar.GetirAsync(iid, ct))?.AdSoyad : null;
        return olusturucu.Olustur(await ModelAsync(y, giden.Sayi, giden.Icerik, giden.Tarih, false, imzalayan, ct, giden.Konu));
    }

    private async Task<GidenYaziBelgeModeli> ModelAsync(IdareYazisi y, string? sayi, string metin, DateTimeOffset tarih,
        bool taslak, string? imzaYetkilisi, CancellationToken ct, string? konu = null)
    {
        var proje = y.ProjeId is { } pid ? await kullanicilar.ProjeGetirAsync(pid, ct) : null;
        var ilgi = y.GelenSayi is null && y.YaziTarihi is null ? null
            : $"{y.GonderenIdare}'nin {(y.YaziTarihi is { } yt ? yt.ToString("dd.MM.yyyy") + " tarihli " : "")}{(y.GelenSayi is null ? "" : y.GelenSayi + " sayılı ")}yazısı.";
        return new GidenYaziBelgeModeli(proje?.Ad, sayi, tarih, y.GonderenIdare, konu ?? y.Konu, ilgi, metin, imzaYetkilisi, taslak);
    }
}
