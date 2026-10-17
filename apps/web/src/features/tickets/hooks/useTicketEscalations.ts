import { useCallback } from 'react';
import { ticketsApi } from '../../../api/tickets';
import { useAsyncData } from '../../../hooks/useAsyncData';

/** Loads escalation history for a ticket, refetching when the id changes. */
export function useTicketEscalations(id: number) {
  const load = useCallback(() => ticketsApi.getEscalations(id), [id]);

  return useAsyncData(load, 'Unable to load escalation history.');
}
