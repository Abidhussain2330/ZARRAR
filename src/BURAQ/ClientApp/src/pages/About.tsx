import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { PageHead } from '@Components/player/Arena'
import { PlayerShell } from '@Components/player/PlayerShell'
import { ValidatedRepoMeta } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import classes from '@Styles/player/About.module.css'

interface Item {
  title: string
  description: string
}

const CAPABILITIES: Item[] = [
  {
    title: 'Jeopardy & live events',
    description:
      'Run timed competitions with categorised challenges, difficulty tiers, hints, attachments and first-blood bonuses, with separate divisions for different audiences.',
  },
  {
    title: 'Isolated challenge environments',
    description:
      'Container-backed challenges start on demand for each team in an isolated, resource-limited sandbox, and shared services are brokered through a controlled gateway.',
  },
  {
    title: 'Personalised challenges',
    description:
      'Dynamic flags and per-team challenge variants mean every team solves its own instance, so a leaked flag is useless to anyone else.',
  },
  {
    title: 'Integrity monitoring',
    description:
      'A built-in security operations layer correlates submissions and behaviour to flag suspected flag sharing and collusion for organiser review.',
  },
  {
    title: 'Live scoreboard',
    description:
      'Rankings, category breakdowns and solve timelines update in real time as teams score, for players and spectators alike.',
  },
  {
    title: 'Teams & notices',
    description:
      'Players form teams with invite codes, and organisers push announcements and challenge updates that arrive instantly, without a refresh.',
  },
  {
    title: 'Practice exercises',
    description:
      'Self-paced exercises stay open outside of events, so learners can build skills before they compete.',
  },
  {
    title: 'Challenge author programme',
    description:
      'Approved challenge developers submit packaged challenges that organisers review before they reach an event.',
  },
  {
    title: 'Open to integrate',
    description:
      'Scoped, expiring API tokens let organisers automate event operations, and the interface is available in eleven languages.',
  },
]

const AUDIENCES: Item[] = [
  {
    title: 'Universities & academies',
    description:
      'Hands-on labs and internal competitions that turn security coursework into practical, measurable skill.',
  },
  {
    title: 'Competition organisers',
    description:
      'National and inter-institutional CTFs with fair scoring, isolated infrastructure and integrity checks built in.',
  },
  {
    title: 'Security teams',
    description:
      'Red, blue and purple team exercises that rehearse attack, detection and response in a safe, contained range.',
  },
]

const PRINCIPLES: string[] = [
  'Every team gets its own isolated environment, and no team can reach another’s.',
  'Flags are unique per team wherever the challenge allows it.',
  'Suspicious activity is reviewed by a person before any action is taken.',
  'Challenges target the platform’s own sandboxes only — never third-party systems.',
]

const About: FC = () => {
  const { valid, rawTag: tag, sha, buildTime } = ValidatedRepoMeta()
  const { t } = useTranslation()
  const shortSha = `#${sha.substring(0, 8)}`
  const version = valid ? `${tag}${shortSha}` : `v${__BURAQ_VERSION__}`
  const year = new Date().getFullYear()

  usePageTitle(t('common.title.about'))

  return (
    <PlayerShell>
      <section className={classes.hero}>
        <img src="/assets/zarrar/zarrar-logo.png" alt="" className={classes.logo} />
        <h1 className={classes.title}>ZARRAR</h1>
        <p className={classes.tagline}>The 1v1 showdown capture-the-flag by THE ARZENS</p>
        <p className={classes.slogan}>
          &gt;&nbsp;{t('common.content.about.slogan')}
          <span className={classes.caret} aria-hidden="true">
            _
          </span>
        </p>
        <p className={classes.blurb}>
          Two hackers. One challenge at a time. Every move live on the big screen.
        </p>
      </section>

      <section className={classes.section}>
        <PageHead eyebrow="ZARRAR / MISSION" title="Why ZARRAR exists" />
        <div className={classes.prose}>
          <p>
            ZARRAR is the 1v1 showdown capture-the-flag by THE ARZENS, built on the BURAQ platform. Two players race
            the same challenge, the first valid flag takes it, and the whole auditorium watches both screens live.
          </p>
          <p>
            Our goal is simple: make practical cyber security training accessible, fair and rigorous. Every challenge
            runs in an isolated environment, every team is scored the same way, and every event is built to reward skill
            rather than shortcuts.
          </p>
        </div>
      </section>

      <section className={classes.section}>
        <PageHead eyebrow="ZARRAR / PLATFORM" title="What the platform does" />
        <div className={classes.grid}>
          {CAPABILITIES.map((item) => (
            <article key={item.title} className={classes.card}>
              <h2 className={classes.cardTitle}>{item.title}</h2>
              <p className={classes.cardBody}>{item.description}</p>
            </article>
          ))}
        </div>
      </section>

      <section className={classes.section}>
        <PageHead eyebrow="ZARRAR / AUDIENCE" title="Who it is for" />
        <div className={classes.grid}>
          {AUDIENCES.map((item) => (
            <article key={item.title} className={classes.card}>
              <h2 className={classes.cardTitle}>{item.title}</h2>
              <p className={classes.cardBody}>{item.description}</p>
            </article>
          ))}
        </div>
      </section>

      <section className={classes.section}>
        <PageHead eyebrow="ZARRAR / FAIR PLAY" title="How we keep it fair" />
        <ul className={classes.principles}>
          {PRINCIPLES.map((principle) => (
            <li key={principle}>{principle}</li>
          ))}
        </ul>
      </section>

      <section className={classes.version}>
        <span className={classes.versionBadge}>{`ZARRAR ${version}`}</span>
        {valid && <p className={classes.versionNote}>{`Built ${buildTime.format('YYYY-MM-DD HH:mm')}`}</p>}
      </section>

      <footer className={classes.legal}>
        ZARRAR (built on BURAQ) is developed and maintained by THE ARZENS. &copy; 2025&ndash;{year} THE ARZENS. All rights reserved.
        Licensed under the{' '}
        <a href="/LICENSE.txt" target="_blank" rel="noreferrer" className={classes.legalLink}>
          Custom THE ARZENS License
        </a>
        . Open-source components used in BURAQ remain under their own licences and notices.
      </footer>
    </PlayerShell>
  )
}

export default About
