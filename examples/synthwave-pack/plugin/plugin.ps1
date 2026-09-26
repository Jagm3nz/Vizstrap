# Synthwave Pack's plugin: "Now playing" with the game's name when you join, and how long you played when you leave.
# Vizstrap sends one JSON object per line on stdin; each line written to stdout is a command (see the package guide).
# Saved as UTF-8 with a BOM so Windows PowerShell reads the Polish letters right.

# Vizstrap talks UTF-8 both ways, without a BOM (Windows PowerShell's console default isn't UTF-8)
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding $false
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Send($command) {
    [Console]::Out.WriteLine(($command | ConvertTo-Json -Compress))
    [Console]::Out.Flush()
}

# the game's name from Roblox's public API (no account needed), or $null when it can't be had
function GameName($universeId) {
    try {
        $reply = Invoke-RestMethod -Uri "https://games.roblox.com/v1/games?universeIds=$universeId" -UseBasicParsing -TimeoutSec 5
        return $reply.data[0].name
    }
    catch {
        return $null
    }
}

$game = $null
$joined = $null

while ($null -ne ($line = [Console]::In.ReadLine())) {
    $message = $line | ConvertFrom-Json

    switch ($message.event) {
        'gameJoined' {
            $game = GameName $message.universeId
            $joined = Get-Date

            if ($game) {
                Send @{ command = 'notify'; title = 'Synthwave Pack'; text = "Grasz w: $game" }
            }

            Send @{ command = 'log'; text = "joined place $($message.placeId): $game" }
        }
        'gameLeft' {
            if ($joined) {
                $minutes = [math]::Max(1, [math]::Round(((Get-Date) - $joined).TotalMinutes))
                $text = if ($game) { "Grałeś $minutes min w $game" } else { "Grałeś $minutes min" }
                Send @{ command = 'notify'; title = 'Synthwave Pack'; text = $text }
            }

            $game = $null
            $joined = $null
        }
        'stopping' {
            exit
        }
    }
}
