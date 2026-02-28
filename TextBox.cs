using System;
using System.Collections.Generic;
using System.Text;

namespace SnakeSSH
{
    sealed class TextBox
    {
        public TextBox(int left, int top, int width, string text)
        {
            Left = left;
            Top = top;
            Width = width;
            Text = text;
            CursorIndex = text.Length;
        }

        public int Left { get; private set; }
        public int Top { get; private set; }
        public int Width { get; }
        public string Text { get; private set; }
        public int CursorIndex { get; private set; }
        public int ScrollOffset { get; private set; }
        public bool InsertMode { get; private set; } = true;
        public bool HasFocus { get; private set; }
        public string? SearchText { get; set; }

        public void SetPosition(int left, int top)
        {
            Left = left;
            Top = top;
        }

        public void SetFocus(bool hasFocus)
        {
            HasFocus = hasFocus;
            Console.CursorVisible = hasFocus;
        }

        public void Draw()
        {
            Console.BackgroundColor = Theme.TextBoxBackground;
            var visibleText = GetVisibleText();
            var isPlaceholder = string.IsNullOrEmpty(Text) && !string.IsNullOrEmpty(SearchText);
            Console.ForegroundColor = isPlaceholder ? Theme.TextBoxSearchTextColor: Theme.TextBoxForeground;
            Console.SetCursorPosition(Left, Top);
            Console.Write(visibleText);
            PlaceCursor();
        }

        public void PlaceCursor()
        {
            if (!HasFocus)
            {
                return;
            }

            var cursorLeft = Left + Math.Clamp(CursorIndex - ScrollOffset, 0, Width - 1);
            Console.SetCursorPosition(cursorLeft, Top);
        }

        public bool HandleKey(ConsoleKeyInfo key)
        {
            var changed = false;
            switch (key.Key)
            {
                case ConsoleKey.LeftArrow:
                    MoveCursorLeft();
                    changed = true;
                    break;
                case ConsoleKey.RightArrow:
                    MoveCursorRight();
                    changed = true;
                    break;
                case ConsoleKey.Backspace:
                    Backspace();
                    changed = true;
                    break;
                case ConsoleKey.Delete:
                    Delete();
                    changed = true;
                    break;
                case ConsoleKey.Insert:
                    InsertMode = !InsertMode;
                    changed = true;
                    break;
                default:
                    if (!char.IsControl(key.KeyChar) && (int)key.KeyChar!=0)
                    {
                        InsertChar(key.KeyChar);
                        changed = true;
                    }
                    break;
            }

            EnsureCursorVisible();
            return changed;
        }

        string GetVisibleText()
        {
            EnsureCursorVisible();
        var sourceText = string.IsNullOrEmpty(Text) ? SearchText ?? string.Empty : Text;
        var slice = sourceText.Length >= ScrollOffset
            ? sourceText[ScrollOffset..Math.Min(sourceText.Length, ScrollOffset + Width)]
            : string.Empty;
            return slice.PadRight(Width);
        }

        void InsertChar(char c)
        {
            if (InsertMode || CursorIndex >= Text.Length)
            {
                Text = Text.Insert(CursorIndex, c.ToString());
            }
            else
            {
                var chars = Text.ToCharArray();
                chars[CursorIndex] = c;
                Text = new string(chars);
            }

            CursorIndex++;
        }

        void Backspace()
        {
            if (CursorIndex <= 0)
            {
                return;
            }

            Text = Text.Remove(CursorIndex - 1, 1);
            CursorIndex--;
        }

        void Delete()
        {
            if (CursorIndex >= Text.Length)
            {
                return;
            }

            Text = Text.Remove(CursorIndex, 1);
        }

        void MoveCursorLeft()
        {
            if (CursorIndex > 0)
            {
                CursorIndex--;
            }
        }

        void MoveCursorRight()
        {
            if (CursorIndex < Text.Length)
            {
                CursorIndex++;
            }
        }

        void EnsureCursorVisible()
        {
            if (CursorIndex < ScrollOffset)
            {
                ScrollOffset = CursorIndex;
                return;
            }

            var cursorRight = ScrollOffset + Width - 1;
            if (CursorIndex > cursorRight)
            {
                ScrollOffset = CursorIndex - Width + 1;
            }
        }
    }
}
