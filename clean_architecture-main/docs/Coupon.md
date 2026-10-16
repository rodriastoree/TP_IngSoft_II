# Guía técnica de Coupon

[Volver al README](../README.md)

## Introducción y alcance

Coupon es la funcionalidad incorporada al proyecto base del Trabajo Práctico de Ingeniería de Software II facilitado por el profesor. Representa un cupón con identificador Guid, código, porcentaje de descuento, vencimiento, estado activo y fecha de creación.

Permite crear y recuperar cupones mediante HTTP, persistirlos en SQLite y verificar su comportamiento con pruebas automatizadas. Mantiene los patrones observables en Product y Cart del proyecto original.

No se implementaron aplicación de cupones a Cart u Order, cálculo de descuentos en compras, listado, actualización, eliminación ni un método público para desactivar cupones. El GET permite recuperar estados vencidos e inactivos ya persistidos.

La consigna del profesor no está disponible en el repositorio. Este documento describe código y resultados comprobados; no establece requisitos académicos ni certifica el cumplimiento de una rúbrica desconocida.

## Arquitectura

La [solución](../ISII.slnx) contiene `ISII.Web` e `ISII.Tests`. La aplicación se organiza mediante vertical slices: las carpetas de features reúnen endpoints, contratos y lógica de consulta. Dominio e infraestructura están separados por carpetas dentro del mismo proyecto web, sin proyectos físicos Core o UseCases.

| Componente | Responsabilidad y ubicación |
|---|---|
| Dominio | [Coupon](../src/ISII.Web/Domain/CouponAggregate/Coupon.cs) contiene estado y reglas; [CouponId](../src/ISII.Web/Domain/CouponAggregate/CouponId.cs) representa el identificador tipado |
| Endpoints | [Create](../src/ISII.Web/CouponFeatures/Create/CreateEndpoint.cs) y [GetById](../src/ISII.Web/CouponFeatures/GetById/GetByIdEndpoint.cs) reciben solicitudes y producen respuestas HTTP |
| Mediator y handler | [GetCouponQuery y GetCouponHandler](../src/ISII.Web/CouponFeatures/GetById/GetCouponHandler.cs) coordinan la consulta sin acceso directo a EF en el endpoint |
| Repositorios | `IRepository<Coupon>` e `IReadRepository<Coupon>` usan el [EfRepository genérico](../src/ISII.Web/Infrastructure/Data/EfRepository.cs) existente |
| Especificación | [CouponByIdSpec](../src/ISII.Web/Domain/CouponAggregate/Specifications/CouponByIdSpec.cs) expresa el filtro por identificador |
| EF Core | [AppDbContext](../src/ISII.Web/Infrastructure/Data/AppDbContext.cs), configuraciones y conversiones realizan el mapeo y la persistencia |
| SQLite | Almacena los datos y aplica las restricciones e índice de la tabla |

### Flujo POST

FastEndpoints → `CreateCouponRequestValidator` → `CreateEndpoint` → `Coupon.Create()` → `IRepository<Coupon>` → EF Core/SQLite → `CouponRecord`.

El validador se ejecuta antes del endpoint. Este genera el Guid, lo convierte con `CouponId.From()`, crea el agregado y llama al repositorio para agregarlo y guardar. Después construye la respuesta pública con el identificador convertido a Guid.

El POST **no utiliza un command ni un handler de Mediator**. Sigue el patrón de creación de Product. Propaga `CancellationToken` a las operaciones del repositorio.

Traduce de forma acotada las excepciones esperadas de creación a errores de validación. Para reconocer un duplicado exige DbUpdateException con códigos SQLite 19/2067, una única entrada EF correspondiente al mismo cupón en estado Added y el mensaje exacto de la restricción Coupons.Code. Otras excepciones continúan hacia el manejador existente.

### Flujo GET

`GetByIdEndpoint` → `GetCouponQuery` → Mediator → `GetCouponHandler` → `CouponByIdSpec` → `IReadRepository<Coupon>` → `CouponDto` → mapper → `CouponRecord`.

El endpoint maneja `Guid.Empty` antes de invocar `CouponId.From()`. El handler consulta mediante `FirstOrDefaultAsync`; si no encuentra el agregado, devuelve `Result.NotFound()`. Si existe, construye el DTO y el endpoint utiliza `ToGetByIdResult()` para mapear la respuesta.

