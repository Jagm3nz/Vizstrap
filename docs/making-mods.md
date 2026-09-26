# 📁 Replacing game files (mods)

Roblox keeps its pictures, sounds and fonts as ordinary files. A **mod** is your own file put in place of one of
them - a different cursor, a different death sound, a different font. Vizstrap copies your files over Roblox's on
every launch and puts the originals back when you remove them.

---

## 🚀 Quick start

1. **Mods → Open mods folder.**
2. Recreate the path of the Roblox file you want to replace, and put your file there under the same name.
   Example: your cursor at `content\textures\Cursors\KeyboardMouse\ArrowCursor.png`.
3. Start Roblox through Vizstrap - that's it.

To remove a mod, delete your file; the original comes back on the next launch.

The folder uses Bloxstrap's layout, so [Bloxstrap's modding guide](https://bloxstraplabs.com/wiki/features/modding/)
and mods made for Bloxstrap work too.

## 🗺️ Where things are

Paths are inside the mods folder (and inside a Roblox version folder, for the originals):

| What | File |
|---|---|
| 🖱️ Mouse cursor | `content\textures\Cursors\KeyboardMouse\ArrowCursor.png` and `ArrowFarCursor.png` |
| 💀 Death sound | `content\sounds\oof.ogg` |
| 🚶 Walking, jumping, getting up | `content\sounds\action_footsteps_plastic.mp3`, `action_jump.mp3`, `action_get_up.mp3` |
| 🌊 Swimming, falling, landing | `content\sounds\action_swim.mp3`, `action_falling.mp3`, `action_jump_land.mp3`, `impact_water.mp3` |
| 😀 Emoji | `content\fonts\TwemojiMozilla.ttf` |
| 🔄 Loading spinner | `content\textures\loading\loadingCircle.png` |
| 🧍 Avatar editor background | `ExtraContent\places\Mobile.rbxl` |

To find other files, open a Roblox version folder (**Mods → Open mods folder**, then go up to `Versions`) and look in
`content` - most names say what they are.

> [!TIP]
> - Keep the picture's **size** - a cursor is 64×64 with the arrow's tip in the **middle** of the picture.
> - Keep the **format**: `.png` stays `.png`, `.ogg` stays `.ogg`.
> - The emoji font must be a **COLR v0** font to show in colour - COLRv1, CBDT and sbix fonts come out black and white.

## ⚡ Presets - without files

**Mods → Presets** does the common ones in one click:

| Preset | Options |
|---|---|
| Mouse cursor | 2006, 2013 or your own PNG |
| Old avatar editor background | on / off |
| Classic character sounds | on / off |
| Death sound | "oof", muted or your own file |
| Emoji | Catmoji, Windows 11/10/8, OpenMoji, EmojiTwo |
| Custom font | Any `.ttf` / `.otf` replaces every font in the game |

Presets put their files in the same mods folder - a preset and your own file for the same path replace each other.
Files in the mods folder always win over [packages](making-a-package.md).

## 📦 Sharing mods

To share your files, put them in a [package](making-a-package.md) under `files\` - with the same paths as above. Players
install the package and switch it on and off with one click; their own mods still win over it.
