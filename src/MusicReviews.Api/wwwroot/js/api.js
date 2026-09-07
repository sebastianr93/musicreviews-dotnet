// Cliente de la Api. Guarda la sesion en localStorage y renueva el access token
// de forma transparente cuando vence.

const STORAGE_KEY = 'musicreviews.session';

export const session = {
    get() {
        try {
            return JSON.parse(localStorage.getItem(STORAGE_KEY)) || null;
        } catch {
            return null;
        }
    },
    set(value) {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
        window.dispatchEvent(new CustomEvent('session-changed'));
    },
    clear() {
        localStorage.removeItem(STORAGE_KEY);
        window.dispatchEvent(new CustomEvent('session-changed'));
    },
    get user() {
        return this.get()?.user ?? null;
    },
    get isAuthenticated() {
        return !!this.get()?.accessToken;
    }
};

/** Error con el ProblemDetails de la Api ya parseado. */
export class ApiError extends Error {
    constructor(status, problem) {
        super(problem?.detail || problem?.title || `Error ${status}`);
        this.status = status;
        this.code = problem?.code;
        this.problem = problem;
    }

    /** Mensajes de ValidationProblemDetails, aplanados para mostrarlos juntos. */
    get validationMessages() {
        const errors = this.problem?.errors;
        return errors ? Object.values(errors).flat() : [];
    }
}

// Una sola renovacion en vuelo: si tres requests fallan con 401 a la vez, las tres
// esperan el mismo refresh en vez de disparar tres rotaciones (y que dos se pisen
// entre si, que para el backend es un reuso de token y revoca la familia entera).
let refreshInFlight = null;

async function refreshTokens() {
    const current = session.get();

    if (!current?.refreshToken) {
        return false;
    }

    refreshInFlight ??= (async () => {
        try {
            const response = await fetch('/api/auth/refresh', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ refreshToken: current.refreshToken })
            });

            if (!response.ok) {
                session.clear();
                return false;
            }

            session.set(await response.json());
            return true;
        } finally {
            refreshInFlight = null;
        }
    })();

    return refreshInFlight;
}

export async function api(path, { method = 'GET', body, formData, auth = false, retry = true } = {}) {
    const headers = {};
    const current = session.get();

    if (body !== undefined) {
        headers['Content-Type'] = 'application/json';
    }

    // Con FormData NO se declara Content-Type: el navegador tiene que ponerlo el mismo
    // para poder agregar el "boundary" que separa las partes. Declararlo a mano deja el
    // cuerpo sin boundary y el servidor no puede parsearlo.

    if (auth && current?.accessToken) {
        headers['Authorization'] = `Bearer ${current.accessToken}`;
    }

    const response = await fetch(path, {
        method,
        headers,
        body: formData ?? (body === undefined ? undefined : JSON.stringify(body))
    });

    // El access token dura 15 minutos: que venza a mitad de sesion es normal,
    // no un error que el usuario tenga que ver.
    if (response.status === 401 && auth && retry && await refreshTokens()) {
        return api(path, { method, body, formData, auth, retry: false });
    }

    if (response.status === 204) {
        return null;
    }

    const isJson = response.headers.get('content-type')?.includes('json');
    const payload = isJson ? await response.json() : null;

    if (!response.ok) {
        throw new ApiError(response.status, payload);
    }

    return payload;
}

export const auth = {
    async register(userName, email, password) {
        const result = await api('/api/auth/register', {
            method: 'POST',
            body: { userName, email, password }
        });
        session.set(result);
        return result;
    },

    async login(userNameOrEmail, password) {
        const result = await api('/api/auth/login', {
            method: 'POST',
            body: { userNameOrEmail, password }
        });
        session.set(result);
        return result;
    },

    /**
     * Cambia la contraseña. La Api revoca TODAS las sesiones, incluida esta, asi que
     * despues hay que volver a entrar: se limpia la sesion local para no quedar con
     * tokens que ya no sirven.
     */
    async changePassword(currentPassword, newPassword) {
        await api('/api/auth/password', {
            method: 'POST',
            auth: true,
            body: { currentPassword, newPassword }
        });

        session.clear();
    },

    async logout() {
        const current = session.get();

        if (current?.refreshToken) {
            // Si el logout falla igual se limpia la sesion local: dejar al usuario
            // "adentro" porque la red fallo seria peor que perder la revocacion.
            try {
                await api('/api/auth/logout', {
                    method: 'POST',
                    body: { refreshToken: current.refreshToken }
                });
            } catch {
                /* ignorado a proposito */
            }
        }

        session.clear();
    }
};

export const catalog = {
    searchArtists: (query, page = 1) =>
        api(`/api/catalog/artists?query=${encodeURIComponent(query)}&page=${page}`),
    searchAlbums: (query, page = 1) =>
        api(`/api/catalog/albums?query=${encodeURIComponent(query)}&page=${page}`),
    searchSongs: (query, page = 1) =>
        api(`/api/catalog/songs?query=${encodeURIComponent(query)}&page=${page}`),
    getArtist: mbid => api(`/api/catalog/artists/${mbid}`),
    getArtistAlbums: (mbid, page = 1) => api(`/api/catalog/artists/${mbid}/albums?page=${page}`),
    getAlbum: mbid => api(`/api/catalog/albums/${mbid}`)
};

