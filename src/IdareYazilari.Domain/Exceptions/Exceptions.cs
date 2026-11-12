namespace IdareYazilari.Domain.Exceptions;

/// <summary>İş kuralı ihlali (ör. geçersiz durum geçişi).</summary>
public class IsKuraliHatasi(string mesaj) : Exception(mesaj);

/// <summary>Kullanıcının işlem için yetkisi yok.</summary>
public class YetkiHatasi(string mesaj) : Exception(mesaj);

/// <summary>Kayıt bulunamadı.</summary>
public class BulunamadiHatasi(string mesaj) : Exception(mesaj);
