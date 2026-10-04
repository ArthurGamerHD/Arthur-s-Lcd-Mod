using System.Collections.Generic;
using VRage.Input;

namespace LcdMod.Client.Gui
{
    /// <summary>
    /// Turns key presses into typed characters on a US layout, with held-key repeat. The game only produces text for
    /// its own IME-focused controls, so mod screens have to build it from the keys themselves. Caps lock works like a
    /// phone keyboard: double-tapping Shift turns it on and the next Shift press turns it off. The Caps Lock key toggles it
    /// too, though the system's own Caps Lock state is not visible to mods, so each entry starts with it off.
    /// </summary>
    internal sealed class KeyTextTranslator
    {
        const int RepeatDelayFrames = 30;
        const int RepeatIntervalFrames = 3;
        const int DoubleTapFrames = 20;

        static readonly Dictionary<MyKeys, string> Symbols = new Dictionary<MyKeys, string>
        {
            { MyKeys.Space, "  " },
            { MyKeys.D0, "0)" }, { MyKeys.D1, "1!" }, { MyKeys.D2, "2@" }, { MyKeys.D3, "3#" }, { MyKeys.D4, "4$" },
            { MyKeys.D5, "5%" }, { MyKeys.D6, "6^" }, { MyKeys.D7, "7&" }, { MyKeys.D8, "8*" }, { MyKeys.D9, "9(" },
            { MyKeys.NumPad0, "00" }, { MyKeys.NumPad1, "11" }, { MyKeys.NumPad2, "22" }, { MyKeys.NumPad3, "33" },
            { MyKeys.NumPad4, "44" }, { MyKeys.NumPad5, "55" }, { MyKeys.NumPad6, "66" }, { MyKeys.NumPad7, "77" },
            { MyKeys.NumPad8, "88" }, { MyKeys.NumPad9, "99" },
            { MyKeys.Multiply, "**" }, { MyKeys.Add, "++" }, { MyKeys.Subtract, "--" }, { MyKeys.Decimal, ".." },
            { MyKeys.Divide, "//" },
            { MyKeys.OemMinus, "-_" }, { MyKeys.OemPlus, "=+" }, { MyKeys.OemComma, ",<" }, { MyKeys.OemPeriod, ".>" },
            { MyKeys.OemQuestion, "/?" }, { MyKeys.OemSemicolon, ";:" }, { MyKeys.OemQuotes, "'\"" },
            { MyKeys.OemOpenBrackets, "[{" }, { MyKeys.OemCloseBrackets, "]}" }, { MyKeys.OemPipe, "\\|" },
            { MyKeys.OemBackslash, "\\|" }, { MyKeys.OemTilde, "`~" }
        };

        readonly Dictionary<MyKeys, int> _held = new Dictionary<MyKeys, int>();
        readonly List<MyKeys> _released = new List<MyKeys>();
        bool _shiftWasDown;
        bool _capsKeyWasDown;
        int _framesSinceShiftTap = int.MaxValue;

        public bool CapsLock { get; private set; }

        /// <summary>Appends the characters typed this frame, given every key currently held down.</summary>
        public void Collect(IList<MyKeys> pressed, bool shift, bool ctrl, List<char> output)
        {
            UpdateCapsLock(shift, pressed.Contains(MyKeys.CapsLock));
            _released.Clear();
            foreach (MyKeys key in _held.Keys)
                if (!pressed.Contains(key))
                    _released.Add(key);
            for (int i = 0; i < _released.Count; i++)
                _held.Remove(_released[i]);

            for (int i = 0; i < pressed.Count; i++)
            {
                MyKeys key = pressed[i];
                char character;
                if (!TryGetCharacter(key, shift, CapsLock, out character))
                    continue;
                int frames;
                _held.TryGetValue(key, out frames);
                _held[key] = ++frames;
                if (ctrl)
                    continue;
                if (frames == 1 || frames >= RepeatDelayFrames && (frames - RepeatDelayFrames) % RepeatIntervalFrames == 0)
                    output.Add(character);
            }
        }

        void UpdateCapsLock(bool shift, bool capsKey)
        {
            if (capsKey && !_capsKeyWasDown)
            {
                CapsLock = !CapsLock;
                _framesSinceShiftTap = int.MaxValue;
            }
            _capsKeyWasDown = capsKey;
            if (_framesSinceShiftTap < int.MaxValue)
                _framesSinceShiftTap++;
            if (shift && !_shiftWasDown)
            {
                if (CapsLock)
                {
                    CapsLock = false;
                    _framesSinceShiftTap = int.MaxValue;
                }
                else if (_framesSinceShiftTap <= DoubleTapFrames)
                {
                    CapsLock = true;
                    _framesSinceShiftTap = int.MaxValue;
                }
                else
                    _framesSinceShiftTap = 0;
            }
            _shiftWasDown = shift;
        }

        static bool TryGetCharacter(MyKeys key, bool shift, bool capsLock, out char character)
        {
            if (key >= MyKeys.A && key <= MyKeys.Z)
            {
                character = (char)((shift != capsLock ? 'A' : 'a') + (key - MyKeys.A));
                return true;
            }
            string pair;
            if (Symbols.TryGetValue(key, out pair))
            {
                character = pair[shift ? 1 : 0];
                return true;
            }
            character = '\0';
            return false;
        }
    }
}
