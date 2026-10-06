import { describe, it, expect, vi } from 'vitest'
import { useRouteResource, RESOURCE_LOAD_ERROR } from './useRouteResource'

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function ok(data) {
  return { response: { status: 200 }, data }
}

describe('useRouteResource — смена ключа маршрута', () => {
  it('смена share-token: поздний ответ старого токена не заменяет новый', async () => {
    const slowT1 = deferred()
    const fastT2 = deferred()
    const fetchResource = vi
      .fn()
      .mockImplementationOnce(() => slowT1.promise)
      .mockImplementationOnce(() => fastT2.promise)

    const state = useRouteResource(fetchResource)

    const first = state.load('t1')
    const second = state.load('t2')

    fastT2.resolve(ok({ name: 'рецепт T2' }))
    await second
    expect(state.resource.value.name).toBe('рецепт T2')

    slowT1.resolve(ok({ name: 'рецепт T1' }))
    await first

    expect(state.key.value).toBe('t2')
    expect(state.resource.value.name).toBe('рецепт T2')
    expect(state.loading.value).toBe(false)
  })

  it('смена ID не переносит старый ресурс под новый маршрут', async () => {
    const state = useRouteResource(
      vi.fn().mockResolvedValueOnce(ok({ id: 'A', name: 'A' })).mockResolvedValueOnce(ok({ id: 'B', name: 'B' }))
    )

    await state.load('A')
    expect(state.resource.value.id).toBe('A')

    await state.load('B')
    expect(state.resource.value.id).toBe('B')
  })
})

describe('useRouteResource — сетевые отказы', () => {
  it('сетевой отказ завершает pending, даёт сообщение и позволяет повторить', async () => {
    const fetchResource = vi
      .fn()
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce(ok({ name: 'восстановлен' }))
    const state = useRouteResource(fetchResource)

    await state.load('t1')

    expect(state.loading.value).toBe(false)
    expect(state.resource.value).toBeNull()
    expect(state.error.value).toBe(RESOURCE_LOAD_ERROR)

    await state.reload()

    expect(state.loading.value).toBe(false)
    expect(state.error.value).toBe('')
    expect(state.resource.value.name).toBe('восстановлен')
  })

  it('неразобранный ответ даёт сообщение и не оставляет ресурс', async () => {
    const state = useRouteResource(vi.fn().mockResolvedValue({ response: { status: 200 }, data: null }))

    await state.load('t1')

    expect(state.resource.value).toBeNull()
    expect(state.error.value).toBe(RESOURCE_LOAD_ERROR)
  })

  it('404 использует понятное сообщение', async () => {
    const state = useRouteResource(vi.fn().mockResolvedValue({ response: { status: 404 }, data: null }), {
      notFound: 'Ссылка недействительна.'
    })

    await state.load('bad')

    expect(state.error.value).toBe('Ссылка недействительна.')
    expect(state.loading.value).toBe(false)
  })
})
