using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;

namespace IdareYazilari.Application.Abstractions;

public interface ISaat
{
    DateTimeOffset Simdi { get; }
}

/// <summary>İsteği yapan kullanıcı. Kimlik doğrulama altyapısı (AD/JWT vb.) tarafından sağlanır.</summary>
public interface IMevcutKullanici
{
    Task<Kullanici> GetirAsync(CancellationToken ct = default);
}

/// <summary>
/// Kullanıcının görebileceği yazıların kapsamı. Koşullar VEYA ile birleşir; Kapsam null ise tüm yazılar.
/// </summary>
public sealed record GorunurlukKapsami(
    Guid? ProjeId = null,
    Guid? SorumluId = null,
    IReadOnlyCollection<Guid>? BirimIdler = null,
    /// <summary>İmza yetkilisi: imza bekleyen veya kendi imzaladığı yazılar (ImzaProjeId doluysa yalnız o proje).</summary>
    Guid? ImzaYetkilisiId = null,
    Guid? ImzaProjeId = null);

public sealed record YaziFiltresi(
    Guid? ProjeId = null,
    Guid? SorumluId = null,
    IReadOnlyCollection<YaziDurumu>? Durumlar = null,
    bool SadeceAcik = false,
    string? Arama = null,
    string? SozlesmeReferansi = null,
    GorunurlukKapsami? Kapsam = null);

