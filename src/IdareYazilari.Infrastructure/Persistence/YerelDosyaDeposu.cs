using IdareYazilari.Application.Abstractions;

namespace IdareYazilari.Infrastructure.Persistence;

/// <summary>Ekleri yerel/paylaşımlı klasörde saklar. Kurumsal belge yönetim sistemiyle değiştirilebilir.</summary>
public class YerelDosyaDeposu(string kokKlasor) : IDosyaDeposu
{
    public async Task<string> KaydetAsync(Guid yaziId, string dosyaAdi, byte[] icerik, CancellationToken ct = default)
    {
        var guvenliAd = string.Concat(Path.GetFileName(dosyaAdi).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var anahtar = Path.Combine(yaziId.ToString("N"), $"{Guid.NewGuid():N}_{guvenliAd}");
        var yol = Path.Combine(kokKlasor, anahtar);
        Directory.CreateDirectory(Path.GetDirectoryName(yol)!);
        await File.WriteAllBytesAsync(yol, icerik, ct);
        return anahtar;
    }

    public Task<Stream?> AcAsync(string depoAnahtari, CancellationToken ct = default)
    {
        var yol = Path.GetFullPath(Path.Combine(kokKlasor, depoAnahtari));
        if (!yol.StartsWith(Path.GetFullPath(kokKlasor), StringComparison.Ordinal) || !File.Exists(yol))
            return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(File.OpenRead(yol));
    }
}
