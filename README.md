# MusicReviews

Aplicacion web de reviews y ratings de musica (albumes y artistas), estilo RateYourMusic / Letterboxd.
Backend API-first en ASP.NET Core 10 sobre PostgreSQL, con catalogo alimentado por MusicBrainz + Cover Art Archive.

---

## Estado

| Fase | Alcance | Estado |
|------|---------|--------|
| 1 | Dominio, `DbContext`, configuraciones EF, migracion inicial, infraestructura Docker | Completa |
| 2 | Auth: Identity + JWT + refresh tokens, roles `User` / `Admin` | Completa |
| 3 | Catalogo: cliente MusicBrainz con cache y throttling | Completa |
| 4 | Reviews: CRUD y listados con proyecciones agregadas | Completa |
| 5 | Comments: arbol anidado sin N+1 + tests unitarios | Completa |
| 6 | Likes y modulo de usuarios (perfil, favoritos) | Completa |
| 7 | Admin: moderacion, roles, estadisticas + tests de integracion | Completa |
| 8 | Frontend minimo usable | **En curso** |

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

> **En Windows, para la API antes de compilar.** Si `dotnet run` esta activo, el build
> falla al copiar los DLL (`El archivo se ha bloqueado por: MusicReviews.Api`) y queda
> corriendo el binario anterior. Los endpoints nuevos responden 404 y parece un bug del
> codigo. `scripts/smoke-test.sh` detecta esa situacion y avisa antes de correr nada.

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

## Autenticacion

### Endpoints

| Metodo | Ruta | Auth | Que hace |
|--------|------|------|----------|
| POST | `/api/auth/register` | anonimo | Crea la cuenta, la asigna al rol `User` y devuelve el par de tokens |
| POST | `/api/auth/login` | anonimo | Acepta usuario **o** email; devuelve el par de tokens |
| POST | `/api/auth/refresh` | anonimo | Rota el refresh token y devuelve un par nuevo |
| POST | `/api/auth/logout` | anonimo | Revoca un refresh token. Idempotente |
| GET | `/api/auth/me` | Bearer | Devuelve la identidad del token. Sirve para probar el bearer |

### Como funciona

El access token es un JWT de 15 minutos firmado con HMAC-SHA256. El refresh token dura 7 dias,
son 256 bits de aleatoriedad criptografica, y **en la base solo se guarda su hash SHA-256**:
el valor en claro existe una sola vez, en la respuesta HTTP que lo emite. Si la base se filtra,
los refresh tokens robados no sirven.

Los tokens **rotan**: cada `refresh` revoca el token presentado y emite uno nuevo, encadenado
por `ReplacedByTokenHash`. Si llega un token que ya estaba revocado, es senial de que alguien
tiene una copia vieja, asi que se revoca la familia completa de tokens activos de ese usuario
y se fuerza un login nuevo.

Otras decisiones:

- **`AddIdentityCore`, no `AddIdentity`.** `AddIdentity` registra ademas el esquema de cookies
  y lo deja como esquema por defecto: es la causa clasica de que un endpoint protegido responda
  con un redirect a `/Account/Login` en vez de un `401` limpio.
- **`ClockSkew = TimeSpan.Zero`.** Por defecto son 5 minutos de tolerancia. Con un access token
  de 15 minutos, eso significaria aceptarlo durante 20.
- **`MapInboundClaims = false`.** Sin el mapeo legacy de WS-Federation, `sub` se llama `sub`
  y no se convierte en una URI larga.
- **Login sin enumeracion de cuentas.** "Usuario inexistente" y "contrasenia incorrecta"
  devuelven el mismo error. Los intentos fallidos alimentan el bloqueo de Identity
  (5 intentos, 5 minutos).

### Cuenta de administrador

No hay usuario por defecto: se siembra solo si le pasas credenciales por configuracion.
Nunca las pongas en `appsettings.json` — usa user-secrets:

