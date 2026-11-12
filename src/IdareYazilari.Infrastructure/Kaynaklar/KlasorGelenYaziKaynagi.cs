using System.Text.Json;
using System.Text.Json.Serialization;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Enums;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace IdareYazilari.Infrastructure.Kaynaklar;

public class KaynakAyari
{
    public string Ad { get; set; } = "";
    public YaziKaynagi Kaynak { get; set; }
    public bool Etkin { get; set; }
    /// <summary>UETS/KEP istemcisinin veya entegrasyon servisinin yazıları bıraktığı klasör.</summary>
    public string Klasor { get; set; } = "";
    /// <summary>Klasördeki tüm yazılar tek projeye aitse proje kodu.</summary>
    public string? ProjeKodu { get; set; }
}

/// <summary>
/// Klasör tabanlı gelen yazı adaptörü. UETS ve KEP sağlayıcılarının istemcileri / entegrasyon servisleri
/// iletileri bir klasöre bırakır; bu adaptör onları okur.
/// Desteklenen biçimler:
///  - *.eml  : KEP iletisi (gönderen, konu, tarih, gövde, ekler). Ek PDF'ler yapay zekâ analizinde okunur.
///  - *.json : Normalleştirilmiş paket (<see cref="JsonPaket"/>), ör. UETS web servis entegrasyonunun çıktısı.
///  - *.txt  : Düz metin yazı.
///  - *.pdf  : Yalnızca PDF (metin yapay zekâ ile çıkarılır).
/// İşlenen dosyalar "islendi", hatalılar "hatali" alt klasörüne taşınır.
/// </summary>
public class KlasorGelenYaziKaynagi(KaynakAyari ayar, ILogger log) : IGelenYaziKaynagi
{
    private static readonly JsonSerializerOptions JsonAyar = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>JSON paketi → paketin klasördeki ek dosyaları (paketle birlikte taşınır).</summary>
    private readonly Dictionary<string, List<string>> _paketEkleri = new();

    public string Ad => ayar.Ad;

