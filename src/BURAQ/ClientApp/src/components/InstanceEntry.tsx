import { ActionIcon, Anchor, Button, Divider, Group, Stack, Text, TextInput, Tooltip } from '@mantine/core'
import { useClipboard, useDebouncedCallback, useDebouncedState } from '@mantine/hooks'
import { showNotification } from '@mantine/notifications'
import {
  mdiAlertCircleOutline,
  mdiCheck,
  mdiContentCopy,
  mdiCubeOutline,
  mdiExclamation,
  mdiOpenInNew,
  mdiServerNetwork,
  mdiTransitConnectionVariant,
} from '@mdi/js'
import { Icon } from '@mdi/react'
import { WsrxState } from '@xdsec/wsrx'
import dayjs from 'dayjs'
import duration from 'dayjs/plugin/duration'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { HandleWsrxError, useWsrx } from '@Components/WsrxProvider'
import { StatusDot } from '@Components/buraq'
import { getProxyUrl as getProxyEntry } from '@Utils/Shared'
import { useConfig } from '@Hooks/useConfig'
import { ClientFlagContext, ContainerPortMappingType } from '@Api'
import classes from '@Styles/InstanceEntry.module.css'
import misc from '@Styles/Misc.module.css'

dayjs.extend(duration)

export type InstanceAction = 'creating' | 'extending' | 'destroying'

interface InstanceEntryProps {
  test?: boolean
  label?: string
  context: ClientFlagContext
  disabled?: boolean
  action?: InstanceAction
  failed?: boolean
  variant?: 'v2'
  onCreate?: () => void
  onExtend?: () => void
  onDestroy?: () => void
}

interface CountdownProps {
  time?: number | null
  onTimeout?: () => void
  onExpiredChange?: (expired: boolean) => void
  extendEnabled: boolean
  enableExtend: () => void
}

const Countdown: FC<CountdownProps> = ({
  time,
  onTimeout,
  onExpiredChange,
  extendEnabled,
  enableExtend,
}) => {
  const { config } = useConfig()
  const [now, setNow] = useState(dayjs())
  const [timeoutExecuted, setTimeoutExecuted] = useState(false)
  const end = useMemo(
    () => (time ? dayjs(time) : dayjs().add(config.defaultLifetime ?? 120, 'minutes')),
    [config.defaultLifetime, time]
  )
  const secondsRemaining = Math.max(0, end.diff(now, 'second'))
  const countdown = dayjs.duration(secondsRemaining, 'seconds')
  const expired = secondsRemaining <= 0

  useEffect(() => {
    setNow(dayjs())
    if (dayjs().isAfter(end)) return
    const interval = window.setInterval(() => setNow(dayjs()), 1000)
    return () => window.clearInterval(interval)
  }, [end])

  useEffect(() => {
    if (!extendEnabled && config.renewalWindow && countdown.asMinutes() < config.renewalWindow) {
      enableExtend()
    }

    if (expired && !timeoutExecuted && onTimeout) {
      setTimeoutExecuted(true)
      onTimeout()
    }

    if (!expired && timeoutExecuted) setTimeoutExecuted(false)
    onExpiredChange?.(expired)
  }, [
    config.renewalWindow,
    countdown,
    enableExtend,
    expired,
    extendEnabled,
    onExpiredChange,
    onTimeout,
    timeoutExecuted,
  ])

  const countdownText = expired ? '00:00:00' : countdown.format('HH:mm:ss')

  return (
    <Text
      component="time"
      dateTime={end.toISOString()}
      className={classes.countdown}
      data-expired={expired || undefined}
      aria-label={`Time remaining ${countdownText}`}
    >
      {countdownText}
    </Text>
  )
}

