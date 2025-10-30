🔷 Helper de Mercado Libre para .NET
Te voy a crear un helper completo en .NET con arquitectura limpia, manejo automático de tokens y todas las operaciones CRUD.
📊 Flujo gráfico del sistema
┌─────────────────────────────────────────────────────────────────┐
│                     INICIALIZACIÓN DEL SISTEMA                   │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           ▼
                ┌──────────────────────┐
                │  Usuario autoriza    │
                │  en ML (OAuth 2.0)   │
                └──────────┬───────────┘
                           │
                           ▼
                ┌──────────────────────┐
                │ Callback recibe CODE │
                │ Exchange por tokens  │
                └──────────┬───────────┘
                           │
                           ▼
        ┌──────────────────────────────────────┐
        │  Guardar en BD:                      │
        │  - access_token (6h)                 │
        │  - refresh_token (6 meses)           │
        │  - expires_at (timestamp)            │
        └──────────┬───────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────────┐
│              SERVICIO DE BACKGROUND (HostedService)              │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Cada 5 horas:                                             │  │
│  │  1. Buscar tokens que expiran en < 30 min                  │  │
│  │  2. Renovar con refresh_token                              │  │
│  │  3. Actualizar BD con nuevos tokens                        │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────────────────────────┐
│                    OPERACIONES DE LA API                         │
│  ┌─────────────────────┐                                         │
│  │ Controller/Service  │                                         │
│  │ solicita operación  │                                         │
│  └─────────┬───────────┘                                         │
│            │                                                     │
│            ▼                                                     │
│  ┌─────────────────────┐                                         │
│  │ MLHelper verifica   │                                         │
│  │ validez del token   │                                         │
│  └─────────┬───────────┘                                         │
│            │                                                     │
│       ┌────┴────┐                                                │
│       │ ¿Válido? │                                               │
│       └────┬────┘                                                │
│         NO │  SÍ                                                 │
│     ┌──────┴──────┐                                              │
│     ▼             ▼                                              │
│  ┌────────┐   ┌──────────────┐                                   │
│  │Renovar │   │ Usar token   │                                   │
│  │  con   │   │  existente   │                                   │
│  │refresh │   └──────┬───────┘                                   │
│  └───┬────┘          │                                           │
│      └───────────────┘                                           │
│                │                                                 │
│                ▼                                                 │
│  ┌──────────────────────────┐                                    │
│  │  Ejecutar operación:      │                                   │
│  │  - GET (consultas)        │                                   │
│  │  - POST (crear)           │                                   │
│  │  - PUT (actualizar)       │                                   │
│  │  - DELETE (eliminar)      │                                   │
│  └──────────────────────────┘                                    │
└──────────────────────────────────────────────────────────────────┘
```

---

## 🗂️ Estructura del proyecto
```
YourProject/
├── Models/
│   ├── MercadoLibre/
│   │   ├── MLToken.cs
│   │   ├── MLProduct.cs
│   │   ├── MLAuthResponse.cs
│   │   └── MLItemRequest.cs
├── Helpers/
│   └── MercadoLibreHelper.cs
├── Services/
│   ├── IMLTokenService.cs
│   ├── MLTokenService.cs
│   └── MLTokenRefreshService.cs (BackgroundService)
├── Data/
│   └── ApplicationDbContext.cs
└── appsettings.json
