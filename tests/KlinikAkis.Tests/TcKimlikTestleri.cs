using KlinikAkis.Domain;

namespace KlinikAkis.Tests;

public sealed class TcKimlikTestleri
{
    [Theory]
    [InlineData("10000000146")] // sağlama kuralına uyan bilinen örnek
    [InlineData("99100000188")]
    public void Gecerli_TC_kabul_edilir(string tc) => Assert.True(TcKimlik.GecerliMi(tc));

    [Theory]
    [InlineData("10000000156")] // 10. hane yanlış
    [InlineData("10000000147")] // 11. hane yanlış
    [InlineData("01000000146")] // 0 ile başlıyor
    [InlineData("1000000014")]  // 10 hane
    [InlineData("100000001466")] // 12 hane
    [InlineData("1000000014a")] // harf
    [InlineData("")]
    [InlineData(null)]
    public void Gecersiz_TC_reddedilir(string? tc) => Assert.False(TcKimlik.GecerliMi(tc));

    [Fact]
    public void SaglamaEkle_gecerli_TC_uretir()
    {
        for (var i = 0; i < 1000; i++)
        {
            var tc = TcKimlik.SaglamaEkle($"99{1000000 + i * 7919 % 9000000}");
            Assert.True(TcKimlik.GecerliMi(tc), tc);
        }
    }

    [Theory]
    [InlineData("012345678")]
    [InlineData("12345678")]
    [InlineData("12345678a")]
    public void SaglamaEkle_hatali_girdiyi_reddeder(string ilk9) =>
        Assert.Throws<ArgumentException>(() => TcKimlik.SaglamaEkle(ilk9));
}