La especificación filtra únicamente por identificador. No filtra por actividad ni vencimiento. El token de cancelación se propaga por Mediator hasta el repositorio.

## API HTTP de Coupon

Esta sección describe los contratos de los endpoints existentes. Los ejemplos de solicitudes y respuestas son **ilustrativos**: los identificadores y fechas mostrados no provienen de una ejecución real ni garantizan registros existentes. Los nombres JSON usan camelCase.

| Método y ruta | Responsabilidad | Autenticación |
|---|---|---|
| `POST /Coupons` | Crear y persistir un cupón válido | Acceso anónimo (`AllowAnonymous()`) |
| `GET /Coupons/{CouponId}` | Recuperar un cupón por identificador | Acceso anónimo (`AllowAnonymous()`) |

### POST /Coupons

Enviar un objeto JSON con `Content-Type: application/json`. Puede solicitarse JSON mediante `Accept: application/json`.

| Propiedad del request | Tipo .NET / JSON | Obligatoriedad y validación |
|---|---|---|
| `code` | `string` / cadena | Obligatorio, no nulo ni blanco; longitud de 3 a 30 unidades UTF-16; se conservan espacios y mayúsculas |
| `discountPercentage` | `decimal` / número | Obligatorio para una creación válida; mayor que 0 y menor o igual a 100; no se limita a dos decimales |
| `expirationDate` | `DateTime` / cadena ISO 8601 | Obligatoria, Kind UTC y estrictamente futura; el dominio vuelve a comprobar que sea posterior a la creación |

La omisión de propiedades no produce datos válidos: el código predeterminado queda vacío y el porcentaje y la fecha toman valores que el validador rechaza. Los detalles de las reglas se explican en Modelo de dominio.

Ejemplo ilustrativo de entrada; actualizar el vencimiento si deja de ser futuro y elegir otro código si ya existe:

```json
{
  "code": "DEMOCOUPON_20261016",
  "discountPercentage": 12.345678,
  "expirationDate": "2035-12-31T23:59:59Z"
}
```

`id`, `createdAt` e `isActive` no forman parte del request documentado. El servidor genera el identificador mediante `Guid.NewGuid()` y `CouponId.From()`, captura `DateTime.UtcNow` y establece el estado inicial activo.

#### 201 Created y CouponRecord

La creación correcta devuelve `Content-Type: application/json` y un encabezado `Location` relativo con el formato `/Coupons/{id}`.

| Propiedad de respuesta | Tipo .NET / JSON | Significado |
|---|---|---|
| `id` | `Guid` / cadena | Identificador generado, sin envoltorio Vogen |
| `code` | `string` / cadena | Código persistido sin normalización |
| `discountPercentage` | `decimal` / número | Porcentaje persistido; no se fuerza una escala de dos decimales |
| `expirationDate` | `DateTime` / cadena ISO 8601 UTC | Vencimiento del cupón |
| `isActive` | `bool` / booleano | Estado; `true` al crear |
| `createdAt` | `DateTime` / cadena ISO 8601 UTC | Instante de creación generado por el servidor |

Ejemplo ilustrativo de encabezados:

```http
HTTP/1.1 201 Created
Content-Type: application/json
Location: /Coupons/11111111-1111-4111-8111-111111111111
```

Ejemplo ilustrativo del cuerpo, con las seis propiedades de [CouponRecord](../src/ISII.Web/CouponFeatures/CouponRecord.cs):

```json
{
  "id": "11111111-1111-4111-8111-111111111111",
  "code": "DEMOCOUPON_20261016",
  "discountPercentage": 12.345678,
  "expirationDate": "2035-12-31T23:59:59Z",
  "isActive": true,
  "createdAt": "2026-10-16T12:00:00.1234567Z"
}
```

Las fechas devueltas tienen Kind UTC y se representan con `Z`; pueden contener fracciones de segundo. La cantidad de dígitos presentada puede variar. El porcentaje se conserva como decimal y se serializa como número JSON; el cliente debe evitar conversiones a punto flotante si necesita conservar su precisión. No se promete conservar ceros finales de la representación textual.

### GET /Coupons/{CouponId}

El parámetro obligatorio de ruta se vincula a un `Guid`. Los ejemplos usan la representación habitual de 36 caracteres con guiones. La solicitud no necesita cuerpo ni encabezado Content-Type; puede usar `Accept: application/json`.

Para consultar el cupón creado, copiar el GUID del `Location` real del POST. No utilizar el GUID ilustrativo de este documento como si identificara un registro existente.

