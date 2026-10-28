import { ActionIcon, Group, Tooltip } from '@mantine/core'
import { mdiChevronLeft, mdiChevronRight, mdiPageFirst } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import classes from '@Styles/admin/AdminControls.module.css'

export interface AdminPaginationProps {
  /** 1-based. */
  page: number
  onChange: (page: number) => void
  /**
   * Total row count when the endpoint reports one. When it does not — the log
   * and submission feeds do not — pass `hasNext` instead.
   */
  total?: number
  pageSize: number
  /** Used when `total` is unknown: a full page implies there may be another. */
  hasNext?: boolean
  disabled?: boolean
}

/**
 * Cursor-style pager shared by every admin collection.
 *
 * Deliberately not Mantine's numbered `Pagination`: half the admin endpoints
 * cannot report a total, so a page-number strip would be a lie on those screens.
 * One control that degrades gracefully beats two that look different.
 */
export const AdminPagination: FC<AdminPaginationProps> = ({
  page,
  onChange,
  total,
  pageSize,
  hasNext,
  disabled = false,
}) => {
  const { t } = useTranslation()

  const pageCount = total !== undefined && total > 0 ? Math.max(1, Math.ceil(total / pageSize)) : undefined
  const canPrev = !disabled && page > 1
  const canNext = !disabled && (pageCount !== undefined ? page < pageCount : (hasNext ?? false))

  return (
    <nav className={classes.pagination} aria-label={t('admin.pagination.aria', { defaultValue: 'Pagination' })}>
      <p className={classes.pageState} role="status" aria-live="polite">
        {pageCount !== undefined
          ? t('admin.pagination.of', { defaultValue: 'Page {{page}} of {{count}}', page, count: pageCount })
          : t('admin.pagination.page', { defaultValue: 'Page {{page}}', page })}
      </p>

      <Group gap="xs" wrap="nowrap">
        <Tooltip label={t('admin.pagination.first', { defaultValue: 'First page' })} withArrow>
          <ActionIcon
            variant="default"
            size="lg"
            disabled={!canPrev}
            aria-label={t('admin.pagination.first', { defaultValue: 'First page' })}
            onClick={() => onChange(1)}
          >
            <Icon path={mdiPageFirst} size={0.9} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label={t('admin.pagination.previous', { defaultValue: 'Previous page' })} withArrow>
          <ActionIcon
            variant="default"
            size="lg"
            disabled={!canPrev}
            aria-label={t('admin.pagination.previous', { defaultValue: 'Previous page' })}
            onClick={() => onChange(page - 1)}
          >
            <Icon path={mdiChevronLeft} size={0.9} />
          </ActionIcon>
        </Tooltip>
        <Tooltip label={t('admin.pagination.next', { defaultValue: 'Next page' })} withArrow>
          <ActionIcon
            variant="default"
            size="lg"
            disabled={!canNext}
            aria-label={t('admin.pagination.next', { defaultValue: 'Next page' })}
            onClick={() => onChange(page + 1)}
          >
            <Icon path={mdiChevronRight} size={0.9} />
          </ActionIcon>
        </Tooltip>
      </Group>
    </nav>
  )
}
