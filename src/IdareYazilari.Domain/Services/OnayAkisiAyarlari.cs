namespace IdareYazilari.Domain.Services;

/// <summary>
/// Giden yazı onay zinciri: birim → hukuk → imza yetkilisi. Hukuk adımı her zaman vardır;
/// birim ve imza adımları yapılandırmayla açılıp kapatılabilir (appsettings "OnayAkisi").
/// </summary>
public class OnayAkisiAyarlari
{
    /// <summary>Taslak hukuka gitmeden önce havale edilen birimin yöneticisi onaylasın mı?</summary>
    public bool BirimOnayi { get; set; } = true;

    /// <summary>Hukuk onayından sonra imza yetkilisi imzalasın mı?</summary>
    public bool ImzaYetkilisiOnayi { get; set; } = true;

    /// <summary>İmzada imzalı dosya (e-imzalı PDF / .imz) yüklenmesi zorunlu mu?</summary>
    public bool ImzaliDosyaZorunlu { get; set; } = false;
}
