<script setup>
import { computed, onMounted } from 'vue'
import { useShoppingList } from '../composables/useShoppingList'
import { DAYS, MEALS, weekRangeLabel, toIso } from '../constants/plan'

const GROUPS = [
  { key: 'weight', label: 'Вес', icon: '⚖️', units: ['g', 'kg'] },
  { key: 'volume', label: 'Объём', icon: '🧴', units: ['ml', 'l'] },
  { key: 'pieces', label: 'Штуки', icon: '🔢', units: ['pcs'] },
  { key: 'household', label: 'Бытовые меры', icon: '🥄', units: ['glass', 'tbsp', 'tsp', 'pinch'] }
]

function groupOf(unit) {
  return GROUPS.find((g) => g.units.includes(unit)) || GROUPS[GROUPS.length - 1]
}

const {
  weekStart,
  items,
  excluded,
  hasPlan,
  loading,
  refreshing,
  error,
  resultIsCurrent,
  resultWeekDate,
  load,
  goToWeek,
  refresh
} = useShoppingList()

const weekLabel = computed(() => weekRangeLabel(weekStart.value))
const resultWeekLabel = computed(() =>
  resultWeekDate.value ? weekRangeLabel(resultWeekDate.value) : ''
)

const groups = computed(() =>
  GROUPS.map((g) => ({
    ...g,
    items: items.value.filter((item) => groupOf(item.unit).key === g.key)
  })).filter((g) => g.items.length > 0)
)

const planWeek = computed(() => resultWeekDate.value || weekStart.value)
const planLink = computed(() => ({ name: 'plan', query: { week: toIso(planWeek.value) } }))

const showMissingPlan = computed(() => !hasPlan.value && !error.value && items.value.length === 0)
const showEmptyPlan = computed(
  () => hasPlan.value && items.value.length === 0 && excluded.value.length === 0
)
const showStaleNotice = computed(() => !resultIsCurrent.value && Boolean(resultWeekLabel.value))

function dayLabel(day) {
  return DAYS[day]?.label || `День ${day + 1}`
}

function mealLabel(code) {
  return MEALS.find((m) => m.code === code)?.label || code
}

function reasonLabel(reason) {
  return reason === 'source_missing' ? 'источник удалён' : 'недоступно'
}

onMounted(load)
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>Список покупок</h2>
      <button
        type="button"
        class="btn btn--ghost"
        :disabled="loading || refreshing"
        @click="refresh"
      >
        {{ refreshing ? 'Обновление…' : 'Обновить' }}
      </button>
    </div>

    <div class="week-nav">
      <button
        type="button"
        class="btn btn--ghost btn--small"
        aria-label="Предыдущая неделя"
        @click="goToWeek(-1)"
      >
        ←
      </button>
      <span class="week-label">{{ weekLabel }}</span>
      <button
        type="button"
        class="btn btn--ghost btn--small"
        aria-label="Следующая неделя"
        @click="goToWeek(1)"
      >
        →
      </button>
    </div>

    <p v-if="loading" class="loading">Загрузка…</p>

    <template v-else>
      <p v-if="error" class="error" role="alert">
        {{ error }}
        <router-link v-if="!resultWeekLabel" to="/family">Перейти на страницу «Семья»</router-link>
      </p>

      <p v-if="showStaleNotice" class="notice">
        Показан результат за {{ resultWeekLabel }} — его не удалось обновить.
      </p>

      <div v-if="excluded.length" class="card incomplete">
        <h3 class="incomplete-title">Список неполный</h3>
        <p class="incomplete-desc">
          Для {{ excluded.length }} {{ excluded.length === 1 ? 'блюда' : 'блюд' }} не удалось
          рассчитать продукты. Замените или удалите их в плане.
        </p>
        <ul class="excluded-list">
          <li v-for="entry in excluded" :key="`${entry.day}:${entry.mealType}:${entry.recipeId}`">
            <strong>{{ dayLabel(entry.day) }} · {{ mealLabel(entry.mealType) }}</strong> —
            {{ entry.recipeName }}
            <span class="excluded-reason">({{ reasonLabel(entry.reason) }})</span>
          </li>
        </ul>
        <router-link :to="planLink" class="btn btn--primary">Перейти к плану недели</router-link>
      </div>

      <div v-if="showMissingPlan" class="card empty-state">
        <div class="empty-icon">🗓️</div>
        <div class="empty-title">На эту неделю план не составлен</div>
        <p class="empty-desc">Составьте план, чтобы сформировать список покупок.</p>
        <router-link :to="planLink" class="btn btn--primary">Перейти к плану</router-link>
      </div>

      <div v-else-if="showEmptyPlan" class="card empty-state">
        <div class="empty-icon">🛒</div>
        <div class="empty-title">В плане этой недели нет блюд</div>
        <p class="empty-desc">Добавьте блюда в план, чтобы сформировать список покупок.</p>
        <router-link :to="planLink" class="btn btn--primary">Перейти к плану</router-link>
      </div>

      <div v-for="group in groups" :key="group.key" class="card group">
        <h3 class="group-title"><span class="group-icon">{{ group.icon }}</span> {{ group.label }}</h3>
        <ul class="item-list">
          <li v-for="item in group.items" :key="`${item.name}-${item.unit}`" class="item">
            <span class="item-name">{{ item.name }}</span>
            <span class="item-amount">{{ item.display }}</span>
          </li>
        </ul>
      </div>
    </template>
  </section>
</template>

<style scoped>
.week-nav {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0.75rem;
  margin: 0 0 1.25rem;
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: 999px;
  padding: 0.3rem;
}

.week-nav .btn {
  min-height: 40px;
}

.week-label {
  font-weight: 700;
  font-size: 0.95rem;
  flex: 1;
  text-align: center;
}

.incomplete {
  max-width: none;
  margin-bottom: 1rem;
  border-color: var(--warning-border, #d9a441);
}

.incomplete-title {
  margin: 0 0 0.4rem;
  font-size: 1.05rem;
}

.incomplete-desc {
  margin: 0 0 0.6rem;
  color: var(--text-soft);
}

.excluded-list {
  margin: 0 0 0.9rem;
  padding-left: 1.1rem;
}

.excluded-list li {
  margin-bottom: 0.25rem;
}

.excluded-reason {
  color: var(--text-soft);
}

.group {
  max-width: none;
  margin-bottom: 1rem;
}

.group-title {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin: 0 0 0.5rem;
  font-size: 1.05rem;
}

.group-icon {
  font-size: 1.25rem;
}

.item-list {
  list-style: none;
  margin: 0;
  padding: 0;
}

.item {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 1rem;
  padding: 0.6rem 0;
  border-bottom: 1px solid var(--border);
}

.item:last-child {
  border-bottom: none;
}

.item-name {
  font-weight: 700;
}

.item-amount {
  color: var(--text-soft);
  white-space: nowrap;
  font-weight: 600;
}
</style>
