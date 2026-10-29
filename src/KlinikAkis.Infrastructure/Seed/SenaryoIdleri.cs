namespace KlinikAkis.Infrastructure.Seed;

/// <summary>
/// Senaryo varlıklarının sabit Id'leri (SPEC §6). QR / derin bağlantı ve testler bunlara dayanır.
/// Biçim: a1 = senaryo hasta, b1 = hekim, c1 = tahlil tanımı; son haneler sıra no.
/// </summary>
public static class SenaryoIdleri
{
    public static readonly Guid Ayse = new("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid Mehmet = new("a1000000-0000-0000-0000-000000000002");
    public static readonly Guid Zeynep = new("a1000000-0000-0000-0000-000000000003");
    public static readonly Guid Hasan = new("a1000000-0000-0000-0000-000000000004");
    public static readonly Guid Elif = new("a1000000-0000-0000-0000-000000000005");
    public static readonly Guid Can = new("a1000000-0000-0000-0000-000000000006");

    /// <summary>Rol seçicide listelenen 6 senaryo hastası, senaryo sırasıyla.</summary>
    public static readonly IReadOnlyList<Guid> Hastalar = [Ayse, Mehmet, Zeynep, Hasan, Elif, Can];

    public static readonly Guid HekimEndokrinoloji = new("b1000000-0000-0000-0000-000000000001");
    public static readonly Guid HekimDahiliye1 = new("b1000000-0000-0000-0000-000000000002");
    public static readonly Guid HekimDahiliye2 = new("b1000000-0000-0000-0000-000000000003");
    public static readonly Guid HekimKbb = new("b1000000-0000-0000-0000-000000000004");
    public static readonly Guid HekimCocukSagligi = new("b1000000-0000-0000-0000-000000000005");
    public static readonly Guid HekimKardiyoloji = new("b1000000-0000-0000-0000-000000000006");

    public static readonly Guid HbA1c = new("c1000000-0000-0000-0000-000000000001");
    public static readonly Guid AclikGlukozu = new("c1000000-0000-0000-0000-000000000002");
    public static readonly Guid Hemoglobin = new("c1000000-0000-0000-0000-000000000003");
    public static readonly Guid Ferritin = new("c1000000-0000-0000-0000-000000000004");
    public static readonly Guid Tsh = new("c1000000-0000-0000-0000-000000000005");
}
