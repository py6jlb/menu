import { ref, computed, onBeforeUnmount } from 'vue'

export const RESEND_COOLDOWN_SECONDS = 300

export function parseCooldownSeconds(message) {
  const match = /(\d+)\s*сек/.exec(message || '')
  return match ? Number(match[1]) : 0
}

/**
 * Секунды ожидания из заголовка `Retry-After`; запасной путь — текст ошибки.
 * Заголовок приоритетнее: он единый для всех ограничиваемых операций.
 */
export function retryAfterSeconds(response, data) {
  const header = response?.headers?.get?.('Retry-After')
  const fromHeader = Number(header)
  if (Number.isFinite(fromHeader) && fromHeader > 0) return Math.floor(fromHeader)
  return parseCooldownSeconds(data?.error)
}

/** Понятное сообщение об ограничении; объясняет срок повтора. */
export function rateLimitMessage(response, data) {
  const seconds = retryAfterSeconds(response, data)
  if (seconds > 0) return `Слишком много запросов. Повторите через ${seconds} сек.`
  return data?.error || 'Слишком много запросов. Попробуйте позже.'
}

/**
 * Обратный отсчёт до повторной отправки кода: общий секундный таймер и метка `m:ss`.
 */
export function useCooldown() {
  const cooldown = ref(0)
  let timer = null

  const cooldownLabel = computed(() => {
    const minutes = Math.floor(cooldown.value / 60)
    const seconds = cooldown.value % 60
    return `${minutes}:${String(seconds).padStart(2, '0')}`
  })

  function stopTimer() {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
  }

  function startCooldown(seconds) {
    cooldown.value = Math.max(0, Math.floor(seconds))
    stopTimer()
    if (cooldown.value === 0) return
    timer = setInterval(() => {
      cooldown.value -= 1
      if (cooldown.value <= 0) {
        cooldown.value = 0
        stopTimer()
      }
    }, 1000)
  }

  onBeforeUnmount(stopTimer)

  return { cooldown, cooldownLabel, startCooldown, stopTimer }
}
