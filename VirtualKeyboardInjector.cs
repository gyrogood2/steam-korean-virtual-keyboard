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
            return "(function(){var I=window.SteamClient&&window.SteamClient.Input;" +
                "if(!I||typeof I.ControllerKeyboardSendText!=='function')return 'skip';" +
                "if(I.__sixKeyHook)return 'already';" +
                "var o=I.ControllerKeyboardSendText;" +
                "I.ControllerKeyboardSendText=function(t){" +
                "if(typeof t==='string'&&t.length===1&&/[A-Za-z]/.test(t)){" +
                "try{fetch('http://127.0.0.1:8793/key?t=" + token + "&c='+encodeURIComponent(t))" +
                ".then(function(r){if(!r.ok)o.call(I,t);})" +
                ".catch(function(){o.call(I,t);});}catch(e){o.call(I,t);}" +
                "return;}" +
                "return o.call(I,t);};" +
                "I.__sixKeyHook=true;I.__sixKeyOriginal=o;return 'installed';})()";
        }

        private const string RestoreKeyHookExpression =
            "(function(){try{var I=window.SteamClient&&window.SteamClient.Input;" +
            "if(I&&I.__sixKeyHook){I.ControllerKeyboardSendText=I.__sixKeyOriginal;I.__sixKeyHook=false;}}" +
            "catch(e){}return 'r';})()";

        private readonly CdpClient _cdp;
        private readonly string _payload;
        private readonly string _installedCheck;
        private readonly string _keyHookExpression;
        private readonly HashSet<string> _installedTargets = new HashSet<string>();
        private readonly HashSet<string> _keyHookTargets = new HashSet<string>();

        public VirtualKeyboardInjector(CdpClient cdp, string payload, string token)
        {
            _cdp = cdp;
            _payload = payload;
            _installedCheck = BuildInstalledCheck(payload);
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
            catch (Exception)
            {
                return; // Steam is not running or the debug port is closed.
            }

            foreach (CdpTarget target in targets)
            {
                if (!IsPage(target))
                {
                    continue;
                }

                EnsureKeyHook(target);

                if (_installedTargets.Contains(target.WebSocketUrl))
                {
                    continue;
                }

                try
                {
                    string installed = _cdp.Evaluate(target, _installedCheck);
                    if (installed == "u")
                    {
                        _installedTargets.Add(target.WebSocketUrl);
                        continue;
                    }

                    string result = _cdp.Evaluate(target, _payload);
                    if (result != null && result.IndexOf("installed", StringComparison.Ordinal) >= 0)
                    {
                        _installedTargets.Add(target.WebSocketUrl);
                        Log.Info(Describe(target) + " => " + result);
                    }
                }
                catch (Exception exception)
                {
                    Log.Error("inject " + Describe(target), exception);
                }
            }
        }

        /// <summary>Removes the injected UI from every page.</summary>
        public void RestoreAll()
        {
            _installedTargets.Clear();
            _keyHookTargets.Clear();

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
            if (_keyHookTargets.Contains(target.WebSocketUrl))
            {
                return;
            }

            try
            {
                string result = _cdp.Evaluate(target, _keyHookExpression);
                if (result == "installed" || result == "already")
                {
                    _keyHookTargets.Add(target.WebSocketUrl);
                    if (result == "installed")
                    {
                        Log.Info("key hook " + Describe(target));
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error("key hook " + Describe(target), exception);
            }
        }

        private static bool IsPage(CdpTarget target)
        {
            return target.Type == "page" && !string.IsNullOrEmpty(target.WebSocketUrl);
        }

        private static string Describe(CdpTarget target)
        {
            return target.Title.Length > 0 ? target.Title : target.Id;
        }

        private static string BuildInstalledCheck(string payload)
        {
            Match match = Regex.Match(payload, @"var VERSION = (\d+)");
            if (!match.Success)
            {
                return "(window.__sixUi ? 'u' : '')";
            }

            return "(window.__sixUi && window.__sixUi.version === "
                 + match.Groups[1].Value + " ? 'u' : '')";
        }
    }
}
