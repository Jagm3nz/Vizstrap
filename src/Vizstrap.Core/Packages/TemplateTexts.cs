namespace Vizstrap.Core.Packages;

/// <summary>The files of a new package's template (see <see cref="PackageStore.CreateTemplate"/>).</summary>
internal static class TemplateTexts
{
    public const string Guide = """
        # Making a Vizstrap mod package

        (PL: Paczka modów to folder z plikiem vizmod.json. Spakuj go przyciskiem „Spakuj folder” w zakładce
        Mody albo zwykłym zipem i zmień rozszerzenie na .vzmod. Poniżej pełny opis po angielsku.)

        A package is a folder with **vizmod.json** at the top. Share it as a `.vzmod` file: the "Pack a
        folder" button on the Mods page makes one (it's a zip). Players install it with "Install from file".

        Everything a package does happens in Vizstrap or on its picture effects overlay, never inside
        Roblox: nothing may be injected into the Roblox player.

        ## vizmod.json

        ```json
        {
          "id": "your-name.my-mod",
          "name": "My mod",
          "author": "Your name",
          "version": "1.0.0",
          "description": "What the mod does.",
          "plugin": { "run": "powershell", "args": ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "plugin\\plugin.ps1"] }
        }
        ```

        - `id`: 2–64 lowercase letters, digits, `.`, `-`, `_`. Installing the same id again updates the package.
        - `plugin` is optional; leave it out when the package has no program.

        ## Folders (all optional)

        - `files\` — Roblox files, laid out like Vizstrap's Modifications folder (for example
          `files\content\textures\Cursors\KeyboardMouse\ArrowCursor.png`). They're copied onto Roblox on
          every launch while the package is on; the player's own Modifications win over packages.
        - `effects\` — picture effects in HLSL, drawn after Vizstrap's own (see below).
        - `loading-themes\` — loading window themes in Bloxstrap's XML format, one folder each with a Theme.xml.
        - `plugin\` — your program (any language), see "Plugins".

        ## Picture effects (HLSL)

        `effects\name.hlsl` holds one function:

        ```hlsl
        float3 Effect(float2 uv, float3 colour)
        {
            return colour;   // the pixel's new colour
        }
        ```

        - `uv` is the position on the screen (0,0 top left … 1,1 bottom right); `colour` is the pixel after
          Vizstrap's effects, in linear light (0 … 1).
        - Available: `Picture(uv)` (the colour anywhere else), `ViewDepth(uv)` (guessed depth, 1 near … 60
          far, or 0 when the depth AI is off), `Luma(c)`, `Time` (seconds), `OutputSize` (pixels),
          `Param0` … `Param7` (the sliders).
        - `effects\name.json` names the effect and its sliders (at most 8):

        ```json
        { "name": "Scanlines", "parameters": [ { "name": "Strength", "min": 0, "max": 1, "default": 0.5 } ] }
        ```

        Players switch each effect on and set its sliders on the Shaders page. If the HLSL doesn't compile, the
        page shows the compiler's message.

        ## Plugins (any language)

        When Roblox starts through Vizstrap, the plugin's program starts too (in the package's folder) and
        gets one JSON object per line on **stdin**:

        ```
        {"event":"started","vizstrap":"1.3.0","robloxProcessId":1234}
        {"event":"gameJoined","placeId":1818,"universeId":13058,"jobId":"...","serverType":"Public","userId":1}
        {"event":"gameLeft"}
        {"event":"stopping"}
        ```

        After "stopping" (Roblox closed) the program has 3 seconds to exit. Each line it writes to
        **stdout** is a command:

        ```
        {"command":"notify","title":"My mod","text":"Hello"}   (a notification from Vizstrap's tray icon)
        {"command":"setLook","look":"Cinematic"}               (Realistic, Cinematic, Vivid or Light)
        {"command":"log","text":"something happened"}          (into Vizstrap's log)
        ```

        Write whole lines and flush after each one. Anything else, and stderr, goes to Vizstrap's log.
        `plugin\plugin.ps1`, `plugin.py` and `plugin.cpp` are the same example in three languages; point
        `run` at the one you use (a program in the package, like `plugin\\plugin.exe`, or one on the PATH).

        A plugin is a normal program with the player's rights, so Vizstrap asks before switching one on.
        """;

    public const string ScanlinesEffect = """
        // A Vizstrap picture effect: old CRT-style scanlines.
        // Effect() gets the pixel's colour (linear light) and returns the new one; Param0 is the "Strength" slider.
        float3 Effect(float2 uv, float3 colour)
        {
            float rows = sin(uv.y * OutputSize.y * 3.14159) * 0.5 + 0.5;
            return colour * (1 - Param0 * (1 - rows) * 0.6);
        }
        """;

    public const string ScanlinesDescription = """
        {
          "name": "Scanlines",
          "parameters": [ { "name": "Strength", "min": 0, "max": 1, "default": 0.5 } ]
        }
        """;

    public const string PowerShellPlugin = """
        # A Vizstrap plugin: a notification when you join a game.
        # Vizstrap sends one JSON object per line on stdin; each line written to stdout is a command.
        while ($null -ne ($line = [Console]::In.ReadLine())) {
            $message = $line | ConvertFrom-Json

            if ($message.event -eq 'gameJoined') {
                $command = @{ command = 'notify'; title = 'My mod'; text = "Joined place $($message.placeId)" } | ConvertTo-Json -Compress
                [Console]::Out.WriteLine($command)
                [Console]::Out.Flush()
            }
            elseif ($message.event -eq 'stopping') {
                exit
            }
        }
        """;

    public const string PythonPlugin = """
        # A Vizstrap plugin: a notification when you join a game.
        # In vizmod.json: "plugin": { "run": "python", "args": ["plugin\\plugin.py"] }
        import json
        import sys

        for line in sys.stdin:
            message = json.loads(line)

            if message["event"] == "gameJoined":
                command = {"command": "notify", "title": "My mod", "text": f"Joined place {message['placeId']}"}
                print(json.dumps(command), flush=True)
            elif message["event"] == "stopping":
                break
        """;

    public const string CppPlugin = """
        // A Vizstrap plugin: a notification when you join a game.
        // Build: cl /EHsc /std:c++17 plugin.cpp   then in vizmod.json: "plugin": { "run": "plugin\\plugin.exe" }
        #include <iostream>
        #include <string>

        int main()
        {
            std::string line;

            while (std::getline(std::cin, line))
            {
                if (line.find("\"event\":\"gameJoined\"") != std::string::npos)
                    std::cout << R"({"command":"notify","title":"My mod","text":"Joined a game"})" << std::endl;
                else if (line.find("\"event\":\"stopping\"") != std::string::npos)
                    break;
            }
        }
        """;
}
