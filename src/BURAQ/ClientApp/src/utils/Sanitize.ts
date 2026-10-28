import DOMPurify from 'dompurify'

// `#text` is mandatory: given an explicit tag allow-list, DOMPurify drops every node type not on it
/* prettier-ignore */
const MARKDOWN_TAGS = [
  '#text',
  'a', 'abbr', 'b', 'blockquote', 'br', 'caption', 'cite', 'code', 'col', 'colgroup', 'dd', 'del',
  'details', 'div', 'dl', 'dt', 'em', 'figcaption', 'figure', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
  'hr', 'i', 'img', 'input', 'ins', 'kbd', 'li', 'mark', 'ol', 'p', 'pre', 'q', 'rp', 'rt', 'ruby',
  's', 'samp', 'section', 'small', 'span', 'strong', 'sub', 'summary', 'sup', 'table', 'tbody',
  'td', 'tfoot', 'th', 'thead', 'tr', 'u', 'ul', 'var', 'wbr',
]

// KaTeX renders `htmlAndMathml`, so both the MathML mirror and the SVG surds have to survive
/* prettier-ignore */
const KATEX_TAGS = [
  'annotation', 'math', 'menclose', 'merror', 'mfrac', 'mi', 'mlabeledtr', 'mn', 'mo', 'mover',
  'mpadded', 'mphantom', 'mroot', 'mrow', 'ms', 'mspace', 'msqrt', 'mstyle', 'msub', 'msubsup',
  'msup', 'mtable', 'mtd', 'mtext', 'mtr', 'munder', 'munderover', 'semantics',
  'circle', 'ellipse', 'g', 'line', 'path', 'polygon', 'polyline', 'rect', 'svg',
]

// every `on*` handler is excluded by construction; `id`/`name` are withheld because they clobber
// the DOM, while `class`/`style` are load-bearing for Shiki highlighting and KaTeX layout
/* prettier-ignore */
const ALLOWED_ATTR = [
  'align', 'alt', 'checked', 'class', 'colspan', 'dir', 'disabled', 'height', 'href', 'lang',
  'loading', 'open', 'rel', 'reversed', 'role', 'rowspan', 'scope', 'span', 'src', 'start',
  'style', 'tabindex', 'target', 'title', 'type', 'width',
  'accent', 'accentunder', 'close', 'columnalign', 'columnlines', 'columnspacing', 'depth',
  'display', 'displaystyle', 'encoding', 'fence', 'largeop', 'linethickness', 'lspace',
  'mathbackground', 'mathcolor', 'mathsize', 'mathvariant', 'maxsize', 'minsize', 'movablelimits',
  'notation', 'rowlines', 'rowspacing', 'rspace', 'scriptlevel', 'separator', 'stretchy',
  'symmetric', 'voffset', 'xmlns',
  'cx', 'cy', 'd', 'fill', 'fill-rule', 'points', 'preserveAspectRatio', 'r', 'rx', 'ry', 'stroke',
  'stroke-linecap', 'stroke-linejoin', 'stroke-width', 'transform', 'viewBox', 'x', 'x1', 'x2',
  'y', 'y1', 'y2',
]

// relative refs, http(s) and mailto only; the trailing branches keep plain attribute values such as
// `center` or an SVG path readable, while any `scheme:` outside the list is dropped
const ALLOWED_URI_REGEXP =
  /^(?:(?:https?|mailto):|data:image\/(?:png|jpe?g|gif|webp|avif|bmp|x-icon)[,;]|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i

const SAME_CONTEXT_TARGETS = ['', '_self', '_top', '_parent']

// DOMPurify exempts `src`/`href` on its DATA_URI_TAGS (img, audio, video, source, track) from
// ALLOWED_URI_REGEXP entirely, so the raster allow-list above does not reach them and any
// `data:` payload — `image/svg+xml` among them — survives on an `<img>`. Re-apply the allow-list.
const ALLOWED_DATA_URI = /^data:image\/(?:png|jpe?g|gif|webp|avif|bmp|x-icon)[,;]/i

const URI_ATTRIBUTES = ['src', 'href']

DOMPurify.addHook('afterSanitizeAttributes', (node) => {
  // a link that opens its own browsing context can otherwise reach back through `window.opener`
  if (node instanceof HTMLAnchorElement && !SAME_CONTEXT_TARGETS.includes(node.target)) {
    node.setAttribute('rel', 'noopener noreferrer')
  }

  if (!(node instanceof Element)) return

  for (const attribute of URI_ATTRIBUTES) {
    const value = node.getAttribute(attribute)

    if (value && /^\s*data:/i.test(value) && !ALLOWED_DATA_URI.test(value.trim())) {
      node.removeAttribute(attribute)
    }
  }
})

export const sanitizeHtml = (html: string): string =>
  DOMPurify.sanitize(html, {
    ALLOWED_TAGS: [...MARKDOWN_TAGS, ...KATEX_TAGS],
    ALLOWED_ATTR,
    ALLOWED_URI_REGEXP,
    FORBID_TAGS: ['script', 'style', 'iframe', 'object', 'embed', 'form'],
    FORBID_ATTR: ['action', 'formaction', 'ping', 'srcdoc', 'xlink:href'],
    ALLOW_ARIA_ATTR: true,
    ALLOW_DATA_ATTR: true,
    SANITIZE_DOM: true,
  })
