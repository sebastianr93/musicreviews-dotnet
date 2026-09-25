# MusicReviews

[![CI](https://github.com/sebastianr93/musicreviews-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/sebastianr93/musicreviews-dotnet/actions/workflows/ci.yml)

Aplicacion web de reviews y ratings de musica (albumes y artistas), estilo RateYourMusic / Letterboxd.
Backend API-first en ASP.NET Core 10 sobre PostgreSQL, con catalogo alimentado por MusicBrainz + Cover Art Archive.

**Demo:** _pendiente de publicar_ · **Documentacion de la Api:** `/scalar/v1` sobre la instancia desplegada

La [especificacion tecnica](docs/especificacion-tecnica.md) describe la arquitectura,
el modelo de datos y las decisiones de diseño. Este documento es la guia de uso y el
detalle de implementacion de cada modulo.

---

## Funcionalidad

**Catalogo.** Busqueda unificada de artistas, albumes y canciones contra MusicBrainz, con
cache local en PostgreSQL, portadas de Cover Art Archive y fotos de artista resueltas via
Wikidata. Las grabaciones duplicadas que MusicBrainz devuelve por separado se agrupan en
un resultado por tema.

**Reviews y comentarios.** Puntaje de 0 a 100 y texto, una review por usuario y album,
con hilo de comentarios anidados resuelto en una sola consulta y borrado logico para no
romper el arbol.

**Social.** Seguir usuarios, feed de la actividad de a quienes se sigue, votos positivos
y negativos sobre reviews y comentarios, perfiles con artistas favoritos y avatar propio.

**Avisos.** Notificaciones persistidas con contador de no leidos sobre indice parcial y
paginacion por cursor.

**Home.** Populares por actividad de los ultimos treinta dias, feed de seguidos, catalogo
para explorar y titulares de prensa musical por RSS (titulo, extracto, imagen y enlace a
la fuente).

**Administracion.** Moderacion de reviews y comentarios, gestion de roles y estadisticas.

### Proximos pasos

- Perfil privado con aprobacion de seguidores.
- Identidad visual propia y paleta derivada de la portada.

---

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download) (`dotnet --version` debe devolver `10.x`)
- Docker Desktop (para PostgreSQL)
- Herramienta `dotnet-ef`:

```bash
dotnet tool install --global dotnet-ef
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

### 3. Aplicar las migraciones

Las migraciones estan versionadas en el repositorio. Viven en el proyecto de
Infrastructure y el proyecto de arranque es la Api, porque es el que registra el
`DbContext` en el contenedor de dependencias:

```bash
dotnet ef database update --project src/MusicReviews.Infrastructure --startup-project src/MusicReviews.Api
```

La Api tambien aplica las migraciones pendientes al arrancar, asi que este paso explicito
solo hace falta para preparar la base sin levantar el servicio. Se puede desactivar con
`Database:MigrateOnStartup=false`.

### 4. Correr la Api

```bash
dotnet run --project src/MusicReviews.Api
```

| Recurso | URL |
|---------|-----|
| Documentacion interactiva (Scalar) | http://localhost:5080/scalar/v1 |
| Documento OpenAPI | http://localhost:5080/openapi/v1.json |
| Health check | http://localhost:5080/api/health |

`GET /api/health` devuelve `database: "up"` solo si la conexion a Postgres funciona.
Es lo que consulta la plataforma de hosting para decidir si el despliegue quedo sano.

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
por `ReplacedByTokenHash`. Si llega un token que ya estaba revocado, es señal de que alguien
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
- **Login sin enumeracion de cuentas.** "Usuario inexistente" y "contraseña incorrecta"
  devuelven el mismo error. Los intentos fallidos alimentan el bloqueo de Identity
  (5 intentos, 5 minutos).

### Cuenta de administrador

No hay usuario por defecto: se siembra solo si le pasas credenciales por configuracion.
Nunca las pongas en `appsettings.json` — usa user-secrets:

```bash
dotnet user-secrets init --project src/MusicReviews.Api
dotnet user-secrets set "SeedAdmin:Email" "admin@musicreviews.local" --project src/MusicReviews.Api
dotnet user-secrets set "SeedAdmin:UserName" "admin" --project src/MusicReviews.Api
dotnet user-secrets set "SeedAdmin:Password" "<una contraseña tuya>" --project src/MusicReviews.Api
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
| GET | `/api/catalog/songs?query=&page=&pageSize=` | Busca canciones, agrupadas por tema |
| GET | `/api/search/quick?q=&limit=` | Sugerencias del catálogo local (desplegable) |
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

### Ordenar los resultados de busqueda

Buscar "the dark side of the moon" en MusicBrainz devuelve decenas de release-groups con
ese titulo **exacto**. Todos matchean igual de bien, asi que el indice les asigna el mismo
`score`, y entre empates el orden es arbitrario: el de Pink Floyd puede quedar en la
posicion 45 y el de una banda desconocida en la primera. No es un bug de la API —
MusicBrainz es un catalogo, no un ranking: no tiene ninguna nocion de popularidad y no
la expone.

Se resuelve en dos pasos:

**Traer mas de lo que se muestra.** Cada busqueda pide 100 resultados (el maximo de la
API) y se reordena antes de paginar. Reordenar solo los 20 de la primera pagina no
traeria nunca al que esta en la posicion 45. Ademas paginar pasa a ser gratis: las
paginas siguientes salen de la misma respuesta cacheada, sin consumir otro turno de la
cola de 1 req/seg.

**Usar la unica señal de popularidad que existe.** La cantidad de ediciones
(`releaseCount`) es un proxy sorprendentemente bueno: un disco reeditado en vinilo, CD,
remasterizado y por pais acumula cientos de releases; una autoedicion tiene una. No mide
gusto, mide cuanta industria hubo alrededor — que para "cual de estos discos homonimos
buscaba el usuario" es exactamente lo que hace falta.

El orden completo (`AlbumSearchRanking`, en Application y con 15 tests):

1. Los que quedan dentro de **10 puntos del mejor score** de la tanda.
2. Entre ellos, mas ediciones primero. Fuera de ese grupo, manda el score.
3. Album antes que EP antes que Single.
4. El lanzamiento original antes que los homonimos posteriores; sin fecha, al final.
5. Desempate por MBID, para que el orden no salte entre recargas.

La tolerancia se mide **contra el maximo de la tanda**, no en tramos fijos. Agrupar en
tramos de 10 fue el primer intento y un test lo tumbo: 100 y 97 caen en tramos distintos
por estar a los lados de un limite arbitrario, y esa diferencia es ruido del indice, no
una señal que deba pesar mas que 400 ediciones contra una.

`releaseCount` no esta garantizado por la API, asi que hay un respaldo (la lista de
releases que viene en la misma respuesta) y, si tampoco viene, el orden degrada con
gracia a tipo y fecha.

### Buscar canciones: agrupar lo que MusicBrainz no agrupa

`GET /api/catalog/songs?query=...`

**MusicBrainz no modela canciones, modela grabaciones.** Hay una fila por cada version
registrada de un tema: el master original, cada remasterizacion, cada version en vivo,
cada edicion por pais, cada aparicion en un recopilatorio. Buscar "Smells Like Teen
Spirit" devuelve la misma cancion cien veces. Mostrar eso tal cual hace que la busqueda
de canciones no sirva para nada.

**La clave de agrupacion es (titulo normalizado, artista normalizado).** No puede ser el
MBID, que es justamente lo que difiere entre versiones; ni la duracion, que varia entre
ediciones y a veces ni viene.

**La normalizacion tiene que ser agresiva**, porque la diferencia entre versiones vive
en el titulo: `Song`, `Song (Remastered 2011)`, `Song (Live at Reading)`,
`Song - 2004 Remaster`. Entonces:

- se pasa a minusculas, se quitan acentos y puntuacion;
- se elimina **todo lo que este entre parentesis o corchetes**, incluidos los anidados;
- se corta la cola tras un guion **solo si contiene una palabra de version**
  (`remaster`, `live`, `mix`, `demo`...).

Esa asimetria es deliberada. Entre parentesis casi siempre hay una anotacion de version;
un guion, en cambio, suele separar partes del titulo real —`Hell Is for Children - Part
2`—, asi que cortarlo siempre uniria canciones distintas.

**El costo asumido:** un titulo cuya unica diferencia real esta entre parentesis
—`(I Can't Get No) Satisfaction` frente a `Satisfaction`— se agrupa igual. Se prefiere
ese falso positivo ocasional a mostrar cuarenta filas identicas: el usuario llega igual
al album correcto, que es a donde lleva el resultado.

Al fundir el grupo, **los datos que le falten al representante se completan desde sus
hermanos**: la version que mejor matchea no siempre es la que trae el album, la portada
o la duracion, y todas describen la misma cancion. La fecha del grupo es la **mas vieja**
—cuando salio el tema, no cuando salio la reedicion que quedo de representante—.

**El plegado de acentos es una tabla escrita a mano, no `Normalize(FormD)`.** Esa seria
la forma canonica, pero el proyecto corre con `InvariantGlobalization=true` y en ese modo
la normalizacion Unicode **no hace nada**: devuelve la cadena tal cual, sin lanzar. La
tilde sobrevive y el fallo es silencioso. La tabla cubre latin basico y extendido; lo que
no esta en ella pero es letra se conserva, para que un titulo en cirilico o japones siga
siendo distinguible en vez de colapsar a una clave vacia. Hay un test que lo fija.

**`VersionCount` es el `ReleaseCount` de las canciones.** Cumple el mismo papel que en
los albumes: es la unica señal de popularidad disponible, porque solo un tema muy
difundido acumula decenas de versiones. El orden usa la misma tolerancia de score que
`AlbumSearchRanking`, y dentro de ella decide la cantidad de versiones.

**Una cancion lleva al album, no a si misma.** Aca se reseña el album, asi que el
resultado enlaza al release-group. Elegirlo tiene su truco: una grabacion popular aparece
en el disco original, en tres recopilatorios y en dos bandas sonoras, y tomar la primera
edicion de la lista manda al usuario a un *Greatest Hits*. Se prefiere un release-group
de tipo `Album` **sin tipos secundarios** —`Compilation`, `Live` y `Soundtrack` lo son— y,
entre esos, el mas viejo. Los resultados que no llegan a ningun album van al final: son
filas con las que el usuario no puede hacer nada.

El agrupado corre **antes de paginar**. Agrupar sobre la pagina ya recortada dejaria
pasar duplicados de la misma cancion en paginas distintas.

### Un solo campo, tres busquedas en paralelo

El buscador del frontend es uno solo —sin selector de tipo—, pero por debajo son tres
endpoints (`/artists`, `/albums`, `/songs`) que el cliente pide **en paralelo** y pinta
seccion por seccion a medida que llegan.

No es una decision estetica. Contra MusicBrainz hay una cola de **1 request por
segundo**: devolver las tres cosas en una sola respuesta obligaria a esperar a la mas
lenta, que llega tres turnos despues que la primera. Asi el usuario ve artistas al primer
segundo y sigue leyendo mientras llega el resto.

Cada seccion **falla por su cuenta**: que MusicBrainz se caiga a mitad de la busqueda de
canciones no puede borrar los albumes que ya estan en pantalla. Y una seccion que llega
vacia no se muestra; el mensaje de "sin resultados" aparece una sola vez, cuando ninguna
de las tres trajo nada.

### Cuando MusicBrainz no responde

Un fallo del catalogo externo viaja como `ExternalServiceUnavailableException` y sale
como **`503` con `Retry-After`**, nunca como `500`. La diferencia no es cosmetica: un
`500` le dice al cliente "esto esta roto", un `503` le dice "volve a intentar". El log
lo registra como `Warning`, no `Error`, para que un `Error` en el log siga significando
"hay algo que arreglar" y los bugs reales no queden tapados por caidas de terceros.

El servicio traduce esa excepcion a un `Result` con `ErrorType.Unavailable` antes de
devolverla: los llamadores internos —crear una review, marcar un favorito— trabajan con
`Result`, y que un fallo del catalogo llegara como excepcion los obligaria a mezclar dos
estilos de manejo de error para el mismo caso. El `GlobalExceptionHandler` la maneja
igual, como red de seguridad.

**El timeout se aplica por intento, no al request completo.** Es la causa de un bug que
costo encontrar: la espera en la cola de 1 req/seg ocurre *dentro* del pipeline del
`HttpClient`, asi que con `HttpClient.Timeout` esa espera consume el mismo presupuesto
que la llamada real. Con la cola ocupada y los reintentos con backoff (1.1s, 2.2s,
4.4s), un request agotaba 20 segundos sin haber hecho una sola peticion HTTP, y el error
—"the request was canceled due to the configured HttpClient.Timeout"— apuntaba al lugar
equivocado. Ahora el cliente se configura con `Timeout.InfiniteTimeSpan` y cada intento
recibe su propio `CancellationTokenSource`; distinguir si cancelo ese token o el de la
request separa "el servicio externo tardo" de "el usuario cerro la pestaña".

**El `Detail` en Development es el mensaje, no el `ToString()`.** El stack trace completo
en el cuerpo de la respuesta llena la pantalla del navegador y no aporta nada que no este
ya en el log de Serilog, con mejor formato.

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
consultar y insertar cabe otra request del mismo usuario (doble click, dos pestañas) y solo
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

### Seguir usuarios

| Metodo | Ruta | Auth | Que hace |
|--------|------|------|----------|
| POST | `/api/users/{userName}/follow` | Bearer | Empieza a seguir. Idempotente |
| DELETE | `/api/users/{userName}/follow` | Bearer | Deja de seguir. Idempotente |
| GET | `/api/users/{userName}/followers` | publico | Quienes lo siguen |
| GET | `/api/users/{userName}/following` | publico | A quienes sigue |

La relacion es **dirigida**: que A siga a B no implica lo contrario.

Las dos operaciones devuelven el **perfil del seguido ya actualizado**, para que el
cliente repinte el boton y el contador sin pedirlo de nuevo.

**La clave primaria compuesta `(FollowerId, FollowedId)` es lo que impide seguir dos
veces a la misma persona.** No hay Id sustituto ni chequeo previo en la aplicacion: entre
un `SELECT` y un `INSERT` cabe un doble click, y solo la base puede decidir sin condicion
de carrera. El servicio captura la violacion y responde con el estado que quedo.

**Un CHECK en la base rechaza seguirse a uno mismo.** Podria validarse solo en el
servicio, pero una fila asi ensuciaria todos los contadores y el feed sin que ninguna
consulta la delate.

**Dos indices, no uno.** `(FollowerId, CreatedAt DESC)` para "a quienes sigo" y
`(FollowedId, CreatedAt DESC)` para "quienes me siguen". La PK solo sirve para la primera
consulta: su columna lider es `FollowerId`.

**Dos FK a la misma tabla, las dos en cascada**, asi que borrar una cuenta limpia sus
relaciones en ambos sentidos. PostgreSQL admite multiples caminos de cascada; en SQL
Server esto no compilaria.

En los listados, `isFollowedByCurrentUser` se resuelve dentro de la misma consulta que
trae las filas, no con una query por fila.

---

## Actividad y feed

| Metodo | Ruta | Auth | Que hace |
|--------|------|------|----------|
| GET | `/api/users/{userName}/activity` | publico | Timeline de un usuario |
| GET | `/api/feed` | Bearer | Actividad combinada de a quienes seguis |

Los dos aceptan `?cursor=` y `?limit=`.

### No hay tabla de actividad

El timeline se **deriva** de las tablas que ya existen —reseñas, comentarios y votos—
en vez de mantener una tabla de feed escrita en cada accion.

Una tabla de feed se lee mas rapido, pero hay que sincronizarla en cada alta, baja y
edicion, y cualquier camino que se olvide de hacerlo deja el feed mostrando contenido que
ya no existe. Derivando, borrar una reseña borra su actividad sin que nadie tenga que
acordarse; el borrado logico de un comentario lo saca del timeline solo. Hay un test que
verifica exactamente eso.

Son **cuatro consultas acotadas** —una por tipo de actividad, cada una pidiendo solo lo
que puede entrar en la pagina— y la mezcla se hace en memoria. La cantidad de consultas
es fija: no crece con los resultados ni con cuantos usuarios seguis. Cada una usa su
propio indice `(UserId, CreatedAt DESC)`, que hubo que agregar: los indices que ya
existian ordenan por album o por objetivo del voto, no por fecha.

Los votos necesitan dos de esas cuatro consultas porque `Like` es polimorfico: sin FK
real, el join contra reseñas y contra comentarios no se puede hacer en una sola.

Van **secuenciales, no con `Task.WhenAll`**: EF Core no admite dos operaciones
simultaneas sobre el mismo `DbContext`. Paralelizarlas exigiria un contexto por consulta,
que para cuatro consultas indexadas no compensa.

### Paginacion por cursor, no por offset

Un feed no se puede paginar por offset. Entre que el usuario pide la pagina 1 y la 2,
alguien publica algo: esa fila entra arriba, corre todo un lugar, y la primera fila de la
pagina 2 es la que ya vio al final de la 1. Con contenido que se agrega constantemente
eso no es un caso raro, es lo normal.

**El cursor lleva el instante y el Id**, no solo la fecha. El timeline mezcla tablas
distintas y nada impide que dos entradas compartan el instante exacto —basta votar y
comentar en la misma operacion—. Con un cursor de solo fecha, un filtro `<` se saltea las
entradas empatadas y un `<=` las repite. Con el Id el orden queda **total** y no hay
ambiguedad.

La consulta filtra por `CreatedAt <= cursor` (incluyente, para no perder los empates) y
el descarte fino lo hace `IsAfter` en memoria, donde el Id ya esta disponible.

Se codifica en **ticks UTC**, no en milisegundos: PostgreSQL guarda `timestamptz` con
precision de microsegundos, y truncar haria que el instante decodificado nunca coincida
con el de la fila — lo que rompe la deteccion de empates en silencio. Va en Base64Url
para que el cliente lo trate como opaco, y un cursor invalido degrada a "empezar de cero"
en vez de devolver un 500.

**El cupo por consulta es `limit + 2`, no `limit + 1`.** Como el filtro por fecha es
incluyente, la entrada del propio cursor siempre vuelve a venir y se descarta en memoria;
con `limit + 1` el resultado quedaba justo en `limit` y el servicio concluia "no hay mas
paginas" con entradas todavia sin entregar. Si ademas hay empates de instante, un bucle
acotado vuelve a consultar duplicando el cupo. Este bug lo encontro un test de
integracion y **los tests unitarios del cursor no podian encontrarlo**: simulan la
paginacion sobre una lista en memoria, donde no existe el `LIMIT` de la consulta.

`ActivityCursorTests` tiene 20 casos. El que importa es
`PaginarElFeedCompleto_VisitaCadaEntradaExactamenteUnaVez`: recorre feeds de hasta 200
entradas —con un tercio compartiendo instante— y verifica que ninguna se repita ni se
pierda. Hay una variante con las 25 entradas en el mismo instante exacto.

---

## Notificaciones

| Metodo | Ruta | Auth | Que hace |
|--------|------|------|----------|
| GET | `/api/notifications` | Bearer | Listado por cursor (`?cursor=`, `?limit=`, `?unreadOnly=`) |
| GET | `/api/notifications/unread-count` | Bearer | Cuantos faltan leer |
| POST | `/api/notifications/{id}/read` | Bearer | Marca uno como leido (idempotente) |
| POST | `/api/notifications/read-all` | Bearer | Marca todos; devuelve cuantos cambiaron |

Tipos: `ReviewCommented`, `CommentReplied`, `ReviewVoted`, `CommentVoted`, `NewFollower`.

### Estas si se persisten (a diferencia de la actividad)

La seccion anterior explica por que el timeline se deriva en vez de guardarse. Con las
notificaciones la decision es la contraria, y por una razon concreta: **tienen estado
propio que no vive en ninguna otra tabla** —si fueron leidas y cuando—. Derivarlas
obligaria igual a guardar una marca de lectura por evento, que es exactamente esta tabla
con mas pasos y con la parte dificil (que evento corresponde a que marca) resuelta a mano.

### Quien se entera de que

Un comentario genera **un solo aviso**. Si es una respuesta va al autor del comentario
padre; si es de primer nivel, al autor de la reseña. **No van los dos**: en un hilo
largo, el autor de la reseña recibiria una notificacion por cada respuesta anidada entre
terceros, que es exactamente el ruido que hace que la gente deje de mirar la campana.

Nadie recibe avisos de sus propias acciones: comentar la propia reseña o votar el propio
comentario no genera nada. La comprobacion esta en `NotifyAsync`, en un solo lugar, y no
repartida por cada servicio que llama.

### Poner y sacar un voto no acumula avisos

Un voto es un estado, no un hecho: mientras esta puesto, "a fulano le gusto tu review" es
cierto; cuando se retira, deja de serlo. Por eso:

- Retirar el voto **retira el aviso si todavia no se leyo**. Si ya se leyo se conserva:
  borrar lo que el usuario ya vio seria reescribirle el historial.
- Cambiar el sentido del voto **actualiza el aviso existente** en vez de sumar otro.
- Un aviso equivalente sin leer se **refresca** —vuelve a subir en el listado— en lugar
  de duplicarse.

Seguir a alguien se trata distinto: dejar de seguir **no** retira el aviso. "Empezo a
seguirte" es un hecho que ocurrio, no un estado visible al lado de un contenido. Volver a
seguir tampoco acumula, porque cae en la regla de refrescar el equivalente sin leer.

### Quien guarda

`INotificationWriter` **no guarda**. Solo encola el cambio en el contexto; el
`SaveChangesAsync` lo hace el servicio que provoco el evento, que es el que sabe cuando su
propia operacion quedo consistente.

- En **votos y seguimientos** el aviso viaja en la misma transaccion que la accion: o
  quedan los dos, o no queda ninguno.
- En **comentarios** hace falta un guardado previo para obtener el `Id` que genera la
  base, asi que el aviso va en una segunda escritura. Si esa fallara, el comentario queda
  publicado y solo se pierde el aviso: es la degradacion correcta, y por eso ese error se
  registra sin propagarse.

La interfaz de escritura esta **separada de la de lectura** a proposito: los servicios de
comentarios y votos solo necesitan escribir, y depender de `INotificationService` les
daria acceso a operaciones que no les corresponden, como marcar avisos ajenos como leidos.
Detras hay una sola clase, registrada por su tipo concreto y reenviada a las dos
interfaces para que sea la misma instancia dentro de la request.

### El contador tiene su propio indice parcial

`GET /api/notifications/unread-count` es la consulta mas frecuente de toda la aplicacion:
se pide en cada carga de pagina para pintar el globito. Lo normal es tener **pocas sin
leer sobre un historial largo**, asi que el indice es parcial:

```csharp
builder.HasIndex(n => n.RecipientId)
    .HasFilter("NOT \"IsRead\"")
    .HasDatabaseName("IX_Notifications_RecipientId_Unread");
```

Un indice completo sobre `RecipientId` crece con todo el historial de todos los usuarios;
este solo indexa las filas que la consulta mira, y se vacia solo a medida que la gente
lee.

### El cursor es mas simple que el de actividad

`KeysetCursor` apunta a **una sola tabla con Id numerico**, asi que la condicion de
keyset se traduce entera a SQL:

```sql
CreatedAt < @t OR (CreatedAt = @t AND Id < @id)
```

No hace falta traer de mas ni descartar en memoria como en el timeline, que mezcla tablas
y desempata con un Id compuesto de texto. El cupo es `limit + 1` —el clasico "una fila de
mas para saber si hay pagina siguiente"— y no `limit + 2`. La fecha se codifica igual en
ticks UTC, por la misma razon de precision.

### Marcar como leido es imposible de hacer sobre un aviso ajeno

El filtro por destinatario va **dentro del `ExecuteUpdateAsync`**, no en una consulta
previa:

```csharp
await _context.Notifications
    .Where(n => n.Id == notificationId && n.RecipientId == userId && !n.IsRead)
    .ExecuteUpdateAsync(...);
```

Asi no hay ventana entre comprobar y actuar, y el caso "no soy el destinatario" no depende
de que alguien se acuerde de escribir el `if`. La respuesta es 404 y no 403: confirmar que
ese aviso existe ya seria filtrar informacion de otro usuario.

### En el frontend

La campana consulta el contador **cada 60 segundos y solo con la pestaña visible**, mas
un refresco al volver a la pestaña. No hay push: para un proyecto de este tamaño, un
`COUNT` sobre un indice parcial una vez por minuto es mas barato —de operar y de
entender— que sostener una conexion abierta por usuario. Si hiciera falta inmediatez, el
lugar donde cambiarlo es ese unico `setInterval`.

---

## Búsqueda instantánea

`GET /api/search/quick?q=&limit=` — público.

**Un desplegable que responde desde la primera letra no puede consultar MusicBrainz.**
Su límite es **1 request por segundo para toda la aplicación**, y la búsqueda unificada
son tres llamadas. Escribir "nirvana" son siete pulsaciones: incluso con debounce, cada
consulta tardaría segundos y una sola persona tecleando dejaría sin catálogo a las demás.
Spotify puede hacerlo porque el índice es suyo.

La salida es la misma: **el índice es nuestro**. El desplegable consulta únicamente
Postgres —artistas y álbumes ya cacheados— y responde en milisegundos. La búsqueda
completa contra MusicBrainz sigue existiendo detrás de Enter.

### El catálogo se siembra solo

Si el catálogo local creciera únicamente cuando alguien abre el detalle de un álbum, el
desplegable estaría vacío durante semanas. Por eso **cada búsqueda completa persiste sus
veinte mejores resultados**: cada persona que busca deja el índice un poco más útil para
la siguiente.

Las filas sembradas son parciales y llevan `CachedAt` en el epoch **a propósito**: no
pasaron por el lookup de detalle, así que les falta lo que solo llega ahí. Con esa fecha
quedan siempre "vencidas" y se refrescan solas la primera vez que alguien las abre. No
hizo falta una columna de "esto es un esbozo": la fecha ya lo dice.

Se siembran los mejores según **nuestro** orden, no los primeros que devolvió MusicBrainz:
el orden de la API no distingue entre homónimos, que es todo el problema que resuelve
`AlbumSearchRanking`. Y solo cuando la búsqueda salió efectivamente a la red, no cuando se
sirvió de la caché en memoria: si no, paginar dejaría de ser gratis.

### Cómo se ordena

Los resultados vienen **mezclados**, no separados por tipo: el desplegable es una sola
lista. El criterio, en orden:

1. **Calidad del match textual** — exacto, luego prefijo, luego contiene. Manda sobre todo
   lo demás: quien escribe "nir" quiere lo que empieza con "nir", no el disco más reseñado
   que en algún lado contiene esas letras.
2. **Popularidad local** — favoritos y reseñas.
3. **Título más corto** — "Nirvana" antes que "Nirvana Tribute Band".

Se piden hasta `limit` de cada tipo y se mezclan después: pedir `limit` repartido dejaría
que un tipo con muchos resultados tape al otro, y el desplegable tiene que poder mostrar
el artista **y** sus discos.

Los comodines de `LIKE` se escapan. No es una inyección —el valor viaja como parámetro—
pero sin eso escribir `%` devuelve el catálogo entero.

**Lo que no cubre:** las canciones. La aplicación no las persiste, porque se reseña el
álbum y no el tema. Aparecen en la página de resultados completa.

**Si el catálogo crece mucho**, el `ILIKE '%...%'` deja de escalar porque no puede usar un
índice B-tree. El reemplazo natural es `pg_trgm` con un índice GIN, que es una extensión y
una migración; para el volumen de este proyecto no hace falta todavía.

### Portadas en todos los listados

La URL de Cover Art Archive es **determinística** a partir del MBID. En el detalle de un
álbum se hace igual un `HEAD` antes de guardarla, porque ahí se persiste y no vale la pena
dejar una rota en la base. **En los listados no**: un listado son cincuenta álbumes, y
comprobar cada uno serían cincuenta requests a un tercero para pintar una grilla.

Se emite la URL directamente y decide el navegador. El frontend dibuja siempre el
recuadro y mete la imagen adentro; si la portada no existe, la imagen se borra sola y
queda el hueco del tamaño correcto. Al revés —reemplazar la imagen por un recuadro cuando
falla— no funciona: el error puede dispararse antes de que el nodo esté en el documento.

### La foto del artista sale de Wikidata

MusicBrainz **no aloja imágenes**. Lo que sí tiene son relaciones hacia otros sitios, y
una apunta a Wikidata; Wikidata guarda en `P18` el nombre del archivo en Wikimedia
Commons; Commons lo sirve por una URL construible. La cadena es
`MBID → relación wikidata → P18 → Commons`.

El primer paso **sale gratis**: la relación viene en el mismo lookup del artista agregando
`inc=url-rels`, así que no consume otro turno de la cola de 1 req/seg. Solo la llamada a
Wikidata es adicional, y Wikidata no impone ese límite.

Se eligió Commons porque sus imágenes tienen licencia libre y son enlazables. Cualquier
otra fuente —resultados de un buscador, la foto de un perfil— sería republicar material
ajeno sin derecho.

**Un solista es un "Artista", no una "Person".** MusicBrainz clasifica así porque su
modelo distingue personas de grupos; para quien lee la pantalla eso es ruido. La
traducción vive en el frontend: el dato crudo se conserva en la API, que es donde tiene
sentido.

---

## Avatar y contraseña

| Método | Ruta | Auth | Qué hace |
|--------|------|------|----------|
| POST | `/api/users/me/avatar` | Bearer | Sube el avatar (multipart, campo `file`) |
| DELETE | `/api/users/me/avatar` | Bearer | Vuelve a la inicial |
| POST | `/api/auth/password` | Bearer | Cambia la contraseña y revoca todas las sesiones |

### El formato lo deciden los bytes

La extensión y el `Content-Type` los elige quien sube el archivo. Un `.jpg` declarado como
`image/jpeg` puede ser cualquier cosa, y como el avatar después se sirve **desde nuestro
dominio**, un archivo que el navegador decida interpretar como HTML es un XSS con nuestro
origen.

`ImageSignature` mira los primeros doce bytes y acepta JPEG, PNG, WebP y GIF. **El SVG no
está en la lista a propósito**: es una imagen legítima, pero es XML y admite `<script>`
adentro.

La otra mitad de la defensa está al servir: los avatares salen con
`X-Content-Type-Options: nosniff`, y el `Content-Type` de la respuesta es el que se dedujo
de los bytes al subir, no el que sugiere la extensión de la URL. Sin `nosniff`, el
navegador puede ignorar el `Content-Type` y decidir por su cuenta.

### Se guardan en la base, no en disco

Los avatares son filas de la tabla `UserAvatars` y los sirve `AvatarsController`. La
primera implementación escribía archivos; se cambió al elegir dónde desplegar.

En Cloud Run —y en el plan gratuito de casi cualquier plataforma— **el filesystem del
contenedor es efímero**: se recrea en cada versión publicada y en cada arranque en frío. Un
avatar escrito en disco desaparece sin aviso, y el síntoma es de los peores: la aplicación
funciona, nadie ve un error, y las fotos se borran solas cada tanto.

Guardar binarios en la base no es lo que uno elige por gusto —lo natural es un bucket de
objetos—, pero acá el costo está acotado y es predecible:

- Cada imagen pesa como máximo el tope de subida, 2 MB.
- Vive en su **propia tabla**, no en una columna de `AspNetUsers`. Una columna `bytea` ahí
  viajaría en cada consulta que traiga un usuario —y son casi todas— salvo que cada
  proyección se acuerde de excluirla.
- Se lee únicamente cuando alguien pide la imagen.

El identificador de la fila es el que aparece en la URL (`/avatars/{32 hex}.png`), y cambiar
la foto **crea una fila nueva con otro id**. Eso hace que la URL anterior deje de resolver
por sí sola, y por lo tanto que estas respuestas se puedan cachear indefinidamente sin
depender de que ningún intermediario haga caso a una invalidación.

El nombre del archivo nunca lo elige el cliente. Y al servir, el texto de la URL se valida
contra el formato exacto —32 hexadecimales— antes de tocar la base: no hay riesgo de
recorrido de directorios porque no hay disco, pero sí de convertir cualquier cadena en una
consulta.

Si algún día corresponde un bucket, `IAvatarStorage` es la única pieza que cambia.

El tope se comprueba **tres veces**, y no es redundancia: el navegador corta antes de
gastar la conexión del usuario, `RequestSizeLimit` corta antes de leer el cuerpo, y el
servicio produce el mensaje que el usuario entiende. Una validación que solo vive en el
cliente no es una validación.

### El `avatarUrl` se valida como URL absoluta http/https

No es formalismo: ese campo se renderiza en el perfil publico que ve cualquiera, asi que
un `javascript:` guardado ahi es un XSS almacenado servido a todos los visitantes.

### Cambiar la contraseña cierra todas las sesiones

Pide la contraseña actual aunque la sesión ya esté abierta: si alguien deja el navegador
desbloqueado, no debería poder quedarse con la cuenta con dos clicks.

Y revoca **todos** los refresh tokens, incluido el de quien hizo el cambio. Cambiar la
contraseña se hace, casi siempre, porque se sospecha que alguien más entró; si las
sesiones viejas siguieran renovándose, el cambio no serviría para nada.

---

## Home

| Metodo | Ruta | Auth | Que hace |
|--------|------|------|----------|
| GET | `/api/home/popular?page=&pageSize=&windowDays=` | publico | Albumes con mas movimiento reciente |
| GET | `/api/home/explore?page=&pageSize=` | publico | Albumes del catalogo para descubrir |
| GET | `/api/feed?cursor=&limit=` | Bearer | Actividad de a quienes se sigue |

Son **tres endpoints, no uno**. El home los pide en paralelo y pinta cada seccion cuando
llega la suya; una seccion que falla se queda con su error y las otras siguen en pie. Y
una seccion vacia **se oculta entera**: un home con tres titulos y nada debajo se ve
roto, aunque tecnicamente este bien.

Los dos primeros son publicos: un visitante sin cuenta tiene que poder llegar, ver que
hay y engancharse. Igual leen el usuario actual cuando esta, porque con sesion la
respuesta cambia.

### Populares: se cuenta desde la actividad, no desde los albumes

La forma directa —recorrer los albumes y contarle a cada uno su movimiento con
subconsultas— toca **todo el catalogo** para descubrir que la mayoria no tuvo ninguno.
Aca se hace al reves: cada consulta arranca de las filas que ocurrieron dentro de la
ventana, que son pocas y estan indexadas por fecha, y agrupa por album.

Son **tres consultas y la mezcla en memoria**, igual que en el timeline de actividad y
por la misma razon: son tres agregaciones que el motor resuelve con su propio indice, y
un `UNION` las dejaria sin poder usarlo. Los votos necesitan un join explicito contra
reseñas porque `Like` es polimorfico y no tiene FK.

**Los pesos importan.** Escribir una reseña es una señal de interes mucho mas fuerte
que hacer un click; sin pesos, un album con cincuenta votos desplazaria a uno con diez
reseñas escritas:

| Actividad | Peso |
|-----------|------|
| Reseña publicada | 3 |
| Comentario | 2 |
| Voto sobre una reseña | 1 |

Los comentarios borrados no suman: el hilo sigue mostrando el hueco, pero el album no
deberia seguir cobrando popularidad por algo que ya no se lee.

**Lo que esto no es.** El resultado no esta acotado por el tamaño de la pagina sino por
la ventana. Con mucho trafico, treinta dias de actividad no entran comodamente en memoria
y esto habria que reemplazarlo por un contador materializado que se actualice con cada
accion. Para el volumen de este proyecto la version derivada es preferible: no hay nada
que sincronizar, y borrar una reseña le quita su peso sin que nadie tenga que acordarse.

### Explorar: lo ultimo que entro al catalogo

El catalogo local **no se precarga**: un album esta ahi porque alguien lo busco. Asi que
"lo mas nuevo" es literalmente lo que la gente estuvo mirando, y ademas cambia solo con
el uso, que es lo que hace que la seccion no muestre siempre lo mismo.

Con sesion se excluye lo que el usuario ya reseñó —recomendarle lo que ya escucho y
puntuo no descubre nada—. Los que tienen portada van primero: la seccion es una grilla de
tapas grandes y una fila de recuadros vacios no invita a explorar.

**Se pagina por offset, al reves que los feeds.** No es una inconsistencia. Un feed crece
por arriba: lo nuevo entra en la primera posicion, corre todo un lugar y el offset repite
filas. Este orden, en cambio, se mueve a la velocidad a la que alguien cachea un album
nuevo, no a la de un scroll.

### Las dos secciones deciden primero que mostrar y despues piden los datos

`LoadAlbumsAsync` trae los albumes de la pagina con sus estadisticas en una sola
consulta. Separarlo evita el N+1 obvio —una consulta por tarjeta— y tambien uno menos
obvio: calcular promedio y cantidad de reseñas durante el ranking obligaria a
computarlos para **todo** el catalogo y no solo para las quince filas que se van a
mostrar.

### El scroll infinito usa IntersectionObserver, no un listener de scroll

Un listener de `scroll` se dispara decenas de veces por segundo y obliga a medir el
layout en cada una: es la receta clasica del scroll que tironea. El observador avisa una
sola vez, cuando el centinela del final entra en pantalla, y con `rootMargin: 400px` la
pagina siguiente ya esta cargando cuando el usuario termina de ver la anterior.

El observador se **desconecta al cambiar de vista**. Sin eso, cada visita al home dejaria
uno mas vivo sobre nodos que ya no estan en el DOM.

---

## Noticias

`GET /api/news?limit=` — publico.

**Solo titulo, extracto corto, imagen y enlace a la fuente original.** Nada mas, y es
deliberado: republicar el articulo entero seria reproducir obra ajena y ademas quitarle
la visita a quien la escribio. Lo que se hace aca es lo que hace cualquier agregador
serio: mostrar lo justo para que alguien decida si le interesa y mandarlo al sitio
original. Por eso el nombre del medio viaja en cada item y se muestra siempre.

Las fuentes se configuran en la seccion `News` de `appsettings.json`. **No estan fijas en
el codigo** porque una URL de feed es lo primero que un medio cambia cuando migra de CMS,
y eso no deberia requerir recompilar. Con la lista vacia el endpoint devuelve nada y el
frontend oculta la seccion.

### Un solo parser para RSS y Atom

Los medios publican en uno u otro segun el CMS que usen y no hay forma de elegir. Las
diferencias son pocas —`item` contra `entry`, `link` como texto contra `link` como
atributo `href`, `pubDate` contra `published`— asi que se normalizan en `RssParser` y
hacia afuera todo es un `NewsItemDto`.

**Nada de esto puede lanzar por culpa de un feed.** Es el unico punto de la aplicacion
que procesa contenido de un tercero sobre el que no se tiene ningun control: un XML mal
formado, un item sin titulo, una fecha en un formato raro o una pagina de error servida
con 200 son cosas que pasan y no son un error de esta aplicacion. Lo que no se entiende
se descarta.

**Las URLs se validan como http/https absolutas.** Esto no es formalismo: el enlace de la
noticia termina en un `<a href>` del navegador, y un `javascript:` ahi es ejecucion de
codigo en la sesion del usuario. Un item cuyo enlace no pasa el filtro se descarta entero
—un extracto que no se puede atribuir es justo lo que no se quiere publicar—.

**El orden de limpiar el HTML importa.** Primero se sacan las etiquetas y despues se
decodifican las entidades. Al reves, `&lt;b&gt;` se convertiria en `<b>` y el paso
siguiente lo borraria como si fuera una etiqueta, perdiendo texto que el autor escribio a
proposito.

### Se sirve viejo antes que vacio

La cache guarda la ultima copia buena por bastante mas tiempo del que la considera fresca
(30 minutos frescos, 24 horas de respaldo). Cuando vence la frescura se intenta refrescar,
y **si ningun medio responde se devuelve igual lo que ya se tenia**. Para una seccion de
relleno, una noticia de ayer es mejor que un hueco.

Cada feed falla por su cuenta y tiene su propio timeout: un medio caido no puede impedir
que se muestren los otros ni hacer esperar a la portada. Van en paralelo porque son sitios
distintos y no hay ninguna cola compartida que respetar —al reves que con MusicBrainz—.

El refresco esta serializado con un semaforo **estatico**. Sin eso, veinte visitas
simultaneas al home con la cache vencida disparan veinte rondas de descargas contra los
mismos medios; el que entra refresca y los demas se encuentran el resultado ya hecho. Es
estatico porque el servicio es scoped: una instancia por request no podria coordinar nada.

### Nada de esto se guarda en la base

Una noticia no tiene estado propio en esta aplicacion —no se puntua, no se comenta, no se
guarda—, asi que persistirla seria mantener una copia de contenido ajeno que ademas hay
que sincronizar cuando el medio la edita o la baja. Alcanza con la cache en memoria.

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

Cubren la logica pura, la mas propensa a bugs sutiles y la que no necesita base:

- `CommentTreeBuilder` (23 casos) — ver la seccion de comentarios anidados.
- `AlbumSearchRanking` (15) — ver el ordenamiento de la busqueda.
- `SongSearchGrouping` (22) — normalizacion de titulos, agrupado de versiones y orden.
- `RssParser` (23) — RSS y Atom, feeds rotos, extractos y filtrado de URLs peligrosas.
- `ImageSignature` (8) — reconocimiento de formato por bytes y rechazo del SVG.
- `ActivityCursor` (20) — ida y vuelta, empates de instante y paginacion completa.
- `KeysetCursor` (9) — ida y vuelta, precision de microsegundos y degradacion ante un
  cursor corrupto.

### Integracion (`tests/MusicReviews.IntegrationTests`)

Levantan la Api completa con `WebApplicationFactory<Program>` contra un **PostgreSQL
real en Docker** (Testcontainers). Requieren Docker corriendo; el contenedor se crea y
se destruye solo.

**Por que Postgres real y no un proveedor en memoria.** Buena parte de lo que hay que
verificar vive en la base y no existe en InMemory: los indices unicos que sostienen
"una review por album" y "un voto por objetivo", el CHECK del puntaje, el borrado en
cascada, y la traduccion real de las proyecciones a SQL. Un test contra InMemory
pasaria con un modelo que en produccion falla.

**MusicBrainz y los feeds de noticias se reemplazan por fakes.** `FakeMusicCatalogService` persiste artistas y
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
de profundidad, respuesta a un comentario de otra review), administracion (roles,
403 vs 401, las dos protecciones al quitar roles), seguimientos, actividad y
notificaciones, las secciones del home y el endpoint de noticias.