```bash
dotnet user-secrets init --project src/MusicReviews.Api
dotnet user-secrets set "SeedAdmin:Email" "admin@musicreviews.local" --project src/MusicReviews.Api
dotnet user-secrets set "SeedAdmin:UserName" "admin" --project src/MusicReviews.Api
dotnet user-secrets set "SeedAdmin:Password" "<una contrasenia tuya>" --project src/MusicReviews.Api
```

La clave de firma JWT en `appsettings.Development.json` es de desarrollo. En produccion va por
variable de entorno (`Jwt__SigningKey`) o user-secrets; si mide menos de 32 bytes, la aplicacion
no arranca.

---

## Catalogo (MusicBrainz + Cover Art Archive)

### Endpoints

Todos publicos (navegar el catalogo no requiere cuenta) y bajo la politica de rate limiting
`catalog`: 30 requests por minuto por usuario, o por IP si la request es anonima.

| Metodo | Ruta | Que hace |
|--------|------|----------|
| GET | `/api/catalog/artists?query=&page=&pageSize=` | Busca artistas |
| GET | `/api/catalog/artists/{mbid}` | Detalle de artista. Lo cachea si no estaba |
| GET | `/api/catalog/artists/{mbid}/albums` | Discografia (albumes y EPs) |
| GET | `/api/catalog/albums?query=&page=&pageSize=` | Busca albumes |
| GET | `/api/catalog/albums/{mbid}` | Detalle de album + estadisticas de reviews |

### Estrategia de cache

Hay dos caches distintas porque resuelven problemas distintos:

- **Busquedas → `IMemoryCache`, 10 minutos.** Los resultados de busqueda son volatiles y
  no vale la pena persistirlos, pero sin esta cache cada tecla que el usuario escribe en
  el buscador consumiria un turno de la cola de 1 req/seg.
- **Detalles → Postgres, 30 dias.** Un artista o un album consultado se persiste como
  entidad local. A partir de ahi la Api responde de la base hasta que el registro queda
  viejo. Es lo que hace que las reviews puedan tener una FK real a `Album`.

Si MusicBrainz no responde o dejo de conocer un MBID que ya teniamos cacheado, se devuelve
el dato viejo en vez de un 404: es preferible un registro desactualizado a perder un album
sobre el que ya hay reviews escritas.

### Respetar el limite de 1 req/seg

El limite de MusicBrainz es **por aplicacion**, no por usuario: si dos requests entrantes
disparan dos llamadas simultaneas, se viola igual. Por eso hay una cola global
(`MusicBrainzThrottle`, singleton) por la que pasa toda llamada saliente, y un
`DelegatingHandler` que la aplica y reintenta con backoff exponencial ante el `503` con el
que MusicBrainz senializa el exceso (respetando `Retry-After` si viene).

El estado de la cola vive en el singleton y no en el handler a proposito:
`HttpClientFactory` recicla la cadena de handlers cada pocos minutos, y ahi el contador
se perderia.

Es una solucion de **un solo nodo**. Si la Api escalara horizontalmente, cada instancia
tendria su propia cola y el limite se violaria; a esa altura corresponde un throttle
distribuido (Redis) o, mejor, montar una replica local de los data dumps publicos de
MusicBrainz y dejar de depender de la API en vivo.

### Configuracion

En `appsettings.json`, seccion `MusicBrainz`. **Cambia el `UserAgent`**: MusicBrainz exige
que identifique la aplicacion y de una via de contacto, y responde `403` si no lo hace.

```json
"UserAgent": "MusicReviews/0.1.0 ( https://github.com/tu-usuario/MusicReviews )"
```

### Fechas parciales

MusicBrainz devuelve `"1997"`, `"1997-06"` o `"1997-06-16"` segun lo que se sepa del
lanzamiento. Guardar eso en un `DateOnly` pelado perderia la diferencia entre "salio en
1997" y "salio el 1 de enero de 1997", asi que `PartialDate` normaliza al primer dia del
periodo conocido y guarda la precision aparte en `Album.ReleaseDatePrecision`.

---

## Reviews

### Endpoints

