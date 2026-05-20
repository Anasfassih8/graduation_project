using Traffic.API.Hubs;
using Traffic.API.Services;

var builder = WebApplication.CreateBuilder(args);

// ================= SERVICES =================

// Enable controllers (for your API endpoints)
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHostedService<TrafficPushService>();

// Enable CORS (so your dashboard can call the API)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(
        policy => policy.SetIsOriginAllowed(_ => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials());//required for signalR
});

var app = builder.Build();

// ================= MIDDLEWARE =================

// Enable HTTPS redirection (optional but good practice)
app.UseHttpsRedirection();

// Enable CORS
app.UseCors();

// Map controller routes (VERY IMPORTANT)
app.MapControllers();

app.MapHub<TrafficHub>("/trafficHub");

app.Run();