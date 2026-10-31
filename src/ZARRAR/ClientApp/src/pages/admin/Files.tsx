import { ActionIcon, Image, Tooltip } from '@mantine/core'
import { useClipboard } from '@mantine/hooks'
import { mdiFileOutline, mdiOpenInNew } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useCallback, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import {
  AdminCellActions,
  AdminCellMeta,
  AdminCellName,
  AdminCellStack,
  AdminLayout,
  AdminMetricGrid,
  AdminMonoValue,
  AdminPagination,
  AdminResultCount,
  AdminSearch,
  AdminTable,
  AdminToolbar,
  showAdminSuccess,
} from '@Components/admin/shell'
import { DataTableColumn, ErrorState, Panel, StatCard } from '@Components/zarrar'
import { showErrorMsg } from '@Utils/Shared'
import { useArrayResponse } from '@Hooks/useArrayResponse'
import api, { LocalFile } from '@Api'

const ITEM_COUNT_PER_PAGE = 50
const IMAGE_EXTENSIONS = new Set(['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg', 'avif'])

/**
 * Storage browser over `GET /api/admin/files`.
 *
 * Read-only on purpose: the endpoint exposes a listing and nothing else, and
 * blobs are owned by the challenge, avatar and writeup workflows that created
 * them. Deleting one here would silently break whichever record references it,
 * so this screen surfaces what exists rather than inventing a lifecycle the
 * backend does not support.
 *
 * The asset URL is derived exactly the way `AssetsController` serves it —
 * `/assets/{hash}/{name}` — so no new route or contract is introduced.
 */
const Files: FC = () => {
  const [page, setPage] = useState(1)
  const [reload, setReload] = useState(0)
  const [query, setQuery] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<unknown>(null)

  const { data: files, total, setData: setFiles } = useArrayResponse<LocalFile>()
  const clipboard = useClipboard()
  const { t } = useTranslation()

  const fetchFiles = useCallback(async () => {
    setLoading(true)
    setError(null)

    try {
      const res = await api.admin.adminFiles({
        count: ITEM_COUNT_PER_PAGE,
        skip: (page - 1) * ITEM_COUNT_PER_PAGE,
      })
      setFiles(res.data)
    } catch (err) {
      setError(err)
      showErrorMsg(err, t)
    } finally {
      setLoading(false)
    }
  }, [page, t])

  useEffect(() => {
    fetchFiles()
  }, [fetchFiles, reload])

  /*
   * The endpoint has no search parameter, so filtering is explicitly scoped to
   * the page in view and the result count says "matching" rather than implying
   * it searched the whole store.
   */
  const visible = useMemo(() => {
    const needle = query.trim().toLowerCase()
    if (!needle) return files ?? []

    return (files ?? []).filter(
      (file) => file.name.toLowerCase().includes(needle) || (file.hash ?? '').toLowerCase().includes(needle)
    )
  }, [files, query])

  const assetUrl = (file: LocalFile) => (file.hash ? `/assets/${file.hash}/${encodeURIComponent(file.name)}` : null)
  const extension = (name: string) => (name.includes('.') ? name.split('.').pop()!.toLowerCase() : null)
  const isImage = (name: string) => {
    const ext = extension(name)
    return ext !== null && IMAGE_EXTENSIONS.has(ext)
  }

  const columns: DataTableColumn<LocalFile>[] = [
    {
      key: 'name',
      header: t('admin.label.files.name', { defaultValue: 'File name' }),
      rowHeader: true,
      render: (file) => {
        const url = assetUrl(file)
        const ext = extension(file.name)
        return (
          <AdminCellActions>
            {isImage(file.name) && url ? (
              <Image src={url} alt="" w={32} h={32} radius="sm" fit="cover" fallbackSrc="" />
            ) : (
              <Icon path={mdiFileOutline} size={0.85} />
            )}
            <AdminCellStack>
              <AdminCellName title={file.name}>{file.name}</AdminCellName>
              {ext && <AdminCellMeta>{ext.toUpperCase()}</AdminCellMeta>}
            </AdminCellStack>
          </AdminCellActions>
        )
      },
    },
    {
      key: 'hash',
      header: t('admin.label.files.hash', { defaultValue: 'Content hash' }),
      width: '24rem',
      render: (file) => (
        <AdminMonoValue
          wide
          value={file.hash}
          placeholder="—"
          onCopy={(value) => {
            clipboard.copy(value)
            showAdminSuccess(t('admin.notification.files.hash_copied', { defaultValue: 'Hash copied' }))
          }}
        />
      ),
    },
    {
      key: 'actions',
      header: '',
      headerLabel: t('admin.table.row_actions', { defaultValue: 'Row actions' }),
      align: 'right',
      width: '5rem',
      render: (file) => {
        const url = assetUrl(file)

        return (
          <AdminCellActions>
            <Tooltip label={t('admin.button.files.open', { defaultValue: 'Open file' })} withArrow>
              <ActionIcon
                variant="subtle"
                component="a"
                href={url ?? undefined}
                target="_blank"
                rel="noreferrer"
                disabled={!url}
                aria-label={t('admin.button.files.open', { defaultValue: 'Open file' })}
              >
                <Icon path={mdiOpenInNew} size={0.85} />
              </ActionIcon>
            </Tooltip>
          </AdminCellActions>
        )
      },
    },
  ]

  return (
    <AdminLayout
      title={t('admin.nav.files', { defaultValue: 'Files' })}
      description={t('admin.nav.files_description', {
        defaultValue: 'Content-addressed blob storage backing attachments, avatars and posters.',
      })}
      breadcrumbs={[{ label: t('admin.nav.group.infrastructure', { defaultValue: 'Infrastructure' }) }]}
    >
      <AdminMetricGrid label={t('admin.nav.files', { defaultValue: 'Files' })}>
        <StatCard
          label={t('admin.label.files.total', { defaultValue: 'Total files' })}
          value={total !== undefined ? total.toLocaleString() : '—'}
          loading={loading && !files}
        />
      </AdminMetricGrid>

      <AdminToolbar
        trailing={
          <>
            <AdminResultCount shown={visible.length} total={total} filtered={Boolean(query.trim())} />
            <AdminPagination page={page} onChange={setPage} total={total} pageSize={ITEM_COUNT_PER_PAGE} />
          </>
        }
      >
        <AdminSearch
          value={query}
          onChange={setQuery}
          placeholder={t('admin.placeholder.files.search', { defaultValue: 'Filter this page by name or hash' })}
        />
      </AdminToolbar>

      <Panel bare>
        {error && !files ? (
          <ErrorState
            message={t('admin.state.load_failed_files', { defaultValue: 'Could not load the file listing.' })}
            onRetry={() => setReload((value) => value + 1)}
          />
        ) : (
          <AdminTable
            caption={t('admin.nav.files', { defaultValue: 'Files' })}
            columns={columns}
            rows={visible}
            rowKey={(file) => file.hash ?? file.name}
            loading={loading && !files}
            maxHeight="calc(100vh - 22rem)"
            emptyTitle={
              query.trim()
                ? t('admin.state.no_results', { defaultValue: 'No matching files' })
                : t('admin.state.no_files', { defaultValue: 'No files stored' })
            }
            emptyDescription={
              query.trim()
                ? t('admin.state.no_results_hint', { defaultValue: 'Adjust the search or clear the filters.' })
                : undefined
            }
          />
        )}
      </Panel>
    </AdminLayout>
  )
}

export default Files
