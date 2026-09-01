#!/usr/bin/env bash
# Smoke test de la API. Verifica auth y catalogo de punta a punta.
#
# Uso (la API tiene que estar corriendo en OTRA terminal):
#   dotnet run --project src/MusicReviews.Api      # terminal 1
#   ./scripts/smoke-test.sh                        # terminal 2
#
# Variables opcionales:
#   BASE_URL   por defecto http://localhost:5080

set -uo pipefail

BASE_URL="${BASE_URL:-http://localhost:5080}"

PASS=0
FAIL=0

# ---------------------------------------------------------------- helpers

# Extrae el valor de una clave string de un JSON plano, sin depender de jq
# (que no viene con Git Bash).
json_str() {
    grep -o "\"$1\"[[:space:]]*:[[:space:]]*\"[^\"]*\"" \
        | head -n 1 \
        | sed 's/.*:[[:space:]]*"\(.*\)"/\1/'
}

json_num() {
    grep -o "\"$1\"[[:space:]]*:[[:space:]]*[0-9.]*" \
        | head -n 1 \
        | sed 's/.*:[[:space:]]*//'
}

ok()   { PASS=$((PASS + 1)); printf '  \033[32mOK\033[0m   %s\n' "$1"; }
bad()  { FAIL=$((FAIL + 1)); printf '  \033[31mFAIL\033[0m %s\n' "$1"; }
step() { printf '\n\033[1m%s\033[0m\n' "$1"; }

# Imprime el status HTTP y deja el cuerpo en $BODY.
call() {
    local method="$1" path="$2" data="${3:-}" auth="${4:-}"
    local args=(-s -w '\n%{http_code}' -X "$method" "$BASE_URL$path")

    [ -n "$data" ] && args+=(-H "Content-Type: application/json" -d "$data")
    [ -n "$auth" ] && args+=(-H "Authorization: Bearer $auth")

    local response
    response=$(curl "${args[@]}")

    STATUS="${response##*$'\n'}"
    BODY="${response%$'\n'*}"
}

expect() {
    local expected="$1" label="$2"
    if [ "$STATUS" = "$expected" ]; then
        ok "$label ($STATUS)"
    else
        bad "$label -> esperaba $expected, recibi $STATUS"
        printf '       %s\n' "$(echo "$BODY" | head -c 300)"
    fi
}

# ---------------------------------------------------------------- health

step "Health"

call GET /api/health
expect 200 "GET /api/health"

if ! echo "$BODY" | grep -q '"database":"up"'; then
    bad "La base no responde. Levanta Postgres: docker compose up -d"
    echo
    exit 1
fi
ok "Postgres conectado"

# En Windows, 'dotnet build' con la API corriendo falla por DLL bloqueados y deja
# en ejecucion el binario viejo. Sin este chequeo, todas las rutas nuevas dan 404
# y parece un fallo del codigo. Un 404 de ruta inexistente no trae "code"; el 404
# de la aplicacion si.
call GET /api/reviews/0/comments
if ! echo "$BODY" | grep -q '"code":'; then
    bad "La API en ejecucion no conoce /api/reviews/{id}/comments: es un binario viejo."
    printf '       Para el proceso de la API (Ctrl+C), corre "dotnet build" y volve a levantarla.\n'
    printf '       En Windows el build falla en silencio si la API esta corriendo: los DLL quedan bloqueados.\n\n'
    exit 1
fi
ok "La API en ejecucion esta al dia"

# ---------------------------------------------------------------- auth

step "Auth"

# Usuario nuevo en cada corrida: evita el 409 de "ya existe".
SUFFIX=$(date +%s)
USERNAME="smoke$SUFFIX"
EMAIL="smoke$SUFFIX@test.local"
PASSWORD="Smoke1234"

call POST /api/auth/register \
    "{\"userName\":\"$USERNAME\",\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}"
expect 200 "POST /api/auth/register"