Los tests de notificaciones **hacen la accion real** —comentar, votar, seguir— y despues
miran la campana. Es la unica forma de verificar que el enganche existe: un aviso nunca
se crea por una llamada directa, siempre es efecto de otra cosa.

---

## Frontend

Abrilo en http://localhost:5080 — la Api lo sirve desde `wwwroot`, en el mismo origen.

| Pantalla | Ruta |
|----------|------|
| Home: populares, actividad de a quienes seguis y explorar | `#/` |
| Busqueda unificada (artistas + albumes + canciones) | `#/search?q=...` |
| Artista con discografia y favoritos | `#/artist/{mbid}` |
| Album con reviews y formulario | `#/album/{mbid}` |
| Review con el hilo de comentarios | `#/review/{id}` |
| Perfil publico | `#/user/{userName}` |
| Editar perfil propio | `#/me` |
| Notificaciones | `#/notifications` |
| Login / registro | `#/login`, `#/register` |

### Cache de los archivos estaticos en desarrollo

`UseStaticFiles` manda `ETag` y `Last-Modified` pero ningun `Cache-Control`, y ante esa
ausencia el navegador aplica **cache heuristica**: se queda con la copia que tiene sin
preguntar. Con modulos ES eso no degrada, **rompe**: si `app.js` llega nuevo y `api.js`
sale de la cache viejo, el import falla en el enlace
(`does not provide an export named X`) y **no se ejecuta absolutamente nada** —ni la barra
de navegacion ni el router—. La pagina queda en blanco con un unico error en consola que
ni siquiera nombra al archivo culpable.

