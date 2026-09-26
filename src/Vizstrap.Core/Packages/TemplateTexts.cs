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
        - `pages\` — tabs your package adds to Vizstrap's settings, one XML file each (see "Tabs").
        - `plugin\` — your program (any language), see "Plugins".

        ## Tabs

        `pages\name.xml` is a tab in Vizstrap's settings, shown while the package is on. Stack elements in it
        like the cards on Vizstrap's own pages:

        ```xml
        <VizstrapPage Title="My mod" Title.pl="Mój mod" Icon="Sparkle24" Description="What this tab is for.">
          <Section Title="Options" />
          <Toggle Id="greet" Title="Say hello when I join a game" Default="true" />
          <TextBox Id="greeting" Title="Greeting" Default="Have fun!" VisibleWhen="greet" />
          <Slider Id="volume" Title="Volume" Min="0" Max="100" Step="5" Default="50" />
          <Choice Id="style" Title="Style" Default="neon">
            <Option Value="neon" Title="Neon" />
            <Option Value="retro" Title="Retro" />
          </Choice>
          <Text>Any text you like.</Text>
          <Link Title="Website" Url="https://example.com" />
        </VizstrapPage>
        ```

        - What the player sets is saved per package and sent to your program (see "Plugins").
        - `VisibleWhen="id"` shows an element only while that toggle is on (`"!id"`: while it's off).
        - Any text attribute can be translated: `Title.pl`, `Description.de`… Vizstrap picks the player's language.
        - `Icon` is a Fluent icon name like `Timer24`, `Sparkle24`, `Games24`.
        - Widgets put Vizstrap's own views on your tab: `PlaytimePeriod`, `PlaytimeSummary`, `PlaytimeChart`,
          `PlaytimeGames`, `PlaytimeClear` (the Activity package is made of them).
        - `pages\my-tab.xml` in this template has every element. The full guide:
          https://github.com/Jagm3nz/Vizstrap/blob/main/docs/making-a-package.md

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
        {"event":"started","vizstrap":"1.0.0","robloxProcessId":1234,"settings":{"greet":true,"greeting":"Have fun!"}}
        {"event":"gameJoined","placeId":1818,"universeId":13058,"jobId":"...","serverType":"Public","userId":1}
        {"event":"settings","values":{"greet":false,"greeting":"Have fun!"}}
        {"event":"gameLeft"}
        {"event":"stopping"}
        ```

        `settings` holds what the player set on your tabs (toggles are true/false, sliders numbers, the rest
        text); "settings" comes again whenever the player saves a change while playing.

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
        # A Vizstrap plugin: a greeting when you join a game, set on the package's tab (pages\my-tab.xml).
        # Vizstrap sends one JSON object per line on stdin; each line written to stdout is a command.
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
        """;

    /// <summary>The template's tab, with every element a tab can have.</summary>
    public const string MyTabPage = """
        <!-- A tab in Vizstrap's settings, shown while this package is on. Every element a tab can have is here. -->
        <VizstrapPage Title="My mod" Icon="Sparkle24" Description="Settings for my mod. Change this text in pages\my-tab.xml.">

          <Section Title="Greeting" />
          <Toggle Id="greet" Default="true"
                  Title="Say hello when I join a game" Description="The program in plugin\ reads this switch." />
          <TextBox Id="greeting" Default="Have fun!" MaxLength="100" VisibleWhen="greet"
                   Title="Greeting" Description="Shown only while the switch above is on." />

          <Section Title="More examples" />
          <Slider Id="volume" Min="0" Max="100" Step="5" Default="50" Title="Volume" Description="A number from Min to Max." />
          <Choice Id="style" Default="neon" Title="Style">
            <Option Value="neon" Title="Neon" />
            <Option Value="retro" Title="Retro" />
            <Option Value="classic" Title="Classic" />
          </Choice>
          <Text>Plain text, for explaining things.</Text>
          <Text Muted="true">Muted text, for small notes.</Text>
          <Link Title="How to make a package" Description="Everything a package can do."
                Url="https://github.com/Jagm3nz/Vizstrap/blob/main/docs/making-a-package.md" />
        </VizstrapPage>
        """;

    public const string PythonPlugin = """
        # A Vizstrap plugin: a greeting when you join a game, set on the package's tab (pages\my-tab.xml).
        # In vizmod.json: "plugin": { "run": "python", "args": ["plugin\\plugin.py"] }
        import json
        import sys

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
