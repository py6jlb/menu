<script setup>
// Регистрация service worker и запрос обновления: новая версия применяется
// по нажатию, а не молча — пользователь может быть посреди заполнения формы.
import { useRegisterSW } from 'virtual:pwa-register/vue'

const { needRefresh, updateServiceWorker } = useRegisterSW()

function update() {
  updateServiceWorker(true)
}
</script>

<template>
  <div v-if="needRefresh" class="update-banner" role="status">
    <span>Доступна новая версия приложения.</span>
    <button type="button" class="btn btn--primary btn--small" @click="update">
      Обновить
    </button>
  </div>
</template>
