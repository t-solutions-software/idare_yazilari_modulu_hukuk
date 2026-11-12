namespace IdareYazilari.Domain.Services;

/// <summary>Süre kuralları. appsettings.json içindeki "Sureler" bölümünden okunur.</summary>
public class SureAyarlari
{
    /// <summary>Sorumlunun taslağı hukuka göndermesi gereken an: cevap süresinin bu oranı (varsayılan %50).</summary>
    public double TaslakOrani { get; set; } = 0.50;

    /// <summary>Hukukun onay / revizyonu tamamlaması gereken an: cevap süresinin bu oranı (varsayılan %75).</summary>
    public double HukukOrani { get; set; } = 0.75;

    /// <summary>Ara tarihler bir önceki mesai bitimine yuvarlansın ve tatil/hafta sonuna denk gelirse önceki iş gününe çekilsin mi?</summary>
    public bool AraTarihleriIsGununeCek { get; set; } = true;

    /// <summary>Ara tarihler bu saate (mesai bitimi) aşağı yuvarlanır; ör. 18:00.</summary>
    public TimeOnly MesaiBitisSaati { get; set; } = new(18, 0);

    /// <summary>
    /// Takvim günü ile hesaplanan son gün resmi tatile denk gelirse bir sonraki iş gününe uzatılsın mı?
    /// Hukuken mümkün olsa da ihtiyatlı olmak için varsayılan olarak kapalıdır.
    /// </summary>
    public bool SonGunTatildeIseUzat { get; set; } = false;

    /// <summary>Metinde süre bulunamazsa kullanılacak varsayılan cevap süresi (takvim günü). Bu durumda süre teyidi istenir.</summary>
    public int VarsayilanCevapSuresiGun { get; set; } = 15;

    /// <summary>"Süre yaklaşıyor" hatırlatmalarının ilgili tarihten kaç saat önce gönderileceği.</summary>
    public int OnHatirlatmaSaat { get; set; } = 24;

    /// <summary>Atama yapılmamış yazılar için proje müdürüne tekrar hatırlatma aralığı (saat).</summary>
    public int AtamaHatirlatmaSaat { get; set; } = 4;

    /// <summary>Cevap son anına bu kadar saat kala cevap hâlâ gönderilmediyse üst yönetime eskalasyon yapılır.</summary>
    public int EskalasyonSaat { get; set; } = 48;

    /// <summary>Türkiye saati (UTC+3, yaz saati uygulaması yok).</summary>
    public static readonly TimeSpan TurkiyeSaatFarki = TimeSpan.FromHours(3);
}
