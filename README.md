# LogiMarket 360

Aplicación de escritorio **Windows Forms (.NET 8)** para ventas y logística de accesorios de computación,
construida con **Clean Architecture**, **SOLID**, persistencia real en **SQLite + EF Core 8** y los
**23 patrones GoF** integrados en procesos de negocio verificables.

> APE 04 · Patrones de Software · UTA · 5 "A" — Arcos Guzmán Juan Carlos

---

## 1. Requisitos

| Herramienta | Versión |
|---|---|
| Windows | 10 / 11 |
| .NET SDK | 8.0.x (`dotnet --list-sdks`) |
| Visual Studio | 2022 (17.8 o superior) con la carga *Desarrollo de escritorio de .NET* |
| Git | cualquiera reciente |

Paquetes NuGet (se restauran solos): `Microsoft.EntityFrameworkCore.Sqlite 8.0.11`,
`Microsoft.EntityFrameworkCore.Design 8.0.11`, `Microsoft.Extensions.DependencyInjection 8.0.1`,
`xunit 2.9.2`, `xunit.runner.visualstudio 2.8.2`, `Microsoft.NET.Test.Sdk 17.11.1`.

## 2. Estructura de la solución

```
LogiMarket360.sln
├─ src/
│  ├─ LogiMarket.Domain          → entidades, agregados, invariantes, State, Composite, Strategy... (sin referencias)
│  ├─ LogiMarket.Application     → casos de uso, puertos, DTO, Chain, Command, Facade, Proxy... (→ Domain)
│  ├─ LogiMarket.Infrastructure  → EF Core/SQLite, repositorios, Adapter, notificadores, Outbox, TXT (→ Application, Domain)
│  └─ LogiMarket.WinForms        → formularios, Mediator de vistas, composition root DI (→ Application; Infrastructure solo para DI)
├─ tests/
│  └─ LogiMarket.Tests           → xUnit: dominio, aplicación e integración con SQLite aislado
└─ docs/                         → matriz de patrones, matriz P01–P38, riesgos de diseño
```

Regla de dependencia: `WinForms → Application → Domain`; `Infrastructure → Application/Domain`.
Se verifica con los `.csproj` y con la prueba automática **P38 Arquitectura**.

## 3. Restaurar, compilar y probar

```powershell
git clone <url-del-repositorio>
cd LogiMarket360
dotnet restore
dotnet build -c Release
dotnet test                      # 52 pruebas automatizadas (dominio + integración)
```

En Visual Studio: abrir `LogiMarket360.sln`, establecer **LogiMarket.WinForms** como proyecto de inicio y pulsar F5.
Las pruebas se ejecutan desde *Prueba → Explorador de pruebas*.

## 4. Base de datos: migraciones y semilla

- Al iniciar, la aplicación **aplica las migraciones EF Core** (`Database.Migrate`) y ejecuta la **semilla idempotente**.
  Si el archivo no existe, se crea desde cero sin editar tablas manualmente.
- Ubicación: `src/LogiMarket.WinForms/bin/<Config>/net8.0-windows/datos/logimarket.db` (configurable en `appsettings.json`).
- Aplicar migraciones manualmente (opcional):

```powershell
dotnet tool restore
dotnet ef database update --project src/LogiMarket.Infrastructure --startup-project src/LogiMarket.Infrastructure
dotnet ef migrations list  --project src/LogiMarket.Infrastructure --startup-project src/LogiMarket.Infrastructure
```

Catálogo inicial (seed): P01 Mouse USB 20.00 (8) · P02 Teclado 30.00 (5) · P03 Memoria USB 10.00 (20) ·
P04 Webcam 40.00 (4) · P05 Alfombrilla 5.00 (30) · KIT_OFICINA = P01 + P02 · KIT_TELETRABAJO = KIT_OFICINA + P03.
Clientes ficticios: Ana López (VIP), Luis Ruiz (NORMAL), Carmen Vega (NORMAL).

## 5. Cuentas de demostración (ficticias)

| Usuario | Contraseña | Rol | Puede |
|---|---|---|---|
| `admin` | `Admin123*` | ADMIN | todas las operaciones y la pantalla de demostración |
| `ventas` | `Ventas123*` | VENTAS | clientes, pedidos, validación, cobro, devoluciones |
| `bodega` | `Bodega123*` | BODEGA | despacho, entrega, ajustes de inventario |
| `consulta` | `Consulta123*` | CONSULTA | solo lectura |

Las contraseñas se almacenan con **PBKDF2-SHA256 + sal** (nunca en texto claro). La autorización se aplica en
`Application` (`Autorizador`, `PagoAutorizadoProxy`, cadena de validación), no solo deshabilitando botones.

## 6. Restablecer los datos de prueba de forma segura

1. **Desde la aplicación** (ADMIN): pantalla *Demostración* → *Restablecer base de pruebas*. Elimina el archivo de
   ejecución y lo recrea con migraciones + seed (queda auditado).
2. **Manual**: cerrar la aplicación y borrar la carpeta `datos/` junto al ejecutable. Se recrea al iniciar.

Las pruebas automáticas usan siempre una base temporal aislada por prueba; nunca tocan la base de ejecución.

## 7. Casos numéricos oficiales (verificados por pruebas)

| Escenario | Cálculo | Resultado |
|---|---|---|
| Ana VIP · KIT_TELETRABAJO ×1 · ESTÁNDAR | 60.00 − 6.00 + 5.00 | **USD 59.00**; P01 8→7, P02 5→4, P03 20→19 |
| Luis NORMAL · P04 ×2 + P05 ×2 · EXPRÉS | 90.00 − 0.00 + 12.00 | **USD 102.00**; P04 4→2, P05 30→28 |
| Carmen · P03 ×10 · ESTÁNDAR (MAYORISTA) | 100.00 − 15.00 + 5.00 | **USD 90.00**; P03 20→10 |
| KIT_TELETRABAJO ×6 | requiere 6 P02, hay 5 | **Rechazo**; sin stock, pago ni evento |
| Devolución DEFECTO de Ana | 59.00 − 5.00 de envío | **Reembolso USD 54.00** |

## 8. Pantallas

Acceso · Panel general · Catálogo y kits · Clientes · Carrito/borrador · Pedidos e historial · Cobro ·
Despacho · Devoluciones · Inventario · Reportes/comprobantes · Auditoría y métricas · Demostración (ADMIN).

