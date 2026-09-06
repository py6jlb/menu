<script setup>
import { ref, computed, onMounted } from 'vue'
import { listRecipes } from '../api/recipes'
import { getWeekPlan, saveWeekPlan } from '../api/plans'
import { DAYS, MEALS, mondayOf, addDays, weekDays, toIso, weekRangeLabel } from '../constants/plan'

const monday = ref(mondayOf(new Date()))
const recipes = ref([])
const draft = ref({})
const savedSnapshot = ref('')
const loading = ref(true)
const loadingError = ref('')
const saving = ref(false)
const savedMessage = ref('')
const saveError = ref('')

const editing = ref(null)
const search = ref('')
const pickerRecipeId = ref(null)
const pickerPortions = ref(1)

const weekLabel = computed(() => weekRangeLabel(monday.value))
const days = computed(() => weekDays(monday.value))

const filteredRecipes = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return recipes.value
  return recipes.value.filter((r) => r.name.toLowerCase().includes(q))
})

const dirty = computed(() => savedSnapshot.value !== snapshot())

function slotKey(day, mealType) {
  return `${day}:${mealType}`
}

function snapshot() {
  return JSON.stringify(
    Object.keys(draft.value)
      .sort()
      .map((key) => {
        const [day, mealType] = key.split(':')
        const entry = draft.value[key]
        return { day: Number(day), mealType, recipeId: entry.recipeId, portions: entry.portions }
      })
  )
}

function slotEntry(day, mealType) {
  return draft.value[slotKey(day, mealType)]
}

function entriesPayload() {
  return Object.keys(draft.value)
    .sort()
    .map((key) => {
      const [day, mealType] = key.split(':')
      const entry = draft.value[key]
      return { day: Number(day), mealType, recipeId: entry.recipeId, portions: entry.portions }
    })
}

async function load() {
  loading.value = true
  loadingError.value = ''
  savedMessage.value = ''
  saveError.value = ''
  const { response, data } = await getWeekPlan(toIso(monday.value))
  if (response.status === 200) {
    const map = {}
    for (const entry of data.entries || []) {
      map[slotKey(entry.day, entry.mealType)] = {
        recipeId: entry.recipeId,
        recipeName: entry.recipeName,
        portions: entry.portions
      }
    }
    draft.value = map
    savedSnapshot.value = snapshot()
  } else if (response.status === 404) {
    draft.value = {}
    savedSnapshot.value = ''
    loadingError.value = data?.error || 'Вы пока не состоите в семье.'
  } else {
    loadingError.value = data?.error || 'Не удалось загрузить план.'
  }
  loading.value = false
}

async function loadRecipes() {
  const { response, data } = await listRecipes()
  if (response.status === 200) {
    recipes.value = data || []
  }
}

async function changeWeek(offset) {
  if (dirty.value && !window.confirm('Есть несохранённые изменения. Продолжить без сохранения?')) return
  monday.value = addDays(monday.value, offset * 7)
  await load()
}

function openPicker(day, mealType) {
  editing.value = { day, mealType }
  search.value = ''
  const current = slotEntry(day, mealType)
  pickerRecipeId.value = current ? current.recipeId : null
  pickerPortions.value = current ? current.portions : 1
}

function closePicker() {
  editing.value = null
}

function selectRecipe(recipe) {
  pickerRecipeId.value = recipe.id
}

function confirmSlot() {
  if (!pickerRecipeId.value || pickerPortions.value < 1) return
  const recipe = recipes.value.find((r) => r.id === pickerRecipeId.value)
  draft.value = {
    ...draft.value,
    [slotKey(editing.value.day, editing.value.mealType)]: {
      recipeId: pickerRecipeId.value,
      recipeName: recipe?.name || '',
      portions: pickerPortions.value
    }
  }
  closePicker()
}

function removeSlot(day, mealType) {
  const key = slotKey(day, mealType)
  const next = { ...draft.value }
  delete next[key]
  draft.value = next
  if (editing.value && slotKey(editing.value.day, editing.value.mealType) === key) {
    closePicker()
  }
}

async function save() {
  saving.value = true
  savedMessage.value = ''
  saveError.value = ''
  const { response, data } = await saveWeekPlan(toIso(monday.value), entriesPayload())
  if (response.status === 200) {
    const map = {}
    for (const entry of data.entries || []) {
      map[slotKey(entry.day, entry.mealType)] = {
        recipeId: entry.recipeId,
        recipeName: entry.recipeName,
        portions: entry.portions
      }
    }
    draft.value = map
    savedSnapshot.value = snapshot()
    savedMessage.value = 'План сохранён.'
  } else {
    saveError.value = data?.error || 'Не удалось сохранить план.'
  }
  saving.value = false
}

function dayHeader(index) {
  return `${DAYS[index].label} ${String(days.value[index].getDate()).padStart(2, '0')}`
}

onMounted(async () => {
  await loadRecipes()
  await load()
})
</script>

