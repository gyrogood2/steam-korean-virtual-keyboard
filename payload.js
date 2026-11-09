(function(){
  var VERSION = 85;
  var TOKEN = '__SIX_TOKEN__';
  var out = [];

  // ---- labels: only on the virtual keyboard page (input is never touched)
  if (document.querySelector('[data-key="VKMove"],[data-key="VKPaste"],[data-key="SwitchKeys_Steam"]')) {
    // A previous run may have left its UI behind. Its token is baked into the
    // fetch URLs and dies with the old process, so reinstall when it differs.
    if (window.__sixUi && window.__sixUi.version === VERSION && window.__sixUi.token === TOKEN) {
      out.push('ui:already');
    } else {
      if (window.__sixUiCleanup) { try { window.__sixUiCleanup(); } catch (e) {} }
      installUi();
      out.push('ui:installed');
    }
  }

  return out.join(',') || 'skip';

  // ============================================================== UI
  function installUi() {
    var LATIN = { q:'ㅂ',w:'ㅈ',e:'ㄷ',r:'ㄱ',t:'ㅅ',y:'ㅛ',u:'ㅕ',i:'ㅑ',o:'ㅐ',p:'ㅔ',a:'ㅁ',s:'ㄴ',d:'ㅇ',f:'ㄹ',g:'ㅎ',h:'ㅗ',j:'ㅓ',k:'ㅏ',l:'ㅣ',z:'ㅋ',x:'ㅌ',c:'ㅊ',v:'ㅍ',b:'ㅠ',n:'ㅜ',m:'ㅡ' };
    var SHIFT = { Q:'ㅃ',W:'ㅉ',E:'ㄸ',R:'ㄲ',T:'ㅆ',O:'ㅒ',P:'ㅖ' };
    var BASE = ';position:absolute;inset:0;z-index:99;display:flex;align-items:center;justify-content:center;font-family:"Malgun Gothic",sans-serif;font-weight:700;font-size:18px;pointer-events:none;color:var(--six-label-color,#18E215);text-shadow:var(--six-label-shadow,none)';

    var css = 'body.six-ko [data-key]{position:relative}';
    for (var lk in LATIN) {
      css += 'body.six-ko [data-key="' + lk + '"] span{visibility:hidden!important}';
      css += 'body.six-ko [data-key="' + lk + '"]::after{content:"' + LATIN[lk] + '"' + BASE + '}';
    }
    for (var sk in SHIFT) {
      var skl = sk.toLowerCase();
      css += 'body.six-ko.six-shift [data-key="' + skl + '"]::after,body.six-ko.six-caps [data-key="' + skl + '"]::after{content:"' + SHIFT[sk] + '"}';
    }
    css += 'body.six-ui [data-key=" "]{position:relative}';
    css += '.six-ind{position:absolute;right:8px;top:calc(50% + 5px);transform:translateY(-50%);z-index:999;display:flex;align-items:center;gap:6px;pointer-events:none;font-family:"Malgun Gothic",sans-serif;color:var(--six-ind-fg,#0b1a0b)}';
    css += '.six-badge{display:inline-block;width:18px;height:18px;line-height:18px;text-align:center;border-radius:50%;background:var(--six-ind-fg,#0b1a0b);color:var(--six-ind-bg,#ffffff);font-family:"Segoe UI",Arial,sans-serif;font-size:8px;font-weight:700;letter-spacing:0}';
    css += '.six-hint{font-size:9px;font-weight:600;opacity:.9;text-shadow:var(--six-label-shadow,none)}';
    css += '.six-state{font-size:13px;font-weight:700;min-width:14px;text-align:center;text-shadow:var(--six-label-shadow,none)}';

    var style = document.createElement('style');
    style.id = '__six_kb_style';
    style.textContent = css;
    try { document.head.appendChild(style); } catch (e) {}

    var comp = null;

    function hookLongPress(c) {
      if (!c || c.__sixLongPressHooked) return;
      try {
        var orig = c.LongPressTimerExpired;
        // Extended-key popup (ż, é, ...) is disabled everywhere.
        c.LongPressTimerExpired = function () {};
        c.__sixLongPressHooked = true;
        c.__sixLongPressOrig = orig;
      } catch (e) {}
    }

    function findComp() {
      var el = document.querySelector('[data-key="q"]');
      if (!el) return null;
      var fk = Object.keys(el).filter(function (k) { return k.indexOf('__reactFiber$') === 0; })[0];
      if (!fk) return null;
      var f = el[fk], d = 0;
      while (f && d < 60) {
        var st = f.stateNode;
        if (st && st.state && st.state.toggleStates) return st;
        f = f.return; d++;
      }
      return null;
    }

    function spaceColors() {
      try {
        var sp = document.querySelector('[data-key=" "]');
        if (!sp) return;
        var s = sp.querySelector('span');
        var fg = s ? getComputedStyle(s).color : null;
        var ch = sp.firstElementChild;
        var bg = ch ? getComputedStyle(ch).backgroundColor : null;
        if (fg && fg !== 'transparent' && fg !== 'rgba(0, 0, 0, 0)') document.body.style.setProperty('--six-ind-fg', fg);
        if (bg && bg !== 'transparent' && bg !== 'rgba(0, 0, 0, 0)') document.body.style.setProperty('--six-ind-bg', bg);
      } catch (e) {}
    }

    function ensureIndicator() {
      var sp = document.querySelector('[data-key=" "]');
      if (!sp) return;
      var ind = sp.querySelector('.six-ind');
      if (!ind) {
        ind = document.createElement('div');
        ind.className = 'six-ind';
        ind.innerHTML = '<span class="six-badge">L1</span><span class="six-hint">표시 전환</span>' +
                        '<span class="six-badge">R1</span><span class="six-hint">한/영</span>' +
                        '<span class="six-state"></span>';
        sp.appendChild(ind);
      }
      var st = ind.querySelector('.six-state');
      if (st) st.textContent = document.body.classList.contains('six-ko') ? '한' : '영';
    }

    function relabel() {
      if (!comp) comp = findComp();
      hookLongPress(comp);

      var sh = false;
      var caps = false;
      try {
        if (comp && comp.state && comp.state.toggleStates) {
          var ts = comp.state.toggleStates;
          sh = !!(ts.Shift && ts.Shift !== 0);
          caps = !!(ts.CapsLock && ts.CapsLock !== 0);
        }
      } catch (e) {}

      try {
        var els = document.querySelectorAll('[data-key]');
        for (var i = 0; i < els.length; i++) {
          var el = els[i];
          var k = el.getAttribute('data-key');
          if (!k) continue;
          var isLetter = k.length === 1 && /[A-Za-z]/.test(k);
          var isSpace = k === ' ';
          if (!isLetter && !isSpace) continue;
          var sp = el.querySelector('span');
          if (!sp) continue;
          var cs = getComputedStyle(sp);
          var col = cs.color;
          if (!col || col === 'transparent' || col === 'rgba(0, 0, 0, 0)') {
            var fill = cs.getPropertyValue('-webkit-text-fill-color');
            if (fill && fill !== 'transparent' && fill !== 'rgba(0, 0, 0, 0)') col = fill;
          }
          if (col && col !== 'transparent' && col !== 'rgba(0, 0, 0, 0)') {
            el.style.setProperty('--six-label-color', col);
          }
          var shadow = cs.textShadow;
          if (shadow && shadow !== 'none') {
            el.style.setProperty('--six-label-shadow', shadow);
          }
        }
      } catch (e) {}

      try { document.body.classList.toggle('six-shift', sh); } catch (e) {}
      try { document.body.classList.toggle('six-caps', caps); } catch (e) {}
      try { spaceColors(); } catch (e) {}
      try { ensureIndicator(); } catch (e) {}
    }

    var flipAt = 0;

    function setKoreanState(korean) {
      try { document.body.classList.toggle('six-ko', !!korean); } catch (e) {}
    }

    function toggleHangul() {
      flipAt = Date.now();
      var next = true;
      try { next = !document.body.classList.contains('six-ko'); } catch (e) {}
      setKoreanState(next);
      try {
        fetch('http://127.0.0.1:8793/hangul?t=__SIX_TOKEN__', { cache: 'no-store' })
          .then(function (r) { return r.json(); })
          .then(function (s) {
            if (s && typeof s.korean === 'boolean') {
              setKoreanState(s.korean);
              flipAt = Date.now();
            }
          })
          .catch(function () {});
      } catch (e) {}
    }

    function syncState() {
      if (Date.now() - flipAt < 900) return;
      try {
        fetch('http://127.0.0.1:8793/state?t=__SIX_TOKEN__', { cache: 'no-store' })
          .then(function (r) { return r.json(); })
          .then(function (s) {
            if (s && typeof s.korean === 'boolean') {
              setKoreanState(s.korean);
            }
          })
          .catch(function () {});
      } catch (e) {}
    }

    function onVgp(e, down) {
      try {
        var d = e.detail || {};
        var b = d.button;
        if (!b) return;
        // L1: flip the displayed state only.
        if (b === 5) {
          e.stopPropagation();
          e.preventDefault();
          if (down) {
            flipAt = Date.now();
            try { document.body.classList.toggle('six-ko'); } catch (x) {}
            fetch('http://127.0.0.1:8793/flip?t=__SIX_TOKEN__', { cache: 'no-store' }).catch(function () {});
          }
          return;
        }
        // R1: switch the real 한/영 state.
        if (b === 6) {
          e.stopPropagation();
          e.preventDefault();
          if (down) {
            toggleHangul();
          }
          return;
        }
      } catch (x) {}
    }
    var vgpDown = function (e) { onVgp(e, true); };
    var vgpUp = function (e) { onVgp(e, false); };
    document.addEventListener('vgp_onbuttondown', vgpDown, true);
    document.addEventListener('vgp_onbuttonup', vgpUp, true);

    var pending = false;
    var mo = new MutationObserver(function () {
      if (pending) return;
      pending = true;
      setTimeout(function () { pending = false; relabel(); }, 60);
    });
    mo.observe(document.body, { childList: true, subtree: true, characterData: true });

    var timer = setInterval(relabel, 120);
    var poll = setInterval(syncState, 250);

    try { document.body.classList.add('six-ui'); } catch (e) {}
    syncState();
    relabel();

    window.__sixUi = { version: VERSION, token: TOKEN };
    window.__sixUiCleanup = function () {
      try { mo.disconnect(); } catch (e) {}
      clearInterval(timer);
      clearInterval(poll);
      try {
        if (comp && comp.__sixLongPressHooked) {
          comp.LongPressTimerExpired = comp.__sixLongPressOrig;
          comp.__sixLongPressHooked = false;
        }
      } catch (e) {}
      try {
        document.removeEventListener('vgp_onbuttondown', vgpDown, true);
        document.removeEventListener('vgp_onbuttonup', vgpUp, true);
      } catch (e) {}
      try { document.body.classList.remove('six-ui', 'six-ko', 'six-shift', 'six-caps'); } catch (e) {}
      try { var ind = document.querySelector('.six-ind'); if (ind) ind.remove(); } catch (e) {}
      try { var st = document.getElementById('__six_kb_style'); if (st) st.remove(); } catch (e) {}
      window.__sixUi = null;
      window.__sixUiCleanup = null;
    };
  }

})()