ACCESS=$(echo "$BODY" | json_str accessToken)
REFRESH=$(echo "$BODY" | json_str refreshToken)

if [ -z "$ACCESS" ] || [ -z "$REFRESH" ]; then
    bad "No se pudieron leer los tokens de la respuesta"
    echo "$BODY" | head -c 400
    echo
    exit 1
fi
ok "Tokens emitidos"

call GET /api/auth/me "" "$ACCESS"
expect 200 "GET /api/auth/me con bearer valido"
echo "$BODY" | grep -q "\"userName\":\"$USERNAME\"" \
    && ok "El token identifica al usuario correcto" \
    || bad "El token no devuelve el usuario esperado"

call GET /api/auth/me
expect 401 "GET /api/auth/me sin bearer"

call POST /api/auth/login "{\"userNameOrEmail\":\"$EMAIL\",\"password\":\"incorrecta\"}"
expect 401 "POST /api/auth/login con contrasenia incorrecta"

call POST /api/auth/login "{\"userNameOrEmail\":\"$EMAIL\",\"password\":\"$PASSWORD\"}"
expect 200 "POST /api/auth/login por email"

call POST /api/auth/refresh "{\"refreshToken\":\"$REFRESH\"}"
expect 200 "POST /api/auth/refresh (rotacion)"

ROTATED=$(echo "$BODY" | json_str refreshToken)
[ -n "$ROTATED" ] && [ "$ROTATED" != "$REFRESH" ] \
    && ok "El refresh token rota (el nuevo es distinto)" \
    || bad "El refresh token no roto"

# Reusar el token ya revocado tiene que fallar y revocar la familia entera.
call POST /api/auth/refresh "{\"refreshToken\":\"$REFRESH\"}"
expect 401 "POST /api/auth/refresh reusando el token viejo (deteccion de reuso)"

# Y el token rotado tambien quedo revocado por la deteccion de reuso.
call POST /api/auth/refresh "{\"refreshToken\":\"$ROTATED\"}"
expect 401 "El token rotado tambien quedo revocado (familia cortada)"

call POST /api/auth/register \
    "{\"userName\":\"x\",\"email\":\"no-es-un-email\",\"password\":\"corta\"}"
expect 400 "POST /api/auth/register con datos invalidos"

# ---------------------------------------------------------------- catalogo

step "Catalogo"

call GET "/api/catalog/artists?query=radiohead"
expect 200 "GET /api/catalog/artists?query=radiohead"

ARTIST_MBID=$(echo "$BODY" | json_str musicBrainzId)
if [ -z "$ARTIST_MBID" ]; then
    bad "La busqueda de artistas no devolvio resultados"
    echo "$BODY" | head -c 400
    echo
else
    ok "Artista encontrado: $ARTIST_MBID"

    call GET "/api/catalog/artists/$ARTIST_MBID"
    expect 200 "GET /api/catalog/artists/{mbid} (primera vez: cachea)"

    START=$(date +%s%N)
    call GET "/api/catalog/artists/$ARTIST_MBID"
    ELAPSED=$(( ($(date +%s%N) - START) / 1000000 ))
    expect 200 "GET /api/catalog/artists/{mbid} (segunda vez: desde Postgres, ${ELAPSED}ms)"

    call GET "/api/catalog/artists/$ARTIST_MBID/albums?pageSize=5"
    expect 200 "GET /api/catalog/artists/{mbid}/albums"
fi

call GET "/api/catalog/albums?query=ok%20computer&pageSize=5"
expect 200 "GET /api/catalog/albums?query=ok+computer"

ALBUM_MBID=$(echo "$BODY" | json_str musicBrainzId)
if [ -z "$ALBUM_MBID" ]; then
    bad "La busqueda de albumes no devolvio resultados"
