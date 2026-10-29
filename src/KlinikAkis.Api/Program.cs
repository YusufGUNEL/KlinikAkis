using System.Globalization;
using Scalar.AspNetCore;

// Sunucu kültürden bağımsız çalışır; biçimlendirme istemcinin işidir (SPEC §10.5).
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

var api = app.MapGroup("/api");

// DB ping Aşama 1'de eklenir.
api.MapGet("/health", () => TypedResults.Ok(new SaglikYaniti("ok", DateTime.UtcNow)))
    .WithName("Saglik");

app.Run();

public sealed record SaglikYaniti(string Durum, DateTime Zaman);

public partial class Program;
