<script setup>
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter, onBeforeRouteLeave, onBeforeRouteUpdate } from 'vue-router'
import { uploadRecipePhoto, deleteRecipePhoto } from '../api/recipes'
import { autocompleteIngredients } from '../api/ingredients'
import { UNITS, SEASONS } from '../constants/recipe'
import { useRecipeDraft, newIngredientDraft } from '../composables/useRecipeDraft'

const route = useRoute()
const router = useRouter()

const {
  editingId,
  isEdit,
  draft,
  dirty,
  loading,
  saving,
  loadError,
  saveError,
  setIdentity,
  load,
  save,
  validate,
  confirmNavigation
} = useRecipeDraft({ initialId: route.params.id || null })

const error = ref('')
const visibleError = computed(() => error.value || saveError.value)

const debounceTimers = new Map()

function newIngredient() {
  return {
    ...newIngredientDraft(),
    suggestions: [],
    showSuggestions: false,
    activeIndex: -1,
    loadingSuggestions: false,
    noSuggestions: false
  }
}

const selectedPhotoPreview = ref('')

watch(
  () => draft.value.photo.selected,
  (file) => {
    if (selectedPhotoPreview.value) URL.revokeObjectURL(selectedPhotoPreview.value)
    selectedPhotoPreview.value = file ? URL.createObjectURL(file) : ''
  }
)

const photoActionLabel = computed(() =>
  draft.value.photo.selected || (draft.value.photo.existing && !draft.value.photo.removed)
    ? 'Заменить фото'
    : 'Выбрать фото'
)

function onPhotoSelected(event) {
  const file = event.target.files?.[0]
  if (!file) return
  draft.value.photo.selected = file
  draft.value.photo.removed = false
  event.target.value = ''
}

function clearSelectedPhoto() {
  draft.value.photo.selected = null
}

function removeExistingPhoto() {
  draft.value.photo.selected = null
  draft.value.photo.removed = true
}

function undoPhotoRemoval() {
  draft.value.photo.removed = false
}

function addStep() {
  draft.value.steps.push('')
}

function removeStep(index) {
  draft.value.steps.splice(index, 1)
}

function moveStep(index, delta) {
  const target = index + delta
  if (target < 0 || target >= draft.value.steps.length) return
  const tmp = draft.value.steps[index]
  draft.value.steps[index] = draft.value.steps[target]
  draft.value.steps[target] = tmp
}

function addIngredient() {
  draft.value.ingredients.push(newIngredient())
}

function removeIngredient(index) {
  clearDebounce(index)
  draft.value.ingredients.splice(index, 1)
}

function moveIngredient(index, delta) {
  const target = index + delta
  if (target < 0 || target >= draft.value.ingredients.length) return
  const tmp = draft.value.ingredients[index]
  draft.value.ingredients[index] = draft.value.ingredients[target]
  draft.value.ingredients[target] = tmp
}

function clearDebounce(index) {
  const timer = debounceTimers.get(index)
  if (timer) {
    clearTimeout(timer)
    debounceTimers.delete(index)
  }
}

function closeSuggestions(index) {
  clearDebounce(index)
  const ing = draft.value.ingredients[index]
  if (!ing) return
  ing.showSuggestions = false
  ing.activeIndex = -1
}

function onIngredientInput(index) {
  const ing = draft.value.ingredients[index]
  if (!ing) return
  if (!ing.name.trim()) {
    clearDebounce(index)
    ing.suggestions = []
    ing.showSuggestions = false
    ing.noSuggestions = false
    ing.activeIndex = -1
    return
  }
  clearDebounce(index)
  debounceTimers.set(index, setTimeout(() => fetchSuggestions(index), 150))
}

async function fetchSuggestions(index) {
  const ing = draft.value.ingredients[index]
  if (!ing) return
  const query = ing.name.trim()
  if (!query) return
  ing.loadingSuggestions = true
  ing.noSuggestions = false
  const { response, data } = await autocompleteIngredients(query)
  // Ответ устарел, если строка пересоздана или сменился сам ресурс.
  if (draft.value.ingredients[index] !== ing || ing.name.trim() !== query) return
  if (response.status === 200 && data) {
    ing.suggestions = data.items || []
    ing.showSuggestions = true
    ing.activeIndex = -1
    ing.noSuggestions = ing.suggestions.length === 0
  } else {
    ing.suggestions = []
    ing.showSuggestions = false
    ing.noSuggestions = false
  }
  ing.loadingSuggestions = false
}