else
    ok "Album encontrado: $ALBUM_MBID"

    call GET "/api/catalog/albums/$ALBUM_MBID"
    expect 200 "GET /api/catalog/albums/{mbid} (primera vez: cachea album + artista + portada)"

    COVER=$(echo "$BODY" | json_str coverArtUrl)
    [ -n "$COVER" ] \
        && ok "Portada resuelta: $COVER" \
        || printf '  \033[33mSKIP\033[0m Este album no tiene portada en Cover Art Archive\n'

    # Se miden dos consultas repetidas, no una: un pico aislado (arranque en frio de
    # EF, primera conexion del pool, GC) no es lo mismo que una cache que no cachea.
    # El umbral es 900ms porque el intervalo minimo entre llamadas a MusicBrainz es
    # 1100ms: cualquier respuesta por debajo de eso no pudo haber salido a la API.
    BEST=999999
    for attempt in 2 3; do
        START=$(date +%s%N)
        call GET "/api/catalog/albums/$ALBUM_MBID"
        ELAPSED=$(( ($(date +%s%N) - START) / 1000000 ))
        expect 200 "GET /api/catalog/albums/{mbid} (consulta $attempt: ${ELAPSED}ms)"
        [ "$ELAPSED" -lt "$BEST" ] && BEST=$ELAPSED
    done

    if [ "$BEST" -lt 900 ]; then
        ok "Las consultas repetidas se sirven de Postgres (mejor: ${BEST}ms)"
    else
        bad "Ninguna consulta repetida bajo de 900ms (mejor: ${BEST}ms): la cache no esta sirviendo"
        printf '       Busca "MusicBrainz" en el log de la API: si aparece una llamada por cada\n'
        printf '       consulta, el problema es la comparacion de CachedAt en GetOrCacheAlbumAsync.\n'
    fi
fi

call GET "/api/catalog/artists?query="
expect 400 "GET /api/catalog/artists con query vacia"

call GET "/api/catalog/albums/00000000-0000-0000-0000-000000000000"
expect 404 "GET /api/catalog/albums con un mbid inexistente"

# ---------------------------------------------------------------- reviews

step "Reviews"

if [ -z "${ALBUM_MBID:-}" ]; then
    bad "Sin album del catalogo no se pueden probar las reviews"
