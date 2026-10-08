param(
    [Parameter(Mandatory = $true)][string]$Specification,
    [Parameter(Mandatory = $true)][string]$Output
)

$ErrorActionPreference = 'Stop'
$raw = Get-Content -LiteralPath $Specification -Raw -Encoding UTF8
$skillIds = [ordered]@{
    Cooking = 'cooking'; Crafting = 'crafting'; Farming = 'farming'; Fishing = 'fishing'
    Jumping = 'jump'; Pickaxes = 'pickaxes'; Riding = 'ride'; Running = 'run'
    Sneaking = 'sneak'; Swimming = 'swim'; WoodCutting = 'woodcutting'
    Swords = 'swords'; Axes = 'axes'; Clubs = 'clubs'; Knives = 'knives'; Spears = 'spears'
    Polearms = 'polearms'; Bows = 'bows'; Crossbows = 'crossbows'; Fists = 'fists'
    Blocking = 'blocking'; Dodge = 'dodge'; ElementalMagic = 'elementalmagic'; BloodMagic = 'bloodmagic'
}

function Escape-CSharp([string]$value) {
    return $value.Trim().Replace('\', '\\').Replace('"', '\"').Replace("`r", '').Replace("`n", '\n')
}

$rows = [System.Collections.Generic.List[string]]::new()
$sectionPattern = '(?ms)^\[(?<skill>[^\]]+)\]\s*(?<body>.*?)(?=^\[[^\]]+\]|^={10,}|\z)'
$perkPattern = '(?ms)^(?<level>35|70|100)\s+—\s+«(?<name>[^»]+)»\s*Механіка:\s*(?<desc>.*?)\s*Flavor:\s*(?<flavor>.*?)(?=^\s*(?:35|70|100)\s+—|\z)'

foreach ($section in [regex]::Matches($raw, $sectionPattern)) {
    $skill = $section.Groups['skill'].Value.Trim()
    if (-not $skillIds.Contains($skill)) { continue }
    $id = $skillIds[$skill]
    foreach ($perk in [regex]::Matches($section.Groups['body'].Value, $perkPattern)) {
        $level = $perk.Groups['level'].Value
        $prefix = "vm_perk_${id}_${level}"
        $name = Escape-CSharp $perk.Groups['name'].Value
        $desc = Escape-CSharp $perk.Groups['desc'].Value
        $flavor = Escape-CSharp $perk.Groups['flavor'].Value
        $rows.Add("            words[`"${prefix}_name`"] = `"$name`";")
        $rows.Add("            words[`"${prefix}_desc`"] = `"$desc`";")
        $rows.Add("            words[`"${prefix}_flavor`"] = `"$flavor`";")
    }
}

if ($rows.Count -ne 216) {
    throw "Expected 216 localization strings (72 perks), generated $($rows.Count)."
}

$content = @"
using System.Collections.Generic;

namespace ValheimMastery
{
    internal static partial class PerkLocalization
    {
        static partial void AddUkrainianPerkWords(Dictionary<string, string> words)
        {
$($rows -join "`r`n")
        }
    }
}
"@

[System.IO.File]::WriteAllText($Output, $content, [System.Text.UTF8Encoding]::new($false))
Write-Output "Generated $($rows.Count) strings in $Output"
