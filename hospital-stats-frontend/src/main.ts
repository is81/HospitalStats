import { createApp } from 'vue';
import ElementPlus from 'element-plus';
import 'element-plus/dist/index.css';
import './styles/theme.css';
import * as ElementPlusIconsVue from '@element-plus/icons-vue';
import { createPinia } from 'pinia';
import router from './router';
import App from './App.vue';

const app = createApp(App);

// register all element-plus icons
for (const [key, component] of Object.entries(ElementPlusIconsVue)) {
  app.component(key, component);
}

const pinia = createPinia();
app.use(ElementPlus, { locale: undefined });
app.use(pinia);
app.use(router);

// 尝试加载企业版插件（开发环境，企业版存在时自动注册）
async function loadEnterprisePlugins() {
  try {
    const { registerEnterprisePlugins } = await import('@enterprise/plugins/index');
    registerEnterprisePlugins(router, pinia);
  } catch {
    // 企业版前端不存在时静默跳过（社区版独立运行时）
  }
}

loadEnterprisePlugins().then(() => {
  app.mount('#app');
});