else
    # Segundo usuario, para verificar que un tercero no puede editar ni borrar.
    OTHER_USER="other$SUFFIX"
    call POST /api/auth/register \
        "{\"userName\":\"$OTHER_USER\",\"email\":\"$OTHER_USER@test.local\",\"password\":\"$PASSWORD\"}"
    OTHER_ACCESS=$(echo "$BODY" | json_str accessToken)

    call POST /api/reviews \
        "{\"albumMusicBrainzId\":\"$ALBUM_MBID\",\"score\":87,\"text\":\"Disco de prueba del smoke test.\"}"
    expect 401 "POST /api/reviews sin autenticar"

    call POST /api/reviews \
        "{\"albumMusicBrainzId\":\"$ALBUM_MBID\",\"score\":150,\"text\":\"Puntaje fuera de rango.\"}" \
        "$ACCESS"
    expect 400 "POST /api/reviews con score fuera de 0-100"

    call POST /api/reviews \
        "{\"albumMusicBrainzId\":\"$ALBUM_MBID\",\"score\":87,\"text\":\"Disco de prueba del smoke test.\"}" \
        "$ACCESS"
    expect 201 "POST /api/reviews"

    # El DTO trae "id" como primer campo, asi que el primer match es el de la review.
    REVIEW_ID=$(echo "$BODY" | grep -o '"id":[0-9]*' | head -n 1 | sed 's/.*://')

    if [ -z "$REVIEW_ID" ]; then
        bad "No se pudo leer el id de la review creada"
        echo "$BODY" | head -c 300
        echo
    else
        ok "Review creada: id $REVIEW_ID"

        call POST /api/reviews \
            "{\"albumMusicBrainzId\":\"$ALBUM_MBID\",\"score\":50,\"text\":\"Segunda review del mismo album.\"}" \
            "$ACCESS"
        expect 409 "POST /api/reviews duplicada (una por usuario y album)"

        call GET "/api/reviews/$REVIEW_ID"
        expect 200 "GET /api/reviews/{id}"

        echo "$BODY" | grep -q '"likeCount":0' \
            && ok "Los contadores vienen en la misma respuesta" \
            || bad "Faltan los contadores en el DTO"

        call GET "/api/albums/$ALBUM_MBID/reviews"
        expect 200 "GET /api/albums/{mbid}/reviews"
        echo "$BODY" | grep -q "\"id\":$REVIEW_ID" \
            && ok "La review aparece en el listado del album" \
            || bad "La review no aparece en el listado del album"

        call GET "/api/users/$USERNAME/reviews"
        expect 200 "GET /api/users/{userName}/reviews"

        call GET "/api/users/no-existe-$SUFFIX/reviews"
        expect 404 "GET /api/users/{userName}/reviews con usuario inexistente"

        call PUT "/api/reviews/$REVIEW_ID" \
            "{\"score\":92,\"text\":\"Editada por el smoke test.\"}" "$OTHER_ACCESS"
        expect 403 "PUT /api/reviews/{id} por un usuario que no es el autor"

        call PUT "/api/reviews/$REVIEW_ID" \
            "{\"score\":92,\"text\":\"Editada por el smoke test.\"}" "$ACCESS"
        expect 200 "PUT /api/reviews/{id} por el autor"
        echo "$BODY" | grep -q '"score":92' \
            && ok "El puntaje se actualizo" \
            || bad "El puntaje no se actualizo"
        echo "$BODY" | grep -q '"updatedAt":null' \
            && bad "updatedAt sigue en null despues de editar" \
            || ok "updatedAt quedo seteado"

        call DELETE "/api/reviews/$REVIEW_ID" "" "$OTHER_ACCESS"
        expect 403 "DELETE /api/reviews/{id} por un usuario que no es el autor"

        call DELETE "/api/reviews/$REVIEW_ID" "" "$ACCESS"
        expect 204 "DELETE /api/reviews/{id} por el autor"

        call GET "/api/reviews/$REVIEW_ID"
        expect 404 "GET /api/reviews/{id} despues de borrarla"
    fi
fi

# ---------------------------------------------------------------- comments

step "Comments"

if [ -z "${ALBUM_MBID:-}" ] || [ -z "${OTHER_ACCESS:-}" ]; then
    bad "Sin album y segundo usuario no se pueden probar los comentarios"
