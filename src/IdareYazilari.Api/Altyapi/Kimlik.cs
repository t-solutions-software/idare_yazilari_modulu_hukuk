using System.Security.Claims;
using System.Text.Encodings.Web;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Exceptions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Api.Altyapi;

/// <summary>
/// GELİŞTİRME amaçlı kimlik doğrulama: "X-Kullanici-Id" başlığındaki kullanıcıyı oturum açmış kabul eder.
/// Yalnız Auth:Mode=Dev ve Development ortamında kullanılabilir; canlıda Entra ID kullanılır.
/// </summary>
public class GelistirmeKimlikDogrulama(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e)
    : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    public const string Sema = "Gelistirme";
    public const string Baslik = "X-Kullanici-Id";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Baslik, out var deger) || !Guid.TryParse(deger, out var id))
            return Task.FromResult(AuthenticateResult.NoResult());
        var kimlik = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], Sema);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(kimlik), Sema)));
    }
}

public class EntegrasyonAyarlari
{
    /// <summary>Diğer modüllerin (hukuk/sözleşme, operasyon) kullanacağı API anahtarları. Gizli değer; ortam değişkeniyle verin.</summary>
    public List<string> ApiAnahtarlari { get; set; } = [];
    /// <summary>Entegrasyon yanıtlarındaki bağlantılar için uygulamanın dış adresi.</summary>
    public string UygulamaAdresi { get; set; } = "https://localhost:5001";
}

/// <summary>Modüller arası entegrasyon uçları için "X-Api-Anahtari" başlığıyla kimlik doğrulama.</summary>
public class ApiAnahtariKimlikDogrulama(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e,
    IOptions<EntegrasyonAyarlari> ayar) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
{
    public const string Sema = "ApiAnahtari";
    public const string Baslik = "X-Api-Anahtari";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Baslik, out var deger) || string.IsNullOrEmpty(deger))
            return Task.FromResult(AuthenticateResult.NoResult());
        var gecerli = ayar.Value.ApiAnahtarlari.Any(a => !string.IsNullOrEmpty(a) &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(deger.ToString())));
        if (!gecerli) return Task.FromResult(AuthenticateResult.Fail("Geçersiz API anahtarı"));
        var kimlik = new ClaimsIdentity([new Claim(ClaimTypes.Name, "entegrasyon")], Sema);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(kimlik), Sema)));
    }
}

/// <summary>
/// Oturumdaki kişiyi modülün kullanıcı tablosuyla eşleştirir. Entra ID'de önce "oid", yoksa e-posta (UPN) ile;
/// e-postayla ilk eşleşmede oid kullanıcıya bağlanır. Roller bu modülde (Kullanicilar tablosu) yönetilir.
/// </summary>
public class HttpMevcutKullanici(IHttpContextAccessor http, IKullaniciDeposu depo) : IMevcutKullanici
{
    private const string ObjectIdTalebi = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private Kullanici? _onbellek;

    public async Task<Kullanici> GetirAsync(CancellationToken ct = default)
    {
        if (_onbellek is not null) return _onbellek;
        var p = http.HttpContext?.User ?? throw new YetkiHatasi("Oturum bulunamadı.");

        Kullanici? k = null;
        if (p.Identity?.AuthenticationType == GelistirmeKimlikDogrulama.Sema &&
            Guid.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            k = await depo.GetirAsync(id, ct);
        }
        else
        {
            var oid = p.FindFirstValue("oid") ?? p.FindFirstValue(ObjectIdTalebi);
            var eposta = p.FindFirstValue("preferred_username") ?? p.FindFirstValue(ClaimTypes.Email) ?? p.FindFirstValue(ClaimTypes.Upn);
            k = await depo.EntraIleBulAsync(oid, eposta, ct);
            if (k is not null && oid is not null && k.EntraObjectId != oid)
            {
                k.EntraObjectId = oid;
                await depo.KaydetAsync(k, yeni: false, ct);
            }
        }

        if (k is not { Aktif: true })
            throw new YetkiHatasi("Bu modülde tanımlı aktif bir kullanıcı kaydınız yok. Yöneticinize başvurun.");
        return _onbellek = k;
    }
}
