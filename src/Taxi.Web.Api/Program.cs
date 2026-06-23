using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Taxi.Application;
using Taxi.Infrastructure;
using Taxi.Infrastructure.Identity;
using Taxi.Infrastructure.Persistence;
using Taxi.Web.Api.Endpoints;
using Taxi.Web.Api.Middleware;
using Taxi.Web.Api.OpenApi;
using Taxi.Application.Realtime;
using Taxi.Web.Api.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.AddNpgsqlDbContext<AppDbContext>(
    "taxidb",
    configureDbContextOptions: options => options
        .UseNpgsql(npgsql => npgsql.UseNetTopologySuite())
        .UseSnakeCaseNamingConvention());

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddAuthorization();
builder.Services.AddEndpoints();
builder.Services.AddSignalR();
builder.Services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(
                "https://taxi-djibouti.vercel.app",
                "http://localhost:5173"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedRolesAsync(scope.ServiceProvider);
}

app.MapDefaultEndpoints();

//if (app.Environment.IsDevelopment())
//{
//    app.MapOpenApi();
 //   app.MapScalarApiReference();
//}

app.UseAuthentication();
app.UseAuthorization();
app.UseCors("Frontend");
app.MapEndpoints();
app.MapHub<RideHub>("/hubs/ride");

app.Run();
