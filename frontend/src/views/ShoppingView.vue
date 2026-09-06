<script setup>
import { ref, computed, onMounted } from 'vue'
import { getShoppingList } from '../api/shopping'
import { mondayOf, addDays, toIso, weekRangeLabel } from '../constants/plan'

const GROUPS = [
  { key: 'weight', label: 'Вес', icon: '⚖️', units: ['g', 'kg'] },
  { key: 'volume', label: 'Объём', icon: '🧴', units: ['ml', 'l'] },
  { key: 'pieces', label: 'Штуки', icon: '🔢', units: ['pcs'] },
  { key: 'household', label: 'Бытовые меры', icon: '🥄', units: ['glass', 'tbsp', 'tsp', 'pinch'] }
]

function groupOf(unit) {
  return GROUPS.find((g) => g.units.includes(unit)) || GROUPS[GROUPS.length - 1]
}

const monday = ref(mondayOf(new Date()))
const items = ref([])
const loading = ref(true)
const loadingError = ref('')
const refreshing = ref(false)

const weekLabel = computed(() => weekRangeLabel(monday.value))

const groups = computed(() =>
  GROUPS.map((g) => ({
    ...g,
    items: items.value.filter((item) => groupOf(item.unit).key === g.key)
  })).filter((g) => g.items.length > 0)
)

const empty = computed(() => !loadingError.value && items.value.length === 0)

async function load() {
  loading.value = true
  loadingError.value = ''
  const { response, data } = await getShoppingList(toIso(monday.value))
  if (response.status === 200) {
    items.value = data?.items || []
  } else if (response.status === 404) {
    items.value = []
    loadingError.value = data?.error || 'Вы пока не состоите в семье.'
  } else {
    items.value = []
    loadingError.value = data?.error || 'Не удалось загрузить список покупок.'
  }
  loading.value = false
}

async function changeWeek(offset) {
  monday.value = addDays(monday.value, offset * 7)
  await load()
}

async function refresh() {
  refreshing.value = true
  await load()
  refreshing.value = false
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
      <button type="button" class="btn btn--ghost btn--small" @click="changeWeek(-1)">←</button>
      <span class="week-label">{{ weekLabel }}</span>
      <button type="button" class="btn btn--ghost btn--small" @click="changeWeek(1)">→</button>
    </div>

    <p v-if="loading" class="loading">Загрузка…</p>
    <p v-else-if="loadingError" class="error">
      {{ loadingError }}
      <router-link to="/family">Перейти на страницу «Семья»</router-link>
    </p>

    <template v-else>
      <div v-if="empty" class="card empty-state">
        <div class="empty-icon">🛒</div>
        <div class="empty-title">Список покупок пуст</div>
        <p class="empty-desc">Заполните план на неделю, чтобы сформировать список покупок.</p>
        <router-link to="/plan" class="btn btn--primary">Перейти к плану</router-link>
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
