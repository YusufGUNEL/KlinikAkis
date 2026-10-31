using Microsoft.AspNetCore.SignalR;

namespace KlinikAkis.Api.Gercekzaman;

/// <summary>Olay türleri (SPEC §8). Payload yalnızca {tur, id}: istemci ilgili REST'i yeniden çeker.</summary>
public static class OlayTuru
{
    public const string RandevuAlindi = nameof(RandevuAlindi);
    public const string RandevuDurumDegisti = nameof(RandevuDurumDegisti);
    public const string KabulYapildi = nameof(KabulYapildi);
    public const string HastaCagrildi = nameof(HastaCagrildi);
    public const string OnAnamnezHazir = nameof(OnAnamnezHazir);
    public const string OnAnamnezKarar = nameof(OnAnamnezKarar);
    public const string TahlilYayinlandi = nameof(TahlilYayinlandi);
    public const string EpikrizOnaylandi = nameof(EpikrizOnaylandi);
    public const string DemoSifirlandi = nameof(DemoSifirlandi);
}

public sealed record Olay(string Tur, Guid? Id);

/// <summary>Tüm olaylar buradan çıkar; testte yerine sahte konabilir (SPEC §8).</summary>
public interface IOlayYayinci
{
    Task YayinlaAsync(string tur, Guid? id, IEnumerable<string> gruplar, CancellationToken ct = default);
}

public sealed class OlayYayinci(IHubContext<KlinikHub> hub) : IOlayYayinci
{
    public Task YayinlaAsync(string tur, Guid? id, IEnumerable<string> gruplar, CancellationToken ct = default) =>
        hub.Clients.Groups([.. gruplar.Distinct(StringComparer.Ordinal)])
            .SendAsync(KlinikHub.IstemciMetodu, new Olay(tur, id), ct);
}
