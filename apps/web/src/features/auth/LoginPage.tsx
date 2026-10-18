import React, { useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { toErrorMessage } from '../../api/client';
import { useAuth } from './useAuth';

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({});
  const [serverError, setServerError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const validate = (): boolean => {
    const nextErrors: { email?: string; password?: string } = {};

    if (!email.trim()) {
      nextErrors.email = 'Email address is required.';
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
      nextErrors.email = 'Please enter a valid email address.';
    }

    if (!password) {
      nextErrors.password = 'Password is required.';
    }

    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  };

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    setServerError(null);

    if (!validate()) {
      return;
    }

    setIsSubmitting(true);

    try {
      await login({ email: email.trim(), password });

      const from = (location.state as { from?: { pathname: string } })?.from?.pathname || '/tickets';
      navigate(from, { replace: true });
    } catch (err) {
      setServerError(toErrorMessage(err, 'Invalid email or password. Please verify your credentials.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div style={{ maxWidth: '420px', margin: '48px auto' }}>
      <div className="card" style={{ padding: '28px', background: 'var(--colour-surface)', border: '1px solid var(--colour-border)', borderRadius: 'var(--radius)' }}>
        <header style={{ marginBottom: '20px' }}>
          <h1>Agent Login</h1>
          <p className="page-header__subtitle">Sign in to manage support tickets and assignments.</p>
        </header>

        {serverError && (
          <div className="error" role="alert" aria-live="assertive">
            <p>{serverError}</p>
          </div>
        )}

        <form onSubmit={handleSubmit} noValidate>
          <div className="field">
            <label htmlFor="login-email" className="field__label">
              Email address
            </label>
            <input
              id="login-email"
              type="email"
              autoComplete="email"
              value={email}
              disabled={isSubmitting}
              aria-invalid={Boolean(errors.email)}
              aria-describedby={errors.email ? 'login-email-error' : undefined}
              onChange={(e) => setEmail(e.target.value)}
            />
            {errors.email && (
              <p id="login-email-error" className="field__error" role="alert">
                {errors.email}
              </p>
            )}
          </div>

          <div className="field">
            <label htmlFor="login-password" className="field__label">
              Password
            </label>
            <input
              id="login-password"
              type="password"
              autoComplete="current-password"
              value={password}
              disabled={isSubmitting}
              aria-invalid={Boolean(errors.password)}
              aria-describedby={errors.password ? 'login-password-error' : undefined}
              onChange={(e) => setPassword(e.target.value)}
            />
            {errors.password && (
              <p id="login-password-error" className="field__error" role="alert">
                {errors.password}
              </p>
            )}
          </div>

          <div style={{ marginTop: '20px' }}>
            <button
              type="submit"
              className="button button--primary"
              disabled={isSubmitting}
              style={{ width: '100%', padding: '10px' }}
            >
              {isSubmitting ? 'Signing in...' : 'Sign in'}
            </button>
          </div>
        </form>

        <div style={{ marginTop: '24px', paddingTop: '16px', borderTop: '1px solid var(--colour-border)', fontSize: '0.85rem', color: 'var(--colour-muted)' }}>
          <strong>Development Test Account:</strong>
          <div>Email: <code>alex.turner@northwind-support.example</code></div>
          <div>Password: <code>Password123!</code></div>
        </div>
      </div>
    </div>
  );
}