Por eso en desarrollo la respuesta lleva `Cache-Control: no-cache, must-revalidate`.
`no-cache` no significa "no guardes" sino "guarda pero pregunta antes de usar": el ETag
sigue trabajando y lo que no cambio vuelve como `304` sin cuerpo. En produccion no se
toca, porque ahi el cacheo agresivo es lo que se quiere.

Si aparece la pagina en blanco despues de un cambio en el frontend, la primera prueba es
recargar sin cache (`Ctrl` + `Shift` + `R`).

### Por que HTML y JS sin framework

El objetivo era una interfaz **completa pero sin peso muerto**. Un puñado de modulos ES
servidos desde `wwwroot` cumple eso sin agregar nada al proyecto: cero build, cero
dependencias, cero segundo proceso, cero CORS. El backend es API-first y el cliente
consume la misma Api publica que consumiria cualquier otro, asi que reemplazarlo por uno
en Angular o React no toca una linea del servidor.

Lo que en un framework viene resuelto —enrutado, reactividad, listas infinitas,
autocompletado— aca esta escrito a mano y documentado mas abajo. Es deliberado: el
proyecto muestra el mecanismo, no la configuracion del mecanismo.

Se usa **hash routing** (`#/album/...`) a proposito: no necesita fallback del servidor
para las rutas del cliente, asi que `UseStaticFiles` alcanza y no hay que interceptar
404 para devolver `index.html`.

