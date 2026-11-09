(() => {
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  const menu = document.querySelector('[data-lp-menu]');
  const drawer = document.getElementById('lp-drawer');
  const backdrop = document.getElementById('lp-drawer-backdrop');
  if (menu && drawer) {
    let lastFocus = null;
    const focusables = () =>
      [...drawer.querySelectorAll('a[href], button:not([disabled]), [tabindex]:not([tabindex="-1"])')]
        .filter((el) => !el.hasAttribute('disabled') && el.offsetParent !== null);

    const setOpen = (open) => {
      drawer.hidden = !open;
      if (backdrop) backdrop.hidden = !open;
      menu.setAttribute('aria-expanded', open ? 'true' : 'false');
      menu.setAttribute('aria-label', open ? 'Close menu' : 'Open menu');
      document.body.classList.toggle('lp-nav-open', open);
      if (open) {
        lastFocus = document.activeElement;
        const first = focusables()[0] || drawer;
        requestAnimationFrame(() => first.focus());
      } else if (lastFocus && typeof lastFocus.focus === 'function') {
        lastFocus.focus();
        lastFocus = null;
      }
    };

    menu.addEventListener('click', () => setOpen(drawer.hidden));
    backdrop?.addEventListener('click', () => setOpen(false));
    drawer.querySelectorAll('[data-lp-close]').forEach((a) =>
      a.addEventListener('click', () => setOpen(false)));

    document.addEventListener('keydown', (e) => {
      if (drawer.hidden) return;
      if (e.key === 'Escape') {
        e.preventDefault();
        setOpen(false);
        return;
      }
      if (e.key !== 'Tab') return;
      const list = focusables();
      if (list.length === 0) return;
      const first = list[0];
      const last = list[list.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      } else if (!drawer.contains(document.activeElement) && !menu.contains(document.activeElement)) {
        e.preventDefault();
        first.focus();
      }
    });
  }

  // Highlight the nav link for the section currently in view
  const sectionLinks = [...document.querySelectorAll('[data-lp-nav-link]')];
  if (sectionLinks.length && 'IntersectionObserver' in window) {
    const linkById = new Map();
    sectionLinks.forEach((a) => {
      const id = (a.getAttribute('href') || '').replace(/^#/, '');
      if (id) {
        if (!linkById.has(id)) linkById.set(id, []);
        linkById.get(id).push(a);
      }
    });
    const sections = [...linkById.keys()]
      .map((id) => document.getElementById(id))
      .filter(Boolean);
    const setActive = (id) => {
      sectionLinks.forEach((a) => {
        const on = (a.getAttribute('href') || '') === '#' + id;
        a.classList.toggle('is-active', on);
        if (on) a.setAttribute('aria-current', 'location');
        else a.removeAttribute('aria-current');
      });
    };
    if (sections.length) {
      const pick = () => {
        const marker = Math.max(72, (document.querySelector('.lp-nav')?.getBoundingClientRect().bottom || 64) + 8);
        let best = sections[0].id;
        // Last section whose top has reached / passed the marker (classic scroll-spy)
        sections.forEach((s) => {
          if (s.getBoundingClientRect().top <= marker + 12) best = s.id;
        });
        setActive(best);
      };
      pick();
      window.addEventListener('scroll', pick, { passive: true });
      window.addEventListener('resize', pick);
    }
  }

  const nav = document.querySelector('.lp-nav');
  if (nav) {
    const onScroll = () => nav.classList.toggle('is-scrolled', window.scrollY > 8);
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
  }

  // Header "Get started": outline while hero CTA is on screen; filled after it scrolls away.
  // Without IntersectionObserver / JS, it stays the outline (ghost) style from CSS.
  const heroCta = document.querySelector('[data-lp-hero-cta]');
  const navCtas = [...document.querySelectorAll('[data-lp-nav-cta]')];
  const setNavCtaSolid = (solid) => {
    navCtas.forEach((el) => el.classList.toggle('is-solid', solid));
    document.documentElement.classList.toggle('lp-nav-cta-solid', solid);
  };
  if (heroCta && navCtas.length) {
    if ('IntersectionObserver' in window) {
      const ioNav = new IntersectionObserver((entries) => {
        const e = entries[0];
        setNavCtaSolid(!(e && e.isIntersecting));
      }, { threshold: 0, rootMargin: '-64px 0px 0px 0px' });
      ioNav.observe(heroCta);
    } else {
      setNavCtaSolid(false);
    }
  }

  // Progressive reveal: mark anything already in (or near) the viewport first, THEN enable hiding.
  const revealables = [...document.querySelectorAll('[data-lp-reveal]')];
  const revealAll = () => {
    revealables.forEach((el) => el.classList.add('is-in'));
    document.documentElement.classList.remove('lp-reveal-ready');
  };

  const bootReveal = () => {
    if (reduce || !('IntersectionObserver' in window) || revealables.length === 0) {
      revealAll();
      return;
    }

    const vh = window.innerHeight || document.documentElement.clientHeight;
    revealables.forEach((el) => {
      const r = el.getBoundingClientRect();
      // Already on screen or within ~1 viewport below → show immediately (hero + first sections).
      if (r.top < vh * 1.15 && r.bottom > -40) el.classList.add('is-in');
    });

    document.documentElement.classList.add('lp-reveal-ready');

    const io = new IntersectionObserver((entries) => {
      for (const e of entries) {
        if (e.isIntersecting || e.intersectionRatio > 0) {
          e.target.classList.add('is-in');
          io.unobserve(e.target);
        }
      }
    }, { threshold: [0, 0.01, 0.08], rootMargin: '0px 0px 15% 0px' });

    revealables.forEach((el) => {
      if (!el.classList.contains('is-in')) io.observe(el);
    });

    // Safety net: never leave content faded (slow IO, tall elements, odd viewports).
    window.setTimeout(() => {
      revealables.forEach((el) => el.classList.add('is-in'));
    }, 2500);

    window.addEventListener('beforeprint', revealAll);
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', bootReveal, { once: true });
  } else {
    bootReveal();
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
      const start = () => { if (!timer) timer = window.setInterval(tick, 2200); };
      const stop = () => { window.clearInterval(timer); timer = 0; };
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
        room.style.transform = `rotateX(${10 - y * 9}deg) rotateY(${-14 + x * 12}deg)`;
      };
      stage.addEventListener('pointermove', (e) => onMove(e.clientX, e.clientY));
      stage.addEventListener('pointerleave', () => { room.style.transform = ''; });
    }

    document.querySelectorAll('[data-lp-tilt]').forEach((el) => {
      el.addEventListener('pointermove', (e) => {
        const r = el.getBoundingClientRect();
        const x = (e.clientX - r.left) / r.width - 0.5;
        const y = (e.clientY - r.top) / r.height - 0.5;
        el.style.transform = `perspective(800px) rotateX(${(-y * 5).toFixed(2)}deg) rotateY(${(x * 7).toFixed(2)}deg) translateY(-2px)`;
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
