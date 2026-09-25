# MusicReviews — Especificacion tecnica

Version 1.0 · Septiembre de 2026

Este documento describe **que** hace el sistema, **como** esta construido y **por que** se
tomo cada decision estructural. El [README](../README.md) es la guia de uso y contiene el
detalle de implementacion de cada modulo; aca esta la vista de conjunto.

---

## Indice

1. [Alcance](#1-alcance)
2. [Arquitectura](#2-arquitectura)
3. [Modelo de datos](#3-modelo-de-datos)
4. [Catalogo musical](#4-catalogo-musical)
5. [Comentarios anidados](#5-comentarios-anidados)
6. [Social, actividad y avisos](#6-social-actividad-y-avisos)
7. [Busqueda](#7-busqueda)
8. [Seguridad](#8-seguridad)
9. [Rendimiento](#9-rendimiento)
10. [Estrategia de pruebas](#10-estrategia-de-pruebas)
11. [Despliegue y operacion](#11-despliegue-y-operacion)
12. [Alternativas descartadas](#12-alternativas-descartadas)
13. [Limitaciones conocidas](#13-limitaciones-conocidas)

---

## 1. Alcance

### 1.1 Que es

Una aplicacion web donde los usuarios puntuan y reseñan albumes, discuten esas reseñas en
hilos y se siguen entre si. El modelo de referencia es RateYourMusic para el catalogo y
Letterboxd para la parte social.

El catalogo no se administra a mano: se alimenta de **MusicBrainz**, la base de datos
musical abierta, y las portadas de **Cover Art Archive**. La aplicacion no almacena musica
ni fragmentos de audio.

### 1.2 Actores

| Actor | Puede |
|-------|-------|
| Anonimo | Explorar el catalogo, buscar, leer reseñas y comentarios, ver perfiles publicos |
| Usuario | Todo lo anterior, mas reseñar, comentar, votar, seguir, marcar favoritos, editar su perfil |
| Administrador | Todo lo anterior, mas moderar reseñas y comentarios, gestionar roles y ver estadisticas |

### 1.3 Reglas de negocio

- Un usuario tiene **como maximo una reseña por album**. La restriccion vive en un indice
  unico de la base, no solo en el servicio.
- El puntaje es un entero de **0 a 100**, con un CHECK en la base.
- Los comentarios se anidan hasta **diez niveles**. Es un limite de producto, no tecnico:
  el armado del arbol soporta cualquier profundidad.
- Un voto es positivo o negativo, **uno por usuario y objetivo**, y se puede retirar.
- Seguir es una relacion **dirigida**: que A siga a B no implica lo contrario. Nadie puede
  seguirse a si mismo.
- El borrado de comentarios es **logico**. El de reseñas es fisico y arrastra sus
  comentarios y votos.

### 1.4 Fuera de alcance

Reproduccion de audio, listas de reproduccion, mensajeria privada, recomendaciones
personalizadas, importacion de bibliotecas de terceros y cualquier forma de monetizacion.

---

## 2. Arquitectura

### 2.1 Capas

```
MusicReviews.Api             Controllers, filtros, middlewares, composicion, frontend
        ↓
MusicReviews.Infrastructure  DbContext, EF, clientes HTTP, implementacion de servicios
        ↓
MusicReviews.Application     Contratos, DTOs, validadores, logica que no depende de nada
        ↓
MusicReviews.Domain          Entidades y enums. No referencia a nadie.
```

Las dependencias apuntan siempre hacia adentro. `Domain` no conoce EF Core; `Application`
declara interfaces (`IReviewService`, `IMusicCatalogService`, `IAvatarStorage`) que
`Infrastructure` implementa; `Api` no menciona Npgsql ni MusicBrainz en ninguna linea.

**No es Clean Architecture completa y no pretende serlo.** No hay capa de casos de uso, no
hay CQRS, no hay MediatR. Para un sistema de este tamaño esas piezas agregan indireccion
sin resolver un problema que exista: un servicio por agregado, inyectado donde hace falta,
se lee mejor y se prueba igual. La decision esta tomada con el costo presente: si el
dominio creciera hasta necesitar decenas de casos de uso con comportamiento transversal,
el paso a mediador seria mecanico porque las interfaces ya estan separadas de sus
implementaciones.

### 2.2 Manejo de errores

Los errores previsibles no viajan como excepciones. Cada operacion devuelve
`Result` o `Result<T>`, que transporta un `Error` con codigo (`reviews.duplicate`,
`users.avatar_too_large`) y tipo (`NotFound`, `Conflict`, `Validation`, `Forbidden`).
`ApiResults` traduce ese tipo al codigo HTTP en un solo lugar.

La ventaja practica: un servicio no puede olvidarse de un caso de error, porque el
compilador lo obliga a devolver algo, y la traduccion a HTTP no queda dispersa en los
controllers. Las excepciones quedan para lo que si es excepcional, y ahi las recoge
`GlobalExceptionHandler`, que las convierte en `ProblemDetails` (RFC 9457).

### 2.3 Composicion

`AddInfrastructure` es el unico punto de registro de la capa de infraestructura. Sigue una
regla estricta: **nada lee la configuracion en el momento de registrar**. Los valores se
resuelven desde el contenedor cuando el servicio se construye.

No es purismo. Leer la configuracion al registrar captura una foto que puede quedar
desactualizada si despues se agrega otra fuente —que es exactamente lo que hace
`WebApplicationFactory` en los tests de integracion— y el sintoma es dificil de rastrear:
la aplicacion termina firmando tokens con una clave y validandolos con otra, o escribiendo
en una base distinta de la configurada.

---

## 3. Modelo de datos

### 3.1 Entidades

| Entidad | Clave | Notas |
|---------|-------|-------|
| `ApplicationUser` | `Guid` | Hereda de `IdentityUser<Guid>`; agrega `Bio`, `AvatarUrl`, `CreatedAt` |
| `Artist` | `int` | `MusicBrainzId` unico e indexado; `CachedAt` para invalidar la cache |
| `Album` | `int` | `MusicBrainzId` unico; FK a `Artist`; `ReleaseDatePrecision` para fechas parciales |
| `Review` | `int` | Unico `(UserId, AlbumId)`; CHECK `Score` entre 0 y 100 |
| `Comment` | `int` | `ParentCommentId` auto-referenciado; `Depth` materializado; borrado logico |
| `Like` | `int` | Polimorfico `(TargetType, TargetId)`; unico `(UserId, TargetType, TargetId)` |
| `FavoriteArtist` | `(UserId, ArtistId)` | PK compuesta, sin Id sustituto |
| `RefreshToken` | `int` | Guarda el hash SHA-256, nunca el token; rotacion con deteccion de reuso |
| `UserFollow` | `(FollowerId, FollowedId)` | PK compuesta; la relacion **es** el registro |
| `Notification` | `int` | `ActorId` nullable para avisos del sistema; `IsRead` + `ReadAt` |
| `UserAvatar` | `Guid` | Bytes de la foto de perfil; tabla aparte para que no viaje en las consultas de usuario |

### 3.2 Tres decisiones que definen el esquema

**El album se identifica por MBID, no por Id local.** Toda la Api publica habla en
identificadores de MusicBrainz. El Id entero existe solo puertas adentro. Consecuencia
util: reseñar un album que todavia no esta en la base no requiere un paso previo de alta
—el servicio lo trae y lo persiste en la misma operacion— y el cliente nunca necesita
saber si el catalogo local ya lo tenia.

**Los votos son polimorficos.** Una sola tabla `Likes` con `(TargetType, TargetId)` en vez
de `ReviewLike` y `CommentLike`. Se gana una sola consulta de conteo y un solo servicio; se
pierde la integridad referencial, que pasa a sostenerla la aplicacion al borrar una reseña
o un comentario. Es un intercambio consciente, y el motivo por el que ese borrado esta
cubierto por tests.

**`Comment.ReviewId` esta denormalizado.** Toda respuesta, a cualquier profundidad, guarda
la reseña raiz. Esa redundancia es lo que permite traer un hilo completo con un unico
`WHERE ReviewId = X`, sin consultas recursivas y sin N+1. Ver la seccion 5.

### 3.3 Indices

| Tabla | Indice | Para que |
|-------|--------|----------|
| `Artists` | `MusicBrainzId` (unico), `Name` | Deduplicar catalogo, buscar local antes de salir a la red |
| `Albums` | `MusicBrainzId` (unico), `Title`, `ArtistId` | Idem, mas discografia por artista |
| `Reviews` | `(UserId, AlbumId)` unico, `(AlbumId, CreatedAt DESC)` | Una reseña por album; listado ordenado |
| `Comments` | `(ReviewId, CreatedAt)`, `ParentCommentId`, `UserId` | Hilo completo en una consulta |
| `Likes` | `(UserId, TargetType, TargetId)` unico, `(TargetType, TargetId, IsLike)` | Impedir doble voto; contar sin joins |
| `Notifications` | `(RecipientId, CreatedAt DESC, Id DESC)` | Paginacion por cursor |
| `Notifications` | `RecipientId` **parcial**, `WHERE NOT IsRead` | Contador de no leidos sin indexar el historial |
| `Notifications` | `(RecipientId, ActorId, Type, ReviewId, CommentId)` | Encontrar el aviso equivalente para refrescarlo o retirarlo |
| `UserAvatars` | `UserId` | Encontrar y borrar la foto anterior al reemplazarla |

El indice parcial de no leidos merece una nota: el historial de avisos leidos crece sin
limite y no se consulta nunca para el contador. Indexarlo entero seria pagar espacio y
escrituras por filas que la consulta descarta siempre.

---

## 4. Catalogo musical

### 4.1 La restriccion que ordena todo

MusicBrainz permite **una peticion por segundo por cliente**, y lo hace cumplir. Esa unica
regla condiciona cada decision de este modulo.

La aplicacion mantiene una **cola global con intervalo minimo de 1100 ms**. No es un
limitador por peticion ni por usuario: es un unico punto por el que pasa todo el trafico
saliente hacia MusicBrainz, porque el limite lo cuenta el servidor remoto por cliente, no
por sesion.

### 4.2 Cache en dos niveles

| Que | Donde | Cuanto | Por que |
|-----|-------|--------|---------|
| Busquedas | Memoria | Minutos | Cambian con cada consulta; no vale la pena persistirlas |
| Detalle de artista y album | PostgreSQL | Dias (`CachedAt`) | Es el catalogo: se consulta muchas veces y cambia poco |

Los detalles son **cache-first**: la primera consulta trae de la red y persiste, las
siguientes se sirven de la base hasta que el registro queda viejo. Las busquedas van
siempre a la red porque la cache local solo contiene lo que alguien ya consulto: buscar
ahi devolveria un subconjunto arbitrario y creciente, que es peor que no buscar.

### 4.3 Portadas y fotos

Las portadas de Cover Art Archive tienen **URL deterministica** a partir del MBID del
release-group. No hace falta consultar nada: se arma la URL y el navegador la pide. Si no
existe, la imagen falla y el frontend cae al recuadro vacio.

Las fotos de artista son mas caras. MusicBrainz no aloja imagenes; lo unico que ofrece es
un enlace a Wikidata (`inc=url-rels`), de ahi se saca la propiedad P18 y de ahi el archivo
en Wikimedia Commons. Son tres saltos, y por eso se resuelve una sola vez y se guarda en
`Artist.ImageUrl`.

### 4.4 Canciones: agrupar lo que MusicBrainz no agrupa

MusicBrainz modela una grabacion por cada version registrada de un tema: el master, cada
remasterizacion, cada version en vivo, cada edicion por pais. Buscar un tema conocido
devuelve decenas de filas que para el usuario son la misma cancion.

`SongSearchGrouping` las colapsa en un resultado por tema. La clave de agrupacion es
`(titulo normalizado, artista normalizado)` unidos por un separador explicito —sin el,
`("abc","de")` y `("ab","cde")` producen la misma clave— y la normalizacion pliega
acentos con una tabla propia.

> **Nota de implementacion que costo un bug.** El proyecto compila con
> `InvariantGlobalization=true`, y bajo esa bandera `String.Normalize(NormalizationForm.FormD)`
> **no hace nada y no falla**: devuelve la cadena intacta. Toda comparacion que dependa de
> plegar acentos pasa silenciosamente a ser sensible a ellos. Por eso el plegado es una
> tabla explicita y hay un test que lo fija.

---

## 5. Comentarios anidados

Es el modulo con mas trabajo de diseño del sistema.

### 5.1 El problema

Un hilo es un arbol. La forma ingenua de leerlo es recursiva: traer los comentarios de
primer nivel, y para cada uno traer sus respuestas, y para cada respuesta las suyas. Eso
es N+1 con N variable y sin cota: un hilo de doscientos comentarios son doscientas
consultas, y el problema aparece recien cuando la aplicacion tiene usuarios.

### 5.2 La solucion

**Una sola consulta plana, arbol armado en memoria.**

Como todo comentario guarda `ReviewId` —incluidas las respuestas anidadas a cualquier
profundidad—, el hilo completo sale de un `WHERE ReviewId = X` ordenado por `CreatedAt`.
Con esa lista en memoria se arma el arbol en dos pasadas: la primera indexa cada nodo por
su Id en un diccionario; la segunda recorre la lista y cuelga cada nodo de su padre, o de
la raiz si `ParentCommentId` es null.

El costo es **O(n)** en tiempo y memoria, con **una** consulta, sea cual sea la
profundidad. El armado es iterativo, no recursivo: un hilo profundo no puede desbordar la
pila.

### 5.3 Detalles que importan

**`Depth` se materializa al insertar** (`padre.Depth + 1`) y no se recalcula. Permite
aplicar el limite de anidamiento sin subir por la cadena de padres, que seria una consulta
por nivel justo en el camino de escritura.

**El borrado es logico.** Borrar fisicamente un nodo intermedio obliga a elegir entre
romper el hilo o cascadear respuestas de otros usuarios. Con `IsDeleted` el nodo
sobrevive como `[eliminado]`, el arbol queda intacto y la conversacion se sigue leyendo.

**Los contadores de votos se resuelven con subconsultas correlacionadas** dentro de la
misma proyeccion, no con una consulta por comentario. La forma es mas incomoda de escribir
pero es la que produce un solo viaje a la base.

---

## 6. Social, actividad y avisos

### 6.1 No hay tabla de actividad

La actividad de un usuario —reseñas, comentarios y votos, en orden cronologico— **no se
persiste**. Se deriva de las tablas que ya existen con una union de tres consultas
acotadas.

El motivo es que una tabla de actividad seria una copia denormalizada de datos que ya
estan, con dos costos permanentes: una escritura extra en cada accion, y la posibilidad de
que quede desincronizada cuando algo se borra o se edita. La derivacion es mas cara al
leer, pero se paga solo cuando alguien mira, y ese costo esta acotado por la paginacion.

### 6.2 Los avisos si se persisten

La diferencia es el **estado de lectura**: `IsRead` no se puede derivar de ninguna otra
tabla. Ese dato solo existe si se guarda, y es lo que justifica la tabla.

Reglas del modulo:

- Comentar una reseña avisa a su autor; responder un comentario avisa a quien escribio el
  comentario padre, no al autor de la reseña.
- Nadie recibe avisos de sus propias acciones.
- **Poner y sacar un voto no acumula avisos.** Retirar un voto retira el aviso si todavia
  no se leyo; si ya se leyo, se conserva, porque borrar algo que la persona ya vio es mas
  confuso que dejarlo.
- Marcar como leido lleva el filtro por destinatario **dentro** del `UPDATE`, de modo que
  tocar un aviso ajeno es imposible por construccion. Un aviso de otro responde 404 y no
  403: un 403 confirmaria que existe.

### 6.3 Paginacion por cursor

Los listados que crecen por arriba —actividad, feed, avisos— paginan por **cursor**, no
por offset. Con offset, una fila nueva insertada entre dos peticiones desplaza todo y el
lector ve un elemento repetido o se saltea uno. El cursor codifica la posicion exacta
(`CreatedAt` en ticks UTC mas el Id como desempate) en Base64Url.

Hay dos implementaciones porque el problema no es el mismo: `KeysetCursor` para un listado
de una sola tabla, y `ActivityCursor` para la union de tres, donde el desempate tiene que
identificar tambien de que tabla vino la fila.

Los listados cuyo orden **no** crece por arriba —la discografia de un artista, los
resultados de una busqueda— siguen paginando por offset, que es mas simple y ahi no tiene
el problema.

---

## 7. Busqueda

Hay dos busquedas y responden a necesidades distintas.

**La busqueda completa** consulta MusicBrainz y devuelve artistas, albumes y canciones. Es
la que encuentra cualquier cosa que exista, y esta sujeta al limite de una peticion por
segundo.

**El desplegable instantaneo** responde desde la primera tecla. No puede consultar
MusicBrainz: a una peticion por segundo, escribir "radiohead" son nueve peticiones y ocho
segundos.

La contradiccion se resuelve moviendo el desplegable **al catalogo local**, y sembrando
ese catalogo desde cada busqueda completa. La primera vez que alguien busca un artista,
sus resultados quedan en la base; a partir de ahi el desplegable los encuentra al instante.
El catalogo se hace mas util con el uso, y la ultima fila del desplegable siempre ofrece
buscar el termino en MusicBrainz para lo que todavia no este.

El orden mezcla los tipos en una sola lista, como Spotify: primero calidad de coincidencia
(empieza con lo escrito, contiene lo escrito), despues popularidad local, y el titulo mas
corto como desempate. Separar por tipo obligaria al usuario a saber de antemano si lo que
busca es un artista o un disco.

---

## 8. Seguridad

### 8.1 Autenticacion

Identity con `AddIdentityCore` —no `AddIdentity`—, porque la Api es stateless y autentica
por JWT. `AddIdentity` registraria ademas el esquema de cookies y lo dejaria como esquema
por defecto, que es la causa clasica de que un endpoint protegido responda con un redirect
a `/Account/Login` en lugar de un 401.

**Access token** de vida corta (15 minutos) y **refresh token** de vida larga (7 dias) con
**rotacion**: cada uso emite uno nuevo e invalida el anterior.

**Deteccion de reuso.** Si llega un refresh token ya consumido, se revoca la cadena
completa de esa sesion. La logica: un token consumido solo puede reaparecer si alguien lo
copio, y en ese punto no hay forma de distinguir al legitimo del atacante, asi que se
cierra todo y ambos vuelven a autenticarse.

En la base se guarda el **hash SHA-256** del token, nunca el token. Una filtracion de la
tabla no entrega sesiones activas.

Cambiar la contraseña **revoca todas las sesiones**. Es el punto del ejercicio: una
contraseña se cambia casi siempre porque se sospecha que alguien mas entro, y si el refresh
token viejo siguiera renovandose el cambio no serviria de nada.

### 8.2 Subida de archivos

El unico archivo que un usuario puede subir es su avatar, y pasa por tres controles:

1. **Tamaño**, con tope configurable (2 MB por defecto).
2. **Formato por bytes magicos**, no por extension ni por `Content-Type`. Los dos ultimos
   los elige quien sube el archivo; los bytes, no. La extension del archivo guardado sale
   de la firma detectada.
3. **SVG excluido a proposito.** Es una imagen legitima, pero es XML y admite `<script>`
   adentro: servido desde el propio dominio seria un XSS con nuestro origen.

Los avatares **se guardan en la base**, en su propia tabla, y los sirve un endpoint. La
razon es el destino de despliegue: el filesystem del contenedor es efimero y un archivo
escrito en disco desaparece en cada version publicada y en cada arranque en frio, sin que
nadie vea un error. El costo esta acotado —2 MB por imagen, tabla aparte para que no viaje
en las consultas de usuario, lectura solo cuando alguien pide la foto— y `IAvatarStorage`
aisla la decision.

Al servir, el `Content-Type` sale de la fila (derivado de los bytes al subir, no de la
extension de la URL) y la respuesta lleva `X-Content-Type-Options: nosniff`. La validacion
por bytes al subir y la cabecera al servir son las dos mitades de la misma defensa: sin
`nosniff`, el navegador puede ignorar el `Content-Type` y decidir por su cuenta que un
archivo es HTML.

El identificador de la fila es el que va en la URL, y reemplazar la foto crea una fila
nueva: la URL anterior deja de resolver sola, lo que permite cachear estas respuestas de
forma indefinida.

### 8.3 XSS en el cliente

El frontend construye **todo** el DOM con `createElement` y `textContent`. Nunca usa
`innerHTML` interpolado. No es preferencia de estilo: el sitio renderiza texto escrito por
otros usuarios —reseñas, comentarios, biografias— y concatenar eso en `innerHTML` es XSS
almacenado servido a todos los visitantes.

`avatarUrl` se valida como URL absoluta http/https por la misma razon: un `javascript:`
guardado ahi se renderiza en el perfil publico.

### 8.4 Limite de peticiones

Rate limiting por politica, particionado por usuario autenticado cuando lo hay y por IP
cuando no. `UseRateLimiter` va **despues** de la autenticacion, para que pueda particionar
por usuario y no castigar a todos los que comparten una IP.

---

## 9. Rendimiento

Las tres tecnicas que sostienen el sistema:

**Subconsultas correlacionadas en lugar de N+1.** Contadores de votos, cantidad de
comentarios, promedio de puntajes: todo se calcula dentro de la misma proyeccion, en un
solo viaje.

**Consultas acotadas antes de proyectar.** El orden y el `Take` se aplican antes del
`Select`, tanto porque es lo correcto como porque EF no sabe ordenar sobre un record
posicional ya proyectado.

**Paginacion por cursor** donde el orden crece por arriba (seccion 6.3).

El home merece una nota aparte: son tres consultas `GROUP BY` acotadas que se combinan en
memoria, y cada seccion decide primero **que** mostrar y despues pide los datos completos
de esos elementos. Al reves —traer todo y despues elegir— la primera consulta crece con el
catalogo entero.

---

## 10. Estrategia de pruebas

### 10.1 Dos suites con proposito distinto

| Suite | Contra que corre | Que verifica |
|-------|------------------|--------------|
| `MusicReviews.UnitTests` | Nada externo | Logica pura: agrupacion de canciones, parseo de RSS, firmas de imagen, traduccion de URLs de base de datos |
| `MusicReviews.IntegrationTests` | PostgreSQL real en Docker (Testcontainers) | La Api completa de punta a punta |

### 10.2 Por que PostgreSQL real y no un proveedor en memoria

Buena parte de lo que hay que verificar **vive en la base y no existe en InMemory**: los
indices unicos que sostienen "una reseña por album" y "un voto por objetivo", los CHECK del
puntaje, el borrado en cascada, los indices parciales y la traduccion real de las
proyecciones a SQL. Un test contra InMemory pasaria con un modelo que en produccion falla,
que es la peor clase de test verde.

### 10.3 Dobles de prueba

MusicBrainz, los feeds de noticias y el almacenamiento de avatares se reemplazan por
implementaciones falsas. No es por velocidad: es porque una suite que depende de un
servicio externo se pone lenta y da fallos rojos cada vez que ese servicio tiene un mal
dia, y un test que falla por motivos ajenos deja de leerse.

### 10.4 Integracion continua

Cada push y cada pull request compilan con `-warnaserror`, corren la suite unitaria,
corren la de integracion en un job separado y compilan la imagen de contenedor. El orden
importa: un error de compilacion o un test unitario roto se ve en un minuto, no despues de
esperar a que arranque PostgreSQL.

---

## 11. Despliegue y operacion

### 11.1 Forma

Contenedor unico en Google Cloud Run + PostgreSQL administrado en Neon. Los pasos
concretos estan en [la guia de despliegue](despliegue-cloud-run.md). El `Dockerfile` es multi-etapa: compila con el
SDK y ejecuta sobre la imagen de runtime de Alpine, como usuario sin privilegios, sin
compilador ni codigo fuente adentro.

### 11.2 Adaptaciones al entorno

Dos convenciones de las plataformas de hosting no coinciden con las de ASP.NET y se
traducen al arrancar:

- **`PORT`**: la plataforma elige el puerto. El contenedor tiene que escuchar ahi y en
  `0.0.0.0`. En `localhost` el balanceador no lo alcanza y el despliegue queda marcado como
  no saludable sin un solo error en el log.
- **`DATABASE_URL`**: llega como URI (`postgres://usuario:clave@host/base`), formato que
  Npgsql no acepta. `DatabaseUrl` la traduce, decodificando usuario y contraseña —vienen
  percent-encoded, y las claves generadas al azar llevan `@` y `/` con frecuencia— y
  entrecomillando los valores que contengan el separador de la cadena.

Detras del proxy, `UseForwardedHeaders` va primero en el pipeline: sin eso, el limitador de
peticiones ve una sola IP para todo el trafico y la redireccion a https entra en bucle.

### 11.3 El contenedor no escribe en disco

Es la restriccion que ordena el resto del despliegue. En Cloud Run, y en el plan gratuito
de practicamente cualquier plataforma, el filesystem se recrea en cada version publicada y
en cada arranque en frio.

Lo unico que un usuario escribe son los avatares, y por eso viven en la base (seccion 8.2).
Con eso, el contenedor es completamente sin estado: se puede matar y recrear en cualquier
momento sin perder nada.

### 11.4 Contenido de demostracion

Una instancia recien desplegada arranca con el catalogo vacio, y el home es un feed: sin
albumes ni reseñas no muestra nada, y quien entra por primera vez no ve una aplicacion
vacia, ve una aplicacion rota. `DemoSeeder` siembra catalogo, cuentas, reseñas, hilos y
votos la primera vez que arranca contra una base vacia.

Corre en segundo plano y no como paso del arranque: sembrar veinte albumes son unas
cuarenta peticiones a MusicBrainz a una por segundo, y bloquear el arranque durante ese
minuto haria que la plataforma diera el despliegue por fallido y reiniciara el contenedor,
que volveria a sembrar desde cero.

En Cloud Run especificamente la siembra se ejecuta **desde la maquina local** contra la
base remota, porque Cloud Run estrangula la CPU del contenedor cuando no esta atendiendo
una peticion y un trabajo de fondo de un minuto nunca terminaria. Ver
[la guia de despliegue](despliegue-cloud-run.md).

### 11.5 Observabilidad

Serilog a consola y a archivo rotativo en formato JSON compacto, con log de peticiones
HTTP. `GET /api/health` reporta el estado de la conexion a PostgreSQL y es lo que consulta
la plataforma. Los errores no manejados salen como `ProblemDetails` con `traceId`, que es
el que permite cruzar una respuesta con su linea de log.

---

## 12. Alternativas descartadas

| Decision | Alternativa | Por que se descarto |
|----------|-------------|---------------------|
| MusicBrainz | Spotify Web API | Desde febrero de 2026 las aplicaciones en modo desarrollo estan limitadas a cinco usuarios y exigen que el dueño tenga Premium. Inviable para un sistema publico. |
| PostgreSQL | SQL Server / SQLite | Indices parciales, tipos nativos de fecha con zona y disponibilidad real en planes gratuitos. SQLite no sostiene la concurrencia ni los indices que el modelo usa. |
| Controllers | Minimal APIs | Con filtros de validacion, politicas de autorizacion y agrupacion por recurso, los controllers dicen mas con menos ceremonia a esta escala. |
| Servicios por agregado | CQRS + MediatR | Indireccion sin problema que resolver en un dominio de este tamaño. |
| `Result<T>` | Excepciones para errores de negocio | Una excepcion para un caso previsible esconde el flujo y cuesta caro. |
| Un voto polimorfico | `ReviewLike` + `CommentLike` | Duplicaria tabla, servicio y consulta de conteo para modelar lo mismo dos veces. |
| Avatares en la base | Archivos en disco / bucket de objetos | El disco es efimero en el destino de despliegue y las fotos desaparecerian sin error visible. Un bucket resuelve mejor a otra escala, a cambio de un servicio mas que administrar y credenciales que rotar. |
| Actividad derivada | Tabla de actividad | Copia denormalizada con escritura extra en cada accion y riesgo permanente de desincronizacion. |
| Frontend sin framework | Angular / React desde el principio | El backend es API-first: el cliente se reemplaza sin tocar el servidor. Escribir a mano el enrutado, el scroll infinito y el autocompletado muestra el mecanismo. |
| Gestion central de paquetes | Versiones por proyecto | Imposibilita que dos proyectos queden en versiones distintas del mismo paquete. |
| Sin `EFCore.NamingConventions` | Tablas en `snake_case` | EF genera identificadores entre comillas y funciona igual; es una dependencia menos que versionar. |

---

## 13. Limitaciones conocidas

**Una sola instancia.** Las migraciones se aplican desde el proceso de la aplicacion, lo
que con varias instancias haria que dos arranques simultaneos compitieran por el mismo
lock. Hay un interruptor (`Database:MigrateOnStartup`) para el dia en que eso cambie.

**El limite de MusicBrainz es global al proceso.** Con mas de una instancia, cada una
tendria su propia cola y entre todas superarian la peticion por segundo. Escalar
horizontalmente exige mover ese control a un recurso compartido.

**Los avatares viven en la base.** Funciona con este volumen de datos y con el tope de
2 MB por imagen; a partir de cierto tamanio corresponde un bucket de objetos. La interfaz
`IAvatarStorage` esta puesta justamente para que ese cambio no toque nada mas.

**La popularidad es local.** El orden del desplegable pondera actividad dentro de la
aplicacion, asi que con poco uso queda dominado por lo poco que hay. MusicBrainz no expone
metricas de popularidad; lo mas cercano es la cantidad de ediciones de un disco, que ya se
usa en la busqueda completa.

**El perfil privado no esta implementado.** Esta previsto en el roadmap: seguimiento con
estado pendiente/aceptado y filtrado en perfil, actividad, feed y home.
