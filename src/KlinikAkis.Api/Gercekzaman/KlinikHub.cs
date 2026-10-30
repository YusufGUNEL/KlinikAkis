using KlinikAkis.Api.Kimlik;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace KlinikAkis.Api.Gercekzaman;

/// <summary>
/// Tek hub (SPEC §8). Gruplar bağlantı açılırken imzalı token'daki claim'lerden sunucuda atanır;
/// istemcinin grup seçen ya da mesaj gönderen bir metodu yoktur. Doğruluğun kaynağı REST'tir: hub yalnızca
/// "değişti, yeniden çek" sinyali iter.
/// </summary>
[Authorize]
public sealed class KlinikHub : Hub
{
    public const string Yol = "/hubs/klinik";
    public const string IstemciMetodu = "olay";

    public override async Task OnConnectedAsync()
    {
        foreach (var grup in Gruplar(Context.User))
            await Groups.AddToGroupAsync(Context.ConnectionId, grup);
        await base.OnConnectedAsync();
    }

    internal static IEnumerable<string> Gruplar(System.Security.Claims.ClaimsPrincipal? kullanici)
    {
        yield return Grup.Demo;
        var rol = kullanici?.FindFirst(DemoClaim.Rol)?.Value;
        string? Claim(string ad) => kullanici?.FindFirst(ad)?.Value;

        switch (rol)
        {
            case DemoRol.Doktor:
                if (Claim(DemoClaim.HekimId) is { } hekim) yield return Grup.Doktor(hekim);
                if (Claim(DemoClaim.BransId) is { } brans) yield return Grup.Klinik(brans);
                break;
            case DemoRol.Hasta:
                if (Claim(DemoClaim.HastaId) is { } hasta) yield return Grup.Hasta(hasta);
                break;
            case DemoRol.Kabul:
                yield return Grup.Kabul;
                break;
            case DemoRol.Tv:
                if (Claim(DemoClaim.BransId) is { } tvBrans) yield return Grup.Tv(tvBrans);
                break;
        }
    }
}

/// <summary>Grup adları (SPEC §8).</summary>
public static class Grup
{
    public const string Demo = "demo";
    public const string Kabul = "reception";
    public static string Doktor(object hekimId) => $"doctor:{hekimId}";
    public static string Klinik(object bransId) => $"clinic:{bransId}";
    public static string Hasta(object hastaId) => $"patient:{hastaId}";
    public static string Tv(object bransId) => $"tv:{bransId}";
}
