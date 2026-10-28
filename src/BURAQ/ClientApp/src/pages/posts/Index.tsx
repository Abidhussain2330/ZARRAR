import dayjs from 'dayjs'
import { FC, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router'
import { Empty, ErrorState, GameStatusBadge, Loading, PageHead, arena } from '@Components/player/Arena'
import { PlayerShell } from '@Components/player/PlayerShell'
import { OnceSWRConfig } from '@Hooks/useConfig'
import { usePageTitle } from '@Hooks/usePageTitle'
import api, { BasicGameInfoModel, PostInfoModel } from '@Api'
import { SPONSOR_TAGS, isSponsorPost } from '@Utils/player/posts'
import classes from '@Styles/player/Home.module.css'

const PAGE_SIZE = 8

/**
 * Promotes the first image in the post body to card artwork.
 *
 * A convention, not a schema field — see the note on SPONSOR_TAGS. It degrades gracefully: a post
 * with no image simply renders without one rather than breaking the card.
 */
const coverFrom = (source: string) => {
  const md = /!\[[^\]]*\]\(([^)\s]+)/.exec(source)
  if (md) return md[1]
  const html = /<img[^>]+src=["']([^"']+)["']/i.exec(source)
  return html ? html[1] : null
}

/** Strips Markdown syntax so a card blurb reads as prose rather than source. */
const plainText = (source: string) =>
  source
    .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/[#*_>`]/g, '')
    .trim()

const visibleTags = (post: PostInfoModel) =>
  (post.tags ?? []).filter((tag) => !SPONSOR_TAGS.includes(tag.trim().toLowerCase()))

/* ------------------------------------------------------------------ cards */

const SponsorCard: FC<{ post: PostInfoModel }> = ({ post }) => {
  // The list endpoint returns summary only; full content is fetched on the detail page.
  const cover = coverFrom(post.summary)
  const blurb = plainText(post.summary)

  return (
    <article className={classes.sponsorCard}>
      <span className={classes.sponsorRail} aria-hidden="true" />

      <div className={classes.sponsorMedia}>
        {cover ? (
          <img src={cover} alt="" loading="lazy" />
        ) : (
          <span className={classes.sponsorMark} aria-hidden="true">
            {post.title.slice(0, 2).toUpperCase()}
          </span>
        )}
      </div>

      <div className={classes.sponsorBody}>
        <span className={classes.sponsorTag}>SPONSOR</span>
        <h3 className={classes.sponsorTitle}>
          <Link to={`/posts/${post.id}`} className={classes.plainLink}>
            {post.title}
          </Link>
        </h3>
        {blurb && <p className={classes.sponsorBlurb}>{blurb}</p>}
        <Link to={`/posts/${post.id}`} className={classes.sponsorMore}>
          Read more →
        </Link>
      </div>
    </article>
  )
}

const PostCard: FC<{ post: PostInfoModel }> = ({ post }) => {
  const cover = coverFrom(post.summary)
  const blurb = plainText(post.summary)
  const tags = visibleTags(post)

  return (
    <article className={`${classes.postCard} ${post.isPinned ? classes.postPinned : ''}`}>
      {cover && (
        <Link to={`/posts/${post.id}`} className={classes.postCover} tabIndex={-1} aria-hidden="true">
          <img src={cover} alt="" loading="lazy" />
        </Link>
      )}

      <div className={classes.postBody}>
        <div className={classes.postTagRow}>
          {post.isPinned && <span className={classes.pinTag}>PINNED</span>}
          {tags.map((tag) => (
            <span key={tag} className={classes.tag}>
              {tag}
            </span>
          ))}
        </div>

        <h2 className={classes.postTitle}>
          <Link to={`/posts/${post.id}`} className={classes.postLink}>
            {post.title}
          </Link>
        </h2>

        {blurb && <p className={classes.postBlurb}>{blurb}</p>}

        <p className={classes.postMeta}>
          {post.authorName ?? 'ZARRAR'} · {dayjs(post.time).format('D MMM YYYY')}
        </p>
      </div>
    </article>
  )
}

const EventCard: FC<{ game: BasicGameInfoModel }> = ({ game }) => (
  <Link to={`/games/${game.id}`} className={`${classes.eventCard} bqFocusRing`}>
    <div className={classes.eventPoster}>
      {game.poster ? (
        <img src={game.poster} alt="" loading="lazy" />
      ) : (
        <span className={classes.eventPosterFallback} aria-hidden="true">
          {(game.title ?? '?').slice(0, 2).toUpperCase()}
        </span>
      )}
    </div>
    <div className={classes.eventBody}>
      <div className={classes.eventTop}>
        <h3 className={classes.eventTitle}>{game.title}</h3>
        <GameStatusBadge start={dayjs(game.start).toISOString()} end={dayjs(game.end).toISOString()} />
      </div>
      <p className={classes.eventMeta}>
        {dayjs(game.start).format('D MMM, HH:mm')} → {dayjs(game.end).format('D MMM, HH:mm')}
      </p>
    </div>
  </Link>
)

/* ------------------------------------------------------------------- page */

const Home: FC = () => {
  const { t } = useTranslation()
  const [page, setPage] = useState(1)

  const { data: posts, error: postsError, mutate: retryPosts } = api.info.useInfoGetPosts(OnceSWRConfig)
  const { data: games, error: gamesError } = api.game.useGameRecentGames({ limit: 6 }, OnceSWRConfig)

  usePageTitle(t('post.title.index'))

  const { sponsors, announcements } = useMemo(() => {
    const all = [...(posts ?? [])].sort((a, b) => {
      if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1
      return b.time - a.time
    })
    return {
      sponsors: all.filter((p) => isSponsorPost(p.tags)),
      announcements: all.filter((p) => !isSponsorPost(p.tags)),
    }
  }, [posts])

  const visible = announcements.slice(0, page * PAGE_SIZE)
  const hasMore = visible.length < announcements.length

  const liveOrUpcoming = useMemo(() => {
    if (!games) return []
    const now = dayjs()
    return games.filter((g) => now.isBefore(dayjs(g.end)))
  }, [games])

  return (
    <PlayerShell>
      <PageHead
        eyebrow="ZARRAR / BRIEFING"
        title="What's happening"
        subtitle="Announcements, partners, and the events you can join."
      />

      {postsError ? (
        <ErrorState
          title="Couldn't load the briefing"
          body="The server didn't answer. Check your connection and try again."
          action={
            <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={() => retryPosts()}>
              Retry
            </button>
          }
        />
      ) : !posts ? (
        <Loading label="Loading briefing…" />
      ) : (
        <>
          {/* Sponsors sit above the feed in their own band — full-width, framed and
              horizontal, so they never read as just another announcement in the list. */}
          {sponsors.length > 0 && (
            <section className={classes.sponsorBand} aria-label="Partners">
              <div className={classes.bandHead}>
                <h2 className={classes.bandTitle}>Partners</h2>
                <span className={classes.bandRule} aria-hidden="true" />
              </div>
              <div className={classes.sponsorList}>
                {sponsors.map((post) => (
                  <SponsorCard key={post.id} post={post} />
                ))}
              </div>
            </section>
          )}

          <div className={classes.layout}>
            <section className={classes.feed} aria-label="Announcements">
              <div className={classes.bandHead}>
                <h2 className={classes.bandTitle}>Announcements</h2>
                <span className={classes.bandRule} aria-hidden="true" />
              </div>

              {announcements.length === 0 ? (
                <Empty
                  title="Nothing posted yet"
                  body="Announcements will show up here once organisers publish them."
                />
              ) : (
                <>
                  {visible.map((post) => (
                    <PostCard key={post.id} post={post} />
                  ))}
                  {hasMore && (
                    <button
                      type="button"
                      className={`${arena.btn} ${arena.btnGhost} ${classes.more} bqFocusRing`}
                      onClick={() => setPage((p) => p + 1)}
                    >
                      Show older posts
                    </button>
                  )}
                </>
              )}
            </section>

            <aside className={classes.rail} aria-label="Events">
              <div className={classes.railHead}>
                <h2 className={classes.railTitle}>Events</h2>
                <Link to="/games" className={`${classes.railAll} bqFocusRing`}>
                  See all
                </Link>
              </div>

              {gamesError ? (
                <p className={classes.railNote}>Events are unavailable right now.</p>
              ) : !games ? (
                <div className={classes.railSkeletons} aria-hidden="true">
                  <span className={arena.skeleton} />
                  <span className={arena.skeleton} />
                </div>
              ) : liveOrUpcoming.length === 0 ? (
                <p className={classes.railNote}>No events scheduled. Check back soon.</p>
              ) : (
                <div className={classes.railList}>
                  {liveOrUpcoming.map((game) => (
                    <EventCard key={game.id} game={game} />
                  ))}
                </div>
              )}
            </aside>
          </div>
        </>
      )}
    </PlayerShell>
  )
}

export default Home
