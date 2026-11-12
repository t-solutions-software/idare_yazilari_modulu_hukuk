using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using IdareYazilari.Domain.Services;

namespace IdareYazilari.Application.Services;

public static class Islemler
{
    public const string BilgiDuzelt = nameof(BilgiDuzelt);
    public const string ProjeAta = nameof(ProjeAta);
    public const string SorumluAta = nameof(SorumluAta);
    public const string TaslakKaydet = nameof(TaslakKaydet);
    public const string OnayaGonder = nameof(OnayaGonder);
    public const string BirimOnayla = nameof(BirimOnayla);
    public const string BirimIade = nameof(BirimIade);
    public const string HukukOnayla = nameof(HukukOnayla);
    public const string RevizyonTalepEt = nameof(RevizyonTalepEt);
    public const string RevizeEtVeOnayla = nameof(RevizeEtVeOnayla);
    public const string Imzala = nameof(Imzala);
    public const string ImzaIade = nameof(ImzaIade);
    public const string IdareyeGonder = nameof(IdareyeGonder);
    public const string IstisnaBildir = nameof(IstisnaBildir);
    public const string IstisnaReddet = nameof(IstisnaReddet);
    public const string EkSureTalepEt = nameof(EkSureTalepEt);
    public const string GecikmeBildir = nameof(GecikmeBildir);
    public const string EkSureYanitiKaydet = nameof(EkSureYanitiKaydet);
    public const string GorevEkle = nameof(GorevEkle);
    public const string IstisnaYazisiGonder = nameof(IstisnaYazisiGonder);
}

/// <summary>Yetki kararları için kullanıcı bağlamı: kullanıcı ve yöneticisi olduğu birimler.</summary>
public sealed record KullaniciBaglami(Kullanici Kullanici, IReadOnlySet<Guid> YonettigiBirimler)
{
    public Guid Id => Kullanici.Id;
    public KullaniciRolu Rol => Kullanici.Rol;
}

/// <summary>Rol, proje ve birim bazlı yetki kuralları (tek yerde).</summary>
public static class Yetki
{
    public static GorunurlukKapsami? Kapsam(KullaniciBaglami b) => b.Rol switch
    {
        KullaniciRolu.Hukuk or KullaniciRolu.Yonetici => null,
        KullaniciRolu.ProjeMuduru => new(ProjeId: b.Kullanici.ProjeId ?? Guid.Empty, BirimIdler: [.. b.YonettigiBirimler]),
        KullaniciRolu.ImzaYetkilisi => new(ImzaYetkilisiId: b.Id, ImzaProjeId: b.Kullanici.ProjeId, BirimIdler: [.. b.YonettigiBirimler]),
        _ => new(SorumluId: b.Id, BirimIdler: [.. b.YonettigiBirimler]),
    };

    public static bool GorebilirMi(KullaniciBaglami b, IdareYazisi y)
    {
        if (b.Rol is KullaniciRolu.Hukuk or KullaniciRolu.Yonetici) return true;
        if (y.SorumluId == b.Id) return true;
        if (y.BirimId is { } bid && b.YonettigiBirimler.Contains(bid)) return true;
        return b.Rol switch
        {
            KullaniciRolu.ProjeMuduru => y.ProjeId is not null && y.ProjeId == b.Kullanici.ProjeId,
            KullaniciRolu.ImzaYetkilisi => ImzaKapsaminda(b, y) && (y.Durum == YaziDurumu.ImzaBekliyor || y.ImzalayanId == b.Id),
            _ => false,
        };
    }

    public static bool ProjeYetkilisi(KullaniciBaglami b, IdareYazisi y) =>
        b.Rol == KullaniciRolu.Yonetici || (b.Rol == KullaniciRolu.ProjeMuduru && y.ProjeId == b.Kullanici.ProjeId);

    /// <summary>Birim onayını verecek kişi mi? Birim yoksa proje müdürü onaylar.</summary>
    public static bool BirimOnaycisi(KullaniciBaglami b, IdareYazisi y) =>
        y.BirimId is { } bid ? b.YonettigiBirimler.Contains(bid) : ProjeYetkilisi(b, y);

    private static bool ImzaKapsaminda(KullaniciBaglami b, IdareYazisi y) =>
        b.Kullanici.ProjeId is null || b.Kullanici.ProjeId == y.ProjeId;

