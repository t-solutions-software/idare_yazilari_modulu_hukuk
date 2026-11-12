using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IdareYazilari.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace IdareYazilari.Infrastructure.Persistence;

public class SharePointAyarlari
{
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    /// <summary>Gizli değer; ortam değişkeni / Key Vault ile verilmeli (Dosyalar__SharePoint__ClientSecret).</summary>
    public string ClientSecret { get; set; } = "";
    /// <summary>SharePoint sitesinin Graph kimliği (ör. kurum.sharepoint.com,guid,guid).</summary>
    public string SiteId { get; set; } = "";
    /// <summary>Doküman kitaplığının (drive) kimliği. Boşsa sitenin varsayılan kitaplığı.</summary>
    public string? DriveId { get; set; }
    public string KokKlasor { get; set; } = "IdareYazilari";
}

/// <summary>
/// Ekleri Microsoft Graph üzerinden SharePoint doküman kitaplığında saklar (uygulama izni: Sites.Selected veya
/// Sites.ReadWrite.All). Klasör yapısı: {KokKlasor}/{yıl}/{yazıId}/{dosya}. Depo anahtarı "sp:{driveId}:{itemId}".
/// </summary>
public class SharePointDosyaDeposu(HttpClient http, IOptions<SharePointAyarlari> secenekler) : IDosyaDeposu
{
    private const string Graph = "https://graph.microsoft.com/v1.0";
    private const int TekParcaSiniri = 4 * 1024 * 1024;
    private string? _token;
    private DateTimeOffset _tokenBitis;
    private string? _driveId;
    private readonly SemaphoreSlim _kilit = new(1, 1);

    public async Task<string> KaydetAsync(Guid yaziId, string dosyaAdi, byte[] icerik, CancellationToken ct = default)
    {
        var a = secenekler.Value;
        var drive = await DriveAsync(ct);
        var ad = string.Concat(Path.GetFileName(dosyaAdi).Select(c => "\"*:<>?/\\|#%".Contains(c) ? '_' : c));
        var yol = $"{a.KokKlasor.Trim('/')}/{DateTime.UtcNow:yyyy}/{yaziId:N}/{Guid.NewGuid():N}_{ad}";
        var yolUrl = string.Join('/', yol.Split('/').Select(Uri.EscapeDataString));

        JsonElement oge;
        if (icerik.Length <= TekParcaSiniri)
        {
            using var istek = await IstekAsync(HttpMethod.Put, $"{Graph}/drives/{drive}/root:/{yolUrl}:/content", ct);
            istek.Content = new ByteArrayContent(icerik);
            oge = await GonderJsonAsync(istek, ct);
        }
        else
        {
            using var oturumIstek = await IstekAsync(HttpMethod.Post, $"{Graph}/drives/{drive}/root:/{yolUrl}:/createUploadSession", ct);
            oturumIstek.Content = JsonContent.Create(new { item = new Dictionary<string, string> { ["@microsoft.graph.conflictBehavior"] = "rename" } });
            var oturum = await GonderJsonAsync(oturumIstek, ct);
            var url = oturum.GetProperty("uploadUrl").GetString()!;
            const int parca = 5 * 1024 * 1024 + 2 * 320 * 1024; // 320 KiB katı
            oge = default;
            for (var bas = 0; bas < icerik.Length; bas += parca)
            {
                var uzunluk = Math.Min(parca, icerik.Length - bas);
                using var p = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(icerik, bas, uzunluk) };
                p.Content.Headers.ContentRange = new ContentRangeHeaderValue(bas, bas + uzunluk - 1, icerik.Length);
                using var yanit = await http.SendAsync(p, ct);
                yanit.EnsureSuccessStatusCode();
                if (bas + uzunluk >= icerik.Length)
                    oge = await yanit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            }
        }
        return $"sp:{drive}:{oge.GetProperty("id").GetString()}";
    }

    public async Task<Stream?> AcAsync(string depoAnahtari, CancellationToken ct = default)
    {
        var p = depoAnahtari.Split(':', 3);
        if (p.Length != 3 || p[0] != "sp") return null;
        using var istek = await IstekAsync(HttpMethod.Get, $"{Graph}/drives/{p[1]}/items/{p[2]}/content", ct);
        var yanit = await http.SendAsync(istek, HttpCompletionOption.ResponseHeadersRead, ct);
        if (yanit.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        yanit.EnsureSuccessStatusCode();
        return await yanit.Content.ReadAsStreamAsync(ct);
    }

    private async Task<string> DriveAsync(CancellationToken ct)
    {
        if (_driveId is not null) return _driveId;
        var a = secenekler.Value;
        if (!string.IsNullOrWhiteSpace(a.DriveId)) return _driveId = a.DriveId;
        using var istek = await IstekAsync(HttpMethod.Get, $"{Graph}/sites/{a.SiteId}/drive?$select=id", ct);
        return _driveId = (await GonderJsonAsync(istek, ct)).GetProperty("id").GetString()!;
    }

    private async Task<HttpRequestMessage> IstekAsync(HttpMethod yontem, string url, CancellationToken ct)
    {
        var istek = new HttpRequestMessage(yontem, url);
        istek.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        return istek;
    }

    private async Task<JsonElement> GonderJsonAsync(HttpRequestMessage istek, CancellationToken ct)
    {
        using var yanit = await http.SendAsync(istek, ct);
        if (!yanit.IsSuccessStatusCode)
            throw new InvalidOperationException($"SharePoint hatası {(int)yanit.StatusCode}: {await yanit.Content.ReadAsStringAsync(ct)}");
        return await yanit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
    }

    /// <summary>İstemci kimlik bilgisi (client credentials) akışıyla Graph erişim belirteci.</summary>
    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenBitis) return _token;
        await _kilit.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenBitis) return _token;
            var a = secenekler.Value;
            using var yanit = await http.PostAsync($"https://login.microsoftonline.com/{a.TenantId}/oauth2/v2.0/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = a.ClientId,
                    ["client_secret"] = a.ClientSecret,
                    ["scope"] = "https://graph.microsoft.com/.default",
                    ["grant_type"] = "client_credentials",
                }), ct);
            if (!yanit.IsSuccessStatusCode)
                throw new InvalidOperationException($"Entra ID belirteci alınamadı: {(int)yanit.StatusCode} {await yanit.Content.ReadAsStringAsync(ct)}");
            var j = await yanit.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            _token = j.GetProperty("access_token").GetString();
            _tokenBitis = DateTimeOffset.UtcNow.AddSeconds(j.GetProperty("expires_in").GetInt32() - 120);
            return _token!;
        }
        finally { _kilit.Release(); }
    }
}
