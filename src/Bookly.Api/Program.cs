using Asp.Versioning;
using Bookly.Core;
using Bookly.Scheduling;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Modules register their own services; the API project is just the composition root.
builder.Services.AddCoreModule(builder.Configuration);
builder.Services.AddSchedulingModule(builder.Configuration);

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("BooklyDb")!,
        name: "postgres",
        tags: ["ready"]);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Dev only: prod runs migrations as an explicit deploy step (ADR-012, M10).
    await app.Services.ApplyCoreMigrationsAsync();
}

var v1 = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1)).Build();

app.MapGet("/api/v{version:apiVersion}/ping",
        () => Results.Ok(new { status = "pong", serverTimeUtc = DateTime.UtcNow }))
    .WithApiVersionSet(v1)
    .WithName("Ping");

// Liveness: process is up, no dependencies checked.
app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });

// Readiness: dependencies (Postgres) reachable.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

// Exposes the implicit Program class to WebApplicationFactory (integration tests, M5).
public partial class Program;