export const InstanceEntry: FC<InstanceEntryProps> = (props) => {
  const {
    test: isPreview,
    label,
    context,
    disabled,
    action,
    failed,
    variant,
    onCreate,
    onDestroy,
  } = props
  const { wsrx, wsrxState, wsrxOptions } = useWsrx()
  const { config } = useConfig()
  const clipBoard = useClipboard()
  const { t } = useTranslation()

  const [forceShowOriginal, setForceShowOriginal] = useState(false)
  const [withContainer, setWithContainer] = useState(!!context.instanceEntry)
  const [expired, setExpired] = useState(false)

  const instanceEntry = context.instanceEntry ?? ''
  const isPlatformProxy =
    config.portMapping === ContainerPortMappingType.PlatformProxy &&
    instanceEntry.length === 36 &&
    !instanceEntry.includes(':')
  const originalEntry = isPlatformProxy ? getProxyEntry(instanceEntry, isPreview) : instanceEntry

  const [canExtend, setCanExtend] = useDebouncedState(false, 500)
  const enableExtend = useDebouncedCallback(() => {
    showNotification({
      color: 'orange',
      title: t('challenge.notification.instance.extend.note.title'),
      message: t('challenge.notification.instance.extend.note.message'),
      icon: <Icon path={mdiExclamation} size={1} />,
    })
    setCanExtend(true)
  }, 100)

  useEffect(() => {
    setWithContainer(!!context.instanceEntry)
    setExpired(false)
    const countdown = dayjs.duration(dayjs(context.closeTime ?? 0).diff(dayjs()))
    setCanExtend(countdown.asMinutes() < (config.renewalWindow ?? 10))
  }, [config.renewalWindow, context.closeTime, context.instanceEntry, setCanExtend])

  const onExtend = () => {
    if (!canExtend || !props.onExtend) return
    props.onExtend()
    setCanExtend(false)
  }

  const localTraffic = wsrx.list().find((traffic) => traffic.remote === originalEntry)
  const [localEntry, setLocalEntry] = useState(localTraffic?.local ?? '')
  const isWsrxUsable = isPlatformProxy && wsrxState === WsrxState.Usable
  const useOriginal = !!localTraffic && forceShowOriginal

  useEffect(() => {
    setLocalEntry(localTraffic?.local ?? '')
    setForceShowOriginal(false)
  }, [localTraffic?.local, originalEntry])

  useEffect(() => {
    if (!originalEntry || !isWsrxUsable) return

    const localAddr = wsrxOptions.allowLan ? '0.0.0.0:0' : '127.0.0.1:0'
    const requestProxy = async () => {
      try {
        const traffic = await wsrx.add({
          label,
          remote: originalEntry,
          local: localAddr,
        })
        setLocalEntry(traffic.local)
      } catch (error) {
        HandleWsrxError(error, t)
      }
    }

    void requestProxy()
  }, [isWsrxUsable, label, originalEntry, t, wsrx, wsrxOptions.allowLan])

  const useLocal = isWsrxUsable && !useOriginal
  const entry = useLocal ? localEntry : originalEntry
  const entryIsWss = isPlatformProxy && !useLocal
  const connectionPending = useLocal && !entry

  const onCopyEntry = () => {
    if (!entry) return
    clipBoard.copy(entry)
    showNotification({
      color: 'teal',
      title: entryIsWss ? t('challenge.notification.instance.copied.url.title') : undefined,
      message: entryIsWss
        ? t('challenge.notification.instance.copied.url.message')
        : t('challenge.notification.instance.copied.entry'),
      icon: <Icon path={mdiCheck} size={1} />,
    })
  }

  if (!withContainer) {
    if (isPreview) {
      return (
        <section className={classes.card} data-variant={variant}>
          <div className={classes.header}>
            <div className={classes.heading}>
              <Icon path={mdiCubeOutline} size={0.85} aria-hidden />
              <h3>{t('challenge.content.instance.title', { defaultValue: 'Challenge instance' })}</h3>
            </div>
            <StatusDot status="offline" label={t('common.indicator.status.offline', { defaultValue: 'Offline' })} />
          </div>
          <Text className={classes.previewMessage}>{t('challenge.content.instance.test.no_container')}</Text>
        </section>
      )
    }

    return (
      <section className={classes.card} data-failed={failed || undefined} data-variant={variant}>
        <div className={classes.header}>
          <div className={classes.heading}>
            <Icon path={mdiCubeOutline} size={0.85} aria-hidden />
            <h3>{t('challenge.content.instance.title', { defaultValue: 'Challenge instance' })}</h3>
          </div>
          <StatusDot
            status={failed ? 'offline' : action === 'creating' ? 'active' : 'offline'}
            label={
              failed
                ? t('common.state.error.title', { defaultValue: 'Failed' })
                : action === 'creating'
                  ? t('challenge.content.instance.provisioning', { defaultValue: 'Provisioning' })
                  : t('challenge.content.instance.inactive', { defaultValue: 'Not started' })
            }
          />
        </div>

        {failed ? (
          <div className={classes.failure} role="alert">
            <Icon path={mdiAlertCircleOutline} size={0.9} aria-hidden />
            <Text>
              {t('challenge.content.instance.failed', {
                defaultValue: 'The instance could not be started. Retry when ready.',
              })}
            </Text>
          </div>
        ) : (
          <Stack gap={2}>
            <Text className={classes.message}>{t('challenge.content.instance.no_container.message')}</Text>
            <Text className={classes.note}>
              {t('challenge.content.instance.no_container.note', {
                min: config.defaultLifetime,
              })}
            </Text>
          </Stack>
        )}

        <Button
          fullWidth
          variant={failed ? 'outline' : 'filled'}
          onClick={onCreate}
          disabled={disabled || action === 'destroying' || action === 'extending'}
          loading={action === 'creating'}
        >
          {failed
            ? t('common.state.error.retry', { defaultValue: 'Try again' })
            : t('challenge.button.instance.create')}
        </Button>
      </section>
    )
  }

  return (
    <section className={classes.card} data-expired={expired || undefined} data-failed={failed || undefined} data-variant={variant}>
      <div className={classes.header}>
        <div className={classes.heading}>
          <Icon path={mdiCubeOutline} size={0.85} aria-hidden />
          <h3>{t('challenge.content.instance.title', { defaultValue: 'Challenge instance' })}</h3>
        </div>
        <StatusDot
          status={failed || expired ? 'offline' : 'active'}
          label={
            failed
              ? t('common.state.error.title', { defaultValue: 'Failed' })
              : expired
                ? t('challenge.content.instance.expired', { defaultValue: 'Expired' })
                : t('challenge.content.instance.running', { defaultValue: 'Running' })
          }
        />
      </div>

      {failed && (
        <div className={classes.failure} role="alert">
          <Icon path={mdiAlertCircleOutline} size={0.9} aria-hidden />
          <Text>{t('challenge.content.instance.failed', { defaultValue: 'The last instance action failed.' })}</Text>
        </div>
      )}

      <TextInput
        label={t('challenge.content.instance.entry.label')}
        description={
          isPlatformProxy &&
          !isPreview && (
            <Text span size="sm">
              {t('challenge.content.instance.entry.description.proxy')}{' '}
              <Anchor href="https://github.com/XDSEC/WebSocketReflectorX/releases" target="_blank" rel="noreferrer">
                {t('challenge.content.instance.entry.description.anchor')}
              </Anchor>
            </Text>
          )
        }
        leftSection={
          <Icon
            path={mdiServerNetwork}
            size={0.9}
            data-proxied={(isWsrxUsable && !useOriginal) || undefined}
            className={classes.icon}
          />
        }
        value={entry}
        placeholder={
          connectionPending
            ? t('challenge.content.instance.connection.pending', { defaultValue: 'Connecting…' })
            : undefined
        }
        readOnly
        classNames={{ input: misc.ffmono }}
        rightSection={
          <Group gap={2}>
            <Divider orientation="vertical" pr={4} />
            {isWsrxUsable && (
              <Tooltip
                label={
                  forceShowOriginal
                    ? t('challenge.button.instance.show.proxied')
                    : t('challenge.button.instance.show.original')
                }
                withArrow
              >
                <ActionIcon
                  aria-label={
                    forceShowOriginal
                      ? t('challenge.button.instance.show.proxied')
                      : t('challenge.button.instance.show.original')
                  }
                  onClick={() => setForceShowOriginal((previous) => !previous)}
                >
                  <Icon path={mdiTransitConnectionVariant} size={0.9} />
                </ActionIcon>
              </Tooltip>
            )}
            <Tooltip label={t('common.button.copy')} withArrow>
              <ActionIcon aria-label={t('common.button.copy')} disabled={!entry} onClick={onCopyEntry}>
                <Icon path={mdiContentCopy} size={0.9} />
              </ActionIcon>
            </Tooltip>
            <Tooltip label={t('challenge.content.instance.open.web')} withArrow>
              <ActionIcon
                aria-label={t('challenge.content.instance.open.web')}
                disabled={entryIsWss || !entry}
                component="a"
                href={
                  entryIsWss || !entry
                    ? '#'
                    : `http://${useLocal && wsrxOptions.allowLan ? entry.replace('0.0.0.0', '127.0.0.1') : entry}`
                }
                target={entryIsWss || !entry ? undefined : '_blank'}
                rel="noreferrer"
              >
                <Icon path={mdiOpenInNew} size={0.9} />
              </ActionIcon>
            </Tooltip>
          </Group>
        }
        rightSectionWidth={isWsrxUsable ? '6.75rem' : '5rem'}
      />

      {!isPreview && (
        <>
          <div className={classes.timeRow}>
            <div>
              <Text className={classes.metaLabel}>{t('challenge.content.instance.actions.count_down')}</Text>
              <Countdown
                time={context.closeTime}
                extendEnabled={canExtend}
                enableExtend={enableExtend}
                onExpiredChange={setExpired}
                onTimeout={onDestroy}
              />
            </div>
            <Text className={classes.note}>
              {t('challenge.content.instance.actions.note', { min: config.renewalWindow })}
            </Text>
          </div>
          <div className={classes.actions}>
            <Button
              color="cyan"
              variant="outline"
              onClick={onExtend}
              disabled={!props.onExtend || !canExtend || disabled || expired}
              loading={action === 'extending'}
            >
              {t('challenge.button.instance.extend')}
            </Button>
            <Button
              color="red"
              variant="outline"
              onClick={onDestroy}
              disabled={disabled && action !== 'destroying'}
              loading={action === 'destroying'}
            >
              {t('challenge.button.instance.destroy')}
            </Button>
          </div>
        </>
      )}
    </section>
  )
}
