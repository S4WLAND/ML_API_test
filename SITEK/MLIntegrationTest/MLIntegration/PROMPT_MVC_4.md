# 🔄 PROMPT PARA CONVERTIR .NET 6+ → ASP.NET MVC 4

## 📋 CONTEXTO

Tengo una integración con MercadoLibre API desarrollada en **.NET 6+ Web API** que funciona correctamente con **MySQL**. Necesito convertirla a **ASP.NET MVC 4 (.NET Framework 4.5)** con **SQL Server** para implementación en proyecto legacy.

---

## 🎯 ESPECIFICACIONES DEL PROYECTO TARGET

### Plataforma
- **Framework**: ASP.NET MVC 4 (.NET Framework 4.5)
- **Base de datos**: SQL Server (cambiar de MySQL)
- **IDE**: Visual Studio 2013+ (compatible con MVC 4)
- **ORM**: Entity Framework 6.x (no EF Core)
- **API**: ASP.NET Web API 2 (no Minimal API)

### Arquitectura
- Patrón Repository + Service Layer
- Dependency Injection con Unity o Ninject
- Async/await donde sea posible (limitado en .NET 4.5)
- Logging con log4net o NLog
- Background tasks con Hangfire o Quartz.NET

---

## 🔧 COMPONENTES A CONVERTIR

### 1. **Program.cs (Minimal API) → Global.asax + WebApiConfig**

**De** (.NET 6):
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));
```

**A** (MVC 4):
- `Global.asax.cs`: Inicialización de la aplicación
- `App_Start/WebApiConfig.cs`: Configuración de rutas API
- `App_Start/UnityConfig.cs`: Dependency Injection
- `Web.config`: ConnectionStrings y configuración

**Cambios necesarios**:
- ✅ Configurar DbContext con SQL Server (Entity Framework 6)
- ✅ Registrar dependencias en contenedor IoC
- ✅ Configurar rutas Web API
- ✅ Configurar logging

---

### 2. **Entity Framework Core → Entity Framework 6**

**Cambios de paquetes**:
```xml
<!-- De: -->
<PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="7.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="7.0.0" />

<!-- A: -->
<PackageReference Include="EntityFramework" Version="6.4.4" />
<PackageReference Include="EntityFramework.SqlServer" Version="6.4.4" />
```

**DbContext**:
```csharp
// MVC 4 - ApplicationDbContext.cs
using System.Data.Entity;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext() : base("DefaultConnection") { }
    
    public DbSet<MLToken> MLTokens { get; set; }
    public DbSet<MLProduct> MLProducts { get; set; }
    
    protected override void OnModelCreating(DbModelBuilder modelBuilder)
    {
        // Configuración de entidades (sintaxis diferente)
        modelBuilder.Entity<MLToken>()
            .ToTable("MLTokens")
            .HasKey(e => e.Id);
    }
}
```

**Migraciones**:
```powershell
# En Package Manager Console (Visual Studio)
Enable-Migrations
Add-Migration InitialCreate
Update-Database
```

---

### 3. **Controllers - Web API vs Web API 2**

**Cambios principales**:
```csharp
// MVC 4 - ApiController hereda de diferente clase
using System.Web.Http; // NO System.AspNetCore.Mvc

[RoutePrefix("api/mercadolibre")]
public class MercadoLibreController : ApiController // ApiController, no ControllerBase
{
    private readonly IMercadoLibreHelper _mlHelper;
    
    // Constructor injection con Unity/Ninject
    public MercadoLibreController(IMercadoLibreHelper mlHelper)
    {
        _mlHelper = mlHelper;
    }
    
    // Rutas con atributos (Web API 2)
    [HttpGet]
    [Route("auth/url")]
    public IHttpActionResult GetAuthUrl(int userId) // IHttpActionResult, no IActionResult
    {
        var authUrl = _mlHelper.GetAuthorizationUrl(GetRedirectUri());
        return Ok(new { authorizationUrl = authUrl });
    }
    
