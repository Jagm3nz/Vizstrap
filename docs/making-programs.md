# 🔌 Making programs for packages

A [package](making-a-package.md) can bring a **program** written in any language. Vizstrap starts it together with
Roblox and tells it what happens - Roblox started, a game joined or left, the player changed the package's settings.
The program can answer with notifications, a change of the shaders' look, or lines in Vizstrap's log.

Ideas: "now playing" notifications, a timer that reminds you to take a break, a stats logger, a look that changes with
the game you join.

---

## 🚀 Quick start

1. **Mods → Mod packages → Make your own → New package** - the template has the same small program in PowerShell,
   Python and C++ in `plugin\`.
2. In `vizmod.json`, point `plugin` at the one you use:

   | Language | `plugin` |
   |---|---|
   | PowerShell (built into Windows) | `{ "run": "powershell", "args": ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "plugin\\plugin.ps1"] }` |
   | Python | `{ "run": "python", "args": ["plugin\\plugin.py"] }` |
   | Any `.exe` (C++, C#, Rust, Go…) | `{ "run": "plugin\\plugin.exe" }` |

   `run` is a file in your package, or a program on the PATH like `python`. It can't point outside the package.
3. Install the package, switch it on, **Save**. Vizstrap asks the player before it runs a package's program the first time.
4. Start Roblox through Vizstrap - your program starts too, in the package's folder.

## 📨 How it works

Vizstrap writes **one JSON object per line** to your program's **stdin**, and reads **one JSON object per line** from its
**stdout**. That's the whole protocol - any language that reads and writes lines can do it.

### Events (Vizstrap → your program)

| Event | When | Fields |
|---|---|---|
| `started` | Roblox started | `vizstrap` - Vizstrap's version, `robloxProcessId`, `settings` - the values of your package's tabs |
| `settings` | The player saved new values while playing | `values` |
| `gameJoined` | The player joined a game | `placeId`, `universeId`, `jobId`, `serverType` (`Public`, `Private`, `Reserved`), `userId` |
| `gameLeft` | The player left the game | |
| `stopping` | Roblox closed | Exit within **3 seconds**, or your program is ended |

```json
{"event":"started","vizstrap":"1.0.0","robloxProcessId":1234,"settings":{"greet":true,"greeting":"Have fun!"}}
{"event":"gameJoined","placeId":920587237,"universeId":383310974,"jobId":"d9758c4f-…","serverType":"Public","userId":1}
```

`settings` / `values` hold every input of your package's [tabs](making-a-package.md#-tabs), by `Id`: toggles as
`true` / `false`, sliders as numbers, choices and text fields as text.

### Commands (your program → Vizstrap)

| Command | What it does |
|---|---|
| `{"command":"notify","title":"My mod","text":"Hello!"}` | A notification from Vizstrap's tray icon (title up to 64, text up to 250 characters) |
| `{"command":"setLook","look":"Cinematic"}` | The shaders' look: `Realistic`, `Cinematic`, `Vivid` or `Light` |
| `{"command":"log","text":"Something happened"}` | A line in Vizstrap's log |

Anything else your program prints, and everything it writes to **stderr**, goes to Vizstrap's log - handy for
`print`-debugging. **Having a problem?** in Vizstrap's menu opens the log.

## 🧪 Examples

All three greet the player when they join a game, with the switch and text from the template's tab.

### PowerShell

```powershell
# Vizstrap talks UTF-8 both ways (Windows PowerShell's console default isn't)
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding $false
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
$settings = $null

while ($null -ne ($line = [Console]::In.ReadLine())) {
    $message = $line | ConvertFrom-Json

    switch ($message.event) {
        'started' { $settings = $message.settings }
        'settings' { $settings = $message.values }
        'gameJoined' {
            if ($settings.greet) {
                $command = @{ command = 'notify'; title = 'My mod'; text = $settings.greeting } | ConvertTo-Json -Compress
                [Console]::Out.WriteLine($command)
                [Console]::Out.Flush()
            }
        }
        'stopping' { exit }
    }
}
```

### Python

```python
import json
import sys

sys.stdin.reconfigure(encoding="utf-8")
sys.stdout.reconfigure(encoding="utf-8")
settings = {}

for line in sys.stdin:
    message = json.loads(line)
    event = message["event"]

    if event == "started":
        settings = message.get("settings", {})
    elif event == "settings":
        settings = message["values"]
    elif event == "gameJoined" and settings.get("greet"):
        command = {"command": "notify", "title": "My mod", "text": settings.get("greeting", "")}
        print(json.dumps(command), flush=True)
    elif event == "stopping":
        break
```

### C#

```csharp
// dotnet new console, then publish it as plugin.exe into the package's plugin folder
using System.Text;
using System.Text.Json.Nodes;

Console.InputEncoding = Console.OutputEncoding = new UTF8Encoding(false);
JsonNode? settings = null;

while (Console.ReadLine() is { } line)
{
    var message = JsonNode.Parse(line)!;
    string? name = message["event"]?.GetValue<string>();

    if (name == "started")
        settings = message["settings"];
    else if (name == "settings")
        settings = message["values"];
    else if (name == "gameJoined" && settings is not null && settings["greet"]?.GetValue<bool>() == true)
    {
        var command = new JsonObject { ["command"] = "notify", ["title"] = "My mod", ["text"] = settings["greeting"]?.GetValue<string>() };
        Console.WriteLine(command.ToJsonString());
        Console.Out.Flush();
    }
    else if (name == "stopping")
        break;
}
```

## 🎮 A bigger example - game names

The **Synthwave Pack**'s program ([`examples/synthwave-pack/plugin/plugin.ps1`](../examples/synthwave-pack/plugin/plugin.ps1))
looks up the game's name from Roblox's public API when you join (`https://games.roblox.com/v1/games?universeIds=…`),
shows "Now playing: …", and when you leave, how many minutes you played.

## ⚡ Tips

- **Flush** after every line, or Vizstrap gets your commands late.
- **UTF-8** both ways - Windows PowerShell and C++ programs need to switch to it (see the examples).
- Don't block on long work between reading lines; a slow web request is fine, a minute-long job isn't.
- Your program runs with the player's rights, like any app they download - Vizstrap asks before the first run.
  Be the kind of program you'd want to run yourself.
- It can't reach into Roblox, and mustn't try: it only gets Vizstrap's events.
