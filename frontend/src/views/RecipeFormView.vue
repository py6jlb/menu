<script setup>
import { ref, reactive, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getRecipe, createRecipe, updateRecipe, uploadRecipePhoto, deleteRecipePhoto } from '../api/recipes'
import { autocompleteIngredients } from '../api/ingredients'
import { UNITS, SEASONS, parseList, joinList } from '../constants/recipe'

const route = useRoute()
const router = useRouter()

const editingId = computed(() => route.params.id || null)
const isEdit = computed(() => Boolean(editingId.value))

const form = reactive({
  name: '',
  description: '',
  cookTimeMinutes: 30,
  servings: 4,
  difficulty: 3,
  calories: '',
  tagsText: '',
  seasonality: [],
  dietText: '',
  steps: [''],
  ingredients: [newIngredient()]
})

const loading = ref(isEdit.value)
const saving = ref(false)
const error = ref('')

const debounceTimers = new Map()

function newIngredient() {
  return { name: '', amount: '', unit: 'g', note: '', suggestions: [], showSuggestions: false, activeIndex: -1, loadingSuggestions: false, noSuggestions: false }
}

const existingPhotoUrl = ref(null)
const selectedPhoto = ref(null)
const selectedPhotoPreview = ref('')
const photoRemoved = ref(false)

const photoActionLabel = computed(() =>
  selectedPhoto.value || (existingPhotoUrl.value && !photoRemoved.value) ? 'Заменить фото' : 'Выбрать фото'
)

function onPhotoSelected(event) {
  const file = event.target.files?.[0]
  if (!file) return
  selectedPhoto.value = file
  if (selectedPhotoPreview.value) URL.revokeObjectURL(selectedPhotoPreview.value)
  selectedPhotoPreview.value = URL.createObjectURL(file)
  event.target.value = ''
}

function clearSelectedPhoto() {
  selectedPhoto.value = null
  if (selectedPhotoPreview.value) {
    URL.revokeObjectURL(selectedPhotoPreview.value)
    selectedPhotoPreview.value = ''
  }
}

function removeExistingPhoto() {
  photoRemoved.value = true
}

function undoPhotoRemoval() {
  photoRemoved.value = false
}

onBeforeUnmount(() => {
  if (selectedPhotoPreview.value) URL.revokeObjectURL(selectedPhotoPreview.value)
})

function addStep() {
  form.steps.push('')
}

function removeStep(index) {
  form.steps.splice(index, 1)
}

function moveStep(index, delta) {
  const target = index + delta
  if (target < 0 || target >= form.steps.length) return
  const tmp = form.steps[index]
  form.steps[index] = form.steps[target]
  form.steps[target] = tmp
}

function addIngredient() {
  form.ingredients.push(newIngredient())
}

function removeIngredient(index) {
  clearDebounce(index)
  form.ingredients.splice(index, 1)
}

function moveIngredient(index, delta) {
  const target = index + delta
  if (target < 0 || target >= form.ingredients.length) return
  const tmp = form.ingredients[index]
  form.ingredients[index] = form.ingredients[target]
  form.ingredients[target] = tmp
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
  form.ingredients[index].showSuggestions = false
  form.ingredients[index].activeIndex = -1
}

function onIngredientInput(index) {
  const ing = form.ingredients[index]
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
  const ing = form.ingredients[index]
  const query = ing.name.trim()
  if (!query) return
  ing.loadingSuggestions = true
  ing.noSuggestions = false
  const { response, data } = await autocompleteIngredients(query)
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
  const ing = form.ingredients[index]
  ing.name = suggestion
  closeSuggestions(index)
}

function onSuggestionKeydown(index, event) {
  const ing = form.ingredients[index]
  if (!ing.showSuggestions || ing.suggestions.length === 0) return
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
  const index = form.seasonality.indexOf(code)
  if (index >= 0) form.seasonality.splice(index, 1)
  else form.seasonality.push(code)
}

