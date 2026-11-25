# LogiMarket 360

Aplicación de escritorio **Windows Forms (.NET 8)** para ventas y logística de accesorios de computación,
construida con **Clean Architecture**, **SOLID**, persistencia real en **SQLite + EF Core 8** y los
**23 patrones GoF** integrados en procesos de negocio verificables.

> APE 04 · Patrones de Software · UTA · 5 "A" — Arcos Guzmán Juan Carlos

---

## 1. Requisitos

| Herramienta | Versión |
|---|---|
| Windows | 10 / 11 (Windows Forms solo se ejecuta en Windows) |
| .NET SDK | 8.0.x (`dotnet --list-sdks`) |
| Visual Studio | 2022 **17.14 o superior** (necesario para abrir `.slnx`) con la carga *Desarrollo de escritorio de .NET* |
| Git | cualquiera reciente |

Paquetes NuGet (se restauran automáticamente):

| Proyecto | Paquete | Versión |
|---|---|---|
| Infrastructure | `Microsoft.EntityFrameworkCore.Sqlite` | 8.0.11 |
| Infrastructure | `Microsoft.EntityFrameworkCore.Design` | 8.0.11 |
| Infrastructure | `Microsoft.Extensions.DependencyInjection` | 8.0.1 |
| Tests | `xunit` | 2.5.3 |
| Tests | `xunit.runner.visualstudio` | 2.5.3 |
| Tests | `Microsoft.NET.Test.Sdk` | 17.8.0 |
| Tests | `coverlet.collector` | 6.0.0 |

## 2. Estructura de la solución

```
LogiMarket360.slnx
├─ src/
│  ├─ LogiMarket.Domain          → entidades, agregados, invariantes, State, Composite, Strategy, Builder,
│  │                               Prototype, Memento, Iterator, Visitor, Abstract Factory (sin referencias)
│  ├─ LogiMarket.Application     → casos de uso, puertos, DTO, Chain, Command, Facade, Proxy, Decorator,
│  │                               Observer, Template Method, Interpreter, Bridge, Flyweight (→ Domain)
│  ├─ LogiMarket.Infrastructure  → EF Core/SQLite, migraciones, seed, repositorios, Unit of Work, Adapter,
│  │                               notificadores EMAIL/SMS (Factory Method), salida TXT (→ Application, Domain)
│  └─ LogiMarket.WinForms        → formularios, FormularioPrincipalMediator, composition root DI
│                                  (→ Application; Infrastructure solo para registrar dependencias)
└─ tests/
   └─ LogiMarket.Tests           → xUnit: dominio, aplicación e integración con SQLite aislado
```

Regla de dependencia: `WinForms → Application → Domain`; `Infrastructure → Application/Domain`.
Se verifica con los `.csproj` y con la prueba automática `P38_ReglaDeDependencias`.

## 3. Restaurar, compilar y probar

> El SDK de .NET 8 no reconoce archivos `.slnx` desde la línea de comandos, por eso los comandos
> apuntan a cada proyecto. (Con el SDK 9.0.200 o superior también funciona `dotnet build LogiMarket360.slnx`.)

```powershell
git clone <url-del-repositorio>
cd LogiMarket360

dotnet restore src/LogiMarket.WinForms/LogiMarket.WinForms.csproj
dotnet restore tests/LogiMarket.Tests/LogiMarket.Tests.csproj

dotnet build src/LogiMarket.WinForms/LogiMarket.WinForms.csproj -c Release
dotnet test  tests/LogiMarket.Tests/LogiMarket.Tests.csproj          # 53 pruebas automatizadas

dotnet run --project src/LogiMarket.WinForms/LogiMarket.WinForms.csproj
```

En Visual Studio: abrir `LogiMarket360.slnx`, establecer **LogiMarket.WinForms** como proyecto de inicio y
pulsar F5. Las pruebas se ejecutan desde *Prueba → Explorador de pruebas*.

## 4. Configuración

La aplicación busca un `appsettings.json` opcional junto al ejecutable. Si no existe, usa estos valores
por defecto:

```json
{
  "DirectorioDatos": "datos",
  "NombreBaseDatos": "logimarket.db",
  "ProcesarOutboxCadaSegundos": 15
}
```

`DirectorioDatos` puede ser relativo (al ejecutable) o absoluto. Dentro de esa carpeta se generan:

| Ruta | Contenido |
|---|---|
| `datos/logimarket.db` | base SQLite de ejecución |
| `datos/comprobantes/` | comprobantes TXT de venta y devolución |
| `datos/notificaciones/email`, `datos/notificaciones/sms` | avisos simulados |
| `datos/logs/bitacora.log` | bitácora operativa (cobros, Proxy, Decorator, Outbox) |

Toda la carpeta `datos/` está excluida de Git.

## 5. Base de datos: migraciones y semilla

- Al iniciar, la aplicación **aplica las migraciones EF Core** (`Database.Migrate`) y ejecuta la
  **semilla idempotente**. Si el archivo no existe, se crea desde cero sin editar tablas manualmente;
  relanzar la semilla no duplica datos.
- Ubicación por defecto: `src/LogiMarket.WinForms/bin/<Config>/net8.0-windows/datos/logimarket.db`.
- Migraciones en `src/LogiMarket.Infrastructure/Persistencia/Migraciones/`.
- Aplicar o listar migraciones manualmente (opcional):

