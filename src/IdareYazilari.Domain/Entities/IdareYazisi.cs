using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using IdareYazilari.Domain.Services;

namespace IdareYazilari.Domain.Entities;

/// <summary>
/// İdare yazısı (aggregate root). Tüm durum geçişleri bu sınıfın metotları üzerinden yapılır;
/// böylece iş akışı kuralları tek yerde toplanır.
/// </summary>
public class IdareYazisi
{
    private readonly List<Taslak> _taslaklar = [];
    private readonly List<HukukIncelemesi> _hukukIncelemeleri = [];
    private readonly List<Istisna> _istisnalar = [];
    private readonly List<Hatirlatma> _hatirlatmalar = [];
    private readonly List<YaziHareketi> _hareketler = [];
    private readonly List<Ek> _ekler = [];

    private IdareYazisi() { } // EF Core

    public Guid Id { get; private set; } = Guid.NewGuid();

    // --- Geliş bilgileri ---
    public YaziKaynagi Kaynak { get; private set; }
    /// <summary>UETS barkodu / KEP ileti kimliği vb. Aynı yazının iki kez kaydedilmesini önler.</summary>
    public string? DisReferans { get; private set; }
    public Guid? ProjeId { get; private set; }
    public string GonderenIdare { get; private set; } = "";
    public string? GelenSayi { get; private set; }
    public DateOnly? YaziTarihi { get; private set; }
    public DateTimeOffset TebligTarihi { get; private set; }
    public DateTimeOffset OlusturmaTarihi { get; private set; }

    // --- Otomatik analiz sonucu ---
    public string Konu { get; private set; } = "";
    public string Metin { get; private set; } = "";
    public string Ozet { get; private set; } = "";
    public AnalizKaynagi AnalizKaynagi { get; private set; }
    public string? SureAciklamasi { get; private set; }
    /// <summary>Süre metinden kesin tespit edilemediyse (varsayılan/çelişkili) proje müdürünün teyidi beklenir.</summary>
    public bool SureTeyitGerekli { get; private set; }

    // --- Süreler ---
    public DateOnly IlkCevapSonGunu { get; private set; }
    public DateOnly CevapSonGunu { get; private set; }
    /// <summary>Ara tarih hesabının başlangıcı (tebliğ; ek süre/gecikme sonrası yeni pencerenin başlangıcı).</summary>
    public DateTimeOffset SurecBaslangici { get; private set; }
    public DateTimeOffset TaslakSonTarihi { get; private set; }
    public DateTimeOffset HukukSonTarihi { get; private set; }
    /// <summary>Ek süre alındı veya gecikme bildirildi; cevap son günü idareye taahhüt edilen tarihtir.</summary>
    public bool SureUzatildi { get; private set; }

    // --- Akış ---
    public YaziDurumu Durum { get; private set; }
    public DateTimeOffset DurumTarihi { get; private set; }
    public Guid? SorumluId { get; private set; }
    public Guid? AtayanId { get; private set; }
    public DateTimeOffset? AtamaTarihi { get; private set; }
    public DateTimeOffset? HukukaGonderimTarihi { get; private set; }
    public DateTimeOffset? HukukOnayTarihi { get; private set; }
    public Guid? OnaylayanHukukcuId { get; private set; }
    public DateTimeOffset? GonderimTarihi { get; private set; }
    public string? GidenSayi { get; private set; }
    public string? GonderimKanali { get; private set; }

    /// <summary>İyimser eşzamanlılık kontrolü için sürüm numarası.</summary>
    public int SurumNo { get; private set; }

    public IReadOnlyList<Taslak> Taslaklar => _taslaklar;
    public IReadOnlyList<HukukIncelemesi> HukukIncelemeleri => _hukukIncelemeleri;
    public IReadOnlyList<Istisna> Istisnalar => _istisnalar;
    public IReadOnlyList<Hatirlatma> Hatirlatmalar => _hatirlatmalar;
    public IReadOnlyList<YaziHareketi> Hareketler => _hareketler;
    public IReadOnlyList<Ek> Ekler => _ekler;

    public Taslak? GuncelTaslak => _taslaklar.MaxBy(t => t.VersiyonNo);
    public Istisna? AcikIstisna => _istisnalar.FirstOrDefault(i =>
        i.Durum == IstisnaDurumu.HukukIncelemesinde ||
        (i.Durum == IstisnaDurumu.EkSureTalepEdildi && i.IdareYaniti is null));