else
    # Review propia del segundo usuario: la del primero se borro al final del
    # bloque anterior, y el unico (UserId, AlbumId) permite una por usuario.
    call POST /api/reviews \
        "{\"albumMusicBrainzId\":\"$ALBUM_MBID\",\"score\":73,\"text\":\"Review para probar comentarios.\"}" \
        "$OTHER_ACCESS"
    expect 201 "POST /api/reviews (review de soporte para el hilo)"
    THREAD_REVIEW=$(echo "$BODY" | grep -o '"id":[0-9]*' | head -n 1 | sed 's/.*://')

    call POST "/api/reviews/$THREAD_REVIEW/comments" '{"text":"Sin autenticar."}'
    expect 401 "POST /api/reviews/{id}/comments sin autenticar"

    call POST "/api/reviews/$THREAD_REVIEW/comments" '{"text":""}' "$ACCESS"
    expect 400 "POST /api/reviews/{id}/comments con texto vacio"

    call POST "/api/reviews/$THREAD_REVIEW/comments" '{"text":"Comentario de primer nivel."}' "$ACCESS"
    expect 201 "POST /api/reviews/{id}/comments (primer nivel)"
    C1=$(echo "$BODY" | grep -o '"id":[0-9]*' | head -n 1 | sed 's/.*://')
    echo "$BODY" | grep -q '"depth":0' \
        && ok "El comentario raiz tiene depth 0" \
        || bad "El depth del comentario raiz no es 0"

    call POST "/api/reviews/$THREAD_REVIEW/comments" \
        "{\"text\":\"Respuesta al primero.\",\"parentCommentId\":$C1}" "$OTHER_ACCESS"
    expect 201 "POST respuesta anidada (nivel 2)"
    C2=$(echo "$BODY" | grep -o '"id":[0-9]*' | head -n 1 | sed 's/.*://')
    echo "$BODY" | grep -q '"depth":1' \
        && ok "La respuesta tiene depth 1" \
        || bad "El depth de la respuesta no es 1"

    call POST "/api/reviews/$THREAD_REVIEW/comments" \
        "{\"text\":\"Respuesta a la respuesta.\",\"parentCommentId\":$C2}" "$ACCESS"
    expect 201 "POST respuesta anidada (nivel 3)"
    C3=$(echo "$BODY" | grep -o '"id":[0-9]*' | head -n 1 | sed 's/.*://')

    call POST "/api/reviews/$THREAD_REVIEW/comments" \
        '{"text":"Respuesta a un padre inexistente.","parentCommentId":999999}' "$ACCESS"
    expect 404 "POST respuesta a un comentario inexistente"

    call GET "/api/reviews/$THREAD_REVIEW/comments"
    expect 200 "GET /api/reviews/{id}/comments (arbol completo)"

    echo "$BODY" | grep -q "^\[{\"id\":$C1," \
        && ok "El arbol tiene un solo nodo de primer nivel" \
        || bad "El primer nivel del arbol no es el esperado"

    echo "$BODY" | grep -q "\"parentCommentId\":$C1" \
        && ok "La respuesta cuelga del comentario correcto" \
        || bad "La respuesta no quedo anidada"

    echo "$BODY" | grep -q "\"parentCommentId\":$C2" \
        && ok "El tercer nivel quedo anidado bajo el segundo" \
        || bad "El tercer nivel no quedo anidado"

    call GET "/api/reviews/999999/comments"
    expect 404 "GET comentarios de una review inexistente"

    call PUT "/api/comments/$C1" '{"text":"Editado por otro usuario."}' "$OTHER_ACCESS"
    expect 403 "PUT /api/comments/{id} por quien no es el autor"

    call PUT "/api/comments/$C1" '{"text":"Editado por su autor."}' "$ACCESS"
    expect 200 "PUT /api/comments/{id} por el autor"

    call DELETE "/api/comments/$C1" "" "$OTHER_ACCESS"
    expect 403 "DELETE /api/comments/{id} por quien no es el autor"

    # El punto del borrado logico: el nodo se va pero el hilo no se rompe.
    call DELETE "/api/comments/$C1" "" "$ACCESS"
    expect 204 "DELETE /api/comments/{id} (borrado logico)"

    call GET "/api/reviews/$THREAD_REVIEW/comments"
    expect 200 "GET arbol despues del borrado logico"

    echo "$BODY" | grep -q '"isDeleted":true' \
        && ok "El nodo borrado sigue en el arbol" \
        || bad "El nodo borrado desaparecio del arbol"

    echo "$BODY" | grep -q "\"parentCommentId\":$C1" \
        && ok "Las respuestas al nodo borrado siguen colgadas de el" \
        || bad "Se rompio el hilo al borrar el nodo intermedio"

    echo "$BODY" | grep -q "\"parentCommentId\":$C2" \
        && ok "El hilo se mantiene hasta el tercer nivel" \
        || bad "Se perdio el tercer nivel"

    call POST "/api/reviews/$THREAD_REVIEW/comments" \
        "{\"text\":\"Respondiendo a algo borrado.\",\"parentCommentId\":$C1}" "$ACCESS"
    expect 400 "POST respuesta a un comentario borrado"

    call PUT "/api/comments/$C1" '{"text":"Editando algo borrado."}' "$ACCESS"
    expect 400 "PUT sobre un comentario borrado"

    # Anidar hasta chocar con el limite de producto (Comment.MaxDepth = 10).
    PARENT=$C3
    DEPTH_HIT=0
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        call POST "/api/reviews/$THREAD_REVIEW/comments" \
            "{\"text\":\"Anidando.\",\"parentCommentId\":$PARENT}" "$ACCESS"
        if [ "$STATUS" = "400" ]; then
            DEPTH_HIT=1
            break
        fi
        [ "$STATUS" = "201" ] || break
        PARENT=$(echo "$BODY" | grep -o '"id":[0-9]*' | head -n 1 | sed 's/.*://')
    done

    [ "$DEPTH_HIT" = "1" ] \
        && ok "El limite de profundidad corta el anidamiento con 400" \
        || bad "No se alcanzo el limite de profundidad (ultimo status: $STATUS)"
