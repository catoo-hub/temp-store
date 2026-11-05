window.tempUi = {
  footerReveal(element) {
    // The layout survives route changes; keep only one set of listeners.
    window.tempUi.disposeFooterReveal?.();
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let frame = 0;
    const update = () => {
      frame = 0;
      if (!element.isConnected) { dispose(); return; }
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
    const schedule = () => { if (!frame) frame = requestAnimationFrame(update); };
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
      element.addEventListener('click', event => {
        const r = element.getBoundingClientRect();
        if (event.target === element && (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom)) element.close();
      });
    }
  },
  tabs(element) {
    if (element.dataset.initialized) return;
    element.dataset.initialized = 'true';
    element.addEventListener('keydown', event => {
      const buttons = [...element.querySelectorAll('[role="tab"]')];
      let index = buttons.indexOf(event.target);
      if (index < 0 || !['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
      event.preventDefault();
      if (event.key === 'Home') index = 0;
      else if (event.key === 'End') index = buttons.length - 1;
      else index = (index + (event.key === 'ArrowRight' ? 1 : -1) + buttons.length) % buttons.length;
      buttons[index].focus();
      buttons[index].click();
    });
  },
  theme(value) {
    document.documentElement.classList.toggle('dark', value);
    document.documentElement.style.colorScheme = value ? 'dark' : 'light';
    try { localStorage.setItem('temp-store.theme', value ? 'dark' : 'light'); } catch {}
  },
  isDark() { return document.documentElement.classList.contains('dark'); }
};
try { window.tempUi.theme(localStorage.getItem('temp-store.theme') === 'dark'); } catch {}
