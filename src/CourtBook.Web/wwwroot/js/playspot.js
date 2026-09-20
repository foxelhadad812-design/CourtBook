/**
 * PlaySpot Core JS v1.0
 * 
 * Modules:
 *  - LoadingBar     : Top progress bar on page load
 *  - Toast          : Notification toasts (success/error/warning/info)
 *  - LoadingBtn     : Button loading states with spinner
 *  - FormValidator  : Client-side form field feedback
 *  - MobileMenu     : Responsive navigation toggle
 *  - Confirm        : Styled confirmation dialog via native modal
 *  - FadeIn         : Intersection observer for fade-in animations
 */

(function (PlaySpot) {
  'use strict';

  /* ─────────────────────────────────────────────────────
     LOADING BAR
     ───────────────────────────────────────────────────── */
  PlaySpot.LoadingBar = {
    _bar: null,
    _timer: null,
    _progress: 0,

    init() {
      const bar = document.createElement('div');
      bar.id = 'ps-loading-bar';
      document.body.prepend(bar);
      this._bar = bar;
    },

    start() {
      if (!this._bar) this.init();
      this._progress = 0;
      this._bar.style.opacity = '1';
      this._bar.style.width = '0%';
      this._bar.classList.remove('complete');
      this._advance();
    },

    _advance() {
      clearTimeout(this._timer);
      if (this._progress < 85) {
        this._progress += (Math.random() * 8) + 2;
        this._bar.style.width = this._progress + '%';
        this._timer = setTimeout(() => this._advance(), 300);
      }
    },

    finish() {
      if (!this._bar) return;
      clearTimeout(this._timer);
      this._bar.style.width = '100%';
      setTimeout(() => {
        this._bar.classList.add('complete');
      }, 200);
    }
  };

  /* ─────────────────────────────────────────────────────
     TOAST NOTIFICATIONS
     ───────────────────────────────────────────────────── */
  PlaySpot.Toast = {
    _container: null,
    _icons: {
      success: '<i class="bi bi-check-circle-fill"></i>',
      error:   '<i class="bi bi-x-circle-fill"></i>',
      warning: '<i class="bi bi-exclamation-triangle-fill"></i>',
      info:    '<i class="bi bi-info-circle-fill"></i>',
    },
    _titles: {
      success: 'Success',
      error:   'Error',
      warning: 'Warning',
      info:    'Information',
    },

    _getContainer() {
      if (!this._container) {
        this._container = document.createElement('div');
        this._container.id = 'ps-toast-container';
        document.body.appendChild(this._container);
      }
      return this._container;
    },

    show(type, message, { title, duration = 4000 } = {}) {
      const container = this._getContainer();
      const toast = document.createElement('div');
      toast.className = `ps-toast ps-toast-${type}`;
      toast.setAttribute('role', 'alert');
      toast.setAttribute('aria-live', 'polite');

      toast.innerHTML = `
        <span class="ps-toast-icon">${this._icons[type] || ''}</span>
        <div class="ps-toast-body">
          <div class="ps-toast-title">${title || this._titles[type] || ''}</div>
          <div class="ps-toast-message">${this._escapeHtml(message)}</div>
        </div>
        <button class="ps-toast-close" aria-label="Close">
          <i class="bi bi-x"></i>
        </button>
      `;

      // Close button handler
      toast.querySelector('.ps-toast-close').addEventListener('click', () => this._dismiss(toast));

      container.appendChild(toast);

      if (duration > 0) {
        setTimeout(() => this._dismiss(toast), duration);
      }

      return toast;
    },

    success(message, opts)  { return this.show('success', message, opts); },
    error(message, opts)    { return this.show('error', message, { duration: 0, ...opts }); },
    warning(message, opts)  { return this.show('warning', message, opts); },
    info(message, opts)     { return this.show('info', message, opts); },

    _dismiss(toast) {
      toast.style.animation = 'ps-toast-out 0.25s ease-in forwards';
      setTimeout(() => toast.remove(), 250);
    },

    _escapeHtml(str) {
      const div = document.createElement('div');
      div.textContent = str;
      return div.innerHTML;
    }
  };

  /* ─────────────────────────────────────────────────────
     LOADING BUTTON
     ───────────────────────────────────────────────────── */
  PlaySpot.LoadingBtn = {
    /**
     * Put a button into loading state.
     * @param {HTMLButtonElement|string} btnOrSelector
     * @returns {Function} restore — call to reset the button
     */
    start(btnOrSelector) {
      const btn = typeof btnOrSelector === 'string'
        ? document.querySelector(btnOrSelector)
        : btnOrSelector;

      if (!btn) return () => {};

      const original = btn.innerHTML;
      btn.disabled = true;
      btn.classList.add('loading');

      // Inject spinner if not present
      if (!btn.querySelector('.ps-spinner')) {
        btn.innerHTML = `<span class="ps-spinner"></span><span class="ps-btn-text">${btn.innerHTML}</span>`;
      }

      return function restore() {
        btn.disabled = false;
        btn.classList.remove('loading');
        btn.innerHTML = original;
      };
    },

    /**
     * Auto-bind to all forms: shows spinner on submit, restores on response.
     */
    autoBindForms() {
      document.querySelectorAll('form[data-loading-btn]').forEach(form => {
        form.addEventListener('submit', function () {
          const submitBtn = form.querySelector('[type="submit"]');
          if (submitBtn) PlaySpot.LoadingBtn.start(submitBtn);
        });
      });
    }
  };

  /* ─────────────────────────────────────────────────────
     MOBILE MENU
     ───────────────────────────────────────────────────── */
  PlaySpot.MobileMenu = {
    init() {
      const toggle = document.getElementById('ps-mobile-toggle');
      const menu   = document.getElementById('ps-mobile-menu');
      if (!toggle || !menu) return;

      toggle.addEventListener('click', (e) => {
        e.stopPropagation();
        const isOpen = menu.classList.toggle('open');
        toggle.setAttribute('aria-expanded', isOpen);
      });

      // Close on outside click
      document.addEventListener('click', (e) => {
        if (!menu.contains(e.target) && e.target !== toggle) {
          menu.classList.remove('open');
          toggle.setAttribute('aria-expanded', 'false');
        }
      });

      // Close on Escape
      document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
          menu.classList.remove('open');
          toggle.setAttribute('aria-expanded', 'false');
        }
      });
    }
  };

  /* ─────────────────────────────────────────────────────
     FADE-IN ANIMATION (Intersection Observer)
     ───────────────────────────────────────────────────── */
  PlaySpot.FadeIn = {
    init() {
      if (!('IntersectionObserver' in window)) return;

      const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
          if (entry.isIntersecting) {
            entry.target.classList.add('ps-animate-fade-in');
            observer.unobserve(entry.target);
          }
        });
      }, { threshold: 0.1, rootMargin: '0px 0px -40px 0px' });

      document.querySelectorAll('[data-fade-in]').forEach(el => {
        observer.observe(el);
      });
    }
  };

  /* ─────────────────────────────────────────────────────
     ALERT HELPER (dismissible Bootstrap alerts cleanup)
     ───────────────────────────────────────────────────── */
  PlaySpot.Alerts = {
    autoHide(selector = '.alert-auto-hide', delay = 5000) {
      document.querySelectorAll(selector).forEach(alert => {
        setTimeout(() => {
          if (typeof bootstrap !== 'undefined' && bootstrap.Alert) {
            const bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
            bsAlert?.close();
          } else {
            alert.style.opacity = '0';
            setTimeout(() => alert.remove(), 300);
          }
        }, delay);
      });
    }
  };

  /* ─────────────────────────────────────────────────────
     CONFIRM DIALOG
     ───────────────────────────────────────────────────── */
  PlaySpot.Confirm = {
    /**
     * Show a Bootstrap modal confirm dialog.
     * @returns {Promise<boolean>}
     */
    show({ title = 'Are you sure?', message = '', confirmText = 'Confirm', isDanger = false } = {}) {
      return new Promise(resolve => {
        const id = 'ps-confirm-' + Date.now();
        const btnClass = isDanger ? 'btn-danger' : 'btn-primary';

        const html = `
          <div class="modal fade" id="${id}" tabindex="-1" aria-modal="true" role="dialog">
            <div class="modal-dialog modal-dialog-centered modal-sm">
              <div class="modal-content border-0 shadow-lg" style="border-radius: var(--ps-radius-xl);">
                <div class="modal-body p-4 text-center">
                  ${isDanger ? '<div style="font-size:2.5rem;margin-bottom:.5rem;">⚠️</div>' : ''}
                  <h5 class="fw-bold mb-2">${title}</h5>
                  ${message ? `<p class="text-muted small mb-0">${message}</p>` : ''}
                </div>
                <div class="modal-footer border-0 pt-0 d-flex gap-2 justify-content-center pb-4">
                  <button type="button" class="btn btn-light px-4" data-bs-dismiss="modal">Cancel</button>
                  <button type="button" class="btn ${btnClass} px-4" id="${id}-confirm">${confirmText}</button>
                </div>
              </div>
            </div>
          </div>
        `;

        document.body.insertAdjacentHTML('beforeend', html);
        const el = document.getElementById(id);
        const modal = new bootstrap.Modal(el);

        document.getElementById(`${id}-confirm`).addEventListener('click', () => {
          modal.hide();
          resolve(true);
        });

        el.addEventListener('hidden.bs.modal', () => {
          el.remove();
          resolve(false);
        }, { once: true });

        modal.show();
      });
    }
  };

  /* ─────────────────────────────────────────────────────
     INIT ON DOM READY
     ───────────────────────────────────────────────────── */
  document.addEventListener('DOMContentLoaded', function () {
    PlaySpot.LoadingBar.start();
    PlaySpot.MobileMenu.init();
    PlaySpot.FadeIn.init();
    PlaySpot.Alerts.autoHide();
    PlaySpot.LoadingBtn.autoBindForms();

    // Finish loading bar on full load
    window.addEventListener('load', () => PlaySpot.LoadingBar.finish());
  });

  // Expose globally
  window.PlaySpot = PlaySpot;

})(window.PlaySpot || {});
