import { useNavigate, Routes, Route } from 'react-router-dom'
import { useState } from 'react'
import { AuthProvider, useAuth } from './auth/AuthContext'
import AppShell from './components/AppShell'
import LandingPage from './components/LandingPage'
import CourseworkKt1 from './components/CourseworkKt1'
import ProtectedRoute from './components/ProtectedRoute'
import ProtectedApp from './components/ProtectedApp'
import LoginPage from './login/page'

function LoginRoute() {
  const navigate = useNavigate()
  const { refreshUser } = useAuth()
  const [error, setError] = useState('')

  return <>
    <LoginPage onSuccess={() => {
      setError('')
      void refreshUser()
        .then(() => navigate('/access-demo'))
        .catch(() => setError('Login succeeded, but the session could not be verified. Please try again.'))
      return true
    }} />
    {error && <p className="login-error" role="alert">{error}</p>}
  </>
}

export default function App() {
  return <AuthProvider>
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<><LandingPage /><CourseworkKt1 /></>} />
        <Route path="login" element={<LoginRoute />} />
        <Route element={<ProtectedRoute />}>
          <Route path="access-demo" element={<ProtectedApp />} />
        </Route>
      </Route>
    </Routes>
  </AuthProvider>
}
