using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Services;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Infrastructure.Belgeler;

public class BelgeAyarlari
{
    /// <summary>Kurum Word şablonu (.docx). Boşsa yerleşik şablon kullanılır.</summary>
    public string? SablonYolu { get; set; }
}

/// <summary>
/// Giden yazıyı Word şablonundan üretir. Şablondaki yer tutucular:
/// {{ProjeAdi}}, {{Sayi}}, {{Tarih}}, {{Muhatap}}, {{MuhatapBuyuk}}, {{Konu}}, {{Ilgi}}, {{Metin}}, {{ImzaYetkilisi}}, {{TaslakIbaresi}}.
/// {{Metin}} bulunan paragraf, metnin her satırı için çoğaltılır (paragraf biçimi korunur).
/// {{Ilgi}} boşsa o paragraf silinir.
/// </summary>
public class WordBelgeOlusturucu(IOptions<BelgeAyarlari> ayar) : IBelgeOlusturucu
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public byte[] Olustur(GidenYaziBelgeModeli m)
    {
        var sablon = !string.IsNullOrWhiteSpace(ayar.Value.SablonYolu) && File.Exists(ayar.Value.SablonYolu)
            ? File.ReadAllBytes(ayar.Value.SablonYolu)
            : VarsayilanSablon();

        using var ms = new MemoryStream();
        ms.Write(sablon);
        using (var doc = WordprocessingDocument.Open(ms, true))
        {
            var degerler = new Dictionary<string, string?>
            {
                ["ProjeAdi"] = m.ProjeAdi ?? "",
                ["Sayi"] = m.Sayi ?? (m.Taslak ? "(sayı imzada verilecek)" : ""),
                ["Tarih"] = m.Tarih.ToOffset(SureAyarlari.TurkiyeSaatFarki).ToString("dd.MM.yyyy", Tr),
                ["Muhatap"] = m.Muhatap,
                ["MuhatapBuyuk"] = m.Muhatap.ToUpper(Tr),
                ["Konu"] = m.Konu,
                ["Ilgi"] = m.Ilgi,
                ["ImzaYetkilisi"] = m.ImzaYetkilisi ?? "",
                ["TaslakIbaresi"] = m.Taslak ? "TASLAK" : "",
            };
            var parcalar = new List<OpenXmlPartRootElement?> { doc.MainDocumentPart!.Document };
            parcalar.AddRange(doc.MainDocumentPart.HeaderParts.Select(h => (OpenXmlPartRootElement?)h.Header));
            parcalar.AddRange(doc.MainDocumentPart.FooterParts.Select(f => (OpenXmlPartRootElement?)f.Footer));
            foreach (var kok in parcalar.Where(p => p is not null))
                foreach (var p in kok!.Descendants<Paragraph>().ToList())
                    ParagrafiDoldur(p, degerler, m.Metin);
            doc.MainDocumentPart.Document.Save();
        }
        return ms.ToArray();
    }

    private static void ParagrafiDoldur(Paragraph p, Dictionary<string, string?> degerler, string metin)
    {
        var metinler = p.Descendants<Text>().ToList();
        var tum = string.Concat(metinler.Select(t => t.Text));
        if (!tum.Contains("{{")) return;

        if (tum.Contains("{{Metin}}"))
        {
            var satirlar = metin.Replace("\r", "").Split('\n');
            foreach (var satir in satirlar.Reverse())
            {
                var kopya = (Paragraph)p.CloneNode(true);
                MetniYaz(kopya, tum.Replace("{{Metin}}", satir));
                p.InsertAfterSelf(kopya);
            }
            p.Remove();
            return;
        }
        if (tum.Contains("{{Ilgi}}") && string.IsNullOrWhiteSpace(degerler["Ilgi"]))
        {
            p.Remove();
            return;
        }
        foreach (var (anahtar, deger) in degerler)
            tum = tum.Replace("{{" + anahtar + "}}", deger ?? "");
        MetniYaz(p, tum);
    }

    /// <summary>Paragraftaki metni ilk çalıştırmaya (run) yazar, diğerlerini boşaltır; ilk çalıştırmanın biçimi korunur.</summary>
    private static void MetniYaz(Paragraph p, string metin)
    {
        var metinler = p.Descendants<Text>().ToList();
        if (metinler.Count == 0) return;
        metinler[0].Text = metin;
        metinler[0].Space = SpaceProcessingModeValues.Preserve;
        foreach (var t in metinler.Skip(1)) t.Text = "";
    }

    /// <summary>Kurum şablonu tanımlanmadığında kullanılan sade resmi yazı şablonu.</summary>
    public static byte[] VarsayilanSablon()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            Paragraph Par(string metin, JustificationValues? hiza = null, bool kalin = false, string? renk = null, int boyut = 24,
                int? ilkSatirGirinti = null, int sonra = 120)
            {
                var rp = new RunProperties(new RunFonts { Ascii = "Times New Roman", HighAnsi = "Times New Roman" }, new FontSize { Val = boyut.ToString() });
                if (kalin) rp.Append(new Bold());
                if (renk is not null) rp.Append(new Color { Val = renk });
                var pp = new ParagraphProperties(new SpacingBetweenLines { After = sonra.ToString() });
                if (hiza is not null) pp.Append(new Justification { Val = hiza.Value });
                if (ilkSatirGirinti is not null) pp.Append(new Indentation { FirstLine = ilkSatirGirinti.Value.ToString() });
                return new Paragraph(pp, new Run(rp, new Text(metin) { Space = SpaceProcessingModeValues.Preserve }));
            }
            main.Document = new Document(new Body(
                Par("{{TaslakIbaresi}}", JustificationValues.Center, true, "C00000", 28),
                Par("{{ProjeAdi}}", JustificationValues.Center, true, boyut: 26, sonra: 360),
                Par("Sayı  : {{Sayi}}", sonra: 0),
                Par("Tarih : {{Tarih}}", sonra: 0),
                Par("Konu  : {{Konu}}", sonra: 480),
                Par("{{MuhatapBuyuk}}NE", JustificationValues.Center, true, sonra: 360),
                Par("İlgi : {{Ilgi}}", sonra: 240),
                Par("{{Metin}}", JustificationValues.Both, ilkSatirGirinti: 708),
                Par("", sonra: 480),
                Par("{{ImzaYetkilisi}}", JustificationValues.Right, true),
                new SectionProperties(new PageMargin { Top = 1418, Bottom = 1418, Left = 1418, Right = 1418 })));
            main.Document.Save();
        }
        return ms.ToArray();
    }
}
