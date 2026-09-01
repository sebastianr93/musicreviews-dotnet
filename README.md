# MusicReviews

Aplicacion web de reviews y ratings de musica (albumes y artistas), estilo RateYourMusic / Letterboxd.
Backend API-first en ASP.NET Core 10 sobre PostgreSQL, con catalogo alimentado por MusicBrainz + Cover Art Archive.

---

## Estado

| Fase | Alcance | Estado |
|------|---------|--------|
| 1 | Dominio, `DbContext`, configuraciones EF, migracion inicial, infraestructura Docker | **En curso** |
| 2 | Auth: Identity + JWT + refresh tokens, roles `User` / `Admin` | Pendiente |
| 3 | Catalogo: cliente MusicBrainz con cache y throttling | Pendiente |
| 4 | Reviews: CRUD y listados con proyecciones agregadas | Pendiente |
| 5 | Comments: arbol anidado sin N+1 + tests unitarios | Pendiente |
| 6 | Likes, Admin, rate limiting, tests de integracion | Pendiente |
| 7 | Frontend minimo usable | Pendiente |

---

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`dotnet --version` debe devolver `10.x`)
- Docker Desktop (para PostgreSQL)
- Herramienta `dotnet-ef`:

```bash
dotnet tool install --global dotnet-ef
# o, si ya la tenias de una version anterior:
dotnet tool update --global dotnet-ef
```

---

## Puesta en marcha

### 1. Levantar PostgreSQL

Desde la raiz del repositorio:

```bash
docker compose up -d
```

Deja corriendo dos contenedores:

| Servicio | Puerto | Credenciales |
|----------|--------|--------------|
| PostgreSQL 17 | `localhost:5432` | `musicreviews` / `musicreviews_dev`, base `musicreviews` |
| pgAdmin 4 | http://localhost:5050 | `dev@musicreviews.local` / `musicreviews_dev` |

La cadena de conexion ya esta configurada en `src/MusicReviews.Api/appsettings.Development.json`.
Son credenciales de desarrollo local: no van a produccion.

### 2. Restaurar y compilar

```bash
dotnet restore
dotnet build
```

### 3. Generar y aplicar la migracion inicial

La migracion vive en el proyecto de Infrastructure; el proyecto de arranque es la Api,
porque es el que registra el `DbContext` en el contenedor de dependencias.

```bash
dotnet ef migrations add InitialCreate ^
  --project src/MusicReviews.Infrastructure ^
  --startup-project src/MusicReviews.Api ^
  --output-dir Persistence/Migrations
```

> En PowerShell el caracter de continuacion de linea es `` ` `` (backtick); en CMD es `^`.
> Tambien podes escribir todo el comando en una sola linea.

```bash
dotnet ef database update ^
  --project src/MusicReviews.Infrastructure ^
  --startup-project src/MusicReviews.Api
```

En Development la Api tambien aplica las migraciones pendientes al arrancar
(`DatabaseInitializer.MigrateAsync`), asi que el `database update` explicito solo hace falta
la primera vez o cuando quieras aplicar sin levantar la Api.

### 4. Correr la Api

```bash
dotnet run --project src/MusicReviews.Api
```

| Recurso | URL |
|---------|-----|
| Documentacion interactiva (Scalar) | http://localhost:5080/scalar/v1 |
| Documento OpenAPI | http://localhost:5080/openapi/v1.json |
| Health check | http://localhost:5080/api/health |

`GET /api/health` devuelve `database: "up"` solo si la conexion a Postgres funciona:
es la forma rapida de confirmar que la fase 1 quedo cerrada de punta a punta.

---

## Estructura

```
MusicReviews/
├── docker-compose.yml            PostgreSQL + pgAdmin
├── Directory.Build.props         Propiedades comunes a todos los proyectos
├── Directory.Packages.props      Versiones centralizadas de paquetes (CPM)
└── src/
    ├── MusicReviews.Domain/          Entidades y enums. Sin EF, sin infraestructura.
    ├── MusicReviews.Application/     Logica de negocio, interfaces de servicios, validadores.
    ├── MusicReviews.Infrastructure/  DbContext, configuraciones EF, repositorios, clientes HTTP.
    └── MusicReviews.Api/             Controllers, DTOs, middlewares, composicion.
