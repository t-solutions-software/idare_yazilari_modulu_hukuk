using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Entities;
using IdareYazilari.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IdareYazilari.Infrastructure.Persistence;

public class EfYaziDeposu(IdareYazilariDbContext db) : IYaziDeposu
{
    private static readonly YaziDurumu[] AcikDurumlar = Enum.GetValues<YaziDurumu>().Where(d => d != YaziDurumu.Gonderildi).ToArray();

    public Task<IdareYazisi?> GetirAsync(Guid id, CancellationToken ct = default) =>
        db.Yazilar
            .Include(y => y.Taslaklar)
            .Include(y => y.HukukIncelemeleri)
            .Include(y => y.Istisnalar)
            .Include(y => y.Hatirlatmalar)
            .Include(y => y.Hareketler)
            .Include(y => y.Ekler)
            .Include(y => y.GidenYazilar)
            .Include(y => y.Gorevler)
            .AsSplitQuery()
            .FirstOrDefaultAsync(y => y.Id == id, ct);

    public Task<bool> DisReferansVarMiAsync(YaziKaynagi kaynak, string disReferans, CancellationToken ct = default) =>
        db.Yazilar.AnyAsync(y => y.Kaynak == kaynak && y.DisReferans == disReferans, ct);

    public async Task<IReadOnlyList<IdareYazisi>> ListeleAsync(YaziFiltresi f, CancellationToken ct = default)
    {
        var q = db.Yazilar.AsNoTracking().AsQueryable();
        if (f.Kapsam is { } k)
        {
            var birimler = k.BirimIdler?.ToList() ?? [];
            q = q.Where(y =>
                (k.ProjeId != null && y.ProjeId == k.ProjeId) ||
                (k.SorumluId != null && y.SorumluId == k.SorumluId) ||
                (y.BirimId != null && birimler.Contains(y.BirimId.Value)) ||
                (k.ImzaYetkilisiId != null && (k.ImzaProjeId == null || y.ProjeId == k.ImzaProjeId) &&
                    (y.Durum == YaziDurumu.ImzaBekliyor || y.ImzalayanId == k.ImzaYetkilisiId)));
        }
        if (!string.IsNullOrWhiteSpace(f.SozlesmeReferansi)) q = q.Where(y => y.SozlesmeReferansi == f.SozlesmeReferansi);
        if (f.ProjeId is { } p) q = q.Where(y => y.ProjeId == p);
        if (f.SorumluId is { } s) q = q.Where(y => y.SorumluId == s);
        if (f.Durumlar is { Count: > 0 } d) q = q.Where(y => d.Contains(y.Durum));
        if (f.SadeceAcik) q = q.Where(y => AcikDurumlar.Contains(y.Durum));
        if (!string.IsNullOrWhiteSpace(f.Arama))
        {
            var a = f.Arama.Trim();
            q = q.Where(y => y.Konu.Contains(a) || y.GonderenIdare.Contains(a) || (y.GelenSayi != null && y.GelenSayi.Contains(a)) ||
                             y.Ozet.Contains(a) || y.EvrakNo.Contains(a) || (y.GidenSayi != null && y.GidenSayi.Contains(a)) ||
                             (y.SozlesmeReferansi != null && y.SozlesmeReferansi.Contains(a)));
        }
        return await q.OrderBy(y => y.CevapSonGunu).Take(1000).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> HatirlatmasiGelenYazilarAsync(DateTimeOffset simdi, CancellationToken ct = default) =>
        await db.Hatirlatmalar
            .Where(h => h.GonderimZamani == null && !h.Iptal && h.Zaman <= simdi)
            .Select(h => h.YaziId).Distinct().ToListAsync(ct);

    public async Task<IReadOnlyList<(Hatirlatma Hatirlatma, IdareYazisi Yazi)>> GonderilenHatirlatmalarAsync(
        DateTimeOffset baslangic, CancellationToken ct = default)
    {
        var liste = await db.Hatirlatmalar.AsNoTracking()
            .Where(h => h.GonderimZamani != null && h.GonderimZamani >= baslangic)
            .Join(db.Yazilar.AsNoTracking(), h => h.YaziId, y => y.Id, (h, y) => new { h, y })
            .ToListAsync(ct);
        return liste.Select(x => (x.h, x.y)).ToList();
    }

    public async Task<IReadOnlyList<Guid>> AktarilacakGorevYazilariAsync(int azamiDeneme, CancellationToken ct = default) =>
        await db.Gorevler.Where(g => g.AktarimDurumu != AktarimDurumu.Aktarildi && g.DenemeSayisi < azamiDeneme)
            .Select(g => g.YaziId).Distinct().ToListAsync(ct);

    public async Task<IReadOnlyList<YaziGorevi>> GorevleriListeleAsync(AktarimDurumu? durum, CancellationToken ct = default)
    {
        var q = db.Gorevler.AsNoTracking().AsQueryable();
        if (durum is { } d) q = q.Where(g => g.AktarimDurumu == d);
        return (await q.Take(500).ToListAsync(ct)).OrderBy(g => g.Tarih).ToList();
    }

    public void Ekle(IdareYazisi yazi) => db.Yazilar.Add(yazi);

    public async Task KaydetAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new EszamanlilikHatasi();
        }
    }
}

