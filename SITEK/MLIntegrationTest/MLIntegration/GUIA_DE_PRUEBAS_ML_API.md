# ============================================
# GUÍA COMPLETA DE PRUEBAS - MERCADOLIBRE API
# Ejecutar en PowerShell desde VS Code
# ============================================

# ============================================
# PASO 1: EJECUTAR MIGRACIONES
# ============================================

Write-Host "`n=== CONFIGURANDO BASE DE DATOS ===" -ForegroundColor Cyan

# Navegar a la carpeta del proyecto
# Set-Location -Path "C:\tu\ruta\MLIntegration"

# Crear migración inicial
Write-Host "`n1. Creando migración..." -ForegroundColor Yellow
dotnet ef migrations add InitialMercadoLibreSetup

# Aplicar migración a MySQL
Write-Host "`n2. Aplicando migración..." -ForegroundColor Yellow
dotnet ef database update

# Verificar tablas creadas en MySQL
Write-Host "`n3. Verificando tablas en MySQL..." -ForegroundColor Yellow
$mysqlVerify = @"
mysql -u root -p -e "USE ml_integration; SHOW TABLES;"
"@
Write-Host $mysqlVerify -ForegroundColor Cyan

# ============================================
# PASO 2: INICIAR SERVIDOR
# ============================================

Write-Host "`n=== INICIANDO SERVIDOR ===" -ForegroundColor Cyan
Write-Host "Ejecutando: dotnet run" -ForegroundColor Yellow
Write-Host "La API estará en: http://localhost:5000" -ForegroundColor Green
Write-Host "Swagger UI: http://localhost:5000" -ForegroundColor Green

# Ejecutar (esto bloqueará la terminal)
# dotnet run

# ============================================
# PASO 3: PRUEBAS DE API (Ejecutar en OTRA terminal)
# ============================================

Write-Host "`n=== PRUEBAS DE API ===" -ForegroundColor Cyan
Write-Host "Abre OTRA terminal PowerShell y ejecuta estos comandos:`n" -ForegroundColor Yellow

# ============================================
# TEST 1: Obtener URL de Autorización
# ============================================

Write-Host "### TEST 1: Obtener URL de Autorización ###" -ForegroundColor Magenta

$test1 = @'
# Obtener URL de autorización
$response = Invoke-RestMethod -Uri "http://localhost:5000/api/mercadolibre/auth/url?userId=1" -Method Get
Write-Host "URL de autorizacion: " -ForegroundColor Green
$response.authorizationUrl

# IMPORTANTE: Copia la URL y pégala en tu navegador
# MercadoLibre te pedirá autorizar la app
# Luego te redirigirá a: http://localhost:5000/api/mercadolibre/callback?code=TG-...
'@
Write-Host $test1 -ForegroundColor White

# ============================================
# TEST 2: Verificar Token en Base de Datos
# ============================================

Write-Host "`n### TEST 2: Verificar Token Guardado ###" -ForegroundColor Magenta

$test2 = @'
# En MySQL (otra terminal):
mysql -u root -p

USE ml_integration;
SELECT Id, UserId, LEFT(AccessToken, 20) as Token_Preview, ExpiresAt, CreatedAt 
FROM MLTokens 
WHERE UserId = 1;

# Deberías ver algo como:
# Id | UserId | Token_Preview        | ExpiresAt           | CreatedAt
# 1  | 1      | APP_USR-123456789... | 2025-10-30 18:00:00 | 2025-10-30 12:00:00
'@
Write-Host $test2 -ForegroundColor White

# ============================================
# TEST 3: Crear Publicación
# ============================================

Write-Host "`n### TEST 3: Crear Publicación ###" -ForegroundColor Magenta

$test3 = @'
# Crear item de prueba
$body = @{
    familyName = "Módulo Siemens 6SN1123-1AA00-0CA0 PRUEBA API"
    categoryId = "MLA30216"
    price = 3050000
    currencyId = "ARS"
    availableQuantity = 1
    buyingMode = "buy_it_now"
    listingTypeId = "gold_special"
    condition = "used"
    channels = @("marketplace")
    pictures = @(
        @{
            source = "https://http2.mlstatic.com/D_774156-MLA48097360478_112021-O.jpg"
        }
    )
    attributes = @(
        @{ id = "BRAND"; value_id = "19317"; value_name = "Siemens" },
        @{ id = "MODEL"; value_name = "6SN1123-1AA00-0CA0" },
        @{ id = "VALUE_ADDED_TAX"; value_id = "48405909"; value_name = "21 %" }
    )
} | ConvertTo-Json -Depth 10

