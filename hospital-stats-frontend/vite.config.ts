import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import legacy from '@vitejs/plugin-legacy';

export default defineConfig({
  plugins: [
    vue(),
    legacy({
      targets: ['chrome >= 64', 'firefox >= 67', 'safari >= 12', 'edge >= 79'],
      modernPolyfills: true,
      renderLegacyChunks: false,
    }),
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: '../HospitalStats.Backend/HospitalStats.Api/wwwroot',
    emptyOutDir: true,
    target: 'es2015',
  },
});
