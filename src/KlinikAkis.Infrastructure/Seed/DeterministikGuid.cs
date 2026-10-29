using System.Security.Cryptography;
using System.Text;

namespace KlinikAkis.Infrastructure.Seed;

/// <summary>
/// Addan sabit Guid üretir (SHA-256 → ilk 16 bayt, RFC 4122 v5 benzeri sürüm/varyant bitleri).
/// Aynı ad her zaman aynı Guid'i verir; böylece "şimdiye göre" üretilen slotların Id'leri de değişmez.
/// </summary>
public static class DeterministikGuid
{
    public static Guid Uret(params object[] parcalar)
    {
        var ad = string.Join('|', parcalar.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)));
        Span<byte> ozet = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(ad), ozet);
        var b = ozet[..16];
        b[6] = (byte)((b[6] & 0x0F) | 0x50);
        b[8] = (byte)((b[8] & 0x3F) | 0x80);
        return new Guid(b, bigEndian: true);
    }
}
