import { activity, auth, catalog, comments, home, likes, news, notifications, reviews, search, session, users } from './api.js';
import { avatar, clear, cover, el, empty, errorBox, formatDate, icon, link, scoreClass, spinner } from './dom.js';

const main = document.getElementById('view');
const nav = document.getElementById('nav-session');

// ---------------------------------------------------------------- router

const routes = [
    [/^\/?$/, viewHome],
    [/^\/search$/, viewSearch],
    [/^\/login$/, viewLogin],
    [/^\/register$/, viewRegister],
    [/^\/me$/, viewMyProfile],
    [/^\/notifications$/, viewNotifications],
    [/^\/artist\/([0-9a-fA-F-]{36})$/, viewArtist],
    [/^\/album\/([0-9a-fA-F-]{36})$/, viewAlbum],
    [/^\/review\/(\d+)$/, viewReview],
    [/^\/user\/(.+)$/, viewProfile]
];

async function render() {
    // La query string va DESPUES del hash (#/?q=...), asi que forma parte de
    // location.hash y hay que separarla antes de comparar contra las rutas.
    // Sin esto, "#/?q=Thriller" no matchea "/" y todo termina en "no encontrado".
    const [path, queryString] = splitHash();
    const params = new URLSearchParams(queryString);

    // Todo lo que la vista anterior engancho fuera de su propio arbol —observadores,
    // timers, listeners en document— se desengancha aca. Sin esto, cada visita al home
    // deja uno mas vivo apuntando a nodos que ya no estan en el DOM.
    runCleanups();

    for (const [pattern, view] of routes) {
        const match = pattern.exec(path);

        if (match) {
            clear(main).append(spinner());

            try {
                const content = await view(...match.slice(1).map(decodeURIComponent), params);
                clear(main).append(content);
            } catch (error) {
                clear(main).append(errorBox(error, () => render()));
            }

            window.scrollTo(0, 0);
            return;
        }
    }

    clear(main).append(el('h1', {}, 'No encontrado'), empty('Esa dirección no existe.'));
}

function go(path) {
    location.hash = path;
}

/**
 * Cosas que hay que desenganchar cuando la vista se va: listeners en `document`,
 * intervalos, observadores. Todo lo que viva dentro del arbol de la vista se limpia
 * solo al reemplazar el contenido; esto es para lo que no.
 */
const cleanups = [];

function registerCleanup(fn) {
    cleanups.push(fn);
}

function runCleanups() {
    stopInfiniteScroll();

    while (cleanups.length > 0) {
        cleanups.pop()();
    }
}

