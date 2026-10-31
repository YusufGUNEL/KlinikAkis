using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading.RateLimiting;
using KlinikAkis.Api.Endpointler;
using KlinikAkis.Api.Gercekzaman;
using KlinikAkis.Api.Kimlik;
using KlinikAkis.Infrastructure.Ai;
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

// Render gibi platformlar dinlenecek portu PORT değişkeniyle verir.
if (Environment.GetEnvironmentVariable("PORT") is { Length: > 0 } port)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

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
    // Tarayıcı WebSocket'te başlık gönderemez; token yalnızca hub yolunda sorgu parametresinden okunur.
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = baglam =>
        {
            var token = baglam.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && baglam.HttpContext.Request.Path.StartsWithSegments(KlinikHub.Yol))
                baglam.Token = token;
            return Task.CompletedTask;
        },
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
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));
builder.Services.AddSingleton<IOlayYayinci, OlayYayinci>();
// e-Nabız gönderim kutusu (simülasyon, SPEC §5.2): saniyede bir bekleyen paketleri işler.
builder.Services.AddHostedService<OutboxIsleyici>();
// Her LLM çağrısı tek AiHatti üzerinden (SPEC §9.1). AI:Provider yoksa Fake: demo ağsız ve ücretsiz çalışır.
var aiAyar = builder.Services.AddKlinikAi(builder.Configuration);
// LLM uçları herkese açık URL'de: IP başına dakikada 10 istek, aşımda 429 ProblemDetails (SPEC §9.9).
var dakikadaIstek = builder.Configuration.GetValue("AI:DakikadaIstek", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AiHizSiniri.Politika, baglam => RateLimitPartition.GetFixedWindowLimiter(
        baglam.Connection.RemoteIpAddress?.ToString() ?? "bilinmiyor",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = dakikadaIstek, Window = TimeSpan.FromMinutes(1) }));
    o.OnRejected = (baglam, ct) => new ValueTask(TypedResults.Problem(
            detail: "Çok fazla istek gönderildi; bir dakika sonra yeniden deneyin.", statusCode: StatusCodes.Status429TooManyRequests)
        .ExecuteAsync(baglam.HttpContext));
});

var app = builder.Build();

if (aiAyar.Uyari is { } aiUyarisi)
    app.Logger.LogWarning("{Uyari}", aiUyarisi);
app.Logger.LogInformation("AI sağlayıcı: {Saglayici} · model: {Model} · epikriz modeli: {EpikrizModel} · günlük limit: {Limit}",
    aiAyar.Saglayici, aiAyar.Model, aiAyar.EpikrizModel ?? aiAyar.Model, aiAyar.GunlukLimit);
if (geciciAnahtar)
    app.Logger.LogWarning("Jwt:Key tanımlı değil; geçici anahtar üretildi. Kalıcı anahtar için: dotnet user-secrets set \"Jwt:Key\" <en az 32 karakter>");

// Şema EnsureCreated ile kurulur, veritabanı boşsa seed yüklenir (SPEC §5.3; migration yok).
using (var kapsam = app.Services.CreateScope())
    await kapsam.ServiceProvider.GetRequiredService<DemoSifirlayici>()
        .HazirlaAsync(app.Configuration.GetValue<bool>("Demo:SemaYenidenKur"));

app.UseExceptionHandler();
app.UseStatusCodePages();
// Yayında Angular derlemesi wwwroot'tan aynı origin'de sunulur (CORS gerekmez). Yerelde wwwroot yok: ng serve.
var arayuzVar = app.Environment.WebRootPath is { } kok && File.Exists(Path.Combine(kok, "index.html"));
if (arayuzVar)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

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
    .MapHastaEndpointleri()
    .MapRandevuEndpointleri()
    .MapOnAnamnezEndpointleri()
    .MapTahlilEndpointleri()
    .MapMuayeneEndpointleri()
    .MapTvEndpointleri()
    .MapOutboxEndpointleri();

app.MapHub<KlinikHub>(KlinikHub.Yol).RequireAuthorization();

if (arayuzVar)
{
    // Bilinmeyen /api yolu 404 kalsın; geri kalan her şey Angular rotasıdır (/doktor, /tv/... yenilenince).
    app.MapFallback("/api/{**yol}", () => TypedResults.NotFound());
    app.MapFallbackToFile("index.html");
}

app.Run();

public sealed record SaglikYaniti(string Durum, DateTime Zaman);

public partial class Program;
