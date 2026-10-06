<script setup>
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { matchRecipes } from '../api/recipes'
import { DAYS, MEALS, mondayOf, weekDays, weekRangeLabel } from '../constants/plan'
import { useWeekDraft } from '../composables/useWeekDraft'
import ExternalStateBadge from '../components/ExternalStateBadge.vue'
import { SEASONS, DIETS } from '../constants/recipe'
import { useAuth } from '../stores/auth'

const { isEmailVerified } = useAuth()

const {
  weekStart: monday,
  draft,
  conflict,
  dirty,
  loading,
  saving,
  loadError: loadingError,
  saveError,
  savedMessage,
  slotEntry,
  setSlot,
  removeSlot: removeDraftSlot,
  loadWeek: load,
  save,
  reloadServerVersion,
  goToWeek,
  confirmNavigation
} = useWeekDraft({ initialWeek: mondayOf(new Date()) })

const recipes = ref([])

const editing = ref(null)
const search = ref('')
const pickerRecipeId = ref(null)
const pickerPortions = ref(1)
const pickerLoading = ref(false)
const pickerError = ref('')

const filter = ref({
  maxDifficulty: null,
  maxCalories: '',
  seasons: [],
  diets: [],
  maxCookTime: '',
  ingredient: '',
  tag: '',
  preferSeasons: [],
  preferDiets: [],
  preferLowCalories: false,
  preferLowComplexity: false
})

const weekLabel = computed(() => weekRangeLabel(monday.value))
const days = computed(() => weekDays(monday.value))

const mobileDay = ref((new Date().getDay() + 6) % 7)

const hasFilters = computed(() => {
  const f = filter.value
  return Boolean(
    f.maxDifficulty ||
      f.maxCalories ||
      f.seasons.length ||
      f.diets.length ||
      f.maxCookTime ||
      f.ingredient ||
      f.tag ||
      f.preferSeasons.length ||
      f.preferDiets.length ||
      f.preferLowCalories ||
      f.preferLowComplexity
  )
})

const filteredRecipes = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return recipes.value
  return recipes.value.filter((r) => r.name.toLowerCase().includes(q))
})

async function loadRecipes(body = {}) {
  pickerLoading.value = true
  pickerError.value = ''
  try {
    const { response, data } = await matchRecipes(body)
    if (response.status === 200) {
      recipes.value = (data?.items || []).map(toPickerItem)
    } else {
      pickerError.value = data?.error || 'Не удалось загрузить рецепты.'
    }
  } catch {
    pickerError.value = 'Не удалось загрузить рецепты. Проверьте соединение.'
  } finally {
    pickerLoading.value = false
  }
}

function toPickerItem(item) {
  return {
    id: item.recipeId,
    name: item.name,
    difficulty: item.difficulty,
    calories: item.calories,
    cookTimeMinutes: item.cookTimeMinutes,
    servings: item.servings,
    tags: item.tags,
    seasonality: item.seasonality,
    diet: item.diet,
    matchScore: item.matchScore
  }
}

function toggleInList(list, value) {
  const index = list.indexOf(value)
  if (index >= 0) list.splice(index, 1)
  else list.push(value)
}

function resetFilters() {
  filter.value = {
    maxDifficulty: null,
    maxCalories: '',
    seasons: [],
    diets: [],
    maxCookTime: '',
    ingredient: '',
    tag: '',
    preferSeasons: [],
    preferDiets: [],
    preferLowCalories: false,
    preferLowComplexity: false
  }
}

async function applyFilters() {
  const f = filter.value
  const body = {
    filters: {
      maxDifficulty: f.maxDifficulty || null,
      maxCalories: f.maxCalories === '' ? null : Number(f.maxCalories),
      seasons: f.seasons,
      diets: f.diets,
      maxCookTimeMinutes: f.maxCookTime === '' ? null : Number(f.maxCookTime),
      includeIngredients: f.ingredient ? [f.ingredient] : null,
      tags: f.tag ? [f.tag] : null
    },
    preferences: {
      preferSeasons: f.preferSeasons,
      preferDiets: f.preferDiets,
      preferLowCalories: f.preferLowCalories,
      preferLowComplexity: f.preferLowComplexity
    }
  }
  await loadRecipes(body)
}

function openPicker(day, mealType) {
  editing.value = { day, mealType }
  search.value = ''
  resetFilters()
  loadRecipes()
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
  setSlot(editing.value.day, editing.value.mealType, {
    recipeId: pickerRecipeId.value,
    recipeName: recipe?.name || '',
    portions: pickerPortions.value
  })
  closePicker()
}

