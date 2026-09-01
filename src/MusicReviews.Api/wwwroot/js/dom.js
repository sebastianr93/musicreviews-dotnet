// Helpers de construccion de DOM.
//
// Todo se arma con createElement y textContent, nunca con innerHTML interpolado.
// No es preferencia de estilo: el sitio renderiza texto escrito por otros usuarios
// (reviews, comentarios, bios) y concatenar eso en innerHTML es XSS almacenado
// servido a todos los visitantes. Con textContent el navegador nunca interpreta
// el contenido como markup.

/**
 * Crea un elemento. Las props que empiezan con "on" se registran como listeners;
 * el resto se asignan como propiedades o atributos segun corresponda.
 * Los hijos string se insertan como texto, nunca como HTML.
 */
export function el(tag, props = {}, ...children) {
    const node = document.createElement(tag);

    for (const [key, value] of Object.entries(props)) {
        if (value === null || value === undefined || value === false) {
            continue;
        }

        if (key.startsWith('on') && typeof value === 'function') {
            node.addEventListener(key.slice(2).toLowerCase(), value);
        } else if (key === 'class') {
            node.className = value;
        } else if (key === 'dataset') {
            Object.assign(node.dataset, value);
        } else if (key in node) {
            node[key] = value;
        } else {
            node.setAttribute(key, value);
        }
    }

    append(node, children);

    return node;
}

function append(node, children) {
    for (const child of children.flat(Infinity)) {
        if (child === null || child === undefined || child === false) {
            continue;
        }

        node.append(child instanceof Node ? child : document.createTextNode(String(child)));
    }
}

export function clear(node) {
    node.replaceChildren();
    return node;
}

/** Fecha corta y legible; los timestamps vienen en UTC desde la Api. */
export function formatDate(value) {
    if (!value) {
        return '';
    }

    return new Date(value).toLocaleDateString('es-AR', {
        day: '2-digit',
        month: 'short',
        year: 'numeric'
    });
}

/** Clase de color segun el puntaje, para que la escala 0-100 se lea de un vistazo. */
export function scoreClass(score) {
    if (score >= 80) return 'score score--high';
    if (score >= 50) return 'score score--mid';
    return 'score score--low';
}

export function avatar(user, size = 32) {
    if (user?.avatarUrl) {
        return el('img', {
            class: 'avatar',
            src: user.avatarUrl,
            alt: '',
            width: size,
            height: size,
            loading: 'lazy'
        });
    }

    const initial = (user?.userName ?? '?').charAt(0).toUpperCase();

    return el('span', {
        class: 'avatar avatar--letter',
        style: `width:${size}px;height:${size}px;line-height:${size}px`
    }, initial);
}

export function link(href, text, props = {}) {
    return el('a', { href, ...props }, text);
}

/** Bloque de error reutilizable: entiende ApiError y muestra los mensajes de validacion. */
export function errorBox(error) {
    const messages = error?.validationMessages?.length
        ? error.validationMessages
        : [error?.message ?? 'Ocurrio un error.'];

    return el('div', { class: 'alert alert--error' },
        messages.map(message => el('p', {}, message)));
}

export function spinner(text = 'Cargando...') {
    return el('p', { class: 'muted' }, text);
}

export function empty(text) {
    return el('p', { class: 'muted empty' }, text);
}
