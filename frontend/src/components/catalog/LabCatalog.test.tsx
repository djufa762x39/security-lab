import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { setFetcher } from '../../api/http'
import LabCard from './LabCard'
import LabCatalog from './LabCatalog'
import { fetchCatalogFromServer, parseCatalog, UNKNOWN_PREREQUISITE_TITLE } from './catalogApi'
import type { CatalogLab } from './catalogTypes'

afterEach(() => {
  cleanup()
  setFetcher((input, init) => fetch(input, init))
})

const OPEN: CatalogLab = {
  id: 'lab-open',
  title: 'Kontrola pristupa',
  objective: 'Prepoznati zamenu ID vrednosti.',
  type: 'AccessControl',
  difficulty: 'Easy',
  tags: ['rbac', 'idor'],
  access: { isUnlocked: true, missingPrerequisites: [] },
}

const LOCKED: CatalogLab = {
  id: 'lab-locked',
  title: 'Odgovor na incident',
  objective: 'Odrediti prioritet događaja.',
  type: 'IncidentResponse',
  difficulty: 'Hard',
  tags: ['incident'],
  access: { isUnlocked: false, missingPrerequisites: [{ id: 'lab-open', title: 'Kontrola pristupa' }] },
}

const loadBoth = () => Promise.resolve({ ok: true as const, labs: [OPEN, LOCKED] })

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('LabCard', () => {
  it('kartica prikazuje metapodatke laboratorije', () => {
    render(<LabCard lab={OPEN} />)
    expect(screen.getByRole('heading', { name: OPEN.title })).toBeTruthy()
    expect(screen.getByText(OPEN.objective)).toBeTruthy()
    expect(screen.getByText('Kontrola pristupa', { selector: 'dd' })).toBeTruthy()
    expect(screen.getByText('Lako')).toBeTruthy()
    expect(screen.getByText('rbac')).toBeTruthy()
    expect(screen.queryByText('Zaključano')).toBeNull()
  })

  it('zaključana laboratorija prikazuje uslov otključavanja koji je vratio server', () => {
    render(<LabCard lab={LOCKED} />)
    expect(screen.getByText('Zaključano')).toBeTruthy()
    expect(screen.getByText('Za otključavanje je potrebno završiti:')).toBeTruthy()
    expect(screen.getByText('Kontrola pristupa', { selector: 'li' })).toBeTruthy()
  })
})

describe('LabCatalog', () => {
  it('filter po težini menja listu', async () => {
    render(<LabCatalog load={loadBoth} />)
    await screen.findByRole('heading', { name: LOCKED.title })
    expect(screen.getByRole('heading', { name: OPEN.title })).toBeTruthy()

    fireEvent.change(screen.getByLabelText('Težina'), { target: { value: 'Hard' } })

    expect(screen.queryByRole('heading', { name: OPEN.title })).toBeNull()
    expect(screen.getByRole('heading', { name: LOCKED.title })).toBeTruthy()
  })

  it('greška servera prikazuje poruku umesto liste', async () => {
    render(<LabCatalog load={() => Promise.resolve({ ok: false as const, message: 'Nemate pristup katalogu.' })} />)
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.queryByRole('heading', { level: 3 })).toBeNull()
  })
})

describe('parseCatalog', () => {
  it('laboratorija bez odluke servera se tretira kao zaključana, a neispravne stavke se izostavljaju', () => {
    const labs = parseCatalog([
      { id: 'a', title: 'A', objective: 'Cilj', type: 0, difficulty: 2, tags: ['x'] },
      { id: 'b', title: 'B', objective: 'Cilj', type: 'NepostojeciTip', difficulty: 'Easy', tags: [] },
    ])
    expect(labs).toHaveLength(1)
    expect(labs![0].type).toBe('AccessControl')
    expect(labs![0].difficulty).toBe('Hard')
    expect(labs![0].access.isUnlocked).toBe(false)
  })

  it('odgovor koji nije lista se odbija', () => {
    expect(parseCatalog({ labs: [] })).toBeNull()
  })

  it('nedostajući preduslov dobija naziv iz kataloga, a nevidljiva laboratorija ne otkriva ID', () => {
    const labs = parseCatalog([
      { id: 'a', title: 'Prva', objective: 'C', type: 'AccessControl', difficulty: 'Easy', tags: [], access: { isUnlocked: true, missingPrerequisites: [] } },
      { id: 'b', title: 'Druga', objective: 'C', type: 'LogAnalysis', difficulty: 'Medium', tags: [], access: { isUnlocked: false, missingPrerequisites: ['a', 'skrivena'] } },
    ])
    expect(labs![1].access.missingPrerequisites).toEqual([
      { id: 'a', title: 'Prva' },
      { id: 'skrivena', title: UNKNOWN_PREREQUISITE_TITLE },
    ])
  })
})

describe('fetchCatalogFromServer', () => {
  it('za svaku laboratoriju pita server za stanje, a neuspešan odgovor ostavlja laboratoriju zaključanom', async () => {
    setFetcher(async input => {
      if (input === '/api/catalog') return json(200, [
        { id: 'a', title: 'Prva', objective: 'C', type: 'AccessControl', difficulty: 'Easy', tags: [] },
        { id: 'b', title: 'Druga', objective: 'C', type: 'LogAnalysis', difficulty: 'Medium', tags: [] },
        { id: 'c', title: 'Treća', objective: 'C', type: 'IncidentResponse', difficulty: 'Hard', tags: [] },
      ])
      if (input === '/api/laboratories/a/access') return json(200, { isUnlocked: true, missingPrerequisites: [] })
      if (input === '/api/laboratories/b/access') return json(200, { isUnlocked: false, missingPrerequisites: ['a'] })
      return json(500, {})
    })

    const result = await fetchCatalogFromServer()

    expect(result.ok).toBe(true)
    if (!result.ok) return
    const [a, b, c] = result.labs
    expect(a.access.isUnlocked).toBe(true)
    expect(b.access).toEqual({ isUnlocked: false, missingPrerequisites: [{ id: 'a', title: 'Prva' }] })
    expect(c.access.isUnlocked).toBe(false)
  })

  it('neprijavljen korisnik dobija poruku umesto kataloga', async () => {
    setFetcher(async () => json(401, {}))

    const result = await fetchCatalogFromServer()

    expect(result).toEqual({ ok: false, message: 'Niste prijavljeni.' })
  })
})