function removeSlot(day, mealType) {
  removeDraftSlot(day, mealType)
  if (editing.value && editing.value.day === day && editing.value.mealType === mealType) {
    closePicker()
  }
}

function handleBeforeUnload(event) {
  if (!dirty.value) return
  event.preventDefault()
  event.returnValue = ''
}

function dayHeader(index) {
  return `${DAYS[index].label} ${String(days.value[index].getDate()).padStart(2, '0')}`
}

function mealLabel(code) {
  return MEALS.find((m) => m.code === code)?.label || code
}

onBeforeRouteLeave(() => confirmNavigation())

onMounted(() => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  load()
  loadRecipes()
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
})
</script>

<template>
  <section>
    <div class="page-heading plan-heading">
      <div>
        <h2>План на неделю</h2>
        <span v-if="dirty" class="dirty-badge">● есть изменения</span>
      </div>
      <button
        v-if="isEmailVerified"
        type="button"
        class="btn btn--primary"
        :disabled="saving || loading || !dirty"
        @click="save"
      >
        {{ saving ? 'Сохранение…' : 'Сохранить' }}
      </button>
    </div>

    <p v-if="!isEmailVerified" class="notice">
      Подтвердите почту, чтобы менять план недели.
      <router-link to="/verify">Ввести код</router-link>
    </p>

    <div class="nav">
      <button type="button" class="btn btn--ghost btn--small" @click="goToWeek(-1)">←</button>
      <span class="week-label">{{ weekLabel }}</span>
      <button type="button" class="btn btn--ghost btn--small" @click="goToWeek(1)">→</button>
    </div>

    <p v-if="savedMessage" class="success">{{ savedMessage }}</p>
    <div v-else-if="conflict" class="conflict">
      <p class="error">{{ saveError }}</p>
      <p v-if="conflict.revision !== null" class="conflict-server">
        Актуальная версия на сервере: ревизия {{ conflict.revision }},
        записей — {{ conflict.entries.length }}.
      </p>
      <button type="button" class="btn btn--ghost btn--small" @click="reloadServerVersion">
        Загрузить актуальную версию
      </button>
    </div>
    <p v-else-if="saveError" class="error">{{ saveError }}</p>

    <p v-if="loading" class="loading">Загрузка…</p>
    <p v-else-if="loadingError" class="error">
      {{ loadingError }}
      <router-link to="/family">Перейти на страницу «Семья»</router-link>
    </p>

    <template v-else>
      <div v-if="recipes.length === 0" class="card empty-state">
        <div class="empty-icon">🍽️</div>
        <div class="empty-title">В семье пока нет рецептов</div>
        <p class="empty-desc">Добавьте рецепты, чтобы планировать неделю.</p>
        <router-link v-if="isEmailVerified" to="/recipes/new" class="btn btn--primary">Добавить рецепт</router-link>
      </div>

      <template v-else>
        <div class="desktop-grid">
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
              :class="{ filled: slotEntry(index, meal.code), readonly: !isEmailVerified }"
              :disabled="!isEmailVerified"
              @click="openPicker(index, meal.code)"
            >
              <template v-if="slotEntry(index, meal.code)">
                <span class="slot-recipe">{{ slotEntry(index, meal.code).recipeName }}</span>
                <span class="slot-portions">{{ slotEntry(index, meal.code).portions }} порц.</span>
                <ExternalStateBadge
                  class="slot-state"
                  variant="slot"
                  :state="slotEntry(index, meal.code).state"
                />
                <span v-if="isEmailVerified" class="slot-remove" @click.stop="removeSlot(index, meal.code)">✕</span>
              </template>
              <span v-else class="slot-empty">+</span>
            </button>
          </template>
        </div>

        <div class="mobile-view">
          <div class="day-tabs">
            <button
              v-for="(day, index) in days"
              :key="`tab-${day.getTime()}`"
              type="button"
              class="day-tab"
              :class="{ active: mobileDay === index }"
              @click="mobileDay = index"
            >
              {{ DAYS[index].label }}
              <span class="day-tab-date">{{ String(day.getDate()).padStart(2, '0') }}</span>
            </button>
          </div>

          <div class="mobile-day-list">
            <button
              v-for="meal in MEALS"
              :key="`${mobileDay}-${meal.code}`"
              type="button"
              class="mobile-slot"
              :class="{ filled: slotEntry(mobileDay, meal.code), readonly: !isEmailVerified }"
              :disabled="!isEmailVerified"
              @click="openPicker(mobileDay, meal.code)"
            >
              <span class="mobile-meal">{{ meal.label }}</span>
              <template v-if="slotEntry(mobileDay, meal.code)">
                <span class="mobile-recipe">{{ slotEntry(mobileDay, meal.code).recipeName }}</span>
                <ExternalStateBadge
                  class="slot-state"
                  variant="mobile"
                  :state="slotEntry(mobileDay, meal.code).state"
                />
                <span class="mobile-meta">
                  {{ slotEntry(mobileDay, meal.code).portions }} порц.
                  <span v-if="isEmailVerified" class="mobile-remove" @click.stop="removeSlot(mobileDay, meal.code)">✕</span>
                </span>
              </template>
              <span v-else class="mobile-empty">+ Добавить</span>
            </button>
          </div>
        </div>
      </template>
    </template>

    <div v-if="editing" class="overlay" @click.self="closePicker">
      <div class="picker">
        <h3>
          {{ mealLabel(editing.mealType) }} ·
          {{ DAYS.find((d) => d.code === editing.day)?.label }}
        </h3>

        <input v-model="search" type="text" placeholder="Поиск рецепта…" />

        <details class="filter-panel">
          <summary>Фильтры и предпочтения</summary>

          <div class="filter-row">
            <label class="field">
              <span>Макс. сложность</span>
              <select v-model.number="filter.maxDifficulty">
                <option :value="null">Любая</option>
                <option v-for="level in 5" :key="level" :value="level">{{ level }}</option>
              </select>
            </label>
            <label class="field">
              <span>Макс. калории</span>
              <input v-model.number="filter.maxCalories" type="number" min="0" placeholder="ккал" />
            </label>
            <label class="field">
              <span>Макс. время (мин)</span>
              <input v-model.number="filter.maxCookTime" type="number" min="1" placeholder="мин" />
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Сезон (жёсткий):</span>
            <label v-for="season in SEASONS" :key="season.code" class="chip">
              <input type="checkbox" :value="season.code" :checked="filter.seasons.includes(season.code)" @change="toggleInList(filter.seasons, season.code)" />
              {{ season.label }}
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Диета (жёсткий):</span>
            <label v-for="diet in DIETS" :key="diet.code" class="chip">
              <input type="checkbox" :value="diet.code" :checked="filter.diets.includes(diet.code)" @change="toggleInList(filter.diets, diet.code)" />
              {{ diet.label }}
            </label>
          </div>

          <div class="filter-row">
            <label class="field">
              <span>Ингредиент</span>
              <input v-model="filter.ingredient" type="text" placeholder="напр. лук" />
            </label>
            <label class="field">
              <span>Тег</span>
              <input v-model="filter.tag" type="text" placeholder="напр. быстро" />
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Предпочтения (сорт.):</span>
            <label class="chip">
              <input type="checkbox" v-model="filter.preferLowCalories" />
              Низкокалорийные
            </label>
            <label class="chip">
              <input type="checkbox" v-model="filter.preferLowComplexity" />
              Простые
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Предпочт. сезоны:</span>
            <label v-for="season in SEASONS" :key="season.code" class="chip">
              <input type="checkbox" :value="season.code" :checked="filter.preferSeasons.includes(season.code)" @change="toggleInList(filter.preferSeasons, season.code)" />
              {{ season.label }}
            </label>
          </div>

          <div class="filter-group">
            <span class="filter-label">Предпочт. диеты:</span>
            <label v-for="diet in DIETS" :key="diet.code" class="chip">
              <input type="checkbox" :value="diet.code" :checked="filter.preferDiets.includes(diet.code)" @change="toggleInList(filter.preferDiets, diet.code)" />
              {{ diet.label }}
            </label>
          </div>

          <div class="filter-actions">
            <button type="button" class="btn btn--primary" @click="applyFilters">Применить</button>
            <button type="button" class="btn btn--ghost" @click="resetFilters">Сбросить</button>
          </div>
        </details>

        <p v-if="pickerLoading" class="picker-loading">Загрузка…</p>
        <p v-else-if="pickerError" class="error">{{ pickerError }}</p>

        <ul class="recipe-options">
          <li
            v-for="recipe in filteredRecipes"
            :key="recipe.id"
            :class="{ selected: recipe.id === pickerRecipeId }"
            @click="selectRecipe(recipe)"
          >
            <span class="recipe-name">{{ recipe.name }}</span>
            <span class="recipe-meta">
              <template v-if="recipe.difficulty">Сл.: {{ recipe.difficulty }}</template>
              <template v-if="recipe.calories !== null && recipe.calories !== undefined"> · {{ recipe.calories }} ккал</template>
              <template v-if="recipe.cookTimeMinutes"> · {{ recipe.cookTimeMinutes }} мин</template>
              <span v-if="recipe.repetitionCount > 0" class="repetition" title="Сколько раз готовилось за последние недели">
                ×{{ recipe.repetitionCount }}
              </span>
            </span>
          </li>
          <li v-if="filteredRecipes.length === 0" class="no-results">Ничего не найдено.</li>
        </ul>

        <label class="portions field">
          <span>Порции</span>
          <input v-model.number="pickerPortions" type="number" min="1" max="100" />
        </label>

        <div class="picker-actions">
          <button type="button" class="btn btn--primary" :disabled="!pickerRecipeId || pickerPortions < 1" @click="confirmSlot">
            Назначить
          </button>
          <button
            v-if="slotEntry(editing.day, editing.mealType)"
            type="button"
            class="btn btn--danger"
            @click="removeSlot(editing.day, editing.mealType)"
          >
            Убрать из плана
          </button>
          <button type="button" class="btn btn--ghost" @click="closePicker">Отмена</button>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.plan-heading {
  align-items: flex-end;
}

