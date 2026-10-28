// SPDX-License-Identifier: LicenseRef-THE-ARZENS-Custom
// Copyright (C) 2022-2025 THE ARZENS
import { useLocalStorage } from '@mantine/hooks'
import dayjs from 'dayjs'
import { useEffect, useRef } from 'react'
import { SWRConfiguration } from 'swr'
import api, { ClientConfig, ContainerPortMappingType } from '@Api'

export const OnceSWRConfig: SWRConfiguration = {
  refreshInterval: 0,
  revalidateOnFocus: false,
}

const RepoMeta = {
  sha: import.meta.env.VITE_APP_GIT_SHA ?? 'unknown',
  rawTag: import.meta.env.VITE_APP_GIT_NAME ?? 'unknown',
  timestamp: import.meta.env.VITE_APP_BUILD_TIMESTAMP ?? '',
  buildTime: import.meta.env.DEV ? dayjs() : dayjs(import.meta.env.VITE_APP_BUILD_TIMESTAMP),
  repo: 'https://github.com/Arzen-007/Buraq_vProduction',
}

export const useConfig = () => {
  const {
    data: config,
    error,
    mutate,
  } = api.info.useInfoGetClientConfig({
    refreshInterval: 0,
    revalidateOnFocus: false,
    revalidateOnReconnect: false,
    refreshWhenHidden: false,
    shouldRetryOnError: false,
    refreshWhenOffline: false,
  })

  const [clientConfig, setClientConfig] = useLocalStorage<ClientConfig>({
    key: 'client-config',
    defaultValue: {
      title: 'ZARRAR',
      slogan: 'Hack for fun not for profit',
      portMapping: ContainerPortMappingType.Default,
      footerInfo: null,
      customTheme: null,
      defaultLifetime: 120,
      extensionDuration: 120,
      renewalWindow: 10,
    },
  })

  useEffect(() => {
    if (config) {
      setClientConfig(config)
    }
  }, [config])

  return { config: config ?? clientConfig, error, mutate }
}

export const useCaptchaConfig = () => {
  const { data, error, mutate } = api.info.useInfoGetClientCaptchaInfo({
    refreshInterval: 0,
    revalidateOnFocus: false,
    revalidateOnReconnect: false,
    refreshWhenHidden: false,
    shouldRetryOnError: false,
    refreshWhenOffline: false,
  })

  return { info: data, error, mutate }
}

export const ValidatedRepoMeta = () => {
  const { sha, rawTag, timestamp, buildTime: buildtime } = RepoMeta

  const tag = rawTag.replace(/-.*$/, '')

  const valid =
    timestamp.length === 20 &&
    buildtime.isValid() &&
    sha.length === 40 &&
    (/^v\d+\.\d+\.\d+$/i.test(tag) || tag === 'develop')

  return { valid, tag, ...RepoMeta }
}

const showBanner = () => {
  const { sha, rawTag: tag, buildTime, repo, valid } = ValidatedRepoMeta()
  const padding = ' '.repeat(45)

  const bannerClr = ['color: #4ccaaa', 'color: unset']
  const textClr = ['font-weight: bold', 'font-weight: bold; color: #4ccaaa']
  const badClr = ['font-weight: bold', 'font-weight: bold; color: #fe3030']

  // BURAQ Banner Block
  // Core licensed under Custom THE ARZENS License; certain components under Custom THE ARZENS License.
  // See NOTICE and LICENSE_ADDENDUM.txt for attribution & trademark guidance.
  const current = new Date().getFullYear()

  const banner = `
  ██████╗ ███████╗           ██████╗████████╗███████╗
 ██╔════╝ ╚══███╔╝ %c ██╗██╗ %c ██╔════╝╚══██╔══╝██╔════╝
 ██║  ███╗  ███╔╝  %c ╚═╝╚═╝ %c ██║        ██║   █████╗
 ██║   ██║ ███╔╝   %c ██╗██╗ %c ██║        ██║   ██╔══╝
 ╚██████╔╝███████╗ %c ╚═╝╚═╝ %c ╚██████╗   ██║   ██║
  ╚═════╝ ╚══════╝           ╚═════╝   ╚═╝   ╚═╝
  ${padding}%c@ %c${valid ? tag : `v${__BURAQ_VERSION__}`}

%cCopyright (C) 2022-${current}, THE ARZENS, All rights reserved.

%cLicense  : %cCustom THE ARZENS License (Core)
%cLicense  : %cCustom THE ARZENS License
%cCommit   : %c${valid ? sha : 'Release build'}
%cBuilt at : %c${buildTime.format('YYYY-MM-DDTHH:mm:ssZ')}
%cIssues   : %c${repo}/issues
 `

  // rewrite the show banner function with %c and css
  console.log(
    banner,
    ...bannerClr.concat(bannerClr, bannerClr, bannerClr),
    ...(valid ? textClr : badClr),
    'font-weight: bold',
    ...textClr.concat(textClr, valid ? textClr : badClr, textClr, textClr)
  )
}

export const useBanner = () => {
  const mounted = useRef(false)
  useEffect(() => {
    if (!mounted.current) {
      showBanner()
      mounted.current = true
    }
  }, [])
}
