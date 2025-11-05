# 🚀 MLIntegration - Sistema de Integración con MercadoLibre API

## 📋 Descripción del Proyecto

Sistema backend en .NET 8 que integra con la API de MercadoLibre para gestionar publicaciones de productos de forma automática. Permite sincronizar, crear, actualizar y eliminar productos en MercadoLibre manteniendo coherencia con un inventario local.

### Características principales:
- ✅ Autenticación OAuth 2.0 con renovación automática de tokens
- ✅ Sincronización masiva de productos (soporta paginación para >1000 items)
- ✅ CRUD completo de publicaciones
- ✅ Background service para renovación de tokens
- ✅ Soft delete de productos
- ✅ Rate limiting inteligente (5 req/s)

---

## 🏗️ Arquitectura del Sistema

### Stack Tecnológico
```
- Framework: .NET 8 (ASP.NET Core Web API)
- Base de Datos: MySQL 8.0 (via Entity Framework Core)
- ORM: Entity Framework Core 8
- API Externa: MercadoLibre API REST
- Autenticación: OAuth 2.0
- Variables de Entorno: dotenv.net
```

### Estructura del Proyecto
```
MLIntegration/
├── Controllers/
│   └── MercadoLibreController.cs      # Endpoints REST
├── Helpers/
│   └── MercadoLibreHelper.cs          # Lógica de negocio ML API
├── Services/
│   ├── IMLTokenService.cs             # Interfaz servicio tokens
│   ├── MLTokenService.cs              # Implementación servicio tokens
│   └── MLTokenRefreshService.cs       # Background service renovación
├── Models/MercadoLibre/
│   ├── MLToken.cs                     # Entidad tokens OAuth
│   ├── MLProduct.cs                   # Entidad productos ML
│   ├── MLAuthResponse.cs              # DTO respuesta OAuth
│   └── MLItemRequest.cs               # DTO creación productos
├── Data/
│   └── ApplicationDbContext.cs        # Contexto EF Core
├── Migrations/                        # Migraciones EF Core
├── .env                               # Variables de entorno
└── Program.cs                         # Configuración y bootstrap
```

---

## 🔄 Flujos Principales

### 1. Flujo de Autenticación OAuth (Setup Inicial)

```
┌─────────────────────────────────────────────────────────┐
│ SETUP MANUAL (Una sola vez)                            │
└─────────────────────────────────────────────────────────┘
    ↓
1. Usuario obtiene refresh_token de ML manualmente
   GET https://auth.mercadolibre.com.ar/authorization
   → Autoriza app → Obtiene code
   → Intercambia code por tokens (curl)
   → Copia refresh_token
    ↓
2. Usuario hace seed en la API
   POST /api/mercadolibre/tokens/seed
   Body: { userId: 1, refreshToken: "TG-xxx" }
    ↓
3. Sistema guarda refresh_token en BD
   MLTokens table: { UserId=1, RefreshToken="TG-xxx", ExpiresAt=NOW }
    ↓
4. Sistema automático arranca
   ├─> Background service renueva cada 10 min (proactivo)
   └─> Endpoint GetValidAccessToken renueva inline (reactivo)
    ↓
✅ Sistema funciona automático por 6 meses
```

### 2. Flujo de Renovación Automática de Tokens

