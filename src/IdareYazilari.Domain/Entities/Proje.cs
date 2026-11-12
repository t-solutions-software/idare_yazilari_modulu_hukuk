namespace IdareYazilari.Domain.Entities;

/// <summary>Kamu-özel işbirliği (PPP/KÖİ) projesi.</summary>
public class Proje
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kod { get; set; } = "";
    public string Ad { get; set; } = "";
    public Guid? ProjeMuduruId { get; set; }

    /// <summary>Projeye ait KEP adresi; gelen KEP iletilerini projeye eşleştirmek için kullanılır.</summary>
    public string? KepAdresi { get; set; }

    /// <summary>Projeye ait UETS adresi / hesap kodu.</summary>
    public string? UetsAdresi { get; set; }
}
