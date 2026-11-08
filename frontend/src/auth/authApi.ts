import { apiFetch } from './apiClient'
import { ApiError } from '../models/ApiErrors'
import type { AuthUser } from '../models/AuthUser'

export async function getCurrentUser(): Promise<AuthUser | null> {
  const response = await apiFetch('/api/auth/me')
  if (response.status === 401) return null
  if (!response.ok) throw new ApiError(response.status)
  return response.json() as Promise<AuthUser>
}

export async function login(email: string, password: string): Promise<void> {
  const response = await apiFetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, password })
  })

  if (!response.ok) throw new ApiError(response.status)
}

export async function logout(): Promise<void> {
  const response = await apiFetch('/api/auth/logout', { method: 'POST' })
  if (!response.ok) throw new ApiError(response.status)
}
