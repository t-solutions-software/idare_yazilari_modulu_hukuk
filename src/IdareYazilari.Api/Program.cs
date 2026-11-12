using System.Text.Json.Serialization;
using IdareYazilari.Api.Altyapi;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Services;
using IdareYazilari.Infrastructure;
using IdareYazilari.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdareYazilari(builder.Configuration,
    arkaPlanServisleri: builder.Configuration.GetValue("ArkaPlan:Etkin", true));

// ---------- kimlik doğrulama ----------
// Auth:Mode = "EntraId" (kurum hesabıyla giriş, hukuk modülüyle aynı kurulum) veya "Dev" (yalnız geliştirme).
var authMode = builder.Configuration["Auth:Mode"] ?? "EntraId";
var gelistirme = authMode.Equals("Dev", StringComparison.OrdinalIgnoreCase);
AuthenticationBuilder auth;
if (gelistirme)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Auth:Mode=Dev yalnız Development ortamında kullanılabilir.");
    auth = builder.Services.AddAuthentication(GelistirmeKimlikDogrulama.Sema)
        .AddScheme<AuthenticationSchemeOptions, GelistirmeKimlikDogrulama>(GelistirmeKimlikDogrulama.Sema, null);
}
else
{
    auth = builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme);
    auth.AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
    // API çağrılarında oturum düşmüşse Microsoft'a yönlendirmek yerine 401 dön; ön yüz sayfayı yeniler.
    builder.Services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, o =>
    {
        var onceki = o.Events.OnRedirectToIdentityProvider;
        o.Events.OnRedirectToIdentityProvider = async ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = 401;
                ctx.HandleResponse();
                return;
            }
            if (onceki is not null) await onceki(ctx);
        };
    });
}
builder.Services.Configure<EntegrasyonAyarlari>(builder.Configuration.GetSection("Entegrasyon"));
auth.AddScheme<AuthenticationSchemeOptions, ApiAnahtariKimlikDogrulama>(ApiAnahtariKimlikDogrulama.Sema, null);

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Entegrasyon", p => p.AddAuthenticationSchemes(ApiAnahtariKimlikDogrulama.Sema).RequireAuthenticatedUser());
    if (!gelistirme)
        o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IMevcutKullanici, HttpMevcutKullanici>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<HataYonetimi>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "İdare Yazıları Modülü", Version = "v1" }));

var app = builder.Build();

if (app.Configuration.GetValue("App:BehindProxy", false))
    app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseExceptionHandler();

// Çerezle kimlik doğrulanan API'lerde CSRF koruması: değiştiren istekler özel başlık taşımalı (hukuk modülüyle aynı yaklaşım).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/entegrasyon")
        && !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)
        && ctx.Request.Headers["X-Idare"] != "1")
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsync("X-Idare başlığı eksik.");
        return;
    }
    await next();
});

if (gelistirme)
{
    // Geliştirmede ekran oturumsuz açılır; kullanıcı ekrandaki seçiciden belirlenir.
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseAuthentication();
app.UseAuthorization();
if (!gelistirme)
{
    // Statik dosyalar yetkilendirmeden sonra: ekran da Entra ID girişi ister.
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

YaziUclari.Esle(app, authMode);

// Veritabanını oluştur (geliştirme). Canlıda EF Core migration / SQL betiği kullanılmalıdır.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdareYazilariDbContext>();
    await db.Database.EnsureCreatedAsync();
    if (app.Configuration.GetValue("OrnekVeri", false))
        await OrnekVeri.YukleAsync(db,
            scope.ServiceProvider.GetRequiredService<YaziKayitServisi>(),
            scope.ServiceProvider.GetRequiredService<ISaat>());
}

app.Run();

public partial class Program;