function buildPayload() {
  return {
    name: form.name.trim(),
    description: form.description.trim() || null,
    cookTimeMinutes: Number(form.cookTimeMinutes),
    servings: Number(form.servings),
    difficulty: Number(form.difficulty),
    calories: form.calories === '' ? null : Number(form.calories),
    tags: parseList(form.tagsText),
    seasonality: form.seasonality,
    diet: parseList(form.dietText),
    steps: form.steps.map((s) => s.trim()).filter((s) => s.length > 0).map((text) => ({ text })),
    ingredients: form.ingredients
      .filter((i) => i.name.trim() || i.amount || i.unit || i.note)
      .map((i) => ({
        name: i.name.trim(),
        amount: Number(i.amount),
        unit: i.unit,
        note: i.note.trim() || null
      }))
  }
}

async function loadRecipe() {
  const { response, data } = await getRecipe(editingId.value)
  if (response.status === 404) {
    error.value = 'Рецепт не найден.'
    loading.value = false
    return
  }
  if (response.status !== 200) {
    error.value = data?.error || 'Не удалось загрузить рецепт.'
    loading.value = false
    return
  }

  form.name = data.name
  form.description = data.description || ''
  existingPhotoUrl.value = data.photoUrl || null
  form.cookTimeMinutes = data.cookTimeMinutes
  form.servings = data.servings
  form.difficulty = data.difficulty
  form.calories = data.calories === null || data.calories === undefined ? '' : String(data.calories)
  form.tagsText = joinList(data.tags)
  form.seasonality = [...data.seasonality]
  form.dietText = joinList(data.diet)
  form.steps = data.steps.length ? [...data.steps] : ['']
  form.ingredients = data.ingredients.length
    ? data.ingredients.map((i) => ({
        name: i.name,
        amount: String(i.amount),
        unit: i.unit,
        note: i.note || ''
      }))
    : [newIngredient()]

  loading.value = false
}

async function submit() {
  error.value = ''
  if (!form.name.trim()) {
    error.value = 'Укажите название рецепта.'
    return
  }
  saving.value = true
  try {
    const payload = buildPayload()
    const { response, data } = isEdit.value
      ? await updateRecipe(editingId.value, payload)
      : await createRecipe(payload)
    if (response.status === 200 || response.status === 201) {
      const id = data.id
      try {
        if (photoRemoved.value && !selectedPhoto.value && existingPhotoUrl.value) {
          const removed = await deleteRecipePhoto(id)
          if (removed.response.status !== 204) {
            console.error('Не удалось удалить фото:', removed.data)
          }
        } else if (selectedPhoto.value) {
          const uploaded = await uploadRecipePhoto(id, selectedPhoto.value)
          if (uploaded.response.status !== 200) {
            console.error('Не удалось загрузить фото:', uploaded.data)
          }
        }
      } catch (err) {
        console.error('Ошибка при работе с фото:', err)
      }
      router.push(`/recipes/${id}`)
    } else {
      error.value = data?.error || 'Не удалось сохранить рецепт.'
    }
  } catch (err) {
    error.value = err.message || 'Сервер недоступен.'
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  if (isEdit.value) loadRecipe()
})

