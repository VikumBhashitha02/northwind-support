import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { SlaStatus } from '../../../types/api';
import { SlaIndicator } from './SlaIndicator';

describe('SlaIndicator', () => {
  const allStates: { status: SlaStatus; expectedText: string }[] = [
    { status: 'WithinSla', expectedText: 'Within SLA' },
    { status: 'AtRisk', expectedText: 'At risk' },
    { status: 'Breached', expectedText: 'Breached' },
    { status: 'Met', expectedText: 'Met' },
    { status: 'NotApplicable', expectedText: 'No SLA' },
  ];

  it.each(allStates)(
    'renders state "$status" distinctly with accessible text "$expectedText"',
    ({ status, expectedText }) => {
      const { unmount } = render(<SlaIndicator status={status} />);

      const indicator = screen.getByTestId(`sla-indicator-${status}`);
      expect(indicator).toBeInTheDocument();
      expect(indicator).toHaveAttribute('role', 'status');
      expect(indicator).toHaveAttribute('aria-label', `SLA Status: ${expectedText}`);
      expect(screen.getByText(expectedText)).toBeInTheDocument();

      unmount();
    },
  );

  it('does not communicate status through colour alone', () => {
    render(<SlaIndicator status="AtRisk" />);
    // Check that text content explicitly communicates the status
    expect(screen.getByText('At risk')).toBeInTheDocument();
    // Check accessible role
    expect(screen.getByRole('status')).toHaveAccessibleName('SLA Status: At risk');
  });
});