@media (max-width: 899px) {
  .plan-heading {
    position: sticky;
    top: 56px;
    z-index: 30;
    background: var(--bg);
    padding: 0.75rem 0.5rem;
    margin: -0.75rem -0.5rem 0;
    border-bottom: 1px solid var(--border);
  }

  .plan-heading h2 {
    font-size: 1.25rem;
  }
}

.dirty-badge {
  font-size: 0.8rem;
  color: var(--warning);
  font-weight: 700;
  display: inline-flex;
  align-items: center;
}

.conflict {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 0.5rem;
  margin: 0 0 1rem;
}

.conflict .error {
  margin: 0;
}

.conflict-server {
  margin: 0;
  color: var(--text-soft);
  font-size: 0.9rem;
}

.nav {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.75rem;
  margin: 0 0 1rem;
}

.week-label {
  font-weight: 700;
  font-size: 0.95rem;
  text-align: center;
  flex: 1;
}

.desktop-grid {
  display: none;
}

.mobile-view {
  display: block;
}

@media (min-width: 900px) {
  .desktop-grid {
    display: grid;
    grid-template-columns: 110px repeat(7, minmax(92px, 1fr));
    gap: 6px;
  }

  .mobile-view {
    display: none;
  }
}

.corner,
.day-head {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--text-soft);
  padding: 0.35rem 0.2rem;
  text-align: center;
}

