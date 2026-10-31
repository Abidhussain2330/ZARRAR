import { Button, SegmentedControl, Switch, TagsInput, Textarea, TextInput } from '@mantine/core'
import { useHotkeys } from '@mantine/hooks'
import { mdiArrowLeft, mdiContentSaveOutline, mdiDeleteOutline, mdiEyeOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, ReactNode, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router'
import {
  AdminFormGrid,
  AdminFormWide,
  AdminLayout,
  AdminMonoValue,
  AdminSection,
  AdminStatusBadge,
  openAdminConfirm,
  showAdminSuccess,
} from '@Components/admin/shell'
import { ErrorState, LoadingState, Panel } from '@Components/zarrar'
import { Markdown } from '@Components/MarkdownRenderer'
import { showErrorMsg } from '@Utils/Shared'
import { isSponsorPost, SPONSOR_TAGS } from '@Utils/player/posts'
import api, { PostEditModel } from '@Api'
import classes from '@Styles/admin/AdminPostEditor.module.css'

/** Mirrors `Limits.MaxPostTitleLength` on the backend. */
const MAX_TITLE_LENGTH = 50

/** Post ids are the first 8 hex chars of a GUID / SHA-256 (see `Post.cs`). */
const POST_ID_PATTERN = /^[0-9a-zA-Z]{8}$/

type ViewMode = 'write' | 'preview' | 'split'

interface Draft {
  title: string
  summary: string
  content: string
  tags: string[]
}

const EMPTY_DRAFT: Draft = { title: '', summary: '', content: '', tags: [] }

const normaliseTags = (tags: string[]) =>
  Array.from(new Set(tags.map((tag) => tag.trim()).filter(Boolean)))

const sameTags = (a: string[], b: string[]) => {
  if (a.length !== b.length) return false
  const sortedB = [...b].sort()
  return [...a].sort().every((tag, index) => tag === sortedB[index])
}

const formatTime = (value?: number) => (value ? new Date(value).toLocaleString() : '—')

interface MarkdownFieldProps {
  label: string
  hint: ReactNode
  value: string
  onChange: (value: string) => void
  disabled: boolean
  mode: ViewMode
  minRows: number
  maxRows: number
  compact?: boolean
  emptyLabel: string
  headerExtra?: ReactNode
}

/**
 * Markdown textarea with a live preview that renders through the same
 * `Markdown` component the player pages use, so what the admin sees is what
 * players get.
 */
const MarkdownField: FC<MarkdownFieldProps> = ({
  label,
  hint,
  value,
  onChange,
  disabled,
  mode,
  minRows,
  maxRows,
  compact,
  emptyLabel,
  headerExtra,
}) => {
  const editor = (
    <Textarea
      className={classes.editor}
      aria-label={label}
      autosize
      minRows={minRows}
      maxRows={maxRows}
      disabled={disabled}
      value={value}
      onChange={(e) => onChange(e.currentTarget.value)}
    />
  )

  const preview = (
    <div className={classes.preview} data-compact={compact || undefined} aria-label={`${label} preview`}>
      {value.trim() ? <Markdown source={value} /> : <p className={classes.previewEmpty}>{emptyLabel}</p>}
    </div>
  )

  return (
    <div>
      <div className={classes.fieldHead}>
        <div>
          <p className={classes.fieldLabel}>{label}</p>
          <p className={classes.fieldHint}>{hint}</p>
        </div>
        {headerExtra}
      </div>
      {mode === 'write' && editor}
      {mode === 'preview' && preview}
      {mode === 'split' && (
        <div className={classes.split}>
          {editor}
          {preview}
        </div>
      )}
    </div>
  )
}

/**
 * Admin notice editor — `/admin/notices/new` and `/admin/notices/:postId`.
 *
 * Covers everything the backend exposes for posts:
 *  - create (`POST /api/edit/posts`), update (`PUT /api/edit/posts/{id}`),
 *    delete (`DELETE /api/edit/posts/{id}`);
 *  - title (max 50), summary, content (both Markdown) and tags;
 *  - pinning.
 *
 * Pinning is a separate request on purpose: the backend treats any update
 * whose body carries `isPinned` as a pin-only change and ignores every other
 * field (see `Post.Update`). So a new notice is created first and pinned
 * afterwards, and on an existing notice the switch applies immediately rather
 * than waiting for Save.
 */
const AdminPostEditor: FC = () => {
  const { postId } = useParams()
  const navigate = useNavigate()
  const { t } = useTranslation()

  const isNew = postId === 'new'
  const isExisting = !isNew && POST_ID_PATTERN.test(postId ?? '')
  const isInvalid = !isNew && !isExisting

  const {
    data: current,
    error: loadError,
    isLoading,
    mutate: reload,
  } = api.info.useInfoGetPost(
    postId ?? '',
    { refreshInterval: 0, revalidateOnFocus: false, shouldRetryOnError: false },
    isExisting
  )

  // Every tag already used by a notice, for suggestions.
  const { data: allPosts } = api.info.useInfoGetPosts({ refreshInterval: 0, revalidateOnFocus: false })
  const knownTags = useMemo(() => {
    const used = (allPosts ?? []).flatMap((post) => post.tags ?? [])
    return normaliseTags([...used, ...SPONSOR_TAGS.slice(0, 2)]).sort((a, b) => a.localeCompare(b))
  }, [allPosts])

  const [draft, setDraft] = useState<Draft>(EMPTY_DRAFT)
  const [pinned, setPinned] = useState(false)
  const [busy, setBusy] = useState(false)
  const [pinBusy, setPinBusy] = useState(false)
  const [mode, setMode] = useState<ViewMode>('split')
  const [touched, setTouched] = useState(false)

  const baseline: Draft = useMemo(
    () =>
      current
        ? {
            title: current.title ?? '',
            summary: current.summary ?? '',
            content: current.content ?? '',
            tags: current.tags ?? [],
          }
        : EMPTY_DRAFT,
    [current]
  )

  // Seed the form once per loaded notice. Keyed on the id, not the object, so a
  // pin toggle (which refreshes `current`) never throws away unsaved text.
  const currentId = current?.id
  useEffect(() => {
    if (!current) return
    setDraft({
      title: current.title ?? '',
      summary: current.summary ?? '',
      content: current.content ?? '',
      tags: current.tags ?? [],
    })
    setPinned(current.isPinned)
  }, [currentId])

  const dirty =
    draft.title !== baseline.title ||
    draft.summary !== baseline.summary ||
    draft.content !== baseline.content ||
    !sameTags(normaliseTags(draft.tags), normaliseTags(baseline.tags)) ||
    (isNew && pinned)

  // Warn before the tab is closed or reloaded with unsaved work.
  useEffect(() => {
    if (!dirty) return
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault()
    }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [dirty])

  const titleLength = draft.title.length
  const titleError = !draft.title.trim()
    ? t('admin.notices.editor.title_required', { defaultValue: 'A title is required.' })
    : titleLength > MAX_TITLE_LENGTH
      ? t('admin.notices.editor.title_too_long', {
          defaultValue: 'Titles are limited to {{max}} characters.',
          max: MAX_TITLE_LENGTH,
        })
      : null

  const refreshLists = () => {
    api.info.mutateInfoGetPosts()
    api.info.mutateInfoGetLatestPosts()
  }

  const buildBody = (): PostEditModel => ({
    title: draft.title.trim(),
    summary: draft.summary,
    content: draft.content,
    tags: normaliseTags(draft.tags),
    // `isPinned` is deliberately omitted — see the component comment.
  })

  const onSave = async () => {
    setTouched(true)
    if (titleError || busy) return
    setBusy(true)

    try {
      if (isNew) {
        const res = await api.edit.editAddPost(buildBody())
        const newId = res.data
        let pinFailed = false
        if (pinned) {
          try {
            await api.edit.editUpdatePost(newId, { isPinned: true })
          } catch (err) {
            pinFailed = true
            showErrorMsg(err, t)
          }
        }
        refreshLists()
        showAdminSuccess(
          pinFailed
            ? t('admin.notices.editor.created_unpinned', {
                defaultValue: 'Notice created, but it could not be pinned. Pin it from the editor.',
              })
            : t('post.notification.created')
        )
        navigate(`/admin/notices/${newId}`, { replace: true })
        return
      }

      if (!isExisting || !postId) return
      const body = buildBody()
      const res = await api.edit.editUpdatePost(postId, body)
      await reload(res.data, { revalidate: false })
      // Reflect the normalised values that were actually saved.
      setDraft((prev) => ({ ...prev, title: body.title ?? prev.title, tags: body.tags ?? prev.tags }))
      refreshLists()
      showAdminSuccess(t('post.notification.saved'))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setBusy(false)
    }
  }

  const onTogglePin = async (next: boolean) => {
    if (isNew) {
      setPinned(next)
      return
    }
    if (!postId || pinBusy) return

    setPinBusy(true)
    const previous = pinned
    setPinned(next)
    try {
      const res = await api.edit.editUpdatePost(postId, { isPinned: next })
      // Only the pin state changed server-side; unsaved text edits stay in the form.
      await reload(current ? { ...current, isPinned: res.data.isPinned } : res.data, { revalidate: false })
      refreshLists()
      showAdminSuccess(
        next
          ? t('post.notification.pinned', { defaultValue: 'Notice pinned' })
          : t('post.notification.unpinned', { defaultValue: 'Notice unpinned' })
      )
    } catch (err) {
      setPinned(previous)
      showErrorMsg(err, t)
    } finally {
      setPinBusy(false)
    }
  }

  const onDelete = () => {
    if (!isExisting || !postId) return
    openAdminConfirm({
      t,
      title: t('post.button.delete'),
      message: t('post.content.delete', { title: current?.title }),
      subject: current?.title,
      onConfirm: async () => {
        setBusy(true)
        try {
          await api.edit.editDeletePost(postId)
          refreshLists()
          showAdminSuccess(t('post.notification.deleted', { defaultValue: 'Notice deleted' }))
          navigate('/admin/notices', { replace: true })
        } catch (err) {
          showErrorMsg(err, t)
        } finally {
          setBusy(false)
        }
      },
    })
  }

  const onViewAsPlayer = () => {
    if (!postId) return
    const go = () => navigate(`/posts/${postId}`, { state: { adminPreview: true } })
    if (!dirty) {
      go()
      return
    }
    openAdminConfirm({
      t,
      severity: 'caution',
      title: t('post.content.updated.title'),
      message: t('admin.notices.editor.leave_unsaved', {
        defaultValue: 'You have unsaved changes. Players will only see the last saved version. Leave anyway?',
      }),
      confirmLabel: t('admin.notices.editor.leave', { defaultValue: 'Leave without saving' }),
      onConfirm: go,
    })
  }

  useHotkeys([['mod+S', () => void onSave()]], [])

  const title = isNew
    ? t('admin.notices.editor.new_title', { defaultValue: 'New notice' })
    : (current?.title ?? t('admin.notices.editor.edit_title', { defaultValue: 'Edit notice' }))

  const breadcrumbs = [{ label: t('admin.nav.posts', { defaultValue: 'Notices' }), to: '/admin/notices' }]
  const backLink = (
    <Button component={Link} to="/admin/notices" variant="default" leftSection={<Icon path={mdiArrowLeft} size={0.8} />}>
      {t('admin.notices.editor.back', { defaultValue: 'Back to notices' })}
    </Button>
  )

  if (isInvalid || (isExisting && loadError)) {
    const notFound = isInvalid || loadError?.status === 404
    return (
      <AdminLayout title={t('admin.notices.editor.edit_title', { defaultValue: 'Edit notice' })} breadcrumbs={breadcrumbs}>
        <Panel>
          <ErrorState
            title={
              notFound
                ? t('admin.notices.editor.not_found', { defaultValue: 'Notice not found' })
                : t('admin.notices.editor.load_failed', { defaultValue: "Couldn't load this notice" })
            }
            message={
              notFound
                ? t('admin.notices.editor.not_found_hint', {
                    defaultValue: 'It may have been deleted. Return to the notices list to pick another one.',
                  })
                : t('admin.notices.editor.load_failed_hint', { defaultValue: 'Check your connection and try again.' })
            }
            onRetry={notFound ? undefined : () => void reload()}
          />
          <div style={{ marginTop: 'var(--zarrar-space-4)' }}>{backLink}</div>
        </Panel>
      </AdminLayout>
    )
  }

  const sponsor = isSponsorPost(draft.tags)
  const shownTitle = draft.title.trim() || t('admin.notices.editor.untitled', { defaultValue: 'Untitled notice' })
  const disabled = busy || (isExisting && isLoading)

  return (
    <AdminLayout
      title={title}
      documentTitle={isNew ? t('post.button.new') : t('post.button.edit')}
      description={t('admin.nav.post_edit_description', {
        defaultValue: 'Platform-wide notice shown on the home page and the notices list.',
      })}
      breadcrumbs={breadcrumbs}
      isLoading={isExisting && isLoading}
      actions={
        <>
          {dirty && !busy && (
            <AdminStatusBadge tone="warning" label={t('admin.state.unsaved', { defaultValue: 'Unsaved changes' })} />
          )}
          {isExisting && (
            <>
              <Button
                variant="outline"
                color="red"
                disabled={disabled}
                leftSection={<Icon path={mdiDeleteOutline} size={0.8} />}
                onClick={onDelete}
              >
                {t('post.button.delete')}
              </Button>
              <Button
                variant="default"
                disabled={disabled}
                leftSection={<Icon path={mdiEyeOutline} size={0.8} />}
                onClick={onViewAsPlayer}
              >
                {t('admin.notices.editor.view_as_player', { defaultValue: 'View as player' })}
              </Button>
            </>
          )}
          <Button
            loading={busy}
            disabled={disabled || (touched && !!titleError)}
            leftSection={<Icon path={mdiContentSaveOutline} size={0.8} />}
            onClick={() => void onSave()}
            title="Ctrl+S"
          >
            {isNew
              ? t('admin.notices.editor.publish', { defaultValue: 'Publish notice' })
              : t('admin.notices.editor.save', { defaultValue: 'Save changes' })}
          </Button>
        </>
      }
    >
      {isExisting && isLoading && !current ? (
        <Panel>
          <LoadingState label={t('admin.notices.editor.loading', { defaultValue: 'Loading notice…' })} />
        </Panel>
      ) : (
        <div className={classes.layout}>
          <div className={classes.main}>
            <Panel>
              <AdminSection
                title={t('admin.notices.editor.details', { defaultValue: 'Details' })}
                description={t('admin.notices.editor.details_hint', {
                  defaultValue: 'The title and summary appear on the notice card players see first.',
                })}
              >
                <AdminFormGrid columns="2">
                  <TextInput
                    required
                    label={t('post.label.title')}
                    disabled={disabled}
                    value={draft.title}
                    maxLength={MAX_TITLE_LENGTH + 20}
                    error={touched ? titleError : null}
                    onBlur={() => setTouched(true)}
                    onChange={(e) => setDraft({ ...draft, title: e.currentTarget.value })}
                    rightSectionWidth={64}
                    rightSection={
                      <span className={classes.counter} data-over={titleLength > MAX_TITLE_LENGTH || undefined}>
                        {titleLength}/{MAX_TITLE_LENGTH}
                      </span>
                    }
                  />
                  <TagsInput
                    clearable
                    label={t('post.label.tag')}
                    description={t('admin.notices.editor.tags_hint', {
                      defaultValue: 'Tag “sponsor” or “partner” to feature it in the Sponsors group.',
                    })}
                    placeholder={t('post.label.add_tag')}
                    disabled={disabled}
                    data={knownTags}
                    value={draft.tags}
                    onChange={(values) => setDraft({ ...draft, tags: values })}
                  />
                  <AdminFormWide>
                    <MarkdownField
                      label={t('post.label.summary')}
                      hint={t('admin.content.markdown_support')}
                      value={draft.summary}
                      onChange={(value) => setDraft({ ...draft, summary: value })}
                      disabled={disabled}
                      mode={mode}
                      minRows={3}
                      maxRows={8}
                      compact
                      emptyLabel={t('admin.notices.editor.summary_empty', {
                        defaultValue: 'No summary yet — the card will show the title only.',
                      })}
                    />
                  </AdminFormWide>
                </AdminFormGrid>
              </AdminSection>
            </Panel>

            <Panel>
              <AdminSection
                title={t('post.label.content')}
                description={t('admin.notices.editor.content_hint', {
                  defaultValue: 'Full notice body shown when a player opens it. Markdown, code blocks and KaTeX are supported.',
                })}
                actions={
                  <SegmentedControl
                    size="xs"
                    value={mode}
                    onChange={(value) => setMode(value as ViewMode)}
                    data={[
                      { value: 'write', label: t('admin.notices.editor.mode_write', { defaultValue: 'Write' }) },
                      { value: 'split', label: t('admin.notices.editor.mode_split', { defaultValue: 'Split' }) },
                      { value: 'preview', label: t('admin.notices.editor.mode_preview', { defaultValue: 'Preview' }) },
                    ]}
                  />
                }
              >
                <MarkdownField
                  label={t('post.label.content')}
                  hint={t('admin.content.markdown_support')}
                  value={draft.content}
                  onChange={(value) => setDraft({ ...draft, content: value })}
                  disabled={disabled}
                  mode={mode}
                  minRows={14}
                  maxRows={32}
                  emptyLabel={t('admin.notices.editor.content_empty', { defaultValue: 'Nothing to preview yet.' })}
                />
              </AdminSection>
            </Panel>
          </div>

          <aside className={classes.aside}>
            <Panel title={t('admin.notices.editor.publishing', { defaultValue: 'Publishing' })}>
              <Switch
                checked={pinned}
                disabled={disabled || pinBusy}
                onChange={(e) => void onTogglePin(e.currentTarget.checked)}
                label={t('admin.notices.editor.pin', { defaultValue: 'Pin to the top of the feed' })}
              />
              <p className={classes.pinNote}>
                {isNew
                  ? t('admin.notices.editor.pin_new_hint', { defaultValue: 'Applied when the notice is published.' })
                  : t('admin.notices.editor.pin_existing_hint', {
                      defaultValue: 'Takes effect immediately — no need to save.',
                    })}
              </p>

              <dl className={classes.metaList} style={{ marginTop: 'var(--zarrar-space-4)' }}>
                <dt>{t('admin.notices.editor.status', { defaultValue: 'Status' })}</dt>
                <dd>
                  {isNew ? (
                    <AdminStatusBadge tone="neutral" label={t('admin.notices.editor.draft', { defaultValue: 'Draft' })} />
                  ) : (
                    <AdminStatusBadge
                      tone={pinned ? 'accent' : 'success'}
                      label={
                        pinned
                          ? t('admin.notices.editor.published_pinned', { defaultValue: 'Published · pinned' })
                          : t('admin.notices.editor.published', { defaultValue: 'Published' })
                      }
                    />
                  )}
                </dd>
                {isExisting && (
                  <>
                    <dt>ID</dt>
                    <dd>
                      <AdminMonoValue value={postId} />
                    </dd>
                    <dt>{t('admin.notices.editor.author', { defaultValue: 'Author' })}</dt>
                    <dd title={current?.authorName ?? undefined}>{current?.authorName ?? '—'}</dd>
                    <dt>{t('admin.notices.editor.updated', { defaultValue: 'Updated' })}</dt>
                    <dd>{formatTime(current?.time)}</dd>
                  </>
                )}
              </dl>
              <p className={classes.shortcut} style={{ marginTop: 'var(--zarrar-space-4)' }}>
                {t('admin.notices.editor.shortcut', { defaultValue: 'Ctrl+S saves' })}
              </p>
            </Panel>

            <Panel title={t('admin.notices.editor.card_preview', { defaultValue: 'Card preview' })}>
              <div className={classes.card} data-pinned={pinned || undefined} data-sponsor={sponsor || undefined}>
                <p className={classes.cardTitle}>{shownTitle}</p>
                {normaliseTags(draft.tags).length > 0 && (
                  <div className={classes.cardTags}>
                    {normaliseTags(draft.tags).map((tag) => (
                      <span key={tag} className={classes.cardTag}>
                        {tag}
                      </span>
                    ))}
                  </div>
                )}
                {draft.summary.trim() && (
                  <div className={classes.cardSummary}>
                    <Markdown source={draft.summary} />
                  </div>
                )}
              </div>
            </Panel>
          </aside>
        </div>
      )}
    </AdminLayout>
  )
}

export default AdminPostEditor