| Metodo | Ruta | Auth | Que hace |
|--------|------|------|----------|
| GET | `/api/reviews/{id}` | publico | Detalle |
| GET | `/api/albums/{mbid}/reviews` | publico | Reviews de un album |
| GET | `/api/users/{userName}/reviews` | publico | Reviews de un usuario |
| POST | `/api/reviews` | Bearer | Crea la review del usuario autenticado |
| PUT | `/api/reviews/{id}` | Bearer | Edita. Solo el autor |
| DELETE | `/api/reviews/{id}` | Bearer | Borra. El autor, o un admin moderando |

Los listados aceptan `?sort=` con `Newest` (por defecto), `Oldest`, `HighestScore`,
`LowestScore` o `MostLiked`, mas `page` y `pageSize`.

### El album se identifica por MBID, no por Id local

`POST /api/reviews` recibe `albumMusicBrainzId`. El usuario viene del buscador del
catalogo y puede estar reseniando un album que nadie consulto todavia, asi que el servicio
lo trae de MusicBrainz y lo persiste antes de insertar la review. Es lo que hace que la FK
`Review.AlbumId` siempre tenga a que apuntar.

### Contadores sin N+1

La proyeccion de `Review` a `ReviewDto` resuelve **en la misma sentencia SQL** los likes,
los dislikes, la cantidad de comentarios y el voto del usuario actual, como subconsultas
correlacionadas:

```csharp
likes.Count(l => l.TargetType == LikeTargetType.Review && l.TargetId == review.Id && l.IsLike)
```

La alternativa ingenua seria `Include(r => r.Comments)` mas los likes y contar en memoria.
Eso trae miles de filas para producir cuatro numeros y, al incluir dos colecciones a la vez,
las multiplica entre si: el producto cartesiano que `AsSplitQuery()` existe para evitar.

`Like` no tiene navegacion hacia `Review` porque la relacion es polimorfica y no hay FK real,
asi que la subconsulta arranca del `DbSet<Like>` capturado, que EF traduce como raiz de
subconsulta. Los indices que la sostienen son `(TargetType, TargetId, IsLike)` en `Likes`
y `(ReviewId, CreatedAt)` en `Comments`.

Si en algun momento el volumen hace que las subconsultas correlacionadas pesen, el paso
siguiente no es cargar colecciones: es resolver los conteos en una segunda query agrupada
por `TargetId` y unir en memoria. Siguen siendo dos queries, no una por fila.

### Una review por usuario y album

La regla la sostiene el indice unico `(UserId, AlbumId)`, no un `SELECT` previo: entre
consultar y insertar cabe otra request del mismo usuario (doble click, dos pestanias) y solo
la base puede decidir sin condicion de carrera. El servicio captura la violacion del
constraint y la traduce a `409 Conflict` con el codigo `reviews.already_exists`.

### Borrado y likes huerfanos

Al borrar una review, los comentarios caen por cascada desde la FK. Los likes **no**:
la relacion polimorfica no tiene FK real, asi que el servicio borra explicitamente los likes
de la review y los de todos sus comentarios. Es el precio de tener una sola tabla de votos,
y esta pagado en un solo lugar.

### Orden estable

Todos los criterios de orden terminan con un desempate por `Id`. Sin un orden total, dos
filas con el mismo `CreatedAt` pueden repetirse entre paginas o no aparecer en ninguna.

---

## Comentarios anidados

### Endpoints

| Metodo | Ruta | Auth | Que hace |
|--------|------|------|----------|
| GET | `/api/reviews/{id}/comments` | publico | Hilo completo, ya anidado |
| GET | `/api/comments/{id}` | publico | Un comentario, sin sus respuestas |
| POST | `/api/reviews/{id}/comments` | Bearer | Publica. Con `parentCommentId` es una respuesta |
| PUT | `/api/comments/{id}` | Bearer | Edita. Solo el autor |
| DELETE | `/api/comments/{id}` | Bearer | Borrado logico. El autor, o un admin |

`GET .../comments` acepta `?sort=` con `Oldest` (por defecto), `Newest` o `MostLiked`.
El orden aplica **solo a los comentarios de primer nivel**: las respuestas van siempre
cronologicas, que es como se lee una conversacion.

### El problema: N+1

