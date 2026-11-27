using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Common;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.SessionState;

namespace SampleWeb
{
    internal static class Json
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static void Write(HttpContext context, object value)
        {
            context.Response.ContentType = "application/json";
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Write(Serializer.Serialize(value));
        }
    }

    /// <summary>
    /// /api/session?action=... : needs exclusive (read/write) session access.
    /// </summary>
    public class SessionApiHandler : IHttpHandler, IRequiresSessionState
    {
        public bool IsReusable => true;

        public void ProcessRequest(HttpContext context)
        {
            var session = context.Session;
            var started = DateTime.UtcNow;
            var action = context.Request["action"] ?? "info";

            switch (action)
            {
                case "increment":
                    session["counter"] = ((session["counter"] as int?) ?? 0) + 1;
                    break;

                case "set":
                    session[context.Request["key"] ?? "key"] = context.Request["value"] ?? "";
                    break;

                case "big":
                    // ~200 KB value: checks large session data round-trips (BLOB / varbinary(max)).
                    session["big"] = new string('x', 200 * 1024);
                    break;

                case "slow":
                    // Holds the exclusive session lock while sleeping, so concurrent calls must queue.
                    session["counter"] = ((session["counter"] as int?) ?? 0) + 1;
                    Thread.Sleep(ParseInt(context.Request["ms"], 2000));
                    break;

                case "timeout":
                    session.Timeout = ParseInt(context.Request["minutes"], 20);
                    break;

                case "abandon":
                    session.Abandon();
                    Json.Write(context, new { action, abandoned = true, sessionId = session.SessionID });
                    return;

                case "info":
                    break;

                default:
                    context.Response.StatusCode = 400;
                    Json.Write(context, new { error = "unknown action " + action });
                    return;
            }

            Json.Write(context, Describe(session, action, started));
        }

        internal static object Describe(HttpSessionState session, string action, DateTime started)
        {
            var values = new Dictionary<string, object>();
            foreach (string key in session.Keys)
            {
                var value = session[key];
                values[key] = value is string s && s.Length > 100 ? $"<string of {s.Length} chars>" : value;
            }

            return new
            {
                action,
                sessionId = session.SessionID,
                isNewSession = session.IsNewSession,
                isReadOnly = session.IsReadOnly,
                timeoutMinutes = session.Timeout,
                mode = session.Mode.ToString(),
                values,
                startedUtc = started.ToString("HH:mm:ss.fff"),
                finishedUtc = DateTime.UtcNow.ToString("HH:mm:ss.fff")
            };
        }

        private static int ParseInt(string value, int fallback) =>
            int.TryParse(value, out var n) ? n : fallback;
    }

    /// <summary>
    /// /api/readonly : read-only session access (like EnableSessionState="ReadOnly").
    /// </summary>
    public class ReadOnlySessionHandler : IHttpHandler, IReadOnlySessionState
    {
        public bool IsReusable => true;

        public void ProcessRequest(HttpContext context)
        {
            Json.Write(context, SessionApiHandler.Describe(context.Session, "readonly", DateTime.UtcNow));
        }
    }

    /// <summary>
    /// /api/rows : lists rows in the session table (diagnostics for this sample only;
    /// never expose session data like this in a real app).
    /// </summary>
    public class SessionRowsHandler : IHttpHandler
    {
        public bool IsReusable => true;

        public void ProcessRequest(HttpContext context)
        {
            var settings = ConfigurationManager.ConnectionStrings["SessionState"];
            var factory = DbProviderFactories.GetFactory(settings.ProviderName);
            var rows = new List<object>();

            using (var conn = factory.CreateConnection())
            {
                conn.ConnectionString = settings.ConnectionString;
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT SessionId, ApplicationName, Created, Expires, LockDate, LockId, Timeout, Locked, Flags, SessionItems FROM AspNetSessionState ORDER BY Created";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            rows.Add(new
                            {
                                sessionId = r.GetString(0),
                                applicationName = r.GetString(1),
                                createdUtc = r.GetDateTime(2).ToString("yyyy-MM-dd HH:mm:ss"),
                                expiresUtc = r.GetDateTime(3).ToString("yyyy-MM-dd HH:mm:ss"),
                                lockDateUtc = r.GetDateTime(4).ToString("yyyy-MM-dd HH:mm:ss"),
                                lockId = Convert.ToInt32(r.GetValue(5)),
                                timeout = Convert.ToInt32(r.GetValue(6)),
                                locked = Convert.ToInt32(r.GetValue(7)) != 0,
                                flags = Convert.ToInt32(r.GetValue(8)),
                                itemsBytes = r.IsDBNull(9) ? 0 : ((byte[])r.GetValue(9)).Length
                            });
                        }
                    }
                }
            }

            Json.Write(context, new { nowUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), rows });
        }
    }
}
