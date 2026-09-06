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
    <header class="topbar">
      <router-link to="/" class="brand">🍳 Меню дома</router-link>

      <nav v-if="isAuthenticated" class="nav-links">
        <router-link to="/recipes" class="topbar-link">Рецепты</router-link>
        <router-link to="/plan" class="topbar-link">План</router-link>
        <router-link to="/shopping" class="topbar-link">Покупки</router-link>
        <router-link to="/family" class="topbar-link">Семья</router-link>
        <router-link to="/settings" class="topbar-link">Настройки</router-link>
      </nav>

      <div v-if="isAuthenticated" class="user-area">
        <span class="email">{{ state.user?.email }}</span>
        <span class="role-badge" :class="{ owner: state.user?.role === 'Admin' || state.user?.role === 'Owner' }">
          {{ state.user?.role === 'Admin' || state.user?.role === 'Owner' ? 'Владелец' : 'Участник' }}
        </span>
        <button type="button" class="btn btn--ghost btn--small logout-btn" @click="logout">
          <svg class="logout-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" x2="9" y1="12" y2="12"/></svg>
          <span class="logout-text">Выйти</span>
        </button>
      </div>

      <nav v-else class="user-area">
        <router-link to="/login" class="btn btn--ghost btn--small">Вход</router-link>
        <router-link to="/register" class="btn btn--primary btn--small">Регистрация</router-link>
      </nav>
    </header>

    <main class="page">
      <router-view />
    </main>

    <nav v-if="isAuthenticated" class="bottom-nav" aria-label="Основная навигация">
      <router-link to="/recipes" class="bottom-item">
        <svg viewBox="0 0 24 24" class="nav-icon" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7h18M7 7V4a2 2 0 0 1 4 0v3M13 7V4a2 2 0 0 1 4 0v3M4 7l1 13a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1l1-13"/></svg>
        <span class="bottom-label">Рецепты</span>
      </router-link>
      <router-link to="/plan" class="bottom-item">
        <svg viewBox="0 0 24 24" class="nav-icon" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/></svg>
        <span class="bottom-label">План</span>
      </router-link>
      <router-link to="/shopping" class="bottom-item">
        <svg viewBox="0 0 24 24" class="nav-icon" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M6 2 3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4Z"/><path d="M3 6h18M16 10a4 4 0 0 1-8 0"/></svg>
        <span class="bottom-label">Покупки</span>
      </router-link>
      <router-link to="/family" class="bottom-item">
        <svg viewBox="0 0 24 24" class="nav-icon" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75"/></svg>
        <span class="bottom-label">Семья</span>
      </router-link>
      <router-link to="/settings" class="bottom-item">
        <svg viewBox="0 0 24 24" class="nav-icon" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z"/></svg>
        <span class="bottom-label">Ещё</span>
      </router-link>
    </nav>
  </div>
</template>

<style>
:root {
  --bg: #faf6f0;
  --surface: #ffffff;
  --surface-2: #f4ede3;
  --text: #3b332b;
  --text-soft: #6f665b;
  --text-faint: #9a9083;
  --border: #e9e0d3;
  --primary: #c96f2a;
  --primary-strong: #a85a1f;
  --primary-soft: #f9ead9;
  --on-primary: #ffffff;
  --success: #4f7a4f;
  --success-bg: #e7f0e4;
  --danger: #c0463f;
  --danger-bg: #fbeae8;
  --warning: #9a6a1f;
  --warning-bg: #f6edcf;
  --shadow-sm: 0 1px 2px rgba(59, 51, 43, 0.05);
  --shadow-md: 0 6px 18px rgba(59, 51, 43, 0.07);
  --radius: 14px;
  --radius-sm: 10px;
  --font: 'Nunito', system-ui, -apple-system, 'Segoe UI', sans-serif;
}

* {
  box-sizing: border-box;
}

html {
  font-size: 16px;
}

body {
  font-family: var(--font);
  margin: 0;
  padding: 0;
  background: var(--bg);
  color: var(--text);
  line-height: 1.5;
  -webkit-font-smoothing: antialiased;
}

h1, h2, h3, h4 {
  line-height: 1.25;
  color: var(--text);
}

