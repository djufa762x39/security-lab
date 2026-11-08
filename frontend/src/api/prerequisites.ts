
import { apiRequest } from './http'

// Podaci o uspesno dodatom preduslovu.
export interface Prerequisite {
  laboratoryId: string
  requiredLaboratoryId: string
}

// Ucitavanje preduslova odredjene laboratorije.
export function getPrerequisites(laboratoryId: string) {
  return apiRequest<string[]>(
    'GET',
    `/api/laboratories/${encodeURIComponent(laboratoryId)}/prerequisites`
  )
}

// Dodavanje novog preduslova.
export function addPrerequisite(
  laboratoryId: string,
  requiredLaboratoryId: string
) {
  return apiRequest<Prerequisite>(
    'POST',
    `/api/laboratories/${encodeURIComponent(laboratoryId)}/prerequisites`,
    { requiredLaboratoryId }
  )
}

// Uklanjanje postojeceg preduslova.
export function removePrerequisite(
  laboratoryId: string,
  requiredLaboratoryId: string
) {
  return apiRequest<void>(
    'DELETE',
    `/api/laboratories/${encodeURIComponent(laboratoryId)}/prerequisites/${encodeURIComponent(requiredLaboratoryId)}`
  )
}
