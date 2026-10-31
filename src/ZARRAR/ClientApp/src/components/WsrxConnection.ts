export interface WsrxConnectionErrorHandlers {
  isDaemonUnavailable: (error: unknown) => boolean
  onError: (error: unknown) => void
}

export const connectWsrxSafely = async (
  connect: () => Promise<unknown>,
  handlers: WsrxConnectionErrorHandlers
): Promise<void> => {
  try {
    await connect()
  } catch (error) {
    if (!handlers.isDaemonUnavailable(error)) handlers.onError(error)
  }
}
