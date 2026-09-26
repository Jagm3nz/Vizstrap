# 📦 Making a Vizstrap package

A **package** (`.vzmod`) adds things to Vizstrap: its own **tabs** in the settings with switches and sliders, **shaders**,
**loading themes**, replaced **game files** and a **program** that runs together with Roblox. Players install it in
**Mods → Mod packages → Install a package**, and can switch it off or remove it there.

This guide builds a package step by step. For shaders there's a guide of its own: [Making shaders](making-shaders.md).

---

## 🚀 Quick start

1. In Vizstrap open **Mods → Mod packages → Make your own → New package** and pick a folder.
   You get a working package with an example of everything below.
2. Change what you like (any text editor will do).
3. Try it: **Install a package → Folder**, pick your package's folder, then **Save**.
   Close and reopen the settings - your tab is in the menu on the left.
4. Changed something? Install the folder again - a package with the same `id` is replaced.
5. Ready to share? **Pack a folder** makes a `.vzmod` file. Send that file; players install it with **Install a package → File**.

## 🗂️ What goes where

| In the package | What it does | Guide |
|---|---|---|
| `vizmod.json` | The package's name, id and author - the only file every package needs | [below](#-vizmodjson) |
| `pages\` | Tabs in Vizstrap's settings, one `.xml` file each | [Tabs](#-tabs) |
| `effects\` | Shaders drawn over the game | [Making shaders](making-shaders.md) |
| `loading-themes\` | Loading windows in Bloxstrap's XML theme format | [Loading themes](#-loading-themes) |
| `files\` | Roblox files to replace (cursors, sounds, textures…) | [Game files](#-game-files) |
| `plugin\` | A program in any language that starts with Roblox | [Programs](#-programs) |

## 📝 vizmod.json

```json
{
  "id": "your-name.my-mod",
  "name": "My mod",
  "author": "Your name",
  "version": "1.0.0",
  "description": "What the mod does, in one or two sentences.",
  "plugin": { "run": "powershell", "args": ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "plugin\\plugin.ps1"] }
}
```

| Field | Required | Notes |
|---|---|---|
| `id` | ✅ | 2-64 lowercase letters, digits, `.`, `-`, `_`. Keep it the same between versions - installing the same id replaces the old version |
| `name` | ✅ | Shown in the package list |
| `author`, `version`, `description` | | Shown in the package list |
| `plugin` | | Only for packages with a program - see [Programs](#-programs) |

## 🗂️ Tabs

Every `.xml` file in `pages\` is one tab in Vizstrap's settings, shown while the package is switched on.
A tab is a list of elements, drawn with the same cards as Vizstrap's own pages:

```xml
<VizstrapPage Title="My mod" Icon="Sparkle24" Description="Settings for my mod.">
  <Section Title="Greeting" />
  <Toggle Id="greet" Title="Say hello when I join a game" Default="true" />
  <TextBox Id="greeting" Title="Greeting" Default="Have fun!" VisibleWhen="greet" />
