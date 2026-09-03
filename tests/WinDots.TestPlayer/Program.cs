using System.Globalization;
using System.Runtime.InteropServices;
using WinDots.TestPlayer;

// A fake media player that publishes a real System Media Transport Controls session so WinDots can be tested
// without a third-party application. Drive it over stdin:
//   play | pause | next | prev | seek <seconds> | title <text> | quit
// It echoes every command it receives from the system as "[event] ..." lines on stdout so tests can assert on them.

SetCurrentProcessExplicitAppUserModelID(FakePlayer.AppUserModelId);

using var player = new FakePlayer();
player.Start();
Console.WriteLine("[ready]");

string? line;
while ((line = Console.ReadLine()) is not null)
{
    var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0)
    {
        continue;
    }

    switch (parts[0].ToLowerInvariant())
    {
        case "play":
            player.Play();
            break;
        case "pause":
            player.Pause();
            break;
        case "next":
            player.Next();
            break;
        case "prev":
            player.Previous();
            break;
        case "seek" when parts.Length == 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds):
            player.Seek(TimeSpan.FromSeconds(seconds));
            break;
        case "title" when parts.Length == 2:
            player.SetTitle(parts[1]);
            break;
        case "quit":
        case "exit":
            Console.WriteLine("[bye]");
            return 0;
        default:
            Console.WriteLine($"[error] unknown command: {line}");
            break;
    }
}

return 0;

[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
