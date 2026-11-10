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
      if (open) {
        drawer.hidden = false;
        if (backdrop) backdrop.hidden = false;
        requestAnimationFrame(() => {
          drawer.classList.add('is-open');
          backdrop?.classList.add('is-open');
        });
      } else {
        drawer.classList.remove('is-open');
        backdrop?.classList.remove('is-open');
        const hide = () => {
          drawer.hidden = true;
          if (backdrop) backdrop.hidden = true;
          drawer.removeEventListener('transitionend', hide);
        };
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) hide();
        else drawer.addEventListener('transitionend', hide, { once: true });
        setTimeout(hide, 320);
      }
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
        let best = '';
        // Last section whose top has reached / passed the marker (classic scroll-spy).
        // While the hero is still in view and no section has reached the marker, clear current.
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

  let motionOn = false;
  const enableMotion = () => {
    if (motionOn || reduce) return;
    motionOn = true;
    document.documentElement.classList.add('lp-motion-on');

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

  // Gentle hero parallax (off under reduced motion / low-power via `reduce`).
  const heroMedia = document.querySelector('[data-lp-parallax-y]');
  if (heroMedia && !reduce && !window.matchMedia('(update: slow)').matches) {
    let ticking = false;
    const apply = () => {
      ticking = false;
      const y = window.scrollY;
      if (y > 1200) return;
      heroMedia.style.transform = 'translate3d(0, ' + (y * 0.18).toFixed(1) + 'px, 0) scale(1.06)';
    };
    window.addEventListener('scroll', () => {
      if (!ticking) {
        ticking = true;
        requestAnimationFrame(apply);
      }
    }, { passive: true });
  }


  // Benefits card deck: native scrollbar / keyboard / touch only.
  // Cards stay fully opaque; depth is transform + shadow + a dim overlay.
  // Desktop deals vertically; viewports <=768px swipe the next card in from the left.
  // No scroll hijacking, no aria-live.
  const deckRoot = document.querySelector('[data-lp-deck]');
  if (deckRoot) {
    const deckCards = [...deckRoot.querySelectorAll('[data-lp-deck-card]')];
    const deckPos = deckRoot.querySelector('[data-lp-deck-pos]');
    const deckN = deckCards.length;
    const deckReduce = window.matchMedia('(prefers-reduced-motion: reduce)');
    const deckSwipe = window.matchMedia('(max-width: 768px)');
    const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
    const ease = (t) => t * t * (3 - 2 * t);
    let deckTicking = false;
    let deckBound = false;
    const renderDeck = () => {
      deckTicking = false;
      if (deckReduce.matches || !deckRoot.classList.contains('is-js')) return;
      const rect = deckRoot.getBoundingClientRect();
      const total = Math.max(1, deckRoot.offsetHeight - window.innerHeight);
      const scrolled = clamp(-rect.top, 0, total);
      const p = scrolled / total;
      const scaled = p * (deckN - 1);
      const swipe = deckSwipe.matches;
      deckCards.forEach((card, i) => {
        const local = i === 0 ? 1 : clamp(scaled - (i - 1), 0, 1);
        const e = ease(local);
        const stack = Math.min(Math.max(0, scaled - i), 3);
        const nextE = ease(i < deckN - 1 ? clamp(scaled - i, 0, 1) : 0);
        if (e < 1) {
          if (swipe) {
            const tx = (1 - e) * -112;
            const rot = (1 - e) * -7;
            card.style.setProperty('--card-t', 'translateX(' + tx + '%) rotate(' + rot + 'deg)');
          } else {
            const ty = (1 - e) * 108;
            const rot = (1 - e) * 6;
            const sc = 0.97 + 0.03 * e;
            card.style.setProperty('--card-t', 'translateY(' + ty + '%) rotate(' + rot + 'deg) scale(' + sc + ')');
          }
          card.style.setProperty('--card-dim', '0');
          card.classList.toggle('is-top', e > 0.55);
        } else if (swipe) {
          const peekX = stack * 16 + nextE * 22;
          const peekRot = stack * 2.4 + nextE * 4;
          const sc = 1 - stack * 0.055 - nextE * 0.03;
          card.style.setProperty('--card-t', 'translateX(' + peekX + 'px) rotate(' + peekRot + 'deg) scale(' + sc + ')');
          card.style.setProperty('--card-dim', String(Math.min(0.5, stack * 0.2 + nextE * 0.22)));
          card.classList.toggle('is-top', stack < 0.2 && nextE < 0.45);
        } else {
          const stackY = -stack * 34;
          const stackRot = -stack * 2.8;
          const stackScale = 1 - stack * 0.065;
          card.style.setProperty('--card-t', 'translateY(' + stackY + 'px) rotate(' + stackRot + 'deg) scale(' + stackScale + ')');
          card.style.setProperty('--card-dim', String(Math.min(0.48, stack * 0.24)));
          card.classList.toggle('is-top', stack < 0.2);
        }
        card.style.setProperty('--card-z', String(20 + i));
      });
      const current = clamp(Math.floor(scaled + 1.001), 1, deckN);
      if (deckPos) deckPos.textContent = String(current);
      deckRoot.dataset.step = String(current);
    };
    const onDeckScroll = () => {
      if (!deckTicking) {
        deckTicking = true;
        requestAnimationFrame(renderDeck);
      }
    };
    const armDeck = () => {
      if (deckReduce.matches || deckN === 0) {
        deckRoot.classList.add('is-static');
        deckRoot.classList.remove('is-js');
        deckCards.forEach((card) => {
          card.style.removeProperty('--card-t');
          card.style.removeProperty('--card-z');
          card.style.removeProperty('--card-dim');
          card.classList.remove('is-top');
        });
        return;
      }
      deckRoot.classList.remove('is-static');
      deckRoot.classList.add('is-js');
      if (!deckBound) {
        window.addEventListener('scroll', onDeckScroll, { passive: true });
        window.addEventListener('resize', onDeckScroll, { passive: true });
        deckBound = true;
      }
      renderDeck();
    };
    armDeck();
    if (typeof deckReduce.addEventListener === 'function') {
      deckReduce.addEventListener('change', armDeck);
    }
    if (typeof deckSwipe.addEventListener === 'function') {
      deckSwipe.addEventListener('change', onDeckScroll);
    }
  }


})();
