let adminToken = '';
let accountToken = '';

async function sendRequest(url, method, body, token) {
  try {
    const response = await fetch(url, {
      method,
      headers: {
        'Content-Type': 'application/json',
        RequestVerificationToken: token,
      },
      body: body == null ? undefined : JSON.stringify(body),
      signal: AbortSignal.timeout(15000),
    });
    if (response.ok) return { success: true };
    const error = await response.json().catch(() => ({}));
    const message =
      response.status === 429
        ? 'Слишком много попыток. Подождите минуту.'
        : 'Не удалось выполнить действие. Обновите страницу и попробуйте ещё раз.';
    return {
      success: false,
      message: typeof error.message === 'string' ? error.message : message,
    };
  } catch {
    return { success: false, message: 'Нет связи с сервером. Попробуйте ещё раз.' };
  }
}

window.tempUi = {
  async getCatalog() {
    const response = await fetch(new URL('api/catalog', document.baseURI), {
      headers: { Accept: 'application/json' },
      signal: AbortSignal.timeout(15000),
    });
    if (!response.ok) throw new Error('Не удалось загрузить товары. Попробуйте ещё раз.');
    return response.json();
  },
  async adminSession() {
    const response = await fetch('/api/admin/session', {
      cache: 'no-store',
      signal: AbortSignal.timeout(15000),
    });
    if (!response.ok) throw new Error('Не удалось проверить вход. Обновите страницу.');
    const session = await response.json();
    adminToken = session.token;
    return session;
  },
  adminRequest(path, method, body) {
    return sendRequest(`/api/admin${path}`, method, body, adminToken);
  },
  async accountSession() {
    const response = await fetch('/api/account/session', {
      cache: 'no-store',
      signal: AbortSignal.timeout(15000),
    });
    if (!response.ok) throw new Error('Не удалось проверить вход. Обновите страницу.');
    const session = await response.json();
    accountToken = session.token;
    return session;
  },
  accountRequest(path, method, body) {
    return sendRequest(`/api/account${path}`, method, body, accountToken);
  },
  restoreAnchor() {
    try {
      const id = decodeURIComponent(location.hash.slice(1));
      if (id) document.getElementById(id)?.scrollIntoView();
    } catch {
      /* Ignore malformed URL fragments. */
    }
  },
  footerReveal(element) {
    // The layout survives route changes; keep only one set of listeners.
    window.tempUi.disposeFooterReveal?.();
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let frame = 0;
    const update = () => {
      frame = 0;
      if (!element.isConnected) {
        dispose();
        return;
      }
      if (motion.matches) {
        element.removeAttribute('data-scroll-reveal');
        return;
      }
      const rect = element.getBoundingClientRect();
      const viewport = document.documentElement.clientHeight;
      const visible = Math.max(0, Math.min(rect.height, viewport - rect.top));
      const distance = Math.min(viewport * 0.8, rect.height * 0.85);
      const progress = Math.max(0, Math.min(1, visible / Math.max(1, distance)));
      const feather = Math.min(160, viewport * 0.18);
      // Move the fade boundary downward; fully reveal the whole footer at completion.
      const edge = progress === 1 ? rect.height + feather : progress * (visible + feather);
      element.style.setProperty('--footer-edge', `${edge.toFixed(2)}px`);
      element.style.setProperty('--footer-feather', `${feather.toFixed(2)}px`);
      element.style.setProperty('--footer-opacity', progress.toFixed(4));
      element.setAttribute('data-scroll-reveal', '');
    };
    const schedule = () => {
      if (!frame) frame = requestAnimationFrame(update);
    };
    const resize = new ResizeObserver(schedule);
    const dispose = () => {
      cancelAnimationFrame(frame);
      window.removeEventListener('scroll', schedule);
      window.removeEventListener('resize', schedule);
      motion.removeEventListener('change', schedule);
      resize.disconnect();
      element.removeAttribute('data-scroll-reveal');
    };
    window.addEventListener('scroll', schedule, { passive: true });
    window.addEventListener('resize', schedule);
    motion.addEventListener('change', schedule);
    resize.observe(document.body);
    resize.observe(element);
    window.tempUi.disposeFooterReveal = dispose;
    update();
  },
  dialog(element, open) {
    if (open && !element.open) element.showModal();
    if (!open && element.open) element.close();
    if (!element.dataset.initialized) {
      element.dataset.initialized = 'true';
      element.addEventListener('click', (event) => {
        const r = element.getBoundingClientRect();
        if (
          event.target === element &&
          (event.clientX < r.left ||
            event.clientX > r.right ||
            event.clientY < r.top ||
            event.clientY > r.bottom)
        )
          element.close();
      });
    }
  },
  theme(value) {
    document.documentElement.classList.toggle('dark', value);
    document.documentElement.style.colorScheme = value ? 'dark' : 'light';
    try {
      localStorage.setItem('temp-store.theme', value ? 'dark' : 'light');
    } catch {}
  },
  isDark() {
    return document.documentElement.classList.contains('dark');
  },
};
try {
  window.tempUi.theme(localStorage.getItem('temp-store.theme') === 'dark');
} catch {}
