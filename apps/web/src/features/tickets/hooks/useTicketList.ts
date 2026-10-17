import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { PagedResult, TicketFilters, TicketListItem } from '../../../types/api';

const DEBOUNCE_MS = 300;

export const emptyFilters: TicketFilters = {
  search: '',
  status: undefined,
  priority: undefined,
  categoryId: undefined,
  customerId: undefined,
  assignedAgentId: undefined,
  unassignedOnly: false,
  slaStatus: undefined,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAtUtc',
  sortDirection: 'desc',
};

interface State {
  isLoading: boolean;
  data?: PagedResult<TicketListItem>;
  error?: string;
}

/** Owns the ticket list's filters and the data they produce. */
export function useTicketList() {
  const [filters, setFilters] = useState<TicketFilters>(emptyFilters);
  const [debouncedSearch, setDebouncedSearch] = useState(filters.search);
  const [state, setState] = useState<State>({ isLoading: true });

  const queryParams = useMemo<TicketFilters>(
    () => ({
      ...filters,
      search: debouncedSearch,
    }),
    [filters, debouncedSearch],
  );

  // Debounce search input so normal typing does not spam requests on every keystroke
  useEffect(() => {
    const handle = window.setTimeout(() => {
      setDebouncedSearch(filters.search);
    }, DEBOUNCE_MS);

    return () => window.clearTimeout(handle);
  }, [filters.search]);

  // Extract a stable serialized key of all non-search filter parameters.
  // Any change to status, priority, category, customer, agent, unassignedOnly,
  // slaStatus, page, pageSize, or sort immediately triggers a new request.
  const nonSearchFilters = useMemo(
    () => Object.fromEntries(Object.entries(filters).filter(([key]) => key !== 'search')),
    [filters],
  );
  const nonSearchKey = JSON.stringify(nonSearchFilters);

  const load = useCallback(async () => {
    setState((current) => ({ ...current, isLoading: true, error: undefined }));

    try {
      const data = await ticketsApi.getTickets(queryParams);
      setState({ isLoading: false, data, error: undefined });
    } catch (error) {
      const message = error instanceof ApiError ? error.message : 'Unable to load tickets.';
      setState((current) => ({ ...current, isLoading: false, error: message }));
    }
  }, [queryParams]);

  useEffect(() => {
    let isCancelled = false;

    const execute = async () => {
      setState((current) => ({ ...current, isLoading: true, error: undefined }));

      try {
        const data = await ticketsApi.getTickets(queryParams);
        if (!isCancelled) {
          setState({ isLoading: false, data, error: undefined });
        }
      } catch (error) {
        if (!isCancelled) {
          const message = error instanceof ApiError ? error.message : 'Unable to load tickets.';
          setState((current) => ({ ...current, isLoading: false, error: message }));
        }
      }
    };

    void execute();

    return () => {
      isCancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- nonSearchKey captures all non-search filter mutations immediately
  }, [nonSearchKey, debouncedSearch]);

  const updateFilters = (patch: Partial<TicketFilters>) =>
    setFilters((current) => ({ ...current, ...patch, page: patch.page ?? 1 }));

  const resetFilters = () => {
    setFilters(emptyFilters);
    setDebouncedSearch(emptyFilters.search);
  };

  return { filters, updateFilters, resetFilters, reload: load, ...state };
}
