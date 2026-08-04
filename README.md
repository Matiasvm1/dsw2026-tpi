# Trabajo Práctico Integrador — Sistema de Turnos Médicos

## Desarrollo de Software 2026 — UTN FRT

API REST para la gestión de turnos médicos: administración de especialidades y médicos,
configuración de disponibilidades horarias, y reserva/cancelación de turnos por parte de
los pacientes.

Acceso al [documento del enunciado](https://frtutneduar-my.sharepoint.com/:b:/g/personal/franciscovicente_doc_frt_utn_edu_ar/IQD-5kaAARqnT5eL7EnPMCPgAX2LFXXX6e3p-u1C43z5rsQ?e=lbbpnz)

---

## Integrantes

| Legajo | Apellido y Nombre |
|--------|-------------------|
| 56552  | Villafañe, Matías |
| 53291  | Khouri, José Nicolás |
| 58150  | Ortiz Cancino, Valentín |
| 58266  | Zurita, Eduardo Ezequiel |

---

## Stack

| Componente | Versión / Tecnología |
|------------|----------------------|
| Framework  | .NET 10 (`net10.0`) |
| API        | ASP.NET Core Web API (controllers) |
| ORM        | Entity Framework Core 10 |
| Base de datos | SQL Server (LocalDB por defecto) |
| Autenticación | ASP.NET Core Identity + JWT Bearer |
| Logging    | Serilog (consola + archivo) |
| Documentación | Swagger / Swashbuckle |
| Tests      | xUnit + Moq |

### Arquitectura

Solución en capas, con la regla de dependencia apuntando hacia el dominio:

```
Dsw2026Tpi.Api            → Controllers, middlewares, configuración, composición
Dsw2026Tpi.Application    → Services (casos de uso), DTOs, interfaces
Dsw2026Tpi.Domain         → Entidades, reglas de negocio, IPersistence
Dsw2026Tpi.Data           → EF Core: DbContexts, migraciones, repositorio
Dsw2026Tpi.CrossCutting   → Excepciones, códigos de error, identidad, helpers
Dsw2026Tpi.Application.Tests → Tests unitarios de los Services
```

---

## Requisitos previos

- **.NET SDK 10.0** o superior (el proyecto usa `Dsw2026Tpi.slnx`, formato de solución
  que requiere un SDK reciente).
- **SQL Server**. Por defecto se usa **LocalDB**, que viene con Visual Studio o con el
  instalable *SQL Server Express LocalDB*.
- **EF Core Tools**, para aplicar las migraciones:
  ```bash
  dotnet tool install --global dotnet-ef
  ```

---

## Puesta en marcha

### 1. Clonar y restaurar

```bash
git clone <url-del-fork>
cd dsw2026-tpi
dotnet restore
```

### 2. Configurar la cadena de conexión

La configuración sensible (cadena de conexión, JWT, admin semilla, CORS) vive en
`Dsw2026Tpi.Api/appsettings.Development.json`. La cadena por defecto apunta a LocalDB:

```json
"ConnectionStrings": {
  "DefaultConnection": "Data Source=(localdb)\\MSSQLLocalDB;Database=Dsw2026Tpi;Integrated Security=True;Connect Timeout=30;Encrypt=True;Trust Server Certificate=True"
}
```

Si usás otra instancia de SQL Server, cambiá ese valor.

> **Importante:** la cadena de conexión, la clave del JWT y las credenciales del admin
> semilla están definidas **únicamente** en `appsettings.Development.json`. La aplicación
> debe ejecutarse con `ASPNETCORE_ENVIRONMENT=Development` (es lo que hacen los perfiles
> de `launchSettings.json`). Fuera de ese entorno no hay cadena de conexión configurada
> y además Swagger no se monta.

### 3. Aplicar las migraciones

La solución tiene **dos `DbContext`** —uno de negocio y uno de Identity— sobre la misma
base de datos. Por eso hay que actualizar **los dos por separado**; `dotnet ef database
update` sin `--context` falla porque no puede decidir cuál usar.

```bash
# Contexto de Identity (usuarios, roles)
dotnet ef database update --context AuthenticationDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api

# Contexto de negocio (especialidades, médicos, turnos)
dotnet ef database update --context Dsw2026TpiDbContext --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api
```

Los roles (`Administrador`, `Paciente`) se siembran automáticamente desde
`Dsw2026Tpi.Data/Sources/roles.json` al iniciar la aplicación.

### 4. Ejecutar

```bash
dotnet run --project Dsw2026Tpi.Api
```

| Perfil | URL |
|--------|-----|
| `http`  | http://localhost:5278 |
| `https` | https://localhost:7075 |

- **Swagger UI:** `/swagger` (solo en entorno Development)
- **Health check:** `/health-check`

Al arrancar, la aplicación crea el usuario administrador semilla definido en la sección
`SeedAdmin` de `appsettings.Development.json`. Usá esas credenciales en
`POST /api/auth/admin/login` para obtener el primer token.

### 5. Ejecutar los tests

```bash
dotnet test
```

---

## Convenciones de la API

- **Base path:** todos los recursos cuelgan de `/api`.
- **Serialización:** JSON en `camelCase`.
- **Identificadores:** `GUID`.
- **Autenticación:** JWT Bearer en el header `Authorization: Bearer <token>`.
  Solo los dos endpoints de login son públicos.
- **Baja lógica:** los `DELETE` no borran físicamente; marcan la entidad como eliminada
  y devuelven `200 OK` con el texto `"ok"`.
- **Fechas y horas:** siempre como string — fecha `yyyy-MM-dd`, hora `HH:mm`.

### Paginación

Los listados aceptan `pageSize` y `pageIndex` por query string. `pageIndex` es base 1,
`pageSize` por defecto es 10 y el máximo es 100.

```json
{
  "pageSize": 10,
  "pageIndex": 1,
  "total": 42,
  "data": [ ... ]
}
```

### Formato de error

Todas las excepciones se normalizan en un middleware global:

```json
{
  "errorCode": "VALIDATION_ERROR",
  "message": "Error de validación",
  "details": [
    { "field": "name", "issue": "El nombre debe tener entre 3 y 100 caracteres" }
  ]
}
```

`details` es opcional y aparece cuando el error tiene desglose por campo.

### Rate limiting

| Alcance | Límite | Partición |
|---------|--------|-----------|
| Login de administrador | 5 / minuto | IP |
| Login de paciente | 10 / minuto | IP |
| Reserva de turno | 5 / minuto | Paciente autenticado |
| General | 100 / minuto | Usuario o IP |

Al excederse se devuelve `429 Too Many Requests` con el sobre de error estándar
(`RATE_LIMIT_EXCEEDED`). Los límites se configuran en la sección `RateLimiting` de
`appsettings.json`.

---

## Endpoints

Leyenda de acceso: 🔓 público · 🔑 autenticado (cualquier rol) · 👤 solo `ADMINISTRADOR`

### Autenticación — `/api/auth`

| Método | Ruta | Acceso | Descripción |
|--------|------|--------|-------------|
| `POST` | `/api/auth/admin/login` | 🔓 | Login de administrador |
| `POST` | `/api/auth/patient/login` | 🔓 | Login de paciente (auto-registro si no existe) |
| `POST` | `/api/auth/admin/register` | 👤 | Alta de un nuevo administrador |

**`POST /api/auth/admin/login`** — la contraseña debe tener al menos 8 caracteres.

```jsonc
// request
{ "email": "admin@system.com", "password": "Admin1234!" }

// 200 OK
{ "token": "eyJhbGciOi...", "role": "ADMINISTRADOR" }
```

**`POST /api/auth/patient/login`** — el `dni` es numérico, de 7 u 8 dígitos. Si el
paciente no existe se registra automáticamente (RN06).

```jsonc
// request
{ "email": "paciente@mail.com", "dni": 12345678 }

// 200 OK
{ "token": "eyJhbGciOi...", "role": "PACIENTE" }
```

**`POST /api/auth/admin/register`** — devuelve `200 OK` con el email creado.

```jsonc
// request
{ "email": "nuevo@system.com", "password": "Otra1234!" }
```

---

### Especialidades — `/api/specialties`

| Método | Ruta | Acceso | Respuesta |
|--------|------|--------|-----------|
| `GET` | `/api/specialties` | 🔑 | `200` paginado |
| `POST` | `/api/specialties` | 👤 | `201` la especialidad creada |
| `PUT` | `/api/specialties/{id}` | 👤 | `200` la especialidad actualizada |
| `DELETE` | `/api/specialties/{id}` | 👤 | `200` `"ok"` (baja lógica) |

`GET` acepta `?name=` para filtrar, además de la paginación.
El paciente **puede** listar especialidades: las necesita para reservar un turno.

```jsonc
// request de POST / PUT — name: 3-100 caracteres · description: 10-100
{ "name": "Cardiología", "description": "Estudio y tratamiento del corazón" }

// response
{ "id": "3f1a...", "name": "Cardiología", "description": "Estudio y tratamiento del corazón" }
```

---

### Médicos — `/api/doctors`

| Método | Ruta | Acceso | Respuesta |
|--------|------|--------|-----------|
| `GET` | `/api/doctors` | 🔑 | `200` paginado |
| `POST` | `/api/doctors` | 👤 | `201` el médico creado |
| `PUT` | `/api/doctors/{id}` | 👤 | `200` el médico actualizado |
| `DELETE` | `/api/doctors/{id}` | 👤 | `200` `"ok"` (baja lógica) |
| `GET` | `/api/doctors/{id}/availabilities` | 🔑 | `200` disponibilidades del médico |

`GET` acepta `?name=` para filtrar, además de la paginación.

```jsonc
// request de POST / PUT — el request pide specialityId; la respuesta anida specialty
{ "name": "Dr. Pérez", "licenseNumber": "MP-12345", "specialityId": "3f1a..." }

// response
{
  "id": "8c2b...",
  "name": "Dr. Pérez",
  "licenseNumber": "MP-12345",
  "specialty": { "id": "3f1a...", "name": "Cardiología" }
}
```

```jsonc
// GET /api/doctors/{id}/availabilities — 200 OK
[
  { "id": "a91f...", "day": "LUNES", "startTime": "09:00", "endTime": "13:00" }
]
```

---

### Disponibilidades — `/api/availabilities`

| Método | Ruta | Acceso | Respuesta |
|--------|------|--------|-----------|
| `POST` | `/api/availabilities` | 👤 | `201` el schedule resultante |
| `PUT` | `/api/availabilities` | 👤 | `200` el schedule resultante |

Configura la agenda mensual de un médico. El sistema genera los turnos disponibles
(*slots*) a partir de los días y horarios indicados, salteando los feriados definidos en
la sección `NonWorkingDays` de `appsettings.json`.

El `PUT` reconfigura la agenda **preservando los turnos ya reservados**: sobrescribe
únicamente los slots libres.

```jsonc
// request
{
  "doctorId": "8c2b...",
  "days": [
    { "day": "LUNES", "startTime": "09:00", "endTime": "13:00" },
    { "day": "MIERCOLES", "startTime": "14:00", "endTime": "18:00" }
  ]
}

// response — el schedule en efecto
{
  "doctorId": "8c2b...",
  "year": 2026,
  "month": 8,
  "days": [
    { "day": "LUNES", "startTime": "09:00", "endTime": "13:00" },
    { "day": "MIERCOLES", "startTime": "14:00", "endTime": "18:00" }
  ]
}
```

---

### Turnos — `/api/appointments`

| Método | Ruta | Acceso | Respuesta |
|--------|------|--------|-----------|
| `POST` | `/api/appointments` | 🔑 | `201` el turno reservado |
| `DELETE` | `/api/appointments/{id}` | 🔑 | `200` `"ok"` (cancelación) |
| `GET` | `/api/appointments/patient?dni=` | 🔑 | `200` turnos del paciente |
| `GET` | `/api/appointments?date=` | 👤 | `200` paginado, turnos del día |
| `GET` | `/api/appointments/search` | 👤 | `200` paginado, búsqueda con filtros |

Un paciente solo puede consultar y cancelar **sus propios** turnos; si intenta operar
sobre los de otro recibe `403`.

**`POST /api/appointments`** — reserva. Limitado a 5 por minuto por paciente.

```jsonc
// request
{
  "doctorId": "8c2b...",
  "availabilitySlotId": "a91f...",
  "patient": { "dni": 12345678 },
  "reason": "Control anual"
}

// 201 Created
{
  "id": "7d4e...",
  "date": "2026-08-10",
  "startTime": "09:00",
  "endTime": "09:30",
  "status": "BOOKED",
  "reason": "Control anual",
  "doctor": { "id": "8c2b...", "name": "Dr. Pérez" },
  "specialty": { "id": "3f1a...", "name": "Cardiología" },
  "patient": { "id": "5b8c...", "dni": "12345678", "fullName": "Juan Pérez" }
}
```

Estados posibles: `BOOKED` · `CANCELLED` · `ATTENDED` · `NO_SHOW`.

**`GET /api/appointments/search`** — filtros opcionales y combinables:
`specialtyId`, `doctorId`, `dni`, `date`, más paginación.

```jsonc
// data[] — shape propio del listado del administrador
{
  "appointmentsId": "7d4e...",
  "appointmentsStatus": "BOOKED",
  "patient": { "dni": 12345678, "fullName": "Juan Pérez" },
  "doctor": {
    "doctorId": "8c2b...",
    "name": "Dr. Pérez",
    "specialty": { "specialtyId": "3f1a...", "name": "Cardiología" }
  },
  "availableTime": "2026-08-10 09:00"
}
```

---

## Flujo de trabajo del equipo

- Rama de larga duración: **`development`**.
- El trabajo se hace en **ramas temporales** por feature o fix; las ramas no se eliminan.
- La integración a `development` se hace **exclusivamente mediante pull requests**.
