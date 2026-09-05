import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import App from './App';
import { AuthProvider } from './state/auth';
import { CartProvider } from './state/cart';
import { ApiError } from './lib/api';
import './styles/app.css';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Retrying a 404 or a 400 just delays the error the customer needs to
      // see. Only transient failures are worth a second attempt.
      retry: (attempt, error) => {
        if (error instanceof ApiError && error.status >= 400 && error.status < 500) return false;
        return attempt < 2;
      },
      refetchOnWindowFocus: false,
    },
    mutations: {
      // Never automatically. Retrying "place order" could place two.
      retry: false,
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {/*  AuthProvider is inside the router because signing out navigates,
            and outside CartProvider because a customer's cart survives a staff
            sign-out — they are unrelated sessions that happen to share a tab. */}
        <AuthProvider>
          <CartProvider>
            <App />
          </CartProvider>
        </AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
