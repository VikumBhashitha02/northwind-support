import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ErrorMessage } from '../../components/ErrorMessage';
import { Spinner } from '../../components/Spinner';
import { useReferenceData } from '../../hooks/useReferenceData';
import { formatDateTime } from '../../lib/format';
import type { TicketDetail } from '../../types/api';
import { CreateTicketForm } from './components/CreateTicketForm';
import { SlaIndicator } from './components/SlaIndicator';
import { PriorityBadge } from './components/TicketBadges';

export function CreateTicketPage() {
  const reference = useReferenceData();
  const navigate = useNavigate();
  const [created, setCreated] = useState<TicketDetail>();

  return (
    <section>
      <header className="page-header">
        <div>
          <p className="breadcrumb">
            <Link to="/tickets">Tickets</Link> / New
          </p>
          <h1>Raise a ticket</h1>
        </div>
      </header>

      {reference.isLoading && <Spinner label="Loading customers and categories" />}

      {reference.error && (
        <ErrorMessage message={reference.error} onRetry={() => void reference.reload()} />
      )}

      {created && (
        <div className="card notice" role="status" style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
          <div>
            <h2 style={{ margin: '0 0 8px 0' }}>Ticket Raised: {created.reference}</h2>
            <p style={{ margin: 0 }}>
              Raised for <strong>{created.customer.name}</strong> ({created.category.name}).
            </p>
          </div>

          <div
            style={{
              padding: '16px',
              borderRadius: 'var(--radius)',
              background: 'var(--colour-page)',
              border: '1px solid var(--colour-border)',
              display: 'flex',
              flexDirection: 'column',
              gap: '12px',
            }}
          >
            <h3 style={{ margin: 0, fontSize: '1rem' }}>Automatic Triage &amp; SLA Decision</h3>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '12px' }}>
              <div>
                <span className="summary__note">Applied Priority</span>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '4px' }}>
                  <PriorityBadge priority={created.priority} />
                </div>
                {created.triage?.priorityReason && (
                  <span className="summary__note" style={{ display: 'block', marginTop: '4px' }}>
                    {created.triage.priorityReason}
                  </span>
                )}
              </div>

              <div>
                <span className="summary__note">SLA Status &amp; Due Date</span>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginTop: '4px' }}>
                  <SlaIndicator status={created.slaStatus} />
                </div>
                <span className="summary__note" style={{ display: 'block', marginTop: '4px' }}>
                  Due: <strong>{formatDateTime(created.dueAtUtc)}</strong>
                  {created.triage?.slaReason ? ` (${created.triage.slaReason})` : ''}
                </span>
              </div>

              <div>
                <span className="summary__note">Assignment</span>
                <p style={{ margin: '4px 0 0 0', fontWeight: 600 }}>
                  {created.assignedAgent?.fullName ?? 'Unassigned'}
                </p>
                {created.triage?.assignmentReason && (
                  <span className="summary__note" style={{ display: 'block', marginTop: '4px' }}>
                    {created.triage.assignmentReason}
                  </span>
                )}
              </div>
            </div>
          </div>

          <div className="button-row">
            <button
              type="button"
              className="button button--primary"
              onClick={() => void navigate(`/tickets/${created.id}`)}
            >
              Open {created.reference}
            </button>
            <button type="button" className="button button--ghost" onClick={() => setCreated(undefined)}>
              Raise another
            </button>
          </div>
        </div>
      )}

      {reference.data && !created && (
        <CreateTicketForm
          customers={reference.data.customers}
          categories={reference.data.categories}
          onCreated={setCreated}
        />
      )}
    </section>
  );
}
