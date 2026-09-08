import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    // The API is proxied rather than called cross-origin, so the browser sees
    // one site. That keeps the refresh cookie SameSite=Strict in development
    // as well as production, instead of relaxing it just to make dev work.
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5199',
        changeOrigin: true,
      },
    },
  },
  build: {
    sourcemap: true,
  },
});
