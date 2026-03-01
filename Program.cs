using SnakeSSH;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;

//Console.OutputEncoding = Encoding.UTF8;
Console.BackgroundColor = ConsoleColor.Black;
Console.Clear();

var connectionsPath = Path.Combine(AppContext.BaseDirectory, "connections.json");
var connections = await LoadConnections(connectionsPath);

Console.CursorVisible = false;

try
{
    await RunUi(connections, connectionsPath);
}
finally
{
    Console.ResetColor();
    Console.CursorVisible = true;
    Console.Clear();
}

return;

static async Task RunUi(List<Connection> connections, string connectionsPath)
{
    const int rectWidth = 60;
    const int rectHeight = 25;
    var searchBox = new TextBox(0, 0, rectWidth - 4, string.Empty)
    {
        SearchText = "Search..."
    };
    var listBox = new ListBox(rectWidth - 4, rectHeight - 4);
    var lastWidth = Console.WindowWidth;
    var lastHeight = Console.WindowHeight;
    var renderLock = new object();
    var isInSession = false;
    var filteredConnections = FilterConnections(connections, searchBox.Text);
    listBox.EnsureSelection(filteredConnections.Count);

    searchBox.SetFocus(true);
    Console.BackgroundColor = ConsoleColor.Black;
    Console.Clear();
    DrawMainWindow(filteredConnections, searchBox, listBox, rectWidth, rectHeight);

    using var resizeCts = new CancellationTokenSource();
    using var resizeTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    var resizeTask = Task.Run(async () =>
    {
        try
        {
            while (await resizeTimer.WaitForNextTickAsync(resizeCts.Token))
            {
                if (Volatile.Read(ref isInSession))
                {
                    continue;
                }

                if (Console.WindowWidth != lastWidth || Console.WindowHeight != lastHeight)
                {
                    lastWidth = Console.WindowWidth;
                    lastHeight = Console.WindowHeight;
                    lock (renderLock)
                    {
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.Clear();
                        var modalRedraw = UiState.ActiveModalRedraw;
                        DrawMainWindow(filteredConnections, searchBox, listBox, rectWidth, rectHeight);
                        modalRedraw?.Invoke();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    });

    try
    {
        while (true)
        {
            var keyInfo = Console.ReadKey(true);
            switch (keyInfo.Key)
            {
                case ConsoleKey.UpArrow:
                    if (listBox.HandleKey(keyInfo, filteredConnections.Count))
                    {
                        lock (renderLock)
                        {
                            listBox.Draw(filteredConnections);
                        }
                    }
                    break;
                case ConsoleKey.DownArrow:
                    if (listBox.HandleKey(keyInfo, filteredConnections.Count))
                    {
                        lock (renderLock)
                        {
                            listBox.Draw(filteredConnections);
                        }
                    }
                    break;
                case ConsoleKey.Escape:
                    return;
                case ConsoleKey.F2:
                    if (filteredConnections.Count > 0)
                    {
                        await EditConnection(filteredConnections[listBox.SelectedIndex], connections, connectionsPath, false);
                        filteredConnections = FilterConnections(connections, searchBox.Text);
                        listBox.EnsureSelection(filteredConnections.Count);
                        lock (renderLock)
                        {
                            DrawMainWindow(filteredConnections, searchBox, listBox, rectWidth, rectHeight);
                        }
                    }
                    break;
                case ConsoleKey.F1:
                    await EditConnection(null, connections, connectionsPath, true);
                    filteredConnections = FilterConnections(connections, searchBox.Text);
                    listBox.EnsureSelection(filteredConnections.Count);
                    lock (renderLock)
                    {
                        DrawMainWindow(filteredConnections, searchBox, listBox, rectWidth, rectHeight);
                    }
                    break;
                case ConsoleKey.Enter:
                if (filteredConnections.Count > 0)
                {
                    Volatile.Write(ref isInSession, true);
                    lock (renderLock)
                    {
                        Console.ResetColor();
                        Console.Clear();
                        Console.CursorVisible = true;
                    }

                    var exitCode = await RunSshConnection(filteredConnections[listBox.SelectedIndex]);

                    if (exitCode != 0)
                    {
                        Console.WriteLine();
                        Console.Write("Press any key to return to the menu...");
                        Console.ReadKey(true);
                    }

                    lock (renderLock)
                    {
                        Console.CursorVisible = false;
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.Clear();
                        DrawMainWindow(filteredConnections, searchBox, listBox, rectWidth, rectHeight);
                    }

                    Volatile.Write(ref isInSession, false);
                }
                break;
                case ConsoleKey.Delete:
                    if (filteredConnections.Count > 0)
                    {
                        var shouldErase = false;
                        lock (renderLock)
                        {
                            shouldErase = PromptYesNo("Erase (Y/N)");
                        }

                        if (shouldErase)
                        {
                            connections.Remove(filteredConnections[listBox.SelectedIndex]);
                            await SaveConnections(connectionsPath, connections);
                            filteredConnections = FilterConnections(connections, searchBox.Text);
                            listBox.EnsureSelection(filteredConnections.Count);
                        }

                        lock (renderLock)
                        {
                            DrawMainWindow(filteredConnections, searchBox, listBox, rectWidth, rectHeight);
                        }
                    }
                    break;
                case ConsoleKey.F12:
                    Volatile.Write(ref isInSession, true);
                    lock (renderLock)
                    {
                        Console.ResetColor();
                        Console.Clear();
                        Console.CursorVisible = false;
                    }

                    RunSnakeGame(rectWidth, rectHeight);

                    lock (renderLock)
                    {
                        Console.BackgroundColor = ConsoleColor.Black;
                        Console.Clear();
                        DrawMainWindow(filteredConnections, searchBox, listBox, rectWidth, rectHeight);
                    }

                    Volatile.Write(ref isInSession, false);
                    break;
                default:
                    if (searchBox.HandleKey(keyInfo))
                    {
                        filteredConnections = FilterConnections(connections, searchBox.Text);
                        listBox.EnsureSelection(filteredConnections.Count);
                        lock (renderLock)
                        {
                            searchBox.Draw();
                            listBox.Draw(filteredConnections);
                        }
                    }
                    break;
            }

            searchBox.PlaceCursor();
        }
    }
    finally
    {
        resizeCts.Cancel();
        await resizeTask;
    }
}

static void DrawMainWindow(IReadOnlyList<Connection> connections, TextBox searchBox, ListBox listBox, int rectWidth, int rectHeight)
{
    var logoHeight = DrawLogo();

    var saveBg = Console.BackgroundColor;
    var saveFg = Console.ForegroundColor;

    var left = Math.Max(0, (Console.WindowWidth - rectWidth) / 2);
    var top = Math.Max(0, (Console.WindowHeight - rectHeight) / 2);
    if (top < logoHeight)
    {
        top = logoHeight;
    }
    var borderBg = Theme.ScreenBorderBackground;
    var borderFg = Theme.ScreenBorderForeground;

    var statusText = " F1=NEW  F2=EDIT  ENTER=CONNECT  DEL=ERASE  ESC=QUIT ";
    searchBox.SetPosition(left + 2, top + 1);
    listBox.SetPosition(left + 2, top + 3);

    for (var row = 0; row < rectHeight; row++)
    {
        Console.SetCursorPosition(left, top + row);

        if (row == 0)
        {
            Console.BackgroundColor = borderBg;
            Console.ForegroundColor = borderFg;
            Console.Write("┌" + new string('─', rectWidth - 2) + "┐");
            continue;
        }

        if (row == rectHeight - 1)
        {
            Console.BackgroundColor = borderBg;
            Console.ForegroundColor = borderFg;
            Console.Write("└─" + statusText + new string('─', rectWidth - 3 - statusText.Length) + "┘");
            continue;
        }

        Console.BackgroundColor = borderBg;
        Console.ForegroundColor = borderFg;
        Console.Write('│');
        WriteSegment(rectWidth - 2, borderBg, borderBg, string.Empty);

        Console.BackgroundColor = borderBg;
        Console.ForegroundColor = borderFg;
        Console.Write('│');
    }

    searchBox.Draw();
    listBox.Draw(connections);
    searchBox.PlaceCursor();
    Console.ForegroundColor = saveFg;
    Console.BackgroundColor = saveBg;
}

static int DrawLogo()
{
    Console.BackgroundColor = ConsoleColor.Black;
    Console.ForegroundColor = ConsoleColor.White;

    const string logo1 = "┌─┐┌┐┌┌─┐┬┌─┌─┐╔═╗╔═╗╦ ╦";
    const string logo2 = "└─┐│││├─┤├┴┐├┤ ╚═╗╚═╗╠═╣";
    const string logo3 = "└─┘┘└┘┴ ┴┴ ┴└─┘╚═╝╚═╝╩ ╩";

    var indent = Math.Max(0, (Console.WindowWidth - logo1.Length) / 2);
    Console.SetCursorPosition(indent, 0);
    Console.WriteLine(logo1);
    Console.SetCursorPosition(indent, 1);
    Console.WriteLine(logo2);
    Console.SetCursorPosition(indent, 2);
    Console.WriteLine(logo3);

    return 3;
}

static void WriteSegment(int width, ConsoleColor bg, ConsoleColor fg, string text)
{
    var content = text.Length > width ? text[..width] : text.PadRight(width);
    Console.BackgroundColor = bg;
    Console.ForegroundColor = fg;
    Console.Write(content);
}

static async Task EditConnection(Connection? connection, List<Connection> connections, string connectionsPath, bool isNew)
{
    const int windowWidth = 50;
    const int windowHeight = 11;
    const int labelWidth = 12;
    var left = Math.Max(0, (Console.WindowWidth - windowWidth) / 2);
    var top = Math.Max(0, (Console.WindowHeight - windowHeight) / 2);
    var textboxWidth = windowWidth - 6 - labelWidth;

    var workingConnection = connection ?? new Connection();

    var textboxes = new[]
    {
        new TextBox(left + 2 + labelWidth, top + 2, textboxWidth, workingConnection.DisplayName ?? string.Empty),
        new TextBox(left + 2 + labelWidth, top + 4, textboxWidth, workingConnection.HostName ?? string.Empty),
        new TextBox(left + 2 + labelWidth, top + 6, textboxWidth, workingConnection.Username ?? string.Empty)
    };

    var focusIndex = 0;
    textboxes[focusIndex].SetFocus(true);

    void RedrawEdit()
    {
        var currentLeft = Math.Max(0, (Console.WindowWidth - windowWidth) / 2);
        var currentTop = Math.Max(0, (Console.WindowHeight - windowHeight) / 2);

        textboxes[0].SetPosition(currentLeft + 2 + labelWidth, currentTop + 2);
        textboxes[1].SetPosition(currentLeft + 2 + labelWidth, currentTop + 4);
        textboxes[2].SetPosition(currentLeft + 2 + labelWidth, currentTop + 6);

        DrawEditWindow(currentLeft, currentTop, windowWidth, windowHeight, labelWidth, textboxes);
    }

    UiState.ActiveModalRedraw = RedrawEdit;
    RedrawEdit();

    while (true)
    {
        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Escape)
        {
            break;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            workingConnection.DisplayName = textboxes[0].Text;
            workingConnection.HostName = textboxes[1].Text;
            workingConnection.Username = textboxes[2].Text;

            if (isNew)
            {
                connections.Add(workingConnection);
            }

            SortConnections(connections);
            await SaveConnections(connectionsPath, connections);
            break;
        }

        if (key.Key == ConsoleKey.UpArrow)
        {
            if (focusIndex > 0)
            {
                var previousIndex = focusIndex;
                textboxes[focusIndex].SetFocus(false);
                focusIndex--;
                textboxes[focusIndex].SetFocus(true);
                textboxes[previousIndex].Draw();
                textboxes[focusIndex].Draw();
            }
            continue;
        }

        if (key.Key == ConsoleKey.DownArrow)
        {
            if (focusIndex < textboxes.Length - 1)
            {
                var previousIndex = focusIndex;
                textboxes[focusIndex].SetFocus(false);
                focusIndex++;
                textboxes[focusIndex].SetFocus(true);
                textboxes[previousIndex].Draw();
                textboxes[focusIndex].Draw();
            }
            continue;
        }

        if (key.Key == ConsoleKey.Tab)
        {
            var previousIndex = focusIndex;
            textboxes[focusIndex].SetFocus(false);
            focusIndex = (focusIndex + 1) % textboxes.Length;
            textboxes[focusIndex].SetFocus(true);
            textboxes[previousIndex].Draw();
            textboxes[focusIndex].Draw();
            continue;
        }

        if (textboxes[focusIndex].HandleKey(key))
        {
            textboxes[focusIndex].Draw();
        }
    }

    UiState.ActiveModalRedraw = null;
    Console.CursorVisible = false;
}

static void DrawEditWindow(int left, int top, int width, int height, int labelWidth, IReadOnlyList<TextBox> textboxes)
{
    var saveFg = Console.ForegroundColor;
    var savebg = Console.ForegroundColor;
    var borderBg = Theme.DialogBorderBackground;
    var borderFg = Theme.DialogBorderForeground;
    var fillBg = Theme.DialogBackground;
    var textFg = Theme.DialogForeground;

    for (var row = 0; row < height; row++)
    {
        Console.SetCursorPosition(left, top + row);
        Console.BackgroundColor = borderBg;
        Console.ForegroundColor = borderFg;

        if (row == 0)
        {
            Console.Write("┌" + new string('─', width - 2) + "┐");
            continue;
        }

        if (row == height - 1)
        {
            var footerText = " ENTER=SAVE ─ ESC=ABORT ";
            Console.Write("└─");
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write(footerText);
            Console.ForegroundColor = borderFg;
            Console.Write(new string('─', width - 3 - footerText.Length) + "┘");
            continue;
        }

        Console.Write('│');
        Console.BackgroundColor = fillBg;
        Console.ForegroundColor = textFg;
        Console.Write(new string(' ', width - 2));
        Console.BackgroundColor = borderBg;
        Console.ForegroundColor = borderFg;
        Console.Write('│');
    }

    Console.BackgroundColor = fillBg;
    Console.ForegroundColor = textFg;

    WriteLabel(left + 2, top + 2, labelWidth, "DisplayName");
    WriteLabel(left + 2, top + 4, labelWidth, "Host/IP");
    WriteLabel(left + 2, top + 6, labelWidth, "Username");

    foreach (var textbox in textboxes)
    {
        textbox.Draw();
    }
    Console.ForegroundColor = saveFg;
    Console.BackgroundColor = savebg;
}

static void WriteLabel(int left, int top, int width, string text)
{
    Console.SetCursorPosition(left, top);
    Console.Write(text.PadRight(width));
}

static async Task<List<Connection>> LoadConnections(string path)
{
    if (!File.Exists(path))
    {
        return [];
    }

    var json = await File.ReadAllTextAsync(path);
    var connections = JsonSerializer.Deserialize<List<Connection>>(json) ?? [];
    SortConnections(connections);
    return connections;
}

static async Task SaveConnections(string path, List<Connection> connections)
{
    SortConnections(connections);
    var json = JsonSerializer.Serialize(connections);
    await File.WriteAllTextAsync(path, json);
}

static void SortConnections(List<Connection> connections)
{
    connections.Sort((left, right) =>
        string.Compare(left?.DisplayName ?? string.Empty, right?.DisplayName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
}

static bool PromptYesNo(string prompt)
{
    var saveLeft = Console.CursorLeft;
    var saveTop = Console.CursorTop;
    var saveBg = Console.BackgroundColor;
    var saveFg = Console.ForegroundColor;
    var saveCursorVisible = Console.CursorVisible;

    void RedrawPrompt()
    {
        var redrawBoxWidth = Math.Min(Console.WindowWidth, Math.Max(prompt.Length + 4, 20));
        var redrawBoxHeight = 5;
        var redrawLeft = Math.Max(0, (Console.WindowWidth - redrawBoxWidth) / 2);
        var redrawTop = Math.Max(0, (Console.WindowHeight - redrawBoxHeight) / 2);

        Console.BackgroundColor = ConsoleColor.Red;
        Console.ForegroundColor = ConsoleColor.White;
        Console.CursorVisible = false;

        Console.SetCursorPosition(redrawLeft, redrawTop);
        Console.Write("┌" + new string('─', redrawBoxWidth - 2) + "┐");

        for (var row = 1; row < redrawBoxHeight - 1; row++)
        {
            Console.SetCursorPosition(redrawLeft, redrawTop + row);
            Console.Write('│');
            Console.Write(new string(' ', redrawBoxWidth - 2));
            Console.Write('│');
        }

        Console.SetCursorPosition(redrawLeft, redrawTop + redrawBoxHeight - 1);
        Console.Write("└" + new string('─', redrawBoxWidth - 2) + "┘");

        var textLeft = redrawLeft + Math.Max(0, (redrawBoxWidth - prompt.Length) / 2);
        Console.SetCursorPosition(textLeft, redrawTop + 2);
        Console.Write(prompt);
    }

    UiState.ActiveModalRedraw = RedrawPrompt;
    RedrawPrompt();

    var key = Console.ReadKey(true);

    UiState.ActiveModalRedraw = null;
    Console.SetCursorPosition(saveLeft, saveTop);
    Console.BackgroundColor = saveBg;
    Console.ForegroundColor = saveFg;
    Console.CursorVisible = saveCursorVisible;

    return key.Key == ConsoleKey.Y;
}

static List<Connection> FilterConnections(IReadOnlyList<Connection> connections, string? query)
{
    if (string.IsNullOrWhiteSpace(query))
    {
        return connections.ToList();
    }

    return connections
        .Where(connection =>
        {
            var displayName = connection.DisplayName ?? string.Empty;
            var hostName = connection.HostName ?? string.Empty;
            return displayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || hostName.Contains(query, StringComparison.OrdinalIgnoreCase);
        })
        .ToList();
}

static async Task<int> RunSshConnection(Connection connection)
{
    var host = connection.HostName?.Trim();
    if (string.IsNullOrEmpty(host))
    {
        return -1;
    }

    var user = connection.Username?.Trim();
    var target = string.IsNullOrEmpty(user) ? host : $"{user}@{host}";

    var startInfo = new ProcessStartInfo("ssh", target)
    {
        UseShellExecute = false
    };

    using var process = Process.Start(startInfo);
    if (process is null)
    {
        return -1;
    }

    await process.WaitForExitAsync();
    return process.ExitCode;
}

static void RunSnakeGame(int rectWidth, int rectHeight)
{
    var random = new Random();

    while (true)
    {
        DrawSnakeWindow(rectWidth, rectHeight, out var left, out var top, out var playWidth, out var playHeightCells);

        var playHeight = playHeightCells * 2;
        var originX = left + 1;
        var originY = top + 1;
        var snake = new Queue<Position>();
        var snakeSet = new HashSet<Position>();
        var direction = new Position(1, 0);
        var pendingGrowth = 0;
        var delay = 120;

        var startX = playWidth / 2;
        var startY = playHeight / 2;
        for (var i = 4; i >= 0; i--)
        {
            var pos = new Position(startX - i, startY);
            snake.Enqueue(pos);
            snakeSet.Add(pos);
        }

        var head = snake.Last();
        var pill = PlacePill(playWidth, playHeight, snakeSet, random);

        foreach (var segment in snakeSet)
        {
            RenderSnakeCell(originX, originY, playHeightCells, segment, snakeSet, head, pill);
        }

        RenderSnakeCell(originX, originY, playHeightCells, pill, snakeSet, head, pill);

        var lastTick = Environment.TickCount;
        var restart = false;

        while (true)
        {
            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true).Key;
                direction = key switch
                {
                    ConsoleKey.UpArrow when direction != new Position(0, 1) => new Position(0, -1),
                    ConsoleKey.DownArrow when direction != new Position(0, -1) => new Position(0, 1),
                    ConsoleKey.LeftArrow when direction != new Position(1, 0) => new Position(-1, 0),
                    ConsoleKey.RightArrow when direction != new Position(-1, 0) => new Position(1, 0),
                    ConsoleKey.Escape => new Position(0, 0),
                    _ => direction
                };

                if (direction == new Position(0, 0))
                {
                    return;
                }
            }

            if (Environment.TickCount - lastTick < delay)
            {
                Thread.Sleep(1);
                continue;
            }

            lastTick = Environment.TickCount;

            var newHead = new Position(head.X + direction.X, head.Y + direction.Y);
            if (newHead.X < 0 || newHead.X >= playWidth || newHead.Y < 0 || newHead.Y >= playHeight || snakeSet.Contains(newHead))
            {
                foreach (var segment in snakeSet)
                {
                    RenderSnakeCell(originX, originY, playHeightCells, segment, snakeSet, head, pill, ConsoleColor.Red);
                }

                Thread.Sleep(500);
                restart = true;
                break;
            }

            snake.Enqueue(newHead);
            snakeSet.Add(newHead);

            var previousHead = head;
            head = newHead;

            if (newHead.Equals(pill))
            {
                pendingGrowth += 4;
                delay = Math.Max(50, delay - 5);
                pill = PlacePill(playWidth, playHeight, snakeSet, random);
            }

            if (pendingGrowth > 0)
            {
                pendingGrowth--;
            }
            else
            {
                var tail = snake.Dequeue();
                snakeSet.Remove(tail);
                RenderSnakeCell(originX, originY, playHeightCells, tail, snakeSet, head, pill);
            }

            RenderSnakeCell(originX, originY, playHeightCells, previousHead, snakeSet, head, pill);
            RenderSnakeCell(originX, originY, playHeightCells, head, snakeSet, head, pill);
            RenderSnakeCell(originX, originY, playHeightCells, pill, snakeSet, head, pill);
        }

        if (!restart)
        {
            return;
        }
    }
}

static void DrawSnakeWindow(int rectWidth, int rectHeight, out int left, out int top, out int playWidth, out int playHeightCells)
{
    var logoHeight = DrawLogo();
    left = Math.Max(0, (Console.WindowWidth - rectWidth) / 2);
    top = Math.Max(logoHeight, (Console.WindowHeight - rectHeight) / 2);
    playWidth = rectWidth - 2;
    playHeightCells = rectHeight - 2;

    var borderBg = Theme.ScreenBorderBackground;
    var borderFg = Theme.ScreenBorderForeground;

    for (var row = 0; row < rectHeight; row++)
    {
        Console.SetCursorPosition(left, top + row);
        Console.BackgroundColor = borderBg;
        Console.ForegroundColor = borderFg;

        if (row == 0)
        {
            Console.Write("┌" + new string('─', rectWidth - 2) + "┐");
            continue;
        }

        if (row == rectHeight - 1)
        {
            Console.Write("└" + new string('─', rectWidth - 2) + "┘");
            continue;
        }

        Console.Write('│');
        Console.Write(new string(' ', rectWidth - 2));
        Console.Write('│');
    }
}

static void RenderSnakeCell(int originX, int originY, int playHeightCells, Position cell, HashSet<Position> snake, Position head, Position pill, ConsoleColor? overrideColor = null)
{
    if (cell.Y < 0 || cell.Y >= playHeightCells * 2)
    {
        return;
    }

    var cellX = cell.X;
    var cellY = cell.Y / 2;
    var upperPos = new Position(cellX, cellY * 2);
    var lowerPos = new Position(cellX, cellY * 2 + 1);

    var hasUpper = snake.Contains(upperPos);
    var hasLower = snake.Contains(lowerPos);
    var headInCell = head.X == cellX && (head.Y == upperPos.Y || head.Y == lowerPos.Y);
    var pillInUpper = pill.X == cellX && pill.Y == upperPos.Y;
    var pillInLower = pill.X == cellX && pill.Y == lowerPos.Y;

    Console.SetCursorPosition(originX + cellX, originY + cellY);
    Console.BackgroundColor = Theme.ScreenBorderBackground;

    if (hasUpper || hasLower)
    {
        Console.ForegroundColor = overrideColor ?? (headInCell ? ConsoleColor.Yellow : ConsoleColor.DarkGreen);
        Console.Write(hasUpper && hasLower ? '█' : hasUpper ? '▀' : '▄');
        return;
    }

    if (pillInUpper || pillInLower)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write(pillInUpper ? '▀' : '▄');
        return;
    }

    Console.Write(' ');
}

static Position PlacePill(int width, int height, HashSet<Position> snake, Random random)
{
    Position pill;
    Position upper;
    Position lower;
    do
    {
        pill = new Position(random.Next(0, width), random.Next(0, height));
        var cellY = pill.Y / 2;
        upper = new Position(pill.X, cellY * 2);
        lower = new Position(pill.X, cellY * 2 + 1);
    } while (snake.Contains(pill) || snake.Contains(upper) || snake.Contains(lower));

    return pill;
}

readonly record struct Position(int X, int Y);

static class UiState
{
    public static Action? ActiveModalRedraw { get; set; }
}