### El texto que ve el usuario va acentuado; el codigo no

Los mensajes de error, las validaciones y todo el texto de la interfaz estan escritos en
castellano correcto, con tildes y con ñ: es lo que el usuario lee, y "resenia" en una
pantalla se ve como un descuido.

Los **identificadores** —clases, metodos, propiedades, nombres de test— siguen en ASCII a
proposito. C# admite Unicode en identificadores, pero un `Reseña` en el codigo obliga a
escribir la ñ para autocompletar, complica cualquier `grep` desde una terminal sin teclado
español y no aporta nada. La linea es simple: **si lo lee una persona en la pantalla, va
bien escrito; si lo lee el compilador, va en ASCII.**

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
├── Dockerfile                    Build multi-etapa: SDK para compilar, runtime para servir
├── docker-compose.yml            PostgreSQL + pgAdmin para desarrollo
├── .env.example                  Variables que espera el servicio en produccion
├── .github/workflows/ci.yml      Compilacion, tests e imagen en cada push
├── Directory.Build.props         Propiedades comunes a todos los proyectos
├── Directory.Packages.props      Versiones centralizadas de paquetes (CPM)
├── docs/                         Especificacion tecnica
├── src/
│   ├── MusicReviews.Domain/          Entidades y enums. Sin EF, sin infraestructura.
│   ├── MusicReviews.Application/     Logica de negocio, interfaces de servicios, validadores.
│   ├── MusicReviews.Infrastructure/  DbContext, configuraciones EF, repositorios, clientes HTTP.
│   └── MusicReviews.Api/             Controllers, DTOs, middlewares, composicion y frontend.
└── tests/
    ├── MusicReviews.UnitTests/         Logica pura, sin base ni red.
    └── MusicReviews.IntegrationTests/  Api completa contra PostgreSQL en Docker.
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
| `UserFollow` | `(FollowerId, FollowedId)` | PK compuesta; la relacion es el registro |
| `Notification` | `int` | `ActorId` nullable para avisos del sistema; `IsRead` + `ReadAt` |
| `UserAvatar` | `Guid` | Bytes de la foto de perfil; tabla aparte para que no viaje en las consultas de usuario |

