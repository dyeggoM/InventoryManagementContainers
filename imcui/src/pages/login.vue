<template>
  <q-layout view="lHh Lpr lFf">
    <q-page-container>
      <q-page class="flex flex-center login-page">
        <q-card class="login-card q-pa-lg" flat bordered>
          <div class="row items-center q-gutter-sm q-mb-lg">
            <q-icon name="inventory_2" size="32px" color="primary" />
            <div class="column">
              <div class="text-h6">IMC</div>
              <div class="text-caption text-grey-7">Inventory Management</div>
            </div>
          </div>

          <q-form @submit="submit" class="column q-gutter-md">
            <q-input
              v-model="email"
              label="Correo"
              type="email"
              autocomplete="username"
              outlined
              autofocus
              :disable="auth.loading"
              :rules="[(value: string) => !!value.trim() || 'Escribe tu correo']"
            >
              <template v-slot:prepend>
                <q-icon name="mail_outline" />
              </template>
            </q-input>

            <q-input
              v-model="password"
              label="Contraseña"
              :type="showPassword ? 'text' : 'password'"
              autocomplete="current-password"
              outlined
              :disable="auth.loading"
              :rules="[(value: string) => !!value || 'Escribe tu contraseña']"
            >
              <template v-slot:prepend>
                <q-icon name="lock_outline" />
              </template>
              <template v-slot:append>
                <q-btn
                  flat
                  round
                  dense
                  type="button"
                  :icon="showPassword ? 'visibility_off' : 'visibility'"
                  :aria-label="showPassword ? 'Ocultar la contraseña' : 'Mostrar la contraseña'"
                  @click="showPassword = !showPassword"
                />
              </template>
            </q-input>

            <!-- The failure has to be visible where the form is, not only as a toast. -->
            <div
              v-if="message"
              class="login-error q-pa-sm rounded-borders"
              role="alert"
              aria-live="polite"
            >
              <q-icon name="error_outline" size="18px" class="q-mr-xs" />
              {{ message }}
            </div>

            <q-banner v-else-if="expired" dense class="bg-orange-1 text-orange-10">
              Tu sesión expiró. Vuelve a iniciar sesión para continuar.
            </q-banner>

            <q-btn
              label="Entrar"
              type="submit"
              color="primary"
              unelevated
              no-caps
              class="full-width"
              :loading="auth.loading"
            />
          </q-form>
        </q-card>
      </q-page>
    </q-page-container>
  </q-layout>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { safeRedirectTarget } from '../router/guards';
import { useAuthStore } from '../stores/auth-store';

const auth = useAuthStore();
const route = useRoute();
const router = useRouter();

const email = ref('');
const password = ref('');
const showPassword = ref(false);
/** Set when the form itself refuses to submit, before the API is even called. */
const localError = ref<string | null>(null);

const message = computed(() => localError.value ?? auth.error);
/** The guard sent the user here from somewhere: say why before they type. */
const expired = computed(() => typeof route.query.redirect === 'string' && route.query.redirect !== '');

async function submit(): Promise<void> {
  localError.value = null;
  if (!email.value.trim() || !password.value) {
    localError.value = 'Escribe tu correo y tu contraseña.';
    return;
  }

  const signedIn = await auth.login(email.value.trim(), password.value);
  if (!signedIn) return;

  // Only a same-origin path is honoured: the query string must not become an open redirect.
  await router.replace(safeRedirectTarget(route.query.redirect));
}
</script>

<style scoped>
.login-page {
  min-height: 100vh;
  background: var(--imc-surface-sunken);
}

.login-card {
  width: 100%;
  max-width: 400px;
}

.login-error {
  background: var(--imc-danger-bg);
  color: var(--imc-danger-text);
  font-size: 13px;
}
</style>
