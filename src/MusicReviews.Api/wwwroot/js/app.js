import { auth, catalog, comments, likes, reviews, session, users } from './api.js';
import { avatar, clear, el, empty, errorBox, formatDate, link, scoreClass, spinner } from './dom.js';

const main = document.getElementById('view');
const nav = document.getElementById('nav-session');

// ---------------------------------------------------------------- router

const routes = [
    [/^\/?$/, viewSearch],
    [/^\/login$/, viewLogin],
    [/^\/register$/, viewRegister],
    [/^\/me$/, viewMyProfile],
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

    for (const [pattern, view] of routes) {
        const match = pattern.exec(path);

        if (match) {
            clear(main).append(spinner());

            try {
                const content = await view(...match.slice(1).map(decodeURIComponent), params);
                clear(main).append(content);
            } catch (error) {
                clear(main).append(errorBox(error));
            }

            window.scrollTo(0, 0);
            return;
        }
    }

    clear(main).append(el('h1', {}, 'No encontrado'), empty('Esa direccion no existe.'));
}

function go(path) {
    location.hash = path;
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

    if (session.isAuthenticated) {
        const user = session.user;

        nav.append(
            link(`#/user/${encodeURIComponent(user.userName)}`, user.userName, { class: 'nav-user' }),
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

// ---------------------------------------------------------------- busqueda

async function viewSearch(params) {
    const query = params.get('q') ?? '';
    const mode = params.get('mode') === 'artists' ? 'artists' : 'albums';

    const input = el('input', {
        type: 'search',
        class: 'input',
        placeholder: 'Buscar un album o un artista...',
        value: query,
        autofocus: true
    });

    const modeSelect = el('select', { class: 'input input--compact' },
        el('option', { value: 'albums', selected: mode === 'albums' }, 'Albumes'),
        el('option', { value: 'artists', selected: mode === 'artists' }, 'Artistas'));

    const form = el('form', {
        class: 'search',
        onSubmit: event => {
            event.preventDefault();
            const term = input.value.trim();

            if (term) {
                go(`/?q=${encodeURIComponent(term)}&mode=${modeSelect.value}`);
            }
        }
    }, input, modeSelect, el('button', { class: 'btn', type: 'submit' }, 'Buscar'));

    const results = el('div', { class: 'results' });
    const container = el('section', {},
        el('h1', {}, 'Catalogo'),
        el('p', { class: 'muted' }, 'Los datos vienen de MusicBrainz y se cachean localmente.'),
        form,
        results);

    if (!query) {
        results.append(empty('Escribi algo para empezar.'));
        return container;
    }

    results.append(spinner());

    const page = mode === 'artists'
        ? await catalog.searchArtists(query)
        : await catalog.searchAlbums(query);

    clear(results);

    if (!page.items.length) {
        results.append(empty('Sin resultados.'));
        return container;
    }

    results.append(el('ul', { class: 'card-list' },
        page.items.map(item => mode === 'artists' ? artistCard(item) : albumCard(item))));

    return container;
}

function artistCard(artist) {
    return el('li', { class: 'card' },
        el('a', { href: `#/artist/${artist.musicBrainzId}`, class: 'card__body' },
            el('strong', {}, artist.name),
            artist.disambiguation && el('span', { class: 'muted' }, ` (${artist.disambiguation})`),
            el('div', { class: 'muted small' },
                [artist.type, artist.country].filter(Boolean).join(' - '))));
}

function albumCard(album) {
    return el('li', { class: 'card' },
        el('a', { href: `#/album/${album.musicBrainzId}`, class: 'card__body card__body--cover' },
            album.coverArtUrl
                ? el('img', { class: 'cover cover--small', src: album.coverArtUrl, alt: '', loading: 'lazy' })
                : el('span', { class: 'cover cover--small cover--empty' }),
            el('div', {},
                el('strong', {}, album.title),
                el('div', { class: 'muted small' },
                    [album.artistName, album.releaseDate?.slice(0, 4), album.primaryType]
                        .filter(Boolean).join(' - ')))));
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
        el('h1', {}, artist.name),
        artist.disambiguation && el('p', { class: 'muted' }, artist.disambiguation),
        el('p', { class: 'muted small' },
            [artist.type, artist.country].filter(Boolean).join(' - '),
            ` - ${artist.favoriteCount} favoritos`),
        favoriteButton,
        el('h2', {}, 'Discografia'),
        albums.items.length
            ? el('ul', { class: 'card-list' }, albums.items.map(albumCard))
            : empty('Sin albumes cargados.'));
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
            album.coverArtUrl
                ? el('img', { class: 'cover', src: album.coverArtUrl, alt: '' })
                : el('div', { class: 'cover cover--empty' }),
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
                    el('span', { class: 'muted' }, ` ${album.reviewCount} review(s)`)))),
        reviewForm(mbid, mine, reload),
        el('h2', {}, 'Reviews'),
        list);
}

