import type { Optional } from '@speckle/shared'
import { useSynchronizedCookie } from '~~/lib/common/composables/reactiveCookie'
import { CookieKeys } from '~~/lib/common/helpers/constants'

export enum AppTheme {
  Light = 'light',
  Dark = 'dark'
}

/**
 * Use this to read & write theme
 */
export function useTheme() {
  const themeCookie = useSynchronizedCookie<Optional<AppTheme>>(CookieKeys.Theme)
  // ORBIT: default to dark (matches the ORBIT portal). Only light when explicitly chosen.
  const isDarkTheme = computed(() => themeCookie.value !== AppTheme.Light)
  const isLightTheme = computed(() => !isDarkTheme.value)

  const setTheme = (newTheme: AppTheme) => {
    themeCookie.value = newTheme
  }

  const toggleTheme = () => {
    if (isDarkTheme.value) {
      setTheme(AppTheme.Light)
    } else {
      setTheme(AppTheme.Dark)
    }
  }

  return {
    setTheme,
    toggleTheme,
    isDarkTheme,
    isLightTheme
  }
}
