import * as signalR from '@microsoft/signalr'
import { useEffect, useRef } from 'react'
import type { DuelState, StageEvent } from '@Utils/zarrar/api'

export interface ZarrarHubHandlers {
  onState?: (state: DuelState) => void
  onEvent?: (event: StageEvent) => void
  onReconnected?: () => void
}

/**
 * Subscribes to `/hub/zarrar` for one game. Monitors and admins receive stage events and duel
 * state; players receive duel state only (the server decides by role).
 */
export const useZarrarHub = (gameId: number, handlers: ZarrarHubHandlers, enabled = true, screen = false) => {
  const ref = useRef(handlers)
  ref.current = handlers

  useEffect(() => {
    if (!enabled || !Number.isFinite(gameId) || gameId <= 0) return

    const connection = new signalR.HubConnectionBuilder()
      // screen=1 marks a projector stage, so the admin checklist can see the monitor is up.
      .withUrl(`/hub/zarrar?game=${gameId}${screen ? '&screen=1' : ''}`)
      .withHubProtocol(new signalR.JsonHubProtocol())
      .withAutomaticReconnect([0, 1000, 2000, 5000, 5000, 10000, 10000, 30000])
      .configureLogging(signalR.LogLevel.None)
      .build()

    connection.serverTimeoutInMilliseconds = 60 * 1000 * 10

    connection.on('DuelState', (state: DuelState) => ref.current.onState?.(state))
    connection.on('StageEvent', (event: StageEvent) => ref.current.onEvent?.(event))
    connection.onreconnected(() => ref.current.onReconnected?.())

    let stopped = false
    const start = async () => {
      try {
        await connection.start()
      } catch {
        if (!stopped) setTimeout(start, 3000)
      }
    }
    start()

    return () => {
      stopped = true
      connection.stop()
    }
  }, [gameId, enabled, screen])
}
