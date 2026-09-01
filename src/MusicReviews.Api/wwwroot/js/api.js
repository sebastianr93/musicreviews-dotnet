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

export async function api(path, { method = 'GET', body, auth = false, retry = true } = {}) {
    const headers = {};
    const current = session.get();

    if (body !== undefined) {
        headers['Content-Type'] = 'application/json';
    }

    if (auth && current?.accessToken) {
        headers['Authorization'] = `Bearer ${current.accessToken}`;
    }

    const response = await fetch(path, {
        method,
        headers,
        body: body === undefined ? undefined : JSON.stringify(body)
    });

    // El access token dura 15 minutos: que venza a mitad de sesion es normal,
    // no un error que el usuario tenga que ver.
    if (response.status === 401 && auth && retry && await refreshTokens()) {
        return api(path, { method, body, auth, retry: false });
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

export const users = {
    profile: userName => api(`/api/users/${encodeURIComponent(userName)}`),
    me: () => api('/api/users/me', { auth: true }),
    updateMe: (bio, avatarUrl) =>
        api('/api/users/me', { method: 'PUT', auth: true, body: { bio, avatarUrl } }),
    addFavorite: artistMusicBrainzId =>
        api('/api/users/me/favorites', { method: 'POST', auth: true, body: { artistMusicBrainzId } }),
    removeFavorite: mbid =>
        api(`/api/users/me/favorites/${mbid}`, { method: 'DELETE', auth: true })
};
