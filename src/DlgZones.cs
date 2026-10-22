using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>**טשטוש וצנזורה** (0.8.35, WORKPLAN 2כט ב): אזורים בסרט שמוסתרים רק בזמן שלהם - פנים, שלט, כיתוב. בנימין: ״אפשרות לטשטש
    /// קטע לפי בחירת אזור וזמן. וטשטוש (למטרות צנזורה) בכללי״. הממשק בהשראת MooviMaker: גוררים על התמונה = אזור חדש, גוררים אזור = הזזה,
    /// פינה = גודל, קליק ימני = מראה / צורה / מחיקה. הזמן לפי הנגן: אזור חדש מתחיל איפה שהנגן עומד ונמשך עד הסוף, ו״סוף כאן״ סוגר אותו -
    /// כך מסתירים פנים שמופיעות ב-0:12 ונעלמות ב-0:45 בשלוש פעולות. עובד על עותק; ״שמירה״ מחזיר אותו (`Result`), ויציאה בלי שמירה שואלת.</summary>
    internal class ZonesDlg : Dlg
    {
        private readonly MainForm _main;
        private readonly MediaInfo _mi;
        /// <summary>רק בשביל הציור (`SubStyle.RenderZones`) - בלי כתובית ובלי הטשטוש של חלון העיצוב.</summary>
        private readonly SubStyle _work = new SubStyle();
        private readonly ZoneCanvas _canvas;
        private readonly ZoneTimeBar _bar;
        private readonly ZoneList _list;
        private readonly Btn _play, _markIn, _markOut, _whole;
        private readonly Lbl _clock, _zoneTitle, _when, _none, _pctCap;
        private readonly Combo _look, _shape;
        private readonly Slider _pct;
        private readonly Timer _timer;
        private readonly List<List<BlurZone>> _undo = new List<List<BlurZone>>();
        private string _lastPush = "";
        private bool _changed, _syncing;
        private long _pos;
        private DateTime _flashUntil = DateTime.MinValue;
        // מה שנבחר לאחרונה - לאזור הבא
        private int _lastLook = BlurZone.Blur, _lastPct = 60;
        private Btn _faceBtn;
        private volatile bool _faceStop;
        private bool _scanning;
        private DateTime _lastProg = DateTime.MinValue;
        private bool _lastEllipse;

        /// <summary>אחרי ״שמירה״: האזורים (עותק חדש).</summary>
        public List<BlurZone> Result;

        public ZonesDlg(MainForm main, MediaInfo mi, List<BlurZone> zones) : base(Lang.T("טשטוש וצנזורה"), Ico.Eye, 760)
        {
            _main = main; _mi = mi;
            if (zones != null) foreach (BlurZone z in zones) _work.Zones.Add(z.Clone());
            Subtitle = Lang.T("גוררים על התמונה כדי לסמן אזור. כל אזור מוסתר רק בזמן שנבחר לו.");
            int W = ContentW, s8 = Theme.S(8), s10 = Theme.S(10);
            int y = Y;

            // ---- התמונה ----
            _canvas = new ZoneCanvas();
            _canvas.HasMedia = true;
            _canvas.Style = _work;
            _canvas.Zones = _work.Zones;
            if (mi != null && mi.Width > 0) { _canvas.AspectW = mi.Width; _canvas.AspectH = mi.Height; }
            _canvas.SetBounds(Pad, y, W, Theme.S(290));
            _canvas.CreateZone += delegate (RectangleF a) { AddZone(a); };
            _canvas.EditStarting += delegate { Push("drag"); };
            _canvas.Edited += delegate { _changed = true; RefreshAll(); };
            _canvas.SelectionChanged += delegate { Select(_canvas.Selected); };
            _canvas.MenuFor += delegate (int i, Point p) { ShowMenu(i, p); };
            Controls.Add(_canvas);
            y = _canvas.Bottom + s8;

            // ---- הנגן: ▶, שעון, פס זמן (המידות בעברית: ▶ מימין) ----
            int rowH = Theme.S(36);
            _play = new Btn(); _play.Icon = Ico.Play; _play.IconOnly = true; _play.Kind = BtnKind.Primary; _play.IconSize = Theme.S(15);
            _play.SetBounds(Pad + W - rowH, y, rowH, rowH);
            _play.Click += delegate { TogglePlay(); };
            Ui.Tip.SetToolTip(_play, Lang.T("ניגון ועצירה (רווח)"));
            Controls.Add(_play);
            _clock = new Lbl(); _clock.Font = Theme.UiBold; _clock.Numeric = true; _clock.Color = Theme.TextDim;
            int clockW = TextRenderer.MeasureText(Theme.Ltr("00:00:00.0 / 00:00:00.0"), _clock.Font).Width + Theme.S(8);
            _clock.SetBounds(_play.Left - s10 - clockW, y, clockW, rowH);
            Controls.Add(_clock);
            _bar = new ZoneTimeBar();
            _bar.Duration = Math.Max(1, mi != null ? mi.DurationMs : 1);
            _bar.Zones = _work.Zones;
            _bar.SetBounds(Pad, y, _clock.Left - s10 - Pad, rowH);
            _bar.SeekTo += delegate (long t) { Seek(t); };
            Ui.Tip.SetToolTip(_bar, Lang.T("לחיצה או גרירה - הנגן עובר לשם. הפסים הצבעוניים - מתי כל אזור מוסתר"));
            Controls.Add(_bar);
            y += rowH + Theme.S(14);

            // ---- למטה: רשימת האזורים (מימין), והאזור שנבחר (משמאל) ----
            int listW = Theme.S(290), gap = Theme.S(20);
            int listX = Pad + W - listW, colW = W - listW - gap;
            Lbl cap = Label(Lang.T("האזורים"), true, Theme.TextDim); cap.Font = Theme.SmallBold;
            cap.SetBounds(listX, y, listW, Theme.S(18));
            Controls.Add(cap);
            _list = new ZoneList();
            _list.Zones = _work.Zones;
            _list.Describe = Describe;
            _list.SetBounds(listX, y + Theme.S(22), listW, Theme.S(142));
            _list.SelectionChanged += delegate { Select(_list.Selected); };
            _list.RemoveClicked += delegate (int i) { RemoveZone(i); };
            _list.MenuFor += delegate (int i, Point p) { ShowMenu(i, p); };
            Controls.Add(_list);

            _zoneTitle = Label("", true, Theme.Text);
            _zoneTitle.SetBounds(Pad, y, colW, Theme.S(20));
            Controls.Add(_zoneTitle);
            _when = Label("", false, Theme.TextDim);
            _when.Font = Theme.Small;
            _when.SetBounds(Pad, y + Theme.S(22), colW, Theme.S(20));
            Controls.Add(_when);
            // ״התחלה כאן״ / ״סוף כאן״ / ״כל הסרט״ - מימין לשמאל, מהקצה של העמודה
            int bx = Pad + colW, bh = Theme.S(32), by = y + Theme.S(46);
            _markIn = SmallBtn(Lang.T("התחלה כאן"), Lang.T("האזור מתחיל איפה שהנגן עומד (המקש I)"), ref bx, by, bh);
            _markOut = SmallBtn(Lang.T("סוף כאן"), Lang.T("האזור נגמר איפה שהנגן עומד (המקש O)"), ref bx, by, bh);
            _whole = SmallBtn(Lang.T("כל הסרט"), Lang.T("האזור מוסתר מההתחלה ועד הסוף"), ref bx, by, bh);
            _markIn.Click += delegate { MarkHere(true); };
            _markOut.Click += delegate { MarkHere(false); };
            _whole.Click += delegate { WholeVideo(); };

            int half = (colW - s10) / 2, ry = y + Theme.S(90);
            _look = new Combo();
            _look.Items.AddRange(new object[] { Lang.T("טשטוש"), Lang.T("פיקסלים"), Lang.T("כיסוי שחור") });
            _look.SetBounds(Pad + colW - half, ry, half, Theme.S(32));
            _look.SelectedIndexChanged += delegate { if (!_syncing) SetLook(_look.SelectedIndex); };
            Controls.Add(_look);
            _shape = new Combo();
            _shape.Items.AddRange(new object[] { Lang.T("מלבן"), Lang.T("עיגול") });
            _shape.SetBounds(Pad, ry, half, Theme.S(32));
            _shape.SelectedIndexChanged += delegate { if (!_syncing) SetShape(_shape.SelectedIndex == 1); };
            Controls.Add(_shape);
            int py = ry + Theme.S(40);
            _pctCap = Label(Lang.T("עוצמה"), true, Theme.TextDim); _pctCap.Font = Theme.SmallBold;
            int capW = Math.Max(Theme.S(60), TextRenderer.MeasureText(_pctCap.Text, _pctCap.Font).Width + Theme.S(6));
            _pctCap.SetBounds(Pad + colW - capW, py + Theme.S(5), capW, Theme.S(20));
            Controls.Add(_pctCap);
            _pct = new Slider();
            _pct.Min = 5; _pct.Max = 100; _pct.Step = 5; _pct.Suffix = "%"; _pct.Editable = true;
            _pct.SetBounds(Pad, py, colW - capW - s8, Theme.S(28));
            _pct.ValueChanged += delegate { if (!_syncing) SetPct((int)Math.Round(_pct.Value)); };
            Controls.Add(_pct);

            _none = Hint(Lang.T("בוחרים אזור ברשימה או על התמונה כדי לשנות אותו.\nאזור חדש: גוררים על התמונה, איפה שצריך להסתיר."));
            _none.Font = Theme.Ui;
            _none.Color = Theme.TextFaint;
            _none.SetBounds(Pad, y + Theme.S(22), colW, Theme.S(80));
            Controls.Add(_none);

            Y = y + Theme.S(172);
            Buttons(Lang.T("שמירה"), Ico.Check, Lang.T("ביטול"));
            // **פנים אוטומטית** (2כט ה): הגלאי של ווינדוס, בלי אינטרנט ובלי הורדה - אזור שזז לכל פנים
            _faceBtn = BottomButton(Lang.T("למצוא פנים"), Ico.Sparkles, delegate { FindFaces(); });
            Ui.Tip.SetToolTip(_faceBtn, Lang.T("מחפש את כל הפנים בסרט ומוסיף לכל אחת אזור שזז איתה - בלי אינטרנט, בגלאי של ווינדוס"));

            _pos = main != null ? main.PlayerPosition : 0;
            // הפריים שעל המסך; אין (הנגן עוד לא צייר, או בבדיקה בלי נגן) - צילום אחד מהקובץ, כדי שיהיה על מה לסמן
            Bitmap first = main != null ? main.CurrentFrame() : null;
            if (first == null && mi != null && mi.HasVideo && !string.IsNullOrEmpty(mi.Path))
            {
                int fh = (int)Math.Round(640.0 * mi.Height / Math.Max(1, mi.Width));
                fh -= fh % 2;
                try { first = FrameGrabber.Grab(mi.Path, Math.Max(0, Math.Min(_pos, mi.DurationMs - 100)), 640, fh < 2 ? 360 : fh); }
                catch { }
            }
            if (first != null) _canvas.SetFrame(first);
            if (main != null) main.FrameShown += OnFrame;
            Select(-1);
            ShowPos(_pos);

            _timer = new Timer();
            _timer.Interval = 30;
            _timer.Tick += delegate { OnTick(); };
            Shown += delegate { _timer.Start(); _canvas.Focus(); };
            FormClosing += OnClosing;
        }

        // ---------- לבדיקות ולעוזר ----------
        internal List<BlurZone> Zones { get { return _work.Zones; } }
        internal int Selected { get { return _canvas.Selected; } }
        internal bool Changed { get { return _changed; } }
        internal string WhenText { get { return _when.Text; } }

        private Btn SmallBtn(string text, string tip, ref int right, int y, int h)
        {
            Btn b = new Btn(); b.Text = text; b.Kind = BtnKind.Subtle; b.Font = Theme.Small;
            int w = Math.Max(Theme.S(84), b.NeedWidth());
            b.SetBounds(right - w, y, w, h);
            right -= w + Theme.S(8);
            Ui.Tip.SetToolTip(b, tip);
            Controls.Add(b);
            return b;
        }

        // ---------- מה שמוצג ----------

        internal static string LookName(int look)
        {
            return look == BlurZone.Pixels ? Lang.T("פיקסלים") : look == BlurZone.Black ? Lang.T("כיסוי שחור") : Lang.T("טשטוש");
        }

        /// <summary>מתי, במילים: ״כל הסרט״, ״מ-0:12.4 עד הסוף״, ״מההתחלה עד 0:45.0״, ״מ-0:12.4 עד 0:45.0״.</summary>
        internal static string WhenWords(BlurZone z)
        {
            if (z.FromMs < 0 && z.ToMs < 0) return Lang.T("כל הסרט");
            if (z.ToMs < 0) return Lang.F("מ-{0} עד הסוף", Theme.Ltr(Tc.Short(z.FromMs)));
            if (z.FromMs < 0) return Lang.F("מההתחלה עד {0}", Theme.Ltr(Tc.Short(z.ToMs)));
            return Lang.F("מ-{0} עד {1}", Theme.Ltr(Tc.Short(z.FromMs)), Theme.Ltr(Tc.Short(z.ToMs)));
        }

        /// <summary>שורה ברשימה: ״טשטוש · מ-0:12.4 עד הסוף״.</summary>
        private static string Describe(BlurZone z)
        {
            string who = z.Kind == "face" ? Lang.T("פנים") + "  ·  " : z.Kind == "track" ? Lang.T("עוקב") + "  ·  " : "";
            return who + LookName(z.Look) + (z.Ellipse ? Lang.T(" (עיגול)") : "") + "  ·  " + WhenWords(z);
        }

        private BlurZone Cur { get { int i = _canvas.Selected; return i >= 0 && i < _work.Zones.Count ? _work.Zones[i] : null; } }

        private void Select(int i)
        {
            if (i >= _work.Zones.Count) i = _work.Zones.Count - 1;
            _canvas.Selected = i; _list.Selected = i; _bar.Selected = i;
            _list.EnsureVisible(i);
            RefreshAll();
        }

        private void RefreshAll()
        {
            BlurZone z = Cur;
            bool has = z != null;
            foreach (Control c in new Control[] { _zoneTitle, _when, _markIn, _markOut, _whole, _look, _shape, _pctCap, _pct }) c.Visible = has;
            _none.Visible = !has;
            if (has)
            {
                _syncing = true;
                _zoneTitle.Text = Lang.F("אזור {0}", Theme.Ltr((_canvas.Selected + 1).ToString()));
                _look.SelectedIndex = Math.Max(0, Math.Min(2, z.Look));
                _shape.SelectedIndex = z.Ellipse ? 1 : 0;
                _pct.Value = z.Pct;
                _pct.Visible = _pctCap.Visible = z.Look != BlurZone.Black;
                _syncing = false;
                if (DateTime.UtcNow > _flashUntil)
                {
                    _when.Text = Lang.F("מתי: {0}", WhenWords(z));
                    _when.Color = z.ActiveAt(_pos) ? Theme.TextDim : Theme.TextFaint;
                }
                _zoneTitle.Invalidate(); _when.Invalidate();
            }
            _canvas.Invalidate(); _list.Invalidate(); _bar.Invalidate();
        }

        /// <summary>הודעה קצרה במקום שורת ה״מתי״ - כשלחיצה לא יכולה לעשות את מה שביקשו.</summary>
        private void Flash(string text)
        {
            _when.Text = text;
            _when.Color = Theme.Warn;
            _when.Invalidate();
            _flashUntil = DateTime.UtcNow.AddSeconds(3);
        }

        // ---------- עריכה ----------

        /// <summary>צעד ביטול (Ctrl+Z). ‏<paramref name="kind"/> - פעולה רציפה (גרירת פס העוצמה) נרשמת פעם אחת.</summary>
        private void Push(string kind)
        {
            string key = kind + "#" + _canvas.Selected;
            if (kind == "pct" && key == _lastPush) return;
            _lastPush = key;
            List<BlurZone> snap = new List<BlurZone>();
            foreach (BlurZone z in _work.Zones) snap.Add(z.Clone());
            _undo.Add(snap);
            if (_undo.Count > 100) _undo.RemoveAt(0);
        }

        internal void Undo()
        {
            if (_undo.Count == 0) return;
            List<BlurZone> snap = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _work.Zones.Clear();
            _work.Zones.AddRange(snap);
            _lastPush = "";
            _changed = true;
            Select(Math.Min(_canvas.Selected, _work.Zones.Count - 1));
        }

        /// <summary>אזור חדש מגרירה על התמונה: מתחיל איפה שהנגן עומד (בהתחלה - כל הסרט), עד הסוף; המראה והצורה - כמו בפעם הקודמת.</summary>
        internal void AddZone(RectangleF area)
        {
            Push("add");
            BlurZone z = new BlurZone();
            z.Area = area;
            z.Look = _lastLook; z.Ellipse = _lastEllipse; z.Pct = _lastPct;
            z.FromMs = _pos >= 500 ? _pos : -1;
            z.ToMs = -1;
            _work.Zones.Add(z);
            _changed = true;
            Select(_work.Zones.Count - 1);
        }

        internal void RemoveZone(int i)
        {
            if (i < 0 || i >= _work.Zones.Count) return;
            Push("remove");
            _work.Zones.RemoveAt(i);
            _changed = true;
            Select(_work.Zones.Count == 0 ? -1 : Math.Min(i, _work.Zones.Count - 1));
        }

        /// <param name="start">״התחלה כאן״ - ואחרת ״סוף כאן״. בתחילת הסרט / בסוף - פתוח לאותו צד.</param>
        internal void MarkHere(bool start)
        {
            BlurZone z = Cur;
            if (z == null) return;
            long t = _pos;
            long dur = _mi != null ? _mi.DurationMs : long.MaxValue;
            if (start)
            {
                long from = t < 100 ? -1 : t;
                if (z.ToMs >= 0 && from >= z.ToMs) { Flash(Lang.T("ההתחלה צריכה להיות לפני הסוף. מזיזים את הנגן אחורה ולוחצים שוב.")); return; }
                Push("in"); z.FromMs = from;
            }
            else
            {
                long to = t >= dur - 100 ? -1 : t;
                if (to >= 0 && z.FromMs >= 0 && to <= z.FromMs) { Flash(Lang.T("הסוף צריך להיות אחרי ההתחלה. מזיזים את הנגן קדימה ולוחצים שוב.")); return; }
                Push("out"); z.ToMs = to;
            }
            _flashUntil = DateTime.MinValue;
            _changed = true;
            RefreshAll();
        }

        private void WholeVideo()
        {
            BlurZone z = Cur;
            if (z == null || (z.FromMs < 0 && z.ToMs < 0)) return;
            Push("whole");
            z.FromMs = -1; z.ToMs = -1;
            _changed = true;
            RefreshAll();
        }

        internal void SetLook(int look)
        {
            BlurZone z = Cur;
            if (z == null || z.Look == look) return;
            Push("look");
            z.Look = look; _lastLook = look;
            _changed = true;
            RefreshAll();
        }

        internal void SetShape(bool ellipse)
        {
            BlurZone z = Cur;
            if (z == null || z.Ellipse == ellipse) return;
            Push("shape");
            z.Ellipse = ellipse; _lastEllipse = ellipse;
            _changed = true;
            RefreshAll();
        }

        private void SetPct(int pct)
        {
            BlurZone z = Cur;
            if (z == null || z.Pct == pct) return;
            Push("pct");
            z.Pct = pct; _lastPct = pct;
            _changed = true;
            _canvas.Invalidate();
        }

        private void ShowMenu(int i, Point screen)
        {
            if (i < 0 || i >= _work.Zones.Count) return;
            Select(i);
            BlurZone z = _work.Zones[i];
            List<MenuItem> items = new List<MenuItem>();
            items.Add(MenuItem.Group(Lang.T("מראה")));
            for (int k = 0; k < 3; k++)
            {
                int look = k;
                items.Add(MenuItem.Make(LookName(k), "", z.Look == k ? Ico.Check : Ico.None, delegate { SetLook(look); }));
            }
            items.Add(MenuItem.Sep());
            items.Add(MenuItem.Make(z.Ellipse ? Lang.T("לשנות למלבן") : Lang.T("לשנות לעיגול"), "", Ico.None, delegate { SetShape(!z.Ellipse); }));
            items.Add(MenuItem.Make(Lang.T("התחלה כאן"), "", Ico.None, delegate { MarkHere(true); }));
            items.Add(MenuItem.Make(Lang.T("סוף כאן"), "", Ico.None, delegate { MarkHere(false); }));
            items.Add(MenuItem.Sep());
            items.Add(MenuItem.Make(Lang.T("מחיקת האזור"), "", Ico.Trash, delegate { RemoveZone(i); }));
            new PopupMenu(items, 220).ShowAt(screen);
        }

        // ---------- נגן ----------

        private void TogglePlay()
        {
            if (_main == null) return;
            if (_main.PlayerIsPlaying) _main.PausePlayer(); else _main.PlayPlayer();
        }

        internal void Seek(long t)
        {
            t = Math.Max(0, Math.Min(_mi != null ? _mi.DurationMs : t, t));
            if (_main != null) _main.SeekPlayer(t);
            ShowPos(t);
        }

        private void ShowPos(long t)
        {
            _pos = t;
            _canvas.PosMs = t; _bar.Position = t;
            string c = Theme.Ltr(Tc.Short(t) + " / " + Tc.Short(_mi != null ? _mi.DurationMs : 0));
            if (_clock.Text != c) { _clock.Text = c; _clock.Invalidate(); }
            RefreshAll();
        }

        private void OnFrame(Bitmap f)
        {
            if (f == null || IsDisposed) return;
            try { _canvas.SetFrame(new Bitmap(f)); }
            catch { }
        }

        private void OnTick()
        {
            if (_main == null || IsDisposed) return;
            long t = _main.PlayerPosition;
            if (t != _pos) ShowPos(t);
            else if (_flashUntil != DateTime.MinValue && DateTime.UtcNow > _flashUntil) { _flashUntil = DateTime.MinValue; RefreshAll(); }
            bool playing = _main.PlayerIsPlaying;
            if (_play.Icon != (playing ? Ico.Pause : Ico.Play)) { _play.Icon = playing ? Ico.Pause : Ico.Play; _play.Invalidate(); }
        }

        // ---------- פנים ----------

        private void FindFaces()
        {
            if (_scanning) { _faceStop = true; return; }
            if (_mi == null || !_mi.HasVideo) return;
            if (!Faces.Available)
            {
                Ui.Info(this, Lang.T("אין גלאי פנים"), Lang.T("בווינדוס הזה אין גלאי פנים מובנה (הוא קיים מווינדוס 10). את הפנים מסמנים ביד - גוררים על התמונה."));
                return;
            }
            _scanning = true; _faceStop = false;
            _faceBtn.Text = Lang.T("לעצור"); _faceBtn.Icon = Ico.Stop; _faceBtn.Invalidate();
            _canvas.Status = Lang.T("מחפש פנים בסרט..."); _canvas.Invalidate();
            string path = _mi.Path; MediaInfo mi = _mi;
            System.Threading.Thread th = new System.Threading.Thread(delegate ()
            {
                List<Faces.Hit> hits = null; Exception err = null;
                try { hits = Faces.Scan(path, mi, 0, mi.DurationMs, FaceProgress, delegate { return _faceStop; }); }
                catch (Exception ex) { err = ex; }
                try { BeginInvoke((Action)delegate { FacesDone(hits, err); }); } catch { }
            });
            th.IsBackground = true;
            th.Start();
        }

        private void FaceProgress(double f)
        {
            if (DateTime.UtcNow - _lastProg < TimeSpan.FromMilliseconds(250)) return;
            _lastProg = DateTime.UtcNow;
            try { BeginInvoke((Action)delegate { if (_scanning) { _canvas.Status = Lang.F("מחפש פנים בסרט... {0}%", Theme.Ltr(((int)(f * 100)).ToString())); _canvas.Invalidate(); } }); }
            catch { }
        }

        /// <summary>סוף החיפוש: האזורים נוספים (צעד ביטול אחד), הנגן עובר לפנים הראשונות, ו**ההודעה אומרת לעבור על הסרט** - גלאי מפספס לפעמים.</summary>
        private void FacesDone(List<Faces.Hit> hits, Exception err)
        {
            bool stopped = _faceStop;
            _scanning = false;
            if (IsDisposed) return;
            _faceBtn.Text = Lang.T("למצוא פנים"); _faceBtn.Icon = Ico.Sparkles; _faceBtn.Invalidate();
            _canvas.Status = null; _canvas.Invalidate();
            if (err != null) { Ui.Error(this, Lang.T("חיפוש הפנים נכשל"), ErrorText.Of(err)); return; }
            List<BlurZone> found = Faces.ToZones(hits ?? new List<Faces.Hit>(), _mi.DurationMs);
            if (found.Count == 0)
            {
                if (!stopped) Ui.Info(this, Lang.T("לא נמצאו פנים"), Lang.T("הגלאי לא מצא פנים בסרט. פנים קטנות מאוד, מהצד או בחושך הוא עלול לפספס - אותן מסמנים ביד."));
                return;
            }
            Push("faces");
            int first = _work.Zones.Count;
            _work.Zones.AddRange(found);
            _changed = true;
            Select(first);
            Seek(found[0].FromMs + 300);
            Ui.Info(this, Lang.T("נמצאו פנים"), (stopped ? Lang.T("החיפוש נעצר באמצע - נוספו רק הפנים שנמצאו עד שם.") + "\n" : "") +
                Lang.F("נוספו {0} אזורים של פנים, וכל אחד זז עם הפנים. כדאי לעבור על הסרט ולוודא שלא פוספס אף אחד: גלאי פנים מפספס לפעמים פנים קטנות, מהצד או בחושך - אותן מסמנים ביד.", Theme.Ltr(found.Count.ToString())));
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            // בבדיקות (``SUBSTUDIO_TEST``) לא שואלים: חלון שנסגר מחוץ למסך עם שאלה פתוחה - הבדיקה נתקעת עליה (test-screens, 22.10)
            if (!Ok && _changed && Visible && Environment.GetEnvironmentVariable("SUBSTUDIO_TEST") != "1" &&
                !Ui.Confirm(this, Lang.T("לצאת בלי לשמור?"), Lang.T("השינויים באזורים לא יישמרו."), Lang.T("לצאת בלי לשמור"), Lang.T("להמשיך לערוך")))
            {
                e.Cancel = true;
                return;
            }
            _faceStop = true;
            _timer.Stop();
            if (_main != null) { _main.FrameShown -= OnFrame; if (_main.PlayerIsPlaying) _main.PausePlayer(); }
        }

        protected override bool OnOk()
        {
            Result = new List<BlurZone>();
            foreach (BlurZone z in _work.Zones) Result.Add(z.Clone());
            return true;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // בזמן הקלדה (העוצמה) ובתיבות הבחירה - המקשים שלהן
            if (ActiveControl is TextBoxBase || ActiveControl is Combo || ActiveControl is Slider) return base.ProcessCmdKey(ref msg, keyData);
            Keys k = keyData & Keys.KeyCode;
            bool shift = (keyData & Keys.Shift) != 0, ctrl = (keyData & Keys.Control) != 0;
            if (ctrl && k == Keys.Z) { Undo(); return true; }
            if (ctrl) return base.ProcessCmdKey(ref msg, keyData);
            switch (k)
            {
                case Keys.Space: TogglePlay(); return true;
                case Keys.Delete: if (Cur != null) RemoveZone(_canvas.Selected); return true;
                case Keys.Left: Seek(_pos - (shift ? 5000 : 1000)); return true;
                case Keys.Right: Seek(_pos + (shift ? 5000 : 1000)); return true;
                case Keys.I: MarkHere(true); return true;
                case Keys.O: MarkHere(false); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); if (_main != null) _main.FrameShown -= OnFrame; }
            base.Dispose(disposing);
        }

        internal static readonly Color[] Colors =
        {
            Color.FromArgb(255, 99, 132), Color.FromArgb(54, 162, 235), Color.FromArgb(255, 193, 7), Color.FromArgb(76, 201, 140),
            Color.FromArgb(171, 120, 255), Color.FromArgb(255, 140, 66),
        };
        internal static Color ColorOf(int i) { return Colors[((i % Colors.Length) + Colors.Length) % Colors.Length]; }
    }

    /// <summary>התמונה עם האזורים: מה שמוסתר עכשיו מצויר כמו בצריבה (`VideoPreview` ← `RenderZones`), ומעליו קו לכל אזור שפעיל בזמן הזה,
    /// ולאזור שנבחר - פינות לגרירה (גם כשהוא לא פעיל עכשיו, מקווקו).</summary>
    internal class ZoneCanvas : VideoPreview
    {
        public List<BlurZone> Zones = new List<BlurZone>();
        public int Selected = -1;
        /// <summary>שורה על התמונה בזמן עבודה (חיפוש פנים); null - בלי.</summary>
        public string Status;
        public event Action<RectangleF> CreateZone;
        public event Action EditStarting, Edited, SelectionChanged;
        public event Action<int, Point> MenuFor;

        /// <summary>הכי קטן שאזור יכול להיות (יחסי) - מתחת ל-8 פיקסלים הצריבה מדלגת עליו (`BlurZone.Pixels_`).</summary>
        internal const float MinRel = 0.03f;

        private enum Mode { None, New, Move, Resize }
        private Mode _mode;
        private int _corner;
        private Point _down, _now;
        private RectangleF _startArea;
        private List<ZoneKey> _startPath;

        private void Begin(int i) { _startArea = Zones[i].AreaAt(PosMs); _startPath = Zones[i].Moving ? new List<ZoneKey>(Zones[i].Path) : null; }

        /// <summary>המלבן החדש (ברגע הזה). אזור שזז - כל המסלול זז ומשנה גודל באותה מידה: ההפרש במרכז, והיחס ברוחב ובגובה.</summary>
        private void Apply(RectangleF r)
        {
            BlurZone z = Zones[Selected];
            if (_startPath == null) { z.Area = r; return; }
            float sx = r.Width / Math.Max(0.001f, _startArea.Width), sy = r.Height / Math.Max(0.001f, _startArea.Height);
            float dx = (r.X + r.Width / 2) - (_startArea.X + _startArea.Width / 2), dy = (r.Y + r.Height / 2) - (_startArea.Y + _startArea.Height / 2);
            List<ZoneKey> moved = new List<ZoneKey>();
            foreach (ZoneKey k in _startPath)
            {
                float w = Math.Min(1, Math.Max(MinRel, k.Area.Width * sx)), h = Math.Min(1, Math.Max(MinRel, k.Area.Height * sy));
                float cx = k.Area.X + k.Area.Width / 2 + dx, cy = k.Area.Y + k.Area.Height / 2 + dy;
                moved.Add(new ZoneKey(k.Ms, new RectangleF(Math.Max(0, Math.Min(1 - w, cx - w / 2)), Math.Max(0, Math.Min(1 - h, cy - h / 2)), w, h)));
            }
            z.Path = moved;
        }
        private bool _moved;

        public ZoneCanvas()
        {
            AllowDrop = false;
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        private bool Shown_(int i) { return i >= 0 && i < Zones.Count && (i == Selected || Zones[i].ActiveAt(PosMs)); }

        private RectangleF ToScreen(RectangleF a)
        {
            RectangleF vr = VideoRect();
            return new RectangleF(vr.X + a.X * vr.Width, vr.Y + a.Y * vr.Height, a.Width * vr.Width, a.Height * vr.Height);
        }

        private PointF ToRel(Point p)
        {
            RectangleF vr = VideoRect();
            return new PointF(Math.Max(0, Math.Min(1, (p.X - vr.X) / Math.Max(1, vr.Width))), Math.Max(0, Math.Min(1, (p.Y - vr.Y) / Math.Max(1, vr.Height))));
        }

        private static PointF[] Corners(RectangleF r)
        {
            return new PointF[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) };
        }

        private int CornerAt(Point p)
        {
            if (!Shown_(Selected)) return -1;
            PointF[] cs = Corners(ToScreen(Zones[Selected].AreaAt(PosMs)));
            int hit = Theme.S(9);
            for (int i = 0; i < 4; i++)
                if (Math.Abs(p.X - cs[i].X) <= hit && Math.Abs(p.Y - cs[i].Y) <= hit) return i;
            return -1;
        }

        /// <summary>האזור שמתחת לעכבר - העליון (האחרון) קודם, ומה שנבחר לפני כולם.</summary>
        private int ZoneAt(Point p)
        {
            if (Shown_(Selected) && ToScreen(Zones[Selected].AreaAt(PosMs)).Contains(p)) return Selected;
            for (int i = Zones.Count - 1; i >= 0; i--)
                if (Shown_(i) && ToScreen(Zones[i].AreaAt(PosMs)).Contains(p)) return i;
            return -1;
        }

        private void SetSelected(int i)
        {
            if (Selected == i) return;
            Selected = i;
            if (SelectionChanged != null) SelectionChanged();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _down = _now = e.Location; _moved = false;
            if (e.Button == MouseButtons.Right)
            {
                int z = ZoneAt(e.Location);
                if (z >= 0) { SetSelected(z); if (MenuFor != null) MenuFor(z, PointToScreen(e.Location)); }
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int c = CornerAt(e.Location);
            if (c >= 0) { _mode = Mode.Resize; _corner = c; Begin(Selected); return; }
            int hit = ZoneAt(e.Location);
            if (hit >= 0) { SetSelected(hit); _mode = Mode.Move; Begin(hit); return; }
            _mode = VideoRect().Contains(e.Location) ? Mode.New : Mode.None;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_mode == Mode.None)
            {
                int c = CornerAt(e.Location);
                Cursor = c == 0 || c == 2 ? Cursors.SizeNWSE : c == 1 || c == 3 ? Cursors.SizeNESW
                       : ZoneAt(e.Location) >= 0 ? Cursors.SizeAll : VideoRect().Contains(e.Location) ? Cursors.Cross : Cursors.Default;
                return;
            }
            _now = e.Location;
            if (!_moved && Math.Abs(_now.X - _down.X) + Math.Abs(_now.Y - _down.Y) < Theme.S(3)) return;
            if (!_moved && (_mode == Mode.Move || _mode == Mode.Resize) && EditStarting != null) EditStarting();
            _moved = true;
            if (_mode == Mode.Move && Selected >= 0)
            {
                RectangleF vr = VideoRect();
                float dx = (_now.X - _down.X) / Math.Max(1, vr.Width), dy = (_now.Y - _down.Y) / Math.Max(1, vr.Height);
                RectangleF a = _startArea;
                a.X = Math.Max(0, Math.Min(1 - a.Width, a.X + dx));
                a.Y = Math.Max(0, Math.Min(1 - a.Height, a.Y + dy));
                Apply(a);
            }
            else if (_mode == Mode.Resize && Selected >= 0)
            {
                PointF p = ToRel(_now);
                float l = _startArea.Left, t = _startArea.Top, r = _startArea.Right, b = _startArea.Bottom;
                if (_corner == 0 || _corner == 3) l = Math.Min(p.X, r - MinRel); else r = Math.Max(p.X, l + MinRel);
                if (_corner == 0 || _corner == 1) t = Math.Min(p.Y, b - MinRel); else b = Math.Max(p.Y, t + MinRel);
                Apply(RectangleF.FromLTRB(l, t, r, b));
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Mode m = _mode;
            _mode = Mode.None;
            if (m == Mode.New)
            {
                RectangleF a = NewRect();
                if (_moved && a.Width >= MinRel && a.Height >= MinRel) { if (CreateZone != null) CreateZone(a); }
                else SetSelected(-1);
            }
            else if ((m == Mode.Move || m == Mode.Resize) && _moved && Edited != null) Edited();
            Invalidate();
            base.OnMouseUp(e);
        }

        private RectangleF NewRect()
        {
            PointF a = ToRel(_down), b = ToRel(_now);
            return RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < Zones.Count; i++)
            {
                if (!Shown_(i) || i == Selected) continue;
                DrawZone(g, i, false);
            }
            if (Shown_(Selected)) DrawZone(g, Selected, true);

            if (_mode == Mode.New && _moved)
            {
                RectangleF r = ToScreen(NewRect());
                using (Pen p = new Pen(Color.White, 1.5f)) { p.DashStyle = DashStyle.Dash; g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height); }
            }
            else if (Zones.Count == 0 && Status == null)
                Pill(g, Lang.T("גוררים על התמונה כדי לסמן אזור להסתרה"), Height - Theme.S(40));
            if (Status != null) Pill(g, Status, Theme.S(12));
        }

        private void DrawZone(Graphics g, int i, bool sel)
        {
            BlurZone z = Zones[i];
            RectangleF r = ToScreen(z.AreaAt(PosMs));
            bool live = z.ActiveAt(PosMs);
            Color c = ZonesDlg.ColorOf(i);
            using (Pen p = new Pen(live ? c : Color.FromArgb(170, c), sel ? 2.2f : 1.4f))
            {
                if (!live) p.DashStyle = DashStyle.Dash;
                if (z.Ellipse) g.DrawEllipse(p, r);
                else g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                if (sel && z.Ellipse)
                    using (Pen q = new Pen(Color.FromArgb(120, c), 1f)) { q.DashStyle = DashStyle.Dot; g.DrawRectangle(q, r.X, r.Y, r.Width, r.Height); }
            }
            // מספר האזור - כמו ברשימה
            float d = Theme.S(18);
            RectangleF badge = new RectangleF(r.X + Theme.S(4), r.Y + Theme.S(4), d, d);
            using (SolidBrush b = new SolidBrush(live ? c : Color.FromArgb(170, c))) g.FillEllipse(b, badge);
            Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Color.FromArgb(20, 20, 24), badge, Theme.SfCenter);
            if (!sel) return;
            float hs = Theme.S(8);
            foreach (PointF pt in Corners(r))
            {
                RectangleF h = new RectangleF(pt.X - hs / 2, pt.Y - hs / 2, hs, hs);
                g.FillRectangle(Brushes.White, h);
                using (Pen p = new Pen(c, 1.2f)) g.DrawRectangle(p, h.X, h.Y, h.Width, h.Height);
            }
            if (!live) Pill(g, Lang.T("האזור שנבחר לא מוסתר בזמן הזה"), Height - Theme.S(40));
        }

        /// <summary>הודעה קטנה על התמונה, על רקע כהה שקוף.</summary>
        private void Pill(Graphics g, string text, float y)
        {
            Size sz = TextRenderer.MeasureText(text, Theme.Ui);
            float w = Math.Min(Width - Theme.S(20), sz.Width + Theme.S(28)), h = Theme.S(30);
            RectangleF r = new RectangleF((Width - w) / 2f, y, w, h);
            Theme.FillRound(g, r, h / 2f, Color.FromArgb(185, 12, 13, 16));
            Theme.Str(g, text, Theme.Ui, Color.FromArgb(235, 255, 255, 255), r, Theme.SfCenter);
        }
    }

    /// <summary>פס הזמן: מתי כל אזור מוסתר (פס צבעוני לכל אזור, עד שלושה בגובה), והנגן. לחיצה וגרירה מזיזות את הנגן. הזמן תמיד משמאל לימין.</summary>
    internal class ZoneTimeBar : Control
    {
        public List<BlurZone> Zones = new List<BlurZone>();
        public long Duration = 1, Position;
        public int Selected = -1;
        /// <summary>סימן סגול אחד על הפס (חלון הסנכרון: איפה המשפט עכשיו בכתוביות); ‎-1‎ - בלי.</summary>
        public long Mark = -1;
        public event Action<long> SeekTo;
        private bool _drag;

        public ZoneTimeBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Cursor = Cursors.Hand;
        }

        private float Inner { get { return Theme.S(10); } }
        private float X(long t) { return Inner + (Width - 2 * Inner) * (float)Math.Max(0, Math.Min(1, t / (double)Math.Max(1, Duration))); }
        private long T(int x) { return (long)Math.Round(Math.Max(0, Math.Min(1, (x - Inner) / Math.Max(1, Width - 2 * Inner))) * Duration); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            _drag = true;
            if (SeekTo != null) SeekTo(T(e.X));
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (_drag && SeekTo != null) SeekTo(T(e.X)); }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            Surface.Inset(g, new RectangleF(0, 0, Width, Height), Theme.S(8), Theme.PanelAlt, false, 0f);
            float lane = Theme.S(5), top = Theme.S(9);
            for (int i = 0; i < Zones.Count; i++)
            {
                BlurZone z = Zones[i];
                float x1 = X(z.FromMs < 0 ? 0 : z.FromMs), x2 = X(z.ToMs < 0 ? Duration : z.ToMs);
                float y = top + (i % 3) * (lane + Theme.S(3));
                Color c = ZonesDlg.ColorOf(i);
                Theme.FillRound(g, new RectangleF(x1, y, Math.Max(Theme.S(3), x2 - x1), lane), lane / 2f, i == Selected ? c : Color.FromArgb(150, c));
            }
            if (Mark >= 0)
            {
                float mx = X(Mark);
                using (SolidBrush b = new SolidBrush(Theme.Purple))
                    g.FillPolygon(b, new PointF[] { new PointF(mx - Theme.S(5), Theme.S(3)), new PointF(mx + Theme.S(5), Theme.S(3)), new PointF(mx, Theme.S(10)) });
            }
            float px = X(Position);
            using (Pen p = new Pen(Theme.Text, 2f)) g.DrawLine(p, px, Theme.S(4), px, Height - Theme.S(4));
            using (SolidBrush b = new SolidBrush(Theme.Text)) g.FillEllipse(b, px - Theme.S(4), Height - Theme.S(10), Theme.S(8), Theme.S(8));
        }
    }

    /// <summary>רשימת האזורים: עיגול בצבע עם המספר, מה ומתי, ופח. קליק ימני - אותו תפריט כמו על התמונה. מצוירת ביד, כמו `WordList`.</summary>
    internal class ZoneList : Control
    {
        public List<BlurZone> Zones = new List<BlurZone>();
        public Func<BlurZone, string> Describe;
        public int Selected = -1;
        public event Action SelectionChanged;
        public event Action<int> RemoveClicked;
        public event Action<int, Point> MenuFor;
        private int _hover = -1, _scroll;
        private bool _hoverTrash;

        public ZoneList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Theme.Panel;
            Font = Theme.Ui;
        }

        private int RowH { get { return Theme.S(32); } }
        private int PadV { get { return Theme.S(5); } }
        private int ViewH { get { return Math.Max(1, Height - PadV * 2); } }

        public void EnsureVisible(int i)
        {
            if (i < 0) { Invalidate(); return; }
            int top = i * RowH, bottom = top + RowH;
            if (top < _scroll) _scroll = top;
            else if (bottom > _scroll + ViewH) _scroll = bottom - ViewH;
            _scroll = Math.Max(0, Math.Min(_scroll, Math.Max(0, Zones.Count * RowH - ViewH)));
            Invalidate();
        }

        private int RowAt(int y)
        {
            if (y < PadV || y >= Height - PadV) return -1;
            int i = (y - PadV + _scroll) / RowH;
            return i >= 0 && i < Zones.Count ? i : -1;
        }

        /// <summary>הפח - בקצה השמאלי בעברית, בימני באנגלית.</summary>
        private Rectangle TrashRect(int row)
        {
            int s = Theme.S(26);
            int y = PadV + row * RowH - _scroll + (RowH - s) / 2;
            return Lang.Rtl ? new Rectangle(Theme.S(8), y, s, s) : new Rectangle(Width - Theme.S(8) - s, y, s, s);
        }

        private void Pick(int i)
        {
            if (i == Selected) return;
            Selected = i;
            EnsureVisible(i);
            if (SelectionChanged != null) SelectionChanged();
        }

        protected override bool IsInputKey(Keys k)
        {
            Keys c = k & Keys.KeyCode;
            return c == Keys.Up || c == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up && Zones.Count > 0) { Pick(Math.Max(0, Selected - 1)); e.Handled = true; }
            else if (e.KeyCode == Keys.Down && Zones.Count > 0) { Pick(Math.Min(Zones.Count - 1, Selected + 1)); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = RowAt(e.Y);
            bool t = h >= 0 && TrashRect(h).Contains(e.Location);
            if (h != _hover || t != _hoverTrash) { _hover = h; _hoverTrash = t; Cursor = t ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; _hoverTrash = false; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int r = RowAt(e.Y);
            if (r < 0) return;
            if (e.Button == MouseButtons.Left && TrashRect(r).Contains(e.Location)) { if (RemoveClicked != null) RemoveClicked(r); return; }
            Pick(r);
            if (e.Button == MouseButtons.Right && MenuFor != null) MenuFor(r, PointToScreen(e.Location));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            _scroll = Math.Max(0, Math.Min(_scroll - e.Delta / 120 * RowH, Math.Max(0, Zones.Count * RowH - ViewH)));
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            using (SolidBrush b = new SolidBrush(Theme.Panel)) g.FillRectangle(b, ClientRectangle);
            Surface.Inset(g, new RectangleF(0, 0, Width, Height), Theme.S(8), Theme.PanelAlt, Focused, 0f);
            if (Zones.Count == 0)
            {
                Theme.Str(g, Lang.T("עוד אין אזורים."), Theme.Ui, Theme.TextFaint,
                    new RectangleF(Theme.S(12), 0, Width - Theme.S(24), Height), Theme.SfCenter);
                return;
            }
            Region old = g.Clip;
            g.SetClip(new Rectangle(0, PadV, Width, ViewH));
            for (int i = 0; i < Zones.Count; i++)
            {
                int y = PadV + i * RowH - _scroll;
                if (y + RowH < 0 || y > Height) continue;
                RectangleF pill = new RectangleF(Theme.S(5), y + Theme.S(2), Width - Theme.S(10), RowH - Theme.S(4));
                if (i == Selected) Theme.FillRound(g, pill, Theme.S(7), Focused ? Theme.AccentSoft : Theme.Mix(Theme.PanelAlt, Theme.Hover, 0.9f));
                else if (i == _hover) Theme.FillRound(g, pill, Theme.S(7), Theme.Mix(Theme.PanelAlt, Theme.Hover, 0.75f));
                // המספר בעיגול בצבע של האזור - בתחילת השורה (מימין בעברית)
                float d = Theme.S(20);
                RectangleF badge = Lang.Rtl ? new RectangleF(pill.Right - Theme.S(8) - d, y + (RowH - d) / 2f, d, d)
                                            : new RectangleF(pill.Left + Theme.S(8), y + (RowH - d) / 2f, d, d);
                using (SolidBrush b = new SolidBrush(ZonesDlg.ColorOf(i))) g.FillEllipse(b, badge);
                Theme.Str(g, (i + 1).ToString(), Theme.SmallBold, Color.FromArgb(20, 20, 24), badge, Theme.SfCenter);
                Rectangle tr = TrashRect(i);
                RectangleF text = Lang.Rtl
                    ? RectangleF.FromLTRB(tr.Right + Theme.S(6), y, badge.Left - Theme.S(8), y + RowH)
                    : RectangleF.FromLTRB(badge.Right + Theme.S(8), y, tr.Left - Theme.S(6), y + RowH);
                string s = Describe != null ? Describe(Zones[i]) : "";
                Theme.Str(g, s, Theme.Small, Theme.Text, text, Theme.SfUi);
                bool hot = i == _hover && _hoverTrash;
                if (hot) Theme.FillRound(g, tr, Theme.S(6), Theme.Mix(Theme.PanelAlt, Theme.Bad, 0.16f));
                int ic = Theme.S(14);
                Icons.Draw(g, Ico.Trash, new RectangleF(tr.X + (tr.Width - ic) / 2f, tr.Y + (tr.Height - ic) / 2f, ic, ic),
                    hot ? Theme.Bad : (i == _hover || i == Selected ? Theme.TextDim : Theme.TextFaint), 1.6f);
            }
            g.Clip = old;
        }
    }
}