fi

# ---------------------------------------------------------------- likes

step "Likes"

if [ -z "${THREAD_REVIEW:-}" ]; then
    bad "Sin review de soporte no se pueden probar los votos"
else
    call POST "/api/reviews/$THREAD_REVIEW/likes" '{"isLike":true}'
    expect 401 "POST /api/reviews/{id}/likes sin autenticar"

    call POST "/api/reviews/$THREAD_REVIEW/likes" '{"isLike":true}' "$ACCESS"
    expect 200 "POST /api/reviews/{id}/likes (like)"
    echo "$BODY" | grep -q '"likeCount":1' \
        && ok "El like quedo registrado" \
        || bad "El contador de likes no subio"

    # Mismo voto: es un toggle, tiene que retirarlo.
    call POST "/api/reviews/$THREAD_REVIEW/likes" '{"isLike":true}' "$ACCESS"
    expect 200 "POST /api/reviews/{id}/likes de nuevo (toggle)"
    echo "$BODY" | grep -q '"likeCount":0' \
        && ok "El mismo voto repetido lo retira" \
        || bad "El toggle no retiro el voto"
    echo "$BODY" | grep -q '"currentUserVote":null' \
        && ok "currentUserVote vuelve a null" \
        || bad "currentUserVote no quedo en null"

    # Voto contrario: cambia, no duplica.
    call POST "/api/reviews/$THREAD_REVIEW/likes" '{"isLike":true}' "$ACCESS"
    call POST "/api/reviews/$THREAD_REVIEW/likes" '{"isLike":false}' "$ACCESS"
    expect 200 "POST /api/reviews/{id}/likes con el voto contrario"
    echo "$BODY" | grep -q '"likeCount":0' && echo "$BODY" | grep -q '"dislikeCount":1' \
        && ok "El voto contrario reemplaza al anterior, no lo duplica" \
        || bad "El cambio de voto no reemplazo al anterior"

    call POST "/api/reviews/$THREAD_REVIEW/likes" '{"isLike":true}' "$OTHER_ACCESS"
    expect 200 "POST /api/reviews/{id}/likes de un segundo usuario"
    echo "$BODY" | grep -q '"likeCount":1' && echo "$BODY" | grep -q '"dislikeCount":1' \
        && ok "Los votos de distintos usuarios se acumulan" \
        || bad "Los votos de distintos usuarios no se acumulan"

    call GET "/api/reviews/$THREAD_REVIEW"
    echo "$BODY" | grep -q '"likeCount":1' \
        && ok "El DTO de la review refleja los votos" \
        || bad "El DTO de la review no refleja los votos"

    call DELETE "/api/reviews/$THREAD_REVIEW/likes" "" "$ACCESS"
    expect 200 "DELETE /api/reviews/{id}/likes"
    echo "$BODY" | grep -q '"dislikeCount":0' \
        && ok "El voto se retiro" \
        || bad "El voto no se retiro"

    call DELETE "/api/reviews/$THREAD_REVIEW/likes" "" "$ACCESS"
    expect 200 "DELETE /api/reviews/{id}/likes repetido (idempotente)"

    call POST "/api/reviews/999999/likes" '{"isLike":true}' "$ACCESS"
    expect 404 "POST likes sobre una review inexistente"

    if [ -n "${C2:-}" ]; then
        call POST "/api/comments/$C2/likes" '{"isLike":true}' "$ACCESS"
        expect 200 "POST /api/comments/{id}/likes"
    fi

    if [ -n "${C1:-}" ]; then
        # C1 quedo borrado logicamente en el bloque anterior.
        call POST "/api/comments/$C1/likes" '{"isLike":true}' "$ACCESS"
        expect 400 "POST likes sobre un comentario borrado"
    fi
