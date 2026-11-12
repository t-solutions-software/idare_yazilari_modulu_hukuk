namespace IdareYazilari.Domain.Services;

public interface ITatilTakvimi
{
    bool IsGunuMu(DateOnly gun);
}

/// <summary>
/// Türkiye resmi tatilleri. Sabit tarihli tatiller koddadır; dini bayramlar (yıla göre değişir) ve
/// idari izinler yapılandırmadan <see cref="EkTatiller"/> ile verilir.
/// </summary>
public class TurkiyeTatilTakvimi : ITatilTakvimi
{
    private static readonly (int Ay, int Gun)[] SabitTatiller =
    [
        (1, 1),   // Yılbaşı
        (4, 23),  // Ulusal Egemenlik ve Çocuk Bayramı
        (5, 1),   // Emek ve Dayanışma Günü
        (5, 19),  // Atatürk'ü Anma, Gençlik ve Spor Bayramı
        (7, 15),  // Demokrasi ve Milli Birlik Günü
        (8, 30),  // Zafer Bayramı
        (10, 29), // Cumhuriyet Bayramı
    ];

    private readonly HashSet<DateOnly> _ekTatiller;

    public TurkiyeTatilTakvimi(IEnumerable<DateOnly>? ekTatiller = null)
    {
        _ekTatiller = new HashSet<DateOnly>(ekTatiller ?? []);
    }

    public IReadOnlyCollection<DateOnly> EkTatiller => _ekTatiller;

    public bool IsGunuMu(DateOnly gun)
    {
        if (gun.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
        if (SabitTatiller.Any(t => t.Ay == gun.Month && t.Gun == gun.Day)) return false;
        return !_ekTatiller.Contains(gun);
    }
}
