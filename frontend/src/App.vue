<script setup>
import { useAuth } from './stores/auth'

const { state, isAuthenticated, clearSession } = useAuth()

function logout() {
  clearSession()
  window.location.href = '/login'
}
</script>

<template>
  <div class="layout">
    <header>
      <router-link to="/" class="brand">Меню для домохозяек</router-link>
      <nav v-if="isAuthenticated" class="user-area">
        <span class="email">{{ state.user?.email }}</span>
        <span class="role">{{ state.user?.role }}</span>
        <button type="button" @click="logout">Выйти</button>
      </nav>
      <nav v-else class="user-area">
        <router-link to="/login">Вход</router-link>
        <router-link to="/register">Регистрация</router-link>
      </nav>
    </header>
    <main>
      <router-view />
    </main>
  </div>
</template>

<style>
body {
  font-family: system-ui, sans-serif;
  margin: 0;
  padding: 0;
  background: #fafafa;
  color: #222;
}

* {
  box-sizing: border-box;
}

.layout {
  max-width: 960px;
  margin: 0 auto;
  padding: 0 1rem 2rem;
}

header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1rem 0;
  border-bottom: 1px solid #e5e5e5;
  margin-bottom: 1.5rem;
}

.brand {
  font-size: 1.25rem;
  font-weight: 700;
  text-decoration: none;
  color: inherit;
}

.user-area {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  font-size: 0.9rem;
}

.email {
  font-weight: 600;
}

.role {
  background: #eef2ff;
  border-radius: 999px;
  padding: 0.1rem 0.6rem;
  font-size: 0.8rem;
  color: #3730a3;
}

a {
  color: #3730a3;
}

button {
  cursor: pointer;
}

form {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-width: 340px;
}

input {
  padding: 0.5rem 0.6rem;
  border: 1px solid #ccc;
  border-radius: 6px;
  font-size: 1rem;
}

button[type='submit'] {
  padding: 0.55rem;
  border: none;
  border-radius: 6px;
  background: #3730a3;
  color: #fff;
  font-size: 1rem;
}

.error {
  color: #b91c1c;
}

.card {
  background: #fff;
  border: 1px solid #e5e5e5;
  border-radius: 10px;
  padding: 1.5rem;
  max-width: 480px;
}
</style>
