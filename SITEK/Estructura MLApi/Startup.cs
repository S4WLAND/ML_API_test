using Microsoft.EntityFrameworkCore;
using YourProject.Data;
using YourProject.Helpers;
using YourProject.Services;

var builder = WebApplication.CreateBuilder(args);

// Configurar DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Registrar HttpClient para MercadoLibreHelper
builder.Services.AddHttpClient<MercadoLibreHelper>();

// Registrar servicios
builder.Services.AddScoped<IMLTokenService, MLTokenService>();
builder.Services.AddScoped<MercadoLibreHelper>();

// Registrar el servicio de background para renovación automática de tokens
builder.Services.AddHostedService<MLTokenRefreshService>();

// Otros servicios
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configuración del pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();