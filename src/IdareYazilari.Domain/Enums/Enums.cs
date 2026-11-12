namespace IdareYazilari.Domain.Enums;

/// <summary>İdare yazısının sisteme giriş kanalı.</summary>
public enum YaziKaynagi
{
    Uets = 1,
    Kep = 2,
    /// <summary>Doğrudan PPP şirketine gelen (elden, posta vb.) yazı.</summary>
    Dogrudan = 3,
    /// <summary>Kamu e-Yazışma (DETSİS / e-Yazışma paketi) ile gelen yazı.</summary>
    EYazisma = 4,
    /// <summary>E-posta ile gelen yazı.</summary>
    EPosta = 5
}

public enum YaziDurumu
{
    /// <summary>Ekrana düştü; proje müdürünün sorumlu ataması bekleniyor.</summary>
    AtamaBekliyor = 1,
    /// <summary>Sorumlu taslak hazırlıyor (süre: cevap süresinin %50'si).</summary>
    TaslakHazirlaniyor = 2,
    /// <summary>Taslak hukuk ekibinde (süre: cevap süresinin %75'i).</summary>
    HukukIncelemesinde = 3,
    /// <summary>Hukuk revizyon talep etti; taslak sorumluya geri döndü.</summary>
    RevizyonBekleniyor = 4,
    /// <summary>Onaylar tamamlandı (hukuk + varsa imza), sayı verildi; idareye gönderim bekleniyor.</summary>
    GonderimeHazir = 5,
    /// <summary>Cevap idareye gönderildi; süreç kapandı.</summary>
    Gonderildi = 6,
    /// <summary>Süresinde cevap verilemeyeceği bildirildi; hukuk nedeni inceliyor.</summary>
    IstisnaHukukIncelemesinde = 7,
    /// <summary>İdareden ek süre talep edildi; idarenin yanıtı bekleniyor.</summary>
    EkSureYanitiBekleniyor = 8,
    /// <summary>Taslak, havale edilen birimin yöneticisinin onayında.</summary>
    BirimOnayinda = 9,
    /// <summary>Hukuk onayladı; imza yetkilisinin imzası bekleniyor.</summary>
    ImzaBekliyor = 10
}

public enum KullaniciRolu
{
    ProjeMuduru = 1,
    /// <summary>Proje içinde cevap sorumlusu olarak atanabilen personel.</summary>
    Personel = 2,
    Hukuk = 3,
    Yonetici = 4,
    /// <summary>Giden yazıyı imzalamaya yetkili kişi (genel müdür, proje direktörü vb.).</summary>
    ImzaYetkilisi = 5
}

public enum HukukKarari
{
    Onay = 1,
    RevizyonTalebi = 2,
    RevizeEdilerekOnay = 3
}

public enum IstisnaDurumu
{
    HukukIncelemesinde = 1,
    /// <summary>Hukuk nedeni uygun bulmadı; asıl süre içinde cevap verilmeli.</summary>
    Reddedildi = 2,
    /// <summary>Hukuk nedeni uygun buldu ve idareden ek süre talep edildi.</summary>
    EkSureTalepEdildi = 3,
    /// <summary>Hukuk, gerekçeli yazı ile cevabın verilebileceği tarihi idareye bildirdi.</summary>
    GecikmeBildirildi = 4
}

public enum IstisnaCozumu
{
    EkSureTalebi = 1,
    GecikmeBildirimi = 2
}

public enum EkSureYaniti
{
    Kabul = 1,
    Ret = 2
}

public enum AnalizKaynagi
{
    /// <summary>Kural tabanlı (regex) çıkarım.</summary>
    Kural = 1,
    /// <summary>Yapay zekâ (LLM) ile çıkarım.</summary>
    YapayZeka = 2,
    /// <summary>Kullanıcı tarafından girildi / düzeltildi.</summary>
    Manuel = 3
}

public enum CevapSuresiTuru
{
    Bulunamadi = 0,
    /// <summary>Metinde açık bir son tarih var (ör. "en geç 20.11.2026 tarihine kadar").</summary>
    KesinTarih = 1,
    /// <summary>Takvim günü (ör. "15 gün içinde").</summary>
    TakvimGunu = 2,
    /// <summary>İş günü (ör. "7 iş günü içinde").</summary>
    IsGunu = 3,
    /// <summary>Ay (ör. "1 ay içinde"); son gün, tebliğ gününe karşılık gelen ay günüdür.</summary>
    Ay = 4
}

public enum HatirlatmaTuru
{
    AtamaBekliyor = 1,
    TaslakSuresiYaklasiyor = 2,
    TaslakSuresiDoldu = 3,
    HukukSuresiYaklasiyor = 4,
    HukukSuresiDoldu = 5,
    GonderimBekliyor = 6,
    CevapSonGunuYaklasiyor = 7,
    CevapSonGunu = 8,
    EkSureYanitiTakibi = 9,
    TaahhutEdilenSureYaklasiyor = 10,
    TaahhutEdilenSure = 11,
    SorumluAtandi = 12,
    RevizyonTalepEdildi = 13,
    HukukIncelemesiBekliyor = 14,
    IstisnaIncelemesiBekliyor = 15,
    SureTeyidiGerekli = 16,
    IstisnaSonuclandi = 17,
    BirimOnayiBekliyor = 18,
    ImzaBekliyor = 19,
    /// <summary>Süre kritik eşiğe geldi ve cevap hâlâ gönderilmedi; üst yönetime eskalasyon.</summary>
    Eskalasyon = 20,
    GonderimHatasi = 21
}

public enum AliciTipi
{
    /// <summary>Belirli bir kullanıcı (ör. atanmış sorumlu).</summary>
    Kullanici = 1,
    /// <summary>Yazının ait olduğu projenin proje müdürü.</summary>
    ProjeMuduru = 2,
    /// <summary>Hukuk ekibinin tamamı.</summary>
    HukukEkibi = 3,
    /// <summary>Yazının havale edildiği birimin yöneticisi (birim yoksa proje müdürü).</summary>
    BirimYoneticisi = 4,
    /// <summary>İmza yetkilileri (projeye özel tanımlıysa onlar, değilse genel imza yetkilileri).</summary>
    ImzaYetkilileri = 5,
    /// <summary>Üst yönetim (yönetici rolündeki kullanıcılar).</summary>
    UstYonetim = 6
}

public enum GidenYaziTuru
{
    Cevap = 1,
    EkSureTalebi = 2,
    GecikmeBildirimi = 3
}

public enum GonderimDurumu
{
    /// <summary>Sayı verildi, gönderim bekleniyor.</summary>
    Bekliyor = 1,
    Gonderildi = 2,
    Hata = 3
}

public enum AktarimDurumu
{
    Bekliyor = 1,
    Aktarildi = 2,
    Hata = 3
}