public interface IYaziDeposu
{
    /// <summary>Yazıyı tüm alt kayıtlarıyla (taslak, inceleme, istisna, hatırlatma, hareket, ek) getirir.</summary>
    Task<IdareYazisi?> GetirAsync(Guid id, CancellationToken ct = default);
    Task<bool> DisReferansVarMiAsync(YaziKaynagi kaynak, string disReferans, CancellationToken ct = default);
    Task<IReadOnlyList<IdareYazisi>> ListeleAsync(YaziFiltresi filtre, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> HatirlatmasiGelenYazilarAsync(DateTimeOffset simdi, CancellationToken ct = default);
    Task<IReadOnlyList<(Hatirlatma Hatirlatma, IdareYazisi Yazi)>> GonderilenHatirlatmalarAsync(
        DateTimeOffset baslangic, CancellationToken ct = default);
    /// <summary>Operasyon modülüne aktarılmayı bekleyen görevlerin yazı kimlikleri.</summary>
    Task<IReadOnlyList<Guid>> AktarilacakGorevYazilariAsync(int azamiDeneme, CancellationToken ct = default);
    /// <summary>Operasyon modülünün çekme (pull) entegrasyonu için görevler.</summary>
    Task<IReadOnlyList<YaziGorevi>> GorevleriListeleAsync(AktarimDurumu? durum, CancellationToken ct = default);
    void Ekle(IdareYazisi yazi);
    /// <summary>Değişiklikleri kaydeder; başka bir kullanıcı aynı yazıyı değiştirdiyse <see cref="EszamanlilikHatasi"/> fırlatır.</summary>
    Task KaydetAsync(CancellationToken ct = default);
}

public interface IKullaniciDeposu
{
    Task<Kullanici?> GetirAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Kullanici>> ListeleAsync(Guid? projeId = null, KullaniciRolu? rol = null, CancellationToken ct = default);
    Task<Proje?> ProjeGetirAsync(Guid id, CancellationToken ct = default);
    Task<Proje?> ProjeBulAsync(string? kod, string? aliciAdres, CancellationToken ct = default);
    Task<IReadOnlyList<Proje>> ProjeleriListeleAsync(CancellationToken ct = default);
    Task<Birim?> BirimGetirAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Birim>> BirimleriListeleAsync(Guid? projeId = null, CancellationToken ct = default);
    Task<Kullanici?> EntraIleBulAsync(string? objectId, string? eposta, CancellationToken ct = default);
    /// <summary>Yönetim işlemleri (kullanıcı/birim/proje tanımı, Entra eşleştirmesi).</summary>
    Task KaydetAsync<T>(T varlik, bool yeni, CancellationToken ct = default) where T : class;
}

/// <summary>Gelen / giden evrak numarası ve giden yazı sayısı üretir (yıllık sıralı, eşzamanlılık güvenli).</summary>
public interface IEvrakNumaratoru
{
    Task<string> GelenEvrakNoAsync(DateTimeOffset tarih, CancellationToken ct = default);
    Task<(string EvrakNo, string Sayi)> GidenNoAsync(string? projeKodu, DateTimeOffset tarih, CancellationToken ct = default);
}

public sealed record GidenYaziBelgeModeli(
    string? ProjeAdi,
    string? Sayi,
    DateTimeOffset Tarih,
    string Muhatap,
    string Konu,
    string? Ilgi,
    string Metin,
    string? ImzaYetkilisi,
    bool Taslak);

/// <summary>Giden yazıyı kurum Word şablonundan (.docx) üretir.</summary>
public interface IBelgeOlusturucu
{
    byte[] Olustur(GidenYaziBelgeModeli model);
}

public sealed record GonderimTalebi(
    Guid YaziId,
    GidenYazi Giden,
    string? GonderenAdres,
    string? AliciAdres,
    string Konu,
    IReadOnlyList<GelenEk> Ekler);

public sealed record GonderimSonucu(bool Basarili, string? DisMesajId, string? Hata);

/// <summary>Giden yazının fiziksel gönderim kancası (KEP, e-Yazışma, UETS...). Kanal adına göre seçilir.</summary>
public interface IGidenYaziGonderici
{
    /// <summary>Kanal adı (ör. "KEP"). Ekrandaki kanal seçimiyle eşleşir.</summary>
    string Kanal { get; }
    Task<GonderimSonucu> GonderAsync(GonderimTalebi talep, CancellationToken ct = default);
}

public sealed record GorevAktarimPaketi(
    string SourceSystem,
    string SourceKey,
    string Title,
    string? Description,
    string AssigneeEmail,
    string? Location,
    DateOnly? Deadline,
    string? Reference);

/// <summary>Görevleri operasyon modülüne iletir (HTTP push). Kapalıysa görevler çekme ucundan alınır.</summary>
public interface IGorevAktarici
{
    bool Etkin { get; }
    Task AktarAsync(GorevAktarimPaketi paket, CancellationToken ct = default);
}

public sealed class EszamanlilikHatasi() : Exception("Yazı siz çalışırken başka bir kullanıcı tarafından güncellendi. Sayfayı yenileyip tekrar deneyin.");

public sealed record Bildirim(Kullanici Alici, string Konu, string Mesaj, Guid YaziId);

/// <summary>Hatırlatma/bildirim kanalı (e-posta, Teams, SMS, log...). Birden fazla kayıtlı olabilir.</summary>
public interface IBildirimGonderici
{
    Task GonderAsync(Bildirim bildirim, CancellationToken ct = default);
}

public interface IDosyaDeposu
{
    Task<string> KaydetAsync(Guid yaziId, string dosyaAdi, byte[] icerik, CancellationToken ct = default);
    Task<Stream?> AcAsync(string depoAnahtari, CancellationToken ct = default);
}

public sealed record GelenEk(string DosyaAdi, string IcerikTuru, byte[] Icerik);

/// <summary>Herhangi bir kanaldan (UETS, KEP, doğrudan) gelen ham yazı.</summary>
public sealed record GelenYaziPaketi
{
    public required YaziKaynagi Kaynak { get; init; }
    public string? DisReferans { get; init; }
    public Guid? ProjeId { get; init; }
    public string? ProjeKodu { get; init; }
    /// <summary>Yazının geldiği KEP/UETS adresi (projeye eşleştirme için).</summary>
    public string? AliciAdres { get; init; }
    public string? GonderenIdare { get; init; }
    /// <summary>Gönderen idarenin KEP / e-posta adresi.</summary>
    public string? GonderenAdres { get; init; }
    public string? GelenSayi { get; init; }
    public string? Lokasyon { get; init; }
    public string? SozlesmeReferansi { get; init; }
    public DateOnly? YaziTarihi { get; init; }
    public required DateTimeOffset TebligTarihi { get; init; }
    public string? Konu { get; init; }
    public string Metin { get; init; } = "";
    /// <summary>Elle girişte kullanıcı son günü biliyorsa doğrudan verebilir; otomatik tespitin önüne geçer.</summary>
    public DateOnly? CevapSonGunu { get; init; }
    public IReadOnlyList<GelenEk> Ekler { get; init; } = [];
}

/// <summary>UETS / KEP gibi otomatik yazı kaynakları için adaptör.</summary>
public interface IGelenYaziKaynagi
{
    string Ad { get; }
    Task<IReadOnlyList<(string Anahtar, GelenYaziPaketi Paket)>> YenileriGetirAsync(CancellationToken ct = default);
    /// <summary>Paket başarıyla kaydedildikten sonra kaynağa işlendi bilgisi verilir (tekrar okunmaması için).</summary>
    Task IslendiAsync(string anahtar, bool basarili, string? hata, CancellationToken ct = default);
}
