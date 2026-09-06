<script setup>
import { ref, computed, onMounted } from 'vue'
import { getShoppingList } from '../api/shopping'
import { mondayOf, addDays, toIso, weekRangeLabel } from '../constants/plan'

const GROUPS = [
  { key: 'weight', label: 'Вес', units: ['g', 'kg'] },
  { key: 'volume', label: 'Объём', units: ['ml', 'l'] },
  { key: 'pieces', label: 'Штуки', units: ['pcs'] },
  { key: 'household', label: 'Бытовые меры', units: ['glass', 'tbsp', 'tsp', 'pinch'] }
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
    <div class="heading">
      <h2>Список покупок</h2>
      <button
        type="button"
        class="refresh-btn"
        :disabled="loading || refreshing"
        @click="refresh"
      >
        {{ refreshing ? 'Обновление…' : 'Обновить' }}
      </button>
    </div>

    <div class="nav">
      <button type="button" @click="changeWeek(-1)">← Предыдущая</button>
      <span class="week-label">{{ weekLabel }}</span>
      <button type="button" @click="changeWeek(1)">Следующая →</button>
    </div>

    <p v-if="loading">Загрузка…</p>
    <p v-else-if="loadingError" class="error">
      {{ loadingError }}
      <router-link to="/family">Перейти на страницу «Семья»</router-link>
    </p>

    <template v-else>
      <div v-if="empty" class="card">
        <p>На эту неделю пока нет списка покупок — заполните план, чтобы его сформировать.</p>
        <router-link to="/plan" class="primary-link">Перейти к плану</router-link>
      </div>

      <div v-for="group in groups" :key="group.key" class="group card">
        <h3>{{ group.label }}</h3>
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
.heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.refresh-btn {
  padding: 0.55rem 1rem;
  border: none;
  border-radius: 6px;
  background: #3730a3;
  color: #fff;
  font-size: 1rem;
}

.refresh-btn:disabled {
  background: #d1d5db;
  cursor: not-allowed;
}

.nav {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  margin: 0.75rem 0 1rem;
}

.week-label {
  font-weight: 600;
}

.group {
  max-width: none;
  margin-bottom: 1rem;
}

.group h3 {
  margin: 0 0 0.5rem;
  font-size: 1rem;
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
  padding: 0.4rem 0;
  border-bottom: 1px solid #f0f0f0;
}

.item:last-child {
  border-bottom: none;
}

.item-name {
  font-weight: 600;
}

.item-amount {
  color: #555;
  white-space: nowrap;
}

.primary-link {
  display: inline-block;
  padding: 0.55rem 0.9rem;
  background: #3730a3;
  color: #fff;
  border-radius: 6px;
  text-decoration: none;
  font-size: 0.95rem;
}
</style>