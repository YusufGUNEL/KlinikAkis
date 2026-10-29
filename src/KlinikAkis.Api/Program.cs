using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using KlinikAkis.Api.Endpointler;
using KlinikAkis.Infrastructure.Seed;
using KlinikAkis.Infrastructure.Veri;
using Microsoft.AspNetCore.Http.HttpResults;
using Scalar.AspNetCore;

// Sunucu kültürden bağımsız çalışır; biçimlendirme istemcinin işidir (SPEC §10.5).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
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

var app = builder.Build();

// Şema EnsureCreated ile kurulur, veritabanı boşsa seed yüklenir (SPEC §5.3; migration yok).
using (var kapsam = app.Services.CreateScope())
    await kapsam.ServiceProvider.GetRequiredService<DemoSifirlayici>().HazirlaAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

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
