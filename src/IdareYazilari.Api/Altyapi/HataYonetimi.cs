using IdareYazilari.Application.Abstractions;
using IdareYazilari.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace IdareYazilari.Api.Altyapi;

/// <summary>Uygulama hatalarını uygun HTTP durum kodlu ProblemDetails yanıtlarına çevirir.</summary>
public class HataYonetimi(IProblemDetailsService problem, ILogger<HataYonetimi> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        var (durum, baslik) = ex switch
        {
            BulunamadiHatasi => (StatusCodes.Status404NotFound, "Bulunamadı"),
            YetkiHatasi => (StatusCodes.Status403Forbidden, "Yetkisiz işlem"),
            EszamanlilikHatasi => (StatusCodes.Status409Conflict, "Eşzamanlı güncelleme"),
            IsKuraliHatasi => (StatusCodes.Status422UnprocessableEntity, "İş kuralı"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Geçersiz istek"),
            _ => (StatusCodes.Status500InternalServerError, "Beklenmeyen hata"),
        };
        if (durum == 500) log.LogError(ex, "Beklenmeyen hata");

        ctx.Response.StatusCode = durum;
        return await problem.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            ProblemDetails = new ProblemDetails
            {
                Status = durum,
                Title = baslik,
                Detail = durum == 500 ? "Sunucu hatası oluştu." : ex.Message,
            },
        });
    }
}
