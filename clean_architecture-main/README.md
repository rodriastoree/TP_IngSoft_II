# ISII — Trabajo Práctico de Ingeniería de Software II

Este repositorio incorpora la funcionalidad **Coupon** al proyecto base facilitado por el profesor. Permite crear cupones y consultarlos por identificador, con reglas de dominio, persistencia SQLite, validaciones HTTP y pruebas automatizadas.

Se conserva la atribución al proyecto original del profesor. Su README identificaba como origen la plantilla **Minimal Clean Architecture** y referenciaba el [repositorio Clean Architecture de Ardalis](https://github.com/ardalis/CleanArchitecture). La ampliación mantiene la organización del proyecto base.

La consigna académica no está disponible en el repositorio. Esta documentación describe la implementación y la evidencia verificadas; no acredita el cumplimiento completo de requisitos de entrega o evaluación desconocidos.

## Funcionalidad incorporada

- Dominio `Coupon` e identificador `CouponId` mediante Vogen.
- Persistencia EF Core/SQLite y migración `AddCoupon`.
- `POST /Coupons`: creación de cupones.
- `GET /Coupons/{CouponId}`: consulta, incluidos cupones vencidos o inactivos.
- Validaciones 400, conflicto 409 por código duplicado y respuesta 500 genérica fuera de Development.
- Pruebas unitarias, de persistencia y HTTP.

Coupon no se aplica a Cart u Order ni calcula descuentos en compras. El alcance, las reglas y las limitaciones se explican en la [guía técnica de Coupon](docs/Coupon.md).

## Tecnologías utilizadas

| Tecnología | Uso en el proyecto |
|---|---|
| .NET 10 y C# | Aplicación web y pruebas |
| FastEndpoints y FluentValidation | Endpoints y validación de entrada |
| Mediator | Query y handler del GET de Coupon |
| Ardalis.SharedKernel, Result y Specification | Entidades, repositorios, resultados y consultas |
| EF Core y SQLite | Mapeo, persistencia y migraciones |
| Vogen | Identificadores tipados y conversiones EF |
| Serilog | Registro de diagnóstico |
| FastEndpoints.Swagger y Scalar | Documentación de API en Development |
| xUnit y Microsoft.AspNetCore.Mvc.Testing | Pruebas y host HTTP en proceso |

Las versiones se centralizan en [Directory.Packages.props](Directory.Packages.props); las referencias efectivas se declaran en los proyectos [Web](src/ISII.Web/ISII.Web.csproj) y [Tests](tests/ISII.Tests/ISII.Tests.csproj).

## Estructura actual

La solución contiene un proyecto web organizado por funcionalidades o **vertical slices**, y un proyecto de pruebas. Dominio e infraestructura son carpetas del proyecto web; no son proyectos independientes.

```text
clean_architecture-main/
├── ISII.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── global.json
├── docs/
│   └── Coupon.md
├── src/ISII.Web/
│   ├── Domain/
│   │   ├── CartAggregate/
│   │   ├── CouponAggregate/
│   │   ├── GuestUserAggregate/
│   │   ├── OrderAggregate/
│   │   └── ProductAggregate/
│   ├── CartFeatures/
│   ├── CouponFeatures/
│   │   ├── Create/
│   │   └── GetById/
│   ├── ProductFeatures/
│   ├── Infrastructure/Data/Config/
│   ├── Migrations/
│   ├── Configurations/
│   ├── Extensions/
│   ├── Properties/launchSettings.json
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── api.http
│   └── Program.cs
└── tests/ISII.Tests/
    ├── ProductTest.cs
    ├── Unit/Domain/
    ├── Integration/Persistence/
    ├── Integration/Http/
    └── Support/
```

## Requisitos y verificación

Se necesita un SDK de .NET compatible con `net10.0` y acceso a los paquetes NuGet para restaurar. [global.json](global.json) solicita `10.0.100`, permite versiones preliminares y utiliza `rollForward: latestMajor`. En la Etapa 6.5 se verificó la solución con SDK `10.0.400`.

La ejecución actual utiliza SQLite y no requiere un servidor SQL Server ni un host Aspire. Tampoco es necesario instalar `dotnet ef` para compilar, ejecutar las pruebas o utilizar las migraciones automáticas del arranque.

Desde la raíz del repositorio, abrir la carpeta que contiene `ISII.slnx` y ejecutar:

```powershell
cd clean_architecture-main
dotnet restore ISII.slnx
dotnet build ISII.slnx
dotnet test ISII.slnx
```

## Ejecución de la API y SQLite

Desde la misma carpeta de la solución:

```powershell
dotnet run --project src/ISII.Web --launch-profile http
```

El perfil `http` de [launchSettings.json](src/ISII.Web/Properties/launchSettings.json) utiliza Development y `http://localhost:57379/`. En ese entorno se habilitan Scalar en `/scalar` y OpenAPI con la ruta `/openapi/{documentName}.json`.

[appsettings.json](src/ISII.Web/appsettings.json) define la conexión `AppDb` con `DataSource=ISII.db;Cache=Shared`. La ruta es relativa al directorio de trabajo del proceso. Puede sobrescribirse mediante `ConnectionStrings__AppDb` con una ruta absoluta propia; la [guía técnica](docs/Coupon.md#ejecucion-y-configuracion) explica cómo mantener alineadas la aplicación y las herramientas EF.

**Iniciar la API puede modificar la base:** el arranque aplica migraciones pendientes y ejecuta el seed. `RecreateOnStartup` está en `false`; activarlo en Development elimina la base y sus datos antes de migrar. Los errores de migración o seed se registran, pero el arranque puede continuar. Ver las [limitaciones conocidas](docs/Coupon.md#limitaciones-conocidas).

## Pruebas automatizadas

| Categoría | Casos |
|---|---:|
| Product originales | 7 |
| Dominio Coupon/CouponId | 30 |
| Persistencia SQLite | 25 |
| HTTP Coupon | 21 |
| **Total** | **83** |

En la Etapa 6.5, sobre el commit `bd58fc3`, restore y build finalizaron correctamente, con cero advertencias y errores. La suite aprobó **83 pruebas en cada una de tres ejecuciones consecutivas**, sin fallos ni omisiones. Estos resultados son evidencia de esa verificación, no una nueva ejecución durante la documentación.

Las pruebas de integración crean bases SQLite temporales propias. Las HTTP utilizan `WebApplicationFactory` y TestServer en proceso. No se dispone de un porcentaje de cobertura medido. La [guía técnica](docs/Coupon.md#pruebas-automatizadas) detalla el aislamiento y el alcance verificado.

## Documentación y referencias

- [Guía técnica de Coupon](docs/Coupon.md): arquitectura, dominio, persistencia, ejecución, pruebas y decisiones.
- [Ejemplos HTTP existentes](src/ISII.Web/api.http): Product y Cart. Los contratos detallados y los ejemplos ejecutables de Coupon quedan reservados para la Etapa 7.3.
- Referencias conservadas del README original: [Clean Architecture](https://github.com/ardalis/CleanArchitecture), [Vertical Slice Architecture](https://jimmybogard.com/vertical-slice-architecture/), [FastEndpoints](https://fast-endpoints.com/) y [Ardalis en YouTube](https://www.youtube.com/@Ardalis).