### Indices

| Tabla | Indice | Para que |
|-------|--------|----------|
| `Artists` | `MusicBrainzId` (unico), `Name` | Deduplicar catalogo, buscar local antes de salir a MusicBrainz |
| `Albums` | `MusicBrainzId` (unico), `Title`, `ArtistId` | Idem + discografia por artista |
| `Reviews` | `(UserId, AlbumId)` unico, `(AlbumId, CreatedAt DESC)` | Una review por album; listado por album ordenado |
| `Comments` | `(ReviewId, CreatedAt)`, `ParentCommentId`, `UserId` | **Traer el hilo completo con un solo `WHERE ReviewId = X`** |
| `Likes` | `(UserId, TargetType, TargetId)` unico, `(TargetType, TargetId, IsLike)` | Impedir doble voto; contar votos sin joins |
| `Notifications` | `(RecipientId, CreatedAt DESC, Id DESC)` | Listado paginado por cursor |
| `Notifications` | `RecipientId` **parcial** `WHERE NOT IsRead` | El contador del globito, sin indexar el historial leido |
| `Notifications` | `(RecipientId, ActorId, Type, ReviewId, CommentId)` | Encontrar el aviso equivalente para refrescarlo o retirarlo |
| `UserAvatars` | `UserId` | Encontrar y borrar la foto anterior al reemplazarla |

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

