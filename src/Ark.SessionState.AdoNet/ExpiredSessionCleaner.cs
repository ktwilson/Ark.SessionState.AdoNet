using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Ark.SessionState.AdoNet
{
    /// <summary>
    /// Runs expired-session cleanup on a background timer. ASP.NET creates one provider
    /// instance per HttpApplication instance, so timers are shared per key (connection
    /// string + table) to avoid every instance running its own cleanup.
    /// </summary>
    internal static class ExpiredSessionCleaner
    {
        private static readonly ConcurrentDictionary<string, Lazy<Timer>> Timers =
            new ConcurrentDictionary<string, Lazy<Timer>>(StringComparer.Ordinal);

        public static void EnsureScheduled(string key, TimeSpan interval, Action cleanup, Action<Exception> onError)
        {
            var lazy = Timers.GetOrAdd(key, _ => new Lazy<Timer>(() => Start(interval, cleanup, onError)));
            _ = lazy.Value;
        }

        private static Timer Start(TimeSpan interval, Action cleanup, Action<Exception> onError)
        {
            var running = 0;

            void Tick(object state)
            {
                // Skip a tick if the previous one is still running.
                if (Interlocked.Exchange(ref running, 1) == 1)
                    return;
                try
                {
                    cleanup();
                }
                catch (Exception ex)
                {
                    try { onError(ex); } catch { /* never let the timer thread throw */ }
                }
                finally
                {
                    Interlocked.Exchange(ref running, 0);
                }
            }

            // First run shortly after startup so the app's first request isn't slowed down.
            return new Timer(Tick, null, TimeSpan.FromSeconds(5), interval);
        }
    }
}
