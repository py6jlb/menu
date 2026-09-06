<script setup>
import { ref, onMounted } from 'vue'
import { listRecipes } from '../api/recipes'

const recipes = ref([])
const loading = ref(true)
const error = ref('')

async function load() {
  loading.value = true
  error.value = ''
  const { response, data } = await listRecipes()
  if (response.status === 200) {
    recipes.value = data || []
  } else {
    error.value = data?.error || 'Не удалось загрузить рецепты.'
  }
  loading.value = false
}

onMounted(load)
</script>

<template>
  <section>
    <div class="heading">
      <h2>Рецепты</h2>
      <router-link to="/recipes/new" class="primary-link">Создать рецепт</router-link>
    </div>

    <p v-if="loading">Загрузка…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <div v-else-if="recipes.length === 0" class="card">
      <p>Пока нет ни одного рецепта.</p>
      <p>
        Если вы ещё не в семье — создайте или вступите в неё на странице
        <router-link to="/family">Семья</router-link>.
      </p>
      <router-link to="/recipes/new" class="primary-link">Добавить первый рецепт</router-link>
    </div>

    <ul v-else class="recipe-list">
      <li v-for="recipe in recipes" :key="recipe.id" class="recipe-item">
        <router-link :to="`/recipes/${recipe.id}`" class="recipe-name">{{ recipe.name }}</router-link>
        <div class="meta">
          <span>Сложность: {{ recipe.difficulty }}/5</span>
          <span>Время: {{ recipe.cookTimeMinutes }} мин</span>
          <span>Порции: {{ recipe.servings }}</span>
          <span v-if="recipe.calories !== null && recipe.calories !== undefined">
            Калории: {{ recipe.calories }} ккал/порция
          </span>
        </div>
        <div v-if="recipe.tags.length" class="tags">
          <span v-for="tag in recipe.tags" :key="tag" class="tag">#{{ tag }}</span>
        </div>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
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

.recipe-list {
  list-style: none;
  padding: 0;
  margin: 1rem 0 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.recipe-item {
  background: #fff;
  border: 1px solid #e5e5e5;
  border-radius: 10px;
  padding: 1rem 1.25rem;
}

.recipe-name {
  font-size: 1.1rem;
  font-weight: 600;
  text-decoration: none;
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  margin-top: 0.4rem;
  color: #555;
  font-size: 0.9rem;
}

.tags {
  margin-top: 0.5rem;
  display: flex;
  flex-wrap: wrap;
  gap: 0.4rem;
}

.tag {
  background: #f3f4f6;
  border-radius: 999px;
  padding: 0.1rem 0.6rem;
  font-size: 0.8rem;
  color: #444;
}
</style>