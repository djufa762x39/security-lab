import type { ReactNode } from 'react'
import type { AuthUser } from '../models/AuthUser'

export type AuthProviderProps = {
  children: ReactNode
}

export type AuthContextValue = {
  user: AuthUser | null
  isLoading: boolean
  isAuthenticated: boolean
  initializationError: string | null
  refreshUser: () => Promise<void>
  login: (email: string, password: string) => Promise<void>
  logout: () => Promise<void>
  request: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
}
