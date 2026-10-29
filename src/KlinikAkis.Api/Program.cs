using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using KlinikAkis.Api.Endpointler;
using KlinikAkis.Api.Kimlik;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

// Sunucu kültürden bağımsız çalışır; biçimlendirme istemcinin işidir (SPEC §10.5).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
// Scalar'da "Authorize" alanı çıksın: /demo/login'den alınan token yapıştırılıp korumalı uçlar denenebilir.
builder.Services.AddOpenApi(o => o.AddDocumentTransformer((belge, _, _) =>
{
    belge.Components ??= new OpenApiComponents();
    belge.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
    {
        ["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            In = ParameterLocation.Header,
            BearerFormat = "JWT",
            Description = "POST /api/demo/login yanıtındaki token",
        },
    };
    foreach (var islem in belge.Paths.Values.SelectMany(yol => yol.Operations ?? []))
    {
        islem.Value.Security ??= [];
        islem.Value.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", belge)] = [] });
    }
    return Task.CompletedTask;
}));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Türkçe harfler "ş" gibi kaçırılmasın; ham yanıt ve Scalar okunur kalsın.
    o.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddKlinikVeri(
    builder.Configuration["Db:Provider"] ?? "Sqlite",
    builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default tanımlı değil."));

// Demo JWT (SPEC §7). Anahtar repoda değil: user-secrets / ortam değişkeni (Jwt__Key).
// Development'ta yoksa geçici anahtar üretilir; yeniden başlatınca eski token'lar geçersiz olur.
var jwtAnahtari = builder.Configuration["Jwt:Key"];
var geciciAnahtar = string.IsNullOrEmpty(jwtAnahtari);
if (geciciAnahtar)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Jwt:Key tanımlı değil (ortam değişkeni Jwt__Key).");
    jwtAnahtari = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
var imzaAnahtari = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtAnahtari!));
if (imzaAnahtari.KeySize < 256)
    throw new InvalidOperationException("Jwt:Key en az 32 bayt olmalı (HMAC-SHA256).");

builder.Services.AddSingleton(sp => new DemoTokenUretici(imzaAnahtari, sp.GetRequiredService<TimeProvider>()));
builder.Services.AddAuthentication().AddJwtBearer(o =>
{
    // "rol", "ad" gibi kısa claim adları olduğu gibi kalsın (ClaimTypes.* URI'lerine eşlenmesin).
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = DemoTokenUretici.Yayinci,
        ValidAudience = DemoTokenUretici.Yayinci,
        IssuerSigningKey = imzaAnahtari,
        NameClaimType = DemoClaim.Ad,
        RoleClaimType = DemoClaim.Rol,
    };
});
// Süre kontrolü varsayılan olarak DateTime.UtcNow kullanır; token'ı üreten saatle (TimeProvider) aynı saate bağlanır.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TimeProvider>((o, zaman) => o.TokenValidationParameters.LifetimeValidator = (nbf, exp, _, p) =>
    {
        var simdi = zaman.GetUtcNow().UtcDateTime;
        return exp is { } bitis && bitis > simdi - p.ClockSkew && (nbf is null || nbf <= simdi + p.ClockSkew);
    });
builder.Services.AddAuthorizationBuilder().AddDemoPolitikalari();

var app = builder.Build();

if (geciciAnahtar)
    app.Logger.LogWarning("Jwt:Key tanımlı değil; geçici anahtar üretildi. Kalıcı anahtar için: dotnet user-secrets set \"Jwt:Key\" <en az 32 karakter>");

// Şema EnsureCreated ile kurulur, veritabanı boşsa seed yüklenir (SPEC §5.3; migration yok).
using (var kapsam = app.Services.CreateScope())
    await kapsam.ServiceProvider.GetRequiredService<DemoSifirlayici>().HazirlaAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

var api = app.MapGroup("/api");

api.MapGet("/health", async Task<Results<Ok<SaglikYaniti>, JsonHttpResult<SaglikYaniti>>> (
        KlinikDbContext db, TimeProvider zaman, CancellationToken ct) =>
    {
        var yanit = new SaglikYaniti("ok", zaman.GetUtcNow().UtcDateTime);
        return await db.Database.CanConnectAsync(ct)
            ? TypedResults.Ok(yanit)
            : TypedResults.Json(yanit with { Durum = "veritabani-erisilemiyor" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    })
    .WithName("Saglik");

api.MapDemoEndpointleri()
    .MapBransSlotEndpointleri()
    .MapHastaEndpointleri();

app.Run();

public sealed record SaglikYaniti(string Durum, DateTime Zaman);

public partial class Program;
