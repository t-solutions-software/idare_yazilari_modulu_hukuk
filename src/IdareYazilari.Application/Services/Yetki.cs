using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;

namespace IdareYazilari.Application.Services;

public static class Islemler
{
    public const string BilgiDuzelt = nameof(BilgiDuzelt);
    public const string ProjeAta = nameof(ProjeAta);
    public const string SorumluAta = nameof(SorumluAta);
    public const string TaslakKaydet = nameof(TaslakKaydet);
    public const string HukukaGonder = nameof(HukukaGonder);
    public const string HukukOnayla = nameof(HukukOnayla);
    public const string RevizyonTalepEt = nameof(RevizyonTalepEt);
    public const string RevizeEtVeOnayla = nameof(RevizeEtVeOnayla);
    public const string IdareyeGonder = nameof(IdareyeGonder);
    public const string IstisnaBildir = nameof(IstisnaBildir);
    public const string IstisnaReddet = nameof(IstisnaReddet);
    public const string EkSureTalepEt = nameof(EkSureTalepEt);
    public const string GecikmeBildir = nameof(GecikmeBildir);
    public const string EkSureYanitiKaydet = nameof(EkSureYanitiKaydet);
}

/// <summary>Rol ve proje bazlı yetki kuralları.</summary>
public static class Yetki
{
    public static bool GorebilirMi(Kullanici k, IdareYazisi y) => k.Rol switch
    {
        KullaniciRolu.Hukuk or KullaniciRolu.Yonetici => true,
        KullaniciRolu.ProjeMuduru => y.ProjeId is not null && y.ProjeId == k.ProjeId,
        KullaniciRolu.Personel => y.SorumluId == k.Id,
        _ => false,
    };

    public static bool ProjeYetkilisi(Kullanici k, IdareYazisi y) =>
        k.Rol == KullaniciRolu.Yonetici || (k.Rol == KullaniciRolu.ProjeMuduru && y.ProjeId == k.ProjeId);

    public static bool SorumluMu(Kullanici k, IdareYazisi y) => y.SorumluId == k.Id;

    public static bool HukukMu(Kullanici k) => k.Rol == KullaniciRolu.Hukuk;

    /// <summary>Kullanıcının bu yazı üzerinde şu an yapabileceği işlemler (ekrandaki butonlar buna göre gösterilir).</summary>
    public static IReadOnlyList<string> YapilabilirIslemler(Kullanici k, IdareYazisi y)
    {
        var liste = new List<string>();
        if (y.Kapali || !GorebilirMi(k, y)) return liste;

        var pm = ProjeYetkilisi(k, y);
        var sorumlu = SorumluMu(k, y);
        var hukuk = HukukMu(k);

        if (pm || hukuk) liste.Add(Islemler.BilgiDuzelt);
        if (y.ProjeId is null && k.Rol == KullaniciRolu.Yonetici) liste.Add(Islemler.ProjeAta);

        switch (y.Durum)
        {
            case YaziDurumu.AtamaBekliyor:
                if (pm && y.ProjeId is not null) liste.Add(Islemler.SorumluAta);
                break;
            case YaziDurumu.TaslakHazirlaniyor:
            case YaziDurumu.RevizyonBekleniyor:
                if (pm) liste.Add(Islemler.SorumluAta);
                if (sorumlu || pm)
                {
                    liste.Add(Islemler.TaslakKaydet);
                    if (y.GuncelTaslak is not null) liste.Add(Islemler.HukukaGonder);
                    liste.Add(Islemler.IstisnaBildir);
                }
                break;
            case YaziDurumu.HukukIncelemesinde:
                if (hukuk)
                    liste.AddRange([Islemler.HukukOnayla, Islemler.RevizyonTalepEt, Islemler.RevizeEtVeOnayla, Islemler.IstisnaBildir]);
                else if (sorumlu || pm)
                    liste.Add(Islemler.IstisnaBildir);
                break;
            case YaziDurumu.HukukOnayladi:
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

    public static void Gerekli(Kullanici k, IdareYazisi y, string islem)
    {
        if (!YapilabilirIslemler(k, y).Contains(islem))
            throw new YetkiHatasi($"'{islem}' işlemi için yetkiniz yok veya yazının mevcut durumu ({y.Durum}) buna uygun değil.");
    }
}