onBeforeUnmount(() => {
  debounceTimers.forEach((timer) => clearTimeout(timer))
  debounceTimers.clear()
})
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>{{ isEdit ? 'Редактирование рецепта' : 'Новый рецепт' }}</h2>
    </div>

    <p v-if="loading" class="loading">Загрузка…</p>

    <form v-else @submit.prevent="submit" class="recipe-form">
      <div class="card form-section">
        <label class="field">
          <span>Название *</span>
          <input v-model="form.name" type="text" maxlength="200" required />
        </label>

        <label class="field">
          <span>Описание</span>
          <textarea v-model="form.description" rows="3" maxlength="2000"></textarea>
        </label>

        <div class="row">
          <label class="field">
            <span>Время (мин) *</span>
            <input v-model.number="form.cookTimeMinutes" type="number" min="1" max="1440" required />
          </label>
          <label class="field">
            <span>Порции *</span>
            <input v-model.number="form.servings" type="number" min="1" max="100" required />
          </label>
          <label class="field">
            <span>Сложность *</span>
            <select v-model.number="form.difficulty" required>
              <option v-for="level in 5" :key="level" :value="level">{{ level }}</option>
            </select>
          </label>
        </div>

        <label class="field">
          <span>Калорийность на порцию (ккал)</span>
          <input v-model.number="form.calories" type="number" min="0" max="10000" />
        </label>
      </div>

      <fieldset class="card fieldset">
        <legend>Фото</legend>
        <div v-if="selectedPhoto" class="photo-preview">
          <img :src="selectedPhotoPreview" alt="Предпросмотр фото" class="photo-thumb" />
          <div class="photo-actions">
            <span class="hint">Новое фото будет загружено после сохранения.</span>
            <button type="button" class="btn btn--ghost" @click="clearSelectedPhoto">Убрать</button>
          </div>
        </div>
        <div v-else-if="existingPhotoUrl && !photoRemoved" class="photo-preview">
          <img :src="existingPhotoUrl" alt="Фото рецепта" class="photo-thumb" />
          <div class="photo-actions">
            <button type="button" class="btn btn--ghost text-danger" @click="removeExistingPhoto">Удалить фото</button>
          </div>
        </div>
        <p v-else-if="photoRemoved" class="hint">
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
          <input v-model="form.tagsText" type="text" placeholder="суп, первое, быстрый" />
        </label>

        <fieldset class="fieldset-inline">
          <legend>Сезонность</legend>
          <div class="chips">
            <label v-for="season in SEASONS" :key="season.code" class="chip">
              <input
                type="checkbox"
                :value="season.code"
                :checked="form.seasonality.includes(season.code)"
                @change="toggleSeason(season.code)"
              />
              {{ season.label }}
            </label>
          </div>
        </fieldset>

        <label class="field">
          <span>Диета (через запятую)</span>
          <input v-model="form.dietText" type="text" placeholder="вегетарианское, безглютеновое" />
        </label>
      </div>

      <fieldset class="card fieldset">
        <legend>Ингредиенты</legend>
        <p class="hint">Количество и единица измерения указываются вместе. Примечание (например, «по вкусу») не влияет на расчёт.</p>
        <div v-for="(ing, index) in form.ingredients" :key="index" class="ingredient-row">
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
            <button type="button" class="btn btn--subtle icon-btn" @click="moveIngredient(index, 1)" :disabled="index === form.ingredients.length - 1">↓</button>
            <button type="button" class="btn btn--ghost" @click="removeIngredient(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="btn btn--ghost" @click="addIngredient">+ Добавить ингредиент</button>
      </fieldset>

      <fieldset class="card fieldset">
        <legend>Шаги приготовления *</legend>
        <p class="hint">Порядок шагов соответствует порядку в списке.</p>
        <div v-for="(step, index) in form.steps" :key="index" class="step-row">
          <textarea v-model="form.steps[index]" rows="2" placeholder="Шаг приготовления" maxlength="2000" class="step-textarea"></textarea>
          <div class="row-actions">
            <button type="button" class="btn btn--subtle icon-btn" @click="moveStep(index, -1)" :disabled="index === 0">↑</button>
            <button type="button" class="btn btn--subtle icon-btn" @click="moveStep(index, 1)" :disabled="index === form.steps.length - 1">↓</button>
            <button type="button" class="btn btn--ghost" @click="removeStep(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="btn btn--ghost" @click="addStep">+ Добавить шаг</button>
      </fieldset>

      <p v-if="error" class="error">{{ error }}</p>

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