```
┌─────────────────────────────────────────────────────────┐
│ BACKGROUND SERVICE (MLTokenRefreshService)              │
│ Ejecuta cada 10 minutos                                │
└─────────────────────────────────────────────────────────┘
    ↓
Verifica tokens en BD
    ↓
¿Algún token expira en <15 min?
    ├─ NO  → Log: "✅ No hay tokens que renovar" → Sleep 10 min
    │
    └─ SÍ  → Para cada token:
              ↓
         POST https://api.mercadolibre.com/oauth/token
         Body: {
           grant_type: "refresh_token",
           client_id: ENV[APP_ID],
           client_secret: ENV[SECRET_KEY],
           refresh_token: "TG-xxx"
         }
              ↓
         ¿Respuesta 200 OK?
              ├─ SÍ → Actualiza MLTokens en BD
              │       ├─ AccessToken = nuevo
              │       ├─ RefreshToken = nuevo (rotación)
              │       ├─ ExpiresAt = NOW + 6h
              │       └─ RefreshCount++
              │
              └─ NO (401/400) → Log error
                                ├─ invalid_grant → Alert: "Reautorizar"
                                └─ Continúa con siguiente token

┌─────────────────────────────────────────────────────────┐
│ FALLBACK INLINE (GetValidAccessTokenAsync)             │
│ Ejecuta en cada request a ML API                       │
└─────────────────────────────────────────────────────────┘
    ↓
Usuario hace request: POST /api/mercadolibre/items
    ↓
GetValidAccessTokenAsync(userId):
    ↓
SELECT * FROM MLTokens WHERE UserId = 1
    ↓
¿Token expira en <15 min?
    ├─ NO  → Devuelve AccessToken (rápido)
    │
    └─ SÍ  → RefreshAccessTokenAsync() (mismo método que background)
             → Devuelve nuevo AccessToken
```

### 3. Flujo de Sincronización de Productos

```
┌─────────────────────────────────────────────────────────┐
│ USUARIO: POST /api/mercadolibre/sync/products          │
└─────────────────────────────────────────────────────────┘
    ↓
Controller → Helper.SyncAllUserProductsAsync(userId)
    ↓
┌─────────────────────────────────────────────────────────┐
│ PASO 1: Obtener TODOS los IDs                          │
└─────────────────────────────────────────────────────────┘
GetUserItemsAsync(userId):
    ↓
GET /users/{ML_USER_ID}/items/search?limit=50&status=active
Response: { paging: { total: 285 }, results: [...] }
    ↓
¿Total productos?
    ├─ ≤50    → Devuelve resultados (1 request)
    ├─ ≤1000  → Paginación con offset
    │          for(offset=0; offset<1000; offset+=50)
    │            GET /users/{ID}/items/search?offset={offset}&limit=50
    │
    └─ >1000  → Scroll API
               GET /users/{ID}/items/search?search_type=scan&limit=100
               Response: { scroll_id: "abc123", results: [...] }
               Loop:
                 GET /users/{ID}/items/search?scroll_id={scroll_id}&limit=100
                 Hasta que results.length == 0
    ↓
Resultado: List<string> itemIds = ["MLA1", "MLA2", ..., "MLA285"]

┌─────────────────────────────────────────────────────────┐
│ PASO 2: Obtener detalles en lotes con Multiget         │
└─────────────────────────────────────────────────────────┘
itemIds.Chunk(20) → batches = [batch1[20], batch2[20], ...]
    ↓
Para cada batch:
    ↓
GET /items?ids=MLA1,MLA2,...,MLA20
Response: [
  { code: 200, body: { id: "MLA1", price: 1000, ... } },
  { code: 200, body: { id: "MLA2", price: 2000, ... } },
  { code: 404, body: { error: "not_found" } }
]
    ↓
Para cada item en response:
    ↓
¿code == 200?
    ├─ SÍ → UpsertProductFromRemote(userId, item.body)
    │       ↓
    │   SELECT * FROM MLProducts WHERE ItemId = "MLA1"
    │       ↓
    │   ¿Existe?
    │       ├─ NO  → INSERT INTO MLProducts (...)
    │       │        Created++
    │       │
    │       └─ SÍ  → UPDATE MLProducts SET Price=..., UpdatedAt=NOW
    │                WHERE ItemId = "MLA1"
    │                Updated++
    │
    └─ NO  → Skipped++
             Errors.Add("MLA1: HTTP 404")
    ↓
await Task.Delay(200ms) // Rate limiting
    ↓
Next batch...

┌─────────────────────────────────────────────────────────┐
│ PASO 3: Retornar resultado                             │
└─────────────────────────────────────────────────────────┘
Response: {
  totalProducts: 285,
  productsCreated: 50,
  productsUpdated: 230,
  productsSkipped: 5,
  errors: ["MLA123: HTTP 404", ...]
}
```

