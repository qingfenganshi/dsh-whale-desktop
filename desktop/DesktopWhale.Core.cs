// =============================================================================
// 本文件是衍生作品的新增部分（原上游项目无此文件）
//   上游项目：DeepSeek-Balance-Whale-Widget  https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget
//   原作者：MeteorNOX    原许可证：MIT License
//   本项目同样以 MIT 发布，原版权声明与完整许可见 LICENSE。
// =============================================================================using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace DesktopWhale
{
    // ===== 通用：配置文件读写 =====
    internal static class Ini
    {
        public static Dictionary<string, string> Read(string file)
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(file)) return d;
                foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
            return d;
        }
        public static void Write(string file, string key, string value)
        {
            try
            {
                var d = Read(file);
                d[key] = value ?? "";
                var sb = new StringBuilder();
                foreach (var kv in d) sb.AppendLine(kv.Key + "=" + kv.Value);
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    internal static class Cfg
    {
        public static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string KeyFile = Path.Combine(Dir, "apikey.txt");
        public static readonly string CfgFile = Path.Combine(Dir, "config.ini");
        public static readonly string HistFile = Path.Combine(Dir, "chat-history.json");
        public static readonly string DshHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
        public static readonly string Ledger = Path.Combine(DshHome, ".dshw-usage.json");
        public static readonly string SizeCfg = Path.Combine(DshHome, ".dshw-size.json");

        public const string DshProcessName = "DeepSeek Harness";

        public static string ApiKey
        {
            get { try { return File.Exists(KeyFile) ? File.ReadAllText(KeyFile, Encoding.UTF8).Trim() : ""; } catch { return ""; } }
            set
            {
                try
                {
                    if (string.IsNullOrEmpty(value)) { if (File.Exists(KeyFile)) File.Delete(KeyFile); }
                    else File.WriteAllText(KeyFile, value.Trim(), new UTF8Encoding(false));
                }
                catch { }
            }
        }

        public static string Get(string key, string def)
        {
            var d = Ini.Read(CfgFile);
            return d.ContainsKey(key) && !string.IsNullOrEmpty(d[key]) ? d[key] : def;
        }
        public static void Set(string key, string value) { Ini.Write(CfgFile, key, value); }

        public static string Skin { get { return Get("skin", "whale1"); } set { Set("skin", value); } }
        public static bool CtxMain { get { return Get("ctxmain", "0") == "1"; } set { Set("ctxmain", value ? "1" : "0"); } }
        public static double Scale { get { double v; return double.TryParse(Get("scale", "1"), out v) ? v : 1; } set { Set("scale", value.ToString("0.00")); } }

        // v13：对话带多少轮历史（1 轮 = 一问一答）。0 = 只发当前问题（最省 token）
        // v22：推理等级 off / low / high
        public static string ReasonLevel
        {
            get
            {
                string v = Get("reason", "off");
                return (v == "low" || v == "high") ? v : "off";
            }
            set { Set("reason", (value == "low" || value == "high") ? value : "off"); }
        }

        public static int HistoryRounds
        {
            get
            {
                int v;
                if (int.TryParse(Get("historyRounds", "5"), out v)) return v < 0 ? 0 : (v > 50 ? 50 : v);
                return 5;
            }
            set { Set("historyRounds", Math.Max(0, Math.Min(50, value)).ToString()); }
        }

        // 音效组跟随 DSH 插件：duck(小黄鸭) / set1(音效1)
        public static string SoundSet
        {
            get
            {
                try
                {
                    var d = Ini.Read(SizeCfg);
                    if (d.ContainsKey("soundSet")) return d["soundSet"];
                }
                catch { }
                return "duck";
            }
        }
        public static double Volume
        {
            get
            {
                try
                {
                    var d = Ini.Read(SizeCfg);
                    if (d.ContainsKey("vol")) { double v; if (double.TryParse(d["vol"], out v)) return Math.Max(0, Math.Min(1, v)); }
                }
                catch { }
                return 0.9;
            }
        }
        public static bool SoundOn
        {
            get
            {
                try
                {
                    var d = Ini.Read(SizeCfg);
                    if (d.ContainsKey("sound")) return d["sound"] == "True" || d["sound"] == "true" || d["sound"] == "1";
                }
                catch { }
                return true;
            }
        }

        public static string SkinFile()
        {
            string p = Path.Combine(Dir, "skins", Skin + ".png");
            if (File.Exists(p)) return p;
            p = Path.Combine(Dir, Skin + ".png");
            if (File.Exists(p)) return p;
            return Path.Combine(Dir, "whale.png");
        }
    }

    // ===== 余额数据（直接读 DSH 插件的账本，口径与插件一致）=====
    internal class BalanceInfo
    {
        public double LastBalance = double.NaN;
        public double TodayUsage = double.NaN;
        public string Date = "";
        public string Error = "";
    }

    internal static class Ledger
    {
        public static BalanceInfo Read()
        {
            var b = new BalanceInfo();
            try
            {
                if (!File.Exists(Cfg.Ledger)) { b.Error = "没有找到账本"; return b; }
                string json = File.ReadAllText(Cfg.Ledger, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                var d = ser.Deserialize<Dictionary<string, object>>(json);
                if (d == null) { b.Error = "账本格式异常"; return b; }
                if (d.ContainsKey("lastBalance")) { double v; if (double.TryParse(Convert.ToString(d["lastBalance"]), out v)) b.LastBalance = v; }
                if (d.ContainsKey("todayUsage")) { double v; if (double.TryParse(Convert.ToString(d["todayUsage"]), out v)) b.TodayUsage = v; }
                if (d.ContainsKey("date")) b.Date = Convert.ToString(d["date"]);
            }
            catch (Exception ex) { b.Error = ex.Message; }
            return b;
        }
    }

    // ===== 峰谷时段（与插件同一口径：北京时间工作日 9-12、14-18 为高峰）=====
    internal static class Peak
    {
        public static bool IsPeak(out DateTime bj, out TimeSpan next)
        {
            var utc = DateTime.UtcNow;
            bj = utc.AddHours(8);
            next = TimeSpan.Zero;
            bool workday = bj.DayOfWeek != DayOfWeek.Saturday && bj.DayOfWeek != DayOfWeek.Sunday;
            bool peak = workday && ((bj.Hour >= 9 && bj.Hour < 12) || (bj.Hour >= 14 && bj.Hour < 18));
            if (peak)
            {
                DateTime end = bj.Hour < 12 ? bj.Date.AddHours(12) : bj.Date.AddHours(18);
                next = end - bj;
            }
            else
            {
                // 计算下一个高峰开始
                DateTime t = bj;
                for (int i = 0; i < 200; i++)
                {
                    if (t.DayOfWeek != DayOfWeek.Saturday && t.DayOfWeek != DayOfWeek.Sunday)
                    {
                        DateTime s1 = t.Date.AddHours(9), s2 = t.Date.AddHours(14);
                        if (t < s1) { next = s1 - t; break; }
                        if (t >= s1 && t < s1.AddHours(3)) { next = TimeSpan.Zero; peak = true; break; }
                        if (t >= s1.AddHours(3) && t < s2) { next = s2 - t; break; }
                    }
                    t = t.Date.AddDays(1);
                    next = t.AddHours(9) - bj;
                }
            }
            return peak;
        }

        public static string Describe()
        {
            DateTime bj; TimeSpan next;
            bool peak = IsPeak(out bj, out next);
            string tail = next.TotalSeconds > 0
                ? " · " + (next.Days > 0 ? next.Days + "天" : "") + next.Hours + ":" + next.Minutes.ToString("00")
                : "";
            return (peak ? "高峰" : "空闲") + tail;
        }
    }

    // ===== DeepSeek 对话 =====
    internal static class Api
    {
        // v26：调试日志（写文件，便于在真机上排查"没有回复"的问题）
        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(Cfg.Dir, "debug.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };

        public static async Task ChatAsync(string key, List<ChatTurn> turns, string ctxNote, Action<string> onDelta, Action onDone, Action<string> onError, Action onThinking = null)
        {
            try
            {
                var payload = new Dictionary<string, object>();
                // v24（真机根因）：上一版这里写成了 "if (high) payload[model]=..."，
                //   **非 high 档根本没有设置 model** ⇒ 请求缺字段 ⇒ 返回空回复（"小鲸鱼："后面什么都没有）。
                //   现在改成无论哪档都一定赋值。
                // 实测 /models：本账号只有 deepseek-flash 和 deepseek-v4-pro
                string level = Cfg.ReasonLevel;
                payload["model"] = (level == "high") ? "deepseek-v4-pro" : "deepseek-flash";
                payload["stream"] = true;
                payload["temperature"] = level == "low" ? 0.3 : 0.7;
                // v25（真机根因）：deepseek-flash 是**推理模型**，回复前会先输出 reasoning_content 思考。
                //   max_tokens 太小（800）时，额度全被思考吃掉 ⇒ content 为 null ⇒ 界面上"小鲸鱼："后一片空白。
                //   实测只有 ≥2000 才能稳定拿到最终回答。
                payload["max_tokens"] = level == "high" ? 4000 : (level == "low" ? 3000 : 2500);
                // 历史轮数由 Cfg.HistoryRounds 决定（0 = 只发当前这一问，最省）
                payload["messages"] = BuildMessages(LimitedTurns(turns), ctxNote);
                var ser = new JavaScriptSerializer();
                var req = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                req.Content = new StringContent(ser.Serialize(payload), Encoding.UTF8, "application/json");

                using (var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        int code = (int)resp.StatusCode;
                        string msg = code == 401 ? "API Key 无效或已过期（401）"
                            : code == 402 ? "账户余额不足（402）"
                            : code == 429 ? "请求太频繁，稍后再试（429）"
                            : "DeepSeek 返回 HTTP " + code;
                        onError(msg);
                        return;
                    }
                    using (var stream = await resp.Content.ReadAsStreamAsync())
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        Log("开始读流，model=" + payload["model"] + " max_tokens=" + payload["max_tokens"]);
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:")) continue;
                            if (line.IndexOf("content\":\"", StringComparison.Ordinal) >= 0) Log("收到内容行");
                            string data = line.Substring(5).Trim();
                            if (data == "[DONE]") break;
                            try
                            {
                                // v28（真机定位）：JavaScriptSerializer 反序列化嵌套 delta 时把异常吞掉了
                                //   （catch {} 兜住了 ⇒ onDelta 从未被调用 ⇒ 界面永远空白）。
                                //   改成**直接用正则从原始 JSON 里取** "content":"xxx"，稳且不依赖序列化器。
                                var mc = System.Text.RegularExpressions.Regex.Match(
                                    data,
                                    "\"delta\"\\s*:\\s*\\{[^}]*?\"content\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                                if (mc.Success)
                                {
                                    string s = UnescapeJson(mc.Groups[1].Value);
                                    if (!string.IsNullOrEmpty(s)) onDelta(s);
                                }
                                else if (onThinking != null && data.IndexOf("reasoning_content", StringComparison.Ordinal) >= 0)
                                {
                                    onThinking();
                                }
                            }
                            catch (Exception pe) { Log("解析异常：" + pe.Message); }
                        }
                    }
                }
                onDone();
            }
            catch (Exception ex) { Log("ChatAsync 异常：" + ex); onError("网络异常：" + ex.Message); }
        }

        // v28：把 JSON 字符串里的转义还原（\n \" \\ \uXXXX 等）
        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        // v13：按配置只取最近 N 轮（1 轮 = 一问一答）
        private static List<ChatTurn> LimitedTurns(List<ChatTurn> turns)
        {
            int rounds = Cfg.HistoryRounds;
            if (rounds <= 0) return new List<ChatTurn>();      // 0 = 只发当前这一问
            int maxMsgs = rounds * 2;
            if (turns.Count <= maxMsgs) return new List<ChatTurn>(turns);
            return turns.GetRange(turns.Count - maxMsgs, maxMsgs);
        }

        private static object[] BuildMessages(List<ChatTurn> turns, string ctxNote)
        {
            var list = new List<object>();
            var sys = new Dictionary<string, object>();
            sys["role"] = "system";
            string content = "你是桌面上的小鲸鱼助手，性格温和、简短、直接。回答尽量控制在 120 字以内，用中文回答。";
            if (!string.IsNullOrEmpty(ctxNote)) content += "\n" + ctxNote;
            sys["content"] = content;
            list.Add(sys);
            foreach (var t in turns)
            {
                var m = new Dictionary<string, object>();
                m["role"] = t.Role;
                m["content"] = t.Text;
                list.Add(m);
            }
            return list.ToArray();
        }
    }

    internal class ChatTurn { public string Role; public string Text; }

    // ===== 历史存取（chat-history.json）=====
    internal static class Hist
    {
        public static List<ChatTurn> Load()
        {
            try
            {
                if (!File.Exists(Cfg.HistFile)) return new List<ChatTurn>();
                var ser = new JavaScriptSerializer();
                var arr = ser.Deserialize<List<Dictionary<string, string>>>(File.ReadAllText(Cfg.HistFile, Encoding.UTF8));
                var list = new List<ChatTurn>();
                if (arr != null)
                    foreach (var d in arr)
                        list.Add(new ChatTurn { Role = d.ContainsKey("r") ? d["r"] : "user", Text = d.ContainsKey("t") ? d["t"] : "" });
                return list;
            }
            catch { return new List<ChatTurn>(); }
        }

        public static void Save(List<ChatTurn> turns)
        {
            try
            {
                var arr = new List<Dictionary<string, string>>();
                int start = Math.Max(0, turns.Count - 60);
                for (int i = start; i < turns.Count; i++)
                    arr.Add(new Dictionary<string, string> { { "r", turns[i].Role }, { "t", turns[i].Text } });
                var ser = new JavaScriptSerializer();
                File.WriteAllText(Cfg.HistFile, ser.Serialize(arr), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ===== 音效（复用插件的 mp3/wav）=====
    // 不走 WPF（本机 .NET Framework 没有 WPF 引用程序集），改用 Windows Media 的 mciSendString：
    // 自带 mp3 解码、零依赖、能并发，够用了。
    internal static class Sfx
    {
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string cmd, StringBuilder ret, int retLen, IntPtr hwnd);

        private static bool warmed;

        private static string PressFile()
        {
            string name = Cfg.SoundSet == "set1" ? "D1.mp3" : "Ya1.mp3";
            string p = Path.Combine(Cfg.Dir, name);
            return File.Exists(p) ? p : Path.Combine(Cfg.Dir, "Ya1.mp3");
        }
        private static string ReleaseFile()
        {
            string name = Cfg.SoundSet == "set1" ? "D2.mp3" : "Ya2.mp3";
            string p = Path.Combine(Cfg.Dir, name);
            return File.Exists(p) ? p : Path.Combine(Cfg.Dir, "Ya2.mp3");
        }

        private static void Mci(string cmd)
        {
            try { mciSendString(cmd, null, 0, IntPtr.Zero); } catch { }
        }

        public static void Warm()
        {
            if (warmed) return;
            warmed = true;
            try
            {
                Mci("close dshw_press");
                Mci("open \"" + PressFile() + "\" type mpegvideo alias dshw_press");
                Mci("close dshw_release");
                Mci("open \"" + ReleaseFile() + "\" type mpegvideo alias dshw_release");
            }
            catch { }
        }

        private static void Play(string alias, double vol)
        {
            try
            {
                int v = (int)Math.Round(Math.Max(0, Math.Min(1, vol)) * 1000);
                Mci("setaudio " + alias + " volume to " + v);
                Mci("seek " + alias + " to start");
                Mci("play " + alias);
            }
            catch { }
        }

        public static void PlayPress()
        {
            if (!Cfg.SoundOn) return;
            Warm();
            Play("dshw_press", Cfg.Volume);
        }

        public static void PlayRelease()
        {
            if (!Cfg.SoundOn) return;
            Warm();
            Play("dshw_release", Cfg.Volume);
        }

        public static void PlayTaskEnd()
        {
            if (!Cfg.SoundOn) return;
            try
            {
                string p = Path.Combine(Cfg.Dir, "task-end.wav");
                if (File.Exists(p)) new SoundPlayer(p).Play();
            }
            catch { }
        }
    }
    // ===== DSH 进程探测 =====
    internal static class DshWatcher
    {
        public static int FindDshPid()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(Cfg.DshProcessName)) return p.Id;
            }
            catch { }
            return 0;
        }
    }

    // ===== 入口 =====
    // ===== 入口：支持两种模式 =====
    //   (无参数)    → 直接显示鲸鱼（手动运行）
    //   --pending  → 开机自启用：先不显示，等 DeepSeek 客户端出现再显示；
    //                 DeepSeek 一旦退出，**整个鲸鱼程序也退出**（与客户端同生命周期）
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool pending = false;
            if (args != null)
                foreach (var a in args)
                    if (string.Equals(a, "--pending", StringComparison.OrdinalIgnoreCase)) pending = true;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (pending) Application.Run(new PendingHost());
                else Application.Run(new WhaleHost(true));
            }
            catch (Exception ex) { MessageBox.Show("启动失败：" + ex.Message, "桌面小鲸鱼"); }
        }

        // 供 PendingHost 显示鲸鱼时用（同一个消息循环）
        public static void RunFromPending()
        {
            Application.Run(new WhaleHost(true));
        }
    }

    // 等待 DeepSeek 出现；一旦出现就显示鲸鱼，并且此后 DeepSeek 一退出就整体退出
    public class PendingHost : ApplicationContext
    {
        private readonly System.Windows.Forms.Timer timer;
        private WhaleHost host;
        private int ticks;

        public PendingHost()
        {
            timer = new System.Windows.Forms.Timer { Interval = 1500 };
            timer.Tick += (s, e) => Check();
            timer.Start();
            Check();
        }

        private void Check()
        {
            try
            {
                if (DshWatcher.FindDshPid() != 0)
                {
                    timer.Stop();
                    host = new WhaleHost(true);
                    return;
                }
            }
            catch { }
            ticks++;
            // 30 分钟内等不到就退出，避免后台常驻太久
            if (ticks > 1200) { timer.Stop(); ExitThread(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (timer != null) { timer.Stop(); timer.Dispose(); }
            base.Dispose(disposing);
        }
    }
    public class WhaleHost : ApplicationContext
    {
        private WhaleForm whale;
        private ChatBubble bubble;
        private NotifyIcon tray;
        private System.Windows.Forms.Timer follow;

        public WhaleHost(bool watchDsh)
        {
            EnsureWhale();
            tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "桌面小鲸鱼", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("打开对话（双击鲸鱼）", null, (s, e) => ToggleBubble());
            menu.Items.Add("换皮肤（下一套）", null, (s, e) => { if (whale != null) whale.NextSkin(); });
            menu.Items.Add("缩小一点", null, (s, e) => { if (whale != null) { whale.ScaleBy(-0.1); whale.SavePosition(); } });
            menu.Items.Add("放大一点", null, (s, e) => { if (whale != null) { whale.ScaleBy(0.1); whale.SavePosition(); } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("回到屏幕中间", null, (s, e) => { if (whale != null) whale.ResetPosition(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => { tray.Visible = false; ExitThread(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (s, e) => ToggleBubble();

            // v21（用户要求）：与 DeepSeek 客户端**同生命周期**
            //   · 客户端在 → 鲸鱼在
            //   · 客户端退出 → 鲸鱼**整个程序退出**（不是隐藏）
            //   连续 3 次（约 6 秒）都查不到客户端才退出，避免启动/重启时的抖动误杀。
            if (watchDsh)
            {
                missing = 0;
                follow = new System.Windows.Forms.Timer { Interval = 2000 };
                follow.Tick += (s, e) =>
                {
                    bool alive = false;
                    try { alive = DshWatcher.FindDshPid() != 0; } catch { alive = true; }
                    if (alive) { missing = 0; return; }
                    missing++;
                    if (missing >= 3)
                    {
                        follow.Stop();
                        if (tray != null) tray.Visible = false;
                        if (bubble != null && !bubble.IsDisposed) bubble.Close();
                        ExitThread();
                    }
                };
                follow.Start();
            }
        }

        private int missing;

        private void EnsureWhale()
        {
            if (whale != null && !whale.IsDisposed) return;
            whale = new WhaleForm();
            whale.OpenChatRequested += () => ToggleBubble();
            whale.Show();
        }

        private void ToggleBubble()
        {
            EnsureWhale();
            if (bubble != null && !bubble.IsDisposed) { bubble.Close(); bubble = null; return; }
            bubble = new ChatBubble(whale);
            bubble.Closed += (s, e) => { bubble = null; };
            bubble.Show();
            bubble.PositionNearWhale(whale);
        }

        protected override void Dispose(bool disposing)
        {
            if (follow != null) { follow.Stop(); follow.Dispose(); }
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (bubble != null) bubble.Dispose();
            base.Dispose(disposing);
        }
    }

    // ===== 鲸鱼本体（拖动 + 双击/右键 + 按压音 + Q弹）=====
    // ===== 鲸鱼本体：分层窗口（Per-Pixel Alpha）画图，彻底没有紫边 =====
    //   · 用 UpdateLayeredWindow 把 PNG 的 alpha 通道直接交给系统 ⇒ 天然透明、无边缘残色
    //   · Q 弹用 GDI+ 变换在绘制里做真实缩放（不是改控件尺寸），效果跟插件一样
    //   · 位置/大小/皮肤都记忆在 config.ini
    public class WhaleForm : Form
    {
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;
        private const int ULW_ALPHA = 0x02;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private Bitmap whaleImg;
        private double squish;              // 0 = 正常, 1 = 最大压扁
        private System.Windows.Forms.Timer animTimer;
        private System.Windows.Forms.Timer posSaveTimer;
        private bool dragging, moved;
        private Point dragOffset, downPoint;
        private int doubleCount;
        private System.Windows.Forms.Timer doubleTimer;
        private int baseSize;

        public event Action OpenChatRequested;

        public WhaleForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            baseSize = 180;
            ClientSize = new Size(baseSize, baseSize);
            RestorePosition();
            LoadSkin();
            Sfx.Warm();

            MouseDown += OnDown;
            MouseMove += OnMove;
            MouseUp += OnUp;

            var ctx = new ContextMenuStrip();
            ctx.Items.Add("打开对话", null, (s, e) => { if (OpenChatRequested != null) OpenChatRequested(); });
            ctx.Items.Add("换皮肤（下一套）", null, (s, e) => NextSkin());
            ctx.Items.Add("放大一点", null, (s, e) => ScaleBy(0.1));
            ctx.Items.Add("缩小一点", null, (s, e) => ScaleBy(-0.1));
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("回到右下角", null, (s, e) => { ResetPosition(); SavePosition(); });
            ctx.Items.Add("退出", null, (s, e) => Application.Exit());
            ContextMenuStrip = ctx;

            animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            animTimer.Tick += (s, e) => AnimateTick();
            posSaveTimer = new System.Windows.Forms.Timer { Interval = 700 };
            posSaveTimer.Tick += (s, e) => { posSaveTimer.Stop(); SavePosition(); };
        }

        // ---------- 位置与尺寸记忆 ----------
        private void RestorePosition()
        {
            try
            {
                var d = Ini.Read(Cfg.CfgFile);
                int x = int.MinValue, y = int.MinValue, s = 0;
                bool has = false;
                if (d.ContainsKey("x") && d.ContainsKey("y"))
                {
                    has = int.TryParse(d["x"], out x) & int.TryParse(d["y"], out y);
                }
                if (d.ContainsKey("size") && int.TryParse(d["size"], out s) && s >= 90 && s <= 400)
                {
                    baseSize = s;
                }
                // v12：构造期间设 ClientSize 可能被 WinForms 覆盖 ⇒ 显示后再强制应用一次（见 OnShown）
                ClientSize = new Size(baseSize, baseSize);
                if (has)
                {
                    var wa = Screen.PrimaryScreen.WorkingArea;
                    if (x > wa.Left - 50 && x < wa.Right - 60 && y > wa.Top - 50 && y < wa.Bottom - 60)
                    {
                        Location = new Point(x, y);
                        return;
                    }
                }
            }
            catch { }
            ResetPosition();
        }

        // 首次显示后，用保存值再校正一次尺寸/位置（避免构造期被默认值覆盖）
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                var d = Ini.Read(Cfg.CfgFile);
                int s;
                if (d.ContainsKey("size") && int.TryParse(d["size"], out s) && s >= 90 && s <= 400)
                {
                    baseSize = s;
                    Size = new Size(s, s);
                }
                int x, y;
                if (d.ContainsKey("x") && d.ContainsKey("y")
                    && int.TryParse(d["x"], out x) && int.TryParse(d["y"], out y))
                {
                    Location = new Point(x, y);
                }
                Render();
            }
            catch { }
        }

        public void SavePosition()
        {
            try
            {
                Cfg.Set("x", Location.X.ToString());
                Cfg.Set("y", Location.Y.ToString());
                SaveSize();
            }
            catch { }
        }

        // 尺寸单独保存，避免被其它键的写入覆盖
        private void SaveSize()
        {
            try { Cfg.Set("size", ClientSize.Width.ToString()); } catch { }
        }

        public void ResetPosition()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - ClientSize.Width - 40, wa.Bottom - ClientSize.Height - 40);
        }

        public void ScaleBy(double delta)
        {
            int s = (int)Math.Max(90, Math.Min(400, ClientSize.Width + delta * 180));
            baseSize = s;
            ClientSize = new Size(s, s);
            SaveSize();          // 先写尺寸
            SavePosition();      // 再写位置（位置随尺寸变化）
            Render();
        }

        // ---------- 皮肤 ----------
        private static readonly string[] SkinOrder = { "whale1", "whale2", "whale3", "whale4" };

        public void LoadSkin()
        {
            try
            {
                string p = Cfg.SkinFile();
                if (File.Exists(p))
                {
                    using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read))
                    using (var tmp = Image.FromStream(fs))
                    {
                        if (whaleImg != null) whaleImg.Dispose();
                        whaleImg = new Bitmap(tmp);
                    }
                }
            }
            catch { }
            if (whaleImg == null)
            {
                whaleImg = new Bitmap(140, 140);
                using (var g = Graphics.FromImage(whaleImg))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillEllipse(new SolidBrush(Color.FromArgb(77, 107, 254)), 4, 4, 132, 132);
                }
            }
            Render();
        }

        public void NextSkin()
        {
            int idx = Array.IndexOf(SkinOrder, Cfg.Skin);
            idx = (idx + 1) % SkinOrder.Length;
            Cfg.Skin = SkinOrder[idx];
            LoadSkin();
        }

        // ---------- Q 弹动画 ----------
        private void Pulse()
        {
            squish = 1.0;
            if (!animTimer.Enabled) animTimer.Start();
        }

        private void AnimateTick()
        {
            // 弹簧回弹：squish 从 1 衰减到 0，并带一点过冲（QQ弹弹的感觉）
            squish -= 0.09;
            if (squish <= -0.12) { squish = 0; animTimer.Stop(); }
            Render();
        }

        // ---------- 绘制到分层窗口 ----------
        public void Render()
        {
            try
            {
                if (!IsHandleCreated) return;
                int w = ClientSize.Width, h = ClientSize.Height;
                using (var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        // 压扁效果：宽拉大、高压缩，底部对齐（像按一个气球）
                        double sq = Math.Max(0, squish);
                        double sx = 1 + 0.12 * sq;
                        double sy = 1 - 0.18 * sq;
                        double dw = w * sx, dh = h * sy;
                        double dx = (w - dw) / 2, dy = h - dh;
                        // v6：贴屏幕左半边时镜像翻转（与插件「贴左自动翻转」一致）
                        double centerX = Left + ClientSize.Width / 2.0;
                        double screenMid = Screen.FromPoint(new Point(Left + ClientSize.Width / 2, Top + ClientSize.Height / 2)).Bounds.Left
                                         + Screen.FromPoint(new Point(Left + ClientSize.Width / 2, Top + ClientSize.Height / 2)).Bounds.Width / 2.0;
                        bool flip = centerX < screenMid;
                        if (flip)
                        {
                            var state = g.Save();
                            g.TranslateTransform((float)(dx + dw / 2), 0f);
                            g.ScaleTransform(-1f, 1f);
                            g.TranslateTransform((float)(-(dx + dw / 2)), 0f);
                            g.DrawImage(whaleImg, (float)dx, (float)dy, (float)dw, (float)dh);
                            g.Restore(state);
                        }
                        else
                        {
                            g.DrawImage(whaleImg, (float)dx, (float)dy, (float)dw, (float)dh);
                        }
                    }

                    IntPtr screenDc = GetDC(IntPtr.Zero);
                    IntPtr memDc = CreateCompatibleDC(screenDc);
                    IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0, 0, 0, 0));
                    IntPtr oldBitmap = SelectObject(memDc, hBitmap);
                    var size = new SIZE(w, h);
                    var src = new POINT(0, 0);
                    var dst = new POINT(Left, Top);
                    var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
                    UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
                    SelectObject(memDc, oldBitmap);
                    DeleteObject(hBitmap);
                    DeleteDC(memDc);
                    ReleaseDC(IntPtr.Zero, screenDc);
                }
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Render(); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Render(); }
        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); Render(); }

        // 分层窗口里，鼠标命中由 alpha 决定；这里用简单矩形判定，并把透明处放行
        protected override void OnPaint(PaintEventArgs e) { /* 全部由 Render 绘制 */ }
        protected override void OnPaintBackground(PaintEventArgs e) { }

        // ---------- 交互 ----------
        private void OnDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragging = true; moved = false;
                dragOffset = e.Location; downPoint = e.Location;
                Sfx.PlayPress();
                Pulse();
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                Sfx.PlayPress();
                Pulse();
                CountDouble();
            }
        }

        private void CountDouble()
        {
            doubleCount++;
            if (doubleTimer != null) { doubleTimer.Stop(); doubleTimer.Dispose(); doubleTimer = null; }
            if (doubleCount >= 2)
            {
                doubleCount = 0;
                if (OpenChatRequested != null) OpenChatRequested();
                return;
            }
            doubleTimer = new System.Windows.Forms.Timer { Interval = 420 };
            doubleTimer.Tick += (s, ev) => { doubleCount = 0; doubleTimer.Stop(); doubleTimer.Dispose(); doubleTimer = null; };
            doubleTimer.Start();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            if (Math.Abs(e.X - downPoint.X) > 4 || Math.Abs(e.Y - downPoint.Y) > 4) moved = true;
            var p = PointToScreen(e.Location);
            Location = new Point(p.X - dragOffset.X, p.Y - dragOffset.Y);
            Render();
        }

        private void OnUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (!dragging) return;
            dragging = false;
            Sfx.PlayRelease();
            Pulse();
            if (!moved) CountDouble();
            posSaveTimer.Stop();
            posSaveTimer.Start();  // 松手 0.7 秒后记住位置
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW; return cp; }
        }

        protected override void Dispose(bool disposing)
        {
            if (animTimer != null) { animTimer.Stop(); animTimer.Dispose(); }
            if (posSaveTimer != null) { posSaveTimer.Stop(); posSaveTimer.Dispose(); }
            if (doubleTimer != null) { doubleTimer.Stop(); doubleTimer.Dispose(); }
            if (whaleImg != null) whaleImg.Dispose();
            base.Dispose(disposing);
        }
    }
}