</VizstrapPage>
```

### The tab

| Attribute | Required | What it does |
|---|---|---|
| `Title` | ✅ | The tab's name in the menu and at the top of the page |
| `Icon` | | A [Fluent icon](https://github.com/microsoft/fluentui-system-icons) name with its size, like `Timer24`, `Sparkle24`, `Games24`, `Heart24`, `Star24`, `Settings24`. Without it: a puzzle piece |
| `Description` | | The line under the title |

### Elements

| Element | What it shows | Attributes |
|---|---|---|
| `<Section>` | A heading between groups of cards | `Title` |
| `<Text>` | A paragraph - the text goes inside the element | `Muted="true"` for small grey notes |
| `<Link>` | A card that opens a web page | `Title`, `Url` (must be `https://`), `Description` |
| `<Toggle>` | A switch | `Id`, `Title`, `Description`, `Default` (`true` / `false`) |
| `<Slider>` | A slider with its number | `Id`, `Title`, `Description`, `Min`, `Max`, `Step`, `Default` |
| `<Choice>` | A drop-down list of `<Option Value="…" Title="…" />` | `Id`, `Title`, `Description`, `Default` (an option's `Value`) |
| `<TextBox>` | A text field | `Id`, `Title`, `Description`, `Default`, `MaxLength` (200 by default) |

Every element also takes **`VisibleWhen`**: the `Id` of a toggle on the same tab. The element shows only while that toggle
is on - or, with `!` in front (`VisibleWhen="!greet"`), only while it's off.

```xml
<Toggle Id="custom" Title="Use my own colour" />
<TextBox Id="colour" Title="Colour" Default="#7F77DD" VisibleWhen="custom" />
<Text VisibleWhen="!custom" Muted="true">The accent colour is used.</Text>
```

### Values

- What the player sets is saved with Vizstrap's settings when they press **Save**, per package, under each element's `Id`.
- An `Id` starts with a letter and has letters, digits, `_`, `.` or `-`. Ids are shared by **all tabs of your package** -
  give every input its own.
- Your program gets the values - see [Programs](#-programs). Toggles arrive as `true` / `false`, sliders as numbers,
  choices as the chosen option's `Value`, text fields as text.

### Widgets - Vizstrap's own views on your tab

| Widget | What it shows |
|---|---|
| `<PlaytimePeriod />` | The **Today / This week / All time** switch |
| `<PlaytimeSummary />` | Total time, sessions and the most played game |
| `<PlaytimeChart />` | Play time of the last 7 days |
| `<PlaytimeGames />` | The games of the period with their time and a **Play** button |
| `<PlaytimeClear />` | A card to clear the history |

The **Activity** package is only widgets and one switch - its whole tab is
[`examples/activity-pack/pages/activity.xml`](../examples/activity-pack/pages/activity.xml):

```xml
<VizstrapPage Title="Activity" Title.pl="Aktywność" Icon="Timer24" Description="How long you played which game.">
  <PlaytimePeriod />
  <PlaytimeSummary />
  <PlaytimeChart VisibleWhen="chart" />
  <PlaytimeGames />

  <Section Title="Options" Title.pl="Opcje" />
  <Toggle Id="chart" Default="true" Title="Show the last 7 days" Title.pl="Pokazuj ostatnie 7 dni" />
  <PlaytimeClear />
</VizstrapPage>
```

### 🌐 Translations

Any text attribute can have a version for each language: add the language code after a dot. Vizstrap picks the
player's language and falls back to the plain attribute.

```xml
<Toggle Id="greet" Title="Say hello" Title.pl="Przywitaj się" Title.de="Hallo sagen" />
```

Codes: `pl`, `de`, `fr`, `es`, `pt` (or `pt-BR`), `ru`, `zh` (or `zh-Hans`).

### Limits

- Up to **12 tabs** from all switched-on packages together, up to 200 elements on one tab.
- A tab with a mistake isn't shown; the reason (with the line number) is in Vizstrap's log - **Having a problem?**
  in Vizstrap's menu opens it.

## 🔌 Programs

A package can bring a program in **any language**. It starts together with Roblox (in the package's folder) and is
stopped when Roblox closes. Vizstrap asks the player before it runs a package's program for the first time.

Point `plugin` in `vizmod.json` at it:

| Your program | `plugin` in vizmod.json |
|---|---|
| PowerShell | `{ "run": "powershell", "args": ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "plugin\\plugin.ps1"] }` |
| Python | `{ "run": "python", "args": ["plugin\\plugin.py"] }` |
| An `.exe` (C++, C#, Rust…) | `{ "run": "plugin\\plugin.exe" }` |

### Talking to Vizstrap

Vizstrap writes **one JSON object per line** to your program's **stdin**:

| Event | When | Fields |
|---|---|---|
| `started` | Roblox started | `vizstrap` (version), `robloxProcessId`, `settings` (your tabs' values) |
| `settings` | The player saved new values while playing | `values` |
| `gameJoined` | The player joined a game | `placeId`, `universeId`, `jobId`, `serverType`, `userId` |
| `gameLeft` | The player left the game | |
| `stopping` | Roblox closed - exit within 3 seconds | |

Your program writes **one JSON object per line** to **stdout** to do something:

| Command | What it does |
|---|---|
| `{"command":"notify","title":"My mod","text":"Hello!"}` | A notification from Vizstrap's tray icon |
| `{"command":"setLook","look":"Cinematic"}` | Changes the shaders' look: `Realistic`, `Cinematic`, `Vivid` or `Light` |
| `{"command":"log","text":"Something happened"}` | A line in Vizstrap's log |

Anything else your program prints, and everything on stderr, goes to Vizstrap's log - handy for debugging.

```powershell
# plugin\plugin.ps1 - greets the player when they join a game, if the switch on the tab is on
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

> [!TIP]
> - Write whole lines and **flush** after each one.
> - Vizstrap talks **UTF-8**. Windows PowerShell doesn't by default - keep the two `Encoding` lines at the top.
> - The template has the same program in PowerShell, Python and C++.

## 🎨 Loading themes

Put Bloxstrap-format themes in `loading-themes\`, one folder each with a `Theme.xml` (and its pictures). They appear in
**Appearance → Style → XML theme** while the package is on. Themes made for Bloxstrap work as they are -
[Bloxstrap's examples](https://github.com/bloxstraplabs/custom-bootstrapper-examples) are a good start.

## 📁 Game files

`files\` uses the same layout as Vizstrap's mods folder: a file at `files\content\textures\Cursors\KeyboardMouse\ArrowCursor.png`
replaces Roblox's arrow cursor. Files are copied onto Roblox at every launch while the package is on, and the originals
come back when it's switched off. A player's own mods win over packages.

## ✅ Before you share

- [ ] `id` is unique and stays the same between versions
- [ ] Your tab shows up after **Save** and reopening the settings
- [ ] Shaders compile (a broken one shows its error on the Shaders page)
- [ ] Your program exits on `stopping`
- [ ] **Pack a folder** → share the `.vzmod`

Everything a package does happens in Vizstrap or on its overlay - never inside Roblox. Packages must not inject
anything into the Roblox player.
