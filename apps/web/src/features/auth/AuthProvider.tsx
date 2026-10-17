import React, { useEffect, useState } from 'react';
import { getStoredToken, onUnauthorized, request, setStoredToken } from '../../api/client';
import type { CurrentUser, LoginResponse } from '../../types/auth';
import { AuthContext, type AuthContextValue } from './auth-context';

const USER_STORAGE_KEY = 'northwind_auth_user';

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [token, setToken] = useState<string | null>(() => getStoredToken());
  const [user, setUser] = useState<CurrentUser | null>(() => {
    try {
      const saved = localStorage.getItem(USER_STORAGE_KEY);
      return saved ? (JSON.parse(saved) as CurrentUser) : null;
    } catch {
      return null;
    }
  });
  const [isLoading, setIsLoading] = useState<boolean>(true);

  const logout = () => {
    setStoredToken(null);
    setToken(null);
    setUser(null);
    try {
      localStorage.removeItem(USER_STORAGE_KEY);
    } catch {
      // Ignore
    }
  };

  useEffect(() => {
    onUnauthorized(logout);
    return () => onUnauthorized(null);
  }, []);

  useEffect(() => {
    let active = true;

    async function checkAuth() {
      const currentToken = getStoredToken();
      if (!currentToken) {
        if (active) {
          setUser(null);
          setIsLoading(false);
        }
        return;
      }

      try {
        const me = await request<CurrentUser>('/auth/me');
        if (active) {
          setUser(me);
          localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(me));
        }
      } catch {
        if (active) {
          logout();
        }
      } finally {
        if (active) {
          setIsLoading(false);
        }
      }
    }

    checkAuth();

    return () => {
      active = false;
    };
  }, []);

  const login = async (credentials: { email: string; password: string }) => {
    const response = await request<LoginResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify(credentials),
    });

    setStoredToken(response.token);
    setToken(response.token);
    setUser(response.user);
    try {
      localStorage.setItem(USER_STORAGE_KEY, JSON.stringify(response.user));
    } catch {
      // Ignore
    }
  };

  const value: AuthContextValue = {
    user,
    token,
    isAuthenticated: Boolean(token && user),
    isLoading,
    login,
    logout,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
