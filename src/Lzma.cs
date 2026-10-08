using System;
using System.IO;

namespace SubtitleStudio
{
    /// <summary>מפענח LZMA - העברה של המפענח מ-LZMA SDK של איגור פבלוב (נחלת הכלל). בשביל מנוע הווידאו: LZMA דוחס את
    /// ffmpeg ל-27 מגה, מול 37 של Deflate שמובנה בדוט-נט (נמדד על 9.0.2: ‏100.5 ← 26.8), ובלי שום ספרייה חיצונית.
    ///
    /// הקלט: ״.lzma״ (5 בתים של מאפיינים, 8 של אורך - ‎-1‎ = לא ידוע ויש סמן סוף - ואז הזרם), כמו ש-xz ופייתון כותבים
    /// ב-FORMAT_ALONE. **כל שינוי כאן - test-release מפענח את המנוע האמיתי ומשווה טביעה.**</summary>
    internal sealed class LzmaDecoder
    {
        private const int NumStates = 12;
        private const int NumPosBitsMax = 4;
        private const int NumLenToPosStates = 4;
        private const int NumAlignBits = 4;
        private const int StartPosModelIndex = 4;
        private const int EndPosModelIndex = 14;
        private const int NumFullDistances = 1 << (EndPosModelIndex / 2);
        private const int MatchMinLen = 2;
        private const int NumPosSlotBits = 6;
        private const int NumLowLenBits = 3, NumMidLenBits = 3, NumHighLenBits = 8;
        private const int NumLowLenSymbols = 1 << NumLowLenBits, NumMidLenSymbols = 1 << NumMidLenBits;

        private const int BitModelTotalBits = 11;
        private const uint BitModelTotal = 1u << BitModelTotalBits;
        private const int NumMoveBits = 5;
        private const uint TopValue = 1u << 24;

        // ---- קלט עם מאגר משלו: קריאה של בית בכל פעם מהזרם הייתה האטה של פי כמה ----
        private Stream _in;
        private readonly byte[] _inBuf = new byte[1 << 16];
        private int _inPos, _inLen;
        private uint _range, _code;

        private byte NextByte()
        {
            if (_inPos == _inLen)
            {
                _inLen = _in.Read(_inBuf, 0, _inBuf.Length);
                _inPos = 0;
                if (_inLen <= 0) throw new InvalidDataException("LZMA: הזרם נגמר באמצע.");
            }
            return _inBuf[_inPos++];
        }

        private uint Bit(ushort[] probs, int i)
        {
            uint p = probs[i];
            uint bound = (_range >> BitModelTotalBits) * p;
            uint bit;
            if (_code < bound)
            {
                _range = bound;
                probs[i] = (ushort)(p + ((BitModelTotal - p) >> NumMoveBits));
                bit = 0;
            }
            else
            {
                _range -= bound;
                _code -= bound;
                probs[i] = (ushort)(p - (p >> NumMoveBits));
                bit = 1;
            }
            if (_range < TopValue) { _code = (_code << 8) | NextByte(); _range <<= 8; }
            return bit;
        }

        private uint DirectBits(int count)
        {
            uint result = 0;
            for (int i = count; i > 0; i--)
            {
                _range >>= 1;
                uint t = (_code - _range) >> 31;
                _code -= _range & (t - 1);
                result = (result << 1) | (1 - t);
                if (_range < TopValue) { _code = (_code << 8) | NextByte(); _range <<= 8; }
            }
            return result;
        }

        private uint Tree(ushort[] probs, int start, int bits)
        {
            uint m = 1;
            for (int i = bits; i > 0; i--) m = (m << 1) + Bit(probs, start + (int)m);
            return m - (1u << bits);
        }

        private uint ReverseTree(ushort[] probs, int start, int bits)
        {
            uint m = 1, symbol = 0;
            for (int i = 0; i < bits; i++)
            {
                uint bit = Bit(probs, start + (int)m);
                m = (m << 1) + bit;
                symbol |= bit << i;
            }
            return symbol;
        }

        // ---- אורך: choice, choice2, low[16][8], mid[16][8], high[256] ----
        private sealed class LenProbs
        {
            public readonly ushort[] P = new ushort[2 + (1 << NumPosBitsMax) * NumLowLenSymbols * 2 + (1 << NumHighLenBits)];
            public const int Choice = 0, Choice2 = 1, Low = 2;
            public const int Mid = Low + (1 << NumPosBitsMax) * NumLowLenSymbols;
            public const int High = Mid + (1 << NumPosBitsMax) * NumMidLenSymbols;
        }

