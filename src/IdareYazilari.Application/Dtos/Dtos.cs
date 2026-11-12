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
    string? DisReferans,
    string? Lokasyon = null,
    string? SozlesmeReferansi = null,
    string? GonderenAdres = null);

public sealed record BilgiDuzeltKomutu(string? Konu, string? Ozet, DateOnly? CevapSonGunu, int? SurumNo,
    string? SozlesmeReferansi = null, string? Lokasyon = null);
public sealed record ProjeAtaKomutu(Guid ProjeId, int? SurumNo);
/// <summary>Havale: birime ve/veya kişiye. Kişi boşsa birim yöneticisi sorumlu olur.</summary>
public sealed record SorumluAtaKomutu(Guid? SorumluId, int? SurumNo, Guid? BirimId = null);
public sealed record TaslakKomutu(string Icerik, bool OnayaGonder, int? SurumNo);
public sealed record AciklamaKomutu(string? Aciklama, int? SurumNo);
public sealed record RevizeKomutu(string Icerik, string? Aciklama, int? SurumNo);
/// <summary>Kanal "KEP" vb. otomatik bir göndericiye karşılık geliyorsa yazı o kanaldan gönderilir; değilse gönderildi işaretlenir.</summary>
public sealed record GonderimKomutu(string? Kanal, string? AliciAdres, int? SurumNo);
public sealed record IstisnaBildirKomutu(string Neden, DateOnly? OnerilenTarih, int? SurumNo);
public sealed record EkSureTalepKomutu(string TalepYazisiMetni, DateOnly TalepEdilenTarih, string? Aciklama, int? SurumNo,
    string? Kanal = null, string? AliciAdres = null);
public sealed record GecikmeBildirKomutu(string GerekceYazisiMetni, DateOnly TaahhutTarihi, string? Aciklama, int? SurumNo,
    string? Kanal = null, string? AliciAdres = null);
public sealed record GorevKomutu(string Baslik, string? Aciklama, string SorumluEposta, string? Lokasyon, DateOnly? Termin);
public sealed record IstisnaYazisiGonderKomutu(Guid GidenYaziId, string? Kanal, string? AliciAdres);
public sealed record EkSureYanitKomutu(EkSureYaniti Yanit, DateOnly? VerilenTarih, int? SurumNo);

// ---------------------------------------------------------------- Sorgular

public enum Aciliyet { Normal = 0, Uyari = 1, Kritik = 2, Gecikti = 3, Kapandi = 4 }

public sealed record YaziOzetDto(
    Guid Id,
    string EvrakNo,
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
    int SurumNo,
    Guid? BirimId,
    string? BirimAdi,
    string? Lokasyon,
    string? SozlesmeReferansi,
    string? GidenSayi);

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
public sealed record GidenYaziDto(Guid Id, GidenYaziTuru Tur, string EvrakNo, string Sayi, DateTimeOffset Tarih, string Konu,
    string? Imzalayan, DateTimeOffset? ImzaTarihi, Guid? ImzaliEkId, string? Kanal, GonderimDurumu Durum,
    DateTimeOffset? GonderimTarihi, string? DisMesajId, string? Hata);
public sealed record GorevDto(Guid Id, string Baslik, string? Aciklama, string SorumluEposta, string? Lokasyon, DateOnly? Termin,
    string? Olusturan, DateTimeOffset Tarih, string DisAnahtar, AktarimDurumu AktarimDurumu, DateTimeOffset? AktarimTarihi, string? Hata);
public sealed record BirimDto(Guid Id, string Kod, string Ad, Guid? ProjeId, Guid? YoneticiId);

/// <summary>Hukuk / sözleşme modülü kartında gösterilecek yazı özeti.</summary>
public sealed record SozlesmeYaziDto(Guid Id, string EvrakNo, YaziKaynagi Kaynak, string GonderenIdare, string? GelenSayi,
    string Konu, string Ozet, DateTimeOffset TebligTarihi, DateOnly CevapSonGunu, YaziDurumu Durum, string DurumAdi,
    string? GidenSayi, DateTimeOffset? GonderimTarihi, string Baglanti);

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
    IReadOnlyList<string> YapilabilirIslemler,
    IReadOnlyList<GidenYaziDto> GidenYazilar,
    IReadOnlyList<GorevDto> Gorevler,
    string? GonderenAdres,
    string? BirimOnaylayan,
    DateTimeOffset? BirimOnayTarihi,
    string? Imzalayan,
    DateTimeOffset? ImzaTarihi);

public sealed record BildirimDto(Guid YaziId, string Konu, HatirlatmaTuru Tur, string Mesaj, DateTimeOffset Zaman);

public sealed record KullaniciDto(Guid Id, string AdSoyad, string Eposta, KullaniciRolu Rol, Guid? ProjeId, Guid? BirimId = null);
public sealed record ProjeDto(Guid Id, string Kod, string Ad, Guid? ProjeMuduruId);
