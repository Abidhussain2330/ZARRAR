/**
 * Full-screen FLAG CAPTURED / ACCESS DENIED hit on the player's own page after a submission.
 *
 * Drawn straight onto <body> rather than through React: a correct flag closes the challenge
 * modal, and the hit has to outlive it. It also shows up on the auditorium screen through the
 * player's live feed. Styles are .zr-hit* in styles/player/fx.css.
 */
export const flagHit = (kind: 'ok' | 'bad', detail?: string | null) => {
  if (typeof document === 'undefined') return

  document.querySelectorAll('.zr-hit').forEach((node) => node.remove())

  const reduceMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
  const life = kind === 'ok' ? 1900 : 1500

  const root = document.createElement('div')
  root.className = `zr-hit ${kind === 'ok' ? 'zr-hit-ok' : 'zr-hit-bad'}`
  root.style.setProperty('--life', `${life}ms`)
  root.setAttribute('role', 'status')
  root.setAttribute('aria-live', 'assertive')

  const band = document.createElement('div')
  band.className = 'zr-hit-band'

  const title = document.createElement('div')
  title.className = 'zr-hit-title'
  title.textContent = kind === 'ok' ? 'FLAG CAPTURED' : 'ACCESS DENIED'

  const sub = document.createElement('div')
  sub.className = 'zr-hit-sub'
  // textContent, never innerHTML: the detail can be a challenge title.
  sub.textContent = detail ?? (kind === 'ok' ? 'Target taken' : 'Wrong flag')

  band.append(title, sub)
  root.append(band)
  document.body.append(root)

  window.setTimeout(() => root.remove(), reduceMotion ? 900 : life)
}
