import { ref, onMounted, onUnmounted } from 'vue'

/**
 * Статус сети браузера. В офлайне приложение остаётся доступным только для чтения,
 * поэтому баннер опирается на `navigator.onLine` и события `online`/`offline`.
 */
export function useOnlineStatus() {
  const online = ref(typeof navigator === 'undefined' || navigator.onLine !== false)

  function goOnline() {
    online.value = true
  }

  function goOffline() {
    online.value = false
  }

  onMounted(() => {
    window.addEventListener('online', goOnline)
    window.addEventListener('offline', goOffline)
  })

  onUnmounted(() => {
    window.removeEventListener('online', goOnline)
    window.removeEventListener('offline', goOffline)
  })

  return { online }
}