export const reviews = {
    byAlbum: (mbid, sort = 'Newest') =>
        api(`/api/albums/${mbid}/reviews?sort=${sort}`, { auth: session.isAuthenticated }),
    byUser: userName =>
        api(`/api/users/${encodeURIComponent(userName)}/reviews`, { auth: session.isAuthenticated }),
    get: id => api(`/api/reviews/${id}`, { auth: session.isAuthenticated }),
    create: (albumMusicBrainzId, score, text) =>
        api('/api/reviews', { method: 'POST', auth: true, body: { albumMusicBrainzId, score, text } }),
    update: (id, score, text) =>
        api(`/api/reviews/${id}`, { method: 'PUT', auth: true, body: { score, text } }),
    remove: id => api(`/api/reviews/${id}`, { method: 'DELETE', auth: true })
};

export const comments = {
    tree: (reviewId, sort = 'Oldest') =>
        api(`/api/reviews/${reviewId}/comments?sort=${sort}`, { auth: session.isAuthenticated }),
    create: (reviewId, text, parentCommentId = null) =>
        api(`/api/reviews/${reviewId}/comments`, {
            method: 'POST', auth: true, body: { text, parentCommentId }
        }),
    update: (id, text) => api(`/api/comments/${id}`, { method: 'PUT', auth: true, body: { text } }),
    remove: id => api(`/api/comments/${id}`, { method: 'DELETE', auth: true })
};

export const likes = {
    review: (id, isLike) =>
        api(`/api/reviews/${id}/likes`, { method: 'POST', auth: true, body: { isLike } }),
    comment: (id, isLike) =>
        api(`/api/comments/${id}/likes`, { method: 'POST', auth: true, body: { isLike } })
};

export const search = {
    /**
     * Sugerencias del catalogo local. No sale a MusicBrainz: su limite es 1 request por
     * segundo y eso no da para un desplegable que responde con cada tecla.
     */
    quick: (q, limit = 8) =>
        api(`/api/search/quick?q=${encodeURIComponent(q)}&limit=${limit}`),

    /** Codigos de QuickSearchKind, tal como los serializa la Api (enteros). */
    kinds: { artist: 1, album: 2 }
};

export const home = {
    popular: (page = 1, pageSize = 12) =>
        api(`/api/home/popular?page=${page}&pageSize=${pageSize}`, { auth: session.isAuthenticated }),
    explore: (page = 1, pageSize = 12) =>
        api(`/api/home/explore?page=${page}&pageSize=${pageSize}`, { auth: session.isAuthenticated })
};

export const news = {
    latest: (limit = 8) => api(`/api/news?limit=${limit}`)
};

export const activity = {
    /** Codigos de ActivityKind, tal como los serializa la Api (enteros). */
    kinds: {
        reviewPublished: 1,
        commentPublished: 2,
        reviewVoted: 3,
        commentVoted: 4
    },

    following: (cursor = null, limit = 20) => {
        const query = new URLSearchParams({ limit });

        if (cursor) {
            query.set('cursor', cursor);
        }

        return api(`/api/feed?${query}`, { auth: true });
    },

    byUser: (userName, cursor = null, limit = 20) => {
        const query = new URLSearchParams({ limit });

        if (cursor) {
            query.set('cursor', cursor);
        }

        return api(`/api/users/${encodeURIComponent(userName)}/activity?${query}`);
    }
};

export const notifications = {
    /** Codigos de NotificationType, tal como los serializa la Api (enteros). */
    types: {
        reviewCommented: 1,
        commentReplied: 2,
        reviewVoted: 3,
        commentVoted: 4,
        newFollower: 5
    },

    list: (cursor = null, limit = 20, unreadOnly = false) => {
        const query = new URLSearchParams({ limit, unreadOnly });

        if (cursor) {
            query.set('cursor', cursor);
        }

        return api(`/api/notifications?${query}`, { auth: true });
    },

    unreadCount: () => api('/api/notifications/unread-count', { auth: true }),

    markRead: id => api(`/api/notifications/${id}/read`, { method: 'POST', auth: true }),

    markAllRead: () => api('/api/notifications/read-all', { method: 'POST', auth: true })
};

export const users = {
    // Con sesion se manda el token aunque el endpoint sea publico: es lo que hace que
    // la respuesta traiga isFollowedByCurrentUser y el boton salga en el estado correcto.
    profile: userName =>
        api(`/api/users/${encodeURIComponent(userName)}`, { auth: session.isAuthenticated }),
    me: () => api('/api/users/me', { auth: true }),
    follow: userName =>
        api(`/api/users/${encodeURIComponent(userName)}/follow`, { method: 'POST', auth: true }),
    unfollow: userName =>
        api(`/api/users/${encodeURIComponent(userName)}/follow`, { method: 'DELETE', auth: true }),
    updateMe: (bio, avatarUrl) =>
        api('/api/users/me', { method: 'PUT', auth: true, body: { bio, avatarUrl } }),
    addFavorite: artistMusicBrainzId =>
        api('/api/users/me/favorites', { method: 'POST', auth: true, body: { artistMusicBrainzId } }),
    uploadAvatar: file => {
        const form = new FormData();
        form.append('file', file);

        return api('/api/users/me/avatar', { method: 'POST', auth: true, formData: form });
    },
    removeAvatar: () => api('/api/users/me/avatar', { method: 'DELETE', auth: true }),
    removeFavorite: mbid =>
        api(`/api/users/me/favorites/${mbid}`, { method: 'DELETE', auth: true })
};
