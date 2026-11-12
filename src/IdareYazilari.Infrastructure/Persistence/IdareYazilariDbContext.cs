using IdareYazilari.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace IdareYazilari.Infrastructure.Persistence;

public class IdareYazilariDbContext(DbContextOptions<IdareYazilariDbContext> options) : DbContext(options)
{
    public DbSet<IdareYazisi> Yazilar => Set<IdareYazisi>();
    public DbSet<Hatirlatma> Hatirlatmalar => Set<Hatirlatma>();
    public DbSet<Kullanici> Kullanicilar => Set<Kullanici>();
    public DbSet<Proje> Projeler => Set<Proje>();
    public DbSet<Birim> Birimler => Set<Birim>();
    public DbSet<YaziGorevi> Gorevler => Set<YaziGorevi>();
    public DbSet<EvrakSayaci> EvrakSayaclari => Set<EvrakSayaci>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite DateTimeOffset karşılaştırma/sıralamayı desteklemez; sıralanabilir ikili biçimde saklanır.
        if (Database.IsSqlite())
        {
            builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
            builder.Properties<DateTimeOffset?>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Proje>(e =>
        {
            e.ToTable("Projeler");
            e.HasIndex(x => x.Kod).IsUnique();
            e.Property(x => x.Kod).HasMaxLength(50);
            e.Property(x => x.Ad).HasMaxLength(300);
            e.Property(x => x.KepAdresi).HasMaxLength(200);
            e.Property(x => x.UetsAdresi).HasMaxLength(200);
        });

        b.Entity<Kullanici>(e =>
        {
            e.ToTable("Kullanicilar");
            e.Property(x => x.AdSoyad).HasMaxLength(200);
            e.Property(x => x.Eposta).HasMaxLength(200);
            e.HasIndex(x => x.Eposta).IsUnique();
            e.Property(x => x.EntraObjectId).HasMaxLength(64);
            e.HasIndex(x => x.EntraObjectId);
        });

        b.Entity<Birim>(e =>
        {
            e.ToTable("Birimler");
            e.Property(x => x.Kod).HasMaxLength(50);
            e.Property(x => x.Ad).HasMaxLength(200);
            e.HasIndex(x => new { x.ProjeId, x.Kod }).IsUnique();
        });

        b.Entity<EvrakSayaci>(e =>
        {
            e.ToTable("EvrakSayaclari");
            e.HasKey(x => x.Anahtar);
            e.Property(x => x.Anahtar).HasMaxLength(100);
            e.Property(x => x.SonNo).IsConcurrencyToken();
        });

        b.Entity<IdareYazisi>(e =>
        {
            e.ToTable("IdareYazilari");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.SurumNo).IsConcurrencyToken();
            e.Property(x => x.GonderenIdare).HasMaxLength(500);
            e.Property(x => x.GelenSayi).HasMaxLength(200);
            e.Property(x => x.DisReferans).HasMaxLength(300);
            e.Property(x => x.Konu).HasMaxLength(1000);
            e.Property(x => x.Ozet).HasMaxLength(4000);
            e.Property(x => x.SureAciklamasi).HasMaxLength(1000);
            e.Property(x => x.GidenSayi).HasMaxLength(200);
            e.Property(x => x.GonderimKanali).HasMaxLength(100);
            e.Property(x => x.EvrakNo).HasMaxLength(50);
            e.HasIndex(x => x.EvrakNo).IsUnique();
            e.Property(x => x.Lokasyon).HasMaxLength(200);
            e.Property(x => x.SozlesmeReferansi).HasMaxLength(100);
            e.HasIndex(x => x.SozlesmeReferansi);
            e.Property(x => x.GonderenAdres).HasMaxLength(200);
            e.HasIndex(x => x.BirimId);
            e.HasIndex(x => new { x.Kaynak, x.DisReferans });
            e.HasIndex(x => new { x.ProjeId, x.Durum });
            e.HasIndex(x => x.SorumluId);
            e.Ignore(x => x.GuncelTaslak);
            e.Ignore(x => x.AcikIstisna);
            e.Ignore(x => x.Kapali);
            e.Ignore(x => x.BekleyenCevap);
            e.HasMany(x => x.GidenYazilar).WithOne().HasForeignKey(x => x.YaziId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Gorevler).WithOne().HasForeignKey(x => x.YaziId).OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Taslaklar).WithOne().HasForeignKey(x => x.YaziId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.HukukIncelemeleri).WithOne().HasForeignKey(x => x.YaziId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Istisnalar).WithOne().HasForeignKey(x => x.YaziId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Hatirlatmalar).WithOne().HasForeignKey(x => x.YaziId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Hareketler).WithOne().HasForeignKey(x => x.YaziId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Ekler).WithOne().HasForeignKey(x => x.YaziId).OnDelete(DeleteBehavior.Cascade);
            foreach (var nav in new[] { "Taslaklar", "HukukIncelemeleri", "Istisnalar", "Hatirlatmalar", "Hareketler", "Ekler", "GidenYazilar", "Gorevler" })
                e.Navigation(nav).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        // Alt kayıtların anahtarları uygulamada üretilir; EF'nin yeni kayıtları "Added" olarak izlemesi için gerekli.
        b.Entity<Taslak>(e => { e.ToTable("Taslaklar"); e.Property(x => x.Id).ValueGeneratedNever(); });
        b.Entity<HukukIncelemesi>(e => { e.ToTable("HukukIncelemeleri"); e.Property(x => x.Id).ValueGeneratedNever(); });
        b.Entity<Istisna>(e => { e.ToTable("Istisnalar"); e.Property(x => x.Id).ValueGeneratedNever(); });
        b.Entity<YaziHareketi>(e => { e.ToTable("YaziHareketleri"); e.Property(x => x.Id).ValueGeneratedNever(); });
        b.Entity<Ek>(e => { e.ToTable("Ekler"); e.Property(x => x.Id).ValueGeneratedNever(); });
        b.Entity<GidenYazi>(e =>
        {
            e.ToTable("GidenYazilar");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EvrakNo).HasMaxLength(50);
            e.Property(x => x.Sayi).HasMaxLength(100);
            e.HasIndex(x => x.EvrakNo).IsUnique();
            e.HasIndex(x => x.Sayi);
        });
        b.Entity<YaziGorevi>(e =>
        {
            e.ToTable("YaziGorevleri");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.DisAnahtar).HasMaxLength(80);
            e.HasIndex(x => x.DisAnahtar).IsUnique();
            e.HasIndex(x => x.AktarimDurumu);
        });
        b.Entity<Hatirlatma>(e =>
        {
            e.ToTable("Hatirlatmalar");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Ignore(x => x.Bekliyor);
            e.HasIndex(x => new { x.GonderimZamani, x.Iptal, x.Zaman });
        });
    }
}

/// <summary>Yıllık sıralı evrak / sayı sayacı.</summary>
public class EvrakSayaci
{
    public string Anahtar { get; set; } = "";
    public int SonNo { get; set; }
}
