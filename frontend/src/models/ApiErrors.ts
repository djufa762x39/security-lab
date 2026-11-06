export class ApiError extends Error {
  readonly status: number

  constructor(status: number) {
    super(`API request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
  }
}

export class NetworkError extends Error {
  constructor(cause?: unknown) {
    super('The API could not be reached.', { cause })
    this.name = 'NetworkError'
  }
}