    public bool Kapali => Durum == YaziDurumu.Gonderildi;

    public static IdareYazisi Olustur(
        YaziKaynagi kaynak,
        string? disReferans,
        Guid? projeId,
        string gonderenIdare,
        string? gelenSayi,
        DateOnly? yaziTarihi,
        DateTimeOffset tebligTarihi,
        string konu,
        string metin,
        string ozet,
        AnalizKaynagi analizKaynagi,
        SonGunSonucu sonGun,
        SureHesaplayici hesaplayici,
        DateTimeOffset simdi)
    {
        if (string.IsNullOrWhiteSpace(metin) && string.IsNullOrWhiteSpace(konu))
            throw new IsKuraliHatasi("Yazının konusu veya metni boş olamaz.");

        var yazi = new IdareYazisi
        {
            Kaynak = kaynak,
            DisReferans = disReferans,
            ProjeId = projeId,
            GonderenIdare = gonderenIdare.Trim(),
            GelenSayi = gelenSayi?.Trim(),
            YaziTarihi = yaziTarihi,
            TebligTarihi = tebligTarihi,
            OlusturmaTarihi = simdi,
            Konu = konu.Trim(),
            Metin = metin,
            Ozet = ozet.Trim(),
            AnalizKaynagi = analizKaynagi,
            SureAciklamasi = sonGun.Aciklama,
            SureTeyitGerekli = sonGun.TeyitGerekli,
            IlkCevapSonGunu = sonGun.SonGun,
            CevapSonGunu = sonGun.SonGun,
            SurecBaslangici = tebligTarihi,
        };
        yazi.AraTarihleriYenile(hesaplayici);
        yazi.DurumDegistir(YaziDurumu.AtamaBekliyor, simdi);
        yazi.Hareket(null, simdi, "Yazı alındı",
            $"Kaynak: {kaynak}; cevap son günü {sonGun.SonGun:dd.MM.yyyy} ({sonGun.Aciklama})");
        return yazi;
    }

    // ------------------------------------------------------------------ Kayıt / düzeltme

    public void EkEkle(Ek ek)
    {
        ek.YaziId = Id;
        _ekler.Add(ek);
    }

    public void ProjeAta(Guid projeId, Guid kullaniciId, DateTimeOffset simdi)
    {
        KapaliIseHata();
        ProjeId = projeId;
        Hareket(kullaniciId, simdi, "Proje eşleştirildi", null);
    }

    /// <summary>Proje müdürü / hukuk otomatik çıkarılan bilgileri düzeltebilir veya teyit edebilir.</summary>
    public void BilgileriDuzelt(string? konu, string? ozet, DateOnly? cevapSonGunu, Guid kullaniciId,
        SureHesaplayici hesaplayici, DateTimeOffset simdi)
    {
        KapaliIseHata();
        var degisiklikler = new List<string>();
        if (!string.IsNullOrWhiteSpace(konu) && konu.Trim() != Konu) { Konu = konu.Trim(); degisiklikler.Add("konu"); }
        if (!string.IsNullOrWhiteSpace(ozet) && ozet.Trim() != Ozet) { Ozet = ozet.Trim(); degisiklikler.Add("özet"); }
        if (cevapSonGunu is { } yeni && yeni != CevapSonGunu)
        {
            if (SureUzatildi)
                throw new IsKuraliHatasi("Ek süre / gecikme bildirimi sonrası son gün yalnızca istisna akışıyla değiştirilebilir.");
            degisiklikler.Add($"cevap son günü {CevapSonGunu:dd.MM.yyyy} → {yeni:dd.MM.yyyy}");
            CevapSonGunu = yeni;
            IlkCevapSonGunu = yeni;
            SureAciklamasi = "Manuel olarak girildi";
            AraTarihleriYenile(hesaplayici);
        }
        SureTeyitGerekli = false;
        AnalizKaynagi = degisiklikler.Count > 0 ? AnalizKaynagi.Manuel : AnalizKaynagi;
        Hareket(kullaniciId, simdi, degisiklikler.Count > 0 ? "Bilgiler düzeltildi" : "Bilgiler teyit edildi",
            degisiklikler.Count > 0 ? string.Join(", ", degisiklikler) : null);
    }

    // ------------------------------------------------------------------ Ana akış

