<script setup>
import { ref, onMounted } from 'vue'

const state = ref('checking')
const message = ref('')

async function checkApi() {
  state.value = 'checking'
  try {
    const res = await fetch('/health')
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    const data = await res.json()
    state.value = 'connected'
    message.value = data.status || 'ok'
  } catch (err) {
    state.value = 'disconnected'
    message.value = err.message
  }
}

onMounted(checkApi)
</script>

<template>
  <main>
    <h1>Меню для домохозяек</h1>
    <button @click="checkApi">Проверить API</button>
    <p v-if="state === 'checking'">Проверка подключения…</p>
    <p v-else-if="state === 'connected'">API connected</p>
    <p v-else>API недоступен ({{ message }})</p>
  </main>
</template>

<style>
body {
  font-family: system-ui, sans-serif;
  margin: 0;
  padding: 2rem;
}
</style>
