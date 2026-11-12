using System.Net;
using System.Net.Mail;
using IdareYazilari.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Infrastructure.Bildirimler;

/// <summary>Bildirimleri uygulama günlüğüne yazar (geliştirme ortamı ve denetim izi için).</summary>
public class LogBildirimGonderici(ILogger<LogBildirimGonderici> log) : IBildirimGonderici
{
    public Task GonderAsync(Bildirim b, CancellationToken ct = default)
    {
        log.LogInformation("BİLDİRİM → {Alici} <{Eposta}>: {Mesaj}", b.Alici.AdSoyad, b.Alici.Eposta, b.Mesaj);
        return Task.CompletedTask;
    }
}

public class SmtpAyarlari
{
    public bool Etkin { get; set; }
    public string Sunucu { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool Ssl { get; set; } = true;
    public string? KullaniciAdi { get; set; }
    public string? Sifre { get; set; }
    public string Gonderen { get; set; } = "idare-yazilari@example.com";
    /// <summary>E-postadaki "Yazıyı aç" bağlantısının kök adresi.</summary>
    public string UygulamaAdresi { get; set; } = "https://localhost:5001";
}

public class SmtpBildirimGonderici(IOptions<SmtpAyarlari> secenekler) : IBildirimGonderici
{
    public async Task GonderAsync(Bildirim b, CancellationToken ct = default)
    {
        var a = secenekler.Value;
        if (!a.Etkin || string.IsNullOrWhiteSpace(b.Alici.Eposta)) return;

        using var istemci = new SmtpClient(a.Sunucu, a.Port) { EnableSsl = a.Ssl };
        if (!string.IsNullOrWhiteSpace(a.KullaniciAdi))
            istemci.Credentials = new NetworkCredential(a.KullaniciAdi, a.Sifre);

        using var mesaj = new MailMessage(a.Gonderen, b.Alici.Eposta)
        {
            Subject = b.Konu,
            Body = $"{b.Mesaj}\n\nYazıyı açmak için: {a.UygulamaAdresi.TrimEnd('/')}/#yazi={b.YaziId}",
        };
        await istemci.SendMailAsync(mesaj, ct);
    }
}