## Despliegue

La instancia publica corre en **Google Cloud Run** contra **PostgreSQL administrado en
Neon**. Los pasos completos estan en
[docs/despliegue-cloud-run.md](docs/despliegue-cloud-run.md); aca queda lo que hay que
saber para entender el contenedor.

El `Dockerfile` es multi-etapa: compila con el SDK y ejecuta sobre la imagen de runtime de
Alpine, como usuario sin privilegios y sin compilador ni codigo fuente adentro.

```bash
docker build -t musicreviews .
docker run -p 8080:8080 --env-file .env musicreviews
```

### Lo que la aplicacion espera del entorno

`.env.example` tiene la lista completa. Lo que no es evidente:

| Variable | Por que |
|----------|---------|
| `DATABASE_URL` | Las plataformas publican la base como URI (`postgres://...`), formato que Npgsql no acepta. La aplicacion la traduce al arrancar (`DatabaseUrl`), asi que se pega tal cual. |
| `PORT` | La plataforma elige el puerto. El contenedor escucha ahi y en `0.0.0.0`: en `localhost` el balanceador no lo alcanza y el despliegue queda "unhealthy" sin un solo error en el log. |
| `Jwt__SigningKey` | Sin valor, la aplicacion no arranca. Es deliberado: una clave por defecto en el codigo es una clave publicada. |
| `Database__MigrateOnStartup` | En `false` cuando las migraciones se aplican como paso previo al despliegue, que es lo recomendado en Cloud Run. |

