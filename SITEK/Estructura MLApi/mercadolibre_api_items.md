# 🧾 Mercado Libre API — Publicaciones (Modelo UP 2025)

Colección de endpoints oficiales para crear, pausar, editar y eliminar publicaciones
en Mercado Libre según la documentación vigente (*Sincroniza y modifica publicaciones*, 06/10/2025).

---

## 🔐 Autenticación OAuth 2.0

Mercado Libre utiliza el protocolo OAuth 2.0 con el flujo Authorization Code Grant Type (Server Side) para garantizar el acceso seguro a los recursos privados de los usuarios.

### Requisitos previos

Antes de comenzar, necesitás:
- **APP_ID**: ID de tu aplicación (se obtiene al crear una app en el [Portal de Desarrolladores](https://developers.mercadolibre.com.ar))
- **CLIENT_SECRET**: Clave secreta de tu aplicación
- **REDIRECT_URI**: URL de redirección configurada en tu aplicación (debe coincidir exactamente con la registrada)

---

### 📋 Paso 1: Solicitar autorización del usuario

Redirigí al usuario a la URL de autorización de Mercado Libre para que inicie sesión y autorice tu aplicación.

**URL de autorización**
```
https://auth.mercadolibre.com.ar/authorization?response_type=code&client_id={APP_ID}&redirect_uri={REDIRECT_URI}
```

**Parámetros**

| Parámetro | Tipo | Descripción |
|-----------|------|-------------|
| `response_type` | string | Debe ser `code` |
| `client_id` | string | Tu APP_ID |
| `redirect_uri` | string | URL de redirección (debe coincidir con la configurada) |

**Ejemplo completo**
```
https://auth.mercadolibre.com.ar/authorization?response_type=code&client_id=1234567890&redirect_uri=https://tuapp.com/callback
```

**⚠️ Consideraciones importantes:**
- El usuario que inicie sesión debe ser administrador, no un operador o colaborador
- Si el usuario es operador/colaborador, recibirás el error `invalid_operator_user_id`
- Podés usar usuarios de test para pruebas
- El usuario verá el diálogo de autorización de Mercado Libre

---

### 📋 Paso 2: Recibir el código de autorización

Después de autorizar, Mercado Libre redirige al usuario a tu `REDIRECT_URI` con un código de autorización:
```
https://tuapp.com/callback?code=TG-xxxxxxxxxxxxxxxxxxxxxxxx
```

Este código de autorización tiene una validez de 10 minutos.

- --

### 📋 Paso 3: Intercambiar el código por un access token

Realizá un POST al endpoint de OAuth para intercambiar el código de autorización por un access token.

** Endpoint**
```http
POST https://api.mercadolibre.com/oauth/token
```

**Headers**
```
Accept: application/json
Content-Type: application/x-www-form-urlencoded
```

**Body (x-www-form-urlencoded)**
```
grant_type=authorization_code
client_id={APP_ID}
client_secret={CLIENT_SECRET}
code={SERVER_GENERATED_AUTHORIZATION_CODE}
redirect_uri={REDIRECT_URI}
```

**Ejemplo con cURL**
```bash
curl -X POST \
  -H 'accept: application/json' \
  -H 'content-type: application/x-www-form-urlencoded' \
  'https://api.mercadolibre.com/oauth/token' \
  -d 'grant_type=authorization_code' \
  -d 'client_id=YOUR_APP_ID' \
  -d 'client_secret=YOUR_SECRET_KEY' \
  -d 'code=TG-xxxxxxxxxxxxxxxxxxxxxxxx' \
  -d 'redirect_uri=https://tuapp.com/callback'
```

**Respuesta exitosa**
```json
{
  "access_token": "APP_USR-1234567890-103010-abcd1234efgh5678-123456789",
  "token_type": "bearer",
  "expires_in": 21600,
  "scope": "offline_access read write",
  "user_id": 123456789,
  "refresh_token": "TG-xxxxxxxxxxxxxxxxxxxxxxxx-123456789"
}
```

**Detalles de la respuesta:**
- `access_token`: Válido por 6 horas desde su generación
- `r efresh_token`: Válido por 6 meses y es de un solo uso
- `e xpires_in`: Tiempo de expiración en segundos (21600 = 6 horas)
- `scope`: Permisos otorgados (`offline_access`, `read`, `write`)

---

### 🔄 Renovar el access token

Cuando el access token expire, usá el refresh token para obtener uno nuevo sin requerir autorización del usuario.

**E ndpoint**
```http
POST https://api.mercadolibre.com/oauth/token
```

**Body (x-www-form-urlencoded)**
```
grant_type=refresh_token
client_id={APP_ID}
client_secret={CLIENT_SECRET}
refresh_token={REFRESH_TOKEN}
```

**Ejemplo con cURL**
```bash
curl -X POST \
  'https://api.mercadolibre.com/oauth/token' \
  -d 'grant_type=refresh_token' \
  -d 'client_id=YOUR_APP_ID' \
  -d 'client_secret=YOUR_SECRET_KEY' \
  -d 'refresh_token=TG-xxxxxxxxxxxxxxxxxxxxxxxx'
```

**⚠️ Importante:**
- Solo se puede usar el último refresh token generado
- El refresh token es de un solo uso; recibirás uno nuevo con cada renovación
- Gua rdá el nuevo refresh token para futuras renovaciones

---

### 🔴 Causas de invalidación del access token

Un access token puede invalidarse antes de su expiración por los siguientes motivos:
- El usuario cambia su contraseña
- La aplicación renueva su App Secret
- El usuario revoca los permisos a tu aplicación
- No se usa la aplicación con ninguna request a `https://api.mercadolibre.com/` por 4 meses

---

### ❌ Códigos de error comunes

Los errores más comunes en el flujo OAuth incluyen:

| Er ror | Descripción |
|-------|-------------|
| `invalid_client` | `client_id` y/o `client_secret` inválidos |
| `invalid_grant` | Código de autorización o refresh token inválido, expirado o revocado; también si el `redirect_uri` no coincide |
| `invalid_scope` | Scopes inválidos. Valores permitidos: `offline_access`, `write`, `read` |
| `invalid_request` | Falta un parámetro obligatorio o hay valores duplicados |
| `unsupported_grant_type` | Valores permitidos: `authorization_code` o `refresh_token` |
| `invalid_operator_user_id` | El usuario que autorizó es un operador/colaborador, no un administrador |

---

### 🔒 Uso del access token

Para cada llamada a la API, enviá el access token en el header Authorization:
```ba sh
curl -H 'Authorization: Bearer APP_USR-1234567890-103010-abcd1234efgh5678-123456789' \
  https://api.mercadolibre.com/users/me
```

---

## 🔐 Variables globales

| Variable | Descripción |
|-----------|--------------|
| `{{access_token}}` | Token OAuth válido con permisos `read write offline_access` |
| `{{item_id}}` | ID del ítem generado por la API (`MLA...`) |

---

## 🧩 1️⃣ Crear publicación

**Endpoint**
```http
POST https://api.mercadolibre.com/items
```

**Headers**
```
Authorization: Bearer {{access_token}}
Content-Type: application/json
Accept: application/json
```

**Body de valores mínimos**
```json
{
  "family_name": "R4 Módulo Simodrive 611 - 6SN1123-1AA00-0CA0_api_test",
  "category_id": "MLA30216",
  "price": 3050000,
  "currency_id": "ARS",
  "available_quantity": 1,
  "buying_mode": "buy_it_now",
  "listing_type_id": "gold_special",
  "condition": "used",
  "channels": ["marketplace"],
  "pictures": [
    {
      "source": "https://http2.mlstatic.com/D_774156-MLA48097360478_112021-O.jpg"
    }
  ],
  "attributes": [
    { "id": "BRAND", "value_id": "19317", "value_name": "Siemens" },
    { "id": "MODEL", "value_name": "6SN1123-1AA00-0CA0_api_test_4" },
    { "id": "VALUE_ADDED_TAX", "value_id": "48405909", "value_name": "21 %" },
    { "id": "IMPORT_DUTY", "value_id": "49553239", "value_name": "0 %" }
  ]
}
```

**Respuesta esperada**
```json
"status": "active"
```

---

## ⏸️ 2️⃣ Pausar publicación

**Endpoint**
```http
PUT https://api.mercadolibre.com/items/{{item_id}}
```

**Headers**
```
Authorization: Bearer {{access_token}}
Content-Type: application/json
Accept: application/json
```

**Body**
```json
{
  "status": "paused"
}
```

**Respuesta esperada**
```json
"status": "paused",
"sub_status": []
```

---

## 💰 3️⃣ Editar precio y cantidad disponible

**Endpoint**
```http
PUT https://api.mercadolibre.com/items/{{item_id}}
```

**Headers**
```
Authorization: Bearer {{access_token}}
Content-Type: application/json
Accept: application/json
```

**Body**
```json
{
  "price": 3090000,
  "available_quantity": 3
}
```

**Respuesta esperada**
```json
"price": 3090000,
"available_quantity": 3
```

---

## ❌ 4️⃣ Eliminar publicación (oficial)

### Paso 1 – Cerrar publicación

**Endpoint**
```http
PUT https://api.mercadolibre.com/items/{{item_id}}
```

**Headers**
```
Authorization: Bearer {{access_token}}
Content-Type: application/json
Accept: application/json
```

**Body**
```json
{
  "status": "closed"
}
```

---

### Paso 2 – Eliminar definitivamente

**Endpoint**
```http
PUT https://api.mercadolibre.com/items/{{item_id}}
```

**Headers**
```
Authorization: Bearer {{access_token}}
Content-Type: application/json
Accept: application/json
```

**Body**
```json
{
  "deleted": "true"
}
```

**Respuesta esperada**
```json
"status": "closed",
"sub_status": ["deleted"]
```

---

## ✅ Estados posibles

| Estado | Sub estado | Descripción |
|---------|-------------|-------------|
| `active` | — | Publicación visible y activa |
| `paused` | `[]` o `"picture_download_pending"` | Pausada por el usuario o por procesamiento de imágenes |
| `closed` | `["deleted"]`, `["expired"]`, etc. | Publicación finalizada o eliminada |

---

> **Nota:**  
> Si obtenés `409 conflict` tras enviar `"deleted": "true"`, esperá unos segundos tras el `PUT` de cierre.  
> Mercado Libre propaga los cambios de estado de forma asíncrona.