### 4. Flujo de Creación de Producto

```
┌─────────────────────────────────────────────────────────┐
│ USUARIO: POST /api/mercadolibre/items                  │
│ Body: { familyName, categoryId, price, ... }           │
└─────────────────────────────────────────────────────────┘
    ↓
Controller → Helper.CreateItemAsync(request, userId)
    ↓
GetValidAccessTokenAsync(userId) → access_token
    ↓
POST https://api.mercadolibre.com/items
Authorization: Bearer {access_token}
Body: {
  "title": "Producto X",
  "category_id": "MLA123",
  "price": 1500,
  "available_quantity": 10,
  ...
}
    ↓
¿Response 200/201?
    ├─ SÍ → ML devuelve: { id: "MLA999", ... }
    │       ↓
    │   UpsertProductFromRemote(userId, response)
    │       ↓
    │   INSERT INTO MLProducts (ItemId="MLA999", ...)
    │       ↓
    │   Return: JSON del producto creado
    │
    └─ NO  → Throw HttpRequestException
             → Controller devuelve 400 BadRequest
```

### 5. Flujo de Actualización de Precio/Stock

```
┌─────────────────────────────────────────────────────────┐
│ USUARIO: PUT /api/mercadolibre/items/{itemId}/price    │
│ Body: { price: 2000, quantity: 5 }                     │
└─────────────────────────────────────────────────────────┘
    ↓
Controller → Helper.UpdatePriceAndQuantityAsync(itemId, price, qty, userId)
    ↓
GetValidAccessTokenAsync(userId) → access_token
    ↓
PUT https://api.mercadolibre.com/items/{itemId}
Authorization: Bearer {access_token}
Body: {
  "price": 2000,
  "available_quantity": 5
}
    ↓
¿Response 200?
    ├─ SÍ → ML devuelve item actualizado
    │       ↓
    │   UpsertProductFromRemote(userId, response)
    │       ↓
    │   UPDATE MLProducts 
    │   SET Price=2000, AvailableQuantity=5, UpdatedAt=NOW
    │   WHERE ItemId = {itemId}
    │       ↓
    │   Return: JSON actualizado
    │
    └─ NO  → Error y rollback
```

### 6. Flujo de Soft Delete

```
┌─────────────────────────────────────────────────────────┐
│ USUARIO: DELETE /api/mercadolibre/items/{itemId}       │
└─────────────────────────────────────────────────────────┘
    ↓
Controller → Helper.DeleteItemAsync(itemId, userId)
    ↓
PASO 1: Cerrar publicación
PUT /items/{itemId}
Body: { status: "closed" }
    ↓
await Task.Delay(2500ms) // Esperar propagación
    ↓
PASO 2: Marcar como deleted
PUT /items/{itemId}
Body: { deleted: "true" }
    ↓
PASO 3: Soft delete local
UPDATE MLProducts 
SET IsDeleted=true, DeletedAt=NOW, Status="closed"
WHERE ItemId = {itemId}
    ↓
✅ Producto eliminado (soft delete)
```

---

## 🗄️ Modelo de Datos

### Tabla: MLTokens

```sql
CREATE TABLE MLTokens (
    Id INT PRIMARY KEY AUTO_INCREMENT,
    UserId INT NOT NULL UNIQUE,
    AccessToken VARCHAR(255) NOT NULL,
    RefreshToken VARCHAR(255) NOT NULL,
    IssuedAt DATETIME NOT NULL,
    RefreshTokenIssuedAt DATETIME NULL,
    LastRefreshedAt DATETIME NULL,
    RefreshCount INT DEFAULT 0,
    ExpiresAt DATETIME NOT NULL,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    INDEX idx_userid (UserId)
);
```

