// Minimal Umbraco 17.x host that project-references the Analyzer RCL
// + the Customizer sibling. Intended for local dev (run via
// aspire/Analyzer.AppHost which provides the SQL Server container) or
// for manual verification of slice 001 acceptance scenarios per
// specs/001-package-skeleton/quickstart.md.

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Aspire ConnectionStrings (auto-injected from the AppHost via env vars)
// will populate "ConnectionStrings:umbracoDbDSN" — Umbraco reads it
// from IConfiguration as part of its standard install bootstrap.

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();

// Partial Program type so integration tests can target it via
// WebApplicationFactory<Program> later if needed (mirrors Customizer's
// host pattern for slice-002+ integration testing).
public partial class Program { }
