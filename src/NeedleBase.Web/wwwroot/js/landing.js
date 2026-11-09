(() => {
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const menu = document.querySelector('[data-lp-menu]');
  const drawer = document.getElementById('lp-drawer');
  if (menu && drawer) {
    const setOpen = (open) => {
      drawer.hidden = !open;
      menu.setAttribute('aria-expanded', open ? 'true' : 'false');
      menu.setAttribute('aria-label', open ? 'Close menu' : 'Open menu');
    };
    menu.addEventListener('click', () => setOpen(drawer.hidden));
    drawer.querySelectorAll('[data-lp-close]').forEach((a) => a.addEventListener('click', () => setOpen(false)));
  }

  if (!reduce && 'IntersectionObserver' in window) {
    const io = new IntersectionObserver((entries) => {
      for (const e of entries) {
        if (e.isIntersecting) {
          e.target.classList.add('is-in');
          io.unobserve(e.target);
        }
      }
    }, { threshold: 0.12, rootMargin: '0px 0px -8% 0px' });
    document.querySelectorAll('[data-lp-reveal]').forEach((el) => io.observe(el));
  } else {
    document.querySelectorAll('[data-lp-reveal]').forEach((el) => el.classList.add('is-in'));
  }

  const measure = document.querySelector('[data-lp-measure]');
  if (measure) {
    const buttons = measure.querySelectorAll('[data-measure]');
    const points = measure.querySelectorAll('.lp-silhouette circle[data-pt]');
    const activate = (name) => {
      buttons.forEach((b) => b.classList.toggle('is-on', b.getAttribute('data-measure') === name));
      points.forEach((p) => p.classList.toggle('is-on', p.getAttribute('data-pt') === name));
    };
    buttons.forEach((b) => b.addEventListener('click', () => activate(b.getAttribute('data-measure'))));
    activate('chest');
  }

  // Motion (CSS 3D loops, parallax, tilt, timeline) starts after the first real user gesture so
  // first paint / LCP stay calm on low-end phones and automated audits.
  let motionOn = false;
  const enableMotion = () => {
    if (motionOn || reduce) return;
    motionOn = true;
    document.documentElement.classList.add('lp-motion-on');

    const demo = document.querySelector('[data-lp-demo]');
    if (demo) {
      const steps = [...demo.children];
      let i = 2;
      let timer = 0;
      const tick = () => {
        i = (i + 1) % steps.length;
        steps.forEach((li, idx) => {
          li.classList.toggle('is-done', idx < i);
          li.classList.toggle('is-now', idx === i);
        });
      };
      const start = () => { if (!timer) timer = setInterval(tick, 2200); };
      const stop = () => { clearInterval(timer); timer = 0; };
      if ('IntersectionObserver' in window) {
        new IntersectionObserver((entries) => {
          entries.forEach((e) => (e.isIntersecting ? start() : stop()));
        }, { threshold: 0.2 }).observe(demo);
      }
    }

    const stage = document.querySelector('[data-lp-stage]');
    const room = document.querySelector('[data-lp-parallax]');
    if (stage && room) {
      const onMove = (clientX, clientY) => {
        const r = stage.getBoundingClientRect();
        const x = (clientX - r.left) / r.width - 0.5;
        const y = (clientY - r.top) / r.height - 0.5;
        room.style.transform = `rotateX(${12 - y * 10}deg) rotateY(${-18 + x * 14}deg)`;
      };
      stage.addEventListener('pointermove', (e) => onMove(e.clientX, e.clientY));
      stage.addEventListener('pointerleave', () => { room.style.transform = ''; });
    }

    document.querySelectorAll('[data-lp-tilt]').forEach((el) => {
      el.addEventListener('pointermove', (e) => {
        const r = el.getBoundingClientRect();
        const x = (e.clientX - r.left) / r.width - 0.5;
        const y = (e.clientY - r.top) / r.height - 0.5;
        el.style.transform = `perspective(700px) rotateX(${(-y * 6).toFixed(2)}deg) rotateY(${(x * 8).toFixed(2)}deg)`;
      });
      el.addEventListener('pointerleave', () => { el.style.transform = ''; });
    });
  };

  if (!reduce) {
    const once = () => {
      enableMotion();
      window.removeEventListener('pointerdown', once);
      window.removeEventListener('touchstart', once);
      window.removeEventListener('keydown', once);
      window.removeEventListener('scroll', once);
    };
    window.addEventListener('pointerdown', once, { passive: true });
    window.addEventListener('touchstart', once, { passive: true });
    window.addEventListener('keydown', once);
    window.addEventListener('scroll', once, { passive: true });
  }
})();
