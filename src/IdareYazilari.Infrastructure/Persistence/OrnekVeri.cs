using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Services;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IdareYazilari.Infrastructure.Persistence;

/// <summary>Geliştirme / demo ortamı için örnek proje, kullanıcı ve yazılar.</summary>
public static class OrnekVeri
{
    public static readonly Guid Proje1 = Guid.Parse("11111111-0000-0000-0000-000000000001");
    public static readonly Guid Proje2 = Guid.Parse("11111111-0000-0000-0000-000000000002");
    public static readonly Guid Pm1 = Guid.Parse("22222222-0000-0000-0000-000000000001");
    public static readonly Guid Personel1 = Guid.Parse("22222222-0000-0000-0000-000000000002");
    public static readonly Guid Personel2 = Guid.Parse("22222222-0000-0000-0000-000000000003");
    public static readonly Guid Pm2 = Guid.Parse("22222222-0000-0000-0000-000000000004");
    public static readonly Guid Hukuk1 = Guid.Parse("22222222-0000-0000-0000-000000000005");
    public static readonly Guid Hukuk2 = Guid.Parse("22222222-0000-0000-0000-000000000006");
    public static readonly Guid Yonetici = Guid.Parse("22222222-0000-0000-0000-000000000007");

    public static async Task YukleAsync(IdareYazilariDbContext db, YaziKayitServisi kayit, ISaat saat, CancellationToken ct = default)
    {
        if (await db.Projeler.AnyAsync(ct)) return;

        db.Projeler.AddRange(
            new Proje { Id = Proje1, Kod = "SH-01", Ad = "Örnek Şehir Hastanesi KÖİ Projesi", ProjeMuduruId = Pm1, KepAdresi = "sehirhastanesi@hs01.kep.tr" },
            new Proje { Id = Proje2, Kod = "OT-01", Ad = "Örnek Otoyol KÖİ Projesi", ProjeMuduruId = Pm2, KepAdresi = "otoyol@hs01.kep.tr" });
        db.Kullanicilar.AddRange(
            new Kullanici { Id = Pm1, AdSoyad = "Ayşe Proje (PM - Şehir Hastanesi)", Eposta = "pm1@example.com", Rol = KullaniciRolu.ProjeMuduru, ProjeId = Proje1 },
            new Kullanici { Id = Personel1, AdSoyad = "Mehmet Teknik (Şehir Hastanesi)", Eposta = "personel1@example.com", Rol = KullaniciRolu.Personel, ProjeId = Proje1 },
            new Kullanici { Id = Personel2, AdSoyad = "Zeynep İşletme (Şehir Hastanesi)", Eposta = "personel2@example.com", Rol = KullaniciRolu.Personel, ProjeId = Proje1 },
            new Kullanici { Id = Pm2, AdSoyad = "Can Yol (PM - Otoyol)", Eposta = "pm2@example.com", Rol = KullaniciRolu.ProjeMuduru, ProjeId = Proje2 },
            new Kullanici { Id = Hukuk1, AdSoyad = "Av. Elif Hukuk", Eposta = "hukuk1@example.com", Rol = KullaniciRolu.Hukuk },
            new Kullanici { Id = Hukuk2, AdSoyad = "Av. Burak Hukuk", Eposta = "hukuk2@example.com", Rol = KullaniciRolu.Hukuk },
            new Kullanici { Id = Yonetici, AdSoyad = "Sistem Yöneticisi", Eposta = "yonetici@example.com", Rol = KullaniciRolu.Yonetici });
        await db.SaveChangesAsync(ct);

        var simdi = saat.Simdi;
        await kayit.KaydetAsync(new GelenYaziPaketi
        {
            Kaynak = YaziKaynagi.Kep,
            DisReferans = "ornek-kep-1",
            ProjeKodu = "SH-01",
            TebligTarihi = simdi.AddDays(-2),
            Metin = $"""
                T.C.
                SAĞLIK BAKANLIĞI
                Kamu Özel İşbirliği Dairesi Başkanlığı

                Sayı : E-12345678-000-{simdi:yyyy}/1001
                Konu : Tıbbi cihaz bakım-onarım hizmetlerindeki aksaklıklar hk.

                ÖRNEK ŞEHİR HASTANESİ PROJE ŞİRKETİNE

                İlgi : {simdi.AddDays(-20):dd.MM.yyyy} tarihli hastane yönetimi yazısı.
                İlgi yazı ile hastanemizde bulunan görüntüleme cihazlarının periyodik bakımlarının sözleşmede öngörülen sürelerde yapılmadığı bildirilmiştir.
                Söz konusu aksaklıkların nedenleri, alınan önlemler ve bakım takviminin yazımızın tebliğinden itibaren 15 (on beş) gün içinde İdaremize bildirilmesi gerekmektedir.
                Aksi halde sözleşmenin ilgili maddeleri uyarınca performans kesintisi uygulanacağı hususunda bilgilerinizi rica ederim.

                Daire Başkanı
                """,
        }, ct);

        await kayit.KaydetAsync(new GelenYaziPaketi
        {
            Kaynak = YaziKaynagi.Uets,
            DisReferans = "ornek-uets-1",
            ProjeKodu = "SH-01",
            TebligTarihi = simdi.AddDays(-1),
            Metin = $"""
                T.C.
                ÖRNEK İL SAĞLIK MÜDÜRLÜĞÜ

                Sayı : E-98765432-010-{simdi:yyyy}/555
                Konu : Otopark işletmesine ilişkin şikâyetler

                Hastane otoparkı ile ilgili olarak vatandaşlardan gelen şikâyetlerin incelenerek, yapılacak iyileştirmelere ilişkin
                açıklamanın en geç {simdi.AddDays(9):dd.MM.yyyy} tarihine kadar Müdürlüğümüze iletilmesini rica ederim.
                """,
        }, ct);

        await kayit.KaydetAsync(new GelenYaziPaketi
        {
            Kaynak = YaziKaynagi.Dogrudan,
            DisReferans = "ornek-elden-1",
            ProjeKodu = "OT-01",
            TebligTarihi = simdi,
            GonderenIdare = "Karayolları Bölge Müdürlüğü",
            Metin = """
                Konu : Bakım-onarım raporu

                Otoyolun 2. kesiminde yapılan üstyapı bakım çalışmalarına ait raporun ivedilikle Bölge Müdürlüğümüze gönderilmesini rica ederim.
                """,
        }, ct);
    }
}