public class EfKullaniciDeposu(IdareYazilariDbContext db) : IKullaniciDeposu
{
    public Task<Kullanici?> GetirAsync(Guid id, CancellationToken ct = default) =>
        db.Kullanicilar.AsNoTracking().FirstOrDefaultAsync(k => k.Id == id, ct);

    public async Task<IReadOnlyList<Kullanici>> ListeleAsync(Guid? projeId = null, KullaniciRolu? rol = null, CancellationToken ct = default)
    {
        var q = db.Kullanicilar.AsNoTracking().AsQueryable();
        if (projeId is { } p) q = q.Where(k => k.ProjeId == p);
        if (rol is { } r) q = q.Where(k => k.Rol == r);
        return await q.OrderBy(k => k.AdSoyad).ToListAsync(ct);
    }

    public Task<Proje?> ProjeGetirAsync(Guid id, CancellationToken ct = default) =>
        db.Projeler.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Proje?> ProjeBulAsync(string? kod, string? aliciAdres, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(kod) &&
            await db.Projeler.AsNoTracking().FirstOrDefaultAsync(p => p.Kod == kod.Trim(), ct) is { } p1)
            return p1;
        if (!string.IsNullOrWhiteSpace(aliciAdres))
        {
            var adres = aliciAdres.Trim().ToLowerInvariant();
            return await db.Projeler.AsNoTracking()
                .FirstOrDefaultAsync(p => (p.KepAdresi != null && p.KepAdresi.ToLower() == adres) ||
                                          (p.UetsAdresi != null && p.UetsAdresi.ToLower() == adres), ct);
        }
        return null;
    }

    public async Task<IReadOnlyList<Proje>> ProjeleriListeleAsync(CancellationToken ct = default) =>
        await db.Projeler.AsNoTracking().OrderBy(p => p.Kod).ToListAsync(ct);

    public Task<Birim?> BirimGetirAsync(Guid id, CancellationToken ct = default) =>
        db.Birimler.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);

    /// <summary>projeId verilirse o projenin birimleri + genel merkez birimleri; verilmezse tümü.</summary>
    public async Task<IReadOnlyList<Birim>> BirimleriListeleAsync(Guid? projeId = null, CancellationToken ct = default)
    {
        var q = db.Birimler.AsNoTracking().AsQueryable();
        if (projeId is { } p) q = q.Where(b => b.ProjeId == p || b.ProjeId == null);
        return await q.OrderBy(b => b.Ad).ToListAsync(ct);
    }

    public async Task<Kullanici?> EntraIleBulAsync(string? objectId, string? eposta, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(objectId) &&
            await db.Kullanicilar.AsNoTracking().FirstOrDefaultAsync(k => k.EntraObjectId == objectId, ct) is { } k1)
            return k1;
        if (string.IsNullOrWhiteSpace(eposta)) return null;
        var e = eposta.Trim().ToLower();
        return await db.Kullanicilar.AsNoTracking().FirstOrDefaultAsync(k => k.Eposta.ToLower() == e, ct);
    }

    public async Task KaydetAsync<T>(T varlik, bool yeni, CancellationToken ct = default) where T : class
    {
        if (yeni) db.Set<T>().Add(varlik); else db.Set<T>().Update(varlik);
        await db.SaveChangesAsync(ct);
        db.Entry(varlik).State = EntityState.Detached;
    }
}
