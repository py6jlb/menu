<script setup>
import { ref, onMounted } from 'vue'
import { useAuth } from '../stores/auth'

const { state, isEmailVerified } = useAuth()

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
  <section>
    <div class="hero">
      <div class="hero-text">
        <h2>Добро пожаловать, {{ state.user?.email }}! 👋</h2>
        <p>Планируйте меню, покупайте продукты и готовьте с удовольствием — всё для вашей семьи в одном месте.</p>
        <div class="hero-actions">
          <router-link v-if="isEmailVerified" to="/recipes/new" class="btn btn--primary">Создать рецепт</router-link>
          <router-link v-else to="/verify" class="btn btn--primary">Подтвердить почту</router-link>
          <router-link to="/plan" class="btn btn--ghost">Перейти к плану</router-link>
          <router-link to="/shopping" class="btn btn--ghost">Список покупок</router-link>
        </div>
      </div>
      <div class="hero-emoji">🍲</div>
    </div>

    <button
      type="button"
      class="health-chip"
      :class="`health-${health.state}`"
      title="Проверить подключение к API"
      @click="checkApi"
    >
      <span class="dot" aria-hidden="true"></span>
      <span aria-live="polite">
        <template v-if="health.state === 'checking'">Проверка…</template>
        <template v-else-if="health.state === 'connected'">API подключено</template>
        <template v-else>API недоступно</template>
      </span>
    </button>

    <div class="quick-grid">
      <router-link to="/recipes" class="card quick-card">
        <span class="quick-icon">📖</span>
        <span class="quick-title">Рецепты</span>
        <span class="quick-desc">Смотрите и создавайте свои рецепты</span>
      </router-link>
      <router-link to="/plan" class="card quick-card">
        <span class="quick-icon">🗓️</span>
        <span class="quick-title">План на неделю</span>
        <span class="quick-desc">Составьте меню на каждый день</span>
      </router-link>
      <router-link to="/family" class="card quick-card">
        <span class="quick-icon">👨‍👩‍👧</span>
        <span class="quick-title">Семья</span>
        <span class="quick-desc">Делитесь меню с близкими</span>
      </router-link>
    </div>
  </section>
</template>

<style scoped>
.hero {
  display: flex;
  align-items: center;
  gap: 1.5rem;
  background: linear-gradient(135deg, #f9ead9, #faf6f0);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: clamp(1.5rem, 4vw, 2.5rem);
  box-shadow: var(--shadow-sm);
}

.hero-text {
  flex: 1;
}

.hero-text h2 {
  margin: 0 0 0.5rem;
  font-size: clamp(1.35rem, 3.5vw, 2rem);
}

.hero-text p {
  color: var(--text-soft);
  margin: 0 0 1.5rem;
  max-width: 52ch;
}

.hero-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 0.6rem;
}

.hero-emoji {
  font-size: clamp(3.5rem, 8vw, 6rem);
  line-height: 1;
  display: none;
}

@media (min-width: 600px) {
  .hero-emoji {
    display: block;
  }
}

.health-chip {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  margin-top: 1rem;
  padding: 0.45rem 0.9rem;
  border: none;
  border-radius: 999px;
  font-family: inherit;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  background: var(--surface-2);
  color: var(--text-soft);
  min-height: 40px;
}

.health-chip .dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--text-faint);
}

.health-connected .dot {
  background: var(--success);
}

.health-disconnected .dot {
  background: var(--danger);
}

.health-disconnected {
  background: var(--danger-bg);
  color: var(--danger);
}

.quick-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1rem;
  margin-top: 1.5rem;
}

@media (min-width: 640px) {
  .quick-grid {
    grid-template-columns: repeat(3, 1fr);
  }
}

.quick-card {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  text-decoration: none;
  color: var(--text);
  transition: transform 0.15s ease, box-shadow 0.15s ease;
}

.quick-card:hover {
  transform: translateY(-3px);
  box-shadow: var(--shadow-md);
}

.quick-icon {
  font-size: 1.75rem;
  line-height: 1;
}

.quick-title {
  font-weight: 800;
  font-size: 1.05rem;
}

.quick-desc {
  color: var(--text-soft);
  font-size: 0.88rem;
}
</style>
