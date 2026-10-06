<script setup>
import { computed, onMounted } from 'vue'
import { useSettings } from '../composables/useSettings'
import { useAuth } from '../stores/auth'
import { MAX_WINDOW_WEEKS, MIN_WINDOW_WEEKS, isValidWindowWeeks } from '../constants/settings'

const { isEmailVerified } = useAuth()
const {
  windowWeeks,
  dirty,
  loading,
  saving,
  loadError,
  saveError,
  savedMessage,
  load,
  save
} = useSettings()

const canSave = computed(
  () => !saving.value && isEmailVerified.value && isValidWindowWeeks(windowWeeks.value)
)

onMounted(load)
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>Личные настройки</h2>
    </div>

    <p v-if="loading" class="loading">Загрузка…</p>

    <div v-else-if="loadError" class="card">
      <p class="error" role="alert">{{ loadError }}</p>
      <button type="button" class="btn" @click="load">Повторить</button>
    </div>

    <div v-else class="card settings-card">
      <p v-if="savedMessage" class="success" role="status">{{ savedMessage }}</p>
      <p v-else-if="saveError" class="error" role="alert">{{ saveError }}</p>

      <p v-if="!isEmailVerified" class="notice">
        Подтвердите почту, чтобы менять настройки.
        <router-link to="/verify">Ввести код</router-link>
      </p>

      <label class="field">
        <span>Окно повторяемости блюд (недель)</span>
        <span class="hint">
          Сколько недель учитывается при подсчёте повторяемости.
          Значение от {{ MIN_WINDOW_WEEKS }} до {{ MAX_WINDOW_WEEKS }}.
        </span>
        <input
          v-model.number="windowWeeks"
          type="number"
          :min="MIN_WINDOW_WEEKS"
          :max="MAX_WINDOW_WEEKS"
          required
          :disabled="!isEmailVerified"
          class="weeks-input"
        />
      </label>

      <div class="explain-block">
        <span class="explain-title">Как это работает</span>
        <p class="explain-text">
          Значение «🔁 N нед.» показывает, в скольких разных неделях окна блюдо было
          в плане. Повторения внутри одной недели считаются один раз. Большое окно =
          дольше помнить блюда и реже повторять их слишком часто; маленькое окно =
          быстрее забывать и чаще готовить любимое. Мы стараемся предлагать блюда,
          которые давно не готовились.
        </p>
      </div>

      <p v-if="dirty" class="hint">Есть несохранённые изменения.</p>

      <button type="button" class="btn btn--primary" :disabled="!canSave" @click="save">
        {{ saving ? 'Сохранение…' : saveError ? 'Повторить сохранение' : 'Сохранить' }}
      </button>
    </div>
  </section>
</template>

<style scoped>
.settings-card {
  max-width: 480px;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.weeks-input {
  max-width: 120px;
}

.explain-block {
  background: var(--surface-2);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  padding: 1rem;
}

.explain-title {
  font-weight: 800;
  font-size: 0.95rem;
  display: block;
  margin-bottom: 0.35rem;
}

.explain-text {
  margin: 0;
  color: var(--text-soft);
  font-size: 0.88rem;
}
</style>