<template>
  <section>
    <div class="heading">
      <h2>План на неделю</h2>
      <button
        type="button"
        class="save-btn"
        :disabled="saving || loading || !dirty"
        @click="save"
      >
        {{ saving ? 'Сохранение…' : 'Сохранить' }}
      </button>
    </div>

    <div class="nav">
      <button type="button" @click="changeWeek(-1)">← Предыдущая</button>
      <span class="week-label">{{ weekLabel }}</span>
      <button type="button" @click="changeWeek(1)">Следующая →</button>
    </div>

    <p v-if="savedMessage" class="success">{{ savedMessage }}</p>
    <p v-else-if="saveError" class="error">{{ saveError }}</p>

    <p v-if="loading">Загрузка…</p>
    <p v-else-if="loadingError" class="error">
      {{ loadingError }}
      <router-link to="/family">Перейти на страницу «Семья»</router-link>
    </p>

    <template v-else>
      <div v-if="recipes.length === 0" class="card">
        <p>В семье пока нет рецептов — добавьте их, чтобы планировать неделю.</p>
        <router-link to="/recipes/new" class="primary-link">Добавить рецепт</router-link>
      </div>

      <div v-else class="grid">
        <div class="corner">Приём пищи</div>
        <div v-for="(day, index) in days" :key="`head-${day.getTime()}`" class="day-head">
          {{ dayHeader(index) }}
        </div>

        <template v-for="meal in MEALS" :key="meal.code">
          <div class="meal-label">{{ meal.label }}</div>
          <button
            v-for="(day, index) in days"
            :key="`${meal.code}-${day.getTime()}`"
            type="button"
            class="slot"
            :class="{ filled: slotEntry(index, meal.code) }"
            @click="openPicker(index, meal.code)"
          >
            <template v-if="slotEntry(index, meal.code)">
              <span class="slot-recipe">{{ slotEntry(index, meal.code).recipeName }}</span>
              <span class="slot-portions">{{ slotEntry(index, meal.code).portions }} порц.</span>
              <span class="slot-remove" @click.stop="removeSlot(index, meal.code)">✕</span>
            </template>
            <span v-else class="slot-empty">+</span>
          </button>
        </template>
      </div>
    </template>

    <div v-if="editing" class="overlay" @click.self="closePicker">
      <div class="picker">
        <h3>
          {{ MEALS.find((m) => m.code === editing.mealType)?.label }} ·
          {{ DAYS.find((d) => d.code === editing.day)?.label }}
        </h3>

        <input v-model="search" type="text" placeholder="Поиск рецепта…" />

        <ul class="recipe-options">
          <li
            v-for="recipe in filteredRecipes"
            :key="recipe.id"
            :class="{ selected: recipe.id === pickerRecipeId }"
            @click="selectRecipe(recipe)"
          >
            {{ recipe.name }}
          </li>
          <li v-if="filteredRecipes.length === 0" class="no-results">Ничего не найдено.</li>
        </ul>

        <label class="portions">
          Порции
          <input v-model.number="pickerPortions" type="number" min="1" max="100" />
        </label>

        <div class="picker-actions">
          <button type="button" class="primary" :disabled="!pickerRecipeId || pickerPortions < 1" @click="confirmSlot">
            Назначить
          </button>
          <button
            v-if="slotEntry(editing.day, editing.mealType)"
            type="button"
            class="danger"
            @click="removeSlot(editing.day, editing.mealType)"
          >
            Убрать из плана
          </button>
          <button type="button" @click="closePicker">Отмена</button>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
}

.save-btn {
  padding: 0.55rem 1rem;
  border: none;
  border-radius: 6px;
  background: #047857;
  color: #fff;
  font-size: 1rem;
}

.save-btn:disabled {
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

.success {
  color: #047857;
}

.grid {
  display: grid;
  grid-template-columns: 110px repeat(7, minmax(92px, 1fr));
  gap: 6px;
  overflow-x: auto;
  padding-bottom: 0.5rem;
}

.corner,
.day-head {
  font-size: 0.85rem;
  font-weight: 600;
  color: #444;
  padding: 0.35rem 0.2rem;
  text-align: center;
}

.meal-label {
  display: flex;
  align-items: center;
  font-size: 0.9rem;
  font-weight: 600;
  color: #444;
}

.slot {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  align-items: flex-start;
  min-height: 64px;
  padding: 0.45rem;
  border: 1px dashed #ccc;
  border-radius: 8px;
  background: #fff;
  font-size: 0.85rem;
  text-align: left;
  position: relative;
}

.slot:hover {
  border-color: #3730a3;
}

.slot.filled {
  border-style: solid;
  border-color: #c7d2fe;
  background: #f5f7ff;
}

.slot-empty {
  margin: auto;
  color: #9ca3af;
  font-size: 1.1rem;
}

.slot-recipe {
  font-weight: 600;
  padding-right: 1.1rem;
}

.slot-portions {
  color: #555;
}

.slot-remove {
  position: absolute;
  top: 0.2rem;
  right: 0.35rem;
  color: #b91c1c;
  font-weight: 700;
  cursor: pointer;
}

.overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 50;
}

.picker {
  background: #fff;
  border-radius: 12px;
  padding: 1.25rem;
  width: 420px;
  max-width: 92vw;
  max-height: 85vh;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.recipe-options {
  list-style: none;
  margin: 0;
  padding: 0;
  border: 1px solid #e5e5e5;
  border-radius: 8px;
  max-height: 260px;
  overflow-y: auto;
}

.recipe-options li {
  padding: 0.5rem 0.75rem;
  border-bottom: 1px solid #f0f0f0;
  cursor: pointer;
}

.recipe-options li:last-child {
  border-bottom: none;
}

.recipe-options li.selected {
  background: #eef2ff;
  font-weight: 600;
}

.recipe-options li.no-results {
  cursor: default;
  color: #777;
}

.portions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.portions input {
  width: 90px;
}

.picker-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.picker-actions button {
  padding: 0.5rem 0.9rem;
  border: 1px solid #ccc;
  border-radius: 6px;
  background: #fff;
  font-size: 0.95rem;
}

.picker-actions button.primary {
  background: #3730a3;
  border-color: #3730a3;
  color: #fff;
}

.picker-actions button.primary:disabled {
  background: #d1d5db;
  border-color: #d1d5db;
  cursor: not-allowed;
}

.picker-actions button.danger {
  color: #b91c1c;
  border-color: #b91c1c;
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