.meal-label {
  display: flex;
  align-items: center;
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--text-soft);
}

.slot {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  align-items: flex-start;
  min-height: 64px;
  padding: 0.45rem;
  border: 1px dashed var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  font-size: 0.85rem;
  text-align: left;
  position: relative;
  transition: border-color 0.15s ease, background 0.15s ease;
}

.slot:hover {
  border-color: var(--primary);
}

.slot.readonly,
.mobile-slot.readonly {
  cursor: default;
}

.slot.readonly:hover {
  border-color: var(--border);
}

.slot.readonly.filled:hover {
  border-color: var(--success);
}

.slot.filled {
  border-style: solid;
  border-color: var(--success);
  background: var(--success-bg);
}

.slot-empty {
  margin: auto;
  color: var(--text-faint);
  font-size: 1.1rem;
}

.slot-recipe {
  font-weight: 700;
  color: var(--text);
  padding-right: 1.1rem;
  overflow: hidden;
  text-overflow: ellipsis;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
}

.slot-portions {
  color: var(--success);
  font-size: 0.8rem;
}

.slot-state {
  border-radius: 999px;
  padding: 0.05rem 0.45rem;
  font-size: 0.7rem;
  font-weight: 700;
  white-space: nowrap;
}

.slot-remove {
  position: absolute;
  top: 0.2rem;
  right: 0.35rem;
  color: var(--danger);
  font-weight: 700;
  cursor: pointer;
}

/* mobile day tabs */
.day-tabs {
  display: flex;
  gap: 0.4rem;
  overflow-x: auto;
  padding-bottom: 0.5rem;
  margin-bottom: 0.75rem;
  -webkit-overflow-scrolling: touch;
  scrollbar-width: thin;
}

