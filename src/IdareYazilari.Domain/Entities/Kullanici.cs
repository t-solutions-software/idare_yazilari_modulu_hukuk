using IdareYazilari.Domain.Enums;

namespace IdareYazilari.Domain.Entities;

public class Kullanici
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AdSoyad { get; set; } = "";
    public string Eposta { get; set; } = "";
    public KullaniciRolu Rol { get; set; }

    /// <summary>Proje müdürü ve personel için bağlı olduğu proje. Hukuk ve yönetici için boştur (tüm projeleri görür).</summary>
    public Guid? ProjeId { get; set; }

    public bool Aktif { get; set; } = true;
}
