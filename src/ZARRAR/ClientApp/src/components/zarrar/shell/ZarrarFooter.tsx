import { FC } from 'react'
import { ZarrarLogo } from '@Components/zarrar/brand/ZarrarMark'
import { Copyright } from '@Components/Copyright'
import { FooterRender } from '@Components/FooterRender'
import { useConfig } from '@Hooks/useConfig'
import classes from '@Styles/zarrar/ZarrarFooter.module.css'

/**
 * Shell footer.
 *
 * `Copyright` is rendered as the original component rather than re-typed: it is
 * a restricted-licence component carrying the upstream ZARRAR attribution, which
 * this deployment is required to keep intact. `FooterRender` is the operator's
 * own configured footer markdown and is the only other link source there is.
 */
export const ZarrarFooter: FC = () => {
  const { config } = useConfig()
  const currentYear = new Date().getFullYear()

  return (
    <footer className={classes.footer}>
      <div className={classes.inner}>
        <div className={classes.brand}>
          <ZarrarLogo variant="horizontal" size={40} />
          <p className={classes.brandLine}>ZARRAR — 1v1 Showdown CTF</p>
          <p className={classes.brandLine}>© {currentYear} ZARRAR by The Arzens</p>
        </div>

        <div className={classes.legal}>
          <Copyright />
          {config.footerInfo && (
            <div className={classes.links}>
              <FooterRender source={config.footerInfo} />
            </div>
          )}
        </div>
      </div>
    </footer>
  )
}
