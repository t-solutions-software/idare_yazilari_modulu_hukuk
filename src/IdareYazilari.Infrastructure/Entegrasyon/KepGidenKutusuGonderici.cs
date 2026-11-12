using IdareYazilari.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace IdareYazilari.Infrastructure.Entegrasyon;

public class KepGonderimAyarlari
{
    public bool Etkin { get; set; }
    /// <summary>KEP istemcisinin / entegrasyon servisinin gönderilecek iletileri aldığı klasör.</summary>
    public string GidenKlasor { get; set; } = "veri/giden/kep";
    /// <summary>Proje KEP adresi tanımlı değilse kullanılacak gönderen adres.</summary>
    public string? VarsayilanGonderen { get; set; }
}

/// <summary>
/// KEP gönderim kancası: imzalı/sayılı giden yazıyı ekleriyle birlikte .eml olarak KEP giden klasörüne bırakır.
/// KEP sağlayıcısının istemcisi (veya entegrasyon servisi) bu klasörü işleyip iletiyi e-imzayla gönderir.
/// Sağlayıcının web servisine doğrudan bağlanmak için aynı arayüzü uygulayan yeni bir sınıf yazılabilir.
/// </summary>
public class KepGidenKutusuGonderici(IOptions<KepGonderimAyarlari> secenekler, ILogger<KepGidenKutusuGonderici> log) : IGidenYaziGonderici
{
    public string Kanal => "KEP";

    public async Task<GonderimSonucu> GonderAsync(GonderimTalebi t, CancellationToken ct = default)
    {
        var a = secenekler.Value;
        var gonderen = t.GonderenAdres ?? a.VarsayilanGonderen;
        if (string.IsNullOrWhiteSpace(gonderen))
            return new GonderimSonucu(false, null, "Projenin KEP adresi tanımlı değil.");
        if (string.IsNullOrWhiteSpace(t.AliciAdres))
            return new GonderimSonucu(false, null, "İdarenin KEP adresi bilinmiyor; gönderimde alıcı adresini girin.");

        var mesaj = new MimeMessage();
        mesaj.From.Add(MailboxAddress.Parse(gonderen));
        mesaj.To.Add(MailboxAddress.Parse(t.AliciAdres));
        mesaj.Subject = t.Konu;
        mesaj.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(gonderen.Split('@').Last());
        mesaj.Headers.Add("X-Idare-Yazisi", t.YaziId.ToString());
        mesaj.Headers.Add("X-Giden-Evrak-No", t.Giden.EvrakNo);

        var govde = new BodyBuilder { TextBody = $"Sayı: {t.Giden.Sayi}\nKonu: {t.Giden.Konu}\n\nYazımız ektedir." };
        foreach (var ek in t.Ekler)
            govde.Attachments.Add(ek.DosyaAdi, ek.Icerik, ContentType.Parse(ek.IcerikTuru));
        mesaj.Body = govde.ToMessageBody();

        Directory.CreateDirectory(a.GidenKlasor);
        var dosya = Path.Combine(a.GidenKlasor, $"{t.Giden.EvrakNo}.eml");
        var gecici = dosya + ".tmp";
        await mesaj.WriteToAsync(gecici, ct);
        File.Move(gecici, dosya, overwrite: true); // istemci yarım dosya görmesin
        log.LogInformation("KEP giden kutusuna bırakıldı: {Dosya} → {Alici}", dosya, t.AliciAdres);
        return new GonderimSonucu(true, mesaj.MessageId, null);
    }
}
