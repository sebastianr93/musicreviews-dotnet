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
    const initial = (user?.userName ?? '?').charAt(0).toUpperCase();

    const letra = el('span', {
        class: 'avatar avatar--letter',
        style: `width:${size}px;height:${size}px;line-height:${size}px`
    }, initial);

    if (!user?.avatarUrl) {
        return letra;
    }

    // Aca si se puede reemplazar en vez de borrar: el avatar tiene un respaldo con
    // sentido —la inicial— y el nodo ya esta en el documento cuando la imagen falla,
    // porque a diferencia de las portadas esta URL la guardamos nosotros y solo deja de
    // resolver si el archivo desaparecio.
    const img = el('img', {
        class: 'avatar',
        src: user.avatarUrl,
        alt: '',
        width: size,
        height: size,
        loading: 'lazy'
    });

    img.addEventListener('error', () => img.replaceWith(letra));

    return img;
}

/**
 * Portada de un album o una cancion.
 *
 * Siempre devuelve el recuadro, y la imagen va adentro. Es a proposito: las URL de
 * Cover Art Archive se construyen a partir del MBID sin comprobar que existan —hacerlo
 * serian cincuenta requests a un tercero para pintar una grilla—, asi que una parte de
 * ellas va a dar 404. Con el recuadro afuera, la imagen que falla se borra sola y queda
 * el hueco del tamanio correcto: la grilla no se descuadra ni salta.
 *
 * Al reves —reemplazar la imagen por un recuadro cuando falla— no funciona: el error
 * puede dispararse antes de que el nodo este en el documento, y ahi no hay nada que
 * reemplazar.
 */
export function cover(url, className = 'cover cover--small') {
    const box = el('span', { class: `${className} cover-box` });

    if (url) {
        const img = el('img', { class: 'cover-box__img', src: url, alt: '', loading: 'lazy' });

        img.addEventListener('error', () => img.remove());
        box.append(img);
    }

    return box;
}

export function link(href, text, props = {}) {
    return el('a', { href, ...props }, text);
}

/**
 * Trazados de los iconos. Se dibujan con `stroke`, sin relleno, para que hereden el
 * color del texto que los rodea con `currentColor` y no haya que mantener una version
 * por tema.
 */
const ICONS = {
    // Campana.
    bell: 'M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9M13.73 21a2 2 0 0 1-3.46 0'
};

/**
 * Icono SVG inline.
 *
 * No se arma con `el`: en un elemento SVG, `className` es un objeto de solo lectura y
 * asignarlo lanza. Ademas los elementos SVG necesitan su propio namespace —creados con
 * `createElement` a secas el navegador los trata como HTML desconocido y no dibuja nada—.
 *
 * Va inline y no como `<img>` para que herede el color y no cueste una request aparte.
 */
export function icon(name, { size = 20, label = null, className = 'icon' } = {}) {
    const NS = 'http://www.w3.org/2000/svg';
    const svg = document.createElementNS(NS, 'svg');

    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('width', size);
    svg.setAttribute('height', size);
    svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', '2');
    svg.setAttribute('stroke-linecap', 'round');
    svg.setAttribute('stroke-linejoin', 'round');
    svg.setAttribute('class', className);

    // Un icono sin texto al lado es invisible para un lector de pantalla: o lleva
    // nombre accesible, o se marca como decorativo para que no lo anuncie vacio.
    if (label) {
        svg.setAttribute('role', 'img');
        svg.setAttribute('aria-label', label);
    } else {
        svg.setAttribute('aria-hidden', 'true');
    }

    const path = document.createElementNS(NS, 'path');
    path.setAttribute('d', ICONS[name] ?? '');
    svg.append(path);

    return svg;
}

/**
 * Bloque de error reutilizable: entiende ApiError y muestra los mensajes de validacion.
 * Ante un 503 ofrece reintentar, porque es un fallo temporal de un servicio externo
 * y volver a intentar es la accion correcta, no recargar la pagina entera.
 */
export function errorBox(error, onRetry) {
    const messages = error?.validationMessages?.length
        ? error.validationMessages
        : [error?.message ?? 'Ocurrio un error.'];

    const isTemporary = error?.status === 503;

    return el('div', { class: `alert ${isTemporary ? 'alert--warn' : 'alert--error'}` },
        messages.map(message => el('p', {}, message)),
        isTemporary && onRetry
            ? el('button', { class: 'btn btn--ghost', onClick: onRetry }, 'Reintentar')
            : null);
}

export function spinner(text = 'Cargando...') {
    return el('p', { class: 'muted' }, text);
}

export function empty(text) {
    return el('p', { class: 'muted empty' }, text);
}
