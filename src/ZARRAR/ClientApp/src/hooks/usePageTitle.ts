import { useDocumentTitle } from '@mantine/hooks'
import { useConfig } from '@Hooks/useConfig'

export const usePageTitle = (title?: string) => {
  const { config, error } = useConfig()

  const platform = error ? 'ZARRAR' : (config?.title ?? 'ZARRAR')

  useDocumentTitle(typeof title === 'string' && title.trim().length > 0 ? `${title} - ${platform}` : platform)
}
