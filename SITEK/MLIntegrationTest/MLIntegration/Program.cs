using Microsoft.EntityFrameworkCore;
using MLIntegration.Data;
using MLIntegration.Helpers;
using MLIntegration.Services;

var builder = WebApplication.CreateBuilder(args);

// Configurar DbContext con MySQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// Registrar HttpClient
builder.Services.AddHttpClient<MercadoLibreHelper>();

// Registrar servicios
builder.Services.AddScoped<IMLTokenService, MLTokenService>();
builder.Services.AddScoped<MercadoLibreHelper>();
builder.Services.AddHostedService<MLTokenRefreshService>();

// Controladores y Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "MercadoLibre Integration API", Version = "v1" });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => 
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ML API v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

Console.WriteLine("✅ API iniciada: http://localhost:5000");
app.Run();