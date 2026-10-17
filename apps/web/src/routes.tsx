import { createBrowserRouter, Navigate } from 'react-router-dom';
import { RootLayout } from './features/auth/RootLayout';
import { LoginPage } from './features/auth/LoginPage';
import { ProtectedRoute } from './features/auth/ProtectedRoute';
import { NotFoundPage } from './features/NotFoundPage';
import { CustomerDetailPage } from './features/customers/CustomerDetailPage';
import { CustomerListPage } from './features/customers/CustomerListPage';
import { CreateTicketPage } from './features/tickets/CreateTicketPage';
import { TicketDetailPage } from './features/tickets/TicketDetailPage';
import { TicketListPage } from './features/tickets/TicketListPage';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <RootLayout />,
    children: [
      { path: 'login', element: <LoginPage /> },
      {
        element: <ProtectedRoute />,
        children: [
          { index: true, element: <Navigate to="/tickets" replace /> },
          { path: 'tickets', element: <TicketListPage /> },
          { path: 'tickets/new', element: <CreateTicketPage /> },
          { path: 'tickets/:id', element: <TicketDetailPage /> },
          { path: 'customers', element: <CustomerListPage /> },
          { path: 'customers/:id', element: <CustomerDetailPage /> },
        ],
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]);
