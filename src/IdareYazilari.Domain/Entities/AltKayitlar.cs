using IdareYazilari.Domain.Enums;

namespace IdareYazilari.Domain.Entities;

/// <summary>Cevap taslağının bir versiyonu. Hukukun revize ettiği metin de yeni versiyon olarak saklanır.</summary>
public class Taslak
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YaziId { get; set; }
    public int VersiyonNo { get; set; }
    public string Icerik { get; set; } = "";
    public Guid HazirlayanId { get; set; }
    public KullaniciRolu HazirlayanRol { get; set; }
    public DateTimeOffset Tarih { get; set; }
}

public class HukukIncelemesi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YaziId { get; set; }
    public int TaslakVersiyonNo { get; set; }
    public HukukKarari Karar { get; set; }
    public string? Aciklama { get; set; }
    public Guid HukukcuId { get; set; }
    public DateTimeOffset Tarih { get; set; }
}

/// <summary>Süresinde cevap verilemeyeceği bildirimi ve hukukun bu konudaki kararı.</summary>
public class Istisna
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YaziId { get; set; }
    public string Neden { get; set; } = "";
    public DateOnly? OnerilenTarih { get; set; }
    public Guid BildirenId { get; set; }
    public DateTimeOffset BildirimTarihi { get; set; }

    /// <summary>İstisna bildirildiğinde yazının bulunduğu durum (red halinde geri dönülür).</summary>
    public YaziDurumu OncekiDurum { get; set; }

    public IstisnaDurumu Durum { get; set; } = IstisnaDurumu.HukukIncelemesinde;
    public IstisnaCozumu? Cozum { get; set; }
    public Guid? HukukcuId { get; set; }
    public DateTimeOffset? KararTarihi { get; set; }
    public string? HukukAciklamasi { get; set; }

    /// <summary>İdareye gönderilen ek süre talep / gecikme gerekçe yazısının metni.</summary>
    public string? IdareyeYaziMetni { get; set; }
    public string? IdareyeGidenSayi { get; set; }

    /// <summary>Ek süre talebinde istenen tarih, gecikme bildiriminde taahhüt edilen tarih.</summary>
    public DateOnly? HedefTarih { get; set; }

    public EkSureYaniti? IdareYaniti { get; set; }
    public DateOnly? IdareninVerdigiTarih { get; set; }
    public DateTimeOffset? IdareYanitTarihi { get; set; }
}

public class Hatirlatma
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YaziId { get; set; }
    public HatirlatmaTuru Tur { get; set; }
    public DateTimeOffset Zaman { get; set; }
    public AliciTipi AliciTipi { get; set; }
    public Guid? AliciKullaniciId { get; set; }
    public string Mesaj { get; set; } = "";
    public DateTimeOffset? GonderimZamani { get; set; }
    public bool Iptal { get; set; }

    public bool Bekliyor => GonderimZamani is null && !Iptal;
}

/// <summary>Yazı üzerindeki her işlemin denetim kaydı.</summary>
public class YaziHareketi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YaziId { get; set; }
    public DateTimeOffset Tarih { get; set; }
    public Guid? KullaniciId { get; set; }
    public string Islem { get; set; } = "";
    public string? Aciklama { get; set; }
}

public class Ek
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YaziId { get; set; }
    public string DosyaAdi { get; set; } = "";
    public string IcerikTuru { get; set; } = "application/octet-stream";
    public long Boyut { get; set; }
    /// <summary>Dosya deposundaki anahtar / yol.</summary>
    public string DepoAnahtari { get; set; } = "";
}

/// <summary>Giden evrak: cevap yazısı, ek süre talebi veya gecikme bildirimi. Sayı ve evrak numarası burada tutulur.</summary>
public class GidenYazi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YaziId { get; set; }
    public GidenYaziTuru Tur { get; set; }
    /// <summary>Giden evrak kayıt numarası (ör. GDN-2026-000045).</summary>
    public string EvrakNo { get; set; } = "";
    /// <summary>Yazıya verilen resmi sayı.</summary>
    public string Sayi { get; set; } = "";
    public DateTimeOffset Tarih { get; set; }
    public string Konu { get; set; } = "";
    public string Icerik { get; set; } = "";
    public Guid HazirlayanId { get; set; }
    public Guid? ImzalayanId { get; set; }
    public DateTimeOffset? ImzaTarihi { get; set; }
    /// <summary>İmzalı dosya (e-imzalı PDF / .imz) eki.</summary>
    public Guid? ImzaliEkId { get; set; }
    public string? Kanal { get; set; }
    public GonderimDurumu Durum { get; set; } = GonderimDurumu.Bekliyor;
    public DateTimeOffset? GonderimTarihi { get; set; }
    /// <summary>KEP ileti kimliği / UETS barkodu vb.</summary>
    public string? DisMesajId { get; set; }
    public string? Hata { get; set; }
}

/// <summary>Yazıdan üretilen ve operasyon modülüne aktarılan görev.</summary>
public class YaziGorevi
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid YaziId { get; set; }
    public string Baslik { get; set; } = "";
    public string? Aciklama { get; set; }
    /// <summary>Operasyon modülündeki sorumlu (e-posta ile eşleştirilir).</summary>
    public string SorumluEposta { get; set; } = "";
    public string? Lokasyon { get; set; }
    public DateOnly? Termin { get; set; }
    public Guid OlusturanId { get; set; }
    public DateTimeOffset Tarih { get; set; }
    /// <summary>Operasyon modülündeki Task.SourceKey karşılığı (SourceSystem = "IdareYazilari").</summary>
    public string DisAnahtar { get; set; } = "";
    public AktarimDurumu AktarimDurumu { get; set; } = AktarimDurumu.Bekliyor;
    public DateTimeOffset? AktarimTarihi { get; set; }
    public int DenemeSayisi { get; set; }
    public string? Hata { get; set; }
}
