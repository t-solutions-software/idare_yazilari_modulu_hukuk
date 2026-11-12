using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Analiz;
using IdareYazilari.Application.Dtos;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using IdareYazilari.Domain.Services;
using Microsoft.Extensions.Logging;

namespace IdareYazilari.Application.Services;

/// <summary>Gelen yazıyı analiz edip sisteme kaydeder ve proje müdürünün ekranına düşürür.</summary>
public class YaziKayitServisi(
    IYaziDeposu depo,
    IKullaniciDeposu kullanicilar,
    IDosyaDeposu dosyalar,
    YaziAnalizServisi analizServisi,
    SureHesaplayici hesaplayici,
    HatirlatmaPlanlayici planlayici,
    IMevcutKullanici mevcut,
    ISaat saat,
    ILogger<YaziKayitServisi> log)
{
    /// <summary>Otomatik kaynaklardan (UETS/KEP) gelen paketi kaydeder. Aynı dış referans daha önce kaydedildiyse null döner.</summary>
    public async Task<IdareYazisi?> KaydetAsync(GelenYaziPaketi paket, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(paket.DisReferans) &&
            await depo.DisReferansVarMiAsync(paket.Kaynak, paket.DisReferans, ct))
        {
            log.LogInformation("{Kaynak} {Ref} daha önce kaydedilmiş, atlanıyor.", paket.Kaynak, paket.DisReferans);
            return null;
        }

        var projeId = paket.ProjeId ?? (await kullanicilar.ProjeBulAsync(paket.ProjeKodu, paket.AliciAdres, ct))?.Id;
        if (projeId is null)
            log.LogWarning("{Kaynak} {Ref}: proje eşleştirilemedi; yönetici ekranına düşecek.", paket.Kaynak, paket.DisReferans);

        var analiz = await analizServisi.AnalizEtAsync(
            new AnalizGirdisi(paket.Metin, paket.Konu, paket.TebligTarihi, paket.Ekler), ct);

        var sonGun = paket.CevapSonGunu is { } elle
            ? new SonGunSonucu(elle, false, "Kayıt sırasında elle girildi")
            : analiz.SonGun;
        var konu = paket.Kaynak == YaziKaynagi.Dogrudan && !string.IsNullOrWhiteSpace(paket.Konu) ? paket.Konu : analiz.Konu;
        var metin = string.IsNullOrWhiteSpace(paket.Metin) ? analiz.CikarilanMetin ?? "" : paket.Metin;

        var yazi = IdareYazisi.Olustur(
            paket.Kaynak, paket.DisReferans, projeId,
            paket.GonderenIdare ?? analiz.GonderenIdare ?? "(Gönderen tespit edilemedi)",
            paket.GelenSayi ?? analiz.GelenSayi,
            paket.YaziTarihi ?? analiz.YaziTarihi,
            paket.TebligTarihi, konu, metin, analiz.Ozet,
            paket.CevapSonGunu is null ? analiz.Kaynak : AnalizKaynagi.Manuel,
            sonGun, hesaplayici, saat.Simdi);

        foreach (var ek in paket.Ekler)
        {
            var anahtar = await dosyalar.KaydetAsync(yazi.Id, ek.DosyaAdi, ek.Icerik, ct);
            yazi.EkEkle(new Ek { DosyaAdi = ek.DosyaAdi, IcerikTuru = ek.IcerikTuru, Boyut = ek.Icerik.LongLength, DepoAnahtari = anahtar });
        }

        yazi.HatirlatmalariYenile(planlayici, saat.Simdi);
        depo.Ekle(yazi);
        await depo.KaydetAsync(ct);
        log.LogInformation("Yazı kaydedildi: {Id} {Konu} son gün {SonGun}", yazi.Id, yazi.Konu, yazi.CevapSonGunu);
        return yazi;
    }

    /// <summary>Doğrudan PPP şirketine gelen (elden/posta/e-posta) yazının ekrandan girilmesi.</summary>
    public async Task<IdareYazisi> ElleKaydetAsync(ElleYaziGirisi giris, IReadOnlyList<GelenEk> ekler, CancellationToken ct = default)
    {
        var k = await mevcut.GetirAsync(ct);
        var projeId = giris.ProjeId;
        switch (k.Rol)
        {
            case KullaniciRolu.ProjeMuduru:
                if (projeId is not null && projeId != k.ProjeId)
                    throw new YetkiHatasi("Yalnızca kendi projenize yazı girebilirsiniz.");
                projeId = k.ProjeId;
                break;
            case KullaniciRolu.Personel:
                throw new YetkiHatasi("Yazı girişi proje müdürü, hukuk veya yönetici tarafından yapılabilir.");
        }
        if (projeId is not null && await kullanicilar.ProjeGetirAsync(projeId.Value, ct) is null)
            throw new BulunamadiHatasi("Proje bulunamadı.");
        if (string.IsNullOrWhiteSpace(giris.Metin) && ekler.Count == 0)
            throw new IsKuraliHatasi("Yazı metni veya ek dosya zorunludur.");

        var yazi = await KaydetAsync(new GelenYaziPaketi
        {
            Kaynak = giris.Kaynak,
            DisReferans = giris.DisReferans,
            ProjeId = projeId,
            GonderenIdare = string.IsNullOrWhiteSpace(giris.GonderenIdare) ? null : giris.GonderenIdare,
            GelenSayi = string.IsNullOrWhiteSpace(giris.GelenSayi) ? null : giris.GelenSayi,
            YaziTarihi = giris.YaziTarihi,
            TebligTarihi = giris.TebligTarihi ?? saat.Simdi,
            Konu = string.IsNullOrWhiteSpace(giris.Konu) ? null : giris.Konu,
            Metin = giris.Metin ?? "",
            CevapSonGunu = giris.CevapSonGunu,
            Ekler = ekler,
        }, ct);

        return yazi ?? throw new IsKuraliHatasi("Bu dış referansla kayıtlı bir yazı zaten var.");
    }
}
