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
     THEME (LIGHT / DARK)
     ───────────────────────────────────────────────────── */
  PlaySpot.Theme = {
    init() {
      const savedTheme = localStorage.getItem('playspot-theme');
      const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      const theme = savedTheme || (prefersDark ? 'dark' : 'light');
      this.setTheme(theme, false);

      const toggleBtn = document.getElementById('ps-theme-toggle');
      if (toggleBtn) {
        toggleBtn.addEventListener('click', () => {
          const current = document.documentElement.getAttribute('data-bs-theme') || 'light';
          const next = current === 'dark' ? 'light' : 'dark';
          this.setTheme(next, true);
        });
      }

      window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (e) => {
        if (!localStorage.getItem('playspot-theme')) {
          this.setTheme(e.matches ? 'dark' : 'light', false);
        }
      });
    },

    setTheme(theme, save = true) {
      document.documentElement.setAttribute('data-bs-theme', theme);
      if (save) {
        localStorage.setItem('playspot-theme', theme);
      }
      this.updateIcon(theme);
    },

    updateIcon(theme) {
      const toggleBtn = document.getElementById('ps-theme-toggle');
      if (!toggleBtn) return;
      const icon = toggleBtn.querySelector('i');
      if (!icon) return;
      if (theme === 'dark') {
        icon.className = 'bi bi-sun-fill text-warning';
        toggleBtn.setAttribute('title', 'Light Mode');
      } else {
        icon.className = 'bi bi-moon-stars-fill text-primary';
        toggleBtn.setAttribute('title', 'Dark Mode');
      }
    }
  };

  /* ─────────────────────────────────────────────────────
     IMAGE FALLBACK
     ───────────────────────────────────────────────────── */
  PlaySpot.ImageFallback = {
    handleError(img, fallback) {
      if (!img) return;
      const target = fallback || img.getAttribute('data-fallback') || '/images/venues/fallbacks/venue.jpg';
      if (img.src !== target) {
        img.onerror = null;
        img.src = target;
      }
    },
    init() {
      document.querySelectorAll('img[data-fallback]').forEach(img => {
        img.addEventListener('error', function () {
          PlaySpot.ImageFallback.handleError(this);
        }, { once: true });
      });
    }
  };

  /* ─────────────────────────────────────────────────────
     SMART PLAYSPOT ASSISTANT
     ───────────────────────────────────────────────────── */
  PlaySpot.Assistant = {
    _panel: null,
    _trigger: null,
    _body: null,
    _role: 'Guest',
    _name: '',
    _isAr: false,
    _state: {
      sport: null,
      city: null,
      budget: null
    },

    init() {
      this._panel = document.getElementById('ps-assistant-panel');
      this._trigger = document.getElementById('ps-assistant-btn');
      this._body = document.getElementById('ps-assistant-body');
      if (!this._panel || !this._trigger || !this._body) return;

      this._role = this._panel.getAttribute('data-role') || 'Guest';
      this._name = this._panel.getAttribute('data-name') || '';
      this._isAr = document.documentElement.getAttribute('dir') === 'rtl' || 
                   document.documentElement.lang === 'ar' || 
                   window.location.search.includes('culture=ar');

      this._trigger.addEventListener('click', () => this.toggle());
      const closeBtn = document.getElementById('ps-assistant-close');
      if (closeBtn) closeBtn.addEventListener('click', () => this.close());

      this.renderWelcome();
    },

    toggle() {
      if (this._panel.classList.contains('open')) {
        this.close();
      } else {
        this.open();
      }
    },

    open() {
      this._panel.classList.add('open');
      const badge = this._trigger.querySelector('.pulse-badge');
      if (badge) badge.style.display = 'none';
      this.scrollToBottom();
    },

    close() {
      this._panel.classList.remove('open');
    },

    scrollToBottom() {
      setTimeout(() => {
        if (this._body) this._body.scrollTop = this._body.scrollHeight;
      }, 50);
    },

    reset() {
      this._state = { sport: null, city: null, budget: null };
      this._body.innerHTML = '';
      this.renderWelcome();
    },

    renderWelcome() {
      let welcomeMsg = '';
      const chips = [];

      if (this._role === 'Owner') {
        const greeting = this._name ? (this._isAr ? `مرحباً بك يا ${this._name}! 👋` : `Welcome, ${this._name}! 👋`) : (this._isAr ? 'مرحباً بك يا صاحب المنشأة! 👋' : 'Welcome, Facility Owner! 👋');
        welcomeMsg = this._isAr 
          ? `${greeting} أنت في بوابة إدارة ومتابعة منشأتك الرياضية. كيف أساعدك اليوم؟`
          : `${greeting} You are logged into your facility management portal. How can I assist you today?`;

        chips.push({ label: this._isAr ? '📊 لوحة الإحصائيات' : '📊 Owner Dashboard', action: () => window.location.href = '/Owner' });
        chips.push({ label: this._isAr ? '➕ إضافة ملعب جديد' : '➕ Add New Court', action: () => window.location.href = '/Owner/Venues/Create' });
        chips.push({ label: this._isAr ? '🏢 منشآتي الرياضية' : '🏢 My Venues', action: () => window.location.href = '/Owner/Venues' });
        chips.push({ label: this._isAr ? '🔍 البحث في الملاعب' : '🔍 Browse Venues', action: () => this.startDiscovery() });
      } else if (this._role === 'Client' || this._role === 'Player') {
        const greeting = this._name ? (this._isAr ? `أهلاً بك مجدداً، ${this._name}! 👋` : `Welcome back, ${this._name}! 👋`) : (this._isAr ? 'أهلاً بك يا بطل! 👋' : 'Welcome back, Champion! 👋');
        welcomeMsg = this._isAr
          ? `${greeting} جاهز لمباراتك القادمة؟ دعني أساعدك في حجز أفضل ملعب أو العثور على مباراة تناسبك.`
          : `${greeting} Ready for your next game? Let me help you find the best venue or match right now.`;

        chips.push({ label: this._isAr ? '⚡ احجز ملعباً (بحث سريع)' : '⚡ Quick Court Finder', action: () => this.startDiscovery() });
        chips.push({ label: this._isAr ? '👥 مباريات المجتمع' : '👥 Community Games', action: () => window.location.href = '/Games' });
        chips.push({ label: this._isAr ? '📅 حجوزاتي' : '📅 My Bookings', action: () => window.location.href = '/my-bookings' });
        chips.push({ label: this._isAr ? '⭐ أعلى الملاعب تقييماً' : '⭐ Top Rated Courts', action: () => window.location.href = '/Venues?sortBy=rating_desc' });
      } else {
        welcomeMsg = this._isAr
          ? 'مرحباً بك في بلاي سبوت! ⚡ أنا مساعدك الذكي لاستكشاف وحجز الملاعب الرياضية في مصر ومطابقة المباريات. بم تبدأ اليوم؟'
          : 'Welcome to PlaySpot! ⚡ I am your sports guide to discovering venues and joining community matches across Egypt. How can I help?';

        chips.push({ label: this._isAr ? '🔍 ابحث عن ملعب مناسب' : '🔍 Find a Court', action: () => this.startDiscovery() });
        chips.push({ label: this._isAr ? '👥 مباريات مفتوحة' : '👥 Open Matches', action: () => window.location.href = '/Games' });
        chips.push({ label: this._isAr ? '🔑 تسجيل الدخول' : '🔑 Sign In', action: () => window.location.href = '/Login' });
        chips.push({ label: this._isAr ? '📝 إنشاء حساب لاعب' : '📝 Register', action: () => window.location.href = '/Register' });
      }

      this.addBotMessage(welcomeMsg, chips);
    },

    startDiscovery() {
      this.addUserMessage(this._isAr ? 'أريد العثور على ملعب مناسب 🏟️' : 'I want to find a court 🏟️');
      
      const sportsMsg = this._isAr 
        ? 'ممتاز! ما هي الرياضة التي ترغب بممارستها؟' 
        : 'Awesome! Which sport would you like to play?';

      const sports = [
        { key: 'Football', label: '⚽ ' + (this._isAr ? 'كرة قدم' : 'Football') },
        { key: 'Padel', label: '🎾 ' + (this._isAr ? 'بادل' : 'Padel') },
        { key: 'Tennis', label: '🏸 ' + (this._isAr ? 'تنس' : 'Tennis') },
        { key: 'Basketball', label: '🏀 ' + (this._isAr ? 'كرة سلة' : 'Basketball') },
        { key: 'Volleyball', label: '🏐 ' + (this._isAr ? 'كرة طائرة' : 'Volleyball') },
        { key: 'Badminton', label: '🏸 ' + (this._isAr ? 'تنس ريشة' : 'Badminton') }
      ];

      const chips = sports.map(s => ({
        label: s.label,
        action: () => this.selectSport(s.key, s.label)
      }));

      this.addBotMessage(sportsMsg, chips);
    },

    selectSport(sportKey, sportLabel) {
      this._state.sport = sportKey;
      this.addUserMessage(sportLabel);

      const cityMsg = this._isAr 
        ? `رائع! في أي منطقة أو مدينة تفضل حجز ملعب ${sportLabel}؟` 
        : `Great choice! In which area or city are you looking to play?`;

      const cities = [
        { key: '', label: this._isAr ? '📍 جميع المناطق' : '📍 All Areas' },
        { key: 'Cairo', label: '📍 ' + (this._isAr ? 'القاهرة' : 'Cairo') },
        { key: 'New Cairo', label: '📍 ' + (this._isAr ? 'التجمع الخامس' : 'New Cairo') },
        { key: 'Maadi', label: '📍 ' + (this._isAr ? 'المعادي' : 'Maadi') },
        { key: 'Zayed', label: '📍 ' + (this._isAr ? 'الشيخ زايد' : 'Sheikh Zayed') },
        { key: 'Alexandria', label: '📍 ' + (this._isAr ? 'الإسكندرية' : 'Alexandria') }
      ];

      const chips = cities.map(c => ({
        label: c.label,
        action: () => this.selectCity(c.key, c.label)
      }));

      this.addBotMessage(cityMsg, chips);
    },

    selectCity(cityKey, cityLabel) {
      this._state.city = cityKey;
      this.addUserMessage(cityLabel);

      const budgetMsg = this._isAr
        ? 'ما هي ميزانيتك المفضلة لسعر الساعة؟'
        : 'What is your preferred budget per hour?';

      const budgets = [
        { max: null, label: this._isAr ? '💰 أي ميزانية' : '💰 Any Budget' },
        { max: 200, label: this._isAr ? '💰 أقل من 200 ج.م' : '💰 Under 200 EGP' },
        { max: 350, label: this._isAr ? '💰 حتى 350 ج.م' : '💰 Up to 350 EGP' },
        { max: 500, label: this._isAr ? '💰 حتى 500 ج.م' : '💰 Up to 500 EGP' }
      ];

      const chips = budgets.map(b => ({
        label: b.label,
        action: () => this.selectBudget(b.max, b.label)
      }));

      this.addBotMessage(budgetMsg, chips);
    },

    async selectBudget(maxBudget, budgetLabel) {
      this._state.budget = maxBudget;
      this.addUserMessage(budgetLabel);

      this.addBotMessage(this._isAr 
        ? '⏳ جاري استخراج أفضل الملاعب المتاحة المتطابقة...' 
        : '⏳ Searching for the best matching facilities...');

      try {
        let url = `/api/venues/search?pageSize=4`;
        if (this._state.sport) url += `&sport=${encodeURIComponent(this._state.sport)}`;
        if (this._state.city) url += `&city=${encodeURIComponent(this._state.city)}`;
        if (this._state.budget) url += `&maxPrice=${this._state.budget}`;

        const res = await fetch(url);
        if (!res.ok) throw new Error('Search failed');
        const data = await res.json();
        const items = data.items || [];

        if (items.length === 0) {
          const noMatchMsg = this._isAr
            ? 'لم أعثر على منشآت مطابقة تماماً للميزانية المحددة في هذه المنطقة. يمكنك استعراض كافة الملاعب في المنصة:'
            : 'No facilities matched these exact budget filters. You can browse all available venues here:';

          this.addBotMessage(noMatchMsg, [
            { label: this._isAr ? '🏟️ استعراض جميع الملاعب' : '🏟️ View All Venues', action: () => window.location.href = '/Venues' },
            { label: this._isAr ? '🔄 بحث جديد' : '🔄 Search Again', action: () => this.reset() }
          ]);
          return;
        }

        const foundMsg = this._isAr
          ? `وجدت لك ${items.length} منشآت رياضية متميزة مطابقة لطلبك! 🎯 اضغط على المنشأة للتفاصيل والحجز:`
          : `Found ${items.length} great sports facilities matching your search! 🎯 Click to view and book:`;

        const cardsContainer = document.createElement('div');
        cardsContainer.className = 'd-flex flex-column gap-2 mt-2 w-100';

        items.forEach(v => {
          const card = document.createElement('div');
          card.className = 'ps-assistant-venue-card p-2 d-flex gap-2 align-items-center cursor-pointer';
          card.style.cursor = 'pointer';
          card.innerHTML = `
            <img src="${v.primaryImageUrl || '/images/venues/fallbacks/venue.jpg'}" alt="${v.name}" style="width: 55px; height: 55px; object-fit: cover; border-radius: 8px;" onerror="this.src='/images/venues/fallbacks/venue.jpg'">
            <div class="flex-grow-1 overflow-hidden" style="font-size: 12px; line-height: 1.3;">
              <div class="fw-bold text-truncate text-dark">${v.name}</div>
              <div class="text-muted small">${v.city || ''} &bull; ⭐ ${v.averageRating ? v.averageRating.toFixed(1) : '5.0'}</div>
              <div class="text-primary fw-semibold">${v.startingPricePerHour ? v.startingPricePerHour + ' EGP/hr' : ''}</div>
            </div>
            <a href="/Venues/Details?id=${v.id}" class="ps-btn ps-btn-primary btn-sm py-1 px-2 rounded-pill" style="font-size: 11px;">
              ${this._isAr ? 'عرض' : 'View'}
            </a>
          `;
          cardsContainer.appendChild(card);
        });

        const actionChips = [
          { label: this._isAr ? '🏟️ استعراض المزيد بالبحث' : '🏟️ More Venues', action: () => window.location.href = `/Venues?sport=${this._state.sport || ''}&city=${this._state.city || ''}` },
          { label: this._isAr ? '🔄 ابدأ من جديد' : '🔄 Restart Search', action: () => this.reset() }
        ];

        this.addBotMessage(foundMsg, actionChips, cardsContainer);
      } catch (err) {
        this.addBotMessage(this._isAr
          ? 'عذراً، حدث خطأ أثناء جلب الملاعب. يرجى المحاولة مرة أخرى.'
          : 'Sorry, an error occurred while searching. Please try again.', [
          { label: this._isAr ? '🔄 إعادة المحاولة' : '🔄 Retry', action: () => this.reset() }
        ]);
      }
    },

    addBotMessage(text, chips = [], extraNode = null) {
      const bubble = document.createElement('div');
      bubble.className = 'ps-assistant-bubble bot shadow-xs';
      bubble.innerHTML = `<div>${text}</div>`;

      if (extraNode) {
        bubble.appendChild(extraNode);
      }

      if (chips && chips.length > 0) {
        const chipsContainer = document.createElement('div');
        chipsContainer.className = 'ps-assistant-chips';
        chips.forEach(c => {
          const btn = document.createElement('button');
          btn.type = 'button';
          btn.className = 'ps-assistant-chip';
          btn.textContent = c.label;
          btn.addEventListener('click', c.action);
          chipsContainer.appendChild(btn);
        });
        bubble.appendChild(chipsContainer);
      }

      this._body.appendChild(bubble);
      this.scrollToBottom();
    },

    addUserMessage(text) {
      const bubble = document.createElement('div');
      bubble.className = 'ps-assistant-bubble user shadow-xs';
      bubble.textContent = text;
      this._body.appendChild(bubble);
      this.scrollToBottom();
    }
  };

  /* ─────────────────────────────────────────────────────
     INIT ON DOM READY
     ───────────────────────────────────────────────────── */
  document.addEventListener('DOMContentLoaded', function () {
    PlaySpot.Theme.init();
    PlaySpot.LoadingBar.start();
    PlaySpot.MobileMenu.init();
    PlaySpot.FadeIn.init();
    PlaySpot.Alerts.autoHide();
    PlaySpot.LoadingBtn.autoBindForms();
    PlaySpot.ImageFallback.init();
    PlaySpot.Assistant.init();

    // Finish loading bar on full load
    window.addEventListener('load', () => PlaySpot.LoadingBar.finish());
  });

  // Expose globally
  window.PlaySpot = PlaySpot;

})(window.PlaySpot || {});

