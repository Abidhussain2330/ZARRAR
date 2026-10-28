import type { Plugin } from 'vite'

let contributorsData: any[] = []

export function fetchContributors(): Plugin {
  return {
    name: 'fetch-contributors',
    buildStart() {
      contributorsData = [{
        login: 'THE ARZENS',
        html_url: 'https://github.com/Arzen-007',
        avatar_url: '',
        contributions: 1,
      }]
    },
    resolveId(id) {
      if (id === 'virtual:contributors') return id
    },
    load(id) {
      if (id === 'virtual:contributors') return `export default ${JSON.stringify(contributorsData)}`
    },
  }
}
