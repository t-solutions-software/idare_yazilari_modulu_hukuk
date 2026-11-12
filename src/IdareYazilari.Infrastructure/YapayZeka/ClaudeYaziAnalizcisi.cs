using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using IdareYazilari.Application.Analiz;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Infrastructure.YapayZeka;

public class YapayZekaAyarlari
{
    public bool Etkin { get; set; }
    /// <summary>Boşsa ANTHROPIC_API_KEY ortam değişkeni kullanılır.</summary>
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-opus-5-5";
    /// <summary>low / medium / high</summary>
    public string Effort { get; set; } = "medium";
    /// <summary>Analize gönderilecek PDF eklerinin toplam azami boyutu (MB).</summary>
    public int AzamiPdfMb { get; set; } = 20;
}

/// <summary>
/// Claude ile yazıdan konu, özet ve cevap süresi ifadesini çıkarır. Son günün hesaplanması model tarafından
/// değil <see cref="SureHesaplayici"/> tarafından yapılır; model yalnızca metindeki ifadeyi raporlar.
/// Metin yalnızca PDF ekteyse PDF doğrudan modele verilir ve metin de çıkarılır.
/// </summary>
public class ClaudeYaziAnalizcisi(
    AnthropicClient istemci,
    IOptions<YapayZekaAyarlari> secenekler,
    ILogger<ClaudeYaziAnalizcisi> log) : IYapayZekaAnalizcisi
{
    private const string SistemTalimati = """
        Bir kamu-özel işbirliği (KÖİ/PPP) proje şirketine idarelerden (bakanlık, il müdürlüğü, hastane, belediye vb.)
        gelen resmi yazıları analiz ediyorsun. Amaç, yazının proje müdürünün ekranında doğru önceliklendirilmesi ve
        cevap süresinin kaçırılmamasıdır.

        Kurallar:
        - konu: Yazıda "Konu:" satırı varsa onu aynen yaz. Yoksa yazının konusunu tek satırda, resmi dille belirt.
        - ozet: 2-4 cümlelik Türkçe özet. Önce idarenin PPP şirketinden ne istediğini (bilgi, belge, düzeltme, savunma,
          görüş vb.) açıkça yaz; varsa yaptırım/ceza ihtimali, ilgi yazıları ve sözleşme maddelerini belirt.
          Yazıda olmayan bilgi ekleme.
        - cevap_suresi: Yazıda cevap / işlem için verilen süreyi raporla. Tarih hesaplaması YAPMA; sadece metindeki ifadeyi aktar.
          * "en geç 20.11.2026 tarihine kadar" gibi kesin tarih → tur "kesin_tarih", tarih "2026-11-20".
          * "15 gün içinde", "on beş (15) gün içerisinde" → tur "takvim_gunu", gun 15.
          * "7 iş günü içinde" → tur "is_gunu", gun 7.
          * "1 hafta" → tur "takvim_gunu", gun 7. "1 ay içinde" → tur "ay", gun 1.
          * Birden fazla süre varsa PPP şirketinin cevabı / işlemi için verilen EN KISA süreyi seç.
          * "ivedilikle", "acilen" gibi süre belirtmeyen ifadeler veya süre yoksa → tur "bulunamadi" ve kaynak_ifade'ye ifadeyi yaz.
          * kaynak_ifade: süreyi bulduğun ifadenin metindeki hali.
          Kullanılmayan alanlar: gun için 0, tarih için boş metin.
        - gonderen_idare, gelen_sayi, yazi_tarihi (YYYY-MM-DD): yazıdan oku; bulamazsan boş metin.
        - metin: Yazı sana PDF olarak verildiyse yazının tam metnini düz metin olarak çıkar; metin zaten verildiyse boş bırak.
        """;

    private static readonly Dictionary<string, JsonElement> Sema = new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object"),
        ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        ["required"] = JsonSerializer.SerializeToElement(new[] { "konu", "ozet", "cevap_suresi", "gonderen_idare", "gelen_sayi", "yazi_tarihi", "metin" }),
        ["properties"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>
        {
            ["konu"] = new { type = "string" },
            ["ozet"] = new { type = "string" },
            ["gonderen_idare"] = new { type = "string" },
            ["gelen_sayi"] = new { type = "string" },
            ["yazi_tarihi"] = new { type = "string" },
            ["metin"] = new { type = "string" },
            ["cevap_suresi"] = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new[] { "tur", "gun", "tarih", "kaynak_ifade" },
                ["properties"] = new Dictionary<string, object>
                {
                    ["tur"] = new { type = "string", @enum = new[] { "kesin_tarih", "takvim_gunu", "is_gunu", "ay", "bulunamadi" } },
                    ["gun"] = new { type = "integer" },
                    ["tarih"] = new { type = "string" },
                    ["kaynak_ifade"] = new { type = "string" },
                },
            },
        }),
    };

    public async Task<HamAnaliz?> AnalizEtAsync(AnalizGirdisi girdi, CancellationToken ct = default)
    {
        var ayar = secenekler.Value;
        var icerik = new List<BetaContentBlockParam>();

        var metinVar = !string.IsNullOrWhiteSpace(girdi.Metin);
        long toplam = 0;
        foreach (var pdf in girdi.Ekler.Where(e => e.IcerikTuru == "application/pdf"))
        {
            toplam += pdf.Icerik.LongLength;
            if (toplam > ayar.AzamiPdfMb * 1024L * 1024L) break;
            icerik.Add(new BetaRequestDocumentBlock
            {
                Source = new BetaBase64PdfSource { Data = Convert.ToBase64String(pdf.Icerik) },
                Title = pdf.DosyaAdi,
            });
            if (metinVar) break; // Metin varsa ilk PDF yeterli bağlam sağlar.
        }
        if (!metinVar && icerik.Count == 0) return null;

        var teblig = SureHesaplayici.TurkiyeTarihi(girdi.TebligTarihi);
        icerik.Add(new BetaTextBlockParam
        {
            Text = $"""
                Tebliğ tarihi: {teblig:yyyy-MM-dd}
                {(girdi.KonuIpucu is null ? "" : $"İleti konusu (ipucu): {girdi.KonuIpucu}\n")}
                {(metinVar ? $"<yazi>\n{girdi.Metin}\n</yazi>" : "Yazı yukarıdaki PDF belge(ler)dedir; metni de çıkar.")}
                """,
        });

        var yanit = await istemci.Beta.Messages.Create(new MessageCreateParams
        {
            Model = ayar.Model,
            MaxTokens = 16000,
            System = SistemTalimati,
            Messages = [new BetaUserMessageParam { Content = icerik }],
            OutputConfig = new BetaOutputConfig
            {
                Effort = ayar.Effort,
                Format = new BetaJsonOutputFormat { Schema = Sema },
            },
            // Güvenlik sınıflandırıcısı bir isteği reddederse sunucu tarafında uygun modele düşülür.
            Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
            Fallbacks = new Default(),
        }, ct);

        if (yanit.StopReason == "refusal")
        {
            log.LogWarning("Yapay zekâ analizi reddedildi: {Detay}", yanit.StopDetails?.Explanation);
            return null;
        }
        if (yanit.StopReason == "max_tokens")
        {
            log.LogWarning("Yapay zekâ yanıtı max_tokens ile kesildi.");
            return null;
        }

        var json = string.Concat(yanit.Content.Select(b => b.TryPickText(out var t) ? t.Text : ""));
        var s = JsonSerializer.Deserialize<Cikti>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Yapay zekâ yanıtı çözümlenemedi.");

        return new HamAnaliz(
            Konu: s.konu.Trim(),
            Ozet: s.ozet.Trim(),
            CevapSuresi: SureyeDonustur(s.cevap_suresi),
            Kaynak: AnalizKaynagi.YapayZeka,
            GonderenIdare: Bos(s.gonderen_idare),
            GelenSayi: Bos(s.gelen_sayi),
            YaziTarihi: DateOnly.TryParse(s.yazi_tarihi, out var yt) ? yt : null,
            CikarilanMetin: metinVar ? null : Bos(s.metin));
    }

    private static CevapSuresiIfadesi SureyeDonustur(SureCiktisi c) => c.tur switch
    {
        "kesin_tarih" when DateOnly.TryParse(c.tarih, out var t) => new(CevapSuresiTuru.KesinTarih, Tarih: t, KaynakMetin: c.kaynak_ifade),
        "takvim_gunu" when c.gun > 0 => new(CevapSuresiTuru.TakvimGunu, c.gun, KaynakMetin: c.kaynak_ifade),
        "is_gunu" when c.gun > 0 => new(CevapSuresiTuru.IsGunu, c.gun, KaynakMetin: c.kaynak_ifade),
        "ay" when c.gun > 0 => new(CevapSuresiTuru.Ay, c.gun, KaynakMetin: c.kaynak_ifade),
        _ => new(CevapSuresiTuru.Bulunamadi, KaynakMetin: Bos(c.kaynak_ifade)),
    };

    private static string? Bos(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ReSharper disable InconsistentNaming
    private sealed record Cikti(string konu, string ozet, SureCiktisi cevap_suresi, string gonderen_idare, string gelen_sayi, string yazi_tarihi, string metin);
    private sealed record SureCiktisi(string tur, int gun, string tarih, string kaynak_ifade);
}
