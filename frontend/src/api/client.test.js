import { describe, it, expect, vi, afterEach } from 'vitest'
import { revisionQuery, postJson, apiJson } from './client'

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('revisionQuery', () => {
  it('пустая строка, если ревизия неизвестна', () => {
    expect(revisionQuery('revision', null)).toBe('')
    expect(revisionQuery('revision', undefined)).toBe('')
  })

  it('0 — валидная ревизия, а не «неизвестна»', () => {
    expect(revisionQuery('revision', 0)).toBe('?revision=0')
  })

  it('использует переданное имя параметра и экранирует значение', () => {
    expect(revisionQuery('expectedRevision', 5)).toBe('?expectedRevision=5')
    expect(revisionQuery('revision', 'a b')).toBe('?revision=a%20b')
  })
})

describe('postJson', () => {
  it('сериализует тело и ставит Content-Type: application/json', async () => {
    const fetchMock = vi.fn(async () => ({ status: 200, ok: true, text: async () => '{"ok":true}' }))
    vi.stubGlobal('fetch', fetchMock)

    const { response, data } = await postJson('/api/x', { a: 1 })

    expect(response.status).toBe(200)
    expect(data).toEqual({ ok: true })
    const [url, options] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/x')
    expect(options.method).toBe('POST')
    expect(options.body).toBe('{"a":1}')
    expect(options.headers['Content-Type']).toBe('application/json')
  })
})

describe('apiJson', () => {
  it('не оборачивает FormData и не ставит Content-Type', async () => {
    const fetchMock = vi.fn(async () => ({ status: 200, ok: true, text: async () => '' }))
    vi.stubGlobal('fetch', fetchMock)

    const form = new FormData()
    await apiJson('/api/x', { method: 'PUT', body: form })

    const [, options] = fetchMock.mock.calls[0]
    expect(options.body).toBe(form)
    expect(options.headers['Content-Type']).toBeUndefined()
  })
})
