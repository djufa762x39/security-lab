
import PrerequisitesEditor from '../components/prerequisites/PrerequisitesEditor'
import PrerequisitesList from '../components/prerequisites/PrerequisitesList'

interface PrerequisitesPageProps {
  laboratoryId: string
  mode: 'teacher' | 'student'
  laboratoryNames?: Record<string, string>
}

export default function PrerequisitesPage({
  laboratoryId,
  mode,
  laboratoryNames = {},
}: PrerequisitesPageProps) {
  return (
    <main>
      <h1>Preduslovi laboratorije</h1>

      {mode === 'teacher' ? (
        <PrerequisitesEditor laboratoryId={laboratoryId} />
      ) : (
        <PrerequisitesList
          laboratoryId={laboratoryId}
          laboratoryNames={laboratoryNames}
        />
      )}
    </main>
  )
}