Si existe, devuelve 200 y el mismo contrato `CouponRecord` de seis propiedades descrito para el POST:

```http
HTTP/1.1 200 OK
Content-Type: application/json
```

El GET devuelve también cupones vencidos o inactivos. **Recuperar un registro no significa que sea válido para aplicar un descuento.** La API no implementa esa aplicación a compras.

Un GUID bien formado que no identifica un registro devuelve 404 mediante `TypedResults.NotFound()`, sin un cuerpo JSON de error definido por Coupon. `Guid.Empty` devuelve 400 antes de convertir a CouponId. Un GUID mal formado se rechaza durante el binding de FastEndpoints y también devuelve 400, con un formato diferente.

### Estados HTTP y evidencia

| Estado | POST | GET | Evidencia y alcance |
|---|---|---|---|
| 201 Created | Creación correcta, cuerpo y Location | No corresponde | Prueba HTTP de creación y consulta posterior |
| 200 OK | No corresponde | Cupón existente, incluso vencido o inactivo | Pruebas HTTP de consulta y estados persistidos |
| 400 Bad Request | Código, porcentaje o fecha inválidos; fecha no UTC; errores de deserialización JSON | Guid.Empty o GUID mal formado | Validaciones y binding cubiertos por pruebas; ver precisión sobre JSON mal formado debajo |
| 404 Not Found | No corresponde | GUID válido sin registro | Prueba HTTP de recurso inexistente |
| 409 Conflict | Código ya persistido con la misma comparación BINARY | No corresponde | Prueba HTTP de duplicado secuencial |
| 500 Internal Server Error | Fallo inesperado no traducido a 400/409 | Fallo inesperado de consulta | Manejador compartido fuera de Development; probado mediante un fallo controlado de guardado en POST, sin prueba directa de fallo GET |

Las 21 pruebas HTTP existentes verifican los escenarios indicados; no cubren cualquier fallo posible. No se ejecutó la API ni se generaron respuestas nuevas para escribir estos ejemplos.

Los metadatos del GET declaran 400 como Problem Details, pero el binding de un GUID mal formado devuelve ErrorResponse. Los endpoints tampoco declaran explícitamente el 500 en sus metadatos de respuestas. Esta guía describe el comportamiento implementado; no afirma que OpenAPI represente todos esos casos. No se modificaron endpoints para corregir sus metadatos.

### Formatos de error

El tipo de contenido `application/problem+json` no implica que todos los cuerpos tengan la misma estructura. La API conserva tres formatos: `ErrorResponse` de FastEndpoints, Problem Details de ASP.NET Core e `InternalErrorResponse` de FastEndpoints.

#### 400 de validación o binding: ErrorResponse

Las validaciones del POST y el GUID mal formado del GET utilizan `statusCode`, `message` y un diccionario `errors` con listas de mensajes. Ejemplo ilustrativo para código vacío, basado en el mensaje del validador:

```json
{
  "statusCode": 400,
  "message": "One or more errors occurred!",
  "errors": {
    "code": [
      "Coupon code is required and must not be blank."
    ]
  }
}
```

Los errores de fecha o de porcentaje se asocian al campo correspondiente. Los de deserialización pueden usar otra clave y mensajes del serializador; no se fija aquí su texto completo. En el GET con GUID mal formado, el error corresponde al parámetro `CouponId`.

