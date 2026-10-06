<script setup>
import { ref, computed } from 'vue'
import { useRoute } from 'vue-router'
import { useAuth } from '../stores/auth'
import { sessionStore } from '../stores/storage'

const DISMISS_KEY = 'menu_planner_verify_banner_dismissed'

const route = useRoute()
const { state, isAuthenticated, isEmailVerified } = useAuth()

const dismissed = ref(sessionStore.get(DISMISS_KEY) === '1')

const visible = computed(
  () =>
    isAuthenticated.value &&
    !isEmailVerified.value &&
    route.name !== 'verify' &&
    !dismissed.value
)

function dismiss() {
  dismissed.value = true
  sessionStore.set(DISMISS_KEY, '1')
}
</script>

<template>
  <div v-if="visible" class="verify-banner" role="status">
    <span class="verify-icon" aria-hidden="true">✉️</span>
    <div class="verify-text">
      <strong>Подтвердите почту</strong>
      <span>
        Мы отправили код на {{ state.user?.email }}. Подтверждение нужно, чтобы создавать
        и редактировать рецепты, план и семью.
      </span>
    </div>
    <router-link to="/verify" class="btn btn--primary btn--small">Ввести код</router-link>
    <button type="button" class="verify-close" aria-label="Закрыть" @click="dismiss">✕</button>
  </div>
</template>

<style scoped>
.verify-banner {
  position: relative;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.75rem;
  background: var(--primary-soft);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 0.85rem 1rem;
  margin-bottom: 1.25rem;
}

.verify-icon {
  font-size: 1.5rem;
  line-height: 1;
}

.verify-text {
  flex: 1;
  min-width: 180px;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
  font-size: 0.9rem;
  color: var(--text-soft);
}

.verify-text strong {
  color: var(--text);
  font-size: 1rem;
}

.verify-close {
  position: absolute;
  top: 0.35rem;
  right: 0.45rem;
  border: none;
  background: transparent;
  color: var(--text-soft);
  font-size: 1rem;
  line-height: 1;
  padding: 0.4rem;
  min-width: 40px;
  min-height: 40px;
  border-radius: 999px;
}

.verify-close:hover {
  background: var(--surface-2);
  color: var(--text);
}
</style>
