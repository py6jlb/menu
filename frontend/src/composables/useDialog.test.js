import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { useDialog } from './useDialog'

function mountDialog(html) {
  const overlay = document.createElement('div')
  overlay.innerHTML = html
  document.body.appendChild(overlay)
  return overlay
}

function keydown(key, shiftKey = false) {
  const event = new KeyboardEvent('keydown', { key, shiftKey, bubbles: true, cancelable: true })
  return event
}

let trigger
let overlay

beforeEach(() => {
  trigger = document.createElement('button')
  trigger.textContent = 'Открыть'
  document.body.appendChild(trigger)
  overlay = mountDialog(
    '<div class="picker" tabindex="-1">' +
      '<input id="first" />' +
      '<button id="middle">Середина</button>' +
      '<button id="last">Последняя</button>' +
      '</div>'
  )
})

afterEach(() => {
  overlay.remove()
  trigger.remove()
})

function makeDialog(options = {}) {
  const dialog = useDialog(options)
  dialog.container.value = overlay.querySelector('.picker')
  return dialog
}

describe('useDialog — фокус при открытии', () => {
  it('переводит фокус на первый доступный элемент', async () => {
    const dialog = makeDialog()
    await dialog.open(trigger)
    expect(document.activeElement).toBe(overlay.querySelector('#first'))
  })

  it('если фокусируемых нет, фокусирует сам контейнер', async () => {
    const empty = mountDialog('<div class="picker" tabindex="-1"></div>')
    const dialog = useDialog()
    dialog.container.value = empty.querySelector('.picker')
    await dialog.open(trigger)
    expect(document.activeElement).toBe(empty.querySelector('.picker'))
    empty.remove()
  })

  it('без триггера запоминает текущий активный элемент', async () => {
    trigger.focus()
    const dialog = makeDialog()
    await dialog.open()
    expect(document.activeElement).toBe(overlay.querySelector('#first'))
    dialog.onKeydown(keydown('Escape'))
    expect(document.activeElement).toBe(trigger)
  })
})

describe('useDialog — удержание фокуса', () => {
  it('Tab с последнего элемента возвращает на первый', () => {
    const dialog = makeDialog()
    const last = overlay.querySelector('#last')
    const first = overlay.querySelector('#first')
    last.focus()

    const event = keydown('Tab')
    dialog.onKeydown(event)

    expect(event.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(first)
  })

  it('Shift+Tab с первого возвращает на последний', () => {
    const dialog = makeDialog()
    const last = overlay.querySelector('#last')
    const first = overlay.querySelector('#first')
    first.focus()

    const event = keydown('Tab', true)
    dialog.onKeydown(event)

    expect(event.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(last)
  })

  it('обычный Tab внутри не перехватывается', () => {
    const dialog = makeDialog()
    const first = overlay.querySelector('#first')
    first.focus()

    const event = keydown('Tab')
    dialog.onKeydown(event)

    expect(event.defaultPrevented).toBe(false)
  })

  it('Tab, когда фокус вне диалога, переносит на первый элемент', () => {
    const dialog = makeDialog()
    trigger.focus()

    const event = keydown('Tab')
    dialog.onKeydown(event)

    expect(event.defaultPrevented).toBe(true)
    expect(document.activeElement).toBe(overlay.querySelector('#first'))
  })
})

describe('useDialog — Escape и закрытие', () => {
  it('Escape закрывает диалог и возвращает фокус к вызвавшему действию', async () => {
    const onClose = vi.fn()
    const dialog = makeDialog({ onClose })
    await dialog.open(trigger)

    const event = keydown('Escape')
    dialog.onKeydown(event)

    expect(event.defaultPrevented).toBe(true)
    expect(onClose).toHaveBeenCalledTimes(1)
    expect(document.activeElement).toBe(trigger)
  })

  it('close() не восстанавливает фокус, когда это явно отключено', async () => {
    const onClose = vi.fn()
    const dialog = makeDialog({ onClose })
    await dialog.open(trigger)
    overlay.querySelector('#middle').focus()

    dialog.close({ restoreFocus: false })

    expect(onClose).toHaveBeenCalledTimes(1)
    expect(document.activeElement).toBe(overlay.querySelector('#middle'))
  })

  it('повторное закрытие не падает и не восстанавливает фокус дважды', async () => {
    const onClose = vi.fn()
    const dialog = makeDialog({ onClose })
    await dialog.open(trigger)
    dialog.close()
    dialog.close()
    expect(onClose).toHaveBeenCalledTimes(2)
  })
})
