using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using KlinikAkis.Domain;
using Microsoft.Extensions.AI;

namespace KlinikAkis.Infrastructure.Ai;

/// <summary>
/// Ön anamnez çıktı şeması (SPEC §9.5). Modele bu tipten üretilen JSON şeması gider (enum'lar, tüm alanlar zorunlu,
/// ek alan yok); dönen JSON aynı tiple okunur ve sunucuda ayrıca doğrulanır. <c>SohbetAdimi = 0</c>: "kaynak yok".
/// </summary>
public sealed record OnAnamnezTaslagi(
    string Ozet,
    IReadOnlyList<string> Semptomlar,
    string Sure,
    Siddet Siddet,
    BransKodu OnerilenBrans,
    BransKodu? AlternatifBrans,
    string Gerekce,
    Aciliyet Aciliyet,
    IReadOnlyList<string> DoktoraSorular,
    IReadOnlyList<KaynakMaddesi> Kaynaklar)
{
    /// <summary>Şema, okuma ve saklama için tek ayar: camelCase, enum adları metin, Türkçe harfler kaçırılmaz.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerOptions.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>
    /// Modele giden yanıt biçimi: tipten üretilen şema + her nesnede <c>additionalProperties: false</c> ve tüm alanlar
    /// zorunlu (§9.5). <c>ForJsonSchema&lt;T&gt;</c> tek başına ek alanları kapatmadığı için dönüşüm açıkça uygulanır.
    /// </summary>
    public static readonly ChatResponseFormatJson YanitBicimi = ChatResponseFormat.ForJsonSchema(
        AIJsonUtilities.TransformSchema(
            AIJsonUtilities.CreateJsonSchema(typeof(OnAnamnezTaslagi), serializerOptions: Json),
            new AIJsonSchemaTransformOptions { DisallowAdditionalProperties = true, RequireAllProperties = true }),
        "on_anamnez", "Poliklinik ön anamnez taslağı ve bölüm önerisi");

    public string JsonOlarak() => JsonSerializer.Serialize(this, Json);
}

public sealed record KaynakMaddesi(string Madde, int SohbetAdimi);
