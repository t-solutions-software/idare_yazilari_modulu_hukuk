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
/// Canlı ortamda kurumun kimlik sağlayıcısı (Active Directory / Entra ID / JWT) ile değiştirilmelidir;
/// <see cref="HttpMevcutKullanici"/> e-posta talebinden de kullanıcı eşleştirebilir.
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

public class HttpMevcutKullanici(IHttpContextAccessor http, IKullaniciDeposu depo) : IMevcutKullanici
{
    private Kullanici? _onbellek;

    public async Task<Kullanici> GetirAsync(CancellationToken ct = default)
    {
        if (_onbellek is not null) return _onbellek;
        var principal = http.HttpContext?.User ?? throw new YetkiHatasi("Oturum bulunamadı.");

        Kullanici? k = null;
        if (Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            k = await depo.GetirAsync(id, ct);
        if (k is null && principal.FindFirstValue(ClaimTypes.Email) is { } eposta)
            k = (await depo.ListeleAsync(ct: ct)).FirstOrDefault(x => x.Eposta.Equals(eposta, StringComparison.OrdinalIgnoreCase));

        if (k is not { Aktif: true }) throw new YetkiHatasi("Kullanıcı tanımlı değil veya pasif.");
        return _onbellek = k;
    }
}
