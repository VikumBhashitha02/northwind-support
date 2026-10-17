import { App } from '../../App';
import { AuthProvider } from './AuthProvider';

export function RootLayout() {
  return (
    <AuthProvider>
      <App />
    </AuthProvider>
  );
}