    [HttpPost]
    [Route("items")]
    public async Task<IHttpActionResult> CreateItem(MLItemRequest request, int userId = 1)
    {
        try
        {
            var result = await _mlHelper.CreateItemAsync(request, userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
    
    private string GetRedirectUri()
    {
        return Request.RequestUri.GetLeftPart(UriPartial.Authority) + "/api/mercadolibre/callback";
    }
}
```

---

### 4. **HttpClient → Usar HttpClient en .NET 4.5**

**Consideraciones**:
```csharp
// .NET 4.5 tiene HttpClient pero con diferencias
using System.Net.Http;

public class MercadoLibreHelper
{
    private readonly HttpClient _httpClient;
    
    public MercadoLibreHelper()
    {
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri("https://api.mercadolibre.com");
    }
    
    // async/await funciona pero con limitaciones
    public async Task<MLAuthResponse> ExchangeCodeForTokenAsync(string code, string redirectUri, int userId)
    {
        var parameters = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "client_id", ConfigurationManager.AppSettings["ML:ClientId"] },
            { "client_secret", ConfigurationManager.AppSettings["ML:ClientSecret"] },
            { "code", code },
            { "redirect_uri", redirectUri }
        };
        
        var content = new FormUrlEncodedContent(parameters);
        var response = await _httpClient.PostAsync("/oauth/token", content);
        
        response.EnsureSuccessStatusCode();
        
        var jsonString = await response.Content.ReadAsStringAsync();
        
        // Usar Json.NET (Newtonsoft.Json) en lugar de System.Text.Json
        return JsonConvert.DeserializeObject<MLAuthResponse>(jsonString);
    }
}
```

---

### 5. **BackgroundService → Hangfire/Quartz.NET**

**De** (.NET 6):
```csharp
public class MLTokenRefreshService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshExpiringTokensAsync();
            await Task.Delay(TimeSpan.FromHours(5), stoppingToken);
        }
    }
}
```

**A** (MVC 4 con Hangfire):
```csharp
// Instalar: Install-Package Hangfire.Core
// Global.asax.cs
protected void Application_Start()
{
    // Configurar Hangfire
    GlobalConfiguration.Configuration.UseSqlServerStorage("DefaultConnection");
    
    // Iniciar servidor
    var server = new BackgroundJobServer();
    
    // Job recurrente cada 5 horas
    RecurringJob.AddOrUpdate(
        "refresh-ml-tokens",
        () => RefreshMLTokens(),
        "0 */5 * * *" // Cron: cada 5 horas
    );
}

public static void RefreshMLTokens()
{
    var tokenService = DependencyResolver.Current.GetService<IMLTokenService>();
    var mlHelper = DependencyResolver.Current.GetService<IMercadoLibreHelper>();
    
    var tokensToRefresh = tokenService.GetTokensExpiringSoon(30);
    
    foreach (var token in tokensToRefresh)
    {
        try
        {
            mlHelper.RefreshAccessTokenAsync(token.UserId, token.RefreshToken).Wait();
        }
        catch (Exception ex)
        {
            // Log error
        }
    }
}
```

---

### 6. **Dependency Injection - Unity Container**

**App_Start/UnityConfig.cs**:
```csharp
using Microsoft.Practices.Unity;
using System.Web.Http;
using Unity.WebApi;

public static class UnityConfig
{
    public static void RegisterComponents()
    {
        var container = new UnityContainer();
        
        // Registrar DbContext (por request)
        container.RegisterType<ApplicationDbContext>(new HierarchicalLifetimeManager());
        
        // Registrar servicios
        container.RegisterType<IMLTokenService, MLTokenService>();
        container.RegisterType<IMercadoLibreHelper, MercadoLibreHelper>();
        
        // Resolver para Web API
        GlobalConfiguration.Configuration.DependencyResolver = new UnityDependencyResolver(container);
    }
}

// Global.asax.cs
protected void Application_Start()
{
    UnityConfig.RegisterComponents();
    WebApiConfig.Register(GlobalConfiguration.Configuration);
}
```

---

### 7. **Logging - log4net**

**Web.config**:
```xml
<configSections>
  <section name="log4net" type="log4net.Config.Log4NetConfigurationSectionHandler, log4net" />
</configSections>

<log4net>
  <appender name="FileAppender" type="log4net.Appender.RollingFileAppender">
    <file value="Logs/MLIntegration.log" />
    <appendToFile value="true" />
    <maximumFileSize value="10MB" />
    <maxSizeRollBackups value="5" />
    <layout type="log4net.Layout.PatternLayout">
      <conversionPattern value="%date [%thread] %-5level %logger - %message%newline" />
    </layout>
  </appender>
  <root>
    <level value="INFO" />
    <appender-ref ref="FileAppender" />
  </root>