/** Separa el hash en ruta y query string: "#/album/x?y=1" -> ["/album/x", "y=1"]. */
function splitHash() {
    const raw = location.hash.replace(/^#/, '');
    const index = raw.indexOf('?');

    if (index === -1) {
        return [raw || '/', ''];
    }

    return [raw.slice(0, index) || '/', raw.slice(index + 1)];
}

// ---------------------------------------------------------------- navegacion

function renderNav() {
    clear(nav);
    stopUnreadPolling();

    if (session.isAuthenticated) {
        const user = session.user;

        nav.append(
            notificationBell(),
            el('a', {
                href: `#/user/${encodeURIComponent(user.userName)}`,
                class: 'nav-user',
                title: user.userName
            }, avatar(user, 28), el('span', { class: 'nav-user__name' }, user.userName)),
            el('a', { href: '#/me', class: 'btn btn--ghost' }, 'Mi perfil'),
            el('button', {
                class: 'btn btn--ghost',
                onClick: async () => {
                    await auth.logout();
                    go('/');
                }
            }, 'Salir')
        );
    } else {
        nav.append(
            el('a', { href: '#/login', class: 'btn btn--ghost' }, 'Entrar'),
            el('a', { href: '#/register', class: 'btn' }, 'Crear cuenta')
        );
    }
}

// ---------------------------------------------------------------- notificaciones

let unreadTimer = null;
let unreadRefresh = null;

/**
 * La barra se vuelve a dibujar en cada cambio de sesion. Sin desenganchar lo anterior,
 * cada redibujado dejaria un intervalo y dos listeners vivos apuntando a una campana que
 * ya no esta en el DOM: el contador se pediria N veces por minuto y creceria con el uso.
 */
function stopUnreadPolling() {
    if (unreadTimer !== null) {
        clearInterval(unreadTimer);
        unreadTimer = null;
    }

    if (unreadRefresh !== null) {
        document.removeEventListener('visibilitychange', unreadRefresh);
        window.removeEventListener('notifications-changed', unreadRefresh);
        unreadRefresh = null;
    }
}

/**
 * Campana con el contador de notificaciones sin leer.
 *
 * El contador se refresca con un intervalo largo y solo con la pestaña visible.
 * No hay push: para un proyecto de este tamaño, un COUNT sobre un indice parcial cada
 * minuto es mas barato —de operar y de entender— que sostener una conexion abierta por
 * usuario. Si en algun momento hace falta inmediatez, el lugar donde cambiarlo es este.
 */
function notificationBell() {
    const badge = el('span', { class: 'badge' });

    // La campana va sin texto al lado, asi que el nombre accesible lo lleva el icono
    // y el title da el mismo dato al que solo usa el mouse.
    const anchor = el('a', {
        href: '#/notifications',
        class: 'btn btn--ghost bell',
        title: 'Notificaciones'
    }, icon('bell', { size: 20, label: 'Notificaciones' }), badge);

    const paint = count => {
        badge.textContent = count > 99 ? '99+' : String(count);
        badge.classList.toggle('is-hidden', count === 0);
    };

    const refresh = async () => {
        if (!session.isAuthenticated || document.hidden) {
            return;
        }

        try {
            paint((await notifications.unreadCount()).unread);
        } catch {
            // Un fallo del contador no tiene por que ensuciar la pantalla:
            // la campana simplemente se queda como estaba.
        }
    };

    paint(0);
    refresh();

    unreadRefresh = refresh;
    unreadTimer = setInterval(refresh, 60_000);

    // visibilitychange se dispara al volver a la pestaña: es el momento en que el
    // usuario realmente mira, y compensa lo que no se pidio mientras no estaba.
    document.addEventListener('visibilitychange', refresh);
    window.addEventListener('notifications-changed', refresh);

    return anchor;
}

async function viewNotifications() {
    if (!session.isAuthenticated) {
        go('/login');
        return el('div');
    }

    const list = el('div', {});
    const moreBox = el('div', { class: 'row' });

    const markAllButton = el('button', {
        class: 'btn btn--ghost',
        onClick: async () => {
            await notifications.markAllRead();
            window.dispatchEvent(new CustomEvent('notifications-changed'));
            await reload();
        }
    }, 'Marcar todo como leído');

    const appendPage = page => {
        if (page.items.length) {
            list.append(el('ul', { class: 'notification-list' },
                page.items.map(notificationRow)));
        }

        clear(moreBox);

        if (page.hasMore) {
            moreBox.append(el('button', {
                class: 'btn btn--ghost',
                onClick: async event => {
                    event.target.disabled = true;
                    appendPage(await notifications.list(page.nextCursor));
                }
            }, 'Ver más'));
        }
    };

    const reload = async () => {
        clear(list).append(spinner());
        const page = await notifications.list();
        clear(list);

        if (!page.items.length) {
            list.append(empty('No tenés notificaciones todavía.'));
            clear(moreBox);
            return;
        }

        appendPage(page);
    };

    await reload();

    return el('section', {},
        el('div', { class: 'row row--between' },
            el('h1', {}, 'Notificaciones'),
            markAllButton),
        list,
        moreBox);
}

function notificationRow(item) {
    const actor = item.actor?.userName ?? 'Alguien';
    const { types } = notifications;

    const [text, href] = (() => {
        switch (item.type) {
            case types.reviewCommented:
                return ['comentó tu reseña', `#/review/${item.reviewId}`];
            case types.commentReplied:
                return ['respondió tu comentario', `#/review/${item.reviewId}`];
            case types.reviewVoted:
                return [
                    item.isLike ? 'le gustó tu reseña' : 'no le gustó tu reseña',
                    `#/review/${item.reviewId}`
                ];
            case types.commentVoted:
                return [
                    item.isLike ? 'le gustó tu comentario' : 'no le gustó tu comentario',
                    null
                ];
            case types.newFollower:
                return [
                    'empezó a seguirte',
                    item.actor ? `#/user/${encodeURIComponent(item.actor.userName)}` : null
                ];
            default:
                return ['tiene novedades para vos', null];
        }
    })();

    const open = async () => {
        if (!item.isRead) {
            await notifications.markRead(item.id);
            window.dispatchEvent(new CustomEvent('notifications-changed'));
        }

        if (href) {
            go(href.replace(/^#/, ''));
        }
    };

    return el('li', {
        class: item.isRead ? 'notification' : 'notification is-unread',
        onClick: open
    },
        item.actor ? avatar(item.actor, 32) : el('span', { class: 'avatar avatar--empty' }),
        el('div', { class: 'notification__body' },
            el('div', {},
                el('strong', {}, actor),
                ' ',
                el('span', {}, text),
                item.album
                    ? el('span', { class: 'muted' }, ` - ${item.album.title}`)
                    : null),
            el('div', { class: 'muted small' }, formatDate(item.createdAt))),
        item.album ? cover(item.album.coverArtUrl, 'cover cover--tiny') : null);
}

// ---------------------------------------------------------------- home

let infiniteObserver = null;

function stopInfiniteScroll() {
    if (infiniteObserver !== null) {
        infiniteObserver.disconnect();
        infiniteObserver = null;
    }
}

/**
 * Portada de calle: la tapa es el contenido, no un adorno al costado del titulo.
 * El puntaje va encima de la imagen para que la grilla se lea de un vistazo sin
 * tener que bajar a los textos.
 */
function homeAlbumCard(album) {
    // El recuadro con aspect-ratio ya lo pone .tile__art; aca solo va la imagen, que
    // se borra sola si la portada no existe.
    const tapa = album.coverArtUrl
        ? el('img', { class: 'tile__cover', src: album.coverArtUrl, alt: '', loading: 'lazy' })
        : null;

    if (tapa) {
        tapa.addEventListener('error', () => tapa.remove());
    }

    const badge = album.averageScore !== null
        ? el('span', { class: `tile__score ${scoreClass(album.averageScore)}` },
            album.averageScore.toFixed(0))
        : null;

    return el('li', { class: 'tile' },
        el('a', { href: `#/album/${album.musicBrainzId}`, class: 'tile__link' },
            el('div', { class: 'tile__art' }, tapa, badge),
            el('strong', { class: 'tile__title' }, album.title),
            el('span', { class: 'muted small' }, album.artistName),
            el('span', { class: 'muted small' },
                [
                    album.releaseDate?.slice(0, 4),
                    album.reviewCount > 0 ? `${album.reviewCount} reseña(s)` : 'sin reseñas'
                ].filter(Boolean).join(' - '))));
}

function albumGrid(albums) {
    return el('ul', { class: 'tile-grid' }, albums.map(homeAlbumCard));
}

/**
 * Home. Tres secciones, cada una pedida por separado y pintada cuando llega la suya,
 * y todas ocultas mientras no tengan contenido: un home con tres titulos vacios se ve
 * roto, aunque tecnicamente este bien.
 *
 * Los enlaces viejos con "#/?q=..." siguen funcionando y caen en el buscador.
 */
async function viewHome(params) {
    if ((params.get('q') ?? '').trim()) {
        return viewSearch(params);
    }

    const searchForm = searchBox();

    const popular = homeSection('Sonando ahora', 'Lo que más se reseña, comenta y vota este mes.');
    const following = homeSection('De quienes seguís', null);
    const explore = homeSection('Explorar el catálogo', 'Lo último que pasó por el catálogo.');
    const headlines = homeSection('Noticias', 'Titulares de medios de música. El enlace lleva a la nota original.');

    const container = el('section', {},
        searchForm,
        popular.node,
        following.node,
        headlines.node,
        explore.node);

    // Las tres salen juntas. Son consultas locales contra Postgres —ninguna sale a
    // MusicBrainz—, asi que lo que se gana no es tiempo de cola sino que una seccion
    // lenta no retenga a las otras dos.
    popular.fill(async body => {
        const page = await home.popular(1, 12);

        if (!page.items.length) {
            return false;
        }

        body.append(albumGrid(page.items));
        return true;
    });

    following.fill(async body => {
        if (!session.isAuthenticated) {
            return false;
        }

        const page = await activity.following(null, 15);

        if (!page.items.length) {
            return false;
        }

        body.append(el('ul', { class: 'activity-list' }, page.items.map(activityRow)));
        return true;
    });

    headlines.fill(async body => {
        const items = await news.latest(8);

        if (!items.length) {
            return false;
        }

        body.append(el('ul', { class: 'news-list' }, items.map(newsCard)));
        return true;
    });

    explore.fill(async body => {
        const first = await home.explore(1, 12);

        if (!first.items.length) {
            return false;
        }

        const grid = albumGrid(first.items);
        const sentinel = el('div', { class: 'sentinel' });

        body.append(grid, sentinel);
        startInfiniteScroll(sentinel, grid, first);

        return true;
    });

    return container;
}

/**
 * Arma una seccion del home que se completa sola.
 * <c>fill</c> recibe el cuerpo y devuelve si hubo contenido; si no lo hubo, la seccion
 * entera se oculta en vez de dejar un titulo colgado.
 */
function homeSection(title, subtitle) {
    const body = el('div', {});
    const node = el('div', { class: 'home-section' },
        el('h2', {}, title),
        subtitle ? el('p', { class: 'muted small' }, subtitle) : null,
        body);

    body.append(spinner());

    return {
        node,
        fill: async produce => {
            try {
                clear(body);

                if (!await produce(body)) {
                    node.classList.add('is-empty');
                }
            } catch (error) {
                // Una seccion que falla no puede llevarse el home entera: se queda con
                // su error y las otras siguen en pie.
                clear(body).append(errorBox(error));
            }
        }
    };
}

/**
 * Scroll infinito sobre la grilla de exploracion.
 *
 * Se usa IntersectionObserver y no un listener de scroll: el listener se dispara
 * decenas de veces por segundo y obliga a medir el layout en cada una, que es la receta
 * clasica del scroll que tironea. El observador avisa una sola vez, cuando el centinela
 * del final entra en pantalla.
 */
function startInfiniteScroll(sentinel, grid, firstPage) {
    let page = firstPage;
    let loading = false;

    stopInfiniteScroll();

    infiniteObserver = new IntersectionObserver(async entries => {
        if (!entries[0].isIntersecting || loading || !page.hasNext) {
            return;
        }

        loading = true;
        sentinel.append(spinner());

        try {
            page = await home.explore(page.page + 1, page.pageSize);
            grid.append(...page.items.map(homeAlbumCard));
        } catch (error) {
            clear(sentinel).append(errorBox(error));
            stopInfiniteScroll();
            return;
        } finally {
            loading = false;
        }

        clear(sentinel);

        if (!page.hasNext) {
            stopInfiniteScroll();
            sentinel.append(el('p', { class: 'muted small' }, 'Eso es todo por ahora.'));
        }
    }, {
        // Se dispara antes de llegar al fondo para que la pagina siguiente ya este
        // cargando cuando el usuario termina de ver la anterior.
        rootMargin: '400px'
    });

    infiniteObserver.observe(sentinel);
}

/**
 * Titular de una noticia.
 *
 * El enlace sale del sitio: `target="_blank"` con `rel="noopener noreferrer"`, que no es
 * decoracion —sin `noopener`, la pagina destino recibe una referencia a esta ventana por
 * `window.opener` y puede redirigirla—. El nombre del medio va siempre visible: el
 * extracto es contenido ajeno y sin la atribucion se lee como propio.
 */
function newsCard(item) {
    return el('li', { class: 'news' },
        el('a', {
            class: 'news__link',
            href: item.url,
            target: '_blank',
            rel: 'noopener noreferrer'
        },
            item.imageUrl
                ? el('img', { class: 'news__image', src: item.imageUrl, alt: '', loading: 'lazy' })
                : null,
            el('div', { class: 'news__body' },
                el('strong', {}, item.title),
                item.excerpt ? el('p', { class: 'muted small news__excerpt' }, item.excerpt) : null,
                el('div', { class: 'muted small' },
                    [item.sourceName, item.publishedAt ? formatDate(item.publishedAt) : null]
                        .filter(Boolean).join(' - ')))));
}

/** Una entrada del timeline de a quienes seguis. */
function activityRow(entry) {
    const { kinds } = activity;

    const verb = {
        [kinds.reviewPublished]: 'reseñó',
        [kinds.commentPublished]: 'comentó',
        [kinds.reviewVoted]: entry.isLike ? 'le gustó la reseña de' : 'no le gustó la reseña de',
        [kinds.commentVoted]: entry.isLike ? 'le gustó un comentario en' : 'no le gustó un comentario en'
    }[entry.kind] ?? 'hizo algo en';

    const target = entry.album
        ? link(`#/album/${entry.album.musicBrainzId}`, entry.album.title)
        : el('span', { class: 'muted' }, 'algo');

    return el('li', { class: 'activity' },
        cover(entry.album?.coverArtUrl),
        el('div', { class: 'activity__body' },
            el('div', {},
                avatar(entry.actor, 20),
                ' ',
                link(`#/user/${encodeURIComponent(entry.actor.userName)}`, entry.actor.userName),
                ` ${verb} `,
                target,
                entry.review ? el('span', { class: scoreClass(entry.review.score) }, entry.review.score) : null),
            entry.excerpt ? el('p', { class: 'muted small activity__excerpt' }, entry.excerpt) : null,
            el('div', { class: 'muted small' }, formatDate(entry.createdAt))),
        entry.review
            ? link(`#/review/${entry.review.id}`, 'Ver', { class: 'btn btn--ghost btn--small' })
            : null);
}

// ---------------------------------------------------------------- buscador

/**
 * Etiqueta legible del tipo de artista.
 *
 * MusicBrainz clasifica a los solistas como "Person" porque su modelo distingue
 * personas de grupos. Para quien lee la pantalla eso es ruido: en un sitio de musica,
 * un solista es un artista.
 */
function artistTypeLabel(type) {
    return {
        Person: 'Artista',
        Group: 'Banda',
        Orchestra: 'Orquesta',
        Choir: 'Coro',
        Character: 'Personaje',
        Other: 'Otro'
    }[type] ?? type ?? null;
}

/**
 * Campo de busqueda con desplegable de sugerencias, al estilo del de Spotify.
 *
 * Las sugerencias salen del catalogo LOCAL, no de MusicBrainz. No es una simplificacion:
 * MusicBrainz admite 1 request por segundo para toda la aplicacion, asi que un
 * desplegable que responde con cada tecla es imposible contra el. Consultando Postgres
 * responde en milisegundos y sin limite, y la busqueda completa —la que si va a
 * MusicBrainz— queda detras de Enter, que ademas siembra el indice local con lo que
 * encuentra. Ver IQuickSearchService.
 */
function searchBox(initialQuery = '') {
    const input = el('input', {
        type: 'search',
        class: 'input',
        placeholder: 'Buscar un artista, un álbum o una canción...',
        value: initialQuery,
        autocomplete: 'off',
        // El desplegable ya es una lista de opciones: sin esto, el navegador dibuja
        // encima su propio historial de busquedas y tapa los resultados.
        role: 'combobox',
        'aria-autocomplete': 'list',
        'aria-expanded': 'false'
    });

    const panel = el('div', { class: 'suggest', hidden: true });
    const box = el('div', { class: 'searchbox' }, input, panel);

    let items = [];
    let active = -1;
    let timer = null;
    // Cada respuesta lleva el numero de peticion que la origino. Sin esto, una consulta
    // lenta que vuelve tarde puede pisar el resultado de una mas nueva y el desplegable
    // termina mostrando sugerencias de lo que el usuario escribio hace tres teclas.
    let lastRequest = 0;

    const close = () => {
        panel.hidden = true;
        input.setAttribute('aria-expanded', 'false');
        active = -1;
    };

    const submit = () => {
        const term = input.value.trim();

        if (term) {
            close();
            go(`/search?q=${encodeURIComponent(term)}`);
        }
    };

    const paint = () => {
        clear(panel);

        items.forEach((item, index) => {
            panel.append(suggestionRow(item, index === active, close));
        });

        // Ultima fila, siempre: es la que lleva a la busqueda completa contra
        // MusicBrainz, que es donde estan las canciones y todo lo que el catalogo local
        // todavia no vio.
        panel.append(el('button', {
            type: 'button',
            class: `suggest__row suggest__row--all${active === items.length ? ' is-active' : ''}`,
            onClick: submit
        }, el('span', {}, `Buscar «${input.value.trim()}» en MusicBrainz`)));

        panel.hidden = false;
        input.setAttribute('aria-expanded', 'true');
    };

    const query = async () => {
        const term = input.value.trim();

        if (!term) {
            items = [];
            close();
            return;
        }

        const request = ++lastRequest;

        try {
            const result = await search.quick(term, 8);

            if (request !== lastRequest) {
                return;
            }

            items = result;
        } catch {
            // Que fallen las sugerencias no puede impedir buscar: se muestra igual la
            // fila que lleva a la busqueda completa.
            items = [];
        }

        active = -1;
        paint();
    };

    input.addEventListener('input', () => {
        clearTimeout(timer);
        // Corto a proposito: la consulta es local y barata, y lo que se busca es que el
        // desplegable acompanie el tecleo en vez de ir dos palabras atras.
        timer = setTimeout(query, 120);
    });

    input.addEventListener('focus', () => {
        if (input.value.trim()) {
            query();
        }
    });

    input.addEventListener('keydown', event => {
        // El total incluye la fila de "buscar en MusicBrainz".
        const total = items.length + 1;

        if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
            event.preventDefault();

            if (panel.hidden) {
                query();
                return;
            }

            active = event.key === 'ArrowDown'
                ? (active + 1) % total
                : (active - 1 + total) % total;

            paint();
            return;
        }

        if (event.key === 'Escape') {
            close();
            return;
        }

        if (event.key === 'Enter') {
            event.preventDefault();

            if (!panel.hidden && active >= 0 && active < items.length) {
                close();
                go(suggestionHref(items[active]).replace(/^#/, ''));
                return;
            }

            submit();
        }
    });

    // Un click afuera cierra. El listener va en el documento y se desengancha cuando el
    // buscador deja de estar en pantalla, para no acumular uno por cada visita.
    const onDocumentClick = event => {
        if (!box.contains(event.target)) {
            close();
        }
    };

    document.addEventListener('click', onDocumentClick);
    registerCleanup(() => {
        clearTimeout(timer);
        document.removeEventListener('click', onDocumentClick);
    });

    return el('form', {
        class: 'search',
        onSubmit: event => {
            event.preventDefault();
            submit();
        }
    }, box, el('button', { class: 'btn', type: 'submit' }, 'Buscar'));
}

function suggestionHref(item) {
    return item.kind === search.kinds.artist
        ? `#/artist/${item.musicBrainzId}`
        : `#/album/${item.musicBrainzId}`;
}

function suggestionRow(item, isActive, onPick) {
    const esArtista = item.kind === search.kinds.artist;

    const subtitle = [
        esArtista ? artistTypeLabel(item.subtitle) : item.subtitle,
        item.year
    ].filter(Boolean).join(' - ');

    return el('a', {
        class: `suggest__row${isActive ? ' is-active' : ''}`,
        href: suggestionHref(item),
        onClick: onPick
    },
        esArtista
            ? avatar({ userName: item.title, avatarUrl: item.imageUrl }, 36)
            : cover(item.imageUrl, 'cover cover--suggest'),
        el('span', { class: 'suggest__text' },
            el('strong', {}, item.title),
            subtitle ? el('span', { class: 'muted small' }, subtitle) : null),
        el('span', { class: 'muted small' }, esArtista ? 'Artista' : 'Álbum'));
}

// ---------------------------------------------------------------- busqueda

/**
 * Buscador unico. Un solo campo, sin selector de tipo: el usuario escribe "nevermind"
 * o "nirvana" o "come as you are" sin tener que decidir antes que clase de cosa es.
 *
 * Las tres busquedas salen EN PARALELO y cada seccion se pinta cuando llega la suya.
 * Es lo que hace viable el campo unico: contra MusicBrainz hay una cola de 1 request
 * por segundo, asi que esperar a las tres para mostrar algo significaria tres segundos
 * de pantalla vacia. Asi el usuario ve artistas al primer segundo y sigue leyendo
 * mientras llega el resto.
 *
 * Cada seccion tambien falla por su cuenta: que MusicBrainz se caiga a mitad de la
 * busqueda de canciones no puede borrar los albumes que ya estan en pantalla.
 */
async function viewSearch(params) {
    const query = (params.get('q') ?? '').trim();

    const form = searchBox(query);

    const results = el('div', { class: 'results' });
    const container = el('section', {},
        el('h1', {}, 'Buscar'),
        el('p', { class: 'muted' }, 'Los datos vienen de MusicBrainz y se cachean localmente.'),
        form,
        results);

    if (!query) {
        results.append(empty('Escribí algo para empezar.'));
        return container;
    }

    const sections = [
        searchSection('Artistas', catalog.searchArtists(query), artistCard),
        searchSection('Álbumes', catalog.searchAlbums(query), albumCard),
        searchSection('Canciones', catalog.searchSongs(query), songCard)
    ];

    results.append(...sections.map(section => section.node));

    // Cuando terminan las tres sin nada, el mensaje va una sola vez y no tres veces.
    Promise.all(sections.map(section => section.done)).then(counts => {
        if (counts.every(count => count === 0)) {
            results.append(empty('Sin resultados.'));
        }
    });

    return container;
}

/**
 * Una seccion del buscador: se dibuja vacia con su spinner y se completa sola.
 * Devuelve el nodo (para insertarlo ya) y una promesa con cuantos resultados hubo
 * (para poder decir "sin resultados" una sola vez cuando ninguna trajo nada).
 */
function searchSection(title, request, renderItem) {
    const body = el('div', {}, spinner());
    const node = el('div', { class: 'search-section is-loading' },
        el('h2', {}, title),
        body);

    const done = request.then(page => {
        node.classList.remove('is-loading');
        clear(body);

        if (!page.items.length) {
            // Una seccion vacia no se muestra: ocupa lugar y no dice nada.
            node.classList.add('is-empty');
            return 0;
        }

        body.append(el('ul', { class: 'card-list' }, page.items.map(renderItem)));

        return page.items.length;
    }).catch(error => {
        node.classList.remove('is-loading');
        clear(body).append(errorBox(error));

        // Cuenta como "trajo algo" para que el mensaje global de sin resultados no
        // aparezca tapando un error que el usuario tiene que ver.
        return -1;
    });

    return { node, done };
}

function artistCard(artist) {
    return el('li', { class: 'card' },
        el('a', { href: `#/artist/${artist.musicBrainzId}`, class: 'card__body card__body--cover' },
            avatar({ userName: artist.name, avatarUrl: artist.imageUrl }, 48),
            el('div', {},
                el('strong', {}, artist.name),
                artist.disambiguation && el('span', { class: 'muted' }, ` (${artist.disambiguation})`),
                el('div', { class: 'muted small' },
                    [artistTypeLabel(artist.type), artist.country].filter(Boolean).join(' - ')))));
}

function albumCard(album) {
    return el('li', { class: 'card' },
        el('a', { href: `#/album/${album.musicBrainzId}`, class: 'card__body card__body--cover' },
            cover(album.coverArtUrl),
            el('div', {},
                el('strong', {}, album.title),
                el('div', { class: 'muted small' },
                    [
                        album.artistName,
                        album.releaseDate?.slice(0, 4),
                        album.primaryType,
                        // La cantidad de ediciones es lo que decide el orden entre
                        // discos homonimos: mostrarla explica por que este esta primero.
                        album.releaseCount > 1 ? `${album.releaseCount} ediciones` : null
                    ].filter(Boolean).join(' - ')))));
}

/**
 * Una cancion lleva al album, no a si misma: en esta aplicacion se reseña el album.
 * Si la grabacion no esta asociada a ninguna edicion no hay a donde ir, y la fila se
 * muestra sin enlace en vez de prometer un click que no hace nada.
 */
function songCard(song) {
    const meta = [
        song.artistName,
        song.albumTitle,
        song.releaseDate?.slice(0, 4),
        formatDuration(song.lengthMilliseconds),
        // Cuantas grabaciones se colapsaron en esta fila: explica por que este tema
        // esta arriba y avisa de que hay mas versiones detras.
        song.versionCount > 1 ? `${song.versionCount} versiones` : null
    ].filter(Boolean).join(' - ');

    const body = [
        cover(song.coverArtUrl),
        el('div', {},
            el('strong', {}, song.title),
            el('div', { class: 'muted small' }, meta))
    ];

    return el('li', { class: 'card' },
        song.albumMusicBrainzId
            ? el('a', { href: `#/album/${song.albumMusicBrainzId}`, class: 'card__body card__body--cover' }, body)
            : el('div', { class: 'card__body card__body--cover' }, body));
}

/** Milisegundos a "m:ss". Devuelve null si la duracion no vino. */
function formatDuration(milliseconds) {
    if (!milliseconds || milliseconds < 0) {
        return null;
    }

    const totalSeconds = Math.round(milliseconds / 1000);
    const minutes = Math.floor(totalSeconds / 60);

    return `${minutes}:${String(totalSeconds % 60).padStart(2, '0')}`;
}

// ---------------------------------------------------------------- artista

async function viewArtist(mbid) {
    const [artist, albums] = await Promise.all([
        catalog.getArtist(mbid),
        catalog.getArtistAlbums(mbid, 1)
    ]);

    const favoriteButton = session.isAuthenticated
        ? el('button', {
            class: 'btn btn--ghost',
            onClick: async event => {
                event.target.disabled = true;
                try {
                    await users.addFavorite(mbid);
                    event.target.textContent = 'Agregado a favoritos';
                } catch (error) {
                    event.target.disabled = false;
                    main.prepend(errorBox(error));
                }
            }
        }, 'Agregar a favoritos')
        : null;

    return el('section', {},
        el('div', { class: 'artist-header' },
            avatar({ userName: artist.name, avatarUrl: artist.imageUrl }, 96),
            el('div', {},
                el('h1', {}, artist.name),
                artist.disambiguation && el('p', { class: 'muted' }, artist.disambiguation),
                el('p', { class: 'muted small' },
                    [artistTypeLabel(artist.type), artist.country].filter(Boolean).join(' - '),
                    ` - ${artist.favoriteCount} favoritos`),
                favoriteButton)),
        el('h2', {}, 'Discografía'),
        albums.items.length
            ? el('ul', { class: 'card-list' }, albums.items.map(albumCard))
            : empty('Sin álbumes cargados.'));
}

// ---------------------------------------------------------------- album

async function viewAlbum(mbid) {
    const album = await catalog.getAlbum(mbid);
    const page = await reviews.byAlbum(mbid);

    const list = el('div', {});
    const reload = async () => {
        clear(list).append(spinner());
        const fresh = await reviews.byAlbum(mbid);
        clear(list).append(renderReviewList(fresh.items));
    };

    clear(list).append(renderReviewList(page.items));

    const mine = session.isAuthenticated
        ? page.items.find(r => r.author.id === session.user.id)
        : null;

    return el('section', {},
        el('div', { class: 'album-header' },
            cover(album.coverArtUrl, 'cover'),
            el('div', {},
                el('h1', {}, album.title),
                el('p', {}, link(`#/artist/${album.artist.musicBrainzId}`, album.artist.name)),
                el('p', { class: 'muted small' },
                    [album.releaseDate?.slice(0, 4), album.primaryType].filter(Boolean).join(' - ')),
                el('p', { class: 'stats' },
                    album.averageScore !== null
                        ? el('span', { class: scoreClass(album.averageScore) },
                            album.averageScore.toFixed(1))
                        : el('span', { class: 'muted' }, 'Sin puntajes'),
                    el('span', { class: 'muted' }, ` ${album.reviewCount} reseña(s)`)))),
        reviewForm(mbid, mine, reload),
        el('h2', {}, 'Reseñas'),
        list);
}

function reviewForm(albumMbid, existing, onSaved) {
    if (!session.isAuthenticated) {
        return el('p', { class: 'muted' },
            link('#/login', 'Iniciá sesión'), ' para escribir una reseña.');
    }

    const score = el('input', {
        type: 'number', class: 'input input--compact', min: 0, max: 100, required: true,
        value: existing ? existing.score : 75
    });

    const text = el('textarea', {
        class: 'input', rows: 4, required: true,
        placeholder: '¿Qué te pareció?', value: existing?.text ?? ''
    });

    const messages = el('div', {});

    return el('form', {
        class: 'panel',
        onSubmit: async event => {
            event.preventDefault();
            clear(messages);

            try {
                if (existing) {
                    await reviews.update(existing.id, Number(score.value), text.value);
                } else {
                    await reviews.create(albumMbid, Number(score.value), text.value);
                }

                await onSaved();
            } catch (error) {
                messages.append(errorBox(error));
            }
        }
    },
        el('h3', {}, existing ? 'Editar tu reseña' : 'Escribir una reseña'),
        el('div', { class: 'row' },
            el('label', {}, 'Puntaje (0-100) ', score),
            el('button', { class: 'btn', type: 'submit' }, existing ? 'Guardar' : 'Publicar')),
        text,
        messages);
}

function renderReviewList(items) {
    if (!items.length) {
        return empty('Todavía no hay reseñas de este álbum.');
    }

    return el('ul', { class: 'review-list' }, items.map(review =>
        el('li', { class: 'panel' },
            el('div', { class: 'review-head' },
                avatar(review.author),
                link(`#/user/${encodeURIComponent(review.author.userName)}`, review.author.userName),
                el('span', { class: scoreClass(review.score) }, review.score),
                el('span', { class: 'muted small' }, formatDate(review.createdAt)),
                review.updatedAt && el('span', { class: 'muted small' }, '(editada)')),
            el('p', { class: 'review-text' }, review.text),
            el('div', { class: 'row row--gap' },
                voteButtons('review', review),
                link(`#/review/${review.id}`, `${review.commentCount} comentario(s)`)))));
}

// ---------------------------------------------------------------- votos

function voteButtons(kind, item) {
    const state = { like: item.likeCount, dislike: item.dislikeCount, mine: item.currentUserVote };

    const likeButton = el('button', { class: 'btn btn--vote' });
    const dislikeButton = el('button', { class: 'btn btn--vote' });

    const paint = () => {
        likeButton.textContent = `+${state.like}`;
        dislikeButton.textContent = `-${state.dislike}`;
        likeButton.classList.toggle('is-active', state.mine === true);
        dislikeButton.classList.toggle('is-active', state.mine === false);
    };

    const vote = async isLike => {
        if (!session.isAuthenticated) {
            go('/login');
            return;
        }

        likeButton.disabled = dislikeButton.disabled = true;

        try {
            const result = kind === 'review'
                ? await likes.review(item.id, isLike)
                : await likes.comment(item.id, isLike);

            // La Api devuelve los contadores ya recalculados: no hace falta
            // recargar la vista entera para que el numero quede bien.
            state.like = result.likeCount;
            state.dislike = result.dislikeCount;
            state.mine = result.currentUserVote;
            paint();
        } finally {
            likeButton.disabled = dislikeButton.disabled = false;
        }
    };

    likeButton.addEventListener('click', () => vote(true));
    dislikeButton.addEventListener('click', () => vote(false));
    paint();

    return el('span', { class: 'votes' }, likeButton, dislikeButton);
}

// ---------------------------------------------------------------- review + hilo

async function viewReview(id) {
    const review = await reviews.get(Number(id));
    const thread = await comments.tree(review.id);

    const threadBox = el('div', { class: 'thread' });

    const reload = async () => {
        clear(threadBox).append(spinner());
        const fresh = await comments.tree(review.id);
        clear(threadBox).append(renderThread(fresh, review.id, reload));
    };

    clear(threadBox).append(renderThread(thread, review.id, reload));

    return el('section', {},
        link(`#/album/${review.album.musicBrainzId}`, `< ${review.album.title}`),
        el('div', { class: 'panel' },
            el('div', { class: 'review-head' },
                avatar(review.author),
                link(`#/user/${encodeURIComponent(review.author.userName)}`, review.author.userName),
                el('span', { class: scoreClass(review.score) }, review.score),
                el('span', { class: 'muted small' }, formatDate(review.createdAt))),
            el('p', { class: 'review-text' }, review.text),
            voteButtons('review', review)),
        el('h2', {}, 'Comentarios'),
        commentForm(review.id, null, reload),
        threadBox);
}

function renderThread(nodes, reviewId, reload) {
    if (!nodes.length) {
        return empty('Nadie comentó todavía.');
    }

    return el('ul', { class: 'comment-list' },
        nodes.map(node => renderComment(node, reviewId, reload)));
}

/**
 * Renderiza un nodo y sus respuestas. La recursion aca es sobre un arbol que ya
 * vino armado en una sola respuesta: no dispara ninguna consulta adicional.
 */
function renderComment(node, reviewId, reload) {
    const isMine = session.isAuthenticated && node.author?.id === session.user.id;
    const replyBox = el('div', {});

    const body = node.isDeleted
        ? el('p', { class: 'muted comment-text' }, '[comentario eliminado]')
        : el('p', { class: 'comment-text' }, node.text);

    const actions = el('div', { class: 'row row--gap small' });

    if (!node.isDeleted) {
        actions.append(voteButtons('comment', node));

        if (session.isAuthenticated) {
            actions.append(el('button', {
                class: 'btn btn--link',
                onClick: () => {
                    if (replyBox.firstChild) {
                        clear(replyBox);
                    } else {
                        replyBox.append(commentForm(reviewId, node.id, reload));
                    }
                }
            }, 'Responder'));
        }

        if (isMine) {
            actions.append(el('button', {
                class: 'btn btn--link btn--danger',
                onClick: async () => {
                    await comments.remove(node.id);
                    await reload();
                }
            }, 'Borrar'));
        }
    }

    return el('li', { class: 'comment' },
        el('div', { class: 'comment-head' },
            node.author ? avatar(node.author, 24) : null,
            node.author
                ? link(`#/user/${encodeURIComponent(node.author.userName)}`, node.author.userName)
                : el('span', { class: 'muted' }, 'usuario eliminado'),
            el('span', { class: 'muted small' }, formatDate(node.createdAt))),
        body,
        actions,
        replyBox,
        node.replies.length
            ? el('ul', { class: 'comment-list comment-list--nested' },
                node.replies.map(reply => renderComment(reply, reviewId, reload)))
            : null);
}

function commentForm(reviewId, parentCommentId, onSaved) {
    if (!session.isAuthenticated) {
        return el('p', { class: 'muted' },
            link('#/login', 'Iniciá sesión'), ' para comentar.');
    }

    const text = el('textarea', {
        class: 'input', rows: parentCommentId ? 2 : 3, required: true,
        placeholder: parentCommentId ? 'Tu respuesta...' : 'Escribí un comentario...'
    });

    const messages = el('div', {});

    return el('form', {
        class: parentCommentId ? 'reply-form' : 'panel',
        onSubmit: async event => {
            event.preventDefault();
            clear(messages);

            try {
                await comments.create(reviewId, text.value, parentCommentId);
                text.value = '';
                await onSaved();
            } catch (error) {
                messages.append(errorBox(error));
            }
        }
    }, text, el('button', { class: 'btn', type: 'submit' }, 'Enviar'), messages);
}

// ---------------------------------------------------------------- perfiles

async function viewProfile(userName) {
    const [profile, userReviews] = await Promise.all([
        users.profile(userName),
        reviews.byUser(userName)
    ]);

    return el('section', {},
        el('div', { class: 'profile-head' },
            avatar(profile, 64),
            el('div', {},
                el('h1', {}, profile.userName),
                profile.bio && el('p', {}, profile.bio),
                el('p', { class: 'muted small' }, `Se unió el ${formatDate(profile.createdAt)}`)),
            followButton(profile)),
        el('div', { class: 'stats' },
            el('span', {}, `${profile.reviewCount} reseñas`),
            el('span', {}, `${profile.commentCount} comentarios`),
            el('span', {}, `${profile.followerCount} seguidores`),
            el('span', {}, `${profile.followingCount} siguiendo`),
            profile.averageScore !== null
                ? el('span', { class: scoreClass(profile.averageScore) },
                    `promedio ${profile.averageScore.toFixed(1)}`)
                : null),
        el('h2', {}, 'Artistas favoritos'),
        profile.favoriteArtists.length
            ? el('ul', { class: 'card-list' }, profile.favoriteArtists.map(favorite =>
                el('li', { class: 'card' },
                    link(`#/artist/${favorite.musicBrainzId}`, favorite.name, { class: 'card__body' }))))
            : empty('Todavía no marcó ninguno.'),
        el('h2', {}, 'Reseñas'),
        renderReviewList(userReviews.items));
}

/**
 * Boton de seguir. La Api devuelve el perfil ya actualizado, asi que el estado y los
 * contadores se repintan con esa respuesta y no hace falta recargar la vista.
 */
function followButton(profile) {
    if (!session.isAuthenticated || profile.isCurrentUser) {
        return null;
    }

    const state = { following: profile.isFollowedByCurrentUser };
    const button = el('button', { class: 'btn' });

    const paint = () => {
        button.textContent = state.following ? 'Siguiendo' : 'Seguir';
        button.classList.toggle('btn--ghost', state.following);
    };

    button.addEventListener('click', async () => {
        button.disabled = true;

        try {
            const updated = state.following
                ? await users.unfollow(profile.userName)
                : await users.follow(profile.userName);

            state.following = updated.isFollowedByCurrentUser;
            paint();
        } finally {
            button.disabled = false;
        }
    });

    paint();

    return el('div', { class: 'profile-actions' }, button);
}

/** Tope de la subida, en bytes. Tiene que coincidir con Avatars:MaxSizeBytes. */
const MAX_AVATAR_BYTES = 2 * 1024 * 1024;

async function viewMyProfile() {
    if (!session.isAuthenticated) {
        go('/login');
        return el('div');
    }

    let profile = await users.me();

    const section = el('section', {},
        el('h1', {}, 'Mi perfil'),
        avatarPanel(profile),
        profilePanel(profile),
        passwordPanel(),
        link(`#/user/${encodeURIComponent(profile.userName)}`, 'Ver mi perfil público'));

    return section;
}

/**
 * Subida del avatar desde la PC o el telefono.
 *
 * El tamanio se comprueba ANTES de subir: el archivo ya esta en el navegador, asi que
 * mandar tres megas para que el servidor conteste 400 es gastar la conexion del usuario
 * —la de un telefono, encima— para averiguar algo que se sabia de antemano. El servidor
 * lo vuelve a comprobar igual, porque una validacion que solo vive en el cliente no es
 * una validacion.
 */
function avatarPanel(profile) {
    const preview = el('div', { class: 'avatar-preview' }, avatar(profile, 96));
    const messages = el('div', {});

    const repaint = updated => {
        clear(preview).append(avatar(updated, 96));

        // La barra muestra el avatar: hay que refrescarla, y la sesion guardada tiene
        // una copia del usuario que quedaria vieja.
        const current = session.get();

        if (current) {
            session.set({ ...current, user: { ...current.user, avatarUrl: updated.avatarUrl } });
        }
    };

    const file = el('input', {
        type: 'file',
        class: 'input',
        accept: 'image/jpeg,image/png,image/webp,image/gif',
        onChange: async event => {
            const chosen = event.target.files?.[0];
            clear(messages);

            if (!chosen) {
                return;
            }

            if (chosen.size > MAX_AVATAR_BYTES) {
                messages.append(errorBox({
                    message: `La imagen pesa ${(chosen.size / 1024 / 1024).toFixed(1)} MB `
                        + `y el máximo son ${MAX_AVATAR_BYTES / 1024 / 1024} MB.`
                }));
                event.target.value = '';
                return;
            }

            file.disabled = true;

            try {
                repaint(await users.uploadAvatar(chosen));
                messages.append(el('div', { class: 'alert alert--ok' }, 'Avatar actualizado.'));
            } catch (error) {
                messages.append(errorBox(error));
            } finally {
                file.disabled = false;
                event.target.value = '';
            }
        }
    });

    const remove = el('button', {
        class: 'btn btn--ghost',
        type: 'button',
        onClick: async () => {
            clear(messages);
            remove.disabled = true;

            try {
                repaint(await users.removeAvatar());
            } catch (error) {
                messages.append(errorBox(error));
            } finally {
                remove.disabled = false;
            }
        }
    }, 'Quitar');

    return el('div', { class: 'panel' },
        el('h3', {}, 'Foto de perfil'),
        el('div', { class: 'row row--gap' },
            preview,
            el('div', {},
                el('label', { class: 'muted small' },
                    `JPG, PNG, WebP o GIF. Máximo ${MAX_AVATAR_BYTES / 1024 / 1024} MB.`,
                    file),
                remove)),
        messages);
}

function profilePanel(profile) {
    const bio = el('textarea', { class: 'input', rows: 3, value: profile.bio ?? '' });
    const messages = el('div', {});

    return el('form', {
        class: 'panel',
        onSubmit: async event => {
            event.preventDefault();
            clear(messages);

            try {
                await users.updateMe(bio.value || null, profile.avatarUrl ?? null);
                messages.append(el('div', { class: 'alert alert--ok' }, 'Perfil actualizado.'));
            } catch (error) {
                messages.append(errorBox(error));
            }
        }
    },
        el('h3', {}, 'Datos'),
        el('label', {}, 'Bio', bio),
        el('button', { class: 'btn', type: 'submit' }, 'Guardar'),
        messages);
}

function passwordPanel() {
    const current = el('input', { type: 'password', class: 'input', required: true, autocomplete: 'current-password' });
    const nueva = el('input', { type: 'password', class: 'input', required: true, minLength: 8, autocomplete: 'new-password' });
    const messages = el('div', {});

    return el('form', {
        class: 'panel',
        onSubmit: async event => {
            event.preventDefault();
            clear(messages);

            try {
                await auth.changePassword(current.value, nueva.value);

                // El cambio revoca todas las sesiones, esta incluida: no hay forma de
                // seguir navegando, asi que se avisa y se manda a entrar de nuevo.
                messages.append(el('div', { class: 'alert alert--ok' },
                    'Contraseña cambiada. Se cerraron todas tus sesiones: volvé a entrar.'));

                setTimeout(() => go('/login'), 1500);
            } catch (error) {
                messages.append(errorBox(error));
            }
        }
    },
        el('h3', {}, 'Cambiar contraseña'),
        el('p', { class: 'muted small' },
            'Al cambiarla se cierran todas tus sesiones, incluida esta.'),
        el('label', {}, 'Contraseña actual', current),
        el('label', {}, 'Contraseña nueva', nueva,
            el('small', { class: 'muted' },
                'Mínimo 8 caracteres, con mayúscula, minúscula y un dígito.')),
        el('button', { class: 'btn', type: 'submit' }, 'Cambiar contraseña'),
        messages);
}

// ---------------------------------------------------------------- auth

function authForm({ title, fields, submit, action }) {
    const messages = el('div', {});

    return el('section', { class: 'narrow' },
        el('h1', {}, title),
        el('form', {
            class: 'panel',
            onSubmit: async event => {
                event.preventDefault();
                clear(messages);

                try {
                    await action();
                    go('/');
                } catch (error) {
                    messages.append(errorBox(error));
                }
            }
        }, fields, el('button', { class: 'btn', type: 'submit' }, submit), messages));
}

function viewLogin() {
    const identifier = el('input', { class: 'input', required: true, autofocus: true });
    const password = el('input', { class: 'input', type: 'password', required: true });

    return el('div', {},
        authForm({
            title: 'Entrar',
            fields: [
                el('label', {}, 'Usuario o email', identifier),
                el('label', {}, 'Contraseña', password)
            ],
            submit: 'Entrar',
            action: () => auth.login(identifier.value.trim(), password.value)
        }),
        el('p', { class: 'narrow muted' }, '¿No tenés cuenta? ', link('#/register', 'Crear una')));
}

function viewRegister() {
    const userName = el('input', { class: 'input', required: true, minLength: 3, autofocus: true });
    const email = el('input', { class: 'input', type: 'email', required: true });
    const password = el('input', { class: 'input', type: 'password', required: true, minLength: 8 });

    return el('div', {},
        authForm({
            title: 'Crear cuenta',
            fields: [
                el('label', {}, 'Usuario', userName),
                el('label', {}, 'Email', email),
                el('label', {}, 'Contraseña', password,
                    el('small', { class: 'muted' },
                        'Mínimo 8 caracteres, con mayúscula, minúscula y un dígito.'))
            ],
            submit: 'Crear cuenta',
            action: () => auth.register(userName.value.trim(), email.value.trim(), password.value)
        }),
        el('p', { class: 'narrow muted' }, '¿Ya tenés cuenta? ', link('#/login', 'Entrar')));
}

// ---------------------------------------------------------------- arranque

window.addEventListener('hashchange', render);
window.addEventListener('session-changed', () => {
    renderNav();
    render();
});

renderNav();
render();
