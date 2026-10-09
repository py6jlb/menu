import { describe, it, expect } from 'vitest'
import { createApp, defineComponent } from 'vue'
import { useOnlineStatus } from './useOnlineStatus'

function mountStatus() {
  let api
  const Comp = defineComponent({
    setup() {
      api = useOnlineStatus()
      return () => null
    }
  })
  const el = document.createElement('div')
  const app = createApp(Comp)
  app.mount(el)
  return { api, app }
}

describe('useOnlineStatus', () => {
  it('начинает онлайн и реагирует на события offline/online', () => {
    const { api, app } = mountStatus()

    expect(api.online.value).toBe(true)

    window.dispatchEvent(new Event('offline'))
    expect(api.online.value).toBe(false)

    window.dispatchEvent(new Event('online'))
    expect(api.online.value).toBe(true)

    app.unmount()
  })

  it('после размонтирования не слушает события', () => {
    const { api, app } = mountStatus()
    app.unmount()

    window.dispatchEvent(new Event('offline'))
    expect(api.online.value).toBe(true)
  })
})