La forma intuitiva de traer un hilo anidado es recursiva: traer los comentarios de primer
nivel y, por cada uno, consultar sus respuestas; por cada respuesta, las suyas. Eso es
exactamente el patron N+1 — un hilo de 200 comentarios dispara 201 consultas, y la
cantidad depende de la **forma del arbol**, no del volumen de datos. Un hilo profundo es
peor que uno ancho aunque tenga los mismos comentarios.

### La solucion

Todo comentario guarda su `ReviewId`, incluso las respuestas anidadas a cualquier
profundidad. Esa denormalizacion es lo que permite traer el hilo entero con **una sola
consulta**:

```sql
SELECT ... FROM "Comments" WHERE "ReviewId" = @id ORDER BY "CreatedAt", "Id"
```

y reconstruir la jerarquia en memoria con un diccionario indexado por Id
(`CommentTreeBuilder`). Dos pasadas sobre la lista, O(n) en tiempo y memoria, cero
consultas adicionales, **sea cual sea la profundidad del hilo**.

La sostiene el indice `(ReviewId, CreatedAt)`: cubre el filtro y el orden de una pasada.

### Detalles del armado

**Sin recursion.** El builder es iterativo, y `Flatten` recorre con una pila explicita.
Una version recursiva funcionaria bien en un hilo normal, pero una cadena de miles de
niveles desbordaria la pila. Hay un test que arma una cadena de 1.000 niveles.

**Ciclos imposibles por construccion.** `Comment.Id` es una secuencia, asi que un
comentario siempre se crea despues de su padre y tiene Id mayor. La arista se crea solo
cuando `ParentCommentId < Id`. Un ciclo en el arbol seria una recursion infinita al
serializar a JSON; esta condicion lo hace imposible aunque la base llegara corrupta.

**Nada se pierde.** Un comentario huerfano (padre ausente del conjunto) o con un padre
invalido se promueve a raiz en vez de desaparecer. La invariante que verifican los tests:
*cada comentario de la entrada aparece exactamente una vez en el arbol*.

**El orden lo pone la consulta, no el builder.** El builder preserva el orden de entrada
al colgar las respuestas. Si reordenara, el orden cronologico del hilo se perderia.

**Profundidad guardada.** `Comment.Depth` se calcula al insertar (`padre.Depth + 1`) y no
se recalcula. Permite aplicar el limite de anidamiento en O(1), sin subir por la cadena de
padres, y deja preparada la paginacion por niveles si algun dia hace falta cargar las
respuestas profundas bajo demanda.

### Borrado logico

Borrar fisicamente un nodo intermedio obliga a elegir entre romper el hilo o cascadear
respuestas de otros usuarios. Con `IsDeleted` el nodo sobrevive: sigue en el arbol
sosteniendo sus respuestas, pero la proyeccion devuelve `text` y `author` en `null`.
El texto queda en la base para auditoria de moderacion. Los likes del comentario si se
borran: no hay FK que los arrastre y votar algo que ya no se muestra no tiene sentido.

### Tests

`tests/MusicReviews.UnitTests/Comments/CommentTreeBuilderTests.cs` — 23 casos.

```bash
dotnet test
```

Ademas del camino feliz, cubre la clase entera de datos que la base no deberia producir:
huerfanos, auto-referencias, ciclos mutuos, ids duplicados, entrada desordenada (una
respuesta antes que su padre), padres borrados con respuestas vivas, y una cadena de 1.000
niveles. El caso `Build_CadaComentarioDeLaEntradaApareceExactamenteUnaVez` corre sobre
hilos generados de 1 a 5.000 comentarios y verifica la invariante en todos.

---

## Likes y usuarios

### Votos

| Metodo | Ruta | Auth | Que hace |
|--------|------|------|----------|
| POST | `/api/reviews/{id}/likes` | Bearer | Vota una review (`{"isLike": true \| false}`) |
| DELETE | `/api/reviews/{id}/likes` | Bearer | Retira el voto. Idempotente |
| POST | `/api/comments/{id}/likes` | Bearer | Vota un comentario |
| DELETE | `/api/comments/{id}/likes` | Bearer | Retira el voto. Idempotente |

