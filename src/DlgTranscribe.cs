using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SubtitleStudio
{
    /// <summary>ההסבר וההסכמה לפני שהשמע עוזב את המחשב.
    ///
    /// כל הקיום של התוכנה הוא ״בלי אינטרנט״, והמשתמש הסכים בעבר רק לשליחת
    /// **טקסט** לתרגום. שליחת הקלטה היא הסלמה, ולכן היא נאמרת במפורש
    /// ומאושרת בנפרד - לא מונחת כמובנת מאליה.</summary>
    internal class TranscribeDlg : Dlg
    {
        private readonly Field _context;
        private readonly Toggle _replace, _range;
        private readonly long _a, _b;
        private readonly MediaInfo _mi;
        private readonly Btn _optLocal, _optGoogle, _optGroq, _connect;
        private readonly Lbl _warn, _warn2, _info, _ctxHint;
        private string _choice;

        public string Context { get { return _context.Text.Trim(); } }
        /// <summary>**לא `_replace.Visible`** - זה נקרא אחרי שהחלון נסגר, וחלון סגור מוסתר: כל פקד בו ״לא גלוי״. מ-0.6.0 עד 0.8.40 המתג
        /// ״למחוק את הכתוביות הקיימות״ (שדולק מראש) לא עבד אף פעם - מי שתמלל שוב קיבל את החדשות על הישנות.</summary>
        public bool ReplaceExisting { get { return _hasExisting && _replace.Checked; } }
        private readonly bool _hasExisting;
        /// <summary>הקטע לתמלול, או 0/0 - כל הסרט.</summary>
        public long FromMs { get { return _range.Checked ? _a : 0; } }
        public long ToMs { get { return _range.Checked ? _b : 0; } }
        public ISttProvider Provider { get { return Stt.ById(_choice) ?? Stt.Gemini; } }

        public TranscribeDlg(MediaInfo mi, int existingCues) : this(mi, existingCues, "") { }

        /// <summary><paramref name="context"/>: הרקע מהתמלול הקודם של אותו סרט. מי שמתמלל שוב (התוצאה לא
        /// הייתה טובה) לא צריך לכתוב את השמות מחדש.</summary>
        public TranscribeDlg(MediaInfo mi, int existingCues, string context) : this(mi, existingCues, context, -1, -1) { }

        /// <summary><paramref name="inMs"/>/<paramref name="outMs"/>: הקטע המסומן על הציר (‎-1 = לא סומן).</summary>
        public TranscribeDlg(MediaInfo mi, int existingCues, string context, long inMs, long outMs) : base(Lang.T("תמלול אוטומטי"), Ico.Sparkles, 620)
        {
            _mi = mi;
            Subtitle = Lang.T("התוכנה מקשיבה לסרט וכותבת את הכתוביות");
            _choice = Stt.Current.Id;

            // **הבחירה כאן ולא בהגדרות.** חלון ההגדרות כבר על גבול הגובה, והמקום
            // שבו מחליטים איפה לתמלל הוא הרגע שבו מתמללים.
            // **שלוש אפשרויות בשורה אחת (0.8.45):** על המחשב, ושני השירותים ברשת. בשליש רוחב נכנסות רק שתי-שלוש
            // מילים, אז כל כרטיס אומר את הדבר האחד שמבדיל אותו; ״מפתח חינמי״ כתוב על הכפתור שמתחת. בלי אייקונים -
            // העיגול מספיק, והטקסט צריך את המקום
            Section(Lang.T("איפה לתמלל"));
            int gapX = Theme.S(10);
            int third = (ContentW - 2 * gapX) / 3;
            // **בטא** (בנימין, 16.11): כל מה שרץ על מודל מקומי מסומן כך - זה חדש, ועדיין בפיתוח
            _optLocal = Option(Lang.T("על המחשב"), Lang.T("בלי אינטרנט · בטא"), "local");
            _optLocal.SetBounds(Pad + 2 * (third + gapX), Y, ContentW - 2 * (third + gapX), Theme.S(56));
            _optGoogle = Option(Lang.T("גוגל"), Lang.T("מכסה יומית קטנה"), "gemini");
            _optGoogle.SetBounds(Pad + third + gapX, Y, third, Theme.S(56));
            _optGroq = Option("Groq", Lang.T("עד 8 שעות ביום"), "groq");
            _optGroq.SetBounds(Pad, Y, third, Theme.S(56));
            Y += Theme.S(64);

            _connect = new Btn();
            _connect.Kind = BtnKind.Ghost;
            _connect.Icon = Ico.Key;
            _connect.Click += delegate { Connect(); };
            Row(_connect, 38, 12);

            // האזהרה היא העיקר כאן, ולכן היא בולטת ולא הערת שוליים
            Section(Lang.T("לפני שמתחילים"));
            _warn = Label("", true, Theme.Warn);
            Row(_warn, 24, 2);
            _warn2 = Hint(CloudNote);
            Row(_warn2, 44, 8);

            _info = Hint("");
            // רק חלק מהסרט: הקטע שסומן על הציר (״תחילת קטע״ / ״סוף קטע״), כמו בהטמעה ובכלים. בלי סימון
            // המתג כבוי ואומר איך מסמנים - כך רואים שהאפשרות קיימת
            _range = new Toggle();
            bool marked = mi != null && (inMs >= 0 || outMs >= 0);
            _a = Math.Max(0, inMs);
            _b = mi != null && outMs > _a ? outMs : (mi != null ? mi.DurationMs : 0);
            marked = marked && _b - _a >= 2000;
            _range.Enabled = marked;
            _range.Text = marked
                ? Lang.F("רק הקטע המסומן על הציר: {0}", Theme.Ltr(Tc.Short(_a) + " – " + Tc.Short(_b)))
                : Lang.T("רק חלק מהסרט? מסמנים אותו קודם על הציר");
            _range.CheckedChanged += delegate { Refresh_(); };
            if (mi != null)
            {
                Section(Lang.T("הקובץ"));
                Row(_info, 44, 4);
                Row(_range, 26, 8);
            }

            _replace = new Toggle();
            _replace.Text = Lang.T("למחוק את הכתוביות הקיימות ולהתחיל מחדש");
            _replace.Checked = true;
            _hasExisting = existingCues > 0;
            _replace.Visible = _hasExisting;
            if (_hasExisting)
            {
                Row(_replace, 28, 6);
                Lbl rh = Hint(Lang.T("אם לא - הכתוביות החדשות יתווספו לקיימות."));
                Row(rh, 22, 10);
            }

            Section(Lang.T("רקע על התוכן (לא חובה)"));
            _context = new Field();
            _context.Placeholder = Lang.T("למשל: שיעור בגמרא · שמות הדוברים · מונחים מיוחדים");
            if (!string.IsNullOrEmpty(context)) _context.Text = context;
            Row(_context, 40, 4);
            // שם שהמודל לא מכיר יוצא משובש או נעלם. כאן אפשר להגיד לו מראש - וזה עובר גם לתרגום
            _ctxHint = Hint(CloudContext);
            Row(_ctxHint, 22, 6);

            Buttons(Lang.T("להתחיל בתמלול"), Ico.Sparkles, Lang.T("ביטול"));
            Refresh_();
        }

        private static string CloudNote { get { return Lang.T("הקול נשלח לשירות שבחרתם רק כדי לתמלל אותו. אם ההקלטה רגישה, עדיף לתמלל על המחשב - בלי אינטרנט.\r\nהתוצאה נכנסת לעורך כמו כל כתובית, וכדאי לעבור עליה ולתקן."); } }
        private static string LocalNote { get { return Lang.T("התמלול על המחשב בבטא: הוא יודע עברית בלבד, ופחות מדויק מהשירותים ברשת - בעיקר בשמות ובמונחים.\r\nהתוצאה נכנסת לעורך כמו כל כתובית, וכדאי לעבור עליה ולתקן."); } }
        private static string CloudContext { get { return Lang.T("שמות של אנשים, מקומות וחברות שכתובים כאן ייכתבו נכון - בלי זה המודל מנחש. הרקע עובר גם לתרגום."); } }
        private static string LocalContext { get { return Lang.T("שמות, מקומות ומונחים בעברית שכתובים כאן ייכתבו נכון יותר. הרקע עובר גם לתרגום."); } }

        private Btn Option(string text, string sub, string id)
        {
            Btn b = new Btn();
            b.Text = text;
            b.Sub = sub;
            b.Kind = BtnKind.Subtle;
            b.Radio = true;
            b.Click += delegate { Choose(id); };
            Controls.Add(b);
            return b;
        }

        private void Choose(string id)
        {
            _choice = id;
            Refresh_();
        }

        /// <summary>מעדכן את כל מה שתלוי בבחירה: הסימון, הכפתור לחיבור, שם
        /// השירות באזהרה, וההערכה כמה זמן זה ייקח.</summary>
        private void Refresh_()
        {
            ISttProvider p = Provider;
            bool local = p.Id == "local";
            _optLocal.Checked = local;
            _optGoogle.Checked = p.Id == "gemini";
            _optGroq.Checked = p.Id == "groq";
            _optLocal.Invalidate();
            _optGoogle.Invalidate();
            _optGroq.Invalidate();

            if (local)
            {
                // הורדה פעם אחת, באותו מקום שבו השירותים ברשת מבקשים מפתח. אחרי שהורד - אין מה ללחוץ כאן
                _connect.Text = Lang.F("הורדה של {0}, פעם אחת", LocalModel.DownloadText);
                _connect.Icon = Ico.Download;
                _connect.Kind = BtnKind.Subtle;
                _connect.Visible = !p.HasKey;
                // **האזהרה נעלמת כשהיא לא נכונה** (ISttProvider.SendsAudioOut): אחרת היא מאבדת משמעות גם כשהיא נכונה
                _warn.Text = Lang.T("הקול נשאר במחשב. שום דבר לא נשלח.");
                _warn.Color = Theme.Good;
            }
            else
            {
                _connect.Text = p.HasKey
                    ? Lang.F("החלפת המפתח של {0}", p.Name)
                    : Lang.F("{0} - מפתח חינמי, פעם אחת", Theme.Pfx(Lang.T("חיבור ל"), p.Name));
                _connect.Icon = Ico.Key;
                _connect.Kind = p.HasKey ? BtnKind.Ghost : BtnKind.Subtle;
                _connect.Visible = true;
                // אחרי Groq גם הטקסט הולך לגוגל, לבדיקת שגיאות השמיעה (TranscribeRunDlg.ProofreadOn) - זה נאמר כאן, לא קורה בשקט
                _warn.Text = TranscribeRunDlg.ProofreadOn(p)
                    ? Theme.Pfx(Lang.T("הקול מהסרט יישלח ל"), p.Name) + Lang.T(", והטקסט - לגוגל, לבדיקת שמות.")
                    : Theme.Pfx(Lang.T("הקול מהסרט יישלח ל"), p.Name) + ".";
                _warn.Color = Theme.Warn;
            }
            _connect.Invalidate();
            _warn.Invalidate();
            _warn2.Text = local ? LocalNote : CloudNote;
            _warn2.Invalidate();
            _ctxHint.Text = local ? LocalContext : CloudContext;
            _ctxHint.Invalidate();

            _replace.Text = _range.Checked ? Lang.T("למחוק את הכתוביות הקיימות בקטע הזה") : Lang.T("למחוק את הכתוביות הקיימות ולהתחיל מחדש");
            _replace.Invalidate();

            if (_mi != null)
            {
                long dur = _range.Checked ? _b - _a : _mi.DurationMs;
                int step = Math.Max(1, p.ChunkSec - Transcribe.OverlapSec);
                int chunks = Math.Max(1, (int)Math.Ceiling((dur / 1000.0) / step));
                // גוגל נמדד: כ-13 שניות לקטע, כולל ההמתנה למכסה. ‏Groq **הערכה,
                // עוד לא נמדד** (אין מפתח): 3.2 שניות המתנה, העלאה של 700KB,
                // ותמלול שלוקח שנייה-שתיים. לעדכן אחרי תמלול אמיתי.
                double perChunk = p.Id == "groq" ? 6 : 13;
                int mins = Math.Max(1, (int)Math.Ceiling(chunks * perChunk / 60.0));
                // על המחשב: לפי אורך הקול. נמדד פי 20-30 מזמן אמת על מחשב נייד; ההערכה זהירה (מחשב איטי)
                if (local) mins = Math.Max(1, (int)Math.Ceiling(dur / 1000.0 / LocalStt.SpeedGuess / 60.0));
                // המספר בסוף המשפט ולא באמצעו: ‏Ltr באמצע טקסט עברי הפך את
                // "40 שניות · 1 קטעים" ל-"1 · 40 שניות קטעים"
                _info.Text = Lang.F("אורך: {0}\r\n{1}אפשר לעצור באמצע, ומה שכבר תומלל יישמר.", Theme.Ltr(Tc.Length(dur)), (mins == 1 ? Lang.T("לוקח בערך דקה. ") : Lang.F("לוקח בערך {0} דקות. ", Theme.Ltr(mins.ToString()))));
                _info.Invalidate();
            }
            Restack();
        }

        private void Connect()
        {
            if (Provider.Id == "local")
            {
                LocalSetupDlg d = new LocalSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
            }
            else if (_choice == "groq")
            {
                GroqSetupDlg d = new GroqSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
            }
            else
            {
                AiSetupDlg d = new AiSetupDlg();
                d.ShowDialog(this);
                d.Dispose();
            }
            Refresh_();
        }

        protected override bool OnOk()
        {
            if (_mi == null || _mi.DurationMs <= 0)
            {
                Ui.Error(this, Lang.T("אין סרט"), Lang.T("צריך לפתוח קודם סרט או קובץ קול."));
                return false;
            }
            if (!Provider.HasKey)
            {
                Connect();
                if (!Provider.HasKey) return false;
            }
            Stt.ProviderId = Provider.Id;
            Settings.SaveAll();
            return true;
        }
    }

    /// <summary>ההורדה החד-פעמית של התמלול על המחשב (0.8.45): מה יורד, כמה, ומה כדאי לדעת לפני.
    ///
    /// אותו עיקרון כמו במילון (`SpellSetupDlg`): זו פנייה לאינטרנט, והמשתמש צריך לדעת שמשהו יורד, כמה, ושאחרי
    /// זה הכול עובד בלי רשת. **סגירה באמצע לא מאבדת כלום** - ההורדה ממשיכה בפעם הבאה מאותו מקום.</summary>
    internal class LocalSetupDlg : Dlg
    {
        private readonly Lbl _status;
        private readonly ProgressBarLite _bar;
        private readonly Btn _ok;
        private volatile bool _busy, _cancel;

        public LocalSetupDlg() : base(Lang.T("תמלול על המחשב (בטא)"), Ico.Download, 580)
        {
            Subtitle = Lang.T("בלי אינטרנט, בלי מפתח, והקול נשאר אצלכם");

            // בטא נאמר לפני הכול: מי שמוריד 62 מגה צריך לדעת שזה חדש
            Lbl beta = Label(Lang.T("גרסת בטא - התכונה חדשה, ועדיין בפיתוח."), true, Theme.Warn);
            Row(beta, 24, 8);

            Section(Lang.T("מה צריך"));
            Row(Hint(Lang.F("מנוע תמלול ומודל לעברית. הם חינמיים, שוקלים {0}, ויורדים פעם אחת. אחרי זה התמלול עובד בלי אינטרנט.", LocalModel.DownloadText)), 22, 10);

            Section(Lang.T("מה כדאי לדעת"));
            Row(Hint(Lang.T("המודל יודע עברית בלבד. הוא מהיר - שיעור של שעה מתומלל בכמה דקות - אבל פחות מדויק מהשירותים ברשת, בעיקר בשמות ובמונחים.")), 22, 4);
            Row(Hint(Lang.T("אפשר לעצור את ההורדה באמצע ולהמשיך אחר כך מאותו מקום. מחיקה - בהגדרות, תחת ״אחסון״.")), 22, 4);
            // CC-BY: הקרדיט נאמר למשתמש, לא רק בקובץ שליד המודל
            Row(Hint(Lang.F("המודל פותח על ידי יעקב קולני, על הקלטות של {0}. המנוע: {1}.", Theme.Ltr("ivrit.ai"), Theme.Ltr("sherpa-onnx"))), 22, 12);

            // שורת המצב והפס מופיעים רק כשמתחילים להוריד. לפני זה הם רק חור מעל הכפתורים.
            _status = Hint("");
            Row(_status, 22, 4);
            _bar = new ProgressBarLite();
            Row(_bar, 10, 8);

            _ok = Buttons(Lang.T("להוריד"), Ico.Download, Lang.T("ביטול"));
            _status.Visible = false;
            _bar.Visible = false;
            Restack();
            FormClosing += delegate { _cancel = true; };
        }

        protected override bool OnOk()
        {
            if (_busy) return false;
            if (!LocalModel.Supported)
            {
                _status.Visible = true;
                Restack();
                Say(Lang.T("התמלול על המחשב צריך ווינדוס של 64 ביט."), Theme.Bad);
                return false;
            }
            _busy = true;
            _cancel = false;
            _ok.Enabled = false;
            _ok.Invalidate();
            _bar.Value = 0;
            _bar.Visible = true;
            _status.Visible = true;
            Restack();
            Say(Lang.T("מוריד…"), Theme.TextDim);

            Thread t = new Thread(delegate ()
            {
                string err;
                bool ok = LocalModel.Download(
                    delegate (long done, long total)
                    {
                        Post(delegate
                        {
                            _bar.Value = total > 0 ? done / (float)total : 0;
                            Say(Lang.F("מוריד… {0} מתוך {1}", Theme.Ltr(MediaInfo.FormatSize(done)), Theme.Ltr(MediaInfo.FormatSize(total))), Theme.TextDim);
                        });
                    },
                    delegate { return _cancel; },
                    out err);
                Post(delegate
                {
                    _busy = false;
                    _ok.Enabled = true;
                    _ok.Invalidate();
                    if (ok) { Ok = true; Close(); }
                    else
                    {
                        _bar.Visible = false;
                        Restack();
                        Say(err ?? Lang.T("ההורדה לא הצליחה."), Theme.Bad);
                    }
                });
            });
            t.IsBackground = true;
            t.Start();
            return false;
        }

        private void Post(MethodInvoker m)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(m); }
            catch { }
        }

        private void Say(string s, Color c)
        {
            _status.Text = s;
            _status.Color = c;
            _status.Invalidate();
        }
    }

    /// <summary>מפתח חינמי ל-Groq. אותו מבנה כמו החיבור לגוגל: שלושה צעדים,
    /// כפתור שפותח את הדף, שדה, ובדיקה - שלא עולה אף שנייה מהמכסה, כי היא רק
    /// מבקשת את רשימת הדגמים.</summary>
    internal class GroqSetupDlg : Dlg
    {
        private readonly Field _key;
        private readonly Lbl _status;
        private readonly Btn _test;
        private bool _busy;

        private static readonly string[][] Steps = new string[][]
        {
            new string[] { Lang.T("נכנסים לאתר של Groq"), Lang.T("הכפתור למטה פותח אותו. אפשר להירשם עם חשבון גוגל.") },
            new string[] { Lang.T("לוחצים ״Create API Key״"), Lang.F("מקבלים מחרוזת ארוכה שמתחילה ב-{0}. בלי כרטיס אשראי.", Theme.Ltr("gsk_")) },
            new string[] { Lang.T("מעתיקים ומדביקים כאן"), Lang.T("המפתח נשמר מוצפן במחשב שלכם בלבד.") }
        };

        public GroqSetupDlg() : base(Lang.T("חיבור ל-Groq"), Ico.Mic, 600)
        {
            Subtitle = Lang.T("תמלול של עד 8 שעות הקלטה ביום, בחינם");

            AiSetupDlg.StepsView steps = new AiSetupDlg.StepsView(Steps);
            Row(steps, Steps.Length * 44 + 4, 12);

            Btn open = new Btn();
            open.Text = Lang.T("פתיחת האתר של Groq");
            open.Icon = Ico.Key;
            open.Kind = BtnKind.Subtle;
            open.Click += delegate
            {
                try { System.Diagnostics.Process.Start(Stt.GroqKeyPage); }
                catch { Ui.Error(this, Lang.T("לא הצלחתי לפתוח את הדפדפן"), Stt.GroqKeyPage); }
            };
            Row(open, 42, 14);

            Section(Lang.T("המפתח שקיבלתם"));
            _key = new Field();
            _key.Placeholder = Lang.T("מדביקים כאן את המפתח");
            _key.Ltr = true;
            _key.Box.Text = Stt.GroqKey;
            Row(_key, 40, 8);

            _test = new Btn();
            _test.Text = Lang.T("בדיקת חיבור");
            _test.Icon = Ico.Check;
            _test.Kind = BtnKind.Subtle;
            _test.Click += delegate { TestKey(); };
            Row(_test, 38, 6);

            _status = Hint("");
            Row(_status, 40, 4);

            Row(Hint(Lang.T("הקול נשלח לשרתים של Groq לצורך התמלול בלבד. אל תשתמשו בזה על הקלטות רגישות.")), 40, 0);

            Buttons(Lang.T("שמירה"), Ico.Save, Lang.T("ביטול"));
        }

        private void TestKey()
        {
            if (_busy) return;
            string k = _key.Text.Trim();
            if (k.Length < 10) { Say(Lang.T("צריך להדביק קודם את המפתח."), Theme.Warn); return; }
            _busy = true;
            _test.Enabled = false;
            Say(Lang.T("בודק..."), Theme.TextDim);
            Thread t = new Thread(delegate ()
            {
                string err;
                bool ok = Stt.Groq.CheckKey(k, out err);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _busy = false;
                        _test.Enabled = true;
                        // כמו בחלון של גוגל: בדיקה שעברה שומרת - מי שבדק ״עובד״ התכוון להשתמש
                        if (ok)
                        {
                            Stt.GroqKey = k;
                            Settings.GroqKeyTouched = true;
                            Settings.SaveAll();
                            if (Settings.KeyOnDisk("groqkey")) Say(Lang.T("החיבור עובד, והמפתח נשמר."), Theme.Good);
                            else Say(Lang.F("החיבור עובד, אבל המפתח לא נשמר בקובץ. {0}", Settings.LastError), Theme.Warn);
                        }
                        else Say(err ?? Lang.T("לא התקבלה תשובה."), Theme.Bad);
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Say(string s, Color c)
        {
            _status.Text = s;
            _status.Color = c;
            _status.Invalidate();
        }

        protected override bool OnOk()
        {
            Stt.GroqKey = _key.Text.Trim();
            Settings.GroqKeyTouched = true;
            Settings.SaveAll();
            if (Stt.GroqKey.Length > 0 && !Settings.KeyOnDisk("groqkey"))
                Ui.Error(this, Lang.T("המפתח לא נשמר"),
                    Lang.T("המפתח פעיל עד שתסגרו את התוכנה, אבל הוא לא נכתב לקובץ ההגדרות, ובהפעלה הבאה הוא לא יהיה.") +
                    Environment.NewLine + Settings.LastError);
            return true;
        }
    }

    /// <summary>חלון ההתקדמות של התמלול. שיעור שלם לוקח דקות, אז הביטול
    /// חייב לעבוד באמת - ומה שכבר תומלל נשמר.</summary>
    internal class TranscribeRunDlg : Dlg
    {
        private readonly Lbl _stat;
        private readonly ProgressBarLite _bar;
        private readonly string _path, _context;
        private readonly long _dur;
        private readonly ISttProvider _provider;
        private readonly long _from, _to;
        private volatile bool _cancel;
        private bool _finished;
        private readonly StopToken _stop = new StopToken();

        public Transcribe.Result Result;

        public TranscribeRunDlg(string path, long durationMs, string context)
            : this(Stt.Current, path, durationMs, context) { }

        public TranscribeRunDlg(ISttProvider provider, string path, long durationMs, string context)
            : this(provider, path, durationMs, context, 0, 0) { }

        /// <summary><paramref name="fromMs"/>/<paramref name="toMs"/>: רק הקטע הזה (0/0 - כל הסרט).</summary>
        public TranscribeRunDlg(ISttProvider provider, string path, long durationMs, string context, long fromMs, long toMs)
            : base(Lang.T("מתמלל..."), Ico.Sparkles, 540)
        {
            _from = fromMs; _to = toMs;
            _provider = provider;
            _path = path; _dur = durationMs; _context = context;
            Subtitle = Lang.T("השאירו את החלון פתוח");

            _stat = Label(Lang.T("מתחיל…"), false, Theme.Text);
            Row(_stat, 24, 10);
            _bar = new ProgressBarLite();
            Row(_bar, 10, 12);

            Lbl note = Hint(Lang.T("אפשר לעצור בכל רגע - מה שכבר תומלל יישאר."));
            Row(note, 22, 10);

            Btn cancel = new Btn();
            cancel.Text = Lang.T("עצירה");
            cancel.Kind = BtnKind.Ghost;
            cancel.Click += delegate { Stop(); };
            Row(cancel, 40, 0);
            Y += Theme.S(6);
            ClientSize = new Size(ClientSize.Width, Y);
            Controls.Add(CloseButton());

            // ‏×‏ או Esc באמצע = ״עצירה״: מה שתומלל נשמר. עד 0.8.5 החלון נסגר והתמלול המשיך
            // ברקע עד הסוף - צרך מכסה, והתוצאה נזרקה. לחיצה שנייה סוגרת מיד.
            FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (_finished || _cancel) { _cancel = true; return; }
                e.Cancel = true;
                Stop();
            };
            Shown += delegate { Start(); };
        }

        private void Stop()
        {
            _cancel = true;
            _stop.Stop();                       // גם הבקשה שבדרך - עד 0.8.8 חיכינו לה, עד שלוש דקות ויותר
            _stat.Text = Lang.T("עוצר…");
            _stat.Invalidate();
        }

        private void Start()
        {
            Thread t = new Thread(delegate ()
            {
                StopToken.Attach(_stop);
                Transcribe.Result r = null;
                try
                {
                    r = Transcribe.Run(_provider, _path, _dur, _context,
                        delegate (double frac, string status)
                        {
                            try
                            {
                                BeginInvoke((MethodInvoker)delegate
                                {
                                    _bar.Value = (float)frac;
                                    _stat.Text = status;
                                    _stat.Invalidate();
                                    _bar.Invalidate();
                                });
                            }
                            catch { }
                        },
                        delegate { return _cancel; }, _from, _to > 0 ? _to : _dur);
                }
                catch (Exception ex)
                {
                    r = new Transcribe.Result();
                    r.Error = ex.Message;
                    Ai.Log("חריגה בתמלול: " + ex);
                }
                if (r != null && !_cancel && ProofreadOn(_provider)) Proofread(r);
                Result = r;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _finished = true;
                        Ok = r != null && r.Cues.Count > 0;
                        Close();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        /// <summary>**בדיקת שגיאות שמיעה אחרי Groq** (0.8.22): כשיש מפתח של גוגל. ‏Whisper שומע שמות לפי הצליל (״Rupert Grant״); גוגל
        /// קורא את כל התמלול ומתקן מילים בודדות (`Ai.Proofread`). בגוגל עצמו - לא: הוא כבר קיבל את הרקע ושומע שמות טוב יותר.
        /// החלון שלפני התמלול אומר שהטקסט יישלח לגוגל (`TranscribeDlg.Refresh_`).</summary>
        internal static bool ProofreadOn(ISttProvider p) { return p != null && p.Id == "groq" && Ai.HasKey; }

        private void Proofread(Transcribe.Result r)
        {
            if (r.Cues.Count < 5) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate { _stat.Text = Lang.T("בודק שמות ושגיאות שמיעה..."); _stat.Invalidate(); });
            }
            catch { }
            List<string> lines = new List<string>();
            foreach (Cue c in r.Cues) lines.Add(c.Text);
            string err;
            string[] fix = null;
            try { fix = Ai.Proofread(lines, _context, out err); }
            catch (Exception ex) { Ai.Log("חריגה בתיקון השמיעה: " + ex); }
            if (fix == null) return;
            for (int i = 0; i < fix.Length && i < r.Cues.Count; i++)
                if (fix[i] != null) { r.Cues[i].Text = fix[i]; r.Proofread++; }
        }
    }
}
