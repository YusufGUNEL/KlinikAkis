using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace KlinikAkis.Api.Kimlik;

/// <summary>
/// Demo JWT'sindeki claim adları ve roller (SPEC §7). SignalR grupları da Aşama 3'te bu claim'lerden atanır;
/// istemci kendi rolünü veya kimliğini seçemez, yalnızca imzalı token'daki değer geçerlidir.
/// </summary>
public static class DemoClaim
{
    public const string Rol = "rol";
    public const string Ad = "ad";
    public const string HastaId = "hastaId";
    public const string HekimId = "hekimId";
    public const string BransId = "bransId";
}

public static class DemoRol
{
    public const string Hasta = "hasta";
    public const string Doktor = "doktor";
    public const string Kabul = "kabul";
    public const string Tv = "tv";

    public static readonly string[] Hepsi = [Hasta, Doktor, Kabul, Tv];
}

/// <summary>Rol başına yetki politikası; politika adı rol adıyla aynıdır.</summary>
public static class DemoPolitika
{
    /// <summary>Doktor ya da kabul: randevu listesi ve durum geçişleri (hangi eylemi kimin yapacağı uçta ayrıca denetlenir).</summary>
    public const string Personel = "personel";

    public static AuthorizationBuilder AddDemoPolitikalari(this AuthorizationBuilder yetki)
    {
        foreach (var rol in DemoRol.Hepsi)
            yetki.AddPolicy(rol, p => p.RequireClaim(DemoClaim.Rol, rol));
        yetki.AddPolicy(Personel, p => p.RequireClaim(DemoClaim.Rol, DemoRol.Doktor, DemoRol.Kabul));
        return yetki;
    }
}

/// <summary>HMAC-SHA256 imzalı, 8 saat geçerli demo token'ı üretir.</summary>
public sealed class DemoTokenUretici(SymmetricSecurityKey anahtar, TimeProvider zaman)
{
    public static readonly TimeSpan Sure = TimeSpan.FromHours(8);
    public const string Yayinci = "klinikakis-demo";

    private readonly JsonWebTokenHandler _isleyici = new();

    public (string Token, DateTime Bitis) Uret(IEnumerable<Claim> claimler)
    {
        var simdi = zaman.GetUtcNow().UtcDateTime;
        var bitis = simdi.Add(Sure);
        var token = _isleyici.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Yayinci,
            Audience = Yayinci,
            Subject = new ClaimsIdentity(claimler),
            IssuedAt = simdi,
            NotBefore = simdi,
            Expires = bitis,
            SigningCredentials = new SigningCredentials(anahtar, SecurityAlgorithms.HmacSha256),
        });
        return (token, bitis);
    }
}