function selectSuggestion(index, suggestion) {
  const ing = draft.value.ingredients[index]
  ing.name = suggestion
  closeSuggestions(index)
}

function onSuggestionKeydown(index, event) {
  const ing = draft.value.ingredients[index]
  if (!ing || !ing.showSuggestions || ing.suggestions.length === 0) return
  if (event.key === 'ArrowDown') {
    event.preventDefault()
    ing.activeIndex = Math.min(ing.activeIndex + 1, ing.suggestions.length - 1)
  } else if (event.key === 'ArrowUp') {
    event.preventDefault()
    ing.activeIndex = Math.max(ing.activeIndex - 1, 0)
  } else if (event.key === 'Enter' && ing.activeIndex >= 0) {
    event.preventDefault()
    selectSuggestion(index, ing.suggestions[ing.activeIndex])
  } else if (event.key === 'Escape') {
    event.preventDefault()
    closeSuggestions(index)
  }
}

function toggleSeason(code) {
  const index = draft.value.seasonality.indexOf(code)
  if (index >= 0) draft.value.seasonality.splice(index, 1)
  else draft.value.seasonality.push(code)
}

async function submit() {
  error.value = validate()
  if (error.value) return

  const photoIntent = {
    removed:
      draft.value.photo.removed &&
      !draft.value.photo.selected &&
      Boolean(draft.value.photo.existing),
    selected: draft.value.photo.selected
  }

  const result = await save()
  if (!result.ok) return

  const id = result.id
  try {
    if (photoIntent.removed) {
      const removed = await deleteRecipePhoto(id)
      if (removed.response.status !== 204) {
        console.error('Не удалось удалить фото:', removed.data)
      }
    } else if (photoIntent.selected) {
      const uploaded = await uploadRecipePhoto(id, photoIntent.selected)
      if (uploaded.response.status !== 200) {
        console.error('Не удалось загрузить фото:', uploaded.data)
      }
    }
  } catch (err) {
    console.error('Ошибка при работе с фото:', err)
  }
  router.push(`/recipes/${id}`)
}

function handleBeforeUnload(event) {
  if (!dirty.value) return
  event.preventDefault()
  event.returnValue = ''
}

watch(
  () => route.params.id || null,
  (id) => {
    if (id === editingId.value) return
    setIdentity(id)
    error.value = ''
    clearDebounceAll()
    load()
  }
)

onBeforeRouteLeave(() => confirmNavigation())

onBeforeRouteUpdate((to, from) => {
  if ((to.params.id || null) === (from.params.id || null)) return true
  return confirmNavigation()
})

function clearDebounceAll() {
  debounceTimers.forEach((timer) => clearTimeout(timer))
  debounceTimers.clear()
}

onMounted(() => {
  window.addEventListener('beforeunload', handleBeforeUnload)
  if (isEdit.value) load()
})

onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', handleBeforeUnload)
  clearDebounceAll()
  if (selectedPhotoPreview.value) URL.revokeObjectURL(selectedPhotoPreview.value)
})
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>{{ isEdit ? 'Редактирование рецепта' : 'Новый рецепт' }}</h2>
      <span v-if="dirty" class="dirty-badge">● есть изменения</span>
    </div>

    <p v-if="loading" class="loading">Загрузка…</p>

    <div v-else-if="loadError" class="card empty-state">
      <div class="empty-icon">🍽️</div>
      <p class="empty-title">{{ loadError }}</p>
      <div class="actions">
        <button type="button" class="btn btn--primary" @click="load()">Повторить</button>
        <router-link :to="isEdit ? `/recipes/${editingId}` : '/recipes'" class="btn btn--ghost"
          >Отмена</router-link
        >
      </div>
    </div>

    <form v-else @submit.prevent="submit" class="recipe-form">
      <div class="card form-section">
        <label class="field">
          <span>Название *</span>
          <input v-model="draft.name" type="text" maxlength="200" required />
        </label>

        <label class="field">
          <span>Описание</span>
          <textarea v-model="draft.description" rows="3" maxlength="2000"></textarea>
        </label>

        <div class="row">
          <label class="field">
            <span>Время (мин) *</span>
            <input v-model.number="draft.cookTimeMinutes" type="number" min="1" max="1440" required />
          </label>
          <label class="field">
            <span>Порции *</span>
            <input v-model.number="draft.servings" type="number" min="1" max="100" required />
          </label>
          <label class="field">
            <span>Сложность *</span>
            <select v-model.number="draft.difficulty" required>
              <option v-for="level in 5" :key="level" :value="level">{{ level }}</option>
            </select>
          </label>
        </div>

        <label class="field">
          <span>Калорийность на порцию (ккал)</span>
          <input v-model.number="draft.calories" type="number" min="0" max="10000" />
        </label>
      </div>

      <fieldset class="card fieldset">
        <legend>Фото</legend>
        <div v-if="draft.photo.selected" class="photo-preview">
          <img :src="selectedPhotoPreview" alt="Предпросмотр фото" class="photo-thumb" />
          <div class="photo-actions">
            <span class="hint">Новое фото будет загружено после сохранения.</span>
            <button type="button" class="btn btn--ghost" @click="clearSelectedPhoto">Убрать</button>
          </div>
        </div>
        <div v-else-if="draft.photo.existing && !draft.photo.removed" class="photo-preview">
          <img :src="draft.photo.existing" alt="Фото рецепта" class="photo-thumb" />
          <div class="photo-actions">
            <button type="button" class="btn btn--ghost text-danger" @click="removeExistingPhoto">
              Удалить фото
            </button>
          </div>
        </div>
        <p v-else-if="draft.photo.removed" class="hint">
          Фото будет удалено после сохранения.
          <button type="button" class="btn btn--subtle" @click="undoPhotoRemoval">Отменить удаление</button>
        </p>
        <label class="file-label btn btn--ghost">
          {{ photoActionLabel }}
          <input type="file" accept="image/jpeg,image/png,image/webp,image/gif" class="file-input" @change="onPhotoSelected" />
        </label>
      </fieldset>

      <div class="card form-section">
        <label class="field">
          <span>Теги (через запятую)</span>
          <input v-model="draft.tagsText" type="text" placeholder="суп, первое, быстрый" />
        </label>

        <fieldset class="fieldset-inline">
          <legend>Сезонность</legend>
          <div class="chips">
            <label v-for="season in SEASONS" :key="season.code" class="chip">
              <input
                type="checkbox"
                :value="season.code"
                :checked="draft.seasonality.includes(season.code)"
                @change="toggleSeason(season.code)"
              />
              {{ season.label }}
            </label>
          </div>
        </fieldset>

        <label class="field">
          <span>Диета (через запятую)</span>
          <input v-model="draft.dietText" type="text" placeholder="вегетарианское, безглютеновое" />
        </label>
      </div>

      <fieldset class="card fieldset">
        <legend>Ингредиенты</legend>
        <p class="hint">Количество и единица измерения указываются вместе. Примечание (например, «по вкусу») не влияет на расчёт.</p>
        <div v-for="(ing, index) in draft.ingredients" :key="index" class="ingredient-row">
          <div class="ing-name-wrap">
            <input
              v-model="ing.name"
              type="text"
              placeholder="Название"
              class="ing-name"
              @input="onIngredientInput(index)"
              @keydown="onSuggestionKeydown(index, $event)"
              @blur="closeSuggestions(index)"
            />
            <ul v-if="ing.showSuggestions" class="suggestions">
              <li
                v-for="(suggestion, sIndex) in ing.suggestions"
                :key="suggestion"
                :class="{ active: sIndex === ing.activeIndex }"
                @mousedown.prevent="selectSuggestion(index, suggestion)"
              >
                {{ suggestion }}
              </li>
              <li v-if="ing.noSuggestions" class="no-suggestions">Нет подсказок</li>
            </ul>
          </div>
          <input v-model.number="ing.amount" type="number" min="0" step="0.01" placeholder="Кол-во" class="ing-amount" />
          <select v-model="ing.unit" class="ing-unit">
            <option v-for="unit in UNITS" :key="unit.code" :value="unit.code">{{ unit.label }}</option>
          </select>
          <input v-model="ing.note" type="text" placeholder="Примечание" class="ing-note" />
          <div class="row-actions">
            <button type="button" class="btn btn--subtle icon-btn" @click="moveIngredient(index, -1)" :disabled="index === 0">↑</button>
            <button type="button" class="btn btn--subtle icon-btn" @click="moveIngredient(index, 1)" :disabled="index === draft.ingredients.length - 1">↓</button>
            <button type="button" class="btn btn--ghost" @click="removeIngredient(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="btn btn--ghost" @click="addIngredient">+ Добавить ингредиент</button>
      </fieldset>

      <fieldset class="card fieldset">
        <legend>Шаги приготовления *</legend>
        <p class="hint">Порядок шагов соответствует порядку в списке.</p>
        <div v-for="(step, index) in draft.steps" :key="index" class="step-row">
          <textarea v-model="draft.steps[index]" rows="2" placeholder="Шаг приготовления" maxlength="2000" class="step-textarea"></textarea>
          <div class="row-actions">
            <button type="button" class="btn btn--subtle icon-btn" @click="moveStep(index, -1)" :disabled="index === 0">↑</button>
            <button type="button" class="btn btn--subtle icon-btn" @click="moveStep(index, 1)" :disabled="index === draft.steps.length - 1">↓</button>
            <button type="button" class="btn btn--ghost" @click="removeStep(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="btn btn--ghost" @click="addStep">+ Добавить шаг</button>
      </fieldset>

      <p v-if="visibleError" class="error">{{ visibleError }}</p>

      <div class="actions">
        <button type="submit" class="btn btn--primary" :disabled="saving">{{ saving ? 'Сохранение…' : 'Сохранить' }}</button>
        <router-link :to="isEdit ? `/recipes/${editingId}` : '/recipes'" class="btn btn--ghost">Отмена</router-link>
      </div>
    </form>
  </section>