fi

# ---------------------------------------------------------------- users

step "Users"

call GET /api/users/me
expect 401 "GET /api/users/me sin autenticar"

call GET /api/users/me "" "$ACCESS"
expect 200 "GET /api/users/me"
echo "$BODY" | grep -q "\"userName\":\"$USERNAME\"" \
    && ok "El perfil corresponde al usuario del token" \
    || bad "El perfil no es el del usuario autenticado"

call PUT /api/users/me \
    '{"bio":"Escucho discos y escribo sobre eso.","avatarUrl":"https://example.com/avatar.png"}' \
    "$ACCESS"
expect 200 "PUT /api/users/me"
echo "$BODY" | grep -q '"bio":"Escucho discos' \
    && ok "La bio se guardo" \
    || bad "La bio no se guardo"

# Una URL con esquema javascript: terminaria renderizada en el perfil publico.
call PUT /api/users/me '{"bio":null,"avatarUrl":"javascript:alert(1)"}' "$ACCESS"
expect 400 "PUT /api/users/me con un avatarUrl que no es http(s)"

call GET "/api/users/$USERNAME"
expect 200 "GET /api/users/{userName} (perfil publico)"
echo "$BODY" | grep -q '"reviewCount"' \
    && ok "El perfil publico trae las estadisticas" \
    || bad "Faltan las estadisticas en el perfil publico"

call GET "/api/users/no-existe-$SUFFIX"
expect 404 "GET /api/users/{userName} inexistente"

if [ -n "${ARTIST_MBID:-}" ]; then
    call POST /api/users/me/favorites "{\"artistMusicBrainzId\":\"$ARTIST_MBID\"}" "$ACCESS"
    expect 200 "POST /api/users/me/favorites"

    call POST /api/users/me/favorites "{\"artistMusicBrainzId\":\"$ARTIST_MBID\"}" "$ACCESS"
    expect 200 "POST /api/users/me/favorites repetido (idempotente)"

    call GET "/api/users/$USERNAME/favorites"
    expect 200 "GET /api/users/{userName}/favorites"
    echo "$BODY" | grep -q "\"musicBrainzId\":\"$ARTIST_MBID\"" \
        && ok "El artista aparece en favoritos" \
        || bad "El artista no aparece en favoritos"

    call GET "/api/users/$USERNAME"
    echo "$BODY" | grep -q "\"musicBrainzId\":\"$ARTIST_MBID\"" \
        && ok "Los favoritos vienen dentro del perfil" \
        || bad "Los favoritos no vienen en el perfil"

    call DELETE "/api/users/me/favorites/$ARTIST_MBID" "" "$ACCESS"
    expect 204 "DELETE /api/users/me/favorites/{mbid}"

    call GET "/api/users/$USERNAME/favorites"
    echo "$BODY" | grep -q "$ARTIST_MBID" \
        && bad "El favorito sigue despues de borrarlo" \
        || ok "El favorito se quito"

    call POST /api/users/me/favorites '{"artistMusicBrainzId":"no-es-un-mbid"}' "$ACCESS"
    expect 400 "POST favoritos con un MBID invalido"
fi

# ---------------------------------------------------------------- resumen

step "Resumen"
printf '  %s pasaron, %s fallaron\n\n' "$PASS" "$FAIL"

[ "$FAIL" -eq 0 ] || exit 1
