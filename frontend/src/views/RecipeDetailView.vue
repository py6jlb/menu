<script setup>
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getRecipe, deleteRecipe } from '../api/recipes'
import { unitLabel, seasonLabel } from '../constants/recipe'

const route = useRoute()
const router = useRouter()

const recipe = ref(null)
const loading = ref(true)
const error = ref('')
const deleting = ref(false)

async function load() {
  const { response, data } = await getRecipe(route.params.id)
  if (response.status === 404) {
    error.value = 'Рецепт не найден.'
  } else if (response.status === 200) {
    recipe.value = data
  } else {
    error.value = data?.error || 'Не удалось загрузить рецепт.'
  }
  loading.value = false
}

async function onDelete() {
  if (!window.confirm(`Удалить рецепт «${recipe.value.name}»?`)) return
  deleting.value = true
  error.value = ''
  const { response } = await deleteRecipe(recipe.value.id)
  if (response.status === 204) {
    router.push('/recipes')
  } else {
    error.value = 'Не удалось удалить рецепт.'
    deleting.value = false
  }
}

onMounted(load)
</script>

<template>
  <section>
    <p v-if="loading">Загрузка…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <div v-else-if="recipe">
      <div class="heading">
        <h2>{{ recipe.name }}</h2>
        <div class="heading-actions">
          <router-link :to="`/recipes/${recipe.id}/edit`" class="primary-link">Редактировать</router-link>
          <button type="button" class="danger" :disabled="deleting" @click="onDelete">Удалить</button>
        </div>
      </div>

      <div class="meta">
        <span>Сложность: {{ recipe.difficulty }}/5</span>
        <span>Время: {{ recipe.cookTimeMinutes }} мин</span>
        <span>Порции: {{ recipe.servings }}</span>
        <span v-if="recipe.calories !== null && recipe.calories !== undefined">
          Калории: {{ recipe.calories }} ккал/порция
        </span>
      </div>

      <p v-if="recipe.description" class="description">{{ recipe.description }}</p>

      <div v-if="recipe.tags.length || recipe.seasonality.length || recipe.diet.length" class="badges">
        <span v-for="tag in recipe.tags" :key="`tag-${tag}`" class="badge">#{{ tag }}</span>
        <span v-for="season in recipe.seasonality" :key="`season-${season}`" class="badge badge-season">
          {{ seasonLabel(season) }}
        </span>
        <span v-for="diet in recipe.diet" :key="`diet-${diet}`" class="badge badge-diet">
          {{ diet }}
        </span>
      </div>

      <h3>Ингредиенты</h3>
      <ul class="ingredients">
        <li v-for="ingredient in recipe.ingredients" :key="ingredient.id" class="ingredient">
          <span class="amount">{{ ingredient.amount }} {{ unitLabel(ingredient.unit) }}</span>
          <span class="name">{{ ingredient.name }}</span>
          <span v-if="ingredient.note" class="note">{{ ingredient.note }}</span>
        </li>
      </ul>

      <h3>Шаги приготовления</h3>
      <ol class="steps">
        <li v-for="(step, index) in recipe.steps" :key="index">{{ step }}</li>
      </ol>
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

.heading-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
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

.danger {
  color: #b91c1c;
  background: none;
  border: 1px solid #b91c1c;
  border-radius: 6px;
  padding: 0.5rem 0.9rem;
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  color: #555;
  font-size: 0.9rem;
}

.description {
  margin: 1rem 0 0;
}

.badges {
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
  margin: 1rem 0;
}

.badge {
  background: #f3f4f6;
  border-radius: 999px;
  padding: 0.1rem 0.6rem;
  font-size: 0.8rem;
  color: #444;
}

.badge-season {
  background: #ecfdf5;
  color: #047857;
}

.badge-diet {
  background: #fef3c7;
  color: #92400e;
}

.ingredients {
  list-style: none;
  padding: 0;
  margin: 0;
}

.ingredient {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  padding: 0.4rem 0;
  border-bottom: 1px solid #eee;
}

.amount {
  font-weight: 600;
}

.note {
  color: #777;
  font-size: 0.9rem;
}

.steps {
  margin: 0;
  padding-left: 1.25rem;
}

.steps li {
  margin: 0.35rem 0;
}
</style>