**Propósito:** Almacenar tokens OAuth de MercadoLibre por usuario

**Ciclo de vida:**
- `AccessToken` expira cada 6 horas
- `RefreshToken` expira cada 180 días
- Background service renueva proactivamente

### Tabla: MLProducts

```sql
CREATE TABLE MLProducts (
    Id INT PRIMARY KEY AUTO_INCREMENT,
    UserId INT NOT NULL,
    ItemId VARCHAR(50) NOT NULL UNIQUE,
    FamilyName VARCHAR(255) NOT NULL,
    CategoryId VARCHAR(50) NOT NULL,
    Price DECIMAL(18,2) NOT NULL,
    AvailableQuantity INT NOT NULL,
    Status VARCHAR(50) NOT NULL,
    SubStatus TEXT NULL,
    IsDeleted BOOLEAN DEFAULT FALSE,
    DeletedAt DATETIME NULL,
    LastSync DATETIME NULL,
    CreatedAt DATETIME NOT NULL,
    UpdatedAt DATETIME NOT NULL,
    INDEX idx_itemid (ItemId),
    INDEX idx_userid_status (UserId, Status)
);
```

**Propósito:** Cache local de productos publicados en MercadoLibre

**Estados:**
- `Status`: active, paused, closed
- `IsDeleted`: Soft delete (refleja sub_status deleted de ML)

---

## 🔌 API Endpoints

### Tokens
```
POST   /api/mercadolibre/tokens/seed        # Setup inicial refresh_token
GET    /api/mercadolibre/tokens/status      # Estado de tokens
```

### Sincronización
```
POST   /api/mercadolibre/sync/products      # Sincronizar todos los productos
GET    /api/mercadolibre/sync/status        # Estado de sincronización local
```

### CRUD Productos
```
POST   /api/mercadolibre/items              # Crear producto
GET    /api/mercadolibre/items/{itemId}     # Obtener producto
PUT    /api/mercadolibre/items/{itemId}/price      # Actualizar precio/stock
PUT    /api/mercadolibre/items/{itemId}/pause      # Pausar publicación
PUT    /api/mercadolibre/items/{itemId}/activate   # Activar publicación
DELETE /api/mercadolibre/items/{itemId}     # Eliminar (soft delete)
```

---

## ⚙️ Configuración

### Variables de Entorno (.env)

```bash
# MercadoLibre Credentials
APP_ID=1234567890
SECRET_KEY=abc123xyz456
ML_USER_ID=1234567890

# Database
DB_HOST=localhost
DB_PORT=3306
DB_NAME=mlintegration
DB_USER=root
DB_PASSWORD=password
```

### appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "MercadoLibre": {
    "RefreshThresholdMinutes": 15
  }
}
```

### Program.cs - Servicios Registrados

```csharp
// DotEnv
DotEnv.Load();

// Database
services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// Services
services.AddScoped<IMLTokenService, MLTokenService>();
services.AddScoped<MercadoLibreHelper>();
services.AddHttpClient<MercadoLibreHelper>();

// Background Services
services.AddHostedService<MLTokenRefreshService>();
```

---

## 🚀 Ejecución del Proyecto

### 1. Setup inicial

```bash
# 1. Clonar proyecto
git clone <repo>
cd MLIntegration

# 2. Configurar .env
cp .env.example .env
nano .env  # Editar con tus credenciales

# 3. Restaurar dependencias
dotnet restore

# 4. Crear base de datos
dotnet ef database update

# 5. Ejecutar
dotnet run
```

### 2. Obtener refresh_token (manual)

```bash
# Paso 1: Abrir en navegador
https://auth.mercadolibre.com.ar/authorization?response_type=code&client_id={APP_ID}&redirect_uri=http://localhost:5000/callback

# Paso 2: Autorizar → copiar code de la URL