</log4net>
```

**Global.asax.cs**:
```csharp
protected void Application_Start()
{
    log4net.Config.XmlConfigurator.Configure();
}
```

---

### 8. **Connection String - MySQL → SQL Server**

**Web.config**:
```xml
<connectionStrings>
  <!-- DE (MySQL): -->
  <!-- <add name="DefaultConnection" 
       connectionString="Server=localhost;Port=3306;Database=ml_integration;User=root;Password=pass;" 
       providerName="MySql.Data.MySqlClient" /> -->
  
  <!-- A (SQL Server): -->
  <add name="DefaultConnection" 
       connectionString="Server=localhost;Database=MLIntegration;User Id=sa;Password=YourPassword;TrustServerCertificate=True;" 
       providerName="System.Data.SqlClient" />
</connectionStrings>
```

---

## 📦 PAQUETES NUGET NECESARIOS (MVC 4)

```xml
<packages>
  <!-- Core MVC 4 -->
  <package id="Microsoft.AspNet.Mvc" version="4.0.40804.0" />
  <package id="Microsoft.AspNet.WebApi" version="4.0.30506.0" />
  <package id="Microsoft.AspNet.WebApi.Client" version="5.2.9" />
  <package id="Microsoft.AspNet.WebApi.Core" version="5.2.9" />
  <package id="Microsoft.AspNet.WebApi.WebHost" version="5.2.9" />
  
  <!-- Entity Framework 6 -->
  <package id="EntityFramework" version="6.4.4" />
  
  <!-- JSON -->
  <package id="Newtonsoft.Json" version="13.0.1" />
  
  <!-- DI -->
  <package id="Unity" version="5.11.10" />
  <package id="Unity.WebAPI" version="5.4.0" />
  
  <!-- Background Jobs -->
  <package id="Hangfire.Core" version="1.7.35" />
  <package id="Hangfire.SqlServer" version="1.7.35" />
  
  <!-- Logging -->
  <package id="log4net" version="2.0.15" />
</packages>
```

---

## ✅ CHECKLIST DE CONVERSIÓN

- [ ] Crear proyecto MVC 4 en Visual Studio
- [ ] Instalar paquetes NuGet necesarios
- [ ] Configurar Web.config con SQL Server
- [ ] Convertir DbContext a Entity Framework 6
- [ ] Adaptar modelos (quitar Data Annotations de EF Core)
- [ ] Convertir Controllers a ApiController
- [ ] Reemplazar System.Text.Json por Newtonsoft.Json
- [ ] Configurar Unity para DI
- [ ] Implementar Hangfire para background tasks
- [ ] Configurar log4net
- [ ] Crear migraciones EF6
- [ ] Probar autenticación OAuth
- [ ] Probar operaciones CRUD
- [ ] Verificar renovación automática de tokens

---

## 🚨 DIFERENCIAS CRÍTICAS A CONSIDERAR

| Característica | .NET 6 | ASP.NET MVC 4 |
|---------------|--------|---------------|
| **Async/await** | Completo | Limitado (no en constructores) |
| **HttpClient** | Optimizado | Funciona pero requiere dispose |
| **JSON** | System.Text.Json | Newtonsoft.Json |
| **DI nativo** | Sí | No (requiere Unity/Ninject) |
| **Background tasks** | IHostedService | Hangfire/Quartz |
| **EF** | EF Core 7 | EF 6.4 |
| **Logging** | ILogger | log4net/NLog |
| **Configuración** | appsettings.json | Web.config |

---

## 🎯 ENTREGABLES ESPERADOS

Por favor, genera:

1. **Estructura completa de carpetas** para MVC 4
2. **Global.asax.cs** con inicialización
3. **App_Start/WebApiConfig.cs** con rutas
4. **App_Start/UnityConfig.cs** con DI
5. **Web.config** completo
6. **Models/** adaptados a EF6
7. **Controllers/** adaptados a ApiController
8. **Helpers/MercadoLibreHelper.cs** con HttpClient compatible
9. **Services/** con Repository pattern
10. **Scripts SQL** para crear tablas en SQL Server
11. **Guía de deployment** en IIS
12. **Guía de testing** con Postman

---

## 📝 NOTAS ADICIONALES

- El código debe ser **thread-safe**
- Implementar **manejo de errores robusto**
- Agregar **comentarios XML** en métodos públicos
- Seguir **principios SOLID**
- Código preparado para **escala empresarial**
- Incluir **unit tests** (opcional)

---

**¿Entendiste el contexto? ¿Necesitas el código fuente .NET 6 completo para iniciar la conversión?**