h2 {
  font-size: 1.5rem;
  font-weight: 800;
  margin: 0 0 1rem;
}

h3 {
  font-size: 1.1rem;
  font-weight: 700;
  margin: 0 0 0.5rem;
}

a {
  color: var(--primary);
}

button {
  cursor: pointer;
  font-family: inherit;
}

input, select, textarea {
  font-family: inherit;
  font-size: 1rem;
  color: var(--text);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  padding: 0.55rem 0.7rem;
  width: 100%;
}

input:focus, select:focus, textarea:focus {
  outline: none;
  border-color: var(--primary);
  box-shadow: 0 0 0 3px rgba(201, 111, 42, 0.18);
}

input::placeholder, textarea::placeholder {
  color: var(--text-faint);
}

/* ---- layout ---- */
.layout {
  min-height: 100vh;
}

.page {
  max-width: 1100px;
  margin: 0 auto;
  padding: 1.5rem 1rem 3rem;
}

@media (min-width: 900px) {
  .page {
    padding: 2rem 2rem 3rem;
  }
}

/* ---- top bar ---- */
.topbar {
  position: sticky;
  top: 0;
  z-index: 40;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 0.6rem 1rem;
  background: rgba(250, 246, 240, 0.9);
  backdrop-filter: blur(8px);
  -webkit-backdrop-filter: blur(8px);
  border-bottom: 1px solid var(--border);
}

.brand {
  font-size: 1.25rem;
  font-weight: 800;
  text-decoration: none;
  color: var(--text);
  white-space: nowrap;
}

.nav-links {
  display: none;
  align-items: center;
  gap: 0.35rem;
  flex: 1;
  justify-content: center;
}

.topbar-link {
  text-decoration: none;
  color: var(--text-soft);
  font-weight: 600;
  padding: 0.5rem 0.9rem;
  border-radius: 999px;
  white-space: nowrap;
  display: inline-flex;
  align-items: center;
  min-height: 40px;
}

.topbar-link:hover {
  background: var(--surface-2);
  color: var(--text);
}

.topbar-link.router-link-active {
  background: var(--primary-soft);
  color: var(--primary);
}

.user-area {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-left: auto;
}

.email {
  display: none;
  font-size: 0.85rem;
  color: var(--text-soft);
  font-weight: 600;
}

.logout-icon {
  width: 18px;
  height: 18px;
  display: none;
}

@media (min-width: 900px) {
  .nav-links {
    display: flex;
  }

  .brand {
    flex: 1;
  }

  .user-area {
    margin-left: 0;
  }

  .logout-icon {
    display: none;
  }
}

@media (max-width: 899px) {
  .user-area .role-badge,
  .user-area .email {
    display: none;
  }

  .logout-btn {
    width: 40px;
    padding: 0;
  }

  .logout-icon {
    display: block;
  }

  .logout-text {
    display: none;
  }
}

@media (min-width: 1200px) {
  .email {
    display: inline;
  }
}

.role-badge {
  background: var(--surface-2);
  border-radius: 999px;
  padding: 0.15rem 0.6rem;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--text-soft);
  white-space: nowrap;
}

.role-badge.owner {
  background: var(--primary-soft);
  color: var(--primary);
}

/* ---- bottom nav (mobile) ---- */
.bottom-nav {
  position: fixed;
  left: 0;
  right: 0;
  bottom: 0;
  z-index: 40;
  display: flex;
  background: var(--surface);
  border-top: 1px solid var(--border);
  padding: 0.35rem 0 max(0.35rem, env(safe-area-inset-bottom));
  box-shadow: 0 -4px 16px rgba(59, 51, 43, 0.06);
}

.bottom-item {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.15rem;
  min-height: 52px;
  padding: 0.3rem 0.1rem;
  text-decoration: none;
  color: var(--text-faint);
  border-radius: var(--radius-sm);
}

.bottom-item:hover {
  color: var(--primary);
  background: var(--surface-2);
}

.bottom-item.router-link-active {
  color: var(--primary);
}

.nav-icon {
  width: 22px;
  height: 22px;
  display: block;
}

.bottom-label {
  font-size: 11px;
  font-weight: 700;
  line-height: 1;
}

