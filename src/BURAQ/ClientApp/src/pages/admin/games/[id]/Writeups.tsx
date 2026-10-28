import { Button, ScrollArea, Select, Stack } from '@mantine/core'
import { mdiFolderDownloadOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router'
import { AdminGameLayout } from '@Components/admin/AdminGameLayout'
import { PDFViewer } from '@Components/admin/PDFViewer'
import { TeamWriteupCard } from '@Components/admin/TeamWriteupCard'
import { AdminResultCount, AdminToolbar } from '@Components/admin/shell'
import { EmptyState, ErrorState, Panel } from '@Components/buraq'
import { OnceSWRConfig } from '@Hooks/useConfig'
import api, { WriteupInfo } from '@Api'
import classes from '@Styles/admin/AdminControls.module.css'
import layout from '@Styles/admin/AdminWriteups.module.css'

const GameWriteups: FC = () => {
  const { id } = useParams()
  const numId = parseInt(id ?? '-1', 10)

  const [selected, setSelected] = useState<WriteupInfo>()
  const [selectedDivision, setSelectedDivision] = useState<string>('')

  const { data, error, mutate } = api.admin.useAdminWriteups(numId, OnceSWRConfig)
  const { t } = useTranslation()

  const writeups = useMemo(() => data?.writeups ?? [], [data?.writeups])

  const { divisions, divisionOptions } = useMemo(() => {
    const raw = data?.divisions ?? {}
    const divisions = Object.fromEntries(Object.entries(raw).map(([key, value]) => [parseInt(key, 10), value]))
    const divisionOptions = [
      { value: '', label: t('game.label.score_table.all_teams') },
      ...Object.entries(divisions).map(([divisionId, name]) => ({ value: divisionId.toString(), label: name })),
    ]
    return { divisions, divisionOptions }
  }, [data?.divisions, t])

  const filteredWriteups = useMemo(() => {
    if (!selectedDivision) return writeups
    const division = parseInt(selectedDivision, 10)
    return writeups.filter((writeup) => writeup.divisionId === division)
  }, [selectedDivision, writeups])

  useEffect(() => {
    if (filteredWriteups.length && (!selected || !filteredWriteups.some((w) => w.id === selected.id))) {
      setSelected(filteredWriteups[0])
    }
  }, [filteredWriteups, selected])

  return (
    <AdminGameLayout
      tab="writeups"
      isLoading={!data && !error}
      description={t('admin.nav.writeups_description', {
        defaultValue: 'Submitted write-ups, previewable inline or downloadable as one archive.',
      })}
      actions={
        <Button
          disabled={!writeups.length}
          leftSection={<Icon path={mdiFolderDownloadOutline} size={0.8} />}
          onClick={() => window.open(`/api/admin/writeups/${numId}/all`, '_blank', 'noopener,noreferrer')}
        >
          {t('admin.button.writeups.download_all')}
        </Button>
      }
    >
      {error ? (
        <ErrorState
          message={t('admin.state.load_failed_writeups', { defaultValue: 'Could not load write-ups.' })}
          onRetry={() => mutate()}
        />
      ) : !writeups.length ? (
        <Panel>
          <EmptyState
            title={t('admin.content.games.writeups.empty.title')}
            description={t('admin.content.games.writeups.empty.description')}
          />
        </Panel>
      ) : (
        <>
          <AdminToolbar
            filters={
              <Select
                className={classes.filter}
                value={selectedDivision}
                onChange={(value) => setSelectedDivision(value ?? '')}
                aria-label={t('admin.tab.games.divisions')}
                data={divisionOptions}
              />
            }
            trailing={
              <AdminResultCount
                shown={filteredWriteups.length}
                total={writeups.length}
                filtered={Boolean(selectedDivision)}
              />
            }
          />

          {/*
            Side-by-side on desktop, stacked on tablet and phone: a PDF preview
            beside a 15rem list has no usable width below about 1200px, so the
            list moves above the viewer rather than both being squeezed.
          */}
          <div className={layout.split}>
            <Panel bare className={layout.viewer}>
              {selected ? (
                <PDFViewer url={selected.url} height="min(72vh, 900px)" />
              ) : (
                <EmptyState
                  title={t('admin.state.no_results', { defaultValue: 'No matching write-ups' })}
                  description={t('admin.state.no_results_hint', {
                    defaultValue: 'Adjust the search or clear the filters.',
                  })}
                />
              )}
            </Panel>

            <Panel bare className={layout.list}>
              <ScrollArea.Autosize mah="min(72vh, 900px)" type="auto">
                <Stack gap="sm" p="sm">
                  {filteredWriteups.map((writeup) => (
                    <TeamWriteupCard
                      key={writeup.id}
                      writeup={writeup}
                      selected={selected?.id === writeup.id}
                      onClick={() => setSelected(writeup)}
                      divisionName={writeup.divisionId ? divisions[writeup.divisionId] : undefined}
                    />
                  ))}
                </Stack>
              </ScrollArea.Autosize>
            </Panel>
          </div>
        </>
      )}
    </AdminGameLayout>
  )
}

export default GameWriteups
