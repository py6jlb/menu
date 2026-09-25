<script setup>
import { ref, onMounted } from 'vue'
import { getSettings, updateSettings } from '../api/settings'
import { useAuth } from '../stores/auth'

const { isEmailVerified } = useAuth()

const windowWeeks = ref(3)
const loading = ref(true)
const error = ref('')
const saving = ref(false)
const savedMessage = ref('')
const saveError = ref('')

async function load() {
  loading.value = true
  error.value = ''
  const { response, data } = await getSettings()
  if (response.status === 200) {
    windowWeeks.value = data?.repetitionWindowWeeks ?? 3
  } else {
    error.value = data?.error || 'Не удалось загрузить настройки.'
  }
  loading.value = false
}

async function save() {
  saving.value = true
  savedMessage.value = ''
  saveError.value = ''
  const { response, data } = await updateSettings({ repetitionWindowWeeks: windowWeeks.value })
  if (response.status === 200) {
    windowWeeks.value = data?.repetitionWindowWeeks ?? windowWeeks.value
    savedMessage.value = 'Настройки сохранены.'
  } else {
    saveError.value = data?.error || 'Не удалось сохранить настройки.'
  }
  saving.value = false
}

onMounted(load)
</script>

<template>
  <section>
    <div class="page-heading">
      <h2>Личные настройки</h2>
    </div>

    <p v-if="loading" class="loading">Загрузка…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <div v-else class="card settings-card">
      <p v-if="savedMessage" class="success">{{ savedMessage }}</p>
      <p v-else-if="saveError" class="error">{{ saveError }}</p>

      <p v-if="!isEmailVerified" class="notice">
        Подтвердите почту, чтобы менять настройки.
        <router-link to="/verify">Ввести код</router-link>
      </p>

      <label class="field">
        <span>Окно повторяемости блюд (недель)</span>
        <span class="hint">
          Сколько недель учитывается при подсчёте «сколько раз блюдо готовилось».
          Значение от 1 до 52.
        </span>
        <input
          v-model.number="windowWeeks"
          type="number"
          min="1"
          max="52"
          required
          :disabled="!isEmailVerified"
          class="weeks-input"
        />
      </label>

      <div class="explain-block">
        <span class="explain-title">Как это работает</span>
        <p class="explain-text">
          Значение «Готовилось ×N» на рецепте показывает, сколько раз это блюдо было в плане
          за последние указанные недели. Большой «окно» = дольше помнить блюда и реже
          повторять их слишком часто; маленькое окно = быстрее забывать и чаще готовить
          любимое. Мы стараемся предлагать блюда, которые давно не готовились.
        </p>
      </div>

      <button
        type="button"
        class="btn btn--primary"
        :disabled="saving || !isEmailVerified || windowWeeks < 1 || windowWeeks > 52"
        @click="save"
      >
        {{ saving ? 'Сохранение…' : 'Сохранить' }}
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
