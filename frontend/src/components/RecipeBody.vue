<script setup>
import { unitLabel, seasonLabel, dietLabel, categoryLabel } from '../constants/recipe'
import { repetitionChip, repetitionTitle } from '../constants/repetition'

defineProps({
  recipe: { type: Object, required: true }
})
</script>

<template>
  <div class="hero-photo">
    <img v-if="recipe.photoUrl" :src="recipe.photoUrl" alt="Фото рецепта" class="photo" />
    <div v-else class="photo-placeholder">🍲</div>
  </div>

  <p v-if="recipe.description" class="description">{{ recipe.description }}</p>

  <div v-if="recipe.state !== 'broken'" class="meta">
    <span class="chip">⭐ Сложность {{ recipe.difficulty }}/5</span>
    <span class="chip">⏱ {{ recipe.cookTimeMinutes }} мин</span>
    <span class="chip">👥 {{ recipe.servings }} порц.</span>
    <span v-if="recipe.calories !== null && recipe.calories !== undefined" class="chip">
      🔥 {{ recipe.calories }} ккал/порция
    </span>
    <span v-if="recipe.repetitionCount > 0" class="chip chip--repetition" :title="repetitionTitle()">
      {{ repetitionChip(recipe.repetitionCount) }}
    </span>
  </div>

  <div v-if="recipe.tags.length || recipe.seasonality.length || recipe.diet.length" class="badges">
    <span v-for="tag in recipe.tags" :key="`tag-${tag}`" class="tag">#{{ tag }}</span>
    <span v-for="season in recipe.seasonality" :key="`season-${season}`" class="badge badge--season">
      🍂 {{ seasonLabel(season) }}
    </span>
    <span v-for="diet in recipe.diet" :key="`diet-${diet}`" class="badge badge--diet">
      {{ dietLabel(diet) }}
    </span>
  </div>

  <div v-if="recipe.state === 'broken'" class="card broken-content">
    <p>Содержимое рецепта недоступно, потому что семья-источник удалила его.</p>
  </div>

  <div v-else class="detail-columns">
    <div v-if="recipe.documentUrl" class="card document-viewer">
      <div class="document-head">
        <h3>Документ рецепта</h3>
        <a :href="recipe.documentUrl" target="_blank" rel="noopener" class="btn btn--ghost btn--small">
          Открыть в новой вкладке
        </a>
      </div>
      <iframe :src="recipe.documentUrl" title="PDF-документ рецепта" class="document-frame"></iframe>
    </div>

    <div class="card column">
      <h3>Ингредиенты</h3>
      <ul class="ingredients">
        <li v-for="ingredient in recipe.ingredients" :key="ingredient.id" class="ingredient">
          <span class="amount">{{ ingredient.amount }} {{ unitLabel(ingredient.unit) }}</span>
          <span class="name">{{ ingredient.name }}</span>
          <span v-if="ingredient.category" class="category">{{ categoryLabel(ingredient.category) }}</span>
          <span v-if="ingredient.note" class="note">{{ ingredient.note }}</span>
        </li>
      </ul>
    </div>

    <div class="card column">
      <h3>Шаги приготовления</h3>
      <ol class="steps">
        <li v-for="(step, index) in recipe.steps" :key="index" class="step">
          <span class="step-num">{{ index + 1 }}</span>
          <span class="step-text">{{ step }}</span>
        </li>
      </ol>
    </div>
  </div>
</template>

<style scoped>
.hero-photo {
  margin-bottom: 1rem;
}

.photo {
  width: 100%;
  max-width: 600px;
  aspect-ratio: 16 / 9;
  object-fit: cover;
  border-radius: var(--radius);
  display: block;
}

.photo-placeholder {
  width: 100%;
  max-width: 600px;
  aspect-ratio: 16 / 9;
  border-radius: var(--radius);
  background: linear-gradient(135deg, #f9ead9, #faf6f0);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 4rem;
}

.description {
  margin: 0 0 1rem;
  color: var(--text-soft);
  font-size: 1.05rem;
  overflow-wrap: anywhere;
}

.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
  margin-bottom: 1rem;
}

.chip {
  background: var(--surface-2);
  color: var(--text-soft);
  border-radius: 999px;
  padding: 0.3rem 0.7rem;
  font-size: 0.85rem;
  font-weight: 600;
  white-space: nowrap;
}

.chip--repetition {
  background: var(--warning-bg);
  color: var(--warning);
}

.badges {
  display: flex;
  flex-wrap: wrap;
  gap: 0.45rem;
  margin-bottom: 1.5rem;
}

.broken-content {
  color: var(--text-soft);
  font-size: 0.95rem;
}

.broken-content p {
  margin: 0;
}

.detail-columns {
  display: grid;
  grid-template-columns: 1fr;
  gap: 1rem;
}

@media (min-width: 800px) {
  .detail-columns {
    grid-template-columns: 1fr 1fr;
    align-items: start;
  }
}

.document-viewer {
  grid-column: 1 / -1;
}

.document-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.document-head h3 {
  margin: 0;
}

.document-frame {
  width: 100%;
  height: 70vh;
  min-height: 320px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}

.ingredients {
  list-style: none;
  margin: 0;
  padding: 0;
}

.ingredient {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
  padding: 0.5rem 0;
  border-bottom: 1px solid var(--border);
  font-size: 0.95rem;
  overflow-wrap: anywhere;
}

.ingredient:last-child {
  border-bottom: none;
}

.amount {
  font-weight: 700;
  color: var(--primary);
  white-space: nowrap;
}

.name {
  overflow-wrap: anywhere;
}

.category {
  color: var(--text-faint);
  font-size: 0.8rem;
  white-space: nowrap;
  border: 1px solid var(--border);
  border-radius: 999px;
  padding: 0.05rem 0.5rem;
}

.note {
  color: var(--text-faint);
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.steps {
  margin: 0;
  padding: 0;
  list-style: none;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.step {
  display: flex;
  gap: 0.75rem;
  align-items: flex-start;
}

.step-num {
  flex: 0 0 auto;
  width: 26px;
  height: 26px;
  border-radius: 50%;
  background: var(--primary-soft);
  color: var(--primary);
  font-weight: 800;
  font-size: 0.85rem;
  display: flex;
  align-items: center;
  justify-content: center;
}

.step-text {
  font-size: 0.95rem;
  overflow-wrap: anywhere;
}
</style>
