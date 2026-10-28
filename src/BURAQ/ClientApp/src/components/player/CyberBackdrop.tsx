import { FC } from 'react'

interface CyberBackdropProps {
  /** Key art behind the scene. Absolute /assets path (set inline: CSS url() breaks under the /admin/ base). */
  art?: string
  /** Bottom-corner readouts. */
  left?: string
  right?: string
  /** Still and quiet: no falling data, beam or glitches (used while the screen is being shared). */
  calm?: boolean
}

/**
 * The living cyber environment behind every player page: key art, red haze, a drifting hex mesh,
 * falling data streams, a running grid floor, a sweeping scan beam, glitch bands, scanlines and
 * HUD corners. Purely decorative and fixed behind the content; styles live in styles/player/fx.css.
 */
export const CyberBackdrop: FC<CyberBackdropProps> = ({
  art = '/assets/zarrar/hacker-bg-red.jpg',
  left = 'SYS://ZARRAR // NODE-01',
  right = '● UPLINK SECURE',
  calm = false,
}) => (
  <div className={calm ? 'zr-backdrop zr-calm' : 'zr-backdrop'} aria-hidden="true">
    <div className="zr-bd-art" style={{ backgroundImage: `url('${art}')` }} />
    <div className="zr-bd-haze" />
    <div className="zr-bd-hex" />
    <div className="zr-bd-rain">
      <i />
      <i />
      <i />
    </div>
    <div className="zr-bd-grid" />
    <div className="zr-bd-beam" />
    <div className="zr-bd-glitch" />
    <div className="zr-bd-scan" />
    <div className="zr-bd-vignette" />
    <div className="zr-hud">
      <span className="tl" />
      <span className="tr" />
      <span className="bl" />
      <span className="br" />
      <b>{left}</b>
      <b>{right}</b>
    </div>
  </div>
)
