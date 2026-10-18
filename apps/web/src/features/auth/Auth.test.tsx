import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getStoredToken, request, setStoredToken } from '../../api/client';
import { AuthProvider } from './AuthProvider';
import { LoginPage } from './LoginPage';
import { ProtectedRoute } from './ProtectedRoute';

describe('Authentication & Protected Routes', () => {
  beforeEach(() => {
    localStorage.clear();
    setStoredToken(null);
    vi.restoreAllMocks();
  });

  afterEach(() => {
    localStorage.clear();
    setStoredToken(null);
  });

  it('attaches Authorization Bearer header when token is stored', async () => {
    setStoredToken('test.jwt.token');

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ id: 1, fullName: 'Alex Morgan' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    await request('/auth/me');

    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining('/auth/me'),
      expect.objectContaining({
        headers: expect.objectContaining({
          Authorization: 'Bearer test.jwt.token',
        }),
      }),
    );
  });

  it('redirects unauthenticated users to /login when visiting a protected route', async () => {
    render(
      <MemoryRouter initialEntries={['/tickets']}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<div>Login Page Mock</div>} />
            <Route element={<ProtectedRoute />}>
              <Route path="/tickets" element={<div>Protected Tickets</div>} />
            </Route>
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('Login Page Mock')).toBeInTheDocument();
    });
    expect(screen.queryByText('Protected Tickets')).not.toBeInTheDocument();
  });

  it('shows client-side validation errors when submitting empty fields in LoginPage', async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    render(
      <MemoryRouter initialEntries={['/login']}>
        <AuthProvider>
          <LoginPage />
        </AuthProvider>
      </MemoryRouter>,
    );

    const submitBtn = screen.getByRole('button', { name: /sign in/i });
    fireEvent.click(submitBtn);

    expect(screen.getByText(/email address is required/i)).toBeInTheDocument();
    expect(screen.getByText(/password is required/i)).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('shows server-side rejection error when credentials are rejected', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      statusText: 'Unauthorized',
      json: async () => ({ detail: 'Invalid email or password.' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    render(
      <MemoryRouter initialEntries={['/login']}>
        <AuthProvider>
          <LoginPage />
        </AuthProvider>
      </MemoryRouter>,
    );

    await user.type(screen.getByLabelText(/email address/i), 'alex.turner@northwind-support.example');
    await user.type(screen.getByLabelText(/password/i), 'WrongPass');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/invalid email or password/i);
    });
  });

  it('stores token and user upon successful login', async () => {
    const user = userEvent.setup();
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({
        token: 'new.jwt.token',
        expiresAtUtc: '2026-10-18T12:00:00Z',
        user: { id: 1, fullName: 'Alex Turner', email: 'alex.turner@northwind-support.example' },
      }),
    });
    vi.stubGlobal('fetch', fetchMock);

    render(
      <MemoryRouter initialEntries={['/login']}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route path="/tickets" element={<div>Tickets Page Destination</div>} />
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    );

    await user.type(screen.getByLabelText(/email address/i), 'alex.turner@northwind-support.example');
    await user.type(screen.getByLabelText(/password/i), 'Password123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => {
      expect(screen.getByText('Tickets Page Destination')).toBeInTheDocument();
    });

    expect(getStoredToken()).toBe('new.jwt.token');
  });
});
