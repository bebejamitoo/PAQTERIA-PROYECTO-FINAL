using PAQTERIA.Server.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-tracking", context =>
    {
        var remoteIp = context.Connection.RemoteIpAddress;
        var ip = remoteIp is null
            ? "unknown"
            : remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4().ToString() : remoteIp.ToString();

        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Demasiadas consultas de rastreo. Inténtalo de nuevo en un minuto." },
            cancellationToken);
    };
});
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileDevelopment", policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddSingleton<PackageDb>();
builder.Services.AddSingleton<EntregaDb>();
builder.Services.AddSingleton<NotificacionDb>();
builder.Services.AddSingleton<IncidentDb>();
builder.Services.AddSingleton<DistributionCenterDb>();
builder.Services.AddSingleton<RouteDb>();
builder.Services.AddSingleton<AuthDb>();
builder.Services.AddHttpClient<OsrmClient>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Paqteria.Admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
             if (context.Request.Path.StartsWithSegments("/api"))
             {
                 context.Response.StatusCode = StatusCodes.Status401Unauthorized;
             }
             else
             {
                 context.Response.Redirect(context.RedirectUri);
             }
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
             if (context.Request.Path.StartsWithSegments("/api"))
             {
                 context.Response.StatusCode = StatusCodes.Status403Forbidden;
             }
             else
             {
                 context.Response.Redirect(context.RedirectUri);
             }
            return Task.CompletedTask;
        };
    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.GetRequiredService<PackageDb>().InitializeAsync();
await app.Services.GetRequiredService<DistributionCenterDb>().InitializeAsync();
await app.Services.GetRequiredService<IncidentDb>().InitializeAsync();
await app.Services.GetRequiredService<RouteDb>().InitializeAsync();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new { message = "Ocurrió un error interno al procesar la solicitud." });
        return;
    }

    await context.Response.WriteAsync("Ocurrió un error interno al procesar la solicitud.");
}));

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapStaticAssets();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseCors("MobileDevelopment");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapFallback("/api/{*path}", () => Results.NotFound(new { message = "La ruta de API no existe." }));
app.MapFallbackToFile("/index.html");

//admin @rumboenvios.comapp.Run();
app.Run();