@media (min-width: 900px) {
  .bottom-nav {
    display: none;
  }

  .page {
    padding-bottom: 3rem;
  }
}

@media (max-width: 899px) {
  .page {
    padding-bottom: 84px;
  }
}

/* ---- buttons ---- */
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  padding: 0.55rem 1rem;
  border: 1px solid transparent;
  border-radius: var(--radius-sm);
  font-weight: 700;
  font-size: 0.95rem;
  line-height: 1.2;
  text-decoration: none;
  min-height: 44px;
  transition: background 0.15s ease, color 0.15s ease, border-color 0.15s ease, transform 0.1s ease;
}

.btn:active {
  transform: translateY(1px);
}

.btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.btn--small {
  min-height: 40px;
  padding: 0.4rem 0.85rem;
  font-size: 0.9rem;
}

.btn--primary {
  background: var(--primary);
  color: var(--on-primary);
}

.btn--primary:hover:not(:disabled) {
  background: var(--primary-strong);
}

.btn--ghost {
  background: var(--surface);
  border-color: var(--border);
  color: var(--text);
}

.btn--ghost:hover:not(:disabled) {
  background: var(--surface-2);
  border-color: var(--border);
}

.btn--danger {
  background: var(--danger);
  color: var(--on-primary);
}

.btn--danger:hover:not(:disabled) {
  background: var(--danger);
  filter: brightness(0.94);
}

.btn--subtle {
  background: transparent;
  border-color: transparent;
  color: var(--primary);
}

.btn--subtle:hover:not(:disabled) {
  background: var(--primary-soft);
}

.btn--block {
  width: 100%;
}

@media (max-width: 899px) {
  .btn--block-mobile {
    width: 100%;
  }
}

/* ---- cards & headings ---- */
.card {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: clamp(1rem, 3vw, 1.5rem);
  box-shadow: var(--shadow-sm);
}

.card + .card {
  margin-top: 1rem;
}

.page-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 1.25rem;
}

.page-heading h2 {
  margin: 0;
}

.page-heading-actions {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  flex-wrap: wrap;
}

/* ---- fields ---- */
.field {
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
}

.field > label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-soft);
}

/* ---- hints & status ---- */
.hint {
  font-size: 0.82rem;
  color: var(--text-soft);
  margin: 0.25rem 0 0;
}

.error {
  color: var(--danger);
  background: var(--danger-bg);
  border-radius: var(--radius-sm);
  padding: 0.6rem 0.75rem;
  font-size: 0.9rem;
}

.success {
  color: var(--success);
  background: var(--success-bg);
  border-radius: var(--radius-sm);
  padding: 0.6rem 0.75rem;
  font-size: 0.9rem;
}

.loading {
  color: var(--text-soft);
  font-size: 0.95rem;
}

/* ---- badges & tags ---- */
.badge {
  display: inline-flex;
  align-items: center;
  background: var(--surface-2);
  color: var(--text-soft);
  border-radius: 999px;
  padding: 0.2rem 0.7rem;
  font-size: 0.8rem;
  font-weight: 600;
}

.badge--season {
  background: var(--success-bg);
  color: var(--success);
}

.badge--diet {
  background: var(--warning-bg);
  color: var(--warning);
}

.tag {
  display: inline-flex;
  align-items: center;
  background: var(--primary-soft);
  color: var(--primary);
  border-radius: 999px;
  padding: 0.2rem 0.6rem;
  font-size: 0.8rem;
  font-weight: 600;
}

/* ---- empty state ---- */
.empty-state {
  text-align: center;
  padding: 2.5rem 1.5rem;
}

.empty-state .empty-icon {
  font-size: 3rem;
  line-height: 1;
}

.empty-state .empty-title {
  font-size: 1.15rem;
  font-weight: 800;
  margin: 0.75rem 0 0.35rem;
}

.empty-state .empty-desc {
  color: var(--text-soft);
  font-size: 0.95rem;
  max-width: 42ch;
  margin: 0 auto 1.25rem;
}

/* ---- misc ---- */
hr {
  border: none;
  border-top: 1px solid var(--border);
  margin: 1.25rem 0;
}

.text-danger {
  color: var(--danger);
}
</style>
