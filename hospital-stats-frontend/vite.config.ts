import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import legacy from '@vitejs/plugin-legacy';
import path from 'path';

// 企业版前端源码路径（开发时加载企业版组件，生产构建时忽略）
const enterpriseSrc = path.resolve(__dirname, '../../HospitalStats-Enterprise/frontend/src');

export default defineConfig({
  plugins: [
    vue(),
    legacy({
      targets: ['chrome >= 64', 'firefox >= 67', 'safari >= 12', 'edge >= 79'],
      modernPolyfills: true,
      renderLegacyChunks: false,
    }),
  ],
  resolve: {
    alias: {
      '@enterprise': enterpriseSrc,
    },
  },
  server: {
    port: 5173,
    fs: {
      // 允许 Vite 加载企业版前端源码（位于项目目录外）
      allow: [
        path.resolve(__dirname, '.'),
        path.resolve(__dirname, '../../HospitalStats-Enterprise/frontend'),
      ],
    },
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
