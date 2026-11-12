using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Services;

namespace IdareYazilari.Tests;

internal static class Ortak
{
    public static readonly TimeSpan Tr = TimeSpan.FromHours(3);
    public static DateTimeOffset An(int y, int a, int g, int s = 10, int d = 0) => new(y, a, g, s, d, 0, Tr);

    public static SureAyarlari Ayarlar() => new();
    public static TurkiyeTatilTakvimi Takvim() => new([new DateOnly(2026, 5, 27)]);
    public static SureHesaplayici Hesaplayici(SureAyarlari? a = null) => new(a ?? Ayarlar(), Takvim());
    public static HatirlatmaPlanlayici Planlayici() => new(Ayarlar());

    public static readonly Guid Proje = Guid.NewGuid();
    public static readonly Guid Pm = Guid.NewGuid();
    public static readonly Guid Sorumlu = Guid.NewGuid();
    public static readonly Guid Hukukcu = Guid.NewGuid();

    /// <summary>Tebliğ 10.11.2026 10:00, cevap son günü 25.11.2026 (15 gün).</summary>
    public static IdareYazisi YeniYazi(DateTimeOffset? teblig = null, DateOnly? sonGun = null)
    {
        var t = teblig ?? An(2026, 11, 10);
        var h = Hesaplayici();
        return IdareYazisi.Olustur("GLN-TEST-1", YaziKaynagi.Kep, "ref-1", Proje, "Bakanlık", "E-1", null, t,
            "Konu", "Metin", "Özet", AnalizKaynagi.Kural,
            new SonGunSonucu(sonGun ?? new DateOnly(2026, 11, 25), false, "test"), h, t);
    }
}
