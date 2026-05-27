<script setup lang="ts">
import { useRouter, useRoute } from 'vue-router';
import { useAuthStore } from './stores/auth';
import { watch } from 'vue';

const router = useRouter();
const route = useRoute();
const authStore = useAuthStore();

watch(
  () => route.path,
  (path) => {
    if (!authStore.isLoggedIn && !route.meta.noAuth) {
      router.push('/login');
    } else if (authStore.isLoggedIn && path === '/login') {
      router.push('/');
    }
  },
  { immediate: true }
);
</script>

<template>
  <router-view />
</template>
