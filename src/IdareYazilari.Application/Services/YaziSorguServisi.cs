using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Dtos;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using IdareYazilari.Domain.Services;

namespace IdareYazilari.Application.Services;

public class YaziSorguServisi(
    IYaziDeposu depo,
    IKullaniciDeposu kullanicilar,
    IMevcutKullanici mevcut,
    SureAyarlari ayarlar,
    ISaat saat)
{
    public static string DurumAdi(YaziDurumu d) => d switch
    {
        YaziDurumu.AtamaBekliyor => "Sorumlu ataması bekliyor",
        YaziDurumu.TaslakHazirlaniyor => "Taslak hazırlanıyor",
        YaziDurumu.HukukIncelemesinde => "Hukuk incelemesinde",
        YaziDurumu.RevizyonBekleniyor => "Revizyon bekleniyor",
        YaziDurumu.HukukOnayladi => "Hukuk onayladı – gönderim bekliyor",
        YaziDurumu.Gonderildi => "İdareye gönderildi",
        YaziDurumu.IstisnaHukukIncelemesinde => "Süre istisnası – hukuk incelemesinde",
        YaziDurumu.EkSureYanitiBekleniyor => "Ek süre talebi – idare yanıtı bekleniyor",
        _ => d.ToString(),
    };

    public async Task<IReadOnlyList<YaziOzetDto>> ListeleAsync(YaziFiltresi filtre, CancellationToken ct = default)
    {
        var k = await mevcut.GetirAsync(ct);
        filtre = k.Rol switch
        {
            KullaniciRolu.ProjeMuduru => filtre with { ProjeId = k.ProjeId ?? Guid.Empty },
            KullaniciRolu.Personel => filtre with { SorumluId = k.Id },
            _ => filtre,
        };
        var yazilar = await depo.ListeleAsync(filtre, ct);
        var sozluk = await SozlukAsync(ct);
        return yazilar
            .Select(y => OzetOlustur(y, sozluk))
            .OrderByDescending(o => o.Aciliyet == Aciliyet.Kapandi ? -1 : (int)o.Aciliyet)
            .ThenBy(o => o.AktifAsamaSonTarihi)
            .ToList();
    }

    public async Task<YaziDetayDto> DetayAsync(Guid id, CancellationToken ct = default)
    {
        var k = await mevcut.GetirAsync(ct);
        var y = await depo.GetirAsync(id, ct);
        if (y is null || !Yetki.GorebilirMi(k, y)) throw new BulunamadiHatasi("Yazı bulunamadı.");
        return DetayOlustur(y, k, await SozlukAsync(ct));
    }

    public async Task<YaziDetayDto> DetayOlusturAsync(IdareYazisi y, CancellationToken ct = default) =>
        DetayOlustur(y, await mevcut.GetirAsync(ct), await SozlukAsync(ct));

    /// <summary>Son günlerde kullanıcıya gönderilmiş hatırlatmalar (ekrandaki bildirim kutusu).</summary>
    public async Task<IReadOnlyList<BildirimDto>> BildirimlerimAsync(int gun, CancellationToken ct = default)
    {
        var k = await mevcut.GetirAsync(ct);
        var projeler = (await kullanicilar.ProjeleriListeleAsync(ct)).ToDictionary(p => p.Id);
        var liste = await depo.GonderilenHatirlatmalarAsync(saat.Simdi.AddDays(-gun), ct);
        return liste
            .Where(x => x.Hatirlatma.AliciTipi switch
            {
                AliciTipi.Kullanici => x.Hatirlatma.AliciKullaniciId == k.Id,
                AliciTipi.HukukEkibi => k.Rol == KullaniciRolu.Hukuk,
                AliciTipi.ProjeMuduru => x.Yazi.ProjeId is { } pid && projeler.TryGetValue(pid, out var p) && p.ProjeMuduruId == k.Id,
                _ => false,
            })
            .OrderByDescending(x => x.Hatirlatma.GonderimZamani)
            .Select(x => new BildirimDto(x.Yazi.Id, x.Yazi.Konu, x.Hatirlatma.Tur, x.Hatirlatma.Mesaj, x.Hatirlatma.GonderimZamani!.Value))
            .ToList();
    }

    private sealed record Sozluk(Dictionary<Guid, Kullanici> Kullanicilar, Dictionary<Guid, Proje> Projeler);

    private async Task<Sozluk> SozlukAsync(CancellationToken ct) => new(
        (await kullanicilar.ListeleAsync(ct: ct)).ToDictionary(u => u.Id),
        (await kullanicilar.ProjeleriListeleAsync(ct)).ToDictionary(p => p.Id));

    private YaziOzetDto OzetOlustur(IdareYazisi y, Sozluk s)
    {
        var simdi = saat.Simdi;
        var bugun = SureHesaplayici.TurkiyeTarihi(simdi);
        var cevapSonAni = SureHesaplayici.GunSonu(y.CevapSonGunu);

        var (asama, asamaSon) = y.Durum switch
        {
            YaziDurumu.AtamaBekliyor or YaziDurumu.TaslakHazirlaniyor => ("Taslak", y.TaslakSonTarihi),
            YaziDurumu.HukukIncelemesinde or YaziDurumu.RevizyonBekleniyor => ("Hukuk", y.HukukSonTarihi),
            YaziDurumu.Gonderildi => ("Tamamlandı", y.GonderimTarihi ?? cevapSonAni),
            _ => ("Cevap", cevapSonAni),
        };

        var aciliyet = y.Kapali ? Aciliyet.Kapandi
            : simdi > cevapSonAni ? Aciliyet.Gecikti
            : simdi > asamaSon || y.CevapSonGunu.DayNumber - bugun.DayNumber <= 1 ? Aciliyet.Kritik
            : asamaSon - simdi <= TimeSpan.FromHours(ayarlar.OnHatirlatmaSaat) || y.SureTeyitGerekli ? Aciliyet.Uyari
            : Aciliyet.Normal;

        return new YaziOzetDto(
            y.Id, y.Kaynak, y.ProjeId,
            y.ProjeId is { } pid && s.Projeler.TryGetValue(pid, out var p) ? p.Ad : null,
            y.GonderenIdare, y.GelenSayi, y.Konu, y.Ozet, y.TebligTarihi,
            y.CevapSonGunu, y.IlkCevapSonGunu, y.TaslakSonTarihi, y.HukukSonTarihi,
            y.CevapSonGunu.DayNumber - bugun.DayNumber,
            y.Durum, DurumAdi(y.Durum), asama, asamaSon, aciliyet,
            y.SorumluId, Ad(y.SorumluId, s),
            y.SureTeyitGerekli, y.SureUzatildi, y.SurumNo);
    }

    private YaziDetayDto DetayOlustur(IdareYazisi y, Kullanici k, Sozluk s) => new(
        OzetOlustur(y, s),
        y.Metin, y.YaziTarihi, y.AnalizKaynagi, y.SureAciklamasi,
        y.HukukOnayTarihi, y.GonderimTarihi, y.GidenSayi, y.GonderimKanali,
        y.Taslaklar.OrderByDescending(t => t.VersiyonNo)
            .Select(t => new TaslakDto(t.VersiyonNo, t.Icerik, Ad(t.HazirlayanId, s), t.HazirlayanRol, t.Tarih)).ToList(),
        y.HukukIncelemeleri.OrderByDescending(i => i.Tarih)
            .Select(i => new IncelemeDto(i.TaslakVersiyonNo, i.Karar, i.Aciklama, Ad(i.HukukcuId, s), i.Tarih)).ToList(),
        y.Istisnalar.OrderByDescending(i => i.BildirimTarihi)
            .Select(i => new IstisnaDto(i.Id, i.Neden, i.OnerilenTarih, Ad(i.BildirenId, s), i.BildirimTarihi, i.Durum, i.Cozum,
                Ad(i.HukukcuId, s), i.KararTarihi, i.HukukAciklamasi, i.IdareyeYaziMetni, i.IdareyeGidenSayi, i.HedefTarih,
                i.IdareYaniti, i.IdareninVerdigiTarih)).ToList(),
        y.Hareketler.OrderByDescending(h => h.Tarih)
            .Select(h => new HareketDto(h.Tarih, Ad(h.KullaniciId, s), h.Islem, h.Aciklama)).ToList(),
        y.Hatirlatmalar.Where(h => !h.Iptal).OrderBy(h => h.Zaman)
            .Select(h => new HatirlatmaDto(h.Tur, h.Zaman, h.AliciTipi,
                h.AliciTipi switch
                {
                    AliciTipi.Kullanici => Ad(h.AliciKullaniciId, s),
                    AliciTipi.HukukEkibi => "Hukuk ekibi",
                    _ => "Proje müdürü",
                }, h.Mesaj, h.GonderimZamani, h.Iptal)).ToList(),
        y.Ekler.Select(e => new EkDto(e.Id, e.DosyaAdi, e.IcerikTuru, e.Boyut)).ToList(),
        Yetki.YapilabilirIslemler(k, y));

    private static string? Ad(Guid? id, Sozluk s) =>
        id is { } g && s.Kullanicilar.TryGetValue(g, out var u) ? u.AdSoyad : null;
}
