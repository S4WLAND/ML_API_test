using dotenv.net;
using Microsoft.EntityFrameworkCore;
using MLIntegration.Data;
using MLIntegration.Helpers;
using MLIntegration.Services;

// ✅ 1. Cargar el archivo .env al inicio
DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

// ✅ 2. Leer todas las variables de entorno
var secretKey   = Environment.GetEnvironmentVariable("SECRET_KEY");
var appId       = Environment.GetEnvironmentVariable("APP_ID");
var dbHost      = Environment.GetEnvironmentVariable("DB_HOST");
var dbPort      = Environment.GetEnvironmentVariable("DB_PORT");
var dbName      = Environment.GetEnvironmentVariable("DB_NAME");
var dbUser      = Environment.GetEnvironmentVariable("DB_USER");
var dbPassword  = Environment.GetEnvironmentVariable("DB_PASSWORD");

// ✅ 3. Construir la cadena de conexión a MySQL
var connectionString =
    $"Server={dbHost};" +
    $"Port={dbPort};" +
    $"Database={dbName};" +
    $"User={dbUser};" +
    $"Password={dbPassword};";

Console.WriteLine("🔐 Variables cargadas correctamente:");
Console.WriteLine($" - DB: {dbName}@{dbHost}:{dbPort}");
Console.WriteLine($" - APP_ID: {appId}");
Console.WriteLine($" - SECRET_KEY: {secretKey.Substring(0, Math.Min(6, secretKey.Length))}***");

// ✅ 4. Registrar DbContext con MySQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// ✅ 5. Registrar HttpClient y servicios personalizados
builder.Services.AddHttpClient<MercadoLibreHelper>();
builder.Services.AddScoped<IMLTokenService, MLTokenService>();
builder.Services.AddScoped<MercadoLibreHelper>();
builder.Services.AddHostedService<MLTokenRefreshService>();

// ✅ 6. Controladores y Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "MercadoLibre Integration API", Version = "v1" });
});

var app = builder.Build();

// ✅ 7. Configuración del entorno
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

Console.WriteLine("✅ API iniciada");
app.Run();