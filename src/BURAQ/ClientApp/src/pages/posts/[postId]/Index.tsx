import dayjs from 'dayjs'
import { FC, useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import { Markdown } from '@Components/MarkdownRenderer'
import { ErrorState, Loading, arena } from '@Components/player/Arena'
import { PlayerShell } from '@Components/player/PlayerShell'
import { RequireRole } from '@Components/WithRole'
import { useLanguage } from '@Utils/I18n'
import { usePageTitle } from '@Hooks/usePageTitle'
import { useUserRole } from '@Hooks/useUser'
import api, { Role } from '@Api'
import { SPONSOR_TAGS, isSponsorPost } from '@Utils/player/posts'
import classes from '@Styles/player/PostDetail.module.css'

const Post: FC = () => {
  const { postId } = useParams()
  const navigate = useNavigate()

  const { t } = useTranslation()
  const { role } = useUserRole()
  const { locale } = useLanguage()

  // Post ids are 8 characters; anything else can't resolve, so don't ask the API.
  const validId = postId?.length === 8

  useEffect(() => {
    if (!validId) navigate('/404')
  }, [validId, navigate])

  const {
    data: post,
    error,
    mutate: retry,
  } = api.info.useInfoGetPost(
    postId ?? '',
    { refreshInterval: 0, revalidateOnFocus: false },
    validId
  )

  usePageTitle(post?.title ?? t('post.title.index'))

  return (
    <PlayerShell>
      <Link to="/posts" className={`${classes.back} bqFocusRing`}>
        ← All announcements
      </Link>

      {error ? (
        <ErrorState
          title="Couldn't load this post"
          body="It may have been removed, or the server didn't answer."
          action={
            <button type="button" className={`${arena.btn} ${arena.btnGhost} bqFocusRing`} onClick={() => retry()}>
              Retry
            </button>
          }
        />
      ) : !post ? (
        <Loading label="Loading post…" />
      ) : (
        <article className={classes.article}>
          <header className={classes.header}>
            <div className={classes.tagRow}>
              {/* A sponsor feature keeps its identity on the detail page too — arriving from the
                  Partners band and landing on something that looks like a plain announcement
                  would break the thread. */}
              {isSponsorPost(post.tags) && <span className={classes.sponsorTag}>SPONSOR</span>}
              {post.isPinned && <span className={classes.pinTag}>PINNED</span>}
              {post.tags?.filter((tag) => !SPONSOR_TAGS.includes(tag.trim().toLowerCase())).map((tag) => (
                <span key={tag} className={classes.tag}>
                  {tag}
                </span>
              ))}
            </div>

            <h1 className={classes.title}>{post.title}</h1>

            <div className={classes.byline}>
              {post.authorAvatar ? (
                <img className={classes.avatar} src={post.authorAvatar} alt="" />
              ) : (
                <span className={classes.avatarFallback} aria-hidden="true">
                  {(post.authorName ?? 'B').slice(0, 1).toUpperCase()}
                </span>
              )}
              <div>
                <p className={classes.author}>{post.authorName ?? 'BURAQ'}</p>
                <p className={classes.date}>{dayjs(post.time).locale(locale).format('LL')}</p>
              </div>

              {RequireRole(Role.Admin, role) && (
                <Link
                  to={`/admin/notices/${post.id}`}
                  className={`${arena.btn} ${arena.btnGhost} ${classes.editBtn} bqFocusRing`}
                >
                  Edit
                </Link>
              )}
            </div>
          </header>

          <div className={classes.content}>
            <Markdown source={post.content ?? ''} />
          </div>
        </article>
      )}
    </PlayerShell>
  )
}

export default Post
