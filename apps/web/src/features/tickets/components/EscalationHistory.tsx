import { formatDateTime } from '../../../lib/format';
import type { TicketEscalation } from '../../../types/api';
import { PriorityBadge } from './TicketBadges';

interface EscalationHistoryProps {
  escalations: TicketEscalation[];
  isLoading?: boolean;
}

export function EscalationHistory({ escalations, isLoading }: EscalationHistoryProps) {
  if (isLoading && escalations.length === 0) {
    return <p className="summary__note">Loading escalation history...</p>;
  }

  if (escalations.length === 0) {
    return <p className="summary__note">No escalations recorded for this ticket.</p>;
  }

  return (
    <div className="card" style={{ marginTop: 'var(--gap)' }}>
      <h2>Escalation History</h2>
      <ul style={{ listStyle: 'none', padding: 0, margin: 0, display: 'flex', flexDirection: 'column', gap: '12px' }}>
        {escalations.map((item) => (
          <li
            key={item.id}
            style={{
              padding: '12px',
              border: '1px solid var(--colour-border)',
              borderRadius: 'var(--radius)',
              background: 'var(--colour-page)',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '8px' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                <PriorityBadge priority={item.fromPriority} />
                <span>→</span>
                <PriorityBadge priority={item.toPriority} />
              </div>
              <span style={{ fontSize: '0.85rem', color: 'var(--colour-muted)' }}>
                {formatDateTime(item.escalatedAtUtc)}
              </span>
            </div>

            <p style={{ margin: '4px 0', fontWeight: 500 }}>
              &ldquo;{item.reason}&rdquo;
            </p>

            <div style={{ fontSize: '0.85rem', color: 'var(--colour-muted)', display: 'flex', gap: '16px', flexWrap: 'wrap' }}>
              <span>Escalated by: <strong>{item.escalatedBy}</strong></span>
              {item.toDueAtUtc && (
                <span>New due date: <strong>{formatDateTime(item.toDueAtUtc)}</strong></span>
              )}
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
