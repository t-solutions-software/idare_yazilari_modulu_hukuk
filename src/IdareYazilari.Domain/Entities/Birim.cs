namespace IdareYazilari.Domain.Entities;

/// <summary>Yazının havale edilebileceği birim (proje içi teknik/işletme birimi veya genel merkez birimi).</summary>
public class Birim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kod { get; set; } = "";
    public string Ad { get; set; } = "";
    /// <summary>Boşsa genel merkez birimidir ve tüm projelerin yazıları havale edilebilir.</summary>
    public Guid? ProjeId { get; set; }
    /// <summary>Birim onayını veren yönetici.</summary>
    public Guid? YoneticiId { get; set; }
    public bool Aktif { get; set; } = true;
}
