using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Requestarr.Core.Test.Qa.Web
{
    // A real browser for the automated web app checks: headless Edge or Chrome (QA_CHECKS_BROWSER, or the one installed),
    // driven over the DevTools protocol, so nothing has to be downloaded. Pages are read with JavaScript and photographed
    // as JPEG; the theme is the system's light or dark setting the page sees, which the web app follows on Auto.
    public sealed class HeadlessBrowser : IAsyncDisposable
    {
        private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

        private readonly Process _process;
        private readonly ClientWebSocket _socket;
        private readonly string _profile;
        private int _nextId;

        private HeadlessBrowser(Process process, ClientWebSocket socket, string profile)
        {
            _process = process;
            _socket = socket;
            _profile = profile;
        }

        public static async Task<HeadlessBrowser> StartAsync(int width = 1366, int height = 900)
        {
            var profile = Path.Combine(Path.GetTempPath(), "requestarr-check-browser-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profile);
            var start = new ProcessStartInfo(Executable())
            {
                Arguments = $"--headless=new --remote-debugging-port=0 --user-data-dir=\"{profile}\" --window-size={width},{height} --hide-scrollbars --no-first-run --no-default-browser-check --disable-extensions about:blank",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            var process = Process.Start(start);
            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            await socket.ConnectAsync(new Uri(await PageSocketAsync(profile)), CancellationToken.None);

            return new HeadlessBrowser(process, socket, profile);
        }

        public Task SetThemeAsync(string scheme) =>
            SendAsync("Emulation.setEmulatedMedia", new { features = new[] { new { name = "prefers-color-scheme", value = scheme } } });

        // Draws the page at this width, as a phone or a narrow window would (Sonarr's small-screen layout below 768).
        public Task SetWidthAsync(int width, int height = 900) =>
            SendAsync("Emulation.setDeviceMetricsOverride", new { width, height, deviceScaleFactor = 1, mobile = false });

        // Opens the address and waits until the page has loaded (after any redirect).
        public async Task GoAsync(string url)
        {
            await SendAsync("Page.navigate", new { url });
            await Task.Delay(300);
            await WaitForAsync("document.readyState === 'complete'");
        }

        public async Task<T> EvalAsync<T>(string expression)
        {
            var result = await SendAsync("Runtime.evaluate", new { expression, returnByValue = true, awaitPromise = true });

            if (result.TryGetProperty("exceptionDetails", out var error))
            {
                throw new InvalidOperationException($"The page's script failed: {error.GetRawText()}");
            }

            return result.GetProperty("result").TryGetProperty("value", out var value) ? value.Deserialize<T>() : default;
        }

        // Runs a statement for what it does (a click, a scroll), not for a value.
        public Task RunAsync(string statement) => EvalAsync<bool>($"(() => {{ {statement}; return true; }})()");

        // Runs a script that leaves the page (a form sent, a link followed) and waits for the next page to load.
        public async Task LeaveAsync(string expression)
        {
            await EvalAsync<bool>("window.__qaOldPage = true");
            await EvalAsync<object>($"setTimeout(() => {{ {expression} }}, 0)");
            await WaitForAsync("!window.__qaOldPage && document.readyState === 'complete'");
        }

        public async Task WaitForAsync(string condition, TimeSpan? timeout = null)
        {
            var until = DateTime.UtcNow + (timeout ?? Wait);

            while (DateTime.UtcNow < until)
            {
                if (await EvalAsync<bool>($"!!({condition})"))
                {
                    return;
                }

                await Task.Delay(250);
            }

            throw new TimeoutException($"The page never showed: {condition} (at {await EvalAsync<string>("location.href")})");
        }

        public async Task<byte[]> ScreenshotAsync()
        {
            var shot = await SendAsync("Page.captureScreenshot", new { format = "jpeg", quality = 80 });
            return Convert.FromBase64String(shot.GetProperty("data").GetString());
        }

        // Requests to these addresses wait, unanswered, until released or the page is left (the loading screen stays up
        // while the app's data is held).
        public Task HoldRequestsAsync(string urlPattern) => SendAsync("Fetch.enable", new { patterns = new[] { new { urlPattern } } });

        public Task ReleaseRequestsAsync() => SendAsync("Fetch.disable", new { });

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // The browser may already be gone.
            }

            _socket.Dispose();

            if (!_process.HasExited)
            {
                _process.Kill(true);
                await _process.WaitForExitAsync();
            }

            _process.Dispose();
            TryDelete(_profile);
        }

        private async Task<JsonElement> SendAsync(string method, object parameters)
        {
            var id = Interlocked.Increment(ref _nextId);
            var request = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
            await _socket.SendAsync(request, WebSocketMessageType.Text, true, CancellationToken.None);

            // Events (the network's, when it's watched) come in between; only the answer with this id counts.
            while (true)
            {
                using var message = JsonDocument.Parse(await ReceiveAsync());
                var root = message.RootElement;

                if (!root.TryGetProperty("id", out var answered) || answered.GetInt32() != id)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException($"{method}: {error.GetRawText()}");
                }

                return root.GetProperty("result").Clone();
            }
        }

        private async Task<byte[]> ReceiveAsync()
        {
            using var whole = new MemoryStream();
            var buffer = new byte[64 * 1024];
            WebSocketReceiveResult part;

            do
            {
                part = await _socket.ReceiveAsync(buffer, CancellationToken.None);
                whole.Write(buffer, 0, part.Count);
            }
            while (!part.EndOfMessage);

            return whole.ToArray();
        }

        // The browser writes the port it chose to its profile; the first page's socket is listed there.
        private static async Task<string> PageSocketAsync(string profile)
        {
            var portFile = Path.Combine(profile, "DevToolsActivePort");
            var until = DateTime.UtcNow + Wait;

            while (!File.Exists(portFile) || new FileInfo(portFile).Length == 0)
            {
                if (DateTime.UtcNow > until)
                {
                    throw new TimeoutException("The browser didn't start (no DevToolsActivePort).");
                }

                await Task.Delay(200);
            }

            var port = (await File.ReadAllLinesAsync(portFile))[0].Trim();
            using var http = new HttpClient();
            using var targets = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/json/list"));

            return targets.RootElement.EnumerateArray().First(t => t.GetProperty("type").GetString() == "page").GetProperty("webSocketDebuggerUrl").GetString();
        }

        private static string Executable()
        {
            var chosen = Environment.GetEnvironmentVariable("QA_CHECKS_BROWSER");
            var candidates = new[]
            {
                chosen,
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                "/usr/bin/google-chrome",
                "/usr/bin/chromium",
                "/usr/bin/chromium-browser",
                "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
            };

            return candidates.FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(c))
                ?? throw new FileNotFoundException("No Edge or Chrome found: set QA_CHECKS_BROWSER to a Chromium browser.");
        }

        private static void TryDelete(string folder)
        {
            for (var attempt = 0; attempt < 5 && Directory.Exists(folder); attempt++)
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (IOException)
                {
                    Thread.Sleep(500);
                }
                catch (UnauthorizedAccessException)
                {
                    Thread.Sleep(500);
                }
            }
        }
    }
}