    /// <summary>Kullanıcının bu yazı üzerinde şu an yapabileceği işlemler (ekrandaki butonlar buna göre gösterilir).</summary>
    public static IReadOnlyList<string> YapilabilirIslemler(KullaniciBaglami b, IdareYazisi y)
    {
        var liste = new List<string>();
        if (!GorebilirMi(b, y)) return liste;

        var pm = ProjeYetkilisi(b, y);
        var sorumlu = y.SorumluId == b.Id;
        var hukuk = b.Rol == KullaniciRolu.Hukuk;
        var birimYon = y.BirimId is { } bid && b.YonettigiBirimler.Contains(bid);

        if (pm || hukuk) liste.Add(Islemler.BilgiDuzelt);
        if (pm || hukuk || sorumlu || birimYon) liste.Add(Islemler.GorevEkle);
        if ((pm || hukuk) && y.GidenYazilar.Any(g => g.Tur != GidenYaziTuru.Cevap && g.Durum != GonderimDurumu.Gonderildi))
            liste.Add(Islemler.IstisnaYazisiGonder);
        if (y.Kapali) return liste;

        if (y.ProjeId is null && b.Rol == KullaniciRolu.Yonetici) liste.Add(Islemler.ProjeAta);

        switch (y.Durum)
        {
            case YaziDurumu.AtamaBekliyor:
                if (pm && y.ProjeId is not null) liste.Add(Islemler.SorumluAta);
                break;
            case YaziDurumu.TaslakHazirlaniyor:
            case YaziDurumu.RevizyonBekleniyor:
                if (pm || birimYon) liste.Add(Islemler.SorumluAta);
                if (sorumlu || pm)
                {
                    liste.Add(Islemler.TaslakKaydet);
                    if (y.GuncelTaslak is not null) liste.Add(Islemler.OnayaGonder);
                    liste.Add(Islemler.IstisnaBildir);
                }
                break;
            case YaziDurumu.BirimOnayinda:
                if (BirimOnaycisi(b, y)) liste.AddRange([Islemler.BirimOnayla, Islemler.BirimIade]);
                if (sorumlu || pm) liste.Add(Islemler.IstisnaBildir);
                break;
            case YaziDurumu.HukukIncelemesinde:
                if (hukuk)
                    liste.AddRange([Islemler.HukukOnayla, Islemler.RevizyonTalepEt, Islemler.RevizeEtVeOnayla, Islemler.IstisnaBildir]);
                else if (sorumlu || pm)
                    liste.Add(Islemler.IstisnaBildir);
                break;
            case YaziDurumu.ImzaBekliyor:
                if (b.Rol == KullaniciRolu.ImzaYetkilisi && ImzaKapsaminda(b, y)) liste.AddRange([Islemler.Imzala, Islemler.ImzaIade]);
                if (hukuk) liste.Add(Islemler.IstisnaBildir);
                break;
            case YaziDurumu.GonderimeHazir:
                if (pm || sorumlu) liste.Add(Islemler.IdareyeGonder);
                break;
            case YaziDurumu.IstisnaHukukIncelemesinde:
                if (hukuk) liste.AddRange([Islemler.IstisnaReddet, Islemler.EkSureTalepEt, Islemler.GecikmeBildir]);
                break;
            case YaziDurumu.EkSureYanitiBekleniyor:
                if (hukuk || pm) liste.Add(Islemler.EkSureYanitiKaydet);
                break;
        }
        return liste;
    }

    public static void Gerekli(KullaniciBaglami b, IdareYazisi y, string islem)
    {
        if (!YapilabilirIslemler(b, y).Contains(islem))
            throw new YetkiHatasi($"'{islem}' işlemi için yetkiniz yok veya yazının mevcut durumu ({y.Durum}) buna uygun değil.");
    }

    /// <summary>Taslak gönderilirken birim onayı gerekli mi? Gönderen kişi zaten birim onaycısıysa adım atlanır.</summary>
    public static bool BirimOnayiGerekli(OnayAkisiAyarlari akis, KullaniciBaglami gonderen, IdareYazisi y) =>
        akis.BirimOnayi && !BirimOnaycisi(gonderen, y);
}

/// <summary>Oturumdaki kullanıcının yetki bağlamını (yönettiği birimler dahil) üretir.</summary>
public class YetkiServisi(IMevcutKullanici mevcut, IKullaniciDeposu kullanicilar)
{
    private KullaniciBaglami? _baglam;

    public async Task<KullaniciBaglami> BaglamAsync(CancellationToken ct = default)
    {
        if (_baglam is not null) return _baglam;
        var k = await mevcut.GetirAsync(ct);
        var birimler = (await kullanicilar.BirimleriListeleAsync(ct: ct)).Where(b => b.YoneticiId == k.Id).Select(b => b.Id).ToHashSet();
        return _baglam = new KullaniciBaglami(k, birimler);
    }
}