El JSON sintácticamente mal formado y los valores que no pueden convertirse al tipo solicitado se rechazan durante la deserialización. FastEndpoints 7.1.1 transforma por defecto `JsonException` a un error con estado 400, sin configuración que lo desactive en este proyecto; este comportamiento se contrastó con la documentación instalada y las [opciones de binding de esa versión](https://raw.githubusercontent.com/FastEndpoints/FastEndpoints/v7.1.1/Src/Library/Config/BindingOptions.cs).

La suite comprueba una fecha con texto no convertible dentro de un JSON válido; no contiene un caso específico de JSON truncado o sintácticamente roto. Por eso el 400 de este último se documenta por el comportamiento del framework, no como una prueba HTTP ejecutada. Todos los cuerpos de los ejemplos ejecutables son JSON válido.

#### 400 de Guid.Empty: Problem Details

Este caso utiliza `TypedResults.Problem()` con título, estado y detalle explícitos. Extracto ilustrativo de esos campos; el framework también puede aportar campos estándar como `type`:

```json
{
  "title": "Invalid coupon ID",
  "status": 400,
  "detail": "Coupon ID must not be empty."
}
```

No tiene el diccionario `errors` del formato anterior. No debe confundirse con el 400 que se produce al intentar vincular un GUID mal formado.

#### 409 de código duplicado: Problem Details

Extracto ilustrativo de los campos públicos definidos por el endpoint:

```json
{
  "title": "Coupon code conflict",
  "status": 409,
  "detail": "Ya existe un cupón con ese código."
}
```

La unicidad es sensible a mayúsculas mediante BINARY y no normaliza el código. Un código idéntico produce conflicto; códigos que solo difieren en mayúsculas/minúsculas pueden coexistir. La restricción de base protege la unicidad, sin depender de una consulta previa. No todo DbUpdateException se convierte en 409; la detección acotada se explica en Arquitectura y Persistencia.

#### 500 inesperado: InternalErrorResponse

Fuera de Development, el manejador existente usa `useGenericReason: true`. Ejemplo ilustrativo de su formato, diferente de Problem Details:

```json
{
  "status": "Internal Server Error!",
  "code": 500,
  "reason": "An unexpected error has occurred.",
  "note": "See application log for stack trace."
}
```

La nota remite al diagnóstico del servidor; no incluye la traza en el cuerpo. El motivo público es genérico. Las pruebas de POST verifican ausencia de la excepción simulada, SQL, connection strings, rutas internas y trazas en el escenario cubierto. No se exponen aquí datos de diagnóstico reales ni se afirma que todos los fallos posibles estén probados.

Development utiliza la página de excepciones del proyecto base, por lo que el formato y la protección anteriores corresponden a Production y otros entornos distintos de Development. No hay un endpoint público para provocar el 500; su verificación automatizada utiliza el interceptor aislado del proyecto de pruebas.

### UTC y uso manual de api.http

Usar fechas ISO 8601 con `Z` para expresar UTC. Una cadena sin sufijo de zona produce Kind Unspecified y se rechaza aunque su hora numérica sea futura. Un offset no debe asumirse equivalente a Kind UTC al deserializar a DateTime; los ejemplos válidos usan `Z`.

El archivo [api.http](../src/ISII.Web/api.http) conserva los ejemplos anteriores de Product y Cart y agrega Coupon. Requiere un cliente que interprete variables `@nombre = valor` e interpolaciones `{{nombre}}`; no utiliza scripts, funciones dinámicas ni captura automática de respuestas.

1. Ajustar el host si no se usa el perfil `http`, cuyo puerto verificado es 57379. La preparación del servidor corresponde al procedimiento de Ejecución y configuración.
2. Elegir `Coupon_Code` no existente y comprobar que `Coupon_ExpirationUtc` siga siendo futura. La fecha 2035-12-31 tiene margen para la fecha de esta documentación, pero debe actualizarse cuando corresponda.
3. Ejecutar primero el POST válido. Copiar el GUID de su Location en `Coupon_Id`; esa variable contiene inicialmente un valor ilustrativo.
4. Ejecutar el GET existente. Para el conflicto, repetir el POST con el mismo `Coupon_Code` ya creado; si el primer POST devuelve 409, cambiar el código para iniciar otra demostración.
5. Ejecutar individualmente los casos negativos. Para esperar 404, `Coupon_MissingId` debe ser un GUID que no exista en la base seleccionada; el valor ilustrativo no constituye una garantía de ausencia.

Los ejemplos no preparan ni limpian bases y no deben ejecutarse en bloque suponiendo que son pruebas aisladas. Un POST válido enviado manualmente persiste datos en la base configurada. No se ejecutaron estas solicitudes ni se modificó ninguna base durante la Etapa 7.3.

## Modelo de dominio

`Coupon` hereda de `EntityBase<Coupon, CouponId>` e implementa `IAggregateRoot`. Tiene un constructor público y una fábrica estática `Create()` que delega en ese mismo constructor. Ambos comparten las reglas. El constructor privado sin parámetros permite la materialización de EF Core.

| Propiedad | Tipo .NET | Comportamiento |
|---|---|---|
| `Id` | `CouponId` | Identificador heredado; el POST lo genera a partir de `Guid.NewGuid()` |
| `Code` | `string` | Código conservado tal como se recibe, sin normalización |
| `DiscountPercentage` | `decimal` | Porcentaje conservado sin redondeo explícito |
| `ExpirationDate` | `DateTime` | Vencimiento UTC validado al crear |
| `IsActive` | `bool` | Inicialmente `true` |
| `CreatedAt` | `DateTime` | Generado internamente con `DateTime.UtcNow` |

`Code`, `DiscountPercentage`, `ExpirationDate`, `IsActive` y `CreatedAt` tienen setters privados. El identificador conserva la propiedad heredada del patrón original; no se presenta como un setter privado propio de Coupon.

### Reglas de creación

1. El identificador debe estar inicializado: el constructor accede a `id.Value`, protegido por Vogen.
2. El código no puede ser nulo, vacío ni contener solo espacios en blanco.
3. Su longitud debe ser de 3 a 30 según `string.Length`, es decir, unidades UTF-16. No se aplica `Trim()` ni conversión de mayúsculas/minúsculas.
4. El porcentaje debe ser mayor que cero y menor o igual a 100. No se limita a dos decimales.
5. El vencimiento debe tener `DateTimeKind.Utc` y ser estrictamente posterior al `CreatedAt` capturado durante la creación.
6. El estado inicial es activo y la fecha de creación es UTC.

`CreateCouponRequestValidator`, basado en FastEndpoints/FluentValidation, verifica código, porcentaje, presencia del vencimiento, Kind UTC y fecha futura antes de ejecutar el endpoint. El dominio conserva sus propias guard clauses. El tiempo puede avanzar entre ambas validaciones; por eso el endpoint también traduce las excepciones esperadas de creación a HTTP 400.

### CouponId y excepciones

`CouponId` es un `readonly partial struct` con `[ValueObject<Guid>]`. Su validación rechaza `Guid.Empty`. Vogen genera las operaciones de construcción y acceso, y los conversores EF se declaran en [VogenEfCoreConverters](../src/ISII.Web/Infrastructure/Data/Config/VogenEfCoreConverters.cs).

Las excepciones relevantes verificadas son `ArgumentNullException` para código nulo, `ArgumentException` para los demás argumentos inválidos del dominio y `Vogen.ValueObjectValidationException` al construir un `CouponId` desde un Guid vacío. El acceso a un identificador sin inicializar también está protegido por Vogen.

### Vigencia con IsValidAt()

`IsValidAt(currentDate)` exige una fecha de consulta UTC y devuelve `IsActive && currentDate < ExpirationDate`.

Un cupón activo es válido antes del vencimiento y deja de serlo exactamente en ese instante. Un cupón inactivo devuelve `false`. La implementación no exige que la fecha consultada sea posterior a `CreatedAt`. El GET no utiliza este método para excluir resultados.

## Persistencia

[AppDbContext](../src/ISII.Web/Infrastructure/Data/AppDbContext.cs) expone `DbSet<Coupon> Coupons` y aplica las configuraciones de su assembly. [CouponConfiguration](../src/ISII.Web/Infrastructure/Data/Config/CouponConfiguration.cs) define el mapeo de la tabla `Coupons`.

### Tabla y conversiones

| Columna | Tipo SQLite | Restricciones y conversión |
|---|---|---|
| `Id` | `TEXT` | No nulo, clave primaria; conversión Vogen de CouponId a Guid y generador `VogenGuidIdValueGenerator` |
| `Code` | `TEXT` | No nulo, longitud máxima configurada de 30, colación BINARY e índice único |
| `DiscountPercentage` | `TEXT` | No nulo; representación decimal sin pérdida de los dígitos verificados en las pruebas |
| `ExpirationDate` | `INTEGER` | No nulo; ticks de DateTime UTC |
| `IsActive` | `INTEGER` | No nulo; valores permitidos 0 y 1 |
| `CreatedAt` | `INTEGER` | No nulo; ticks de DateTime UTC |

El conversor de fechas exige Kind UTC al escribir; no convierte automáticamente una fecha local o sin Kind a UTC. Al leer construye `new DateTime(ticks, DateTimeKind.Utc)`. Los ticks conservan la precisión de DateTime y permiten comparar las dos fechas mediante SQL.

El porcentaje utiliza `decimal` en .NET y `TEXT` en SQLite. La prueba de ida y vuelta verifica un valor con seis decimales sin redondeo. No se utiliza un tipo binario de punto flotante para almacenar ese porcentaje.

### Unicidad y CHECK

El índice `IX_Coupons_Code` es único y utiliza la colación BINARY del código. Los códigos que difieren por mayúsculas y minúsculas pueden coexistir. La base constituye la protección definitiva contra duplicados; el POST no depende de una consulta previa.

| Restricción | Qué exige SQLite |
|---|---|
| `CK_Coupons_Code_MaxLength` | `length(Code) <= 30` |
| `CK_Coupons_Dates` | Ambas fechas tienen almacenamiento entero, ticks entre 0 y 3155378975999999999 y vencimiento posterior a creación |
| `CK_Coupons_IsActive` | Estado 0 o 1 |

Las restricciones de SQLite no reproducen todas las reglas del dominio. No hay CHECK para código no blanco, longitud mínima ni rango porcentual. Además, `length()` de SQLite cuenta puntos de código Unicode, mientras el dominio usa unidades UTF-16. La validación del dominio sigue siendo necesaria.

La base compara vencimiento con creación, no con el reloj actual. Por eso puede conservar y materializar cupones que hayan vencido. EF los recupera mediante el constructor privado, sin ejecutar nuevamente las reglas de creación pública. Lo mismo permite recuperar un estado inactivo ya persistido.

### Migraciones

La migración [20261010023149_AddCoupon](../src/ISII.Web/Migrations/20261010023149_AddCoupon.cs) crea tabla, clave, CHECK e índice. Su `Down()` elimina la tabla `Coupons`. Se mantiene la migración previa [20260829135436_Initial](../src/ISII.Web/Migrations/20260829135436_Initial.cs).

Las pruebas aplican ambas migraciones a una base nueva y comprueban que no queden migraciones pendientes. No reemplazan la verificación del esquema por `EnsureCreated()` ni por EF InMemory.

<a id="ejecucion-y-configuracion"></a>

## Ejecución y configuración

### Requisitos y comandos

La solución está en `clean_architecture-main/ISII.slnx`, respecto de la raíz del repositorio. Se requiere un SDK compatible con .NET 10 y acceso a NuGet para restaurar. [global.json](../global.json) declara SDK `10.0.100`, `rollForward: latestMajor` y `allowPrerelease: true`. El SDK utilizado en la verificación de la Etapa 6.5 fue `10.0.400`.

Desde la raíz del repositorio:

```powershell
cd clean_architecture-main
dotnet restore ISII.slnx
dotnet build ISII.slnx
dotnet test ISII.slnx
```

Para iniciar la aplicación desde esa carpeta:

```powershell
dotnet run --project src/ISII.Web --launch-profile http
```

El perfil [http](../src/ISII.Web/Properties/launchSettings.json) configura Development y `http://localhost:57379/`. La ejecución actual utiliza SQLite; no necesita un servidor de base de datos separado ni Aspire.

### Conexión y ubicación del archivo

[appsettings.json](../src/ISII.Web/appsettings.json) define `ConnectionStrings:AppDb` como `DataSource=ISII.db;Cache=Shared`. `DataSource` es relativo al directorio de trabajo del proceso, no a la ubicación de este documento. Cambiar el directorio desde el que trabajan las herramientas puede seleccionar otro archivo de base.

Para usar una ubicación inequívoca, configurar `ConnectionStrings__AppDb` con una cadena SQLite cuyo `DataSource` sea una ruta absoluta elegida en el equipo. ASP.NET Core la utiliza como sobrescritura de configuración. La [AppDbContextFactory](../src/ISII.Web/Infrastructure/Data/AppDbContextFactory.cs), utilizada por herramientas EF, lee esa misma variable; si falta, usa la conexión relativa predeterminada.

La factory de diseño no carga por sí misma los archivos appsettings. Si se utiliza una conexión distinta, debe mantenerse alineada con la aplicación mediante la variable mencionada. Esta documentación no requiere modificar archivos de configuración ni instalar herramientas. `dotnet ef` no es necesario para restore, build, test o las migraciones automáticas del arranque; tampoco hay un manifiesto local de esa herramienta versionado.

### Arranque y entornos

[MiddlewareConfig](../src/ISII.Web/Configurations/MiddlewareConfig.cs) ejecuta `MigrateAsync()` y luego `SeedData.InitializeAsync()` durante el arranque. Iniciar la API puede crear o modificar la base seleccionada.

`DatabaseOptions.RecreateOnStartup` tiene valor predeterminado `false` y [appsettings.Development.json](../src/ISII.Web/appsettings.Development.json) también lo establece en `false`.

**Advertencia: activar RecreateOnStartup en Development elimina la base y sus datos mediante EnsureDeletedAsync() antes de aplicar migraciones.** No debe activarse sobre datos que se quieran conservar. La condición de eliminación no se ejecuta en Production.

| Entorno | Comportamiento existente |
|---|---|
| Development | Página de excepciones y diagnóstico de servicios; OpenAPI con ruta `/openapi/{documentName}.json` y Scalar en `/scalar` |
| Production y otros entornos distintos de Development | Manejador FastEndpoints con `useGenericReason: true` y HSTS; no se habilita la documentación mediante la rama de Development |

El perfil HTTP fija Development. Para seleccionar Production se debe omitir ese perfil y configurar el entorno correspondiente; de lo contrario, el perfil vuelve a establecer Development.

El mensaje genérico del 500 fue verificado mediante pruebas HTTP en Production. Development conserva la página de excepciones del proyecto base, por lo que no debe atribuirse a ese entorno la misma protección pública.

Existe una limitación heredada: el bloque de migración/seed captura `Exception`, registra el error y no lo relanza. El host puede continuar aunque la inicialización de datos haya fallado. La ausencia de un error de arranque no demuestra por sí sola que el esquema esté disponible.

<a id="pruebas-automatizadas"></a>

## Pruebas automatizadas

Las pruebas pertenecen a [ISII.Tests](../tests/ISII.Tests/ISII.Tests.csproj), que referencia el proyecto web y utiliza xUnit. Las versiones y referencias se mantienen en los archivos de proyecto y en [Directory.Packages.props](../Directory.Packages.props).

| Categoría y archivos | Casos | Comportamientos principales |
|---|---:|---|
| [ProductTest](../tests/ISII.Tests/ProductTest.cs) | 7 | Constructor, fábrica, actualización de nombre/precio y conservación de otros valores del modelo original |
| [CouponTests](../tests/ISII.Tests/Unit/Domain/CouponTests.cs) y [CouponIdTests](../tests/ISII.Tests/Unit/Domain/CouponIdTests.cs) | 30 | Creación, código y UTF-16, porcentajes, UTC, excepciones, identificador y límites de vigencia |
| [CouponPersistenceTests](../tests/ISII.Tests/Integration/Persistence/CouponPersistenceTests.cs) | 25 | Migraciones, repositorio, conversiones, decimal, fechas, índice BINARY, restricciones, estados persistidos y aislamiento |
| [CouponEndpointsTests](../tests/ISII.Tests/Integration/Http/CouponEndpointsTests.cs) | 21 | Estados HTTP, creación y consulta, Location, validaciones, duplicados, mayúsculas, vencidos/inactivos y respuesta 500 segura |
| **Total** | **83** | Los casos parametrizados se cuentan individualmente |

### Aislamiento y recursos

[SqliteTestDatabase](../tests/ISII.Tests/Support/SqliteTestDatabase.cs) crea un subdirectorio temporal único por instancia y un archivo `coupon.sqlite3` con ruta absoluta. xUnit crea una instancia de clase por caso. La conexión utiliza `Pooling=False`; no utiliza `ISII.db` ni una base compartida entre casos.

La infraestructura aplica migraciones reales, mantiene los contextos que crea y los libera durante `DisposeAsync()`. La limpieza valida la pertenencia de las rutas, elimina únicamente el archivo propio y sus archivos auxiliares conocidos, y borra el directorio sin recursividad. Una excepción de inicialización también activa la limpieza; la suite HTTP utiliza bloques de liberación para cliente, factory y base.

[CouponWebApplicationFactory](../tests/ISII.Tests/Support/CouponWebApplicationFactory.cs) construye el host de `Program` mediante `WebApplicationFactory<Program>`. Su cliente ejecuta el pipeline real con TestServer en proceso, sin iniciar un servidor externo. Reemplaza los registros de AppDbContext, sus opciones y su configuración EF, utiliza la base del caso, fuerza Production y `RecreateOnStartup=false` y comprueba la ruta física con `PRAGMA database_list`.

El interceptor artificial de error 500 vive solo en el proyecto de pruebas. Se arma expresamente para una inserción, pertenece a la factory del caso y se consume una vez. Se comprueba la respuesta genérica, la ausencia de inserción y una creación/consulta posterior exitosa. Los repositorios y endpoints permanecen reales.

### Evidencia confirmada

En la Etapa 6.5 se verificó el commit `bd58fc3` mediante `dotnet restore ISII.slnx`, `dotnet build ISII.slnx` y tres ejecuciones consecutivas de `dotnet test ISII.slnx`.

| Verificación | Resultado registrado |
|---|---|
| Restore | Correcto, sin advertencias ni errores informados |
| Build | Correcto, cero advertencias y cero errores |
| Primera ejecución | 83 aprobadas, 0 fallidas, 0 omitidas |
| Segunda ejecución | 83 aprobadas, 0 fallidas, 0 omitidas |
| Tercera ejecución | 83 aprobadas, 0 fallidas, 0 omitidas |

La misma auditoría confirmó `System.Security.Cryptography.Pkcs` 10.0.13 y `SQLitePCLRaw.lib.e_sqlite3` 3.53.3, con NuGetAudit activo y sin vulnerabilidades reportadas en los orígenes consultados. Ese resultado corresponde a la auditoría de la Etapa 6.5; no garantiza el estado futuro de los avisos.

Estos resultados no representan nuevas ejecuciones durante las etapas documentales 7.2 y 7.3. `coverlet.collector` está referenciado, pero no se recolectó cobertura en aquellas corridas. No se dispone de un porcentaje medido ni se afirma cobertura del 100%.

## Decisiones técnicas

| Decisión observable | Propósito |
|---|---|
| CouponId basado en Vogen | Distinguir el identificador del agregado de otros Guid y validar el valor vacío |
| Índice único en SQLite | Hacer cumplir la unicidad al escribir, sin depender de una comprobación previa vulnerable a carreras |
| UTC y ticks | Evitar ambigüedad de Kind y conservar precisión al guardar y recuperar fechas |
| Decimal y almacenamiento TEXT | Representar el porcentaje y conservar los dígitos sin redondeo de punto flotante |
| Bases SQLite temporales reales | Verificar migraciones y restricciones del proveedor, con datos aislados y sin afectar la base de la aplicación |
| Flujos POST y GET diferentes | Mantener los patrones existentes: creación con repositorio como Product y consulta con Mediator, handler y especificación como Product/Cart |

La unicidad evita dos registros con el mismo código aunque compitan escrituras. Las pruebas actuales verifican duplicados secuenciales; no demuestran que toda solicitud concurrente vaya a responder necesariamente 409, porque pueden intervenir otros errores del proveedor.

<a id="limitaciones-conocidas"></a>

## Limitaciones conocidas

### Alcance de Coupon y pruebas

- Coupon no se aplica a compras ni modifica Cart u Order. No hay método público de desactivación; las pruebas de estados inactivos preparan datos mediante SQL en sus bases propias.
- No hay pruebas directas de la carrera entre validación y creación, de todas las ramas de traducción de excepciones del dominio ni de un DbUpdateException no duplicado atravesando el POST. El 500 probado utiliza una InvalidOperationException controlada.
- No se ejecutan solicitudes duplicadas simultáneas. La identificación del conflicto también depende del mensaje exacto del proveedor SQLite y debe revisarse al cambiar sus versiones.
- La fecha de creación depende del reloj del sistema. No hay reloj inyectable para probar de forma determinista un vencimiento exactamente igual al instante interno de creación.
- No hay porcentaje de cobertura medido. Las siete pruebas originales de Product verifican su dominio; no certifican toda su API.

### Comportamientos y remanentes del proyecto base

- La inicialización de base puede fallar y permitir que el host continúe; es un comportamiento heredado del manejo de migración/seed.
- Development utiliza una página de excepciones para diagnóstico. La protección genérica del 500 documentada y probada corresponde a Production.
- Persisten referencias de plantilla: comentarios SQL Server/Aspire en el registro que efectivamente usa SQLite y un Solution Item `../MinimalClean.nuspec` inexistente. No se corrigieron código ni solución en esta etapa documental.
- La configuración NsDepCop restringe el acceso a Infrastructure para ProductFeatures y CartFeatures, pero no incluye una regla equivalente para CouponFeatures. No se observó una dependencia directa de CouponFeatures hacia ese namespace en la revisión.

### Información académica pendiente

Sin la consigna no pueden confirmarse formato de entrega, criterios de evaluación ni exigencias de defensa oral. Las explicaciones de decisiones y pruebas sirven de apoyo técnico, pero no sustituyen esas instrucciones.