Las rutas quedan anidadas bajo cada recurso en vez de exponer el par
`(targetType, targetId)`: el cliente no tiene por que enterarse de que por debajo hay
una sola tabla polimorfica.

**Semantica de toggle.** El mismo voto lo retira, el contrario lo cambia (no crea una
segunda fila), y si no habia voto lo crea. La respuesta devuelve `likeCount`,
`dislikeCount` y `currentUserVote` ya actualizados, para que el cliente no tenga que
volver a pedir el recurso.

**Validacion del objetivo.** Como la relacion es polimorfica no hay FK que rechace un
voto sobre un id inexistente, asi que el servicio verifica que la review o el comentario
existan antes de insertar. Sin eso, la tabla acumularia votos apuntando a la nada.
Tampoco se puede votar un comentario borrado.

### Perfiles y favoritos

| Metodo | Ruta | Auth | Que hace |
|--------|------|------|----------|
| GET | `/api/users/me` | Bearer | Perfil propio |
| PUT | `/api/users/me` | Bearer | Edita bio y avatar |
| GET | `/api/users/{userName}` | publico | Perfil publico con estadisticas y favoritos |
| GET | `/api/users/{userName}/favorites` | publico | Artistas favoritos |
| POST | `/api/users/me/favorites` | Bearer | Agrega un favorito por MBID. Idempotente |
| DELETE | `/api/users/me/favorites/{mbid}` | Bearer | Quita un favorito. Idempotente |

El perfil se arma con dos consultas: una trae los datos del usuario con `reviewCount`,
`commentCount` y `averageScore` como agregaciones en la misma sentencia, y otra los
favoritos, que son una lista y no un escalar.

Agregar un favorito acepta un MBID que puede no estar cacheado: el servicio lo trae de
MusicBrainz y lo persiste antes de guardar la relacion, igual que al crear una review.
Quitarlo, en cambio, se resuelve solo contra la cache local — sacar un favorito nunca
deberia disparar una llamada a la API externa.

**El `avatarUrl` se valida como URL absoluta http/https.** No es formalismo: ese campo
se renderiza en el perfil publico que ve cualquiera, asi que un `javascript:` guardado
ahi es un XSS almacenado servido a todos los visitantes.

---

## Administracion

| Metodo | Ruta | Que hace |
|--------|------|----------|
| GET | `/api/admin/stats` | Usuarios, actividad, contenido y albumes mas resenados |
| GET | `/api/admin/users?search=&page=` | Usuarios con sus roles y actividad |
| POST | `/api/admin/users/{userId}/roles` | Asigna un rol. Idempotente |
| DELETE | `/api/admin/users/{userId}/roles/{role}` | Quita un rol |

Todo el controlador exige el rol `Admin` (politica `RequireAdmin`). Un usuario
autenticado sin ese rol recibe `403`, no `401`: el token es valido, lo que falta es el
permiso.

**La moderacion no tiene endpoints propios.** `DELETE /api/reviews/{id}` y
`DELETE /api/comments/{id}` ya aceptan a un administrador como autor alternativo, asi
que duplicarlos bajo `/api/admin` solo agregaria dos caminos para la misma operacion.
Un admin borra contenido de cualquiera, pero **no lo edita**: reescribir lo que otro
opino no es moderar.

**Dos protecciones al quitar roles.** Un administrador no puede quitarse a si mismo el
rol `Admin` — un click lo dejaria sin acceso al panel — ni se puede quitar el ultimo
administrador del sistema, porque despues no quedaria nadie que pueda volver a
asignarlo y habria que tocar la base a mano.

---

## Tests

```bash
dotnet test
```

### Unitarios (`tests/MusicReviews.UnitTests`)

23 casos sobre `CommentTreeBuilder`, la logica mas propensa a bugs sutiles del backend.
Ver la seccion de comentarios anidados.

### Integracion (`tests/MusicReviews.IntegrationTests`)

Levantan la Api completa con `WebApplicationFactory<Program>` contra un **PostgreSQL
real en Docker** (Testcontainers). Requieren Docker corriendo; el contenedor se crea y
se destruye solo.

