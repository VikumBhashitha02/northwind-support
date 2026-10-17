import { useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { TicketDetail } from '../../../types/api';

interface EscalationPanelProps {
  ticket: TicketDetail;
  onUpdated: (ticket: TicketDetail) => void;
  onEscalated: () => void;
}

export function EscalationPanel({ ticket, onUpdated, onEscalated }: EscalationPanelProps) {
  const [reason, setReason] = useState('');
  const [actor, setActor] = useState('Support Lead');
  const [clientError, setClientError] = useState<string | null>(null);
  const [serverError, setServerError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const isProhibited =
    ticket.priority === 'Critical' ||
    ticket.status === 'Resolved' ||
    ticket.status === 'Closed';

  const prohibitedReason =
    ticket.priority === 'Critical'
      ? 'This ticket is already at Critical priority and cannot be escalated.'
      : ticket.status === 'Resolved' || ticket.status === 'Closed'
      ? `This ticket is ${ticket.status.toLowerCase()} and cannot be escalated.`
      : null;

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setClientError(null);
    setServerError(null);

    const trimmedReason = reason.trim();
    if (trimmedReason.length < 5) {
      setClientError('Escalation reason must be at least 5 characters.');
      return;
    }

    if (trimmedReason.length > 500) {
      setClientError('Escalation reason cannot exceed 500 characters.');
      return;
    }

    const trimmedActor = actor.trim();
    if (!trimmedActor) {
      setClientError('Actor name is required.');
      return;
    }

    setIsSubmitting(true);

    try {
      const response = await ticketsApi.escalateTicket(ticket.id, {
        reason: trimmedReason,
        escalatedBy: trimmedActor,
      });

      setReason('');
      onUpdated(response.ticket);
      onEscalated();
    } catch (err) {
      if (err instanceof ApiError) {
        setServerError(err.problem?.detail ?? err.message);
      } else if (err instanceof Error) {
        setServerError(err.message);
      } else {
        setServerError('Failed to escalate ticket. Please try again.');
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="card" style={{ marginTop: 'var(--gap)' }}>
      <h2>Escalate Ticket</h2>

      {isProhibited ? (
        <div
          role="note"
          style={{
            padding: '12px',
            borderRadius: 'var(--radius)',
            background: 'var(--colour-warning-bg)',
            color: 'var(--colour-warning-text)',
            fontSize: '0.9rem',
          }}
        >
          {prohibitedReason}
        </div>
      ) : (
        <form onSubmit={handleSubmit} noValidate>
          <div className="field" style={{ marginBottom: '12px' }}>
            <label className="field__label" htmlFor="escalate-actor">
              Escalated by
            </label>
            <input
              id="escalate-actor"
              type="text"
              value={actor}
              disabled={isSubmitting}
              onChange={(e) => {
                setActor(e.target.value);
                if (clientError) setClientError(null);
              }}
              required
            />
          </div>

          <div className="field" style={{ marginBottom: '12px' }}>
            <label className="field__label" htmlFor="escalate-reason">
              Reason for escalation (5–500 characters)
            </label>
            <textarea
              id="escalate-reason"
              rows={3}
              value={reason}
              disabled={isSubmitting}
              placeholder="Explain why this ticket requires higher priority and SLA recalculation..."
              onChange={(e) => {
                setReason(e.target.value);
                if (clientError) setClientError(null);
              }}
              required
            />
          </div>

          {clientError && (
            <div
              role="alert"
              style={{
                color: 'var(--colour-danger-text)',
                background: 'var(--colour-danger-bg)',
                padding: '8px 12px',
                borderRadius: 'var(--radius)',
                marginBottom: '12px',
                fontSize: '0.9rem',
              }}
            >
              {clientError}
            </div>
          )}

          {serverError && (
            <div
              role="alert"
              style={{
                color: 'var(--colour-danger-text)',
                background: 'var(--colour-danger-bg)',
                padding: '8px 12px',
                borderRadius: 'var(--radius)',
                marginBottom: '12px',
                fontSize: '0.9rem',
              }}
            >
              {serverError}
            </div>
          )}

          <button
            type="submit"
            className="button button--danger"
            disabled={isSubmitting}
            style={{ width: '100%' }}
          >
            {isSubmitting ? 'Escalating...' : 'Escalate ticket'}
          </button>
        </form>
      )}
    </div>
  );
}