        private uint DecodeLen(LenProbs l, uint posState)
        {
            if (Bit(l.P, LenProbs.Choice) == 0) return Tree(l.P, LenProbs.Low + (int)posState * NumLowLenSymbols, NumLowLenBits);
            if (Bit(l.P, LenProbs.Choice2) == 0) return NumLowLenSymbols + Tree(l.P, LenProbs.Mid + (int)posState * NumMidLenSymbols, NumMidLenBits);
            return NumLowLenSymbols + NumMidLenSymbols + Tree(l.P, LenProbs.High, NumHighLenBits);
        }

        // ---- חלון הפלט ----
        private byte[] _win;
        private uint _winSize, _pos, _streamPos;
        private Stream _out;
        private long _written;
        private Action<long> _progress;

        private void Flush()
        {
            uint size = _pos - _streamPos;
            if (size == 0) return;
            _out.Write(_win, (int)_streamPos, (int)size);
            _written += size;
            if (_pos >= _winSize) _pos = 0;
            _streamPos = _pos;
            if (_progress != null) _progress(_written);
        }

        private void Put(byte b)
        {
            _win[_pos++] = b;
            if (_pos >= _winSize) Flush();
        }

        private byte Get(uint distance)
        {
            uint p = _pos - distance - 1;
            if (p >= _winSize) p += _winSize;
            return _win[p];
        }

