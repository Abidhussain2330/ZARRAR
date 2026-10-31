import { generateColors } from '@mantine/colors-generator'
import {
  ActionIcon,
  Button,
  ColorInput,
  FileInput,
  Group,
  InputBase,
  NumberInput,
  Stack,
  Switch,
  Text,
  TextInput,
  Tooltip,
  useMantineTheme,
} from '@mantine/core'
import { mdiContentSaveOutline, mdiRestore } from '@mdi/js'
import { Icon } from '@mdi/react'
import { FC, useEffect, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ColorPreview } from '@Components/ColorPreview'
import { LogoBox } from '@Components/LogoBox'
import { SwitchLabel } from '@Components/admin/SwitchLabel'
import {
  AdminFormGrid,
  AdminFormWide,
  AdminLayout,
  AdminPending,
  AdminSection,
  openAdminConfirm,
  showAdminSuccess,
} from '@Components/admin/shell'
import { ErrorState, Panel } from '@Components/zarrar'
import { webCryptoAvailable } from '@Utils/Crypto'
import { getInputNumber, IMAGE_MIME_TYPES, showErrorMsg } from '@Utils/Shared'
import { OnceSWRConfig, useCaptchaConfig, useConfig } from '@Hooks/useConfig'
import api, { AccountPolicy, ConfigEditModel, ContainerPolicy, GlobalConfig } from '@Api'
import misc from '@Styles/Misc.module.css'

const HEX_COLOR = /^#[0-9A-F]{6}$/i

/**
 * Platform settings.
 *
 * Same three policy groups and the same single `PUT /api/admin/config` call as
 * before. What changed is the layout — fixed four-column grids became the
 * shared responsive form grid — and the save affordance: the old floating
 * button was positioned with a hard-coded viewport calculation that landed off
 * screen below 1080px, on a page that had no other way to save.
 */