**Por que Postgres real y no un proveedor en memoria.** Buena parte de lo que hay que
verificar vive en la base y no existe en InMemory: los indices unicos que sostienen
"una review por album" y "un voto por objetivo", el CHECK del puntaje, el borrado en
cascada, y la traduccion real de las proyecciones a SQL. Un test contra InMemory
pasaria con un modelo que en produccion falla.

**MusicBrainz se reemplaza por un fake.** `FakeMusicCatalogService` persiste artistas y
albumes deterministas derivados del MBID pedido. Depender de la API real metería una
red externa en el camino critico de la suite, con un limite de 1 request por segundo
que la volveria lentisima y fallos rojos cada vez que el servicio tenga un mal dia.
Lo que se prueba aca es la Api propia, no la integracion con un tercero.

Todas las clases comparten una sola coleccion de xUnit: se paraleliza entre colecciones,
y dos suites escribiendo en la misma base a la vez darian fallos intermitentes
irreproducibles. El aislamiento entre tests lo da que cada uno crea sus propios datos
con nombres unicos.

Cubren auth (rotacion y deteccion de reuso de refresh tokens, no enumeracion de
cuentas), reviews (el constraint unico, autoria, moderacion por admin, contadores),
comentarios (anidamiento de tres niveles, borrado logico que no rompe el hilo, limite
de profundidad, respuesta a un comentario de otra review) y administracion (roles,
403 vs 401, las dos protecciones al quitar roles).

---

## Frontend

Abrilo en http://localhost:5080 — la Api lo sirve desde `wwwroot`, en el mismo origen.

| Pantalla | Ruta |
|----------|------|
| Busqueda de albumes y artistas | `#/?q=...&mode=albums\|artists` |
| Artista con discografia y favoritos | `#/artist/{mbid}` |
| Album con reviews y formulario | `#/album/{mbid}` |
| Review con el hilo de comentarios | `#/review/{id}` |
| Perfil publico | `#/user/{userName}` |
| Editar perfil propio | `#/me` |
| Login / registro | `#/login`, `#/register` |

### Por que HTML y JS sin framework

Esta fase pedia una interfaz **minima pero usable**, sin condicionar la decision real
de stack que viene despues. Un puñado de modulos ES servidos desde `wwwroot` cumple eso
sin agregar nada al proyecto: cero build, cero dependencias, cero segundo proceso, cero
CORS. La fase 2 del frontend queda completamente libre para Angular, y este codigo se
descarta sin arrastrar deuda.

Se usa **hash routing** (`#/album/...`) a proposito: no necesita fallback del servidor
para las rutas del cliente, asi que `UseStaticFiles` alcanza y no hay que interceptar
404 para devolver `index.html`.

### Todo el DOM se arma con `createElement`

`dom.js` construye nodos con `createElement` y `textContent`, nunca con `innerHTML`
interpolado. No es preferencia de estilo: el sitio renderiza texto escrito por otros
usuarios —reviews, comentarios, bios— y concatenar eso en `innerHTML` es XSS almacenado
servido a todos los visitantes. Es la contraparte en el cliente de la validacion del
`avatarUrl` en el servidor: las dos puntas del mismo agujero.

### Renovacion transparente del token

El access token dura 15 minutos. `api.js` intercepta el `401`, rota el refresh token y
reintenta el request original una sola vez. La renovacion se comparte entre llamadas
concurrentes (`refreshInFlight`): si tres requests fallan a la vez, las tres esperan el
mismo refresh. Sin eso se dispararian tres rotaciones simultaneas y el backend leeria
dos de ellas como reuso de un token ya revocado — revocando la familia entera y sacando
al usuario de la sesion.

### El hilo se dibuja de una sola respuesta

`renderComment` es recursiva, pero recorre un arbol que ya vino armado en el JSON de
`GET /api/reviews/{id}/comments`. No hay ninguna peticion por nodo: es el mismo motivo
por el que la consulta del backend es una sola.

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
| `RefreshToken` | `int` | Guarda el hash SHA-256, nunca el token; rotacion con deteccion de reuso |

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