    public async Task<IReadOnlyList<(string Anahtar, GelenYaziPaketi Paket)>> YenileriGetirAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(ayar.Klasor)) return [];
        var sonuc = new List<(string, GelenYaziPaketi)>();
        var dosyalar = Directory.EnumerateFiles(ayar.Klasor).OrderBy(File.GetLastWriteTimeUtc).ToList();

        // JSON paketlerinin referans verdiği ekler ayrı yazı olarak okunmamalı.
        var paketEkleri = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var json in dosyalar.Where(d => d.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var j = JsonSerializer.Deserialize<JsonPaket>(await File.ReadAllTextAsync(json, ct), JsonAyar);
                foreach (var ek in j?.Ekler ?? []) paketEkleri.Add(Path.Combine(ayar.Klasor, Path.GetFileName(ek)));
            }
            catch (JsonException) { /* aşağıda hatalı olarak işaretlenir */ }
        }

        foreach (var dosya in dosyalar.Where(d => !paketEkleri.Contains(d)))
        {
            try
            {
                var paket = Path.GetExtension(dosya).ToLowerInvariant() switch
                {
                    ".eml" => await EmlOkuAsync(dosya, ct),
                    ".json" => await JsonOkuAsync(dosya, ct),
                    ".txt" => await TxtOkuAsync(dosya, ct),
                    ".pdf" => await PdfOkuAsync(dosya, ct),
                    _ => null,
                };
                if (paket is not null) sonuc.Add((dosya, paket));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "{Kaynak}: {Dosya} okunamadı", ayar.Ad, dosya);
                Tasi(dosya, "hatali");
            }
        }
        return sonuc;
    }

    public Task IslendiAsync(string anahtar, bool basarili, string? hata, CancellationToken ct = default)
    {
        Tasi(anahtar, basarili ? "islendi" : "hatali");
        if (_paketEkleri.Remove(anahtar, out var ekler))
            foreach (var ek in ekler) Tasi(ek, basarili ? "islendi" : "hatali");
        if (!basarili) log.LogError("{Kaynak}: {Dosya} kaydedilemedi: {Hata}", ayar.Ad, anahtar, hata);
        return Task.CompletedTask;
    }

    private async Task<GelenYaziPaketi> EmlOkuAsync(string dosya, CancellationToken ct)
    {
        var mesaj = await MimeMessage.LoadAsync(dosya, ct);
        var ekler = new List<GelenEk>();
        foreach (var ek in mesaj.Attachments.OfType<MimePart>())
        {
            using var ms = new MemoryStream();
            await ek.Content.DecodeToAsync(ms, ct);
            ekler.Add(new GelenEk(ek.FileName ?? "ek", ek.ContentType.MimeType, ms.ToArray()));
        }
        var metin = mesaj.TextBody ?? "";
        // KEP iletilerinde asıl yazı genellikle ek PDF'tedir; gövdede yalnızca üst yazı bulunur.
        var txtEk = ekler.FirstOrDefault(e => e.IcerikTuru == "text/plain");
        if (txtEk is not null) metin = System.Text.Encoding.UTF8.GetString(txtEk.Icerik);

        return new GelenYaziPaketi
        {
            Kaynak = ayar.Kaynak,
            DisReferans = mesaj.MessageId ?? Path.GetFileName(dosya),
            ProjeKodu = ayar.ProjeKodu,
            AliciAdres = mesaj.To.Mailboxes.FirstOrDefault()?.Address,
            GonderenIdare = mesaj.From.Mailboxes.FirstOrDefault() is { } f ? (string.IsNullOrWhiteSpace(f.Name) ? f.Address : f.Name) : null,
            TebligTarihi = mesaj.Date == DateTimeOffset.MinValue ? DateTimeOffset.Now : mesaj.Date,
            Konu = mesaj.Subject,
            Metin = metin,
            Ekler = ekler,
        };
    }

    private async Task<GelenYaziPaketi> JsonOkuAsync(string dosya, CancellationToken ct)
    {
        await using var akim = File.OpenRead(dosya);
        var j = await JsonSerializer.DeserializeAsync<JsonPaket>(akim, JsonAyar, ct)
                ?? throw new InvalidDataException("Boş JSON paketi");
        var klasor = Path.GetDirectoryName(dosya)!;
        var ekler = new List<GelenEk>();
        var ekYollari = new List<string>();
        foreach (var ek in j.Ekler ?? [])
        {
            var yol = Path.Combine(klasor, Path.GetFileName(ek));
            if (!File.Exists(yol))
                throw new FileNotFoundException($"Paketin eki bulunamadı: {ek}");
            ekler.Add(new GelenEk(Path.GetFileName(yol), IcerikTuru(yol), await File.ReadAllBytesAsync(yol, ct)));
            ekYollari.Add(yol);
        }
        _paketEkleri[dosya] = ekYollari;
        return new GelenYaziPaketi
        {
            Kaynak = j.Kaynak ?? ayar.Kaynak,
            DisReferans = j.DisReferans ?? Path.GetFileName(dosya),
            ProjeKodu = j.ProjeKodu ?? ayar.ProjeKodu,
            AliciAdres = j.AliciAdres,
            GonderenIdare = j.GonderenIdare,
            GelenSayi = j.GelenSayi,
            YaziTarihi = j.YaziTarihi,
            TebligTarihi = j.TebligTarihi ?? File.GetLastWriteTime(dosya),
            Konu = j.Konu,
            Metin = j.Metin ?? "",
            CevapSonGunu = j.CevapSonGunu,
            Ekler = ekler,
        };
    }

    private async Task<GelenYaziPaketi> TxtOkuAsync(string dosya, CancellationToken ct) => new()
    {
        Kaynak = ayar.Kaynak,
        DisReferans = Path.GetFileName(dosya),
        ProjeKodu = ayar.ProjeKodu,
        TebligTarihi = File.GetLastWriteTime(dosya),
        Metin = await File.ReadAllTextAsync(dosya, ct),
    };

    private async Task<GelenYaziPaketi> PdfOkuAsync(string dosya, CancellationToken ct) => new()
    {
        Kaynak = ayar.Kaynak,
        DisReferans = Path.GetFileName(dosya),
        ProjeKodu = ayar.ProjeKodu,
        TebligTarihi = File.GetLastWriteTime(dosya),
        Ekler = [new GelenEk(Path.GetFileName(dosya), "application/pdf", await File.ReadAllBytesAsync(dosya, ct))],
    };

    private void Tasi(string dosya, string altKlasor)
    {
        if (!File.Exists(dosya)) return;
        var hedefKlasor = Path.Combine(ayar.Klasor, altKlasor);
        Directory.CreateDirectory(hedefKlasor);
        var hedef = Path.Combine(hedefKlasor, $"{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(dosya)}");
        File.Move(dosya, hedef, overwrite: true);
    }

    private static string IcerikTuru(string yol) => Path.GetExtension(yol).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        _ => "application/octet-stream",
    };

    public sealed class JsonPaket
    {
        public YaziKaynagi? Kaynak { get; set; }
        public string? DisReferans { get; set; }
        public string? ProjeKodu { get; set; }
        public string? AliciAdres { get; set; }
        public string? GonderenIdare { get; set; }
        public string? GelenSayi { get; set; }
        public DateOnly? YaziTarihi { get; set; }
        public DateTimeOffset? TebligTarihi { get; set; }
        public string? Konu { get; set; }
        public string? Metin { get; set; }
        public DateOnly? CevapSonGunu { get; set; }
        /// <summary>Aynı klasördeki ek dosya adları.</summary>
        public List<string>? Ekler { get; set; }
    }
}
