# 🧾 Mercado Libre API — Publicaciones (Modelo UP 2025)

Colección de endpoints oficiales para crear, pausar, editar y eliminar publicaciones
en Mercado Libre según la documentación vigente (*Sincroniza y modifica publicaciones*, 06/10/2025).

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
