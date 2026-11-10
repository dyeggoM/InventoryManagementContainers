import { defineRouter } from '#q-app';
import { routes, handleHotUpdate } from 'vue-router/auto-routes';
import {
  createMemoryHistory,
  createRouter,
  createWebHashHistory,
  createWebHistory,
} from 'vue-router';
import { setLoginRedirect } from '../services/api';
import { useAuthStore } from '../stores/auth-store';
import { LOGIN_PATH, resolveNavigation } from './guards';

/*
 * If not building with SSR mode, you can
 * directly export the Router instantiation;
 *
 * The function below can be async too; either use
 * async/await or return a Promise which resolves
 * with the Router instance.
 */

export default defineRouter(({ store /*, ssrContext */ }) => {
  const createHistory = import.meta.env.QUASAR_SERVER
    ? createMemoryHistory
    : import.meta.env.QUASAR_VUE_ROUTER_MODE === 'history'
      ? createWebHistory
      : createWebHashHistory;

  const Router = createRouter({
    scrollBehavior: () => ({ left: 0, top: 0 }),
    routes,

    // Leave this as is and make changes in quasar.conf.js instead!
    // quasar.conf.js -> build -> vueRouterMode
    // quasar.conf.js -> build -> publicPath
    history: createHistory(import.meta.env.QUASAR_VUE_ROUTER_BASE),
  });

  /**
   * Every route but /login needs a session; /dashboard needs the superadmin role and
   * /configuration the admin one. The rules themselves live in ./guards so they can be tested
   * without a router.
   */
  Router.beforeEach(async (to) => {
    const auth = useAuthStore(store);

    // An access token that has already expired still has a refresh cookie behind it: trade it
    // before letting the navigation through, or the first request of the page bounces off a 401.
    if (auth.isAuthenticated && auth.tokenExpired) await auth.refresh();

    const decision = resolveNavigation({
      path: to.path,
      isAuthenticated: auth.isAuthenticated,
      roles: auth.roles,
    });

    return decision.allowed ? true : decision.redirect;
  });

  /**
   * A request that cannot be refreshed (see services/api.ts) ends up here: the app sends the user
   * to the login page, remembering the route so signing in returns them to it.
   */
  setLoginRedirect((returnTo) => {
    const query = returnTo ? { redirect: returnTo } : {};
    void Router.replace({ path: LOGIN_PATH, query });
  });

  // enable HMR for it
  if (import.meta.hot) {
    handleHotUpdate(Router);
  }

  return Router;
});