```powershell
dotnet tool install --global dotnet-ef --version 8.0.11
dotnet ef migrations list  --project src/LogiMarket.Infrastructure --startup-project src/LogiMarket.Infrastructure
dotnet ef database update  --project src/LogiMarket.Infrastructure --startup-project src/LogiMarket.Infrastructure
```

Catálogo inicial (seed):

| SKU | Producto | Precio USD | Stock | Peso kg |
|---|---|---|---|---|
| P01 | Mouse USB | 20.00 | 8 | 0.20 |
| P02 | Teclado | 30.00 | 5 | 0.70 |
| P03 | Memoria USB | 10.00 | 20 | 0.10 |
| P04 | Webcam | 40.00 | 4 | 0.35 |
| P05 | Alfombrilla | 5.00 | 30 | 0.10 |

Kits: `KIT_OFICINA = P01 + P02` (USD 50.00) · `KIT_TELETRABAJO = KIT_OFICINA + P03` (USD 60.00).

Clientes ficticios: Ana López (VIP), Luis Ruiz (NORMAL), Carmen Vega (NORMAL; obtiene MAYORISTA por volumen).

## 6. Cuentas de demostración (ficticias)

| Usuario | Contraseña | Rol | Puede |
|---|---|---|---|
| `admin` | `Admin123*` | ADMIN | todas las operaciones y la pantalla de demostración |
| `ventas` | `Ventas123*` | VENTAS | clientes, pedidos, validación, cobro, devoluciones |
| `bodega` | `Bodega123*` | BODEGA | despacho, entrega, ajustes de inventario |
| `consulta` | `Consulta123*` | CONSULTA | solo lectura |

Las contraseñas se almacenan con **PBKDF2-SHA256 + sal** (nunca en texto claro). La autorización se aplica
en `Application` (`Autorizador`, `PagoAutorizadoProxy`, cadena de validación), no solo deshabilitando botones.
Estas credenciales son exclusivamente de demostración.

## 7. Restablecer los datos de prueba de forma segura

1. **Desde la aplicación** (ADMIN): pantalla *Demostración* → *Restablecer base de pruebas*. Elimina la base
   de ejecución y la recrea con migraciones + seed (la operación queda auditada).
2. **Manual**: cerrar la aplicación y borrar la carpeta `datos/` junto al ejecutable. Se recrea al iniciar.

Las pruebas automáticas usan siempre una base SQLite temporal aislada por prueba; nunca tocan la base de ejecución.

## 8. Casos numéricos oficiales (verificados por pruebas)

| Escenario | Cálculo | Resultado |
|---|---|---|
| Ana VIP · KIT_TELETRABAJO ×1 · ESTÁNDAR | 60.00 − 6.00 + 5.00 | **USD 59.00**; P01 8→7, P02 5→4, P03 20→19 |
| Luis NORMAL · P04 ×2 + P05 ×2 · EXPRÉS | 90.00 − 0.00 + 12.00 | **USD 102.00**; P04 4→2, P05 30→28 |
| Carmen · P03 ×10 · ESTÁNDAR (MAYORISTA) | 100.00 − 15.00 + 5.00 | **USD 90.00**; P03 20→10 |
| KIT_TELETRABAJO ×6 | requiere 6 P02, hay 5 | **Rechazo**; sin stock, pago ni evento |
| Devolución DEFECTO de Ana | 59.00 − 5.00 de envío | **Reembolso USD 54.00** |

## 9. Pantallas

Acceso · Panel general · Catálogo y kits · Clientes · Carrito/borrador · Pedidos e historial · Cobro ·
Despacho · Devoluciones · Inventario y movimientos · Reportes/comprobantes · Auditoría y métricas ·
Demostración (solo ADMIN: cambiar proveedor de pago, inducir rechazo de cobro, fallo de notificador/Outbox
y restablecer datos).

## 10. Pruebas automatizadas

53 pruebas xUnit nombradas según la matriz de aceptación (`P01_…` a `P38_…`) más regresiones (`R01_…` a `R04_…`):

| Archivo | Cobertura |
|---|---|
| `Dominio/CatalogoCompositeTests.cs` | Composite, ciclos, Iterator, Visitor |
| `Dominio/PedidoDominioTests.cs` | Builder, Prototype, Memento, State, Strategy, Abstract Factory |
| `Dominio/RegresionTests.cs` | fallos detectados y corregidos (Memento, Mediator) |
| `Integracion/FlujoVentasTests.cs` | P01–P10: seed, casos oficiales, rechazo, idempotencia, persistencia |
| `Integracion/PatronesIntegradosTests.cs` | Factory Method, Singleton, Adapter, Bridge, Decorator, Facade, Flyweight, Proxy |
| `Integracion/ComportamentalesTests.cs` | Chain, Command, Interpreter, Mediator, Observer, Template Method |
| `Integracion/IntegridadArquitecturaTests.cs` | devolución única, concurrencia, Outbox, LSP/ISP/DIP, dependencias |
| `Integracion/CompensacionTests.cs` | R04: un error imprevisto tras un cobro aprobado anula el cobro |

## 11. Generar el ZIP de entrega

Desde la raíz del repositorio, con todos los cambios confirmados:

```powershell
git archive --format=zip -o APE04_LogiMarket360_ARCOS_GUZMAN_JUAN_CARLOS.zip HEAD
```

`git archive` solo incluye archivos versionados, así que el ZIP queda sin `bin/`, `obj/`, `.vs/`,
`*.csproj.user` ni la base de ejecución. 