</template>

<style scoped>
.recipe-form {
  max-width: 760px;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.form-section {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.row {
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.75rem;
}

@media (min-width: 640px) {
  .row {
    grid-template-columns: repeat(3, 1fr);
  }
}

.fieldset {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.fieldset legend {
  font-weight: 800;
  font-size: 1rem;
  color: var(--text);
}

.fieldset-inline {
  border: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.fieldset-inline legend {
  font-weight: 800;
  font-size: 0.95rem;
  color: var(--text);
  margin-bottom: 0.25rem;
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.chip {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.45rem 0.85rem;
  border: 1px solid var(--border);
  border-radius: 999px;
  background: var(--surface);
  font-weight: 600;
  font-size: 0.9rem;
  cursor: pointer;
  min-height: 40px;
  transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;
}

.chip input {
  display: none;
}

.chip:has(input:checked) {
  background: var(--primary-soft);
  border-color: var(--primary);
  color: var(--primary);
}

.ingredient-row,
.step-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.5rem;
  padding: 0.85rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}

.ing-name-wrap {
  position: relative;
  grid-column: 1 / -1;
}

.step-textarea {
  grid-column: 1 / -1;
}

.row-actions {
  grid-column: 1 / -1;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  justify-content: flex-end;
}

.icon-btn {
  width: 44px;
  min-height: 44px;
  padding: 0;
}

@media (min-width: 640px) {
  .ingredient-row {
    grid-template-columns: 1fr 6rem 7.5rem 1fr auto;
    align-items: center;
    background: transparent;
    border: none;
    padding: 0.4rem 0;
    border-top: 1px solid var(--border);
    border-radius: 0;
  }

  .ing-name-wrap {
    grid-column: 1 / 2;
  }

  .ing-amount {
    grid-column: 2 / 3;
  }

  .ing-unit {
    grid-column: 3 / 4;
  }

  .ing-note {
    grid-column: 4 / 5;
  }

  .row-actions {
    grid-column: 5 / 6;
    justify-content: flex-start;
    flex-wrap: nowrap;
    gap: 0.25rem;
  }

  .step-row {
    grid-template-columns: 1fr auto;
    align-items: center;
    background: transparent;
    border: none;
    padding: 0.4rem 0;
    border-top: 1px solid var(--border);
    border-radius: 0;
  }

  .step-textarea {
    grid-column: 1 / 2;
  }

  .step-row .row-actions {
    grid-column: 2 / 3;
    justify-content: flex-end;
  }
}

.suggestions {
  position: absolute;
  top: 100%;
  left: 0;
  right: 0;
  z-index: 10;
  margin: 0;
  padding: 0;
  list-style: none;
  background: var(--surface);
  border: 1px solid var(--border);
  border-top: none;
  border-radius: 0 0 var(--radius-sm) var(--radius-sm);
  max-height: 12rem;
  overflow-y: auto;
  box-shadow: var(--shadow-md);
}

.suggestions li {
  padding: 0.55rem 0.7rem;
  cursor: pointer;
  font-size: 0.9rem;
}

.suggestions li.active {
  background: var(--primary-soft);
}

.suggestions li.no-suggestions {
  cursor: default;
  color: var(--text-soft);
}

.photo-preview {
  display: flex;
  align-items: flex-start;
  gap: 0.9rem;
  flex-wrap: wrap;
}

.photo-actions {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  align-items: flex-start;
}

.photo-thumb {
  max-width: 240px;
  max-height: 180px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--border);
}

.file-label {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
  align-self: flex-start;
}

.file-input {
  max-width: 100%;
}

.actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-wrap: wrap;
}
</style>