### El contenedor no escribe en disco

Es la restriccion que ordena el resto. En Cloud Run —y en el plan gratuito de practicamente
cualquier plataforma— **el filesystem es efimero**: se recrea en cada version publicada y
en cada arranque en frio.

Lo unico que un usuario escribe son los avatares, y por eso se guardan **en la base**, en
su propia tabla. No es lo que uno elegiria con un bucket de objetos disponible, pero el
sintoma de la alternativa seria de los peores: la aplicacion funciona, nadie ve un error, y
las fotos de perfil desaparecen solas cada tanto. Ver la seccion de avatares mas arriba.

### Detras del proxy

La peticion llega por http y con la IP del balanceador. `UseForwardedHeaders` va primero
en el pipeline para que el limitador de peticiones vea la IP real —si no, particiona todo
el trafico en una sola direccion y castiga a todos juntos— y para que la redireccion a
https no entre en bucle. `KnownProxies` queda vacio a proposito: en estas plataformas la
IP del balanceador es dinamica y el unico camino de entrada al contenedor es ese proxy.

### Contenido de demostracion

Una instancia recien desplegada arranca con el catalogo vacio, y el home es un feed: sin
albumes ni reseñas no muestra nada. Con `Demo__Enabled=true` y `Demo__Password` definida,
`DemoSeeder` siembra catalogo, seis cuentas, reseñas, hilos y votos. Es idempotente: si las
cuentas ya existen, no hace nada.

