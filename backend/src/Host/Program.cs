using Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdentityModule(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapIdentityEndpoints();

app.MapFallbackToFile("index.html");

app.Run();
