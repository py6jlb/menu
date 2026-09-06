<script setup>
import { ref, onMounted } from 'vue'
import { useAuth } from '../stores/auth'

const { state } = useAuth()

const health = ref({ state: 'checking', message: '' })

async function checkApi() {
  health.value = { state: 'checking', message: '' }
  try {
    const res = await fetch('/health')
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    const data = await res.json()
    health.value = { state: 'connected', message: data.status || 'ok' }
  } catch (err) {
    health.value = { state: 'disconnected', message: err.message }
  }
}

onMounted(checkApi)
</script>

<template>
  <section class="card">
    <h2>Добро пожаловать, {{ state.user?.email }}</h2>
    <p>Вы вошли как <strong>{{ state.user?.role }}</strong>.</p>

    <h3>Проверка API</h3>
    <button type="button" @click="checkApi">Проверить API</button>
    <p v-if="health.state === 'checking'">Проверка подключения…</p>
    <p v-else-if="health.state === 'connected'">API connected</p>
    <p v-else class="error">API недоступен ({{ health.message }})</p>
  </section>
</template>
