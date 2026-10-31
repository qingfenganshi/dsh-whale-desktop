// =============================================================================
// 本文件是衍生作品的新增部分（原上游项目无此文件）
//   上游项目：DeepSeek-Balance-Whale-Widget  https://github.com/MeteorNOX/DeepSeek-Balance-Whale-Widget
//   原作者：MeteorNOX    原许可证：MIT License
//   本项目同样以 MIT 发布，原版权声明与完整许可见 LICENSE。
// =============================================================================using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DesktopWhale
{
    // ===== 椭圆气泡窗体：样式、按钮、配色、字体与 DSH 插件保持一致 =====
    // 插件气泡样式参照（assets/whale-widget.js 的 .dshw-chat-* 一段）：
    //   白椭圆 + 深蓝边框 #203170 + 左下两个圆点尾巴
    //   标题栏：小鲸鱼 + 模式提示（精简上下文 / 携带主会话）
    //   按钮：🔑 填 Key、🧠 上下文切换、🎨 换皮肤、🗑 清空、✕ 关闭
    //   正文区：浅色底 #FAFBFF，气泡文字 9.5pt 雅黑
    //   底部：输入框 + 蓝色发送按钮 #4D6BFE
    public class ChatBubble : Form
    {
        private const int Border = 4;
        private const int TailZone = 46;
        private List<ChatTurn> turns = new List<ChatTurn>();
        private readonly WhaleForm host;

        private RichTextBox log;
        private TextBox input;
        private Button btnSend, btnKey, btnCtx, btnSkin, btnClear, btnClose;
        private Button[] bubbleScaleBtns = new Button[2];
        private ComboBox historyCombo;
        private ComboBox reasonCombo;
        private static readonly Size BubbleBase = new Size(520, 380);  // 更接近插件 1026:700 的扁椭圆比例

        // 气泡大小档位（相对基准尺寸的百分比）
        private double BubbleScale
        {
            get { double v; double.TryParse(Cfg.Get("bubblescale", "1"), out v); return v < 0.6 ? 0.6 : (v > 1.8 ? 1.8 : v); }
            set { Cfg.Set("bubblescale", value.ToString("0.00")); }
        }

        private void ApplyBubbleScale()
        {
            double s = BubbleScale;
            ClientSize = new Size((int)(BubbleBase.Width * s), (int)(BubbleBase.Height * s));
            Relayout();
            ApplyEllipseRegion();
            Invalidate();
        }

        // 尺寸变化后重新摆内容区（标题/正文/输入框）
        private void Relayout()
        {
            try
            {
                var er = EllipseRect();
                int w = (int)(er.Width * 0.72);
                int h = (int)(er.Height * 0.70);
                var inner = Controls["inner"] as Panel;
                if (inner != null)
                {
                    inner.Left = er.Left + (er.Width - w) / 2;
                    inner.Top = er.Top + (er.Height - h) / 2;
                    inner.Width = w;
                    inner.Height = h;
                }
                var head = inner != null ? inner.Controls["head"] as Panel : null;
                if (head != null) LayoutHead(head);
            }
            catch { }
        }

        private void ResizeBy(int step)
        {
            double s = Math.Max(0.6, Math.Min(1.8, BubbleScale + step * 0.1));
            BubbleScale = s;
            ApplyBubbleScale();
            if (userMovedBubble) { /* 保持相对位置 */ }
            else if (host != null && !host.IsDisposed)
            {
                var wa = Screen.FromControl(host).WorkingArea;
                int l, tp; ComputeSidePosition(host, wa, out l, out tp);
                Location = new Point(l, tp);
            }
        }
        private Label title, modeLabel, statusLabel;  // 标题/模式
        private System.Windows.Forms.Timer statusTimer;

        // 插件用的同一组颜色
        private static readonly Color Blue = Color.FromArgb(77, 107, 254);
        private static readonly Color Edge = Color.FromArgb(32, 49, 112);
        private static readonly Color Ink = Color.FromArgb(31, 35, 48);
        private static readonly Color Err = Color.FromArgb(192, 57, 43);
        private static readonly Color Ok = Color.FromArgb(46, 158, 91);
        private static readonly Color MsgBg = Color.FromArgb(250, 251, 255);

        public ChatBubble(WhaleForm host)
        {
            this.host = host;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            ApplyBubbleScale();
            BackColor = Color.White;

            turns = Hist.Load();
            BuildInner();
            RenderHistory();
            StartStatusLoop();
        }

        // ---------- 布局 ----------
        private Rectangle EllipseRect()
        {
            return new Rectangle(Border, Border, ClientSize.Width - Border * 2 - 1, ClientSize.Height - Border * 2 - TailZone);
        }

        private void BuildInner()
        {
            var er = EllipseRect();
            int w = (int)(er.Width * 0.72);
            int h = (int)(er.Height * 0.70);
            var inner = new Panel
            {
                Left = er.Left + (er.Width - w) / 2,
                Top = er.Top + (er.Height - h) / 2,
                Width = w,
                Height = h,
                BackColor = Color.White,
            };
            inner.Name = "inner";

            // ---- 头部：标题 + 状态 + 按钮 ----
            var head = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.White };
            head.Name = "head";

            title = new Label
            {
                Text = "小鲸鱼",
                Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
                ForeColor = Edge,
                AutoSize = true,
                Left = 0,
                Top = 4,
            };
            modeLabel = new Label
            {
                Text = Cfg.CtxMain ? "携带主会话" : "精简上下文",
                Font = new Font("Microsoft YaHei UI", 8f),
                ForeColor = Blue,
                AutoSize = true,
                Top = 7,
            };
            btnKey = IconBtn("🔑", "填写 / 更换 DeepSeek API Key");
            btnKey.Click += (s, e) => ShowKeyDialog();
            btnCtx = IconBtn("🧠", "切换上下文：精简 / 主会话");
            btnCtx.Click += (s, e) => ToggleCtx();
            btnSkin = IconBtn("🎨", "换皮肤（下一套）");
            btnSkin.Click += (s, e) => { if (host != null) host.NextSkin(); };
            btnClear = IconBtn("🗑", "清空对话");
            btnClear.Click += (s, e) => ClearHistory();
            btnClose = IconBtn("✕", "关闭 (Esc)");
            btnClose.Click += (s, e) => Close();
            // v7：气泡大小调节
            var btnBigger = IconBtn("➕", "对话框放大");
            btnBigger.Click += (s, e) => ResizeBy(1);
            var btnSmaller = IconBtn("➖", "对话框缩小");
            btnSmaller.Click += (s, e) => ResizeBy(-1);
            head.Controls.Add(btnBigger);
            head.Controls.Add(btnSmaller);
            bubbleScaleBtns[0] = btnSmaller;
            bubbleScaleBtns[1] = btnBigger;

            head.Controls.Add(title);
            head.Controls.Add(modeLabel);
            head.Controls.Add(btnSkin);
            head.Controls.Add(btnCtx);
            head.Controls.Add(btnKey);
            head.Controls.Add(btnClear);
            head.Controls.Add(btnClose);

            // v13：历史轮数选择器（0 = 只发当前这一问；越大越费 token）
            historyCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Microsoft YaHei UI", 7.5f),
                Width = 62,
                Height = 20,
                Top = 3,
                FlatStyle = FlatStyle.Flat,
            };
            foreach (int n in new int[] { 0, 1, 2, 3, 5, 10, 20 })
            {
                historyCombo.Items.Add(n == 0 ? "无历史" : (n + " 轮"));
            }
            int idx = Array.IndexOf(new int[] { 0, 1, 2, 3, 5, 10, 20 }, Cfg.HistoryRounds);
            historyCombo.SelectedIndex = idx < 0 ? 4 : idx;
            new ToolTip().SetToolTip(historyCombo, "对话带多少轮历史（0/无历史 = 最省 token）");
            historyCombo.SelectedIndexChanged += (s, e) =>
            {
                int[] opts = new int[] { 0, 1, 2, 3, 5, 10, 20 };
                int sel = historyCombo.SelectedIndex;
                if (sel < 0) return;
                Cfg.HistoryRounds = opts[sel];
                AppendSys("对话历史：改为 " + (opts[sel] == 0 ? "不带历史（只发当前问题，最省 token）" : opts[sel] + " 轮"));
            };
            head.Controls.Add(historyCombo);

            // v22：推理等级下拉（off / low / high）
            reasonCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Microsoft YaHei UI", 7.5f),
                Width = 62,
                Height = 20,
                Top = 3,
                FlatStyle = FlatStyle.Flat,
            };
            reasonCombo.Items.Add("推理 Off");
            reasonCombo.Items.Add("推理 Low");
            reasonCombo.Items.Add("推理 High");
            reasonCombo.SelectedIndex = Cfg.ReasonLevel == "high" ? 2 : (Cfg.ReasonLevel == "low" ? 1 : 0);
            new ToolTip().SetToolTip(reasonCombo, "推理等级：Off 最省 token；Low 稍微想想；High 用推理模型（最贵最准）");
            reasonCombo.SelectedIndexChanged += (s, e) =>
            {
                string[] vals = new string[] { "off", "low", "high" };
                int sel = reasonCombo.SelectedIndex;
                if (sel < 0) return;
                Cfg.ReasonLevel = vals[sel];
                AppendSys(vals[sel] == "off" ? "推理等级：Off（最省 token）"
                    : vals[sel] == "low" ? "推理等级：Low（低温 + 先想一步，略费）"
                    : "推理等级：High（deepseek-reasoner，最贵最准）");
            };
            head.Controls.Add(reasonCombo);

            head.Resize += (s, e) => LayoutHead(head);

            // ---- 余额状态行（与插件的「余额 / 今日已用 / 峰谷」一致）----
            var status = new Panel { Dock = DockStyle.Top, Height = 20, BackColor = Color.White };
            status.Name = "status";
            statusLabel = new Label
            {
                Text = "余额读取中…",
                Font = new Font("Microsoft YaHei UI", 8.5f),
                ForeColor = Color.FromArgb(120, 128, 150),
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            status.Controls.Add(statusLabel);

            // ---- 正文 ----
            log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = MsgBg,
                ForeColor = Ink,
                Font = new Font("Microsoft YaHei UI", 9.5f),
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.Vertical,
            };

            // ---- 底部输入 ----
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = Color.White };
            input = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 9.5f),
                BorderStyle = BorderStyle.FixedSingle,
            };
            btnSend = new Button
            {
                Text = "➤",
                Dock = DockStyle.Right,
                Width = 32,
                FlatStyle = FlatStyle.Flat,
                BackColor = Blue,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f),
            };
            btnSend.FlatAppearance.BorderSize = 0;
            bottom.Controls.Add(input);
            bottom.Controls.Add(btnSend);
            bottom.Padding = new Padding(6, 5, 6, 6);   // 输入框四周留白（原来贴边）

            inner.Controls.Add(log);
            inner.Controls.Add(bottom);
            inner.Controls.Add(status);
            inner.Controls.Add(head);
            Controls.Add(inner);

            btnSend.Click += (s, e) => SendAsync();
            input.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SendAsync(); }
                if (e.KeyCode == Keys.Escape) Close();
            };

            LayoutHead(head);
        }

        private void LayoutHead(Panel head)
        {
            if (head == null || btnClose == null) return;
            btnClose.Left = head.Width - btnClose.Width;
            btnClear.Left = btnClose.Left - btnClear.Width;
            btnKey.Left = btnClear.Left - btnKey.Width;
            btnCtx.Left = btnKey.Left - btnCtx.Width;
            btnSkin.Left = btnCtx.Left - btnSkin.Width;
            if (bubbleScaleBtns[1] != null && bubbleScaleBtns[0] != null)
            {
                bubbleScaleBtns[1].Left = btnSkin.Left - bubbleScaleBtns[1].Width;
                bubbleScaleBtns[0].Left = bubbleScaleBtns[1].Left - bubbleScaleBtns[0].Width;
            }
            // 标题区：小鲸鱼 + 模式，放在按钮左边的可用空间里，超长自动收窄（之前被按钮压住）
            int btnsLeft = (bubbleScaleBtns[0] != null) ? bubbleScaleBtns[0].Left : btnSkin.Left;
            // v13：历史轮数下拉框放在"最左按钮"左边
            int titleRightLimit = btnsLeft;
            if (historyCombo != null)
            {
                historyCombo.Left = btnsLeft - historyCombo.Width - 4;
                titleRightLimit = historyCombo.Left;
            }
            if (reasonCombo != null)
            {
                reasonCombo.Left = titleRightLimit - reasonCombo.Width - 2;
                titleRightLimit = reasonCombo.Left;
            }
            title.MaximumSize = new Size(Math.Max(40, titleRightLimit - 4), 18);
            title.AutoSize = true;
            modeLabel.Left = title.Right + 6;
            modeLabel.MaximumSize = new Size(Math.Max(20, titleRightLimit - modeLabel.Left - 2), 16);
            modeLabel.AutoSize = true;
            // 按钮压到标题文字上时，标题省略不用管（按钮从右往左排）
        }

        private Button IconBtn(string text, string tip)
        {
            var b = new Button
            {
                Text = text,
                Top = 3,
                Width = 22,
                Height = 21,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(65, 81, 155),
                Font = new Font("Segoe UI Emoji", 9f),
                TabStop = false,
            };
            b.FlatAppearance.BorderSize = 0;
            new ToolTip().SetToolTip(b, tip);
            return b;
        }

        // ---------- 绘制：白椭圆 + 深蓝边 + 两个尾巴圆点 ----------
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateEllipticRgn(int l, int t, int r, int b);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

        private void ApplyEllipseRegion()
        {
            try
            {
                if (!IsHandleCreated) return;
                var er = EllipseRect();
                IntPtr region = CreateEllipticRgn(er.Left, er.Top, er.Right, er.Bottom);
                if (region == IntPtr.Zero) return;

                int d1 = (int)(er.Width * 0.070);
                int d2 = (int)(er.Width * 0.045);
                int x1, y1, x2, y2;
                // v16：与 OnPaint 完全同一套坐标（大点贴椭圆、小点更外）
                if (aboveWhale)
                {
                    y1 = er.Bottom - (int)(d1 * 0.70);
                    y2 = y1 + (int)(d1 * 1.05);
                    if (tailOnRight) { x1 = er.Left + (int)(er.Width * 0.33); x2 = x1 + (int)(er.Width * 0.060); }
                    else { x1 = er.Left + (int)(er.Width * 0.60); x2 = x1 - (int)(er.Width * 0.060); }
                }
                else
                {
                    y1 = er.Top - (int)(d1 * 0.30);
                    y2 = y1 - (int)(d1 * 1.05);
                    x1 = tailOnRight ? er.Left + (int)(er.Width * 0.33) : er.Right - (int)(er.Width * 0.33) - d1;
                    x2 = tailOnRight ? x1 + (int)(er.Width * 0.060) : x1 - (int)(er.Width * 0.060);
                }
                IntPtr t1 = CreateEllipticRgn(x1, y1, x1 + d1, y1 + d1);
                IntPtr tmp = CreateRectRgn(0, 0, 0, 0);
                CombineRgn(tmp, region, t1, 2);
                DeleteObject(region); DeleteObject(t1);
                region = tmp;
                IntPtr t2 = CreateEllipticRgn(x2, y2, x2 + d2, y2 + d2);
                IntPtr tmp2 = CreateRectRgn(0, 0, 0, 0);
                CombineRgn(tmp2, region, t2, 2);
                DeleteObject(region); DeleteObject(t2);

                SetWindowRgn(Handle, tmp2, true);
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyEllipseRegion(); }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            // v19：统一走 Relayout（之前这里另写了一套 0.70/0.66 的比例，
            //   和 Relayout 的 0.72/0.70 互相覆盖 ⇒ 内容区大小不对，文字被截断）
            Relayout();
            ApplyEllipseRegion();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var er = EllipseRect();
            using (var br = new SolidBrush(Color.White)) g.FillEllipse(br, er);
            using (var pen = new Pen(Edge, Border))
            {
                g.DrawEllipse(pen, er);
                // 尾巴两个圆点（与窗口区域同一套坐标，保证画出来的就是可点击区域）
                int d1 = (int)(er.Width * 0.070);
                int d2 = (int)(er.Width * 0.045);
                int x1, y1, x2, y2;
                // v16（对照插件截图重画）：尾巴 = **大圆点贴气泡边缘** + **小圆点更靠外**，
                //   两个点连线指向鲸鱼。之前大小顺序和朝向都反了。
                if (aboveWhale)
                {
                    // 气泡在鲸鱼上方 → 点从椭圆底边往下排：大点先、小点更下
                    y1 = er.Bottom - (int)(d1 * 0.70);
                    y2 = y1 + (int)(d1 * 1.05);
                    if (tailOnRight) { x1 = er.Left + (int)(er.Width * 0.33); x2 = x1 + (int)(er.Width * 0.060); }
                    else { x1 = er.Left + (int)(er.Width * 0.60); x2 = x1 - (int)(er.Width * 0.060); }
                }
                else
                {
                    // 气泡在鲸鱼下方 → 点从椭圆顶边往上排：大点先、小点更上
                    y1 = er.Top - (int)(d1 * 0.30);
                    y2 = y1 - (int)(d1 * 1.05);
                    x1 = tailOnRight ? er.Left + (int)(er.Width * 0.33) : er.Right - (int)(er.Width * 0.33) - d1;
                    x2 = tailOnRight ? x1 + (int)(er.Width * 0.060) : x1 - (int)(er.Width * 0.060);
                }
                g.FillEllipse(Brushes.White, x1, y1, d1, d1);
                g.DrawEllipse(pen, x1, y1, d1, d1);
                g.FillEllipse(Brushes.White, x2, y2, d2, d2);
                g.DrawEllipse(pen, x2, y2, d2, d2);
            }
        }

        // ---------- 定位在鲸鱼头顶 ----------
        // 气泡放在鲸鱼**旁边**，绝不遮住鲸鱼（v6）：
        //   鲸鱼在屏幕右半边 → 气泡放鲸鱼左侧
        //   鲸鱼在屏幕左半边 → 气泡放鲸鱼右侧
        //   竖直方向与鲸鱼顶部对齐，放不下就下移；两头都夹在屏幕内。
        // v8（用户要求）：气泡在鲸鱼**脑门正上方**，尾巴朝下指着鲸鱼头部。
        //   鲸鱼在屏幕右半边 → 尾巴在气泡右下；左半边 → 尾巴在气泡左下（随鲸鱼翻转）。
        private void ComputeSidePosition(Form whale, Rectangle wa, out int left, out int top)
        {
            int gap = 6;
            // v18（用户要求）：原间距只有 +4px，气泡几乎贴住鲸鱼 ⇒ 太近。
            //   改为往上拉开 40px：负值 = 气泡整体上移，尾巴底端与鲸鱼头顶留出空隙。
            int tailH = (int)(TailZone * (BubbleScale));
            top = whale.Top - Height + tailH - 40;
            // 水平：尾巴锚点更靠右（用户要求"从中间往右"）
            //   鲸鱼在屏幕右半边 → 尾巴在气泡右下 0.62
            //   鲸鱼在屏幕左半边 → 尾巴在气泡左下 0.38
            int whaleCx = whale.Left + whale.Width / 2;
            bool whaleOnRightHalf = whaleCx >= wa.Left + wa.Width / 2;
            double anchor = whaleOnRightHalf ? 0.62 : 0.38;
            int anchorPx = (int)(Width * anchor);
            left = whaleCx - anchorPx;

            // 上方放不下 → 放到鲸鱼下方（尾巴朝上）
            if (top < wa.Top + 8)
            {
                top = whale.Bottom - tailH + 4;
                aboveWhale = false;
            }
            else aboveWhale = true;

            if (left < wa.Left + 8) left = wa.Left + 8;
            if (left + Width > wa.Right - 8) left = wa.Right - Width - 8;
            // v15：垂直方向不要被屏幕底边硬夹下去（之前鲸鱼在右下角时，
            //      气泡被 "top + Height > wa.Bottom" 拉到最低，看起来"太靠下"）。
            //      一旦被夹，就反过来放到鲸鱼**侧面**，保证不被挤压。
            if (top + Height > wa.Bottom - 8)
            {
                int sideLeft = whaleOnRightHalf ? whale.Left - Width - 10 : whale.Right + 10;
                if (sideLeft >= wa.Left + 8 && sideLeft + Width <= wa.Right - 8)
                {
                    left = sideLeft;
                    top = whale.Top + (whale.Height - Height) / 2;
                    aboveWhale = true;
                }
            }
            if (top < wa.Top + 8) top = wa.Top + 8;
            if (top + Height > wa.Bottom - 8) top = wa.Bottom - Height - 8;
        }

        public void PositionNearWhale(Form whale)
        {
            if (whale == null) return;
            SyncWithWhale(true);
        }

        // 尾巴朝向：true = 尾巴在右下（气泡在鲸鱼左边）
        private bool tailOnRight = true;
        private bool aboveWhale = true;  // 气泡在鲸鱼上方（尾巴朝下）

        // v9：每次刷新位置都重算尾巴方向 —— 鲸鱼左右移动时，尾巴跟着翻
        //   气泡中心 <= 鲸鱼中心 → 气泡在鲸鱼左边 → 尾巴放**右下**
        //   气泡中心 >  鲸鱼中心 → 气泡在鲸鱼右边 → 尾巴放**左下**
        private void SyncWithWhale(bool reposition)
        {
            try
            {
                if (host == null || host.IsDisposed) return;
                var wa = Screen.FromControl(host).WorkingArea;
                if (reposition && !userMovedBubble)
                {
                    int l, tp;
                    ComputeSidePosition(host, wa, out l, out tp);
                    Location = new Point(l, tp);
                }
                int bubbleCx = Left + Width / 2;
                int whaleCx = host.Left + host.Width / 2;
                // v14 修正：气泡默认就在鲸鱼正上方居中，两边中心几乎相等 ⇒ 按"中心比较"永远翻不动。
                //   改成按**鲸鱼在屏幕哪半边**决定（与插件一致）：
                //     鲸鱼在屏幕右半边 → 尾巴在气泡**右下**（tailOnRight = true）
                //     鲸鱼在屏幕左半边 → 尾巴在气泡**左下**（tailOnRight = false）
                bool whaleOnRightHalf = whaleCx >= wa.Left + wa.Width / 2;
                bool newTailRight = whaleOnRightHalf;
                bool newAbove = (Top + Height / 2) <= (host.Top + host.Height / 2);
                bool changed = (newTailRight != tailOnRight) || (newAbove != aboveWhale);
                tailOnRight = newTailRight;
                aboveWhale = newAbove;
                if (changed) ApplyEllipseRegion();
                Invalidate();
            }
            catch { }
        }
        // ---------- 余额/峰谷状态（每 30 秒刷新，与插件口径一致）----------
        private void StartStatusLoop()
        {
            UpdateStatus();
            statusTimer = new System.Windows.Forms.Timer { Interval = 30000 };
            statusTimer.Tick += (s, e) => UpdateStatus();
            statusTimer.Start();
        }

        private void UpdateStatus()
        {
            try
            {
                var b = Ledger.Read();
                string bal = double.IsNaN(b.LastBalance) ? "—" : "¥" + b.LastBalance.ToString("0.00");
                string use = double.IsNaN(b.TodayUsage) ? "—" : "¥" + b.TodayUsage.ToString("0.00");
                statusLabel.Text = "余额 " + bal + "   今日已用 " + use + "   " + Peak.Describe();
                statusLabel.ForeColor = Color.FromArgb(100, 110, 135);
                if (!string.IsNullOrEmpty(b.Error)) statusLabel.Text = "余额读取失败：" + b.Error;
            }
            catch (Exception ex) { statusLabel.Text = "余额读取异常：" + ex.Message; }
        }

        // ---------- 上下文开关 / 清空 ----------
        private void ToggleCtx()
        {
            Cfg.CtxMain = !Cfg.CtxMain;
            modeLabel.Text = Cfg.CtxMain ? "携带主会话" : "精简上下文";
            btnCtx.BackColor = Cfg.CtxMain ? Color.FromArgb(230, 235, 255) : Color.White;
            AppendSys(Cfg.CtxMain
                ? "已切换到「携带主会话」模式：会带上当前会话的标题与概要（更懂你，但更费 token）"
                : "已切换到「精简上下文」模式：只带这一问一答（最省 token）");
        }

        private void ClearHistory()
        {
            turns = new List<ChatTurn>();
            Hist.Save(turns);
            RenderHistory();
            AppendSys("已清空。");
        }

        private void RenderHistory()
        {
            log.Clear();
            if (turns.Count == 0)
            {
                AppendRaw("咕噜咕噜～ 我是桌面小鲸鱼，有什么想问的直接说。\n\n", Ink);
                if (string.IsNullOrEmpty(Cfg.ApiKey)) AppendRaw("提示：点 🔑 填一个 API Key（sk- 开头），填一次就行。\n\n", Err);
                return;
            }
            foreach (var t in turns)
            {
                if (t.Role == "user") AppendRaw("你：" + t.Text + "\n", Blue);
                else AppendRaw("小鲸鱼：" + t.Text + "\n\n", Ink);
            }
        }

        // ---------- Key 对话框 ----------
        private void ShowKeyDialog()
        {
            using (var dlg = new KeyDialog(Cfg.ApiKey))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if (!string.IsNullOrEmpty(dlg.appendLog))
                    {
                        AppendSys(dlg.appendLog);
                    }
                    else
                    {
                        Cfg.ApiKey = dlg.KeyValue;
                        AppendSys(string.IsNullOrEmpty(Cfg.ApiKey) ? "已清空 API Key。" : "✅ API Key 已保存。");
                    }
                    bool hasKey = !string.IsNullOrEmpty(Cfg.ApiKey);
                    btnKey.ForeColor = hasKey ? Color.FromArgb(65, 81, 155) : Color.FromArgb(232, 162, 0);
                    input.Focus();
                }
            }
        }

        // ---------- 发送 ----------
        private async void SendAsync()
        {
            string text = (input.Text ?? "").Trim();
            if (text.Length == 0) return;
            string key = Cfg.ApiKey;
            if (string.IsNullOrEmpty(key)) { AppendSys("请先点 🔑 填写 API Key。"); ShowKeyDialog(); return; }

            btnSend.Enabled = false;
            input.Clear();
            AppendRaw("你：" + text + "\n", Blue);
            turns.Add(new ChatTurn { Role = "user", Text = text });
            Hist.Save(turns);

            string note = null;
            if (Cfg.CtxMain)
            {
                try
                {
                    string st = Path.Combine(Cfg.DshHome, ".dshw-usage.json");
                    var b = Ledger.Read();
                    note = "当前用户余额 ¥" + (double.IsNaN(b.LastBalance) ? "未知" : b.LastBalance.ToString("0.00"))
                         + "，今日已用 ¥" + (double.IsNaN(b.TodayUsage) ? "未知" : b.TodayUsage.ToString("0.00"))
                         + "，时段：" + Peak.Describe() + "。";
                }
                catch { }
            }

            var pending = new ChatTurn { Role = "assistant", Text = "" };
            AppendRaw("小鲸鱼：", Ink);

            // v27（真机日志定位）：原来的写法
            //     await Task.Run(async () => { await Api.ChatAsync(...); });
            //     await done.Task;
            //   Task.Run 把整个 ChatAsync 跑在后台线程，**await 的是"跑完"**；
            //   而内容是通过 BeginInvoke 回到 UI 线程的，等到这里时 UI 消息根本没被处理，
            //   于是"小鲸鱼："后面永远空着（日志证明内容其实都收到了）。
            //   改成：直接用 async 方法在 UI 线程上 await（OpenAI 的 await 不阻塞消息泵）。
            try
            {
                await Api.ChatAsync(key, new List<ChatTurn>(turns), note,
                    delta =>
                    {
                        // 回调在后台线程 ⇒ 必须投递回 UI 线程再改控件
                        try
                        {
                            if (IsDisposed) return;
                            BeginInvoke(new Action(() =>
                            {
                                pending.Text += delta;
                                AppendRaw(delta, Ink);
                            }));
                        }
                        catch { }
                    },
                    () => { },
                    err => { throw new Exception(err); },
                    () => { });
                // 等一小会儿让最后几帧 UI 更新落地
                await Task.Delay(120);

                AppendRaw("\n\n", Color.Black);
                if (!string.IsNullOrEmpty(pending.Text)) { turns.Add(pending); Hist.Save(turns); }
                Sfx.PlayTaskEnd();
            }
            catch (Exception ex)
            {
                Api.Log("发送失败：" + ex.Message);
                AppendSys("[" + ex.Message + "]");
            }
            finally { btnSend.Enabled = true; input.Focus(); }
        }

        private void AppendRaw(string s, Color c)
        {
            log.SelectionStart = log.TextLength;
            log.SelectionColor = c;
            log.AppendText(s);
            log.SelectionColor = Ink;
            log.ScrollToCaret();
        }
        private void AppendSys(string text) { AppendRaw(text + "\n\n", Color.FromArgb(120, 128, 150)); }

        // ===== v7：跟随鲸鱼 + 相对位置 =====
        private System.Windows.Forms.Timer followTimer;
        private Point relOffset;         // 气泡相对鲸鱼左上角的偏移（拖动气泡时会更新）
        private bool userMovedBubble;    // 用户自己拖过气泡 → 尊重他的相对位置

        // 让气泡能拖动（无边框窗体需要自己实现）
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;

        private void EnableDrag(Control c)
        {
            try
            {
                c.MouseDown += (s, e) =>
                {
                    if (e.Button != MouseButtons.Left) return;
                    // 拖动开始前记下相对偏移
                    ReleaseCapture();
                    SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                };
                c.MouseUp += (s, e) => { ApplyRelativeOffset(); };
            }
            catch { }
        }

        // 相对位置：气泡相对鲸鱼左上角的偏移
        private void ApplyRelativeOffset()
        {
            if (host == null || host.IsDisposed) return;
            userMovedBubble = true;
            relOffset = new Point(Left - host.Left, Top - host.Top);
            try { Cfg.Set("bubbleDX", relOffset.X.ToString()); Cfg.Set("bubbleDY", relOffset.Y.ToString()); Cfg.Set("bubbleMoved", "1"); } catch { }
        }

        private void StartFollow()
        {
            try
            {
                Cfg.Set("bubbleMoved", userMovedBubble ? "1" : "0");
                followTimer = new System.Windows.Forms.Timer { Interval = 120 };
                followTimer.Tick += (s, e) =>
                {
                    if (host == null || host.IsDisposed) return;
                    if (userMovedBubble)
                    {
                        // 跟着鲸鱼保持相对位置
                        int nx = host.Left + relOffset.X;
                        int ny = host.Top + relOffset.Y;
                        if (Left != nx || Top != ny) Location = new Point(nx, ny);
                        SyncWithWhale(false);   // 相对模式下也要让尾巴 / 上下朝向跟着翻
                    }
                    else
                    {
                        SyncWithWhale(true);    // 默认模式：跟随定位 + 尾巴翻转
                    }
                };
                followTimer.Start();
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            input.Focus();
            // 恢复上次的相对位置
            try
            {
                var d = Ini.Read(Cfg.CfgFile);
                if (d.ContainsKey("bubbleMoved") && d["bubbleMoved"] == "1" && d.ContainsKey("bubbleDX") && d.ContainsKey("bubbleDY"))
                {
                    int dx, dy;
                    if (int.TryParse(d["bubbleDX"], out dx) && int.TryParse(d["bubbleDY"], out dy))
                    {
                        userMovedBubble = true;
                        relOffset = new Point(dx, dy);
                        if (host != null) Location = new Point(host.Left + dx, host.Top + dy);
                    }
                }
            }
            catch { }
            EnableDrag(log);
            StartFollow();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (statusTimer != null) { statusTimer.Stop(); statusTimer.Dispose(); }
            base.OnFormClosed(e);
        }
    }

    // ===== API Key 输入对话框 =====
    public class KeyDialog : Form
    {
        private readonly TextBox tb;
        public string KeyValue { get { return tb.Text; } }
        // v29：清空操作的结果提示（调用方负责显示）
        public string appendLog = "";

        public KeyDialog(string current)
        {
            Text = "填写 DeepSeek API Key";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; TopMost = true;
            ClientSize = new Size(480, 180);

            var tip = new Label
            {
                Text = "粘贴 sk- 开头的 Key（只保存在本程序文件夹 apikey.txt）\n" +
                       "申请地址：https://platform.deepseek.com/api_keys",
                Left = 16, Top = 12, Width = 450, Height = 44,
                Font = new Font("Microsoft YaHei UI", 9f),
            };
            tb = new TextBox { Left = 16, Top = 62, Width = 448, Font = new Font("Consolas", 10f), Text = current ?? "" };
            var ok = new Button { Text = "保存", Left = 288, Top = 110, Width = 88, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Left = 382, Top = 110, Width = 82, DialogResult = DialogResult.Cancel };
            var clear = new Button { Text = "清空已保存的 Key（分享前必点）", Left = 16, Top = 110, Width = 260 };
            clear.Click += (s, e) =>
            {
                var r = MessageBox.Show(
                    "会做两件事：\n\n" +
                    "① 删除本程序的 apikey.txt\n" +
                    "② 清除插件存在 DSH 里的 DEEPSEEK_API_KEY\n\n" +
                    "清空后对话将无法使用，需要重新填。确定吗？",
                    "清空 API Key", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;

                tb.Text = "";
                // ① 删本程序的 key 文件（先覆盖再删，降低被恢复的可能）
                try
                {
                    if (File.Exists(Cfg.KeyFile))
                    {
                        try { File.WriteAllText(Cfg.KeyFile, new string('0', 64), new UTF8Encoding(false)); } catch { }
                        File.Delete(Cfg.KeyFile);
                    }
                }
                catch { }
                // ② 清掉 DSH 凭据里的 DEEPSEEK_API_KEY（只删这一项，其他配置不动，先备份）
                try
                {
                    string cred = Path.Combine(Cfg.DshHome, ".credentials.yaml");
                    if (File.Exists(cred))
                    {
                        var lines = File.ReadAllLines(cred, Encoding.UTF8);
                        var keep = new List<string>();
                        bool skipped = false;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string ln = lines[i];
                            if (ln.TrimStart().StartsWith("DEEPSEEK_API_KEY:", StringComparison.OrdinalIgnoreCase))
                            {
                                skipped = true;
                                continue;
                            }
                            keep.Add(ln);
                        }
                        if (skipped)
                        {
                            try { File.Copy(cred, cred + ".bak", true); } catch { }
                            File.WriteAllLines(cred, keep.ToArray(), new UTF8Encoding(false));
                        }
                    }
                }
                catch { }
                appendLog = "✅ 已清空：本程序 apikey.txt + DSH 里的 DEEPSEEK_API_KEY";
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.AddRange(new Control[] { tip, tb, clear, cancel, ok });
            AcceptButton = ok; CancelButton = cancel;
        }
    }
}
