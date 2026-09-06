<script setup>
import { ref, onMounted } from 'vue'
import { getSettings, updateSettings } from '../api/settings'

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
    <h2>Личные настройки</h2>

    <p v-if="loading">Загрузка…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <div v-else class="card">
      <p v-if="savedMessage" class="success">{{ savedMessage }}</p>
      <p v-else-if="saveError" class="error">{{ saveError }}</p>

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
        />
      </label>

      <button
        type="button"
        class="save-btn"
        :disabled="saving || windowWeeks < 1 || windowWeeks > 52"
        @click="save"
      >
        {{ saving ? 'Сохранение…' : 'Сохранить' }}
      </button>
    </div>
  </section>
</template>

<style scoped>
.card {
  background: #fff;
  border: 1px solid #e5e5e5;
  border-radius: 10px;
  padding: 1.5rem;
  max-width: 480px;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.hint {
  color: #777;
  font-size: 0.85rem;
}

.field input {
  max-width: 120px;
}

.success {
  color: #047857;
}

.save-btn {
  padding: 0.55rem;
  border: none;
  border-radius: 6px;
  background: #3730a3;
  color: #fff;
  font-size: 1rem;
}

.save-btn:disabled {
  background: #d1d5db;
  cursor: not-allowed;
}
</style>