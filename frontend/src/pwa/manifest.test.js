import { describe, it, expect } from 'vitest'
import { PWA_MANIFEST } from './manifest'

describe('PWA_MANIFEST', () => {
  it('идентичность приложения: имя, standalone, кремовый фон, ru', () => {
    expect(PWA_MANIFEST.name).toBe('Меню для домохозяек')
    expect(PWA_MANIFEST.short_name).toBe('Меню')
    expect(PWA_MANIFEST.display).toBe('standalone')
    expect(PWA_MANIFEST.start_url).toBe('/')
    expect(PWA_MANIFEST.scope).toBe('/')
    expect(PWA_MANIFEST.theme_color).toBe('#faf6f0')
    expect(PWA_MANIFEST.background_color).toBe('#faf6f0')
    expect(PWA_MANIFEST.lang).toBe('ru')
  })

  it('иконки: 192, 512 и maskable 512', () => {
    expect(PWA_MANIFEST.icons).toEqual([
      { src: '/pwa-192x192.png', sizes: '192x192', type: 'image/png' },
      { src: '/pwa-512x512.png', sizes: '512x512', type: 'image/png' },
      { src: '/maskable-icon-512x512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
    ])
  })
})