        /// <summary>מפענח את כל הזרם (כולל הכותרת של 13 בתים) אל <paramref name="output"/>. <paramref name="progress"/>:
        /// כמה בתים נכתבו, מדי פעם. מחזיר כמה נכתבו; זרק InvalidDataException על כל נתון לא תקין.
        /// <paramref name="expectedSize"/>: האורך הנכון, כשהוא ידוע מבחוץ. פייתון (וגם xz) כותב בכותרת ״לא ידוע״ וסמן סוף
        /// בזרם - ובזרם פגום הסמן אולי לא יגיע, והפענוח היה כותב זבל לדיסק עד שהקלט נגמר. כך הוא נעצר באורך הנכון.</summary>
        public long Decode(Stream input, Stream output, long expectedSize, Action<long> progress)
        {
            _in = input; _out = output; _progress = progress;
            _inPos = _inLen = 0; _written = 0;

            byte[] props = new byte[5];
            for (int i = 0; i < 5; i++) props[i] = NextByte();
            long outSize = 0;
            for (int i = 0; i < 8; i++) outSize |= (long)NextByte() << (8 * i);
            if (expectedSize > 0)
            {
                if (outSize >= 0 && outSize != expectedSize) throw new InvalidDataException("LZMA: האורך בכותרת לא תואם.");
                outSize = expectedSize;
            }
            int d = props[0];
            if (d >= 9 * 5 * 5) throw new InvalidDataException("LZMA: מאפיינים לא תקינים.");
            int lc = d % 9; d /= 9;
            int lp = d % 5, pb = d / 5;
            uint dictSize = 0;
            for (int i = 0; i < 4; i++) dictSize |= (uint)props[1 + i] << (8 * i);
            uint dictCheck = Math.Max(dictSize, 1);
            // החלון לא גדול מהפלט עצמו: מילון של 64 מגה על קובץ קטן לא צריך 64 מגה זיכרון
            long winLong = Math.Max(dictCheck, 1u << 12);
            if (outSize >= 0 && outSize < winLong) winLong = Math.Max(outSize, 1 << 12);
            _winSize = (uint)winLong;
            _win = new byte[_winSize];
            _pos = _streamPos = 0;

            int posStateMask = (1 << pb) - 1;
            ushort[] isMatch = NewProbs(NumStates << NumPosBitsMax);
            ushort[] isRep = NewProbs(NumStates), isRepG0 = NewProbs(NumStates), isRepG1 = NewProbs(NumStates), isRepG2 = NewProbs(NumStates);
            ushort[] isRep0Long = NewProbs(NumStates << NumPosBitsMax);
            ushort[] posSlot = NewProbs(NumLenToPosStates << NumPosSlotBits);
            ushort[] posDec = NewProbs(NumFullDistances - EndPosModelIndex + 1);
            ushort[] align = NewProbs(1 << NumAlignBits);
            LenProbs lenDec = new LenProbs(), repLenDec = new LenProbs();
            Fill(lenDec.P); Fill(repLenDec.P);
            ushort[] lit = NewProbs(0x300 << (lc + lp));
            uint litPosMask = (1u << lp) - 1;

            // הטווח: 5 בתים, הראשון אפס
            _range = 0xFFFFFFFF; _code = 0;
            for (int i = 0; i < 5; i++) _code = (_code << 8) | NextByte();

            int state = 0;
            uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
            long now = 0;
            byte prev = 0;
            while (outSize < 0 || now < outSize)
            {
                uint posState = (uint)now & (uint)posStateMask;
                if (Bit(isMatch, (state << NumPosBitsMax) + (int)posState) == 0)
                {
                    int baseIdx = 0x300 * (int)((((uint)now & litPosMask) << lc) + ((uint)prev >> (8 - lc)));
                    uint symbol = 1;
                    if (state >= 7)
                    {
                        uint matchByte = Get(rep0);
                        do
                        {
                            uint matchBit = (matchByte >> 7) & 1;
                            matchByte <<= 1;
                            uint bit = Bit(lit, baseIdx + (int)(((1 + matchBit) << 8) + symbol));
                            symbol = (symbol << 1) | bit;
                            if (matchBit != bit)
                            {
                                while (symbol < 0x100) symbol = (symbol << 1) | Bit(lit, baseIdx + (int)symbol);
                                break;
                            }
                        }
                        while (symbol < 0x100);
                    }
                    else
                    {
                        while (symbol < 0x100) symbol = (symbol << 1) | Bit(lit, baseIdx + (int)symbol);
                    }
                    prev = (byte)symbol;
                    Put(prev);
                    state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                    now++;
                    continue;
                }

                uint len;
                if (Bit(isRep, state) == 1)
                {
                    if (now == 0) throw new InvalidDataException("LZMA: חזרה לפני כל נתון.");
                    if (Bit(isRepG0, state) == 0)
                    {
                        if (Bit(isRep0Long, (state << NumPosBitsMax) + (int)posState) == 0)
                        {
                            state = state < 7 ? 9 : 11;
                            prev = Get(rep0);
                            Put(prev);
                            now++;
                            continue;
                        }
                    }
                    else
                    {
                        uint dist;
                        if (Bit(isRepG1, state) == 0) dist = rep1;
                        else
                        {
                            if (Bit(isRepG2, state) == 0) dist = rep2;
                            else { dist = rep3; rep3 = rep2; }
                            rep2 = rep1;
                        }
                        rep1 = rep0; rep0 = dist;
                    }
                    len = DecodeLen(repLenDec, posState) + MatchMinLen;
                    state = state < 7 ? 8 : 11;
                }
                else
                {
                    rep3 = rep2; rep2 = rep1; rep1 = rep0;
                    len = MatchMinLen + DecodeLen(lenDec, posState);
                    state = state < 7 ? 7 : 10;
                    uint lenState = len - MatchMinLen;
                    if (lenState >= NumLenToPosStates) lenState = NumLenToPosStates - 1;
                    uint slot = Tree(posSlot, (int)(lenState << NumPosSlotBits), NumPosSlotBits);
                    if (slot >= StartPosModelIndex)
                    {
                        int direct = (int)((slot >> 1) - 1);
                        rep0 = (2 | (slot & 1)) << direct;
                        if (slot < EndPosModelIndex)
                            rep0 += ReverseTree(posDec, (int)(rep0 - slot - 1), direct);
                        else
                        {
                            rep0 += DirectBits(direct - NumAlignBits) << NumAlignBits;
                            rep0 += ReverseTree(align, 0, NumAlignBits);
                        }
                    }
                    else rep0 = slot;
                    if (rep0 == 0xFFFFFFFF) break;                    // סמן הסוף
                }
                if (rep0 >= now || rep0 >= dictCheck) throw new InvalidDataException("LZMA: מרחק לא תקין.");
                if (outSize >= 0 && now + len > outSize) throw new InvalidDataException("LZMA: ארוך מהאורך שבכותרת.");

                uint p = _pos - rep0 - 1;
                if (p >= _winSize) p += _winSize;
                for (uint k = len; k > 0; k--)
                {
                    if (p >= _winSize) p = 0;
                    _win[_pos++] = _win[p++];
                    if (_pos >= _winSize) Flush();
                }
                now += len;
                prev = Get(0);
            }
            Flush();
            _win = null;
            if (outSize >= 0 && now != outSize) throw new InvalidDataException("LZMA: קצר מהאורך שבכותרת.");
            return now;
        }

        private static ushort[] NewProbs(int n)
        {
            ushort[] a = new ushort[n];
            Fill(a);
            return a;
        }

        private static void Fill(ushort[] a)
        {
            for (int i = 0; i < a.Length; i++) a[i] = (ushort)(BitModelTotal >> 1);
        }
    }
}
