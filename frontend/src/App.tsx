import CourseworkKt1 from './components/CourseworkKt1'
import { useEffect, useState } from 'react'
import Acc06 from './components/acc06/Acc06'
import CreateAccountForm from './components/CreateAccountForm'

export default function App() {
  const [health, setHealth] = useState('Checking API…')
  useEffect(() => {
    fetch('/api/health').then(r => r.ok ? r.json() : Promise.reject(r.status))
      .then(data => setHealth(data.status === 'ok' ? 'API available' : 'API is not ready'))
      .catch(() => setHealth('API unavailable (start the backend).'))
  }, [])
  return <main>
    <p className="eyebrow">Security Lab / OIB</p>
    <h1>Security Lab</h1>
    <p>An interactive laboratory platform for the Fundamentals of Information Security course. Develop features through the assigned project components.</p>
    <section><h2>Development status</h2><p>{health}</p><p>KT1: initial implementation, component architecture, and data flow diagram.</p></section>
    <section><h2>Kreiranje naloga (administrator)</h2><CreateAccountForm /></section>
    <CourseworkKt1 />
    <Acc06 />
  </main>
}
