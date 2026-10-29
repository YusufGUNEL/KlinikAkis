namespace KlinikAkis.Domain;

/// <summary>
/// T.C. kimlik numarası sağlama kuralı (SPEC §9.2). Seed üreticisi ve maskeleyici aynı fonksiyonu kullanır.
/// h10 = ((h1+h3+h5+h7+h9)*7 − (h2+h4+h6+h8)) mod 10, h11 = (h1+…+h10) mod 10.
/// </summary>
public static class TcKimlik
{
    public static bool GecerliMi(string? tc)
    {
        if (tc is null || tc.Length != 11 || tc[0] == '0')
            return false;
        foreach (var c in tc)
            if (c is < '0' or > '9')
                return false;

        var (h10, h11) = SaglamaHaneleri(tc.AsSpan(0, 9));
        return tc[9] - '0' == h10 && tc[10] - '0' == h11;
    }

    /// <summary>İlk 9 haneye iki sağlama hanesini ekler.</summary>
    public static string SaglamaEkle(string ilk9)
    {
        if (ilk9.Length != 9 || ilk9[0] == '0' || !ilk9.All(char.IsAsciiDigit))
            throw new ArgumentException("İlk 9 hane rakam olmalı ve 0 ile başlamamalı.", nameof(ilk9));

        var (h10, h11) = SaglamaHaneleri(ilk9);
        return string.Concat(ilk9, (char)('0' + h10), (char)('0' + h11));
    }

    private static (int H10, int H11) SaglamaHaneleri(ReadOnlySpan<char> ilk9)
    {
        int tek = 0, cift = 0;
        for (var i = 0; i < 9; i++)
        {
            var h = ilk9[i] - '0';
            if (i % 2 == 0) tek += h; else cift += h;
        }
        // C#'ta % negatif sonuç verebilir; mod 10'u pozitife çek.
        var h10 = (((tek * 7) - cift) % 10 + 10) % 10;
        var h11 = (tek + cift + h10) % 10;
        return (h10, h11);
    }
}