$response = Invoke-RestMethod -Uri "http://localhost:5000/api/mercadolibre/items?userId=1" `
    -Method Post `
    -ContentType "application/json" `
    -Body $body

Write-Host "Item creado exitosamente!" -ForegroundColor Green
Write-Host "Item ID: $($response.RootElement.GetProperty('id').GetString())" -ForegroundColor Cyan
Write-Host "Status: $($response.RootElement.GetProperty('status').GetString())" -ForegroundColor Cyan
Write-Host "Permalink: $($response.RootElement.GetProperty('permalink').GetString())" -ForegroundColor Yellow

# Guardar el Item ID para siguientes pruebas
$global:itemId = $response.RootElement.GetProperty('id').GetString()
'@
Write-Host $test3 -ForegroundColor White

# ============================================
# TEST 4: Consultar Item
# ============================================

Write-Host "`n### TEST 4: Consultar Item ###" -ForegroundColor Magenta

$test4 = @'
# Consultar item creado (reemplaza MLA1234567890 con tu Item ID)
$itemId = "MLA1234567890"  # Usa el ID del test anterior

$response = Invoke-RestMethod -Uri "http://localhost:5000/api/mercadolibre/items/$itemId?userId=1" -Method Get

# Mostrar información
$item = $response.RootElement
Write-Host "Familia: $($item.GetProperty('title').GetString())" -ForegroundColor Green
Write-Host "Precio: $($item.GetProperty('price').GetDecimal())" -ForegroundColor Cyan
Write-Host "Cantidad: $($item.GetProperty('available_quantity').GetInt32())" -ForegroundColor Cyan
Write-Host "Status: $($item.GetProperty('status').GetString())" -ForegroundColor Yellow
'@
Write-Host $test4 -ForegroundColor White

# ============================================
# TEST 5: Actualizar Precio y Cantidad
# ============================================

Write-Host "`n### TEST 5: Actualizar Precio y Cantidad ###" -ForegroundColor Magenta

$test5 = @'
# Actualizar precio y cantidad
$itemId = "MLA1234567890"  # Tu Item ID

