import { describe, it, expect, vi, afterEach } from 'vitest'
import { API_CACHE_NAME, isCacheableApiRequest, clearApiCache } from './apiCache'

function match(path, method = 'GET') {
  return isCacheableApiRequest({
    url: new URL(`https://example.test${path}`),
    request: { method }
  })
}

describe('isCacheableApiRequest', () => {
  it('кэширует читающие экраны', () => {
    const paths = [
      '/api/shopping-list',
      '/api/plans/week/2026-09-07',
      '/api/recipes',
      '/api/recipes/abc',
      '/api/families',
      '/api/settings',
      '/api/ingredients/autocomplete',
      '/api/shared/token123'
    ]
    for (const path of paths) {
      expect(match(path), path).toBe(true)
    }
  })

  it('не кэширует auth, admin, управление ссылками и подбор', () => {
    const paths = [
      '/api/auth/login',
      '/api/auth/me',
      '/api/admin/unlock',
      '/api/recipes/abc/share',
      '/api/recipes/abc/share/regenerate',
      '/api/recipes/match',
      '/api/shared/token123/import'
    ]
    for (const path of paths) {
      expect(match(path), path).toBe(false)
    }
  })

  it('не кэширует изменяющие методы даже на читающих путях', () => {
    expect(match('/api/shopping-list', 'POST')).toBe(false)
    expect(match('/api/recipes/abc', 'PUT')).toBe(false)
  })
})

describe('clearApiCache', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('удаляет runtime-кэш API по имени', async () => {
    const del = vi.fn().mockResolvedValue(true)
    vi.stubGlobal('caches', { delete: del })

    await clearApiCache()

    expect(del).toHaveBeenCalledWith(API_CACHE_NAME)
  })

  it('без Cache Storage не падает', async () => {
    vi.stubGlobal('caches', undefined)

    await expect(clearApiCache()).resolves.toBeUndefined()
  })
})