const Configs: FC = () => {
  const { data: configs, error, mutate } = api.admin.useAdminGetConfigs(OnceSWRConfig)
  const { mutate: mutateCaptchaConfig } = useCaptchaConfig()
  const { mutate: mutateConfig } = useConfig()

  const [disabled, setDisabled] = useState(false)
  const [globalConfig, setGlobalConfig] = useState<GlobalConfig | null>()
  const [accountPolicy, setAccountPolicy] = useState<AccountPolicy | null>()
  const [containerPolicy, setContainerPolicy] = useState<ContainerPolicy | null>()
  const [color, setColor] = useState<string | undefined | null>()
  const [logoFile, setLogoFile] = useState<File | null>(null)
  const [dirty, setDirty] = useState(false)

  const { t } = useTranslation()
  const theme = useMantineTheme()

  useEffect(() => {
    if (!configs) return

    setContainerPolicy(configs.containerPolicy)
    setGlobalConfig(configs.globalConfig)
    setAccountPolicy(configs.accountPolicy)
    setColor(configs.globalConfig?.customTheme)
    setDirty(false)
  }, [configs])

  const colors = useMemo(
    () => (color && HEX_COLOR.test(color) ? generateColors(color) : theme.colors.brand),
    [color, theme.colors.brand]
  )

  const updateConfig = async (conf: ConfigEditModel) => {
    setDisabled(true)

    try {
      await api.admin.adminUpdateConfigs(conf)

      if (logoFile) {
        await api.admin.adminUpdateLogo({ file: logoFile })
        setLogoFile(null)
      }

      mutate({ ...conf })
      mutateConfig({ ...conf.globalConfig, ...conf.containerPolicy })
      mutateCaptchaConfig()
      setDirty(false)
      showAdminSuccess(t('admin.notification.settings.saved', { defaultValue: 'Settings saved' }))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  const onSave = () =>
    updateConfig({
      globalConfig: { ...globalConfig, customTheme: color && HEX_COLOR.test(color) ? color : '' },
      accountPolicy,
      containerPolicy,
    })

  const onResetLogo = async () => {
    setDisabled(true)
    setLogoFile(null)

    try {
      await api.admin.adminResetLogo()
      mutate({ ...configs, globalConfig: { ...globalConfig, faviconHash: '' } })
      mutateConfig({ ...configs, logoUrl: '' })
      showAdminSuccess(t('admin.notification.settings.logo_reset', { defaultValue: 'Logo reset to default' }))
    } catch (err) {
      showErrorMsg(err, t)
    } finally {
      setDisabled(false)
    }
  }

  // Every field funnels through these so a single place marks the form dirty.
  const patchGlobal = (patch: Partial<GlobalConfig>) => {
    setGlobalConfig({ ...globalConfig, ...patch })
    setDirty(true)
  }

  const patchAccount = (patch: Partial<AccountPolicy>) => {
    setAccountPolicy({ ...accountPolicy, ...patch })
    setDirty(true)
  }

  const patchContainer = (patch: Partial<ContainerPolicy>) => {
    setContainerPolicy({ ...containerPolicy, ...patch })
    setDirty(true)
  }

  return (
    <AdminLayout
      title={t('admin.tab.settings')}
      description={t('admin.nav.settings_description', {
        defaultValue: 'Platform identity, registration policy and container defaults.',
      })}
      breadcrumbs={[{ label: t('admin.nav.group.system', { defaultValue: 'System' }) }]}
      isLoading={!configs && !error}
      actions={
        <>
          {disabled && <AdminPending />}
          <Button
            disabled={disabled || !configs}
            leftSection={<Icon path={mdiContentSaveOutline} size={0.8} />}
            onClick={onSave}
          >
            {dirty
              ? t('admin.button.save_changes', { defaultValue: 'Save changes' })
              : t('admin.button.save')}
          </Button>
        </>
      }
    >
      {error && !configs ? (
        <ErrorState
          message={t('admin.state.load_failed_settings', { defaultValue: 'Could not load platform settings.' })}
          onRetry={() => mutate()}
        />
      ) : (
        <>
          <Panel>
            <AdminSection
              title={t('admin.content.settings.platform.title')}
              description={t('admin.content.settings.platform.name.description')}
            >
              <AdminFormGrid>
                <TextInput
                  label={t('admin.content.settings.platform.name.label')}
                  description={t('admin.content.settings.platform.name.description')}
                  placeholder="ZARRAR"
                  disabled={disabled}
                  value={globalConfig?.title ?? ''}
                  onChange={(e) => patchGlobal({ title: e.currentTarget.value })}
                />
                <TextInput
                  label={t('admin.content.settings.platform.slogan.label')}
                  description={t('admin.content.settings.platform.slogan.description')}
                  placeholder="Hack for fun not for profit"
                  disabled={disabled}
                  value={globalConfig?.slogan ?? ''}
                  onChange={(e) => patchGlobal({ slogan: e.currentTarget.value })}
                />
                <TextInput
                  label={t('admin.content.settings.platform.description.label')}
                  description={t('admin.content.settings.platform.description.description')}
                  placeholder="ZARRAR — Unified CTF Platform"
                  disabled={disabled}
                  value={globalConfig?.description ?? ''}
                  onChange={(e) => patchGlobal({ description: e.currentTarget.value })}
                />
                <TextInput
                  label={t('admin.content.settings.platform.footer.label')}
                  description={t('admin.content.settings.platform.footer.description')}
                  placeholder={t('admin.placeholder.settings.footer')}
                  disabled={disabled}
                  value={globalConfig?.footerInfo ?? ''}
                  onChange={(e) => patchGlobal({ footerInfo: e.currentTarget.value })}
                />
                <TextInput
                  label={t('admin.content.settings.platform.public_url.label', { defaultValue: 'Public URL' })}
                  description={t('admin.content.settings.platform.public_url.description', {
                    defaultValue:
                      'Canonical public origin (e.g. https://ctf.example.com). Used to build links in emails — leave empty only for local development.',
                  })}
                  placeholder="https://ctf.example.com"
                  disabled={disabled}
                  value={globalConfig?.publicUrl ?? ''}
                  onChange={(e) => patchGlobal({ publicUrl: e.currentTarget.value })}
                />
              </AdminFormGrid>
            </AdminSection>
          </Panel>

          <Panel>
            <AdminSection title={t('admin.content.settings.platform.logo.label')}>
              <AdminFormGrid>
                <FileInput
                  label={t('admin.content.settings.platform.logo.label')}
                  description={t('admin.content.settings.platform.logo.description')}
                  placeholder={
                    globalConfig?.faviconHash
                      ? t('admin.placeholder.settings.logo.custom')
                      : t('admin.placeholder.settings.logo.default')
                  }
                  disabled={disabled}
                  accept={IMAGE_MIME_TYPES.join(',')}
                  value={logoFile}
                  onChange={(file) => {
                    setLogoFile(file)
                    setDirty(true)
                  }}
                  rightSection={
                    <Tooltip label={t('common.button.reset')}>
                      <ActionIcon
                        variant="subtle"
                        aria-label={t('common.button.reset')}
                        disabled={disabled}
                        onClick={() =>
                          openAdminConfirm({
                            t,
                            severity: 'caution',
                            title: t('common.button.reset'),
                            message: t('admin.content.settings.platform.logo.reset', {
                              defaultValue: 'Reset the platform logo to the ZARRAR default?',
                            }),
                            onConfirm: onResetLogo,
                          })
                        }
                      >
                        <Icon path={mdiRestore} size={0.8} />
                      </ActionIcon>
                    </Tooltip>
                  }
                />

                <Stack gap="xs">
                  <Text size="sm" fw={500}>
                    {t('admin.content.settings.platform.logo.preview', { defaultValue: 'Preview' })}
                  </Text>
                  <Group gap="sm" align="flex-end" wrap="wrap">
                    {[20, 40, 60, 80].map((size) => (
                      <Stack align="center" gap={2} key={size}>
                        <LogoBox size={size} url={logoFile ? URL.createObjectURL(logoFile) : undefined} />
                        <Text fw="bold" ta="center" size="xs">
                          {size}px
                        </Text>
                      </Stack>
                    ))}
                  </Group>
                </Stack>

                <ColorInput
                  label={t('admin.content.settings.platform.color.label')}
                  description={t('admin.content.settings.platform.color.description')}
                  placeholder={t('common.content.color.custom.placeholder')}
                  disabled={disabled}
                  value={color ?? ''}
                  onChange={(value) => {
                    setColor(value)
                    setDirty(true)
                  }}
                />

                <InputBase
                  label={t('admin.content.settings.platform.color_palette.label')}
                  description={t('admin.content.settings.platform.color_palette.description')}
                  h="100%"
                  variant="unstyled"
                  component={ColorPreview}
                  colors={colors}
                  displayColorsInfo={false}
                  classNames={{ input: misc.flex }}
                />
              </AdminFormGrid>
            </AdminSection>
          </Panel>

          <Panel>
            <AdminSection title={t('admin.content.settings.account.title')}>
              <AdminFormGrid>
                <Switch
                  checked={accountPolicy?.allowRegister ?? true}
                  disabled={disabled}
                  label={SwitchLabel(
                    t('admin.content.settings.account.allow_register.label'),
                    t('admin.content.settings.account.allow_register.description')
                  )}
                  onChange={(e) => patchAccount({ allowRegister: e.currentTarget.checked })}
                />
                <Switch
                  checked={accountPolicy?.emailConfirmationRequired ?? false}
                  disabled={disabled}
                  label={SwitchLabel(
                    t('admin.content.settings.account.email_confirmation_required.label'),
                    t('admin.content.settings.account.email_confirmation_required.description')
                  )}
                  onChange={(e) => patchAccount({ emailConfirmationRequired: e.currentTarget.checked })}
                />
                <Switch
                  checked={accountPolicy?.activeOnRegister ?? true}
                  disabled={disabled}
                  label={SwitchLabel(
                    t('admin.content.settings.account.auto_active.label'),
                    t('admin.content.settings.account.auto_active.description')
                  )}
                  onChange={(e) => patchAccount({ activeOnRegister: e.currentTarget.checked })}
                />
                <Switch
                  checked={accountPolicy?.useCaptcha ?? false}
                  disabled={disabled}
                  label={SwitchLabel(
                    t('admin.content.settings.account.use_captcha.label'),
                    t('admin.content.settings.account.use_captcha.description')
                  )}
                  onChange={(e) => patchAccount({ useCaptcha: e.currentTarget.checked })}
                />
                <AdminFormWide>
                  <TextInput
                    label={t('admin.content.settings.account.email_domain_list.label')}
                    description={t('admin.content.settings.account.email_domain_list.description')}
                    placeholder={t('admin.placeholder.settings.email_domain_list')}
                    disabled={disabled}
                    value={accountPolicy?.emailDomainList ?? ''}
                    onChange={(e) => patchAccount({ emailDomainList: e.currentTarget.value })}
                  />
                </AdminFormWide>
              </AdminFormGrid>
            </AdminSection>
          </Panel>

          <Panel>
            <AdminSection title={t('admin.content.settings.container.title')}>
              <AdminFormGrid>
                <NumberInput
                  label={t('admin.content.settings.container.default_lifetime.label')}
                  description={t('admin.content.settings.container.default_lifetime.description')}
                  placeholder="120"
                  min={1}
                  max={7200}
                  disabled={disabled}
                  value={containerPolicy?.defaultLifetime ?? 120}
                  onChange={(e) => {
                    const number = getInputNumber(e)
                    if (isNaN(number)) return
                    patchContainer({ defaultLifetime: number })
                  }}
                />
                <NumberInput
                  label={t('admin.content.settings.container.extension_duration.label')}
                  description={t('admin.content.settings.container.extension_duration.description')}
                  placeholder="120"
                  min={1}
                  max={7200}
                  disabled={disabled}
                  value={containerPolicy?.extensionDuration ?? 120}
                  onChange={(e) => {
                    const number = getInputNumber(e)
                    if (isNaN(number)) return
                    patchContainer({ extensionDuration: number })
                  }}
                />
                <NumberInput
                  label={t('admin.content.settings.container.renewal_window.label')}
                  description={t('admin.content.settings.container.renewal_window.description')}
                  placeholder="10"
                  min={1}
                  max={360}
                  disabled={disabled}
                  value={containerPolicy?.renewalWindow ?? 10}
                  onChange={(e) => {
                    const number = getInputNumber(e)
                    if (isNaN(number)) return
                    patchContainer({ renewalWindow: number })
                  }}
                />
                <Switch
                  checked={containerPolicy?.autoDestroyOnLimitReached ?? true}
                  disabled={disabled}
                  label={SwitchLabel(
                    t('admin.content.settings.container.auto_destroy.label'),
                    t('admin.content.settings.container.auto_destroy.description')
                  )}
                  onChange={(e) => patchContainer({ autoDestroyOnLimitReached: e.currentTarget.checked })}
                />
              </AdminFormGrid>
            </AdminSection>
          </Panel>

          <Panel>
            <AdminSection
              title={t('admin.content.settings.platform.api_encryption.label')}
              description={t('admin.content.settings.platform.api_encryption.description')}
            >
              {/*
                Read-only, exactly as before: the key pair is provisioned by the
                server and this switch reports the provisioned state. Making it
                writable here would imply the browser can rotate a server-held
                key, which it cannot.
              */}
              <Switch
                readOnly
                checked={globalConfig?.apiEncryption ?? false}
                disabled={disabled || !webCryptoAvailable}
                label={SwitchLabel(
                  t('admin.content.settings.platform.api_encryption.label'),
                  t('admin.content.settings.platform.api_encryption.description'),
                  webCryptoAvailable ? null : t('admin.content.settings.platform.api_encryption.not_available')
                )}
              />
            </AdminSection>
          </Panel>
        </>
      )}
    </AdminLayout>
  )
}

export default Configs
