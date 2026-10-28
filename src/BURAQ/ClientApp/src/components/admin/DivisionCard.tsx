import { ActionIcon, Group, Stack, Text, Tooltip } from '@mantine/core'
import { mdiContentCopy, mdiDeleteOutline, mdiPencilOutline, mdiTagOutline } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC } from 'react'
import { useTranslation } from 'react-i18next'
import { ActionIconWithConfirm } from '@Components/ActionIconWithConfirm'
import { ScrollingText } from '@Components/ScrollingText'
import { PermissionDot } from '@Components/admin/PermissionSelector'
import { PERMISSION_DEFINITIONS, permissionMaskToArray } from '@Utils/Permission'
import { Division } from '@Api'
import classes from '@Styles/admin/AdminMiscCards.module.css'

export interface DivisionCardProps {
  division: Division
  challengeTitleMap: Map<number, string>
  onEdit: (division: Division) => void
  onDelete: (division: Division) => Promise<void>
  onCopyInviteCode: (code: string) => void
  className?: string
}

export const DivisionCard: FC<DivisionCardProps> = ({
  division,
  challengeTitleMap,
  onEdit,
  onDelete,
  onCopyInviteCode,
  className,
}) => {
  const { t } = useTranslation()

  const overrides = (division.challengeConfigs ?? []).toSorted((a, b) => {
    const left = challengeTitleMap.get(a.challengeId) ?? `#${a.challengeId}`
    const right = challengeTitleMap.get(b.challengeId) ?? `#${b.challengeId}`
    return left.localeCompare(right)
  })

  const renderPermissionDots = (mask?: number | null, includeGlobal = false) => {
    const grantedValues = new Set(permissionMaskToArray(mask))
    const allDefinitions = PERMISSION_DEFINITIONS.filter((definition) => includeGlobal || definition.challengeScoped)

    return (
      <Group gap={6} wrap="wrap">
        {allDefinitions.map((definition) => {
          const isGranted = grantedValues.has(definition.value)
          return <PermissionDot key={definition.value} {...definition} granted={isGranted} />
        })}
      </Group>
    )
  }

  return (
    <div className={`${classes.card} ${className ?? ''}`}>
      <Stack gap="xs">
        <Group justify="space-between" align="flex-start">
          <Group gap="sm">
            <Icon path={mdiTagOutline} size={0.8} />
            <span className={classes.title} style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
              {division.name}
            </span>
          </Group>
          <Group gap="sm" wrap="nowrap">
            <ActionIcon
              variant="transparent"
              color="blue"
              aria-label={t('admin.button.divisions.edit')}
              onClick={() => onEdit(division)}
            >
              <Icon path={mdiPencilOutline} size={1} />
            </ActionIcon>
            <ActionIconWithConfirm
              iconPath={mdiDeleteOutline}
              color="red"
              ariaLabel={t('admin.content.games.divisions.delete_confirm', { name: division.name })}
              message={t('admin.content.games.divisions.delete_confirm', { name: division.name })}
              onClick={() => onDelete(division)}
            />
          </Group>
        </Group>

        {division.inviteCode && (
          <Group gap="sm" align="center" wrap="wrap">
            <Text size="sm">{t('admin.content.games.divisions.form.invite_code.label')}</Text>
            <Text ff="monospace" fw="bold" fz="sm">
              {division.inviteCode}
            </Text>
            <Tooltip label={t('common.button.copy')} withArrow>
              <ActionIcon
                size="sm"
                variant="transparent"
                aria-label={t('common.button.copy')}
                onClick={() => onCopyInviteCode(division.inviteCode ?? '')}
              >
                <Icon path={mdiContentCopy} size={0.6} />
              </ActionIcon>
            </Tooltip>
          </Group>
        )}

        <Group gap="sm" align="center">
          <Text size="sm">{t('admin.content.games.divisions.default_permission_label')}</Text>
          {renderPermissionDots(division.defaultPermissions, true)}
        </Group>

        {overrides.length > 0 && (
          <Stack gap="xs">
            <Text size="sm">{t('admin.content.games.divisions.override_label')}</Text>
            <Stack gap="xs">
              {overrides.map((config) => (
                <Group key={config.challengeId} align="center">
                  <Text size="sm" miw="2rem">{`#${config.challengeId}`}</Text>
                  <ScrollingText text={challengeTitleMap.get(config.challengeId) ?? ''} w="14rem" />
                  {renderPermissionDots(config.permissions)}
                </Group>
              ))}
            </Stack>
          </Stack>
        )}
      </Stack>
    </div>
  )
}
