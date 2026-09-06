<script setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getRecipe, createRecipe, updateRecipe } from '../api/recipes'
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
  ingredients: [{ name: '', amount: '', unit: 'g', note: '' }]
})

const loading = ref(isEdit.value)
const saving = ref(false)
const error = ref('')

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
  form.ingredients.push({ name: '', amount: '', unit: 'g', note: '' })
}

function removeIngredient(index) {
  form.ingredients.splice(index, 1)
}

function moveIngredient(index, delta) {
  const target = index + delta
  if (target < 0 || target >= form.ingredients.length) return
  const tmp = form.ingredients[index]
  form.ingredients[index] = form.ingredients[target]
  form.ingredients[target] = tmp
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
    steps: form.steps.map((s) => s.trim()).filter((s) => s.length > 0),
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
    : [{ name: '', amount: '', unit: 'g', note: '' }]

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
      router.push(`/recipes/${data.id}`)
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
</script>

<template>
  <section>
    <h2>{{ isEdit ? 'Редактирование рецепта' : 'Новый рецепт' }}</h2>

    <p v-if="loading">Загрузка…</p>

    <form v-else @submit.prevent="submit" class="recipe-form">
      <label>
        Название *
        <input v-model="form.name" type="text" maxlength="200" required />
      </label>

      <label>
        Описание
        <textarea v-model="form.description" rows="3" maxlength="2000"></textarea>
      </label>

      <div class="row">
        <label>
          Время приготовления (мин) *
          <input v-model.number="form.cookTimeMinutes" type="number" min="1" max="1440" required />
        </label>
        <label>
          Порции *
          <input v-model.number="form.servings" type="number" min="1" max="100" required />
        </label>
        <label>
          Сложность *
          <select v-model.number="form.difficulty" required>
            <option v-for="level in 5" :key="level" :value="level">{{ level }}</option>
          </select>
        </label>
      </div>

      <label>
        Калорийность на порцию (ккал)
        <input v-model.number="form.calories" type="number" min="0" max="10000" />
      </label>

      <label>
        Теги (через запятую)
        <input v-model="form.tagsText" type="text" placeholder="суп, первое, быстрый" />
      </label>

      <fieldset>
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

      <label>
        Диета (через запятую)
        <input v-model="form.dietText" type="text" placeholder="вегетарианское, безглютеновое" />
      </label>

      <fieldset>
        <legend>Ингредиенты</legend>
        <p class="hint">Количество и единица измерения указываются вместе. Примечание (например, «по вкусу») не влияет на расчёт.</p>
        <div v-for="(ing, index) in form.ingredients" :key="index" class="ingredient-row">
          <input v-model="ing.name" type="text" placeholder="Название" class="ing-name" />
          <input v-model.number="ing.amount" type="number" min="0" step="0.01" placeholder="Кол-во" class="ing-amount" />
          <select v-model="ing.unit" class="ing-unit">
            <option v-for="unit in UNITS" :key="unit.code" :value="unit.code">{{ unit.label }}</option>
          </select>
          <input v-model="ing.note" type="text" placeholder="Примечание" class="ing-note" />
          <div class="row-actions">
            <button type="button" class="ghost" @click="moveIngredient(index, -1)" :disabled="index === 0">↑</button>
            <button type="button" class="ghost" @click="moveIngredient(index, 1)" :disabled="index === form.ingredients.length - 1">↓</button>
            <button type="button" class="ghost danger-text" @click="removeIngredient(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="ghost" @click="addIngredient">+ Добавить ингредиент</button>
      </fieldset>

      <fieldset>
        <legend>Шаги приготовления *</legend>
        <p class="hint">Порядок шагов соответствует порядку в списке.</p>
        <div v-for="(step, index) in form.steps" :key="index" class="step-row">
          <textarea v-model="form.steps[index]" rows="2" placeholder="Шаг приготовления" maxlength="2000"></textarea>
          <div class="row-actions">
            <button type="button" class="ghost" @click="moveStep(index, -1)" :disabled="index === 0">↑</button>
            <button type="button" class="ghost" @click="moveStep(index, 1)" :disabled="index === form.steps.length - 1">↓</button>
            <button type="button" class="ghost danger-text" @click="removeStep(index)">Удалить</button>
          </div>
        </div>
        <button type="button" class="ghost" @click="addStep">+ Добавить шаг</button>
      </fieldset>

      <p v-if="error" class="error">{{ error }}</p>

      <div class="actions">
        <button type="submit" :disabled="saving">{{ saving ? 'Сохранение…' : 'Сохранить' }}</button>
        <router-link :to="isEdit ? `/recipes/${editingId}` : '/recipes'" class="cancel">Отмена</router-link>
      </div>
    </form>
  </section>
</template>

<style scoped>
.recipe-form {
  max-width: 640px;
}

label {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  font-size: 0.9rem;
}

.row {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 0.75rem;
}

textarea {
  padding: 0.5rem 0.6rem;
  border: 1px solid #ccc;
  border-radius: 6px;
  font-size: 1rem;
  font-family: inherit;
}

select {
  padding: 0.5rem 0.6rem;
  border: 1px solid #ccc;
  border-radius: 6px;
  font-size: 1rem;
}

fieldset {
  border: 1px solid #e5e5e5;
  border-radius: 8px;
  padding: 0.75rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

legend {
  font-weight: 600;
  font-size: 0.9rem;
}

.hint {
  font-size: 0.8rem;
  color: #666;
  margin: 0;
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
}

.chip {
  display: inline-flex;
  flex-direction: row;
  align-items: center;
  gap: 0.3rem;
}

.ingredient-row,
.step-row {
  display: flex;
  align-items: flex-start;
  gap: 0.4rem;
}

.ing-name {
  flex: 2;
}

.ing-amount {
  flex: 0 0 5.5rem;
}

.ing-unit {
  flex: 0 0 7.5rem;
}

.ing-note {
  flex: 2;
}

.step-row textarea {
  flex: 1;
}

.row-actions {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}

.ghost {
  background: none;
  border: 1px solid #ccc;
  border-radius: 6px;
  padding: 0.3rem 0.6rem;
  font-size: 0.85rem;
}

.danger-text {
  color: #b91c1c;
  border-color: #f3c6c6;
}

.actions {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.cancel {
  font-size: 0.9rem;
}
</style>