.day-tab {
  flex: 0 0 auto;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.1rem;
  min-height: 52px;
  min-width: 56px;
  padding: 0.35rem 0.6rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--text-soft);
  font-weight: 700;
  font-size: 0.9rem;
}

.day-tab .day-tab-date {
  font-size: 0.75rem;
  color: var(--text-faint);
  font-weight: 600;
}

.day-tab.active {
  background: var(--primary);
  border-color: var(--primary);
  color: var(--on-primary);
}

.day-tab.active .day-tab-date {
  color: rgba(255, 255, 255, 0.85);
}

.mobile-day-list {
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.mobile-slot {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 0.25rem;
  text-align: left;
  padding: 0.8rem 0.9rem;
  border: 1px dashed var(--border);
  border-radius: var(--radius);
  background: var(--surface);
  font-size: 0.95rem;
  min-height: 64px;
  width: 100%;
}

.mobile-slot.filled {
  border-style: solid;
  border-color: var(--success);
  background: var(--success-bg);
}

.mobile-meal {
  font-size: 0.78rem;
  font-weight: 800;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--text-faint);
}

.mobile-slot.filled .mobile-meal {
  color: var(--success);
}

.mobile-recipe {
  font-weight: 800;
  color: var(--text);
  font-size: 1rem;
}

.mobile-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  color: var(--success);
  font-size: 0.85rem;
  font-weight: 600;
}

.mobile-remove {
  color: var(--danger);
  font-weight: 800;
  font-size: 1rem;
  padding: 0.45rem 0.7rem;
  margin: -0.45rem -0.7rem -0.45rem 0;
}

.mobile-empty {
  color: var(--text-faint);
  font-weight: 600;
}

/* picker overlay */
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(59, 51, 43, 0.45);
  display: flex;
  align-items: flex-end;
  justify-content: center;
  z-index: 50;
}

@media (min-width: 900px) {
  .overlay {
    align-items: center;
  }
}

.picker {
  background: var(--surface);
  border-radius: var(--radius) var(--radius) 0 0;
  padding: 1.25rem;
  width: 100%;
  max-width: 92vw;
  max-height: 88vh;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  box-shadow: var(--shadow-md);
  animation: sheetUp 0.2s ease;
}

@keyframes sheetUp {
  from {
    transform: translateY(30px);
    opacity: 0.6;
  }
  to {
    transform: translateY(0);
    opacity: 1;
  }
}

@media (min-width: 900px) {
  .picker {
    width: 480px;
    border-radius: var(--radius);
  }
}

.recipe-options {
  list-style: none;
  margin: 0;
  padding: 0;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  max-height: 260px;
  overflow-y: auto;
  background: var(--surface);
}

.recipe-options li {
  padding: 0.6rem 0.75rem;
  border-bottom: 1px solid var(--border);
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.recipe-options li:last-child {
  border-bottom: none;
}

.recipe-options li.selected {
  background: var(--primary-soft);
}

.recipe-name {
  font-weight: 700;
}

.recipe-meta {
  font-size: 0.8rem;
  color: var(--text-soft);
}

.repetition {
  background: var(--warning-bg);
  color: var(--warning);
  border-radius: 999px;
  padding: 0.05rem 0.5rem;
  font-size: 0.8rem;
  font-weight: 700;
  margin-left: 0.4rem;
}

.recipe-options li.no-results {
  cursor: default;
  color: var(--text-soft);
}

.portions input {
  max-width: 110px;
}

.picker-actions {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  position: sticky;
  bottom: 0;
  background: var(--surface);
  padding-top: 0.5rem;
}

.filter-panel {
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  padding: 0.5rem 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.6rem;
}

.filter-panel summary {
  cursor: pointer;
  font-weight: 700;
  font-size: 0.9rem;
}

.filter-row {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.6rem;
}

@media (min-width: 480px) {
  .filter-row {
    grid-template-columns: repeat(3, 1fr);
  }
}

.filter-row .field input,
.filter-row .field select {
  width: 100%;
}

.filter-group {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.4rem;
  font-size: 0.85rem;
}

.filter-label {
  font-size: 0.8rem;
  color: var(--text-soft);
  font-weight: 600;
}

.filter-group .chip {
  display: inline-flex;
  align-items: center;
  gap: 0.25rem;
  font-weight: 600;
}

.filter-group .chip input {
  display: none;
}

.filter-actions {
  display: flex;
  gap: 0.5rem;
}

.picker-loading {
  color: var(--text-soft);
  font-size: 0.9rem;
  margin: 0;
}
</style>
