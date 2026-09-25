import { createRouter, createWebHistory } from 'vue-router'
import { useAuth } from '../stores/auth'
import HomeView from '../views/HomeView.vue'
import LoginView from '../views/LoginView.vue'
import RegisterView from '../views/RegisterView.vue'
import VerifyEmailView from '../views/VerifyEmailView.vue'
import ForgotPasswordView from '../views/ForgotPasswordView.vue'
import AdminView from '../views/AdminView.vue'
import FamilyView from '../views/FamilyView.vue'
import RecipesView from '../views/RecipesView.vue'
import RecipeFormView from '../views/RecipeFormView.vue'
import RecipeDetailView from '../views/RecipeDetailView.vue'
import SharedRecipeView from '../views/SharedRecipeView.vue'
import PlanView from '../views/PlanView.vue'
import SettingsView from '../views/SettingsView.vue'
import ShoppingView from '../views/ShoppingView.vue'

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: '/', name: 'home', component: HomeView, meta: { requiresAuth: true } },
    { path: '/verify', name: 'verify', component: VerifyEmailView, meta: { requiresAuth: true } },
    { path: '/family', name: 'family', component: FamilyView, meta: { requiresAuth: true } },
    { path: '/recipes', name: 'recipes', component: RecipesView, meta: { requiresAuth: true } },
    {
      path: '/recipes/new',
      name: 'recipe-new',
      component: RecipeFormView,
      meta: { requiresAuth: true, requiresVerified: true }
    },
    { path: '/recipes/:id', name: 'recipe-detail', component: RecipeDetailView, meta: { requiresAuth: true } },
    {
      path: '/recipes/:id/edit',
      name: 'recipe-edit',
      component: RecipeFormView,
      meta: { requiresAuth: true, requiresVerified: true }
    },
    { path: '/plan', name: 'plan', component: PlanView, meta: { requiresAuth: true } },
    { path: '/settings', name: 'settings', component: SettingsView, meta: { requiresAuth: true } },
    { path: '/shopping', name: 'shopping', component: ShoppingView, meta: { requiresAuth: true } },
    { path: '/admin', name: 'admin', component: AdminView, meta: { requiresAuth: true, requiresAdmin: true } },
    { path: '/r/:token', name: 'shared-recipe', component: SharedRecipeView },
    { path: '/forgot', name: 'forgot', component: ForgotPasswordView },
    { path: '/login', name: 'login', component: LoginView, meta: { guestOnly: true } },
    { path: '/register', name: 'register', component: RegisterView, meta: { guestOnly: true } }
  ]
})

router.beforeEach((to) => {
  const { isAuthenticated, isEmailVerified, isAdmin } = useAuth()
  if (to.meta.requiresAuth && !isAuthenticated.value) {
    return { name: 'login' }
  }
  if (to.meta.guestOnly && isAuthenticated.value) {
    return { name: 'home' }
  }
  if (to.meta.requiresAdmin && !isAdmin.value) {
    return { name: 'home' }
  }
  if (to.meta.requiresVerified && !isEmailVerified.value) {
    return { name: 'verify' }
  }
  return true
})

export default router
