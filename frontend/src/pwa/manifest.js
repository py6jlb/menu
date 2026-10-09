/**
 * Единый объект Web App Manifest. Импортируется конфигом Vite и юнит-тестом,
 * поэтому идентичность приложения проверяется без сборки.
 */
export const PWA_MANIFEST = {
  name: 'Меню для домохозяек',
  short_name: 'Меню',
  description: 'Семейное планирование меню: рецепты, недельный план и список покупок.',
  lang: 'ru',
  start_url: '/',
  scope: '/',
  display: 'standalone',
  theme_color: '#faf6f0',
  background_color: '#faf6f0',
  icons: [
    { src: '/pwa-192x192.png', sizes: '192x192', type: 'image/png' },
    { src: '/pwa-512x512.png', sizes: '512x512', type: 'image/png' },
    { src: '/maskable-icon-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
  ]
}