Corre **en segundo plano**, no como paso del arranque: son unas cuarenta peticiones a
MusicBrainz a una por segundo, y bloquear el arranque durante ese minuto haria que la
plataforma diera el despliegue por fallido y reiniciara el contenedor, que volveria a
sembrar desde cero.

Los albumes se resuelven por busqueda y no por MBID escrito a mano, porque un identificador
equivocado no falla de forma ruidosa: devuelve 404 y deja un catalogo incompleto que nadie
nota.

> **En Cloud Run la siembra se hace desde la maquina local**, apuntando a la base de Neon.
> Cloud Run estrangula la CPU del contenedor cuando no esta atendiendo una peticion, asi
> que un trabajo de fondo de un minuto nunca termina. La guia de despliegue lo detalla.

---

## Integracion continua

`.github/workflows/ci.yml` corre en cada push y cada pull request:

1. **Compilacion y tests unitarios** con `-warnaserror`. Un warning no frena el trabajo en
   local, pero no llega a `main`.
2. **Tests de integracion** en un job aparte, porque levantan PostgreSQL con Testcontainers
   y tardan varias veces mas: asi un error de compilacion se ve en un minuto y no en cinco.
3. **Imagen de contenedor**, que se compila sin publicarse, para que un `Dockerfile` roto
   se descubra en el pull request y no en el despliegue.

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
