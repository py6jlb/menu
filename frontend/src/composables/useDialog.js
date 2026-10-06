import { nextTick, ref } from 'vue'

const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])'
].join(',')

/**
 * Доступность модального диалога: перевод фокуса внутрь при открытии, удержание
 * фокуса на Tab/Shift+Tab, закрытие по Escape и возврат фокуса к вызвавшему
 * действию. Фокус-ловушка работает на уровне контейнера (события всплывают от
 * элементов внутри), поэтому не требует глобальных слушателей.
 *
 * Контейнер должен иметь `tabindex="-1"`, чтобы принимать фокус, когда внутри
 * нет доступных элементов. Разметка диалога задаёт `role="dialog"` и
 * `aria-modal="true"`; композабл отвечает только за поведение фокуса.
 */
export function useDialog(options = {}) {
  const container = ref(null)
  let returnFocusTo = null

  function focusableElements() {
    if (!container.value) return []
    return Array.from(container.value.querySelectorAll(FOCUSABLE_SELECTOR))
  }

  function focusFirst() {
    const target = focusableElements()[0]
    if (target) target.focus()
    else if (container.value) container.value.focus()
  }

  /** Запомнить вызвавшее действие и перевести фокус внутрь диалога. */
  function open(trigger = null) {
    returnFocusTo = trigger || document.activeElement
    return nextTick(focusFirst)
  }

  /** Закрыть диалог и вернуть фокус к вызвавшему действию. */
  function close() {
    const target = returnFocusTo
    returnFocusTo = null
    if (target && typeof target.focus === 'function' && target.isConnected) {
      target.focus()
    }
    if (typeof options.onClose === 'function') options.onClose()
  }

  function onKeydown(event) {
    if (event.key === 'Escape') {
      event.preventDefault()
      event.stopPropagation()
      close()
      return
    }
    if (event.key !== 'Tab') return

    const items = focusableElements()
    if (items.length === 0) {
      event.preventDefault()
      if (container.value) container.value.focus()
      return
    }

    const first = items[0]
    const last = items[items.length - 1]
    const active = document.activeElement
    // Контейнер с tabindex="-1" считаем «вне» списка: Tab с него уходит на первый
    // элемент, Shift+Tab — на последний, чтобы фокус не покидал диалог.
    const inside = Boolean(
      container.value && container.value.contains(active) && active !== container.value
    )

    if (event.shiftKey) {
      if (!inside || active === first) {
        event.preventDefault()
        last.focus()
      }
    } else if (!inside || active === last) {
      event.preventDefault()
      first.focus()
    }
  }

  return { container, open, close, onKeydown }
}
