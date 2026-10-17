import { Badge, type BadgeTone } from '../../../components/Badge';
import type { SlaStatus } from '../../../types/api';

export interface SlaIndicatorProps {
  status: SlaStatus;
  showIcon?: boolean;
}

const slaConfig: Record<SlaStatus, { text: string; tone: BadgeTone; icon: string; ariaLabel: string }> = {
  WithinSla: { text: 'Within SLA', tone: 'info', icon: '✓', ariaLabel: 'SLA Status: Within SLA' },
  AtRisk: { text: 'At risk', tone: 'warning', icon: '⚠', ariaLabel: 'SLA Status: At risk' },
  Breached: { text: 'Breached', tone: 'danger', icon: '✕', ariaLabel: 'SLA Status: Breached' },
  Met: { text: 'Met', tone: 'success', icon: '✓', ariaLabel: 'SLA Status: Met' },
  NotApplicable: { text: 'No SLA', tone: 'neutral', icon: '—', ariaLabel: 'SLA Status: No SLA' },
};

/**
 * Reusable, accessible SLA indicator displaying one of the 5 SLA states
 * using both text and distinct visual tone with accessible labels.
 */
export function SlaIndicator({ status, showIcon = true }: SlaIndicatorProps) {
  const config = slaConfig[status] ?? {
    text: status,
    tone: 'neutral' as BadgeTone,
    icon: '—',
    ariaLabel: `SLA Status: ${status}`,
  };

  return (
    <span role="status" aria-label={config.ariaLabel} data-testid={`sla-indicator-${status}`}>
      <Badge tone={config.tone}>
        {showIcon && <span aria-hidden="true" style={{ marginRight: '4px' }}>{config.icon}</span>}
        {config.text}
      </Badge>
    </span>
  );
}
