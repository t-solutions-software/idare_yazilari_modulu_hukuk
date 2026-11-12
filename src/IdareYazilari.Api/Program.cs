using System.Text.Json.Serialization;
using IdareYazilari.Api.Altyapi;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Services;
using IdareYazilari.Infrastructure;
using IdareYazilari.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdareYazilari(builder.Configuration,
    arkaPlanServisleri: builder.Configuration.GetValue("ArkaPlan:Etkin", true));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IMevcutKullanici, HttpMevcutKullanici>();
builder.Services.AddAuthentication(GelistirmeKimlikDogrulama.Sema)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, GelistirmeKimlikDogrulama>(GelistirmeKimlikDogrulama.Sema, null);
builder.Services.AddAuthorization();

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<HataYonetimi>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "İdare Yazıları Modülü", Version = "v1" }));

var app = builder.Build();

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

YaziUclari.Esle(app);

// Veritabanını oluştur (geliştirme). Canlıda EF Core migration kullanılmalıdır.
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
