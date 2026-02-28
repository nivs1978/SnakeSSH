using System;
using System.Collections.Generic;

namespace SnakeSSH
{
    sealed class ListBox
    {
        const int ScrollbarPrecision = 2;

        public ListBox(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Left { get; private set; }
        public int Top { get; private set; }
        public int Width { get; }
        public int Height { get; }
        public int SelectedIndex { get; private set; }
        int offset;

        int TextWidth => Math.Max(1, Width - 1);

        public void SetPosition(int left, int top)
        {
            Left = left;
            Top = top;
        }

        public void EnsureSelection(int itemCount)
        {
            if (itemCount <= 0)
            {
                SelectedIndex = 0;
                offset = 0;
                return;
            }

            if (SelectedIndex >= itemCount)
            {
                SelectedIndex = itemCount - 1;
            }

            if (SelectedIndex < offset)
            {
                offset = SelectedIndex;
            }

            if (SelectedIndex - offset > Height - 1)
            {
                offset = SelectedIndex - (Height - 1);
            }
        }

        public bool HandleKey(ConsoleKeyInfo key, int itemCount)
        {
            if (itemCount <= 0)
            {
                SelectedIndex = 0;
                return false;
            }

            var previousIndex = SelectedIndex;
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    SelectedIndex = Math.Max(0, SelectedIndex - 1);
                    break;
                case ConsoleKey.DownArrow:
                    SelectedIndex = Math.Min(itemCount - 1, SelectedIndex + 1);
                    break;
            }

            if (SelectedIndex < offset)
            {
                offset = SelectedIndex;
            }

            if (SelectedIndex - offset > Height - 1)
            {
                offset = SelectedIndex - (Height - 1);
            }

            return previousIndex != SelectedIndex;
        }

        public void Draw(IReadOnlyList<Connection> connections)
        {
            for (var row = 0; row < Height; row++)
            {
                var index = row + offset;
                var text = index < connections.Count ? connections[index].DisplayName : string.Empty;
                text ??= string.Empty;
                text = text.Length > TextWidth ? text[..TextWidth] : text.PadRight(TextWidth);

                var bg = index == SelectedIndex ? Theme.ListSelectedBackground : Theme.ListBackground;
                var fg = index == SelectedIndex ? Theme.ListSelectedForeground : Theme.ListForeground;

                Console.BackgroundColor = bg;
                Console.ForegroundColor = fg;
                Console.SetCursorPosition(Left, Top + row);
                Console.Write(text);
                DrawScrollbar(row, connections.Count);
            }
        }

        void DrawScrollbar(int row, int itemCount)
        {
            if (Width <= 1)
            {
                return;
            }

            if (itemCount <= Height)
            {
                var prevBg = Console.BackgroundColor;
                var prevFg = Console.ForegroundColor;
                Console.BackgroundColor = Theme.ScreenBorderBackground;
                Console.ForegroundColor = Theme.ScreenBorderForeground;
                Console.Write(' ');
                Console.BackgroundColor = prevBg;
                Console.ForegroundColor = prevFg;
                return;
            }

            var virtualHeight = Height * ScrollbarPrecision;
            var scrollbarHeight = Math.Max(1, (int)Math.Round((double)virtualHeight * Height / itemCount));
            var scrollbarTop = (int)Math.Round((double)offset * (virtualHeight - scrollbarHeight) / (itemCount - Height));

            var upperPixel = row * ScrollbarPrecision;
            var lowerPixel = row * ScrollbarPrecision + 1;

            var upperFilled = upperPixel >= scrollbarTop && upperPixel < scrollbarTop + scrollbarHeight;
            var lowerFilled = lowerPixel >= scrollbarTop && lowerPixel < scrollbarTop + scrollbarHeight;

            var prevBgColor = Console.BackgroundColor;
            var prevFgColor = Console.ForegroundColor;
            Console.BackgroundColor = Theme.ScreenBorderBackground;
            Console.ForegroundColor = Theme.ScreenBorderForeground;

            if (upperFilled && lowerFilled)
            {
                Console.Write('█');
            }
            else if (upperFilled)
            {
                Console.Write('▀');
            }
            else if (lowerFilled)
            {
                Console.Write('▄');
            }
            else
            {
                Console.Write(' ');
            }

            Console.BackgroundColor = prevBgColor;
            Console.ForegroundColor = prevFgColor;
        }
    }
}
