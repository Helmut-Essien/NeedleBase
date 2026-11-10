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


  // Benefits card deck.
  // Desktop (>768px): vertical scroll-driven deal (unchanged).
  // Mobile: stacked (Tinder-style) deck — pointer swipes fly the top card off
  // or bring the previous card back on top. No nested scroller, no page hijack.
  const deckRoot = document.querySelector('[data-lp-deck]');
  if (deckRoot) {
    const stage = deckRoot.querySelector('[data-lp-deck-stage]') || deckRoot.querySelector('.lp-deck__stage');
    const cards = [...deckRoot.querySelectorAll('[data-lp-deck-card]')];
    const posEl = deckRoot.querySelector('[data-lp-deck-pos]');
    const dots = [...deckRoot.querySelectorAll('[data-lp-deck-dot]')];
    const hint = deckRoot.querySelector('[data-lp-deck-hint]');
    const prevBtn = deckRoot.querySelector('[data-lp-deck-prev]');
    const nextBtn = deckRoot.querySelector('[data-lp-deck-next]');
    const n = cards.length;
    const mqReduce = window.matchMedia('(prefers-reduced-motion: reduce)');
    const mqStack = window.matchMedia('(max-width: 768px)');

    let mode = null;
    let ticking = false;
    let hintGone = false;
    let current = 0;
    let pinnedBound = false;
    let stackBound = false;
    let busy = false;
    let drag = null;
    let nudgeIo = null;
    let nudged = false;

    const STACK_Y = [0, 14, 26, 36];
    const STACK_S = [1, 0.95, 0.90, 0.86];
    const STACK_R = [0, 3.2, -2.6, 1.8];
    const STACK_X = [0, 10, -8, 6];
    const STACK_DIM = [0, 0.14, 0.24, 0.32];
    const OFF_T = 'translateX(-130%) rotate(-20deg)';
    const TRANS_FLY = 420;
    const TRANS_SNAP = 520;
    const AXIS_LOCK = 8;
    const THRESH = 0.25;
    const FLICK = 0.55;

    const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
    const ease = (t) => t * t * (3 - 2 * t);
    const prefersReduce = () => mqReduce.matches;
    const isStack = () => mqStack.matches;
    const motion = () => (prefersReduce() ? 'auto' : 'smooth');
    const wait = (ms) => new Promise((r) => setTimeout(r, ms));

    const clearCardVars = () => {
      cards.forEach((card) => {
        card.style.removeProperty('--card-t');
        card.style.removeProperty('--card-z');
        card.style.removeProperty('--card-dim');
        card.classList.remove('is-top');
      });
    };

    const setCurrent = (i) => {
      current = clamp(i, 0, Math.max(0, n - 1));
      if (posEl) posEl.textContent = String(current + 1);
      deckRoot.dataset.step = String(current + 1);
      dots.forEach((d, di) => {
        if (di === current) d.setAttribute('aria-current', 'true');
        else d.removeAttribute('aria-current');
      });
      if (prevBtn) prevBtn.disabled = current <= 0;
      if (nextBtn) nextBtn.disabled = current >= n - 1;
    };

    const dismissHint = () => {
      if (hintGone) return;
      hintGone = true;
      if (hint) hint.classList.add('is-gone');
    };

    const restTransform = (depth) => {
      const d = Math.min(Math.max(0, depth), 3);
      return 'translate(' + STACK_X[d] + 'px, ' + STACK_Y[d] + 'px) rotate(' + STACK_R[d] + 'deg) scale(' + STACK_S[d] + ')';
    };

    const applyLayout = (skip) => {
      cards.forEach((card, i) => {
        if (skip === i) return;
        if (i < current) {
          card.style.setProperty('--card-t', OFF_T);
          card.style.setProperty('--card-z', '1');
          card.style.setProperty('--card-dim', '0');
          card.classList.remove('is-top');
        } else {
          const depth = i - current;
          card.style.setProperty('--card-t', restTransform(depth));
          card.style.setProperty('--card-z', String(40 - depth));
          card.style.setProperty('--card-dim', String(STACK_DIM[Math.min(depth, 3)]));
          card.classList.toggle('is-top', depth === 0);
        }
      });
    };

    const enableTransitions = () => {
      deckRoot.classList.remove('is-dragging');
      void stage.offsetWidth;
    };

    const animateTo = (target) => {
      target = clamp(target, 0, n - 1);
      if (target === current) {
        enableTransitions();
        applyLayout();
        return Promise.resolve();
      }
      dismissHint();
      if (prefersReduce()) {
        setCurrent(target);
        applyLayout();
        return Promise.resolve();
      }
      busy = true;
      const from = current;
      enableTransitions();
      if (target > from) {
        const top = cards[from];
        top.style.setProperty('--card-z', '50');
        top.style.setProperty('--card-t', 'translateX(-130%) rotate(-22deg)');
        setCurrent(target);
        applyLayout(from);
        return wait(TRANS_FLY).then(() => {
          applyLayout();
          busy = false;
        });
      }
      const incoming = cards[target];
      incoming.style.setProperty('--card-z', '50');
      incoming.style.setProperty('--card-dim', '0');
      setCurrent(target);
      applyLayout();
      return wait(TRANS_FLY).then(() => {
        applyLayout();
        busy = false;
      });
    };

    const bounce = (dir) => {
      enableTransitions();
      if (prefersReduce()) {
        applyLayout();
        return;
      }
      const card = cards[current];
      if (!card) return;
      busy = true;
      const dx = dir < 0 ? -32 : 32;
      const rot = dir < 0 ? -7 : 7;
      card.style.setProperty('--card-t', 'translateX(' + dx + 'px) rotate(' + rot + 'deg)');
      wait(140).then(() => {
        applyLayout();
        return wait(TRANS_SNAP);
      }).then(() => { busy = false; });
    };

    const goPinned = (i) => {
      i = clamp(i, 0, n - 1);
      const total = Math.max(1, deckRoot.offsetHeight - window.innerHeight);
      const top = deckRoot.getBoundingClientRect().top + window.scrollY + (i / Math.max(1, n - 1)) * total;
      window.scrollTo({ top, behavior: motion() });
    };

    const goTo = (i) => {
      if (mode === 'stack') {
        if (busy || drag) return;
        i = clamp(i, 0, n - 1);
        if (i === current) return;
        animateTo(i);
      } else if (mode === 'pinned') {
        goPinned(i);
      }
    };

    const renderPinned = () => {
      ticking = false;
      if (mode !== 'pinned') return;
      const rect = deckRoot.getBoundingClientRect();
      const total = Math.max(1, deckRoot.offsetHeight - window.innerHeight);
      const scrolled = clamp(-rect.top, 0, total);
      const scaled = (scrolled / total) * (n - 1);
      cards.forEach((card, i) => {
        const local = i === 0 ? 1 : clamp(scaled - (i - 1), 0, 1);
        const e = ease(local);
        const stack = Math.min(Math.max(0, scaled - i), 3);
        if (e < 1) {
          const ty = (1 - e) * 108;
          const rot = (1 - e) * 6;
          const sc = 0.97 + 0.03 * e;
          card.style.setProperty('--card-t', 'translateY(' + ty + '%) rotate(' + rot + 'deg) scale(' + sc + ')');
          card.style.setProperty('--card-dim', '0');
          card.classList.toggle('is-top', e > 0.55);
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
      setCurrent(clamp(Math.floor(scaled + 1.001), 1, n) - 1);
    };

    const onWinScroll = () => {
      if (mode !== 'pinned') return;
      if (!ticking) {
        ticking = true;
        requestAnimationFrame(renderPinned);
      }
    };

    const onPointerDown = (e) => {
      if (mode !== 'stack' || busy) return;
      if (e.button != null && e.button !== 0) return;
      const top = cards[current];
      if (!top || !top.contains(e.target)) return;
      if (drag && drag.captured) {
        try { stage.releasePointerCapture(drag.id); } catch (err) { /* ignore */ }
      }
      drag = {
        id: e.pointerId,
        x0: e.clientX,
        y0: e.clientY,
        t0: performance.now(),
        axis: null,
        lastX: e.clientX,
        lastT: performance.now(),
        vx: 0,
        captured: false
      };
    };

    const onPointerMove = (e) => {
      if (!drag || e.pointerId !== drag.id) return;
      const dx = e.clientX - drag.x0;
      const dy = e.clientY - drag.y0;
      const now = performance.now();
      const dt = Math.max(1, now - drag.lastT);
      drag.vx = (e.clientX - drag.lastX) / dt;
      drag.lastX = e.clientX;
      drag.lastT = now;

      if (!drag.axis) {
        if (Math.hypot(dx, dy) < AXIS_LOCK) return;
        if (Math.abs(dx) > Math.abs(dy)) {
          drag.axis = 'x';
          try {
            stage.setPointerCapture(drag.id);
            drag.captured = true;
          } catch (err) { /* ignore */ }
          deckRoot.classList.add('is-dragging');
        } else {
          drag.axis = 'y';
          drag = null;
          return;
        }
      }
      if (drag.axis !== 'x') return;
      if (e.cancelable) e.preventDefault();
      if (Math.abs(dx) > 16) dismissHint();

      const card = cards[current];
      const w = card.offsetWidth || 280;
      let x = dx;
      const atStart = current === 0 && x > 0;
      const atEnd = current === n - 1 && x < 0;
      if (atStart || atEnd) x = x / (1 + Math.abs(x) / Math.max(1, w * 0.7));
      const rot = (x / w) * 16;
      card.style.setProperty('--card-z', '50');

      if (x < 0 && current < n - 1) {
        const p = clamp(-x / (w * 0.8), 0, 1);
        const next = cards[current + 1];
        const ty = STACK_Y[1] * (1 - p);
        const sc = STACK_S[1] + (1 - STACK_S[1]) * p;
        const r = STACK_R[1] * (1 - p);
        const sx = STACK_X[1] * (1 - p);
        next.style.setProperty('--card-t', 'translate(' + sx + 'px, ' + ty + 'px) rotate(' + r + 'deg) scale(' + sc + ')');
        card.style.setProperty('--card-t', 'translateX(' + x + 'px) rotate(' + rot + 'deg)');
      } else if (x > 0 && current > 0) {
        const p = clamp(x / (w * 0.8), 0, 1);
        const prev = cards[current - 1];
        const start = -1.3 * w;
        prev.style.setProperty('--card-z', '55');
        prev.style.setProperty('--card-dim', '0');
        prev.style.setProperty('--card-t', 'translateX(' + (start + (0 - start) * p) + 'px) rotate(' + (-20 * (1 - p)) + 'deg)');
        const ty = STACK_Y[1] * p;
        const sc = 1 - (1 - STACK_S[1]) * p;
        const r = STACK_R[1] * p;
        const sx = STACK_X[1] * p;
        card.style.setProperty('--card-t', 'translate(' + (x + sx) + 'px, ' + ty + 'px) rotate(' + (rot + r) + 'deg) scale(' + sc + ')');
      } else {
        card.style.setProperty('--card-t', 'translateX(' + x + 'px) rotate(' + rot + 'deg)');
      }
    };

    const endDrag = (e, cancel) => {
      if (!drag || e.pointerId !== drag.id) return;
      const info = drag;
      drag = null;
      if (info.captured) {
        try { stage.releasePointerCapture(info.id); } catch (err) { /* ignore */ }
      }
      if (info.axis !== 'x') return;
      const dx = info.lastX - info.x0;
      const w = cards[current].offsetWidth || 280;
      const far = Math.abs(dx) > w * THRESH;
      const flick = Math.abs(info.vx) > FLICK && (info.vx === 0 || (info.vx < 0) === (dx < 0));
      const commit = !cancel && (far || flick);
      if (commit && dx < 0 && current < n - 1) {
        animateTo(current + 1);
        return;
      }
      if (commit && dx > 0 && current > 0) {
        animateTo(current - 1);
        return;
      }
      if (commit && ((dx < 0 && current >= n - 1) || (dx > 0 && current <= 0))) {
        bounce(dx < 0 ? -1 : 1);
        return;
      }
      enableTransitions();
      applyLayout();
    };

    const onPointerUp = (e) => endDrag(e, false);
    const onPointerCancel = (e) => endDrag(e, true);

    const onStageKey = (e) => {
      if (mode !== 'stack') return;
      let next = null;
      if (e.key === 'ArrowLeft' || e.key === 'PageUp') next = current - 1;
      else if (e.key === 'ArrowRight' || e.key === 'PageDown') next = current + 1;
      else if (e.key === 'Home') next = 0;
      else if (e.key === 'End') next = n - 1;
      if (next === null) return;
      e.preventDefault();
      if (busy || drag) return;
      if (next === current) {
        if (e.key === 'ArrowLeft' || e.key === 'PageUp' || e.key === 'Home') bounce(1);
        else if (e.key === 'ArrowRight' || e.key === 'PageDown' || e.key === 'End') bounce(-1);
        return;
      }
      goTo(next);
    };

    const setStackA11y = (on) => {
      if (on) {
        deckRoot.setAttribute('aria-roledescription', 'carousel');
        if (stage) {
          stage.setAttribute('tabindex', '0');
          stage.setAttribute('aria-label', 'Feature cards');
        }
        cards.forEach((card, i) => {
          card.setAttribute('role', 'group');
          card.setAttribute('aria-roledescription', 'slide');
          card.setAttribute('aria-label', (i + 1) + ' of ' + n);
        });
      } else {
        deckRoot.removeAttribute('aria-roledescription');
        if (stage) {
          stage.removeAttribute('tabindex');
          stage.removeAttribute('aria-label');
        }
        cards.forEach((card) => {
          card.removeAttribute('role');
          card.removeAttribute('aria-roledescription');
          card.removeAttribute('aria-label');
        });
      }
    };

    const startNudge = () => {
      if (nudged || prefersReduce() || hintGone || mode !== 'stack' || drag || busy) return;
      nudged = true;
      const card = cards[current];
      if (!card) return;
      enableTransitions();
      card.style.setProperty('--card-t', 'translateX(-22px) rotate(-5deg)');
      wait(380).then(() => {
        if (mode === 'stack' && !drag) applyLayout();
      });
    };

    const armNudge = () => {
      if (nudgeIo) {
        nudgeIo.disconnect();
        nudgeIo = null;
      }
      if (prefersReduce() || nudged || !stage) return;
      nudgeIo = new IntersectionObserver((entries) => {
        const e = entries[0];
        if (e && e.isIntersecting && e.intersectionRatio >= 0.4) {
          startNudge();
          if (nudgeIo) {
            nudgeIo.disconnect();
            nudgeIo = null;
          }
        }
      }, { threshold: [0.4, 0.6] });
      nudgeIo.observe(stage);
    };

    const leavePinned = () => {
      if (pinnedBound) {
        window.removeEventListener('scroll', onWinScroll);
        pinnedBound = false;
      }
    };

    const leaveStack = () => {
      if (stackBound && stage) {
        stage.removeEventListener('pointerdown', onPointerDown);
        stage.removeEventListener('pointermove', onPointerMove);
        stage.removeEventListener('pointerup', onPointerUp);
        stage.removeEventListener('pointercancel', onPointerCancel);
        stage.removeEventListener('keydown', onStageKey);
        stackBound = false;
      }
      if (nudgeIo) {
        nudgeIo.disconnect();
        nudgeIo = null;
      }
      drag = null;
      busy = false;
      deckRoot.classList.remove('is-dragging', 'is-stack');
      setStackA11y(false);
    };

    const enterPinned = () => {
      deckRoot.classList.add('is-js');
      deckRoot.classList.remove('is-static', 'is-stack', 'is-dragging');
      setStackA11y(false);
      if (!pinnedBound) {
        window.addEventListener('scroll', onWinScroll, { passive: true });
        pinnedBound = true;
      }
      renderPinned();
    };

    const enterStack = () => {
      deckRoot.classList.add('is-js', 'is-stack');
      deckRoot.classList.remove('is-static', 'is-dragging');
      clearCardVars();
      setStackA11y(true);
      setCurrent(current);
      applyLayout();
      if (stage && !stackBound) {
        stage.addEventListener('pointerdown', onPointerDown);
        stage.addEventListener('pointermove', onPointerMove, { passive: false });
        stage.addEventListener('pointerup', onPointerUp);
        stage.addEventListener('pointercancel', onPointerCancel);
        stage.addEventListener('keydown', onStageKey);
        stackBound = true;
      }
      armNudge();
    };

    const enterStatic = () => {
      deckRoot.classList.add('is-static');
      deckRoot.classList.remove('is-js', 'is-stack', 'is-dragging');
      clearCardVars();
      setStackA11y(false);
    };

    const armDeck = () => {
      const next = isStack() ? 'stack' : (prefersReduce() ? 'static' : 'pinned');
      if (next === mode) {
        if (mode === 'pinned') renderPinned();
        if (mode === 'stack') applyLayout();
        return;
      }
      if (mode === 'pinned') leavePinned();
      if (mode === 'stack') leaveStack();
      mode = next;
      if (mode === 'stack') enterStack();
      else if (mode === 'pinned') enterPinned();
      else enterStatic();
    };

    dots.forEach((d) => {
      d.addEventListener('click', () => {
        const i = parseInt(d.getAttribute('data-lp-deck-dot'), 10) - 1;
        if (Number.isNaN(i)) return;
        if (mode === 'stack' && i === current) return;
        goTo(i);
      });
    });
    if (prevBtn) prevBtn.addEventListener('click', () => {
      if (mode === 'stack') {
        if (busy || drag) return;
        if (current <= 0) bounce(1);
        else goTo(current - 1);
      } else goTo(current - 1);
    });
    if (nextBtn) nextBtn.addEventListener('click', () => {
      if (mode === 'stack') {
        if (busy || drag) return;
        if (current >= n - 1) bounce(-1);
        else goTo(current + 1);
      } else goTo(current + 1);
    });

    armDeck();
    if (typeof mqReduce.addEventListener === 'function') mqReduce.addEventListener('change', armDeck);
    if (typeof mqStack.addEventListener === 'function') mqStack.addEventListener('change', armDeck);
    window.addEventListener('resize', () => {
      if (mode === 'stack') applyLayout();
      else if (mode === 'pinned') onWinScroll();
    }, { passive: true });
  }



})();
