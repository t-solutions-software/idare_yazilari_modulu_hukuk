using IdareYazilari.Domain.Enums;

namespace IdareYazilari.Application.Dtos;

// ---------------------------------------------------------------- Komutlar

public sealed record ElleYaziGirisi(
    YaziKaynagi Kaynak,
    Guid? ProjeId,
    string? GonderenIdare,
    string? GelenSayi,
    DateOnly? YaziTarihi,
    DateTimeOffset? TebligTarihi,
    string? Konu,
    string Metin,
    DateOnly? CevapSonGunu,
    string? DisReferans);

public sealed record BilgiDuzeltKomutu(string? Konu, string? Ozet, DateOnly? CevapSonGunu, int? SurumNo);
public sealed record ProjeAtaKomutu(Guid ProjeId, int? SurumNo);
public sealed record SorumluAtaKomutu(Guid SorumluId, int? SurumNo);
public sealed record TaslakKomutu(string Icerik, bool HukukaGonder, int? SurumNo);
public sealed record AciklamaKomutu(string? Aciklama, int? SurumNo);
public sealed record RevizeKomutu(string Icerik, string? Aciklama, int? SurumNo);
public sealed record GonderimKomutu(string? GidenSayi, string? Kanal, int? SurumNo);
public sealed record IstisnaBildirKomutu(string Neden, DateOnly? OnerilenTarih, int? SurumNo);
public sealed record EkSureTalepKomutu(string TalepYazisiMetni, DateOnly TalepEdilenTarih, string? GidenSayi, string? Aciklama, int? SurumNo);
public sealed record GecikmeBildirKomutu(string GerekceYazisiMetni, DateOnly TaahhutTarihi, string? GidenSayi, string? Aciklama, int? SurumNo);
public sealed record EkSureYanitKomutu(EkSureYaniti Yanit, DateOnly? VerilenTarih, int? SurumNo);

// ---------------------------------------------------------------- Sorgular

public enum Aciliyet { Normal = 0, Uyari = 1, Kritik = 2, Gecikti = 3, Kapandi = 4 }

public sealed record YaziOzetDto(
    Guid Id,
    YaziKaynagi Kaynak,
    Guid? ProjeId,
    string? ProjeAdi,
    string GonderenIdare,
    string? GelenSayi,
    string Konu,
    string Ozet,
    DateTimeOffset TebligTarihi,
    DateOnly CevapSonGunu,
    DateOnly IlkCevapSonGunu,
    DateTimeOffset TaslakSonTarihi,
    DateTimeOffset HukukSonTarihi,
    int KalanGun,
    YaziDurumu Durum,
    string DurumAdi,
    string AktifAsama,
    DateTimeOffset AktifAsamaSonTarihi,
    Aciliyet Aciliyet,
    Guid? SorumluId,
    string? SorumluAdi,
    bool SureTeyitGerekli,
    bool SureUzatildi,
    int SurumNo);

public sealed record TaslakDto(int VersiyonNo, string Icerik, string? Hazirlayan, KullaniciRolu HazirlayanRol, DateTimeOffset Tarih);
public sealed record IncelemeDto(int TaslakVersiyonNo, HukukKarari Karar, string? Aciklama, string? Hukukcu, DateTimeOffset Tarih);
public sealed record IstisnaDto(
    Guid Id, string Neden, DateOnly? OnerilenTarih, string? Bildiren, DateTimeOffset BildirimTarihi,
    IstisnaDurumu Durum, IstisnaCozumu? Cozum, string? Hukukcu, DateTimeOffset? KararTarihi, string? HukukAciklamasi,
    string? IdareyeYaziMetni, string? IdareyeGidenSayi, DateOnly? HedefTarih,
    EkSureYaniti? IdareYaniti, DateOnly? IdareninVerdigiTarih);
public sealed record HareketDto(DateTimeOffset Tarih, string? Kullanici, string Islem, string? Aciklama);
public sealed record HatirlatmaDto(HatirlatmaTuru Tur, DateTimeOffset Zaman, AliciTipi AliciTipi, string? Alici, string Mesaj, DateTimeOffset? GonderimZamani, bool Iptal);
public sealed record EkDto(Guid Id, string DosyaAdi, string IcerikTuru, long Boyut);

public sealed record YaziDetayDto(
    YaziOzetDto Ozet,
    string Metin,
    DateOnly? YaziTarihi,
    AnalizKaynagi AnalizKaynagi,
    string? SureAciklamasi,
    DateTimeOffset? HukukOnayTarihi,
    DateTimeOffset? GonderimTarihi,
    string? GidenSayi,
    string? GonderimKanali,
    IReadOnlyList<TaslakDto> Taslaklar,
    IReadOnlyList<IncelemeDto> HukukIncelemeleri,
    IReadOnlyList<IstisnaDto> Istisnalar,
    IReadOnlyList<HareketDto> Hareketler,
    IReadOnlyList<HatirlatmaDto> Hatirlatmalar,
    IReadOnlyList<EkDto> Ekler,
    IReadOnlyList<string> YapilabilirIslemler);

public sealed record BildirimDto(Guid YaziId, string Konu, HatirlatmaTuru Tur, string Mesaj, DateTimeOffset Zaman);

public sealed record KullaniciDto(Guid Id, string AdSoyad, string Eposta, KullaniciRolu Rol, Guid? ProjeId);
public sealed record ProjeDto(Guid Id, string Kod, string Ad, Guid? ProjeMuduruId);
