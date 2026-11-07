import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

export default function LandingPage() {
  const { isAuthenticated } = useAuth()

  return <section className="landing-page">
    <p className="eyebrow">Security Lab / OIB</p>
    <h1>Learn information security by doing.</h1>
    <p>
      Explore practical security laboratories and build a stronger understanding
      of secure systems through guided exercises.
    </p>
    <div className="landing-actions">
      <Link className="primary-link" to={isAuthenticated ? '/access-demo' : '/login'}>
        {isAuthenticated ? 'Open laboratory' : 'Log in'}
      </Link>
      <a className="secondary-link" href="#about">About the platform</a>
    </div>
    <div id="about" className="landing-highlights">
      <article>
        <h2>Practice</h2>
        <p>Work with realistic access and identity scenarios.</p>
      </article>
      <article>
        <h2>Learn securely</h2>
        <p>Server-side checks protect every authenticated operation.</p>
      </article>
      <article>
        <h2>Track progress</h2>
        <p>Continue your work after signing in again.</p>
      </article>
    </div>
  </section>
}
