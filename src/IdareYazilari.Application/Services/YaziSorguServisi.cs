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
    YetkiServisi yetki,
    SureAyarlari ayarlar,
    ISaat saat)
{
    public static string DurumAdi(YaziDurumu d) => d switch
    {
        YaziDurumu.AtamaBekliyor => "Sorumlu ataması bekliyor",
        YaziDurumu.TaslakHazirlaniyor => "Taslak hazırlanıyor",
        YaziDurumu.HukukIncelemesinde => "Hukuk incelemesinde",
        YaziDurumu.RevizyonBekleniyor => "Revizyon bekleniyor",
        YaziDurumu.BirimOnayinda => "Birim onayında",
        YaziDurumu.ImzaBekliyor => "Hukuk onayladı – imza bekliyor",
        YaziDurumu.GonderimeHazir => "Gönderime hazır",
        YaziDurumu.Gonderildi => "İdareye gönderildi",
        YaziDurumu.IstisnaHukukIncelemesinde => "Süre istisnası – hukuk incelemesinde",
        YaziDurumu.EkSureYanitiBekleniyor => "Ek süre talebi – idare yanıtı bekleniyor",
        _ => d.ToString(),
    };

    public async Task<IReadOnlyList<YaziOzetDto>> ListeleAsync(YaziFiltresi filtre, CancellationToken ct = default)
    {
        var b = await yetki.BaglamAsync(ct);
        filtre = filtre with { Kapsam = Yetki.Kapsam(b) };
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
        var b = await yetki.BaglamAsync(ct);
        var y = await depo.GetirAsync(id, ct);
        if (y is null || !Yetki.GorebilirMi(b, y)) throw new BulunamadiHatasi("Yazı bulunamadı.");
        return DetayOlustur(y, b, await SozlukAsync(ct));
    }

    public async Task<YaziDetayDto> DetayOlusturAsync(IdareYazisi y, CancellationToken ct = default) =>
        DetayOlustur(y, await yetki.BaglamAsync(ct), await SozlukAsync(ct));

    /// <summary>Hukuk / sözleşme modülü kartı için: bir sözleşme referansına bağlı yazılar (entegrasyon, kullanıcı kapsamı yok).</summary>
    public async Task<IReadOnlyList<SozlesmeYaziDto>> SozlesmeYazilariAsync(string referans, string uygulamaAdresi, CancellationToken ct = default)
    {
        var yazilar = await depo.ListeleAsync(new YaziFiltresi(SozlesmeReferansi: referans.Trim()), ct);
        return yazilar.OrderByDescending(y => y.TebligTarihi).Select(y => new SozlesmeYaziDto(
            y.Id, y.EvrakNo, y.Kaynak, y.GonderenIdare, y.GelenSayi, y.Konu, y.Ozet, y.TebligTarihi, y.CevapSonGunu,
            y.Durum, DurumAdi(y.Durum), y.GidenSayi, y.GonderimTarihi,
            $"{uygulamaAdresi.TrimEnd('/')}/#yazi={y.Id}")).ToList();
    }

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

    private sealed record Sozluk(Dictionary<Guid, Kullanici> Kullanicilar, Dictionary<Guid, Proje> Projeler, Dictionary<Guid, Birim> Birimler);

    private async Task<Sozluk> SozlukAsync(CancellationToken ct) => new(
        (await kullanicilar.ListeleAsync(ct: ct)).ToDictionary(u => u.Id),
        (await kullanicilar.ProjeleriListeleAsync(ct)).ToDictionary(p => p.Id),
        (await kullanicilar.BirimleriListeleAsync(ct: ct)).ToDictionary(b => b.Id));

    private YaziOzetDto OzetOlustur(IdareYazisi y, Sozluk s)
    {
        var simdi = saat.Simdi;
        var bugun = SureHesaplayici.TurkiyeTarihi(simdi);
        var cevapSonAni = SureHesaplayici.GunSonu(y.CevapSonGunu);

        var (asama, asamaSon) = y.Durum switch
        {
            YaziDurumu.AtamaBekliyor or YaziDurumu.TaslakHazirlaniyor => ("Taslak", y.TaslakSonTarihi),
            YaziDurumu.BirimOnayinda => ("Taslak", y.TaslakSonTarihi),
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
            y.Id, y.EvrakNo, y.Kaynak, y.ProjeId,
            y.ProjeId is { } pid && s.Projeler.TryGetValue(pid, out var p) ? p.Ad : null,
            y.GonderenIdare, y.GelenSayi, y.Konu, y.Ozet, y.TebligTarihi,
            y.CevapSonGunu, y.IlkCevapSonGunu, y.TaslakSonTarihi, y.HukukSonTarihi,
            y.CevapSonGunu.DayNumber - bugun.DayNumber,
            y.Durum, DurumAdi(y.Durum), asama, asamaSon, aciliyet,
            y.SorumluId, Ad(y.SorumluId, s),
            y.SureTeyitGerekli, y.SureUzatildi, y.SurumNo,
            y.BirimId, y.BirimId is { } bid && s.Birimler.TryGetValue(bid, out var birim) ? birim.Ad : null,
            y.Lokasyon, y.SozlesmeReferansi, y.GidenSayi);
    }

    private YaziDetayDto DetayOlustur(IdareYazisi y, KullaniciBaglami k, Sozluk s) => new(
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
                    AliciTipi.BirimYoneticisi => "Birim yöneticisi",
                    AliciTipi.ImzaYetkilileri => "İmza yetkilileri",
                    AliciTipi.UstYonetim => "Üst yönetim",
                    _ => "Proje müdürü",
                }, h.Mesaj, h.GonderimZamani, h.Iptal)).ToList(),
        y.Ekler.Select(e => new EkDto(e.Id, e.DosyaAdi, e.IcerikTuru, e.Boyut)).ToList(),
        Yetki.YapilabilirIslemler(k, y),
        y.GidenYazilar.OrderByDescending(g => g.Tarih).Select(g => new GidenYaziDto(g.Id, g.Tur, g.EvrakNo, g.Sayi, g.Tarih, g.Konu,
            Ad(g.ImzalayanId, s), g.ImzaTarihi, g.ImzaliEkId, g.Kanal, g.Durum, g.GonderimTarihi, g.DisMesajId, g.Hata)).ToList(),
        y.Gorevler.OrderByDescending(g => g.Tarih).Select(g => new GorevDto(g.Id, g.Baslik, g.Aciklama, g.SorumluEposta, g.Lokasyon,
            g.Termin, Ad(g.OlusturanId, s), g.Tarih, g.DisAnahtar, g.AktarimDurumu, g.AktarimTarihi, g.Hata)).ToList(),
        y.GonderenAdres, Ad(y.BirimOnaylayanId, s), y.BirimOnayTarihi, Ad(y.ImzalayanId, s), y.ImzaTarihi);

    private static string? Ad(Guid? id, Sozluk s) =>
        id is { } g && s.Kullanicilar.TryGetValue(g, out var u) ? u.AdSoyad : null;
}
