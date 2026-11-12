using IdareYazilari.Domain.Enums;

namespace IdareYazilari.Domain.Services;

/// <summary>
/// Yazı metninden çıkarılan cevap süresi ifadesi. Son günün hesaplanması (aritmetik) her zaman
/// <see cref="SureHesaplayici"/> tarafından yapılır; çıkarım yapan bileşen (kural/LLM) yalnızca ifadeyi bulur.
/// </summary>
public sealed record CevapSuresiIfadesi(
    CevapSuresiTuru Tur,
    int? Gun = null,
    DateOnly? Tarih = null,
    string? KaynakMetin = null)
{
    public static CevapSuresiIfadesi Bulunamadi { get; } = new(CevapSuresiTuru.Bulunamadi);
}
