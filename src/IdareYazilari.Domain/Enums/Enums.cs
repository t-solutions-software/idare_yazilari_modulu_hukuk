namespace IdareYazilari.Domain.Enums;

/// <summary>İdare yazısının sisteme giriş kanalı.</summary>
public enum YaziKaynagi
{
    Uets = 1,
    Kep = 2,
    /// <summary>Doğrudan PPP şirketine gelen (elden, posta, e-posta vb.) yazı.</summary>
    Dogrudan = 3
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
    /// <summary>Hukuk onayladı; idareye gönderim bekleniyor.</summary>
    HukukOnayladi = 5,
    /// <summary>Cevap idareye gönderildi; süreç kapandı.</summary>
    Gonderildi = 6,
    /// <summary>Süresinde cevap verilemeyeceği bildirildi; hukuk nedeni inceliyor.</summary>
    IstisnaHukukIncelemesinde = 7,
    /// <summary>İdareden ek süre talep edildi; idarenin yanıtı bekleniyor.</summary>
    EkSureYanitiBekleniyor = 8
}

public enum KullaniciRolu
{
    ProjeMuduru = 1,
    /// <summary>Proje içinde cevap sorumlusu olarak atanabilen personel.</summary>
    Personel = 2,
    Hukuk = 3,
    Yonetici = 4
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
    IstisnaSonuclandi = 17
}

public enum AliciTipi
{
    /// <summary>Belirli bir kullanıcı (ör. atanmış sorumlu).</summary>
    Kullanici = 1,
    /// <summary>Yazının ait olduğu projenin proje müdürü.</summary>
    ProjeMuduru = 2,
    /// <summary>Hukuk ekibinin tamamı.</summary>
    HukukEkibi = 3
}
