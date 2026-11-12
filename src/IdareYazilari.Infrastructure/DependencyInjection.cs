using Anthropic;
using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Analiz;
using IdareYazilari.Application.Services;
using IdareYazilari.Domain.Services;
using IdareYazilari.Infrastructure.ArkaPlan;
using IdareYazilari.Infrastructure.Belgeler;
using IdareYazilari.Infrastructure.Bildirimler;
using IdareYazilari.Infrastructure.Entegrasyon;
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
        services.AddSingleton(config.GetSection("OnayAkisi").Get<OnayAkisiAyarlari>() ?? new OnayAkisiAyarlari());
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
        services.Configure<NumaralandirmaAyarlari>(config.GetSection("Numaralandirma"));
        services.AddSingleton<IEvrakNumaratoru, EfEvrakNumaratoru>();

        // --- Doküman deposu: Yerel klasör veya SharePoint (Microsoft Graph)
        if ((config["Dosyalar:Saglayici"] ?? "Yerel").Equals("SharePoint", StringComparison.OrdinalIgnoreCase))
        {
            services.Configure<SharePointAyarlari>(config.GetSection("Dosyalar:SharePoint"));
            services.AddHttpClient<SharePointDosyaDeposu>();
            services.AddSingleton<IDosyaDeposu>(sp => sp.GetRequiredService<SharePointDosyaDeposu>());
        }
        else
        {
            services.AddSingleton<IDosyaDeposu>(new YerelDosyaDeposu(config["Dosyalar:Klasor"] ?? "veri/ekler"));
        }

        // --- Word şablonu, gönderim kancaları, operasyon entegrasyonu
        services.Configure<BelgeAyarlari>(config.GetSection("Belge"));
        services.AddSingleton<IBelgeOlusturucu, WordBelgeOlusturucu>();
        services.Configure<KepGonderimAyarlari>(config.GetSection("KepGonderim"));
        if (config.GetValue("KepGonderim:Etkin", false))
            services.AddSingleton<IGidenYaziGonderici, KepGidenKutusuGonderici>();
        services.Configure<OperasyonAyarlari>(config.GetSection("Operasyon"));
        services.AddHttpClient<IGorevAktarici, OperasyonGorevAktarici>();

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
        services.AddScoped<YetkiServisi>();
        services.AddScoped<BelgeServisi>();
        services.AddScoped<GorevAktarimServisi>();

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
            services.AddHostedService<GorevAktarimZamanlayici>();
        }
        return services;
    }
}
