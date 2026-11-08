import { afterEach, describe, expect, it } from 'vitest'
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import LabCard from './LabCard'
import LabCatalog from './LabCatalog'
import { parseCatalog } from './catalogApi'
import type { CatalogLab } from './catalogTypes'

afterEach(cleanup)

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
})