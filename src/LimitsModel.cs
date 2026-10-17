using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Web.Script.Serialization;

namespace CodexLimits {
    public static class Json {
        public static JavaScriptSerializer Serializer { get { return new JavaScriptSerializer(); } }
        public static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }
        public static object Get(Dictionary<string, object> map, string key) {
            object value; return map != null && map.TryGetValue(key, out value) ? value : null;
        }
        public static string Text(Dictionary<string, object> map, string key) { return Get(map, key) as string; }
        public static double? Number(Dictionary<string, object> map, string key) {
            object value = Get(map, key);
            if (!(value is int || value is long || value is decimal || value is double)) return null;
            double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsNaN(d) || double.IsInfinity(d) ? (double?)null : d;
        }
    }
    public class QuotaWindow {
        public double? Remaining;
        public int? Minutes;
        public DateTimeOffset? Reset;
        public string Label {
            get {
                if (!Minutes.HasValue) return "Период неизвестен";
                if (Minutes == 10080) return "Неделя";
                if (Minutes == 1440) return "Сутки";
                if (Minutes % 60 == 0) return (Minutes / 60) + " ч";
                return Minutes + " мин";
            }
        }
        public string Percent { get { return Remaining.HasValue ? Remaining.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%" : "—"; } }
        public static QuotaWindow Parse(object value) {
            var map = Json.Map(value); if (map == null) return null;
            double? used = Json.Number(map, "usedPercent"), minutes = Json.Number(map, "windowDurationMins"), reset = Json.Number(map, "resetsAt");
            var window = new QuotaWindow();
            if (used.HasValue) window.Remaining = Math.Max(0, Math.Min(100, 100 - used.Value));
            if (minutes.HasValue && minutes > 0 && minutes <= int.MaxValue) window.Minutes = (int)minutes.Value;
            if (reset.HasValue && reset > 0) {
                try { window.Reset = new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(reset.Value); }
                catch (ArgumentOutOfRangeException) { }
            }
            return window;
        }
        public string Countdown(DateTimeOffset now) {
            if (!Reset.HasValue) return "срок неизвестен";
            TimeSpan span = Reset.Value - now;
            if (span.TotalSeconds <= 0) return "ожидаем обновление";
            if (span.TotalDays >= 1) return ((int)span.TotalDays) + "д " + span.Hours + "ч";
            if (span.TotalHours >= 1) return ((int)span.TotalHours) + "ч " + span.Minutes + "м";
            return Math.Max(1, (int)Math.Ceiling(span.TotalMinutes)) + "м";
        }
        public string ResetMoscow {
            get { return Reset.HasValue ? Reset.Value.ToOffset(TimeSpan.FromHours(3)).ToString("dd.MM.yyyy, HH:mm", CultureInfo.InvariantCulture) + " МСК" : "Не предоставлено"; }
        }
    }
    public class LimitPool {
        public string Id, Name, Plan, CreditBalance;
        public bool UnlimitedCredits, Blocked;
        public readonly List<QuotaWindow> Windows = new List<QuotaWindow>();
        public static LimitPool Parse(Dictionary<string,object> map, string fallbackId) {
            var pool = new LimitPool { Id = Json.Text(map,"limitId") ?? fallbackId, Name = Json.Text(map,"limitName"), Plan = Json.Text(map,"planType") };
            foreach (string key in new[] {"primary", "secondary"}) {
                var window = QuotaWindow.Parse(Json.Get(map,key)); if (window != null) pool.Windows.Add(window);
            }
            var credits = Json.Map(Json.Get(map,"credits"));
            pool.CreditBalance = Json.Text(credits,"balance");
            pool.UnlimitedCredits = object.Equals(Json.Get(credits,"unlimited"),true);
            pool.Blocked = Json.Get(map,"rateLimitReachedType") != null || object.Equals(Json.Get(map,"spendControlReached"),true);
            return pool;
        }
    }
    public class Snapshot {
        public readonly List<LimitPool> Pools = new List<LimitPool>();
        public int? ResetsAvailable;
        public bool? UsageAllowed;
        public LimitPool Main { get { return Pools.Find(p => p.Id == "codex") ?? (Pools.Count > 0 ? Pools[0] : null); } }
        public QuotaWindow MainWindow {
            get {
                if (Main == null || Main.Windows.Count == 0) return null;
                // Show the quota that will constrain work first; retain unknown windows in details.
                QuotaWindow result = Main.Windows[0];
                foreach (var window in Main.Windows)
                    if (window.Remaining.HasValue && (!result.Remaining.HasValue || window.Remaining < result.Remaining)) result = window;
                return result;
            }
        }
        public static Snapshot Parse(string json) {
            var root = Json.Map(Json.Serializer.DeserializeObject(json));
            if (root == null) throw new FormatException("Ответ лимитов имеет неверный формат");
            var result = new Snapshot();
            var pools = Json.Map(Json.Get(root,"rateLimitsByLimitId"));
            if (pools != null) foreach (var pair in pools) {
                var map = Json.Map(pair.Value); if (map != null) result.Pools.Add(LimitPool.Parse(map,pair.Key));
            }
            if (result.Pools.Count == 0) {
                var legacy = Json.Map(Json.Get(root,"rateLimits"));
                if (legacy != null) result.Pools.Add(LimitPool.Parse(legacy,"codex"));
            }
            var resets = Json.Map(Json.Get(root,"rateLimitResetCredits"));
            var available = Json.Number(resets,"availableCount");
            if (available.HasValue && available >= 0 && available <= int.MaxValue) result.ResetsAvailable = (int)available.Value;
            object allowed = Json.Get(root,"ordinaryUsageAllowed");
            if (allowed is bool) result.UsageAllowed = (bool)allowed;
            return result;
        }
    }
    public static class Placement {
        public static double InferScale(int referenceHeight,bool text,double dpiScale) {
            if(referenceHeight<=0)return dpiScale;
            return Math.Max(.5,Math.Min(3.5,referenceHeight/(text?17d:28d)));
        }
        public static bool DictationControl(string name) {
            if(string.IsNullOrEmpty(name)||name.Length>90||name.IndexOf('\n')>=0)return false;
            string n=name.Trim().ToLowerInvariant();
            return n=="stop dictation"||n=="cancel dictation"||n=="finish dictation"||n=="stop recording"||n=="cancel recording"||
                n=="остановить диктовку"||n=="отменить диктовку"||n=="завершить диктовку"||n=="закончить диктовку"||n=="остановить запись"||n=="отменить запись";
        }
        public static Rectangle VisibleBadge(Rectangle left,Rectangle right,double scale,bool dictation) {
            return dictation?Rectangle.Empty:Between(left,right,scale);
        }
        public static Rectangle Between(Rectangle left, Rectangle right, double scale) {
            int margin = (int)Math.Ceiling(8 * scale), width = Math.Min((int)(280*scale), right.Left-left.Right-2*margin);
            if (width < 145*scale || Math.Abs(left.Top+left.Height/2-right.Top-right.Height/2) > 24*scale) return Rectangle.Empty;
            int height = (int)(26*scale);
            return new Rectangle(left.Right+(right.Left-left.Right-width)/2, right.Top+(right.Height-height)/2, width,height);
        }
    }
}