function reviewForm(albumMbid, existing, onSaved) {
    if (!session.isAuthenticated) {
        return el('p', { class: 'muted' },
            link('#/login', 'Inicia sesion'), ' para escribir una review.');
    }

    const score = el('input', {
        type: 'number', class: 'input input--compact', min: 0, max: 100, required: true,
        value: existing ? existing.score : 75
    });

    const text = el('textarea', {
        class: 'input', rows: 4, required: true,
        placeholder: 'Que te parecio?', value: existing?.text ?? ''
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
        el('h3', {}, existing ? 'Editar tu review' : 'Escribir una review'),
        el('div', { class: 'row' },
            el('label', {}, 'Puntaje (0-100) ', score),
            el('button', { class: 'btn', type: 'submit' }, existing ? 'Guardar' : 'Publicar')),
        text,
        messages);
}

function renderReviewList(items) {
    if (!items.length) {
        return empty('Todavia no hay reviews de este album.');
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
        return empty('Nadie comento todavia.');
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
            link('#/login', 'Inicia sesion'), ' para comentar.');
    }

    const text = el('textarea', {
        class: 'input', rows: parentCommentId ? 2 : 3, required: true,
        placeholder: parentCommentId ? 'Tu respuesta...' : 'Escribi un comentario...'
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
                el('p', { class: 'muted small' }, `Se unio el ${formatDate(profile.createdAt)}`))),
        el('div', { class: 'stats' },
            el('span', {}, `${profile.reviewCount} reviews`),
            el('span', {}, `${profile.commentCount} comentarios`),
            profile.averageScore !== null
                ? el('span', { class: scoreClass(profile.averageScore) },
                    `promedio ${profile.averageScore.toFixed(1)}`)
                : null),
        el('h2', {}, 'Artistas favoritos'),
        profile.favoriteArtists.length
            ? el('ul', { class: 'card-list' }, profile.favoriteArtists.map(favorite =>
                el('li', { class: 'card' },
                    link(`#/artist/${favorite.musicBrainzId}`, favorite.name, { class: 'card__body' }))))
            : empty('Todavia no marco ninguno.'),
        el('h2', {}, 'Reviews'),
        renderReviewList(userReviews.items));
}

async function viewMyProfile() {
    if (!session.isAuthenticated) {
        go('/login');
        return el('div');
    }

    const profile = await users.me();

    const bio = el('textarea', { class: 'input', rows: 3, value: profile.bio ?? '' });
    const avatarUrl = el('input', {
        type: 'url', class: 'input', placeholder: 'https://...', value: profile.avatarUrl ?? ''
    });
    const messages = el('div', {});

    return el('section', {},
        el('h1', {}, 'Mi perfil'),
        el('form', {
            class: 'panel',
            onSubmit: async event => {
                event.preventDefault();
                clear(messages);

                try {
                    await users.updateMe(bio.value || null, avatarUrl.value || null);
                    messages.append(el('div', { class: 'alert alert--ok' }, 'Perfil actualizado.'));
                } catch (error) {
                    messages.append(errorBox(error));
                }
            }
        },
            el('label', {}, 'Bio', bio),
            el('label', {}, 'URL del avatar', avatarUrl),
            el('button', { class: 'btn', type: 'submit' }, 'Guardar'),
            messages),
        link(`#/user/${encodeURIComponent(profile.userName)}`, 'Ver mi perfil publico'));
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
                el('label', {}, 'Contrasenia', password)
            ],
            submit: 'Entrar',
            action: () => auth.login(identifier.value.trim(), password.value)
        }),
        el('p', { class: 'narrow muted' }, 'No tenes cuenta? ', link('#/register', 'Crear una')));
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
                el('label', {}, 'Contrasenia', password,
                    el('small', { class: 'muted' },
                        'Minimo 8 caracteres, con mayuscula, minuscula y un digito.'))
            ],
            submit: 'Crear cuenta',
            action: () => auth.register(userName.value.trim(), email.value.trim(), password.value)
        }),
        el('p', { class: 'narrow muted' }, 'Ya tenes cuenta? ', link('#/login', 'Entrar')));
}

// ---------------------------------------------------------------- arranque

window.addEventListener('hashchange', render);
window.addEventListener('session-changed', () => {
    renderNav();
    render();
});

renderNav();
render();
