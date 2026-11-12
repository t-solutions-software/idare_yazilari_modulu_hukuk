using System.Net.Http.Json;
using IdareYazilari.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Infrastructure.Entegrasyon;

public class OperasyonAyarlari
{
    /// <summary>"Cekme": operasyon modülü /api/entegrasyon/operasyon/gorevler ucundan alır. "Http": buradan POST edilir.</summary>
    public string Mod { get; set; } = "Cekme";
    /// <summary>Http modunda operasyon modülünün görev alma ucu.</summary>
    public string? Url { get; set; }
    /// <summary>Http modunda gönderilecek API anahtarı (X-Api-Anahtari başlığı).</summary>
    public string? ApiAnahtari { get; set; }
}

/// <summary>
/// Görevleri operasyon modülüne HTTP ile iletir. Gövde, operasyon modülündeki OpsTask alanlarıyla eşleşir
/// (SourceSystem + SourceKey tekil; aynı görev iki kez oluşmaz).
/// </summary>
public class OperasyonGorevAktarici(HttpClient http, IOptions<OperasyonAyarlari> secenekler) : IGorevAktarici
{
    public bool Etkin => secenekler.Value.Mod.Equals("Http", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(secenekler.Value.Url);

    public async Task AktarAsync(GorevAktarimPaketi paket, CancellationToken ct = default)
    {
        using var istek = new HttpRequestMessage(HttpMethod.Post, secenekler.Value.Url) { Content = JsonContent.Create(paket) };
        if (!string.IsNullOrWhiteSpace(secenekler.Value.ApiAnahtari))
            istek.Headers.Add("X-Api-Anahtari", secenekler.Value.ApiAnahtari);
        using var yanit = await http.SendAsync(istek, ct);
        if (!yanit.IsSuccessStatusCode && yanit.StatusCode != System.Net.HttpStatusCode.Conflict) // 409: zaten var
            throw new HttpRequestException($"Operasyon modülü {(int)yanit.StatusCode}: {await yanit.Content.ReadAsStringAsync(ct)}");
    }
}
