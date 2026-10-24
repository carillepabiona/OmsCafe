/* ══════════════════════════════════════════════════════════
   menu-validation.js
   Custom (non-browser-bubble) validation for forms marked
   <form data-validate novalidate>.

   Fields opt in with data-* attributes:
     data-label="Item name"      required for a field to be validated; used in messages
     data-max / data-minlen      text length limits
     data-pattern="name"         allowed-characters preset (see PATTERNS)
     data-unique="items"         duplicate check against window.MM_EXISTING[...]
     data-min / data-max         number limits (data-min-msg / data-max-msg override text)
     data-money                  max 2 decimal places
     data-int                    whole numbers only
     data-counter                live "n/max" character counter
   ══════════════════════════════════════════════════════════ */
(function () {
    'use strict';

    const MAX_IMAGE_BYTES = 5 * 1024 * 1024; // 5 MB
    let uid = 0;

    const PATTERNS = {
        name: {
            re: /^[\p{L}\p{N} .,'’&()+\/-]+$/u,
            msg: 'Use letters, numbers and basic punctuation only ( . , \' & ( ) + / - ).'
        }
    };

    /* ── helpers ─────────────────────────────────────────── */
    const existing = () => window.MM_EXISTING || {};
    const label = el => el.dataset.label || el.name || 'This field';
    const host = el => el.closest('.form-group, .opt-edit-row, .time-row') || el.parentElement;
    const inValidatedForm = el => el && el.closest && el.closest('form[data-validate]');

    function errorEl(el, create) {
        if (!el.id) el.id = 'mm-f' + (++uid);
        const h = host(el);
        let err = h.querySelector('.field-error[data-for="' + el.id + '"]');
        if (!err && create) {
            err = document.createElement('div');
            err.className = 'field-error';
            err.id = el.id + '-err';
            err.dataset.for = el.id;
            err.setAttribute('role', 'alert');
            h.appendChild(err);
        }
        return err;
    }

    function setError(el, msg) {
        const err = errorEl(el, !!msg);
        if (msg) {
            err.textContent = msg;
            err.hidden = false;
            el.classList.add('is-invalid');
            el.classList.remove('is-valid');
            el.setAttribute('aria-invalid', 'true');
            el.setAttribute('aria-describedby', err.id);
        } else {
            if (err) { err.textContent = ''; err.hidden = true; }
            el.classList.remove('is-invalid');
            el.removeAttribute('aria-invalid');
            el.removeAttribute('aria-describedby');
            const filled = el.type !== 'file' && String(el.value || '').trim() !== '';
            el.classList.toggle('is-valid', filled && el.dataset.touched === '1');
        }
    }

    /* ── single-field rules → returns message or '' ──────── */
    function fieldError(el) {
        if (el.disabled || el.type === 'hidden' || el.type === 'checkbox' || !el.dataset.label) return '';
        const name = label(el);

        // files
        if (el.type === 'file') {
            const f = el.files && el.files[0];
            if (!f) return el.required ? 'Please choose ' + name.toLowerCase() + '.' : '';
            if (!f.type.startsWith('image/')) return 'Please choose an image file (JPG, PNG, WEBP or GIF).';
            if (f.size > MAX_IMAGE_BYTES) return 'Image is too large. Maximum size is 5 MB.';
            return '';
        }

        // browser couldn't even parse the number (e.g. "abc", "1e")
        if (el.type === 'number' && el.validity && el.validity.badInput) {
            return name + ' must be a valid number.';
        }

        let v = String(el.value == null ? '' : el.value);
        if (el.type !== 'number') v = v.trim();

        if (!v) {
            if (!el.required) return '';
            return el.tagName === 'SELECT'
                ? 'Please select a ' + name.toLowerCase() + '.'
                : name + ' is required.';
        }

        // numbers
        if (el.type === 'number') {
            const n = Number(v);
            if (!isFinite(n)) return name + ' must be a valid number.';

            const min = el.dataset.min !== undefined ? Number(el.dataset.min) : (el.min !== '' ? Number(el.min) : null);
            const max = el.dataset.max !== undefined ? Number(el.dataset.max) : (el.max !== '' ? Number(el.max) : null);

            if (min !== null && n < min) {
                return el.dataset.minMsg || (min === 0 ? name + ' cannot be negative.' : name + ' must be at least ' + min + '.');
            }
            if (max !== null && n > max) {
                return el.dataset.maxMsg || name + ' cannot be more than ' + max.toLocaleString('en-PH') + '.';
            }
            if (el.dataset.int !== undefined && !Number.isInteger(n)) return name + ' must be a whole number.';
            if (el.dataset.money !== undefined && !/^\d+(\.\d{1,2})?$/.test(v)) {
                return 'Enter a valid amount with up to 2 decimal places (e.g., 85.50).';
            }
            return '';
        }

        // text-like
        const minLen = Number(el.dataset.minlen) || 0;
        const maxLen = Number(el.dataset.max) || 0;
        if (minLen && v.length < minLen) return name + ' must be at least ' + minLen + ' characters.';
        if (maxLen && v.length > maxLen) return name + ' must be ' + maxLen + ' characters or fewer.';

        if (el.dataset.pattern && PATTERNS[el.dataset.pattern]) {
            const p = PATTERNS[el.dataset.pattern];
            if (!p.re.test(v)) return p.msg;
            if (el.dataset.pattern === 'name' && !/[\p{L}\p{N}]/u.test(v)) {
                return name + ' must contain at least one letter or number.';
            }
        }

        if (el.dataset.unique) {
            const kind = el.dataset.unique;
            const list = existing()[kind] || [];
            const idInput = el.form && el.form.querySelector('input[name="Id"]');
            const selfId = ((idInput && idInput.value) || '').toLowerCase();
            const key = v.toLowerCase();
            const catEl = kind === 'items' ? document.getElementById('itemCategoryId') : null;
            const cat = catEl ? String(catEl.value).toLowerCase() : null;

            const dup = list.some(x =>
                String(x.id).toLowerCase() !== selfId &&
                String(x.name).trim().toLowerCase() === key &&
                (cat === null || cat === '' || String(x.categoryId).toLowerCase() === cat));

            if (dup) {
                return kind === 'items'
                    ? 'An item named “' + v + '” already exists in this category.'
                    : name + ' “' + v + '” already exists.';
            }
        }
        return '';
    }

    /* ── form-level rules ────────────────────────────────── */
    const FORM_RULES = {
        custForm: function (form) {
            const box = form.querySelector('#custOptionsContainer');
            const out = form.querySelector('#custOptionsError');
            const rows = Array.from(box.querySelectorAll('.opt-edit-row'));
            let msg = '';

            if (!rows.length) {
                msg = 'Add at least one option for this customization.';
            } else {
                const labels = rows
                    .map(r => r.querySelector('[name$=".OptionLabel"]').value.trim().toLowerCase())
                    .filter(Boolean);
                if (new Set(labels).size !== labels.length) {
                    msg = 'Option labels must be unique. Remove or rename the duplicate.';
                }
                const isRadio = form.querySelector('#custType').value === 'Radio';
                const defaults = rows.filter(r => r.querySelector('[name$=".IsDefault"]').checked).length;
                if (!msg && isRadio && defaults > 1) {
                    msg = 'A single-choice (Radio) customization can only have one default option.';
                }
            }
            out.textContent = msg;
            out.hidden = !msg;
            return { msg: msg, focus: form.querySelector('.opts-hdr .txt-btn') };
        }
    };

    /* ── whole-form validation ───────────────────────────── */
    function validateForm(form) {
        let first = null;

        form.querySelectorAll('[data-label]').forEach(el => {
            if ((el.tagName === 'TEXTAREA' || el.type === 'text') && el.value) el.value = el.value.trim();
            el.dataset.touched = '1';
            const m = fieldError(el);
            setError(el, m);
            if (m && !first) first = el;
        });

        const rule = FORM_RULES[form.id];
        if (rule) {
            const r = rule(form);
            if (r.msg && !first) first = r.focus;
        }

        if (first) {
            first.scrollIntoView({ block: 'center', behavior: 'smooth' });
            try { first.focus({ preventScroll: true }); } catch (_) { /* noop */ }
            return false;
        }
        return true;
    }

    function shake(form) {
        const box = form.closest('.modal, .cafe-modal') || form;
        box.classList.remove('is-shaking');
        void box.offsetWidth; // restart animation
        box.classList.add('is-shaking');
        setTimeout(() => box.classList.remove('is-shaking'), 400);
    }

    function lock(form, submitter) {
        const btns = Array.from(form.querySelectorAll('button[type="submit"]'));
        const main = submitter || btns[0];
        setTimeout(() => {
            btns.forEach(b => { b.disabled = true; });
            if (main) {
                main.dataset.orig = main.innerHTML;
                main.innerHTML = '<span class="btn-spinner"></span> Saving…';
            }
        }, 0);
    }

    function unlock(form) {
        form.querySelectorAll('button[type="submit"]').forEach(b => {
            if (b.dataset.orig) { b.innerHTML = b.dataset.orig; delete b.dataset.orig; }
            b.disabled = false;
        });
    }

    /* ── character counters ──────────────────────────────── */
    function updateCounter(el) {
        const max = Number(el.dataset.max) || 0;
        if (!el.dataset.counter || !max) return;
        let c = el.parentElement.querySelector('.char-count[data-for="' + (el.id || '') + '"]');
        if (!c) {
            if (!el.id) el.id = 'mm-f' + (++uid);
            c = document.createElement('div');
            c.className = 'char-count';
            c.dataset.for = el.id;
            el.after(c);
        }
        const len = el.value.length;
        c.textContent = len + ' / ' + max;
        c.classList.toggle('over', len > max);
    }

    /* ── events (delegated, so dynamic option rows just work) ─ */
    document.addEventListener('submit', function (e) {
        const form = e.target;
        if (!form.matches || !form.matches('form[data-validate]')) return;
        const sub = e.submitter;
        // buttons marked formnovalidate (e.g. "Mark available") skip field validation
        if (!(sub && sub.hasAttribute('formnovalidate'))) {
            if (!validateForm(form)) {
                e.preventDefault();
                e.stopImmediatePropagation();
                shake(form);
                return;
            }
        }
        lock(form, sub);
    }, true);

    document.addEventListener('focusout', function (e) {
        const el = e.target;
        if (!inValidatedForm(el) || !el.dataset || !el.dataset.label || el.type === 'file') return;
        el.dataset.touched = '1';
        setError(el, fieldError(el));
    });

    document.addEventListener('input', function (e) {
        const el = e.target;
        if (!inValidatedForm(el)) return;
        updateCounter(el);
        if (el.dataset && el.dataset.label && (el.dataset.touched === '1' || el.classList.contains('is-invalid'))) {
            setError(el, fieldError(el));
        }
    });

    document.addEventListener('change', function (e) {
        const el = e.target;
        const form = inValidatedForm(el);
        if (!form) return;

        // category change can create/clear a duplicate-name conflict
        if (el.id === 'itemCategoryId') {
            const n = document.getElementById('itemName');
            if (n && n.value.trim()) setError(n, fieldError(n));
        }
        if (el.dataset && el.dataset.label && el.type !== 'file') setError(el, fieldError(el));

        // re-check option rules live once the user has already hit an error
        if (form.id === 'custForm') {
            const out = form.querySelector('#custOptionsError');
            if (out && !out.hidden) FORM_RULES.custForm(form);
            if (el.id === 'custType') FORM_RULES.custForm(form);
        }
    });

    // coming back via the browser's back button must not leave "Saving…" stuck
    window.addEventListener('pageshow', function (e) {
        if (e.persisted) document.querySelectorAll('form[data-validate]').forEach(unlock);
    });

    /* ── public API ──────────────────────────────────────── */
    window.MM = {
        validateForm: validateForm,

        /** Clear all errors/state; call after programmatically filling a form. */
        reset: function (form) {
            if (!form) return;
            form.querySelectorAll('.field-error').forEach(x => {
                if (x.id === 'custOptionsError') { x.textContent = ''; x.hidden = true; }
                else x.remove();
            });
            form.querySelectorAll('.is-invalid, .is-valid').forEach(x => x.classList.remove('is-invalid', 'is-valid'));
            form.querySelectorAll('[data-touched]').forEach(x => delete x.dataset.touched);
            form.querySelectorAll('[aria-invalid]').forEach(x => x.removeAttribute('aria-invalid'));
            form.querySelectorAll('[data-counter]').forEach(updateCounter);
            unlock(form);
        },

        /** Image picker check. Returns true if OK; otherwise shows the error and clears the input. */
        checkFile: function (input) {
            const m = fieldError(input);
            setError(input, m);
            if (m) { input.value = ''; return false; }
            return true;
        },

        /** For auto-submitting forms (availability times). */
        autoSubmit: function (form) {
            if (validateForm(form)) form.submit();
            else shake(form);
        }
    };

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-counter]').forEach(updateCounter);
    });
})();