# Paso 3: Intercambiar code por tokens
curl -X POST https://api.mercadolibre.com/oauth/token \
  -d "grant_type=authorization_code" \
  -d "client_id={APP_ID}" \
  -d "client_secret={SECRET_KEY}" \
  -d "code={CODE}" \
  -d "redirect_uri=http://localhost:5000/callback"

# Respuesta: { "refresh_token": "TG-xxx", ... }
```

### 3. Seed en la API

```bash
POST http://localhost:5000/api/mercadolibre/tokens/seed
Content-Type: application/json

{
  "userId": 1,
  "refreshToken": "TG-xxx..."
}
```

### 4. Sincronizar productos

```bash
POST http://localhost:5000/api/mercadolibre/sync/products?userId=1
```

---

## 📊 Performance

### Sincronización de Productos

| Cantidad | Tiempo (sin Multiget) | Tiempo (con Multiget) | Mejora |
|----------|----------------------|----------------------|--------|
| 50       | ~20 seg              | ~3 seg               | 6.6x   |
| 100      | ~40 seg              | ~5 seg               | 8x     |
| 500      | ~3 min               | ~15 seg              | 12x    |
| 1000     | ~6 min               | ~30 seg              | 12x    |

### Rate Limiting

- **Paginación:** 200ms entre páginas (5 req/s)
- **Multiget:** 200ms entre lotes de 20 items
- **Renovación tokens:** 500ms entre usuarios

---

## 🔒 Seguridad

### Tokens OAuth
- ✅ Almacenados en BD (no en código)
- ✅ Renovación automática
- ✅ Logs de expiración
- ✅ Manejo de invalid_grant

### Variables Sensibles
- ✅ .env en .gitignore
- ✅ No hardcodeadas
- ✅ Leídas de Environment.GetEnvironmentVariable()

### Rate Limiting
- ✅ 200ms delay entre requests
- ✅ Manejo de errores 429
- ✅ Retry con backoff (implícito)

---

## 📈 Mejoras Futuras

### Funcionalidad
- [ ] Webhooks de MercadoLibre (notificaciones en tiempo real)
- [ ] Sincronización incremental (solo productos modificados)
- [ ] Multitenancy (múltiples cuentas ML)
- [ ] Gestión de imágenes
- [ ] Respuestas a preguntas

### Performance
- [ ] Cache con Redis
- [ ] Queue con RabbitMQ/Azure Service Bus
- [ ] Procesamiento en background con Hangfire
- [ ] Paginación en endpoints locales

### DevOps
- [ ] Docker y Docker Compose
- [ ] CI/CD con GitHub Actions
- [ ] Monitoring con Application Insights
- [ ] Health checks
- [ ] Swagger/OpenAPI documentation

---

## 📝 Notas Importantes

### Limitaciones de MercadoLibre API
- Máx 50 items por página en `/items/search`
- Máx 20 items por request en Multiget
- Máx 1000 items con offset (usar scroll para más)
- Rate limit: ~5 requests/segundo recomendado

### Gestión de Errores
- `401 Unauthorized` → Token expirado, renovar automáticamente
- `404 Not Found` → Item no existe en ML
- `429 Too Many Requests` → Rate limit excedido, esperar
- `invalid_grant` → Refresh token inválido, reautorizar manualmente

### Ciclo de Vida de Tokens
- **Access Token:** 6 horas → Se renueva automáticamente
- **Refresh Token:** 180 días → Alert cuando quedan <30 días
- **Renovación:** Background cada 10 min + Inline cuando necesario

---

## 📞 Soporte

**Documentación MercadoLibre:**
- API Docs: https://developers.mercadolibre.com.ar
- OAuth: https://developers.mercadolibre.com.ar/es_ar/autenticacion-y-autorizacion

**Stack Overflow:**
- Tag: `mercadolibre`
- Foros oficiales de ML

---

**Proyecto:** MLIntegration v1.0  
**Framework:** .NET 8  
**Fecha:** Noviembre 2025  
**Estado:** ✅ Producción Ready