```

Direccion de las dependencias: `Api → Infrastructure → Application → Domain`.
El Domain no referencia a nadie hacia arriba.

---

## Modelo de datos

| Entidad | Clave | Notas |
|---------|-------|-------|
| `ApplicationUser` | `Guid` | Hereda de `IdentityUser<Guid>`; agrega `Bio`, `AvatarUrl`, `CreatedAt` |
| `Artist` | `int` | `MusicBrainzId` unico e indexado; `CachedAt` para invalidar la cache |
| `Album` | `int` | `MusicBrainzId` unico; FK a `Artist`; `ReleaseDatePrecision` para fechas parciales |
| `Review` | `int` | Unico `(UserId, AlbumId)`; CHECK `Score` entre 0 y 100 |
| `Comment` | `int` | `ParentCommentId` auto-referenciado; borrado logico |
| `Like` | `int` | Polimorfico `(TargetType, TargetId)`; unico `(UserId, TargetType, TargetId)` |
| `FavoriteArtist` | `(UserId, ArtistId)` | PK compuesta, sin Id sustituto |

### Indices

| Tabla | Indice | Para que |
|-------|--------|----------|
| `Artists` | `MusicBrainzId` (unico), `Name` | Deduplicar catalogo, buscar local antes de salir a MusicBrainz |
| `Albums` | `MusicBrainzId` (unico), `Title`, `ArtistId` | Idem + discografia por artista |
| `Reviews` | `(UserId, AlbumId)` unico, `(AlbumId, CreatedAt DESC)` | Una review por album; listado por album ordenado |
| `Comments` | `(ReviewId, CreatedAt)`, `ParentCommentId`, `UserId` | **Traer el hilo completo con un solo `WHERE ReviewId = X`** |
| `Likes` | `(UserId, TargetType, TargetId)` unico, `(TargetType, TargetId, IsLike)` | Impedir doble voto; contar votos sin joins |

---

## Decisiones tomadas y por que

**`ApplicationUser` vive en el Domain.**
Hereda de `IdentityUser<Guid>`, asi que el Domain referencia `Microsoft.Extensions.Identity.Stores`.
Es un paquete de abstracciones (POCOs), no de persistencia: no arrastra EF Core ni infraestructura.
A cambio, las entidades tienen navegaciones reales (`Review.User`, `Comment.User`) y las queries
usan `Include` en vez de joins manuales contra `AspNetUsers` en cada consulta.

**`DateTimeOffset` en vez de `DateTime` para todos los timestamps.**
Npgsql mapea `DateTimeOffset` a `timestamptz` sin ambiguedad. Con `DateTime` hay que vigilar el
`Kind` en cada asignacion, y `Kind.Unspecified` lanza excepcion al escribir en `timestamptz`.
`DateTimeOffset` elimina esa clase de bug entera.

**Borrado logico en `Comment`.**
Borrar fisicamente un nodo intermedio del hilo obliga a elegir entre romper el arbol o cascadear
respuestas de terceros. Con `IsDeleted` el nodo sobrevive como "[eliminado]" y el hilo queda intacto.
Por eso el FK auto-referenciado usa `Restrict`: si el borrado es logico, nunca hay huerfanas.

**`Comment → User` usa `Restrict`, no `Cascade`.**
Borrar una cuenta no debe arrastrar comentarios que sostienen respuestas de otros usuarios.
La baja de cuenta se resuelve anonimizando el usuario, no borrandolo en cascada.
La baja de cuenta esta fuera del alcance del MVP.

**`Like` es polimorfico en una sola tabla.**
`(TargetType, TargetId)` apunta a dos tablas distintas, asi que no hay FK real hacia el target:
la integridad la sostiene la aplicacion al borrar reviews y comentarios. La contrapartida vale la
pena frente a duplicar `ReviewLike` + `CommentLike`, porque deja una sola tabla y una sola query
de conteo para ambos casos.

**Central Package Management (`Directory.Packages.props`).**
Las versiones de paquetes se declaran una sola vez para toda la solucion. Los `.csproj` solo
listan que paquete usan, sin version: no hay forma de que dos proyectos queden en versiones
distintas del mismo paquete.

**Sin `EFCore.NamingConventions` (snake_case).**
Postgres normaliza identificadores sin comillas a minusculas, asi que muchos proyectos agregan
esa dependencia para tener tablas en `snake_case`. Se dejo afuera a proposito: EF genera los
identificadores entre comillas y funciona sin problemas, y es una dependencia menos que versionar.

---

## Comandos utiles

```bash
# Ver el SQL que generaria la migracion sin aplicarla
dotnet ef migrations script --project src/MusicReviews.Infrastructure --startup-project src/MusicReviews.Api

# Deshacer la ultima migracion (si todavia no se aplico)
dotnet ef migrations remove --project src/MusicReviews.Infrastructure --startup-project src/MusicReviews.Api

# Resetear la base por completo
docker compose down -v && docker compose up -d
```
