using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Services;

namespace IdareYazilari.Application.Analiz;

/// <summary>
/// Düzenli ifadelerle Türkçe resmi yazılardan konu, sayı, süre ve özet çıkarır.
/// Yapay zekâ yapılandırılmadığında tek başına, yapılandırıldığında çapraz kontrol için kullanılır.
/// </summary>
public partial class KuralTabanliAnalizci : IYaziAnalizcisi
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Dictionary<string, int> SayiKelimeleri = new()
    {
        ["bir"] = 1, ["iki"] = 2, ["üç"] = 3, ["dört"] = 4, ["beş"] = 5, ["altı"] = 6, ["yedi"] = 7,
        ["sekiz"] = 8, ["dokuz"] = 9, ["on"] = 10, ["yirmi"] = 20, ["otuz"] = 30, ["kırk"] = 40,
        ["elli"] = 50, ["altmış"] = 60, ["yetmiş"] = 70, ["seksen"] = 80, ["doksan"] = 90,
    };

    private static readonly Dictionary<string, int> Aylar = new()
    {
        ["ocak"] = 1, ["şubat"] = 2, ["mart"] = 3, ["nisan"] = 4, ["mayıs"] = 5, ["haziran"] = 6,
        ["temmuz"] = 7, ["ağustos"] = 8, ["eylül"] = 9, ["ekim"] = 10, ["kasım"] = 11, ["aralık"] = 12,
    };

    private const string SayiKelime = "(?:(?:bir|iki|üç|dört|beş|altı|yedi|sekiz|dokuz|on|yirmi|otuz|kırk|elli|altmış|yetmiş|seksen|doksan)(?!\\p{L})\\s*)+";

    [GeneratedRegex(@"(?<g>\d{1,2})\s*[./-]\s*(?<a>\d{1,2})\s*[./-]\s*(?<y>\d{4})\s*(?:tarihine|tarihi|günü)\s*(?:kadar|dek|değin|mesai\s+bitimine\s+kadar)", RegexOptions.IgnoreCase)]
    private static partial Regex TarihineKadarRegex();

    [GeneratedRegex(@"en\s+geç\s+(?<g>\d{1,2})\s*[./-]\s*(?<a>\d{1,2})\s*[./-]\s*(?<y>\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex EnGecRegex();

    [GeneratedRegex(@"(?<g>\d{1,2})\s+(?<ay>ocak|şubat|mart|nisan|mayıs|haziran|temmuz|ağustos|eylül|ekim|kasım|aralık)\s+(?<y>\d{4})\s*(?:tarihine|tarihi|günü)\s*(?:kadar|dek|değin)", RegexOptions.IgnoreCase)]
    private static partial Regex AyAdliTarihRegex();

    [GeneratedRegex(@"(?<![\p{L}\d])(?<sayi>\d{1,3}|" + SayiKelime + @")\s*(?:\((?<parantez>[^)]{1,30})\))?\s*(?<is>iş\s+)?(?<birim>gün|hafta|ay)(?:ü|lük|lık|dür|dir)?\s*(?:lık\s+)?(?:süre\s+)?(?:içinde|içerisinde|zarfında|içerisinde|dahilinde)", RegexOptions.IgnoreCase)]
    private static partial Regex SureRegex();

    [GeneratedRegex(@"ivedi|acilen|acil\s+olarak|derhal|gecikmeksizin", RegexOptions.IgnoreCase)]
    private static partial Regex IvediRegex();

    [GeneratedRegex(@"^\s*konu\s*:\s*(?<v>.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex KonuRegex();

    [GeneratedRegex(@"^\s*sayı\s*:\s*(?<v>.+?)\s*(?:(?<g>\d{1,2})[./](?<a>\d{1,2})[./](?<y>\d{4}))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SayiRegex();

    [GeneratedRegex(@"^\s*tarih\s*:\s*(?<g>\d{1,2})[./](?<a>\d{1,2})[./](?<y>\d{4})", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TarihSatiriRegex();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-ZÇĞİÖŞÜ0-9])")]
    private static partial Regex CumleAyiriciRegex();

    [GeneratedRegex(@"rica|talep|bildirilmesi|gönderilmesi|iletilmesi|sunulması|gerekmekte|yapılması|tamamlanması|giderilmesi|açıklama|savunma", RegexOptions.IgnoreCase)]
    private static partial Regex TalepRegex();

    public Task<HamAnaliz?> AnalizEtAsync(AnalizGirdisi girdi, CancellationToken ct = default)
    {
        var metin = girdi.Metin ?? "";
        if (string.IsNullOrWhiteSpace(metin))
            return Task.FromResult<HamAnaliz?>(null);

        var teblig = SureHesaplayici.TurkiyeTarihi(girdi.TebligTarihi);
        var konu = KonuBul(metin) ?? girdi.KonuIpucu ?? IlkAnlamliSatir(metin) ?? "(Konu tespit edilemedi)";
        var (sayi, sayiTarihi) = SayiBul(metin);
        var yaziTarihi = TarihSatiriBul(metin) ?? sayiTarihi;

        return Task.FromResult<HamAnaliz?>(new HamAnaliz(
            Konu: konu,
            Ozet: OzetCikar(metin),
            CevapSuresi: SureBul(metin, teblig),
            Kaynak: AnalizKaynagi.Kural,
            GonderenIdare: GonderenBul(metin),
            GelenSayi: sayi,
            YaziTarihi: yaziTarihi));
    }

    /// <summary>Metindeki tüm süre ifadelerini bulur ve en kısa süreyi (en ihtiyatlı olanı) seçer.</summary>
    public static CevapSuresiIfadesi SureBul(string metin, DateOnly teblig)
    {
        var adaylar = new List<(CevapSuresiIfadesi Ifade, int YaklasikGun)>();
        var normal = Normalize(metin);

        foreach (Match m in TarihineKadarRegex().Matches(normal).Concat(EnGecRegex().Matches(normal)))
            if (TarihOlustur(m.Groups["g"].Value, m.Groups["a"].Value, m.Groups["y"].Value) is { } t)
                adaylar.Add((new CevapSuresiIfadesi(CevapSuresiTuru.KesinTarih, Tarih: t, KaynakMetin: m.Value.Trim()), t.DayNumber - teblig.DayNumber));

        foreach (Match m in AyAdliTarihRegex().Matches(normal))
            if (TarihOlustur(m.Groups["g"].Value, Aylar[m.Groups["ay"].Value.ToLower(Tr)].ToString(), m.Groups["y"].Value) is { } t)
                adaylar.Add((new CevapSuresiIfadesi(CevapSuresiTuru.KesinTarih, Tarih: t, KaynakMetin: m.Value.Trim()), t.DayNumber - teblig.DayNumber));

        foreach (Match m in SureRegex().Matches(normal))
        {
            var sayi = SayiCoz(m.Groups["sayi"].Value) ?? SayiCoz(m.Groups["parantez"].Value);
            if (sayi is not > 0) continue;
            var birim = m.Groups["birim"].Value.ToLower(Tr);
            var isGunu = m.Groups["is"].Success;
            var ifade = (birim, isGunu) switch
            {
                ("gün", true) => new CevapSuresiIfadesi(CevapSuresiTuru.IsGunu, sayi, KaynakMetin: m.Value.Trim()),
                ("gün", false) => new CevapSuresiIfadesi(CevapSuresiTuru.TakvimGunu, sayi, KaynakMetin: m.Value.Trim()),
                ("hafta", _) => new CevapSuresiIfadesi(CevapSuresiTuru.TakvimGunu, sayi * 7, KaynakMetin: m.Value.Trim()),
                _ => new CevapSuresiIfadesi(CevapSuresiTuru.Ay, sayi, KaynakMetin: m.Value.Trim()),
            };
            var yaklasik = ifade.Tur switch
            {
                CevapSuresiTuru.IsGunu => (int)Math.Ceiling(sayi.Value * 7 / 5.0),
                CevapSuresiTuru.Ay => sayi.Value * 28,
                _ => ifade.Gun!.Value,
            };
            adaylar.Add((ifade, yaklasik));
        }

        var gecerli = adaylar.Where(a => a.YaklasikGun >= 0).ToList();
        if (gecerli.Count > 0)
            return gecerli.MinBy(a => a.YaklasikGun).Ifade;

        var ivedi = IvediRegex().Match(normal);
        return ivedi.Success
            ? new CevapSuresiIfadesi(CevapSuresiTuru.Bulunamadi, KaynakMetin: ivedi.Value)
            : CevapSuresiIfadesi.Bulunamadi;
    }

    public static int? SayiCoz(string? deger)
    {
        if (string.IsNullOrWhiteSpace(deger)) return null;
        deger = deger.Trim();
        if (int.TryParse(deger, out var n)) return n;
        var toplam = 0;
        foreach (var kelime in deger.ToLower(Tr).Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!SayiKelimeleri.TryGetValue(kelime, out var v)) return null;
            toplam += v;
        }
        return toplam > 0 ? toplam : null;
    }

    public static string OzetCikar(string metin, int azamiUzunluk = 600)
    {
        var govde = GovdeyiAyikla(metin);
        var cumleler = CumleAyiriciRegex().Split(govde)
            .Select(c => c.Trim())
            .Where(c => c.Length > 15)
            .ToList();
        if (cumleler.Count == 0) return Kisalt(govde, azamiUzunluk);

        var secilen = new List<string> { cumleler[0] };
        secilen.AddRange(cumleler.Skip(1).Where(c => TalepRegex().IsMatch(c)));
        var sb = new StringBuilder();
        foreach (var c in secilen.Distinct())
        {
            if (sb.Length + c.Length > azamiUzunluk && sb.Length > 0) break;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(c);
        }
        return Kisalt(sb.ToString(), azamiUzunluk);
    }

    private static string GovdeyiAyikla(string metin)
    {
        var satirlar = metin.Replace("\r", "").Split('\n').Select(s => s.Trim()).ToList();
        // Konu / ilgi satırlarından sonrasını al; başlık, hitap ve imza bloklarını at.
        var bas = satirlar.FindLastIndex(s => Regex.IsMatch(s, @"^(konu|[iİI]lgi(\s*\(?\w\)?)?)\s*:", RegexOptions.IgnoreCase));
        var govde = satirlar.Skip(bas + 1)
            .Where(s => s.Length > 0)
            .Where(s => !(s == s.ToUpper(Tr) && s.Length < 120)) // "... MÜDÜRLÜĞÜNE" gibi hitap/başlık satırları
            .Where(s => !Regex.IsMatch(s, @"^(ek|ekler|dağıtım|gereği|bilgi)\s*[:\-]", RegexOptions.IgnoreCase))
            .TakeWhile(s => !Regex.IsMatch(s, @"^(saygılarımla|arz ederim|e-imzalıdır|imza)", RegexOptions.IgnoreCase));
        return Regex.Replace(string.Join(" ", govde), @"\s+", " ").Trim();
    }

    private static string? KonuBul(string metin)
    {
        var m = KonuRegex().Match(metin);
        return m.Success ? m.Groups["v"].Value.Trim() : null;
    }

    private static (string? Sayi, DateOnly? Tarih) SayiBul(string metin)
    {
        var m = SayiRegex().Match(metin);
        if (!m.Success) return (null, null);
        var tarih = m.Groups["g"].Success ? TarihOlustur(m.Groups["g"].Value, m.Groups["a"].Value, m.Groups["y"].Value) : null;
        return (m.Groups["v"].Value.Trim().TrimEnd('-', ' '), tarih);
    }

    private static DateOnly? TarihSatiriBul(string metin)
    {
        var m = TarihSatiriRegex().Match(metin);
        return m.Success ? TarihOlustur(m.Groups["g"].Value, m.Groups["a"].Value, m.Groups["y"].Value) : null;
    }

    /// <summary>Antetteki büyük harfli satırlardan gönderen idareyi tahmin eder ("T.C." hariç).</summary>
    private static string? GonderenBul(string metin)
    {
        var satirlar = metin.Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var sayiIndex = satirlar.FindIndex(s => s.StartsWith("Sayı", true, Tr));
        var antet = satirlar.Take(sayiIndex > 0 ? sayiIndex : 4)
            .Where(s => s == s.ToUpper(Tr) && !Regex.IsMatch(s, @"^T\.?\s*C\.?$"))
            .ToList();
        return antet.Count > 0 ? string.Join(" - ", antet) : null;
    }

    private static string? IlkAnlamliSatir(string metin) =>
        metin.Replace("\r", "").Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 10) is { } s
            ? Kisalt(s, 150) : null;

    private static DateOnly? TarihOlustur(string g, string a, string y) =>
        int.TryParse(g, out var gun) && int.TryParse(a, out var ay) && int.TryParse(y, out var yil)
        && ay is >= 1 and <= 12 && gun >= 1 && gun <= DateTime.DaysInMonth(yil, ay)
            ? new DateOnly(yil, ay, gun) : null;

    private static string Normalize(string metin) =>
        Regex.Replace(metin.Replace("İ", "i").Replace("I", "ı").ToLower(Tr), @"\s+", " ");

    private static string Kisalt(string metin, int uzunluk) =>
        metin.Length <= uzunluk ? metin : metin[..(uzunluk - 1)].TrimEnd() + "…";
}
