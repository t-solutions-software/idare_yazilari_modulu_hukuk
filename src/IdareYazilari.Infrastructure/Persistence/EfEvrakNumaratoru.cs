using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Infrastructure.Persistence;

public class NumaralandirmaAyarlari
{
    /// <summary>Kullanılabilir yer tutucular: {yil}, {no}, {no:000000}, {proje}.</summary>
    public string GelenEvrakFormati { get; set; } = "GLN-{yil}-{no:000000}";
    public string GidenEvrakFormati { get; set; } = "GDN-{yil}-{no:000000}";
    /// <summary>Giden yazı sayısı. Proje bazında ayrı sıra tutulur.</summary>
    public string SayiFormati { get; set; } = "{proje}-{yil}/{no:00000}";
}

/// <summary>
/// Veritabanındaki sayaç tablosuyla yıllık sıralı numara üretir. Sayaç ayrı bir DbContext kapsamında
/// iyimser kilitle artırılır; çakışmada yeniden denenir (iki kullanıcı aynı numarayı alamaz).
/// </summary>
public class EfEvrakNumaratoru(IServiceScopeFactory kapsam, IOptions<NumaralandirmaAyarlari> ayar) : IEvrakNumaratoru
{
    public async Task<string> GelenEvrakNoAsync(DateTimeOffset tarih, CancellationToken ct = default)
    {
        var yil = SureHesaplayici.TurkiyeTarihi(tarih).Year;
        return Bicimle(ayar.Value.GelenEvrakFormati, yil, await ArtirAsync($"GLN:{yil}", ct), null);
    }

    public async Task<(string EvrakNo, string Sayi)> GidenNoAsync(string? projeKodu, DateTimeOffset tarih, CancellationToken ct = default)
    {
        var yil = SureHesaplayici.TurkiyeTarihi(tarih).Year;
        var proje = string.IsNullOrWhiteSpace(projeKodu) ? "GM" : projeKodu;
        var evrakNo = Bicimle(ayar.Value.GidenEvrakFormati, yil, await ArtirAsync($"GDN:{yil}", ct), proje);
        var sayi = Bicimle(ayar.Value.SayiFormati, yil, await ArtirAsync($"SAYI:{proje}:{yil}", ct), proje);
        return (evrakNo, sayi);
    }

    private async Task<int> ArtirAsync(string anahtar, CancellationToken ct)
    {
        for (var deneme = 0; ; deneme++)
        {
            using var scope = kapsam.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdareYazilariDbContext>();
            var sayac = await db.EvrakSayaclari.FirstOrDefaultAsync(s => s.Anahtar == anahtar, ct);
            if (sayac is null) db.EvrakSayaclari.Add(sayac = new EvrakSayaci { Anahtar = anahtar, SonNo = 1 });
            else sayac.SonNo++;
            try
            {
                await db.SaveChangesAsync(ct);
                return sayac.SonNo;
            }
            catch (DbUpdateException) when (deneme < 20)
            {
                await Task.Delay(Random.Shared.Next(5, 50), ct);
            }
        }
    }

    public static string Bicimle(string format, int yil, int no, string? proje)
    {
        var s = format.Replace("{yil}", yil.ToString()).Replace("{proje}", proje ?? "");
        return System.Text.RegularExpressions.Regex.Replace(s, @"\{no(?::(?<f>[0#]+))?\}",
            m => m.Groups["f"].Success ? no.ToString(m.Groups["f"].Value) : no.ToString());
    }
}
