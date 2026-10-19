const CACHE_NAME = "raya-trainer-v1";
const STATIC_ASSETS = [
  "/",
  "/index.html",
  "/app.js",
  "/styles.css",
  "/manifest.json",
  "/vendor/vue.global.prod.js",
  "/icons/icon-192.png",
  "/icons/icon-512.png"
];

// 安装：预缓存全部已知静态资源
self.addEventListener("install", event => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(STATIC_ASSETS))
      .then(() => self.skipWaiting())
  );
});

// 激活：清理旧缓存
self.addEventListener("activate", event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(
        keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key))
      ))
      .then(() => self.clients.claim())
  );
});

// fetch：静态资源 cache-first，API 请求不拦截（交给前端处理）
self.addEventListener("fetch", event => {
  const url = new URL(event.request.url);

  // API 请求：不拦截，前端自行处理离线降级
  if (url.pathname.startsWith("/api/")) {
    return;
  }

  // 静态资源：cache-first，后台更新
  event.respondWith(
    caches.match(event.request).then(cached => {
      const fetchPromise = fetch(event.request).then(response => {
        if (response && response.status === 200) {
          const clone = response.clone();
          caches.open(CACHE_NAME).then(cache => cache.put(event.request, clone));
        }
        return response;
      }).catch(() => cached);

      return cached || fetchPromise;
    })
  );
});
