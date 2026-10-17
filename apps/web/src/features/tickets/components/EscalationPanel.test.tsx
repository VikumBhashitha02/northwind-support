import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { TicketDetail } from '../../../types/api';
import { EscalationPanel } from './EscalationPanel';

const baseTicket: TicketDetail = {
  id: 42,
  reference: 'TCK-0042',
  title: 'Cannot access payment gateway',
  description: 'Gateway returns timeout for all customers.',
  status: 'Open',
  priority: 'High',
  customer: {
    id: 1,
    name: 'Contoso Ltd',
    email: 'contact@contoso.com',
    phone: null,
    tier: 'Premium',
  },
  category: {
    id: 1,
    name: 'Billing',
    requiresSpecialist: true,
  },
  assignedAgent: {
    id: 3,
    fullName: 'Priya Nair',
  },
  createdAtUtc: '2026-10-18T10:00:00Z',
  updatedAtUtc: '2026-10-18T10:00:00Z',
  dueAtUtc: '2026-10-18T14:00:00Z',
  resolvedAtUtc: null,
  slaStatus: 'WithinSla',
};

describe('EscalationPanel', () => {
  it('blocks submission and makes no API request when reason is shorter than 5 characters', async () => {
    const user = userEvent.setup();
    const escalateSpy = vi.spyOn(ticketsApi, 'escalateTicket');
    const onUpdated = vi.fn();
    const onEscalated = vi.fn();

    render(
      <EscalationPanel
        ticket={baseTicket}
        onUpdated={onUpdated}
        onEscalated={onEscalated}
      />,
    );

    const textarea = screen.getByLabelText(/reason for escalation/i);
    await user.clear(textarea);
    await user.type(textarea, 'bad');

    const submitButton = screen.getByRole('button', { name: /escalate ticket/i });
    await user.click(submitButton);

    expect(screen.getByText('Escalation reason must be at least 5 characters.')).toBeInTheDocument();
    expect(escalateSpy).not.toHaveBeenCalled();
    expect(onUpdated).not.toHaveBeenCalled();
    expect(onEscalated).not.toHaveBeenCalled();
  });

  it('shows HTTP 409 conflict error and does not update the ticket', async () => {
    const user = userEvent.setup();
    const conflictMessage = 'Ticket TCK-0042 is already at Critical priority and cannot be escalated.';
    const escalateSpy = vi
      .spyOn(ticketsApi, 'escalateTicket')
      .mockRejectedValueOnce(new ApiError(409, conflictMessage, { detail: conflictMessage }));

    const onUpdated = vi.fn();
    const onEscalated = vi.fn();

    render(
      <EscalationPanel
        ticket={baseTicket}
        onUpdated={onUpdated}
        onEscalated={onEscalated}
      />,
    );

    const textarea = screen.getByLabelText(/reason for escalation/i);
    await user.type(textarea, 'Urgent request from customer VP');

    const submitButton = screen.getByRole('button', { name: /escalate ticket/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(conflictMessage);
    });

    expect(escalateSpy).toHaveBeenCalledTimes(1);
    expect(onUpdated).not.toHaveBeenCalled();
    expect(onEscalated).not.toHaveBeenCalled();
  });

  it('disables or hides escalation control when ticket is already Critical', () => {
    const criticalTicket: TicketDetail = {
      ...baseTicket,
      priority: 'Critical',
    };

    render(
      <EscalationPanel
        ticket={criticalTicket}
        onUpdated={vi.fn()}
        onEscalated={vi.fn()}
      />,
    );

    expect(
      screen.getByText(/this ticket is already at critical priority and cannot be escalated/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /escalate ticket/i })).not.toBeInTheDocument();
  });

  it('disables or hides escalation control when ticket is Resolved or Closed', () => {
    const resolvedTicket: TicketDetail = {
      ...baseTicket,
      status: 'Resolved',
    };

    render(
      <EscalationPanel
        ticket={resolvedTicket}
        onUpdated={vi.fn()}
        onEscalated={vi.fn()}
      />,
    );

    expect(
      screen.getByText(/this ticket is resolved and cannot be escalated/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /escalate ticket/i })).not.toBeInTheDocument();
  });

  it('successfully escalates ticket, calls onUpdated and onEscalated, and clears reason', async () => {
    const user = userEvent.setup();
    const updatedTicket: TicketDetail = {
      ...baseTicket,
      priority: 'Critical',
      dueAtUtc: '2026-10-18T12:00:00Z',
    };

    const escalationRecord = {
      id: 1,
      ticketId: 42,
      fromPriority: 'High' as const,
      toPriority: 'Critical' as const,
      fromAgentId: 3,
      toAgentId: 3,
      fromDueAtUtc: baseTicket.dueAtUtc,
      toDueAtUtc: '2026-10-18T12:00:00Z',
      reason: 'Urgent escalation requested by operations lead',
      escalatedBy: 'Support Lead',
      escalatedAtUtc: '2026-10-18T10:30:00Z',
    };

    const escalateSpy = vi
      .spyOn(ticketsApi, 'escalateTicket')
      .mockResolvedValueOnce({ ticket: updatedTicket, escalation: escalationRecord });

    const onUpdated = vi.fn();
    const onEscalated = vi.fn();

    render(
      <EscalationPanel
        ticket={baseTicket}
        onUpdated={onUpdated}
        onEscalated={onEscalated}
      />,
    );

    const textarea = screen.getByLabelText(/reason for escalation/i);
    await user.type(textarea, 'Urgent escalation requested by operations lead');

    const submitButton = screen.getByRole('button', { name: /escalate ticket/i });
    await user.click(submitButton);

    await waitFor(() => {
      expect(onUpdated).toHaveBeenCalledWith(updatedTicket);
      expect(onEscalated).toHaveBeenCalledTimes(1);
    });

    expect(escalateSpy).toHaveBeenCalledWith(42, {
      reason: 'Urgent escalation requested by operations lead',
      escalatedBy: 'Support Lead',
    });
    expect(textarea).toHaveValue('');
  });
});
