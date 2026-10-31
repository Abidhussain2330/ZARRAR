import react from '@vitejs/plugin-react'
import { readFileSync } from 'fs'
import process from 'process'
import { defineConfig, loadEnv } from 'vite'
import banner from 'vite-plugin-banner'
import { optimizeCssModules } from 'vite-plugin-optimize-css-modules'
import Pages from 'vite-plugin-pages'
import webfontDownload from 'vite-plugin-webfont-dl'
import { fetchContributors } from './plugins/vite-fetch-contributors'
import { i18nVirtualManifest } from './plugins/vite-i18n-virtual-manifest'

export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, process.cwd())

  const TARGET = env.VITE_BACKEND_URL ?? 'http://localhost:8080'
  const current = new Date()
  const VERSION: string = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf-8')).version

  // Product identity is ZARRAR (built on ZARRAR); the upstream copyright line (below) stays because
  // both Custom THE ARZENS License and the Restricted License require that attribution to travel
  // with the build output.
  const BANNER =
    `/* ZARRAR (ZARRAR) @${env.VITE_APP_GIT_NAME ?? 'unknown'}\n * \n` +
    ` * License   : Custom THE ARZENS License (Core)\n` +
    ` * License   : Custom THE ARZENS License\n` +
    ` * Version   : ${VERSION}\n` +
    ` * Commit    : ${env.VITE_APP_GIT_SHA ?? 'release build'}\n` +
    ` * Build     : ${env.VITE_APP_BUILD_TIMESTAMP ?? current.toISOString()}\n` +
    ` * Derived from the ZARRAR project.\n` +
    ` * Copyright (C) 2022-${current.getFullYear()} THE ARZENS. All Rights Reserved.\n */`

  console.log(`Using backend URL: ${TARGET}`)

  return {
    base: command === 'serve' ? '/' : '/admin/',
    server: {
      port: 63000,
      proxy: {
        // Container access uses /api/proxy/{id} as a raw TCP-over-WebSocket endpoint.
        // Keep ordinary HTTP API forwarding and explicitly upgrade proxy websocket requests.
        '/api': { target: TARGET, ws: true },
        '/swagger': TARGET,
        '/assets': TARGET,
        '/hub': { target: TARGET.replace('http', 'ws'), ws: true },
        '/favicon.webp': TARGET,
      },
    },
    resolve: {
      tsconfigPaths: true,
    },
    preview: { port: 64000 },
    build: {
      outDir: 'build',
      assetsDir: 'static',
      cssMinify: 'esbuild',
      cssCodeSplit: false,
      chunkSizeWarningLimit: 2400,
      reportCompressedSize: true,
      rolldownOptions: {
        output: {
          hashCharacters: 'base36',
          chunkFileNames: 'static/[hash].js',
          assetFileNames: 'static/[hash].[ext]',
          entryFileNames: 'static/[name].[hash].js',
        },
      },
    },
    html: { cspNonce: '%nonce%' },
    define: {
      __ZARRAR_VERSION__: JSON.stringify(VERSION),
    },
    plugins: [
      react(),
      banner(BANNER),
      webfontDownload(
        [
          'https://fonts.googleapis.com/css2?family=JetBrains+Mono:ital,wght@0,100..800;1,100..800&family=Lexend:wght@100..900&family=Space+Grotesk:wght@400..700&display=swap',
          // ZARRAR display faces: Orbitron for the stage/wordmark, Chakra Petch for player
          // headings, Rajdhani for dense HUD labels on the monitor stage.
          'https://fonts.googleapis.com/css2?family=Orbitron:wght@500..900&family=Chakra+Petch:wght@400;500;600;700&family=Rajdhani:wght@500;600;700&display=swap',
        ],
        {
          injectAsStyleTag: false,
          async: false,
        }
      ),
      Pages({ dirs: [{ dir: './src/pages', baseRoute: '', filePattern: '**/*.tsx' }] }),
      i18nVirtualManifest(),
      fetchContributors(),
      optimizeCssModules(),
    ],
  }
})
