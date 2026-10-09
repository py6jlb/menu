import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { VitePWA } from 'vite-plugin-pwa'
import { PWA_MANIFEST } from './src/pwa/manifest'
import { API_CACHE_NAME, isCacheableApiRequest } from './src/pwa/apiCache'

const SEVEN_DAYS_SECONDS = 7 * 24 * 60 * 60

export default defineConfig({
  plugins: [
    vue(),
    VitePWA({
      // Обновление применяется по запросу (компонент UpdatePrompt), а не молча.
      registerType: 'prompt',
      injectRegister: false,
      manifest: PWA_MANIFEST,
      workbox: {
        // Прекэш оболочки: html/css/js + локальные шрифты, иконки, favicon.
        globPatterns: ['**/*.{js,css,html,ico,png,svg,woff2}'],
        navigateFallback: '/index.html',
        cleanupOutdatedCaches: true,
        runtimeCaching: [
          {
            // Офлайн-чтение allowlist-эндпоинтов: свежее из сети, при отсутствии — из кэша.
            // Предикат самодостаточен — Workbox сериализует его в service worker.
            urlPattern: isCacheableApiRequest,
            handler: 'NetworkFirst',
            options: {
              cacheName: API_CACHE_NAME,
              networkTimeoutSeconds: 3,
              expiration: { maxEntries: 50, maxAgeSeconds: SEVEN_DAYS_SECONDS },
              // Кэшируем только успешные ответы: 401/404 не должны залипать офлайн.
              cacheableResponse: { statuses: [200] }
            }
          }
        ]
      },
      // В dev service worker не мешает HMR; проверка установки — на сборке/preview.
      devOptions: { enabled: false }
    })
  ],
  server: {
    proxy: {
      '/api': 'http://localhost:8080',
      '/health': 'http://localhost:8080'
    }
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.js']
  }
})