$body = @{
    price = 3200000
    quantity = 5
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "http://localhost:5000/api/mercadolibre/items/$itemId/price?userId=1" `
    -Method Put `
    -ContentType "application/json" `
    -Body $body

Write-Host "Item actualizado!" -ForegroundColor Green
Write-Host "Nuevo precio: $($response.RootElement.GetProperty('price').GetDecimal())" -ForegroundColor Cyan
Write-Host "Nueva cantidad: $($response.RootElement.GetProperty('available_quantity').GetInt32())" -ForegroundColor Cyan
'@
Write-Host $test5 -ForegroundColor White

# ============================================
# TEST 6: Pausar Publicación
# ============================================

Write-Host "`n### TEST 6: Pausar Publicación ###" -ForegroundColor Magenta

$test6 = @'
# Pausar item
$itemId = "MLA1234567890"

$response = Invoke-RestMethod -Uri "http://localhost:5000/api/mercadolibre/items/$itemId/pause?userId=1" -Method Put

Write-Host "Item pausado!" -ForegroundColor Yellow
Write-Host "Status: $($response.RootElement.GetProperty('status').GetString())" -ForegroundColor Cyan
'@
Write-Host $test6 -ForegroundColor White

# ============================================
# TEST 7: Eliminar Publicación
# ============================================

Write-Host "`n### TEST 7: Eliminar Publicación ###" -ForegroundColor Magenta

$test7 = @'
# Eliminar item (proceso de 2 pasos)
$itemId = "MLA1234567890"

$response = Invoke-RestMethod -Uri "http://localhost:5000/api/mercadolibre/items/$itemId?userId=1" -Method Delete

Write-Host "Item eliminado!" -ForegroundColor Red
Write-Host "Status: $($response.result.RootElement.GetProperty('status').GetString())" -ForegroundColor Cyan
Write-Host "Sub Status: $($response.result.RootElement.GetProperty('sub_status').EnumerateArray())" -ForegroundColor Cyan
'@
Write-Host $test7 -ForegroundColor White

# ============================================
# TEST 8: Verificar Renovación Automática de Tokens
# ============================================

Write-Host "`n### TEST 8: Verificar Background Service ###" -ForegroundColor Magenta

$test8 = @'
# Ver logs del servidor en la terminal donde ejecutaste dotnet run
# Deberías ver logs cada 5 horas:
# [INFO] Servicio de renovación automática de tokens ML iniciado
# [INFO] No hay tokens que requieran renovación
# [INFO] Renovando token para usuario 1, expira en: 2025-10-30 17:55:00 UTC

# Para forzar renovación (simular token por expirar):
# En MySQL:
UPDATE MLTokens 
SET ExpiresAt = DATE_ADD(NOW(), INTERVAL 25 MINUTE) 
WHERE UserId = 1;

# Espera 5 minutos y verifica logs del servidor
'@
Write-Host $test8 -ForegroundColor White

# ============================================
# PRUEBAS CON SWAGGER UI (Alternativa visual)
# ============================================

Write-Host "`n### ALTERNATIVA: Usar Swagger UI ###" -ForegroundColor Magenta

$swaggerInstructions = @'
1. Abre http://localhost:5000 en tu navegador
2. Verás todos los endpoints disponibles
3. Click en "Try it out" en cualquier endpoint
4. Ingresa los parámetros y click en "Execute"
5. Swagger mostrará la respuesta automáticamente

Ventajas:
- Interfaz visual
- No necesitas escribir comandos
- Muestra ejemplos de requests/responses
- Ideal para pruebas rápidas
'@
Write-Host $swaggerInstructions -ForegroundColor Cyan

# ============================================
# TROUBLESHOOTING
# ============================================

Write-Host "`n=== TROUBLESHOOTING ===" -ForegroundColor Red

$troubleshooting = @'
### ERROR: "No se puede conectar a MySQL"
Solución:
1. Verifica que MySQL esté corriendo:
   Get-Service MySQL*
2. Si está detenido:
   Start-Service MySQL80  # o el nombre de tu servicio
3. Verifica credenciales en appsettings.json

### ERROR: "401 Unauthorized" en llamadas API
Solución:
1. Token expirado, vuelve a autorizar:
   GET http://localhost:5000/api/mercadolibre/auth/url?userId=1
2. Verifica en BD que el token exista:
   SELECT * FROM MLTokens WHERE UserId = 1;

### ERROR: "dotnet ef no reconocido"
Solución:
dotnet tool install --global dotnet-ef

### ERROR: Puerto 5000 ocupado
Solución:
1. Cambiar puerto en launchSettings.json:
   "applicationUrl": "http://localhost:5001"
2. O matar proceso:
   Get-Process -Id (Get-NetTCPConnection -LocalPort 5000).OwningProcess | Stop-Process

### ERROR: "Cannot connect to MySQL server"
Solución:
1. Verifica que MySQL esté escuchando en 3306:
   netstat -an | findstr :3306
2. Prueba conexión:
   mysql -u root -p -h localhost
'@
Write-Host $troubleshooting -ForegroundColor Yellow

# ============================================
# VERIFICACIÓN FINAL
# ============================================

Write-Host "`n=== CHECKLIST FINAL ===" -ForegroundColor Green

$checklist = @'
[ ] MySQL instalado y corriendo
[ ] Base de datos ml_integration creada
[ ] Migraciones aplicadas (dotnet ef database update)
[ ] appsettings.json configurado con credenciales correctas
[ ] APP_ID y SECRET_KEY de MercadoLibre configurados
[ ] Servidor corriendo (dotnet run)
[ ] Swagger UI accesible (http://localhost:5000)
[ ] Autorización OAuth completada (token guardado en BD)
[ ] Test de crear item exitoso
[ ] Test de consultar item exitoso
[ ] Test de actualizar precio exitoso
[ ] Background service activo (ver logs)

¡Todo listo para implementar en producción!
'@
Write-Host $checklist -ForegroundColor White

Write-Host "`n=== FIN DE LA GUÍA ===" -ForegroundColor Cyan