    public void SorumluAta(Guid sorumluId, Guid atayanId, DateTimeOffset simdi)
    {
        DurumKontrol("sorumlu atanamaz", YaziDurumu.AtamaBekliyor, YaziDurumu.TaslakHazirlaniyor, YaziDurumu.RevizyonBekleniyor);
        if (ProjeId is null)
            throw new IsKuraliHatasi("Yazı henüz bir projeyle eşleştirilmemiş.");

        var oncekiSorumlu = SorumluId;
        SorumluId = sorumluId;
        AtayanId = atayanId;
        AtamaTarihi = simdi;
        if (Durum == YaziDurumu.AtamaBekliyor)
            DurumDegistir(YaziDurumu.TaslakHazirlaniyor, simdi);
        Hareket(atayanId, simdi, oncekiSorumlu is null ? "Sorumlu atandı" : "Sorumlu değiştirildi", null);
    }

    public Taslak TaslakKaydet(string icerik, Guid kullaniciId, KullaniciRolu rol, DateTimeOffset simdi)
    {
        DurumKontrol("taslak kaydedilemez", YaziDurumu.TaslakHazirlaniyor, YaziDurumu.RevizyonBekleniyor);
        return YeniTaslak(icerik, kullaniciId, rol, simdi, "Taslak kaydedildi");
    }

    public void HukukaGonder(Guid kullaniciId, DateTimeOffset simdi)
    {
        DurumKontrol("hukuka gönderilemez", YaziDurumu.TaslakHazirlaniyor, YaziDurumu.RevizyonBekleniyor);
        if (GuncelTaslak is null || string.IsNullOrWhiteSpace(GuncelTaslak.Icerik))
            throw new IsKuraliHatasi("Hukuka göndermeden önce taslak kaydedilmelidir.");

        HukukaGonderimTarihi = simdi;
        var gecikme = simdi > TaslakSonTarihi ? " (taslak süresi aşılarak)" : "";
        DurumDegistir(YaziDurumu.HukukIncelemesinde, simdi);
        Hareket(kullaniciId, simdi, "Taslak hukuka gönderildi" + gecikme, $"Versiyon {GuncelTaslak.VersiyonNo}");
    }

    public void HukukOnayla(Guid hukukcuId, string? aciklama, DateTimeOffset simdi)
    {
        DurumKontrol("onaylanamaz", YaziDurumu.HukukIncelemesinde);
        Incele(HukukKarari.Onay, hukukcuId, aciklama, simdi);
        Onaylandi(hukukcuId, simdi);
        Hareket(hukukcuId, simdi, "Hukuk onayladı", aciklama);
    }

    public void RevizyonTalepEt(Guid hukukcuId, string aciklama, DateTimeOffset simdi)
    {
        DurumKontrol("revizyon talep edilemez", YaziDurumu.HukukIncelemesinde);
        if (string.IsNullOrWhiteSpace(aciklama))
            throw new IsKuraliHatasi("Revizyon talebinde açıklama zorunludur.");
        Incele(HukukKarari.RevizyonTalebi, hukukcuId, aciklama, simdi);
        DurumDegistir(YaziDurumu.RevizyonBekleniyor, simdi);
        Hareket(hukukcuId, simdi, "Hukuk revizyon talep etti", aciklama);
    }

    /// <summary>Hukuk taslağı kendisi revize eder ve onaylar.</summary>
    public void RevizeEtVeOnayla(Guid hukukcuId, string yeniIcerik, string? aciklama, DateTimeOffset simdi)
    {
        DurumKontrol("revize edilemez", YaziDurumu.HukukIncelemesinde);
        YeniTaslak(yeniIcerik, hukukcuId, KullaniciRolu.Hukuk, simdi, "Hukuk taslağı revize etti");
        Incele(HukukKarari.RevizeEdilerekOnay, hukukcuId, aciklama, simdi);
        Onaylandi(hukukcuId, simdi);
        Hareket(hukukcuId, simdi, "Hukuk revize ederek onayladı", aciklama);
    }

    public void IdareyeGonder(Guid kullaniciId, string? gidenSayi, string? kanal, DateTimeOffset simdi)
    {
        DurumKontrol("idareye gönderilemez (önce hukuk onayı gerekir)", YaziDurumu.HukukOnayladi);
        GonderimTarihi = simdi;
        GidenSayi = gidenSayi?.Trim();
        GonderimKanali = kanal?.Trim();
        var gecikme = SureHesaplayici.TurkiyeTarihi(simdi) > CevapSonGunu ? " (SÜRE AŞILARAK)" : "";
        DurumDegistir(YaziDurumu.Gonderildi, simdi);
        Hareket(kullaniciId, simdi, "Cevap idareye gönderildi" + gecikme,
            string.Join("; ", new[] { gidenSayi is null ? null : $"Sayı: {gidenSayi}", kanal is null ? null : $"Kanal: {kanal}" }.Where(x => x is not null)));
    }

    // ------------------------------------------------------------------ İstisnalar

    /// <summary>Sorumlu, cevap süresi içinde cevap verilemeyeceğini nedeniyle birlikte bildirir.</summary>
    public Istisna SuresindeCevapVerilemezBildir(string neden, DateOnly? onerilenTarih, Guid kullaniciId, DateTimeOffset simdi)
    {
        DurumKontrol("istisna bildirilemez", YaziDurumu.TaslakHazirlaniyor, YaziDurumu.RevizyonBekleniyor, YaziDurumu.HukukIncelemesinde);
        if (string.IsNullOrWhiteSpace(neden))
            throw new IsKuraliHatasi("Süresinde cevap verilememe nedeni zorunludur.");
        if (onerilenTarih is { } t && t <= CevapSonGunu)
            throw new IsKuraliHatasi("Önerilen tarih mevcut cevap son gününden sonra olmalıdır.");

        var istisna = new Istisna
        {
            YaziId = Id,
            Neden = neden.Trim(),
            OnerilenTarih = onerilenTarih,
            BildirenId = kullaniciId,
            BildirimTarihi = simdi,
            OncekiDurum = Durum,
        };
        _istisnalar.Add(istisna);
        DurumDegistir(YaziDurumu.IstisnaHukukIncelemesinde, simdi);
        Hareket(kullaniciId, simdi, "Süresinde cevap verilemeyeceği bildirildi", istisna.Neden);
        return istisna;
    }

    /// <summary>Hukuk nedeni uygun bulmaz; yazı önceki aşamasına döner, asıl süre geçerlidir.</summary>
    public void IstisnaReddet(Guid hukukcuId, string aciklama, DateTimeOffset simdi)
    {
        DurumKontrol("istisna reddedilemez", YaziDurumu.IstisnaHukukIncelemesinde);
        if (string.IsNullOrWhiteSpace(aciklama))
            throw new IsKuraliHatasi("Ret gerekçesi zorunludur.");
        var istisna = IncelemedekiIstisna();
        istisna.Durum = IstisnaDurumu.Reddedildi;
        istisna.HukukcuId = hukukcuId;
        istisna.KararTarihi = simdi;
        istisna.HukukAciklamasi = aciklama.Trim();
        DurumDegistir(istisna.OncekiDurum, simdi);
        Hareket(hukukcuId, simdi, "Hukuk istisna nedenini uygun bulmadı", aciklama);
    }

    /// <summary>Hukuk nedeni uygun bulur ve idareden ek süre talep eder. İdarenin yanıtı beklenir.</summary>
    public void EkSureTalepEt(Guid hukukcuId, string talepYazisiMetni, DateOnly talepEdilenTarih, string? gidenSayi,
        string? aciklama, DateTimeOffset simdi)
    {
        DurumKontrol("ek süre talep edilemez", YaziDurumu.IstisnaHukukIncelemesinde);
        if (string.IsNullOrWhiteSpace(talepYazisiMetni))
            throw new IsKuraliHatasi("Ek süre talep yazısı metni zorunludur.");
        if (talepEdilenTarih <= CevapSonGunu)
            throw new IsKuraliHatasi("Talep edilen tarih mevcut cevap son gününden sonra olmalıdır.");

        var istisna = IncelemedekiIstisna();
        istisna.Durum = IstisnaDurumu.EkSureTalepEdildi;
        istisna.Cozum = IstisnaCozumu.EkSureTalebi;
        istisna.HukukcuId = hukukcuId;
        istisna.KararTarihi = simdi;
        istisna.HukukAciklamasi = aciklama?.Trim();
        istisna.IdareyeYaziMetni = talepYazisiMetni;
        istisna.IdareyeGidenSayi = gidenSayi?.Trim();
        istisna.HedefTarih = talepEdilenTarih;
        DurumDegistir(YaziDurumu.EkSureYanitiBekleniyor, simdi);
        Hareket(hukukcuId, simdi, "İdareden ek süre talep edildi", $"Talep edilen tarih: {talepEdilenTarih:dd.MM.yyyy}");
    }

    /// <summary>İdarenin ek süre talebine verdiği yanıt kaydedilir.</summary>
    public void EkSureYanitiKaydet(EkSureYaniti yanit, DateOnly? verilenTarih, Guid kullaniciId,
        SureHesaplayici hesaplayici, DateTimeOffset simdi)
    {
        DurumKontrol("ek süre yanıtı kaydedilemez", YaziDurumu.EkSureYanitiBekleniyor);
        var istisna = _istisnalar.Single(i => i.Durum == IstisnaDurumu.EkSureTalepEdildi && i.IdareYaniti is null);

        istisna.IdareYaniti = yanit;
        istisna.IdareYanitTarihi = simdi;
        if (yanit == EkSureYaniti.Kabul)
        {
            var tarih = verilenTarih ?? istisna.HedefTarih
                ?? throw new IsKuraliHatasi("İdarenin verdiği tarih belirtilmelidir.");
            if (tarih <= CevapSonGunu)
                throw new IsKuraliHatasi("İdarenin verdiği tarih mevcut cevap son gününden sonra olmalıdır.");
            istisna.IdareninVerdigiTarih = tarih;
            SureyiUzat(tarih, hesaplayici, simdi);
            Hareket(kullaniciId, simdi, "İdare ek süreyi kabul etti", $"Yeni cevap son günü: {tarih:dd.MM.yyyy}");
        }
        else
        {
            Hareket(kullaniciId, simdi, "İdare ek süre talebini reddetti",
                $"Asıl cevap son günü geçerli: {CevapSonGunu:dd.MM.yyyy}");
        }
        DurumDegistir(istisna.OncekiDurum, simdi);
    }

    /// <summary>
    /// Hukuk nedeni uygun bulur; süresinde cevap verilemeyeceğini gerekçeleriyle idareye yazar ve cevabın
    /// verilebileceği tarihi bildirir. Cevap son günü taahhüt edilen tarih olur, hatırlatmalar buna göre kurulur.
    /// </summary>
    public void GecikmeBildir(Guid hukukcuId, string gerekceYazisiMetni, DateOnly taahhutTarihi, string? gidenSayi,
        string? aciklama, SureHesaplayici hesaplayici, DateTimeOffset simdi)
    {
        DurumKontrol("gecikme bildirilemez", YaziDurumu.IstisnaHukukIncelemesinde);
        if (string.IsNullOrWhiteSpace(gerekceYazisiMetni))
            throw new IsKuraliHatasi("İdareye gönderilecek gerekçe yazısının metni zorunludur.");
        if (taahhutTarihi <= SureHesaplayici.TurkiyeTarihi(simdi))
            throw new IsKuraliHatasi("Taahhüt edilen tarih bugünden sonra olmalıdır.");

        var istisna = IncelemedekiIstisna();
        istisna.Durum = IstisnaDurumu.GecikmeBildirildi;
        istisna.Cozum = IstisnaCozumu.GecikmeBildirimi;
        istisna.HukukcuId = hukukcuId;
        istisna.KararTarihi = simdi;
        istisna.HukukAciklamasi = aciklama?.Trim();
        istisna.IdareyeYaziMetni = gerekceYazisiMetni;
        istisna.IdareyeGidenSayi = gidenSayi?.Trim();
        istisna.HedefTarih = taahhutTarihi;

        SureyiUzat(taahhutTarihi, hesaplayici, simdi);
        DurumDegistir(istisna.OncekiDurum, simdi);
        Hareket(hukukcuId, simdi, "Gecikme gerekçesi idareye bildirildi",
            $"Cevabın verileceği taahhüt edilen tarih: {taahhutTarihi:dd.MM.yyyy}");
    }

    // ------------------------------------------------------------------ Hatırlatmalar

    /// <summary>
    /// Mevcut duruma göre olması gereken hatırlatmaları planlayıcıdan alır; artık geçerli olmayan bekleyen
    /// hatırlatmaları iptal eder, eksik olanları ekler. Gönderilmiş hatırlatmalara dokunulmaz.
    /// </summary>
    public void HatirlatmalariYenile(HatirlatmaPlanlayici planlayici, DateTimeOffset simdi)
    {
        var istenen = planlayici.Planla(this)
            .Where(h => h.Zaman >= simdi || h.Zaman >= DurumTarihi)
            .ToList();

        static bool Ayni(Hatirlatma h, PlanlananHatirlatma p) =>
            h.Tur == p.Tur && h.AliciTipi == p.AliciTipi && h.AliciKullaniciId == p.AliciKullaniciId && h.Zaman == p.Zaman;

        foreach (var mevcut in _hatirlatmalar.Where(h => h.Bekliyor))
            if (!istenen.Any(p => Ayni(mevcut, p)))
                mevcut.Iptal = true;

        foreach (var p in istenen)
        {
            if (_hatirlatmalar.Any(h => !h.Iptal && Ayni(h, p))) continue;
            _hatirlatmalar.Add(new Hatirlatma
            {
                YaziId = Id,
                Tur = p.Tur,
                Zaman = p.Zaman,
                AliciTipi = p.AliciTipi,
                AliciKullaniciId = p.AliciKullaniciId,
                Mesaj = p.Mesaj,
            });
        }
    }

    public void SurumArtir() => SurumNo++;

    // ------------------------------------------------------------------ Yardımcılar

    private void SureyiUzat(DateOnly yeniSonGun, SureHesaplayici hesaplayici, DateTimeOffset simdi)
    {
        CevapSonGunu = yeniSonGun;
        SureUzatildi = true;
        SurecBaslangici = simdi;
        AraTarihleriYenile(hesaplayici);
    }

    private void AraTarihleriYenile(SureHesaplayici hesaplayici)
    {
        var ara = hesaplayici.AraTarihleriHesapla(SurecBaslangici, CevapSonGunu);
        TaslakSonTarihi = ara.TaslakSonTarihi;
        HukukSonTarihi = ara.HukukSonTarihi;
    }

    private void Onaylandi(Guid hukukcuId, DateTimeOffset simdi)
    {
        HukukOnayTarihi = simdi;
        OnaylayanHukukcuId = hukukcuId;
        DurumDegistir(YaziDurumu.HukukOnayladi, simdi);
    }

    private void Incele(HukukKarari karar, Guid hukukcuId, string? aciklama, DateTimeOffset simdi) =>
        _hukukIncelemeleri.Add(new HukukIncelemesi
        {
            YaziId = Id,
            TaslakVersiyonNo = GuncelTaslak?.VersiyonNo ?? 0,
            Karar = karar,
            Aciklama = aciklama?.Trim(),
            HukukcuId = hukukcuId,
            Tarih = simdi,
        });

    private Taslak YeniTaslak(string icerik, Guid kullaniciId, KullaniciRolu rol, DateTimeOffset simdi, string islem)
    {
        if (string.IsNullOrWhiteSpace(icerik))
            throw new IsKuraliHatasi("Taslak içeriği boş olamaz.");
        var taslak = new Taslak
        {
            YaziId = Id,
            VersiyonNo = (GuncelTaslak?.VersiyonNo ?? 0) + 1,
            Icerik = icerik,
            HazirlayanId = kullaniciId,
            HazirlayanRol = rol,
            Tarih = simdi,
        };
        _taslaklar.Add(taslak);
        Hareket(kullaniciId, simdi, islem, $"Versiyon {taslak.VersiyonNo}");
        return taslak;
    }

    private Istisna IncelemedekiIstisna() =>
        _istisnalar.SingleOrDefault(i => i.Durum == IstisnaDurumu.HukukIncelemesinde)
        ?? throw new IsKuraliHatasi("Hukuk incelemesinde bekleyen istisna yok.");

    private void DurumDegistir(YaziDurumu yeni, DateTimeOffset simdi)
    {
        Durum = yeni;
        DurumTarihi = simdi;
    }

    private void DurumKontrol(string islem, params YaziDurumu[] izinli)
    {
        if (!izinli.Contains(Durum))
            throw new IsKuraliHatasi($"Yazı '{Durum}' durumundayken {islem}.");
    }

    private void KapaliIseHata()
    {
        if (Kapali) throw new IsKuraliHatasi("Gönderilmiş yazı üzerinde değişiklik yapılamaz.");
    }

    private void Hareket(Guid? kullaniciId, DateTimeOffset simdi, string islem, string? aciklama) =>
        _hareketler.Add(new YaziHareketi
        {
            YaziId = Id,
            Tarih = simdi,
            KullaniciId = kullaniciId,
            Islem = islem,
            Aciklama = string.IsNullOrWhiteSpace(aciklama) ? null : aciklama,
        });
}
