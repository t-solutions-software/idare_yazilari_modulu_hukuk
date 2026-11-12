using Anthropic;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Analiz;
using IdareYazilari.Application.Services;
using IdareYazilari.Domain.Services;
using IdareYazilari.Infrastructure.ArkaPlan;
using IdareYazilari.Infrastructure.Bildirimler;
using IdareYazilari.Infrastructure.Kaynaklar;
using IdareYazilari.Infrastructure.Persistence;
using IdareYazilari.Infrastructure.YapayZeka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Infrastructure;

public class SistemSaati : ISaat
{
    public DateTimeOffset Simdi => DateTimeOffset.Now.ToOffset(SureAyarlari.TurkiyeSaatFarki);
}

public static class DependencyInjection
{
    public static IServiceCollection AddIdareYazilari(this IServiceCollection services, IConfiguration config, bool arkaPlanServisleri = true)
    {
        // --- Süre kuralları ve takvim
        var sureAyarlari = config.GetSection("Sureler").Get<SureAyarlari>() ?? new SureAyarlari();
        services.AddSingleton(sureAyarlari);
        var ekTatiller = config.GetSection("Sureler:EkTatiller").Get<DateOnly[]>() ?? [];
        services.AddSingleton<ITatilTakvimi>(new TurkiyeTatilTakvimi(ekTatiller));
        services.AddSingleton<SureHesaplayici>();
        services.AddSingleton<HatirlatmaPlanlayici>();
        services.AddSingleton<ISaat, SistemSaati>();

        // --- Veritabanı
        var saglayici = config["Veritabani:Saglayici"] ?? "Sqlite";
        var baglanti = config.GetConnectionString("IdareYazilari") ?? "Data Source=idare_yazilari.db";
        services.AddDbContext<IdareYazilariDbContext>(o =>
        {
            if (saglayici.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)) o.UseSqlServer(baglanti);
            else o.UseSqlite(baglanti);
        });
        services.AddScoped<IYaziDeposu, EfYaziDeposu>();
        services.AddScoped<IKullaniciDeposu, EfKullaniciDeposu>();
        services.AddSingleton<IDosyaDeposu>(new YerelDosyaDeposu(config["Dosyalar:Klasor"] ?? "veri/ekler"));

        // --- Analiz
        services.AddSingleton<KuralTabanliAnalizci>();
        services.AddScoped<YaziAnalizServisi>();
        services.Configure<YapayZekaAyarlari>(config.GetSection("YapayZeka"));
        var yz = config.GetSection("YapayZeka").Get<YapayZekaAyarlari>() ?? new YapayZekaAyarlari();
        if (yz.Etkin)
        {
            services.AddSingleton(_ => string.IsNullOrWhiteSpace(yz.ApiKey) ? new AnthropicClient() : new AnthropicClient { ApiKey = yz.ApiKey });
            services.AddSingleton<IYapayZekaAnalizcisi, ClaudeYaziAnalizcisi>();
        }

        // --- Uygulama servisleri
        services.AddScoped<YaziKayitServisi>();
        services.AddScoped<YaziIsAkisiServisi>();
        services.AddScoped<YaziSorguServisi>();
        services.AddScoped<HatirlatmaGonderimServisi>();

        // --- Bildirim kanalları
        services.Configure<SmtpAyarlari>(config.GetSection("Smtp"));
        services.AddSingleton<IBildirimGonderici, LogBildirimGonderici>();
        services.AddSingleton<IBildirimGonderici, SmtpBildirimGonderici>();

        // --- Gelen yazı kaynakları (UETS / KEP)
        foreach (var kaynak in config.GetSection("GelenYaziKaynaklari").Get<List<KaynakAyari>>() ?? [])
        {
            if (!kaynak.Etkin) continue;
            services.AddScoped<IGelenYaziKaynagi>(sp =>
                new KlasorGelenYaziKaynagi(kaynak, sp.GetRequiredService<ILoggerFactory>().CreateLogger($"Kaynak.{kaynak.Ad}")));
        }

        services.Configure<ArkaPlanAyarlari>(config.GetSection("ArkaPlan"));
        if (arkaPlanServisleri)
        {
            services.AddSingleton<GelenYaziToplamaServisi>();
            services.AddHostedService(sp => sp.GetRequiredService<GelenYaziToplamaServisi>());
            services.AddHostedService<HatirlatmaZamanlayici>();
        }
        return services;
    }
}
