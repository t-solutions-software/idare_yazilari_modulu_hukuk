using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Services;

namespace IdareYazilari.Application.Analiz;

public sealed record AnalizGirdisi(
    string Metin,
    string? KonuIpucu,
    DateTimeOffset TebligTarihi,
    IReadOnlyList<GelenEk> Ekler);

/// <summary>Bir analizcinin ham çıktısı: son gün hesabı yapılmaz, yalnızca süre ifadesi bulunur.</summary>
public sealed record HamAnaliz(
    string Konu,
    string Ozet,
    CevapSuresiIfadesi CevapSuresi,
    AnalizKaynagi Kaynak,
    string? GonderenIdare = null,
    string? GelenSayi = null,
    DateOnly? YaziTarihi = null,
    /// <summary>Metin ek dosyalardan (ör. PDF) okunduysa çıkarılan düz metin.</summary>
    string? CikarilanMetin = null);

public interface IYaziAnalizcisi
{
    Task<HamAnaliz?> AnalizEtAsync(AnalizGirdisi girdi, CancellationToken ct = default);
}

/// <summary>Yapay zekâ tabanlı analizci (opsiyonel). Yapılandırılmamışsa kayıt edilmez.</summary>
public interface IYapayZekaAnalizcisi : IYaziAnalizcisi;

public sealed record AnalizSonucu(
    string Konu,
    string Ozet,
    SonGunSonucu SonGun,
    AnalizKaynagi Kaynak,
    string? GonderenIdare,
    string? GelenSayi,
    DateOnly? YaziTarihi,
    string? CikarilanMetin);
