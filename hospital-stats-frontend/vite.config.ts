import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import legacy from '@vitejs/plugin-legacy';
import path from 'path';
import fs from 'fs';

// 企业版前端源码路径（存在则加载企业版组件，否则用空 stub）
const enterpriseSrc = path.resolve(__dirname, '../../HospitalStats-Enterprise/frontend/src');
const enterpriseFallback = path.resolve(__dirname, 'src/plugins/enterpriseStub.ts');
const hasEnterprise = fs.existsSync(enterpriseSrc);

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
      '@enterprise': hasEnterprise ? enterpriseSrc : enterpriseFallback,
    },
  },
  server: {
    port: 5173,
    fs: {
      allow: [
        path.resolve(__dirname, '.'),
        ...(hasEnterprise ? [path.resolve(__dirname, '../../HospitalStats-Enterprise/frontend')] : []),
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
