using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SteamOskKorean
{
    /// <summary>
    /// Injects the OSK UI payload into Steam's virtual keyboard page and can undo it.
    /// </summary>
    internal sealed class VirtualKeyboardInjector
    {
        private const string RestoreExpression =
            "(function(){try{if(window.__sixUiCleanup)window.__sixUiCleanup();}catch(e){}return 'r';})()";

        // Installs a thin key hook in the context that owns ControllerKeyboardSendText.
        // Letters are forwarded to the local server, which sends real VK events.
        private static string BuildKeyHookExpression(string token)
        {
            // The token stays inline in the installed function so the sweep can
            // tell its own hook from one left behind by a killed process.
            return "(function(){try{var I=window.SteamClient&&window.SteamClient.Input;" +
                "if(!I||typeof I.ControllerKeyboardSendText!=='function')return 'skip';" +
                "var stale=false;" +
                "if(I.__sixKeyHook){" +
                "var s=String(I.ControllerKeyboardSendText);" +
                "if(s.indexOf('" + token + "')>=0)return 'already';" +
                // Only unwrap when the installed function is ours. If Steam has
                // replaced it, the saved original is older than the live one.
                "if(s.indexOf('127.0.0.1:8793')>=0&&I.__sixKeyOriginal)I.ControllerKeyboardSendText=I.__sixKeyOriginal;" +
                "I.__sixKeyHook=false;stale=true;}" +
                "var o=I.ControllerKeyboardSendText;" +
                "I.ControllerKeyboardSendText=function(x){" +
                "if(typeof x==='string'&&x.length===1&&/[A-Za-z]/.test(x)){" +
                "try{fetch('http://127.0.0.1:8793/key?t=" + token + "&c='+encodeURIComponent(x))" +
                ".then(function(r){if(!r.ok)o.call(I,x);})" +
                ".catch(function(){o.call(I,x);});}catch(e){o.call(I,x);}" +
                "return;}" +
                "return o.call(I,x);};" +
                "I.__sixKeyHook=true;I.__sixKeyOriginal=o;" +
                "return stale?'refreshed':'installed';" +
                "}catch(e){return 'e';}})()";
        }

        private const string RestoreKeyHookExpression =
            "(function(){try{var I=window.SteamClient&&window.SteamClient.Input;" +
            "if(I&&I.__sixKeyHook){I.ControllerKeyboardSendText=I.__sixKeyOriginal;I.__sixKeyHook=false;}}" +
            "catch(e){}return 'r';})()";

        private const string ProbeExpression =
            "(document.querySelector('[data-key=VKMove],[data-key=VKPaste],[data-key=SwitchKeys_Steam]')?'1':'')";

        private readonly CdpClient _cdp;
        private readonly string _payload;
        private readonly string _installedCheck;
        private readonly string _keyHookExpression;
        private readonly Dictionary<string, UiCheck> _uiChecks = new Dictionary<string, UiCheck>();
        private long _lastKeyHookSweep;
        private long _lastUnavailableLog;

        private sealed class UiCheck
        {
            public long LastMs;
            public bool IsOsk;
        }

        public VirtualKeyboardInjector(CdpClient cdp, string payload, string token)
        {
            _cdp = cdp;
            _payload = payload;
            _installedCheck = BuildInstalledCheck(payload, token);
            _keyHookExpression = BuildKeyHookExpression(token);
        }

        /// <summary>Injects the payload into every page that is not yet installed.</summary>
        public void InjectAll()
        {
            IList<CdpTarget> targets;
            try
            {
                targets = _cdp.GetTargets();
            }
            catch (Exception exception)
            {
                // Steam not running or the debug port is closed. Do not spam
                // the log once per second; a reminder every minute is enough.
                long stamp = NowMs();
                if (stamp - _lastUnavailableLog >= 60000)
                {
                    _lastUnavailableLog = stamp;
                    Log.Warn("cdp unavailable: " + exception.Message);
                }

                return;
            }

            long now = NowMs();
            bool sweepKeyHook = now - _lastKeyHookSweep >= 5000;
            if (sweepKeyHook)
            {
                _lastKeyHookSweep = now;
            }

            var live = new HashSet<string>();

            foreach (CdpTarget target in targets)
            {
                if (!IsPage(target))
                {
                    continue;
                }

                live.Add(target.WebSocketUrl);

                if (sweepKeyHook)
                {
                    EnsureKeyHook(target);
                }

                UiCheck check;
                if (!_uiChecks.TryGetValue(target.WebSocketUrl, out check))
                {
                    check = new UiCheck();
                    _uiChecks[target.WebSocketUrl] = check;
                }

                // Probe new targets immediately, then back off. A reloaded OSK
                // page keeps the same URL, so keep checking installed pages.
                long interval = (check.IsOsk || LooksLikeOsk(target)) ? 2000 : 10000;
                if (now - check.LastMs < interval)
                {
                    continue;
                }

                check.LastMs = now;

                try
                {
                    check.IsOsk = _cdp.Evaluate(target, ProbeExpression) == "1";
                    if (!check.IsOsk)
                    {
                        continue;
                    }

                    string installed = _cdp.Evaluate(target, _installedCheck);
                    if (installed == "u")
                    {
                        continue;
                    }

                    string result = _cdp.Evaluate(target, _payload);
                    if (result != null && result.IndexOf("installed", StringComparison.Ordinal) >= 0)
                    {
                        Log.Info(Describe(target) + " => " + result);
                    }
                }
                catch (Exception exception)
                {
                    Log.Error("inject " + Describe(target), exception);
                }
            }

            if (_uiChecks.Count > live.Count)
            {
                var stale = new List<string>();
                foreach (string url in _uiChecks.Keys)
                {
                    if (!live.Contains(url))
                    {
                        stale.Add(url);
                    }
                }

                foreach (string url in stale)
                {
                    _uiChecks.Remove(url);
                }
            }
        }

        /// <summary>Removes the injected UI from every page.</summary>
        public void RestoreAll()
        {
            _uiChecks.Clear();
            _lastKeyHookSweep = 0;

            IList<CdpTarget> targets;
            try
            {
                targets = _cdp.GetTargets();
            }
            catch (Exception)
            {
                return;
            }

            foreach (CdpTarget target in targets)
            {
                if (!IsPage(target))
                {
                    continue;
                }

                try
                {
                    _cdp.Evaluate(target, RestoreKeyHookExpression);
                    _cdp.Evaluate(target, RestoreExpression);
                }
                catch (Exception exception)
                {
                    Log.Error("restore " + Describe(target), exception);
                }
            }
        }

        private void EnsureKeyHook(CdpTarget target)
        {
            try
            {
                string result = _cdp.Evaluate(target, _keyHookExpression);
                if (result == "installed")
                {
                    Log.Info("key hook " + Describe(target));
                }
                else if (result == "refreshed")
                {
                    Log.Info("key hook refreshed " + Describe(target));
                }
            }
            catch (Exception exception)
            {
                Log.Error("key hook " + Describe(target), exception);
            }
        }

        private static long NowMs()
        {
            return DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
        }

        private static bool IsPage(CdpTarget target)
        {
            return target.Type == "page" && !string.IsNullOrEmpty(target.WebSocketUrl);
        }

        private static bool LooksLikeOsk(CdpTarget target)
        {
            if (string.IsNullOrEmpty(target.Title))
            {
                return false;
            }

            return target.Title.IndexOf("\uAC00\uC0C1", StringComparison.Ordinal) >= 0
                || target.Title.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0
                || target.Title.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Describe(CdpTarget target)
        {
            return target.Title.Length > 0 ? target.Title : target.Id;
        }

        private static string BuildInstalledCheck(string payload, string token)
        {
            Match match = Regex.Match(payload, @"var VERSION = (\d+)");
            if (!match.Success)
            {
                return "(window.__sixUi ? 'u' : '')";
            }

            // The version alone is not enough: a payload injected by a previous
            // process carries a dead token and must be replaced.
            return "(window.__sixUi && window.__sixUi.version === "
                 + match.Groups[1].Value
                 + " && window.__sixUi.token === '" + token + "' ? 'u' : '')";
        }
    }
}
