using IdareYazilari.Application.Abstractions;
using IdareYazilari.Application.Dtos;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using IdareYazilari.Domain.Exceptions;
using IdareYazilari.Domain.Services;

namespace IdareYazilari.Application.Services;

/// <summary>Yazı üzerindeki tüm iş akışı adımları (atama, taslak, hukuk, gönderim, istisnalar).</summary>
public class YaziIsAkisiServisi(
    IYaziDeposu depo,
    IKullaniciDeposu kullanicilar,
    SureHesaplayici hesaplayici,
    HatirlatmaPlanlayici planlayici,
    IMevcutKullanici mevcut,
    ISaat saat)
{
    public Task<IdareYazisi> BilgileriDuzeltAsync(Guid id, BilgiDuzeltKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.BilgiDuzelt, (u, y, simdi) =>
        {
            y.BilgileriDuzelt(k.Konu, k.Ozet, k.CevapSonGunu, u.Id, hesaplayici, simdi);
            return Task.CompletedTask;
        }, ct);

    public Task<IdareYazisi> ProjeAtaAsync(Guid id, ProjeAtaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.ProjeAta, async (u, y, simdi) =>
        {
            _ = await kullanicilar.ProjeGetirAsync(k.ProjeId, ct) ?? throw new BulunamadiHatasi("Proje bulunamadı.");
            y.ProjeAta(k.ProjeId, u.Id, simdi);
        }, ct);

    public Task<IdareYazisi> SorumluAtaAsync(Guid id, SorumluAtaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.SorumluAta, async (u, y, simdi) =>
        {
            var sorumlu = await kullanicilar.GetirAsync(k.SorumluId, ct) ?? throw new BulunamadiHatasi("Kullanıcı bulunamadı.");
            if (!sorumlu.Aktif || sorumlu.ProjeId != y.ProjeId ||
                sorumlu.Rol is not (KullaniciRolu.Personel or KullaniciRolu.ProjeMuduru))
                throw new IsKuraliHatasi("Sorumlu, yazının ait olduğu projenin aktif personeli veya proje müdürü olmalıdır.");
            y.SorumluAta(sorumlu.Id, u.Id, simdi);
        }, ct);

    public Task<IdareYazisi> TaslakKaydetAsync(Guid id, TaslakKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.TaslakKaydet, (u, y, simdi) =>
        {
            y.TaslakKaydet(k.Icerik, u.Id, u.Rol, simdi);
            if (k.HukukaGonder) y.HukukaGonder(u.Id, simdi);
            return Task.CompletedTask;
        }, ct);

    public Task<IdareYazisi> HukukaGonderAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.HukukaGonder, (u, y, simdi) => { y.HukukaGonder(u.Id, simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> HukukOnaylaAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.HukukOnayla, (u, y, simdi) => { y.HukukOnayla(u.Id, k.Aciklama, simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> RevizyonTalepEtAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.RevizyonTalepEt, (u, y, simdi) => { y.RevizyonTalepEt(u.Id, k.Aciklama ?? "", simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> RevizeEtVeOnaylaAsync(Guid id, RevizeKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.RevizeEtVeOnayla, (u, y, simdi) => { y.RevizeEtVeOnayla(u.Id, k.Icerik, k.Aciklama, simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> IdareyeGonderAsync(Guid id, GonderimKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.IdareyeGonder, (u, y, simdi) => { y.IdareyeGonder(u.Id, k.GidenSayi, k.Kanal, simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> IstisnaBildirAsync(Guid id, IstisnaBildirKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.IstisnaBildir, (u, y, simdi) => { y.SuresindeCevapVerilemezBildir(k.Neden, k.OnerilenTarih, u.Id, simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> IstisnaReddetAsync(Guid id, AciklamaKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.IstisnaReddet, (u, y, simdi) => { y.IstisnaReddet(u.Id, k.Aciklama ?? "", simdi); return Task.CompletedTask; }, ct);

    public Task<IdareYazisi> EkSureTalepEtAsync(Guid id, EkSureTalepKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.EkSureTalepEt, (u, y, simdi) =>
        {
            y.EkSureTalepEt(u.Id, k.TalepYazisiMetni, k.TalepEdilenTarih, k.GidenSayi, k.Aciklama, simdi);
            return Task.CompletedTask;
        }, ct);

    public Task<IdareYazisi> GecikmeBildirAsync(Guid id, GecikmeBildirKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.GecikmeBildir, (u, y, simdi) =>
        {
            y.GecikmeBildir(u.Id, k.GerekceYazisiMetni, k.TaahhutTarihi, k.GidenSayi, k.Aciklama, hesaplayici, simdi);
            return Task.CompletedTask;
        }, ct);

    public Task<IdareYazisi> EkSureYanitiKaydetAsync(Guid id, EkSureYanitKomutu k, CancellationToken ct = default) =>
        Uygula(id, k.SurumNo, Islemler.EkSureYanitiKaydet, (u, y, simdi) =>
        {
            y.EkSureYanitiKaydet(k.Yanit, k.VerilenTarih, u.Id, hesaplayici, simdi);
            return Task.CompletedTask;
        }, ct);

    /// <summary>
    /// Ortak işlem iskeleti: yazıyı yükle → görünürlük + yetki kontrolü → eşzamanlılık kontrolü →
    /// işlemi uygula → hatırlatmaları yeniden planla → kaydet.
    /// </summary>
    private async Task<IdareYazisi> Uygula(Guid id, int? beklenenSurum, string islem,
        Func<Kullanici, IdareYazisi, DateTimeOffset, Task> uygula, CancellationToken ct)
    {
        var kullanici = await mevcut.GetirAsync(ct);
        var yazi = await depo.GetirAsync(id, ct);
        if (yazi is null || !Yetki.GorebilirMi(kullanici, yazi))
            throw new BulunamadiHatasi("Yazı bulunamadı.");
        if (beklenenSurum is { } s && s != yazi.SurumNo)
            throw new EszamanlilikHatasi();

        Yetki.Gerekli(kullanici, yazi, islem);

        var simdi = saat.Simdi;
        await uygula(kullanici, yazi, simdi);
        yazi.HatirlatmalariYenile(planlayici, simdi);
        yazi.SurumArtir();
        await depo.KaydetAsync(ct);
        return yazi;
    }
}
