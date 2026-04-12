var builder = WebApplication.CreateBuilder(args);

// ================= SERVICES =================

// Enable controllers (for your API endpoints)
builder.Services.AddControllers();

// Enable CORS (so your dashboard can call the API)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy => policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader());
});

var app = builder.Build();

// ================= MIDDLEWARE =================

// Enable HTTPS redirection (optional but good practice)
app.UseHttpsRedirection();

// Enable CORS
app.UseCors("AllowAll");

// Map controller routes (VERY IMPORTANT)
app.MapControllers();

app.Run();