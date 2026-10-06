import { describe, it, expect, vi, beforeEach } from 'vitest'

vi.mock('../api/families', () => ({
  getMyFamily: vi.fn(),
  createFamily: vi.fn(),
  joinFamily: vi.fn(),
  regenerateInviteCode: vi.fn(),
  removeMember: vi.fn()
}))

import {
  getMyFamily,
  createFamily,
  joinFamily,
  regenerateInviteCode,
  removeMember
} from '../api/families'
import {
  useFamily,
  FAMILY_LOAD_ERROR,
  FAMILY_JOIN_ERROR,
  FAMILY_COPY_ERROR,
  FAMILY_REMOVE_ERROR,
  FAMILY_NETWORK_ERROR
} from './useFamily'

function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function ok(status, data) {
  return { response: { status }, data }
}

const familyDto = (extra = {}) => ({
  id: 'fam-1',
  name: 'Семья',
  inviteCode: 'ABC123',
  ownerId: 'user-1',
  members: [{ id: 'user-1', email: 'owner@example.com', role: 'Owner' }],
  ...extra
})

function setClipboard(writeText) {
  Object.defineProperty(navigator, 'clipboard', {
    configurable: true,
    value: { writeText }
  })
}

beforeEach(() => {
  vi.clearAllMocks()
  setClipboard(vi.fn().mockResolvedValue(undefined))
})

describe('useFamily — первоначальная загрузка', () => {
  it('200 сохраняет семью и снимает загрузку', async () => {
    getMyFamily.mockResolvedValue(ok(200, familyDto()))
    const state = useFamily()

    await state.load()

    expect(state.family.value.name).toBe('Семья')
    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toBe('')
  })

  it('404 оставляет пустое состояние без ошибки', async () => {
    getMyFamily.mockResolvedValue(ok(404, { error: 'Семья не найдена.' }))
    const state = useFamily()

    await state.load()

    expect(state.family.value).toBeNull()
    expect(state.loadError.value).toBe('')
  })

  it('сетевой отказ даёт ошибку загрузки и допускает повтор', async () => {
    getMyFamily
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce(ok(200, familyDto({ name: 'Восстановлена' })))
    const state = useFamily()

    await state.load()

    expect(state.loading.value).toBe(false)
    expect(state.loadError.value).toBe(FAMILY_LOAD_ERROR)
    expect(state.family.value).toBeNull()

    await state.load()

    expect(state.loadError.value).toBe('')
    expect(state.family.value.name).toBe('Восстановлена')
  })
})

describe('useFamily — создание', () => {
  it('успех сохраняет семью и снимает ожидание', async () => {
    createFamily.mockResolvedValue(ok(201, familyDto()))
    const state = useFamily()

    const result = await state.create('Семья')

    expect(result).toBe(true)
    expect(state.family.value.id).toBe('fam-1')
    expect(state.creating.value).toBe(false)
    expect(state.createError.value).toBe('')
  })

  it('конфликт показывает сообщение, не пряча форму', async () => {
    createFamily.mockResolvedValue(ok(409, { error: 'Вы уже состоите в семье.' }))
    const state = useFamily()

    await state.create('Семья')

    expect(state.family.value).toBeNull()
    expect(state.createError.value).toBe('Вы уже состоите в семье.')
    expect(state.creating.value).toBe(false)
  })

  it('сетевой отказ завершает ожидание и позволяет повторить', async () => {
    createFamily
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce(ok(201, familyDto()))
    const state = useFamily()

    await state.create('Семья')
    expect(state.createError.value).toBe(FAMILY_NETWORK_ERROR)
    expect(state.creating.value).toBe(false)

    await state.create('Семья')
    expect(state.createError.value).toBe('')
    expect(state.family.value.id).toBe('fam-1')
  })

  it('двойной клик не запускает конкурирующий запрос', async () => {
    const pending = deferred()
    createFamily.mockReturnValue(pending.promise)
    const state = useFamily()

    const first = state.create('Семья')
    const second = state.create('Семья')

    expect(createFamily).toHaveBeenCalledTimes(1)
    expect(state.creating.value).toBe(true)

    pending.resolve(ok(201, familyDto()))
    await Promise.all([first, second])

    expect(createFamily).toHaveBeenCalledTimes(1)
    expect(state.creating.value).toBe(false)
  })
})

describe('useFamily — вступление по коду', () => {
  it('неверный код показывает ошибку, форма остаётся доступной для повтора', async () => {
    joinFamily
      .mockResolvedValueOnce(ok(404, { error: 'Семья по такому коду не найдена.' }))
      .mockResolvedValueOnce(ok(200, familyDto()))
    const state = useFamily()

    await state.join('NOPE1234')

    expect(state.family.value).toBeNull()
    expect(state.joinError.value).toBe('Семья по такому коду не найдена.')
    expect(state.joining.value).toBe(false)

    await state.join('ABC123')

    expect(state.joinError.value).toBe('')
    expect(state.family.value.id).toBe('fam-1')
  })

  it('сетевой отказ даёт понятное сообщение и снимает ожидание', async () => {
    joinFamily.mockRejectedValue(new Error('offline'))
    const state = useFamily()

    await state.join('ABC123')

    expect(state.joinError.value).toBe(FAMILY_NETWORK_ERROR)
    expect(state.joining.value).toBe(false)
  })

  it('двойной клик не запускает конкурирующий запрос', async () => {
    const pending = deferred()
    joinFamily.mockReturnValue(pending.promise)
    const state = useFamily()

    const first = state.join('ABC123')
    const second = state.join('ABC123')

    expect(joinFamily).toHaveBeenCalledTimes(1)

    pending.resolve(ok(200, familyDto()))
    await Promise.all([first, second])

    expect(state.joining.value).toBe(false)
  })
})

describe('useFamily — копирование инвайт-кода', () => {
  it('успех помечает код скопированным', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    setClipboard(writeText)
    const state = useFamily()
    state.family.value = familyDto()

    const result = await state.copyInviteCode()

    expect(result).toBe(true)
    expect(writeText).toHaveBeenCalledWith('ABC123')
    expect(state.copied.value).toBe(true)
    expect(state.copyError.value).toBe('')
  })

  it('отказ clipboard не скрывает семью и код', async () => {
    setClipboard(vi.fn().mockRejectedValue(new Error('denied')))
    const state = useFamily()
    state.family.value = familyDto()

    const result = await state.copyInviteCode()

    expect(result).toBe(false)
    expect(state.copyError.value).toBe(FAMILY_COPY_ERROR)
    expect(state.family.value).not.toBeNull()
    expect(state.family.value.inviteCode).toBe('ABC123')
    expect(state.loadError.value).toBe('')
  })

  it('отсутствие clipboard даёт ошибку, не скрывая семью', async () => {
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined })
    const state = useFamily()
    state.family.value = familyDto()

    await state.copyInviteCode()

    expect(state.copyError.value).toBe(FAMILY_COPY_ERROR)
    expect(state.family.value).not.toBeNull()
  })
})

describe('useFamily — перегенерация кода', () => {
  it('успех обновляет инвайт-код', async () => {
    regenerateInviteCode.mockResolvedValue(ok(200, { inviteCode: 'NEW99999' }))
    const state = useFamily()
    state.family.value = familyDto()

    await state.regenerate()

    expect(regenerateInviteCode).toHaveBeenCalledWith('fam-1')
    expect(state.family.value.inviteCode).toBe('NEW99999')
    expect(state.regenerateError.value).toBe('')
  })

  it('отказ не меняет код и не скрывает семью', async () => {
    regenerateInviteCode.mockResolvedValue(ok(403, { error: 'Нет прав.' }))
    const state = useFamily()
    state.family.value = familyDto()

    await state.regenerate()

    expect(state.family.value.inviteCode).toBe('ABC123')
    expect(state.regenerateError.value).toBe('Нет прав.')
    expect(state.regenerating.value).toBe(false)
  })

  it('двойной клик не запускает конкурирующий запрос', async () => {
    const pending = deferred()
    regenerateInviteCode.mockReturnValue(pending.promise)
    const state = useFamily()
    state.family.value = familyDto()

    const first = state.regenerate()
    const second = state.regenerate()

    expect(regenerateInviteCode).toHaveBeenCalledTimes(1)

    pending.resolve(ok(200, { inviteCode: 'NEW' }))
    await Promise.all([first, second])

    expect(state.regenerating.value).toBe(false)
  })
})

describe('useFamily — удаление участника', () => {
  it('успех убирает участника из списка', async () => {
    removeMember.mockResolvedValue(ok(204, null))
    const member = { id: 'user-2', email: 'member@example.com', role: 'Member' }
    const state = useFamily()
    state.family.value = familyDto({ members: [...familyDto().members, member] })

    await state.removeMember(member)

    expect(removeMember).toHaveBeenCalledWith('fam-1', 'user-2')
    expect(state.family.value.members).toHaveLength(1)
    expect(state.removeError.value).toBe('')
  })

  it('сбой удаления не исчезает вместе со списком участников', async () => {
    removeMember.mockRejectedValue(new Error('offline'))
    const member = { id: 'user-2', email: 'member@example.com', role: 'Member' }
    const state = useFamily()
    state.family.value = familyDto({ members: [...familyDto().members, member] })

    await state.removeMember(member)

    expect(state.removeError.value).toBe(FAMILY_NETWORK_ERROR)
    expect(state.removing.value).toBe(false)
    expect(state.family.value.members).toHaveLength(2)
    expect(state.family.value.members).toContainEqual(member)
  })

  it('двойной клик не запускает конкурирующий запрос', async () => {
    const pending = deferred()
    removeMember.mockReturnValue(pending.promise)
    const member = { id: 'user-2', email: 'member@example.com', role: 'Member' }
    const state = useFamily()
    state.family.value = familyDto()

    const first = state.removeMember(member)
    const second = state.removeMember(member)

    expect(removeMember).toHaveBeenCalledTimes(1)

    pending.resolve(ok(204, null))
    await Promise.all([first, second])

    expect(state.removing.value).toBe(false)
  })
})
