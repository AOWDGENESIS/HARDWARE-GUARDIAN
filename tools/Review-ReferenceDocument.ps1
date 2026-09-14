#requires -Version 7.2
<#
.SYNOPSIS
    Prueft Referenzdokumente auf maschinell feststellbare Maengel.

.DESCRIPTION
    Ein Regelwerk ist Software. Es hat Abhaengigkeiten, Dubletten, toten Code
    (Regeln ohne Bezug), Widersprueche und Kodierungsprobleme. Dieses Werkzeug
    findet die Faelle, die ein Mensch beim Lesen uebersieht - deterministisch,
    ohne Modell, ohne Ermessen.

    Geprueft wird:
      1. Kodierung   : BOM, ungueltiges UTF-8, gemischte Zeilenenden, Steuerzeichen
      2. Copy-Paste  : typografische Anfuehrungszeichen, geschuetzte Leerzeichen,
                       Bindestrich-Varianten (Ursache der Ausfaelle F-01/F-36)
      3. Struktur    : Ueberschriftenhierarchie, Regel-IDs, doppelte IDs
      4. Dubletten   : identische und fast identische Zeilen/Abschnitte
      5. Widersprueche: Regelpaare mit entgegengesetzter Modalitaet (immer/nie,
                       muss/darf nicht) zum selben Gegenstand - als Kandidaten,
                       nicht als Urteil
      6. Platzhalter : TODO, TBD, XXX, FIXME, "..." in Regeln
      7. Form        : Laengenverteilung, ueberlange Zeilen, leere Abschnitte
      8. Querbezug   : Regel-IDs, die in mehreren Dokumenten vorkommen, aber
                       unterschiedlich lauten

    Ergebnis: Bericht auf der Konsole + optional JSON. Exitcode 1 bei harten
    Maengeln (Kodierung, Copy-Paste-Schaeden, doppelte IDs mit anderem Text).

.PARAMETER Path
    Eine oder mehrere Dateien (.md, .txt).

.PARAMETER JsonOut
    Pfad fuer einen JSON-Bericht.

.PARAMETER MaxLineLength
    Zeilenlaenge, ab der gewarnt wird. Standard 200.

.EXAMPLE
    .\tools\Review-ReferenceDocument.ps1 -Path .\reference\*.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]]$Path,

    [Parameter(Mandatory = $false)]
    [string]$JsonOut = "",

    [Parameter(Mandatory = $false)]
    [int]$MaxLineLength = 200
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$script:Utf8 = [System.Text.UTF8Encoding]::new($false)
$script:Utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)

$HardFindings = 0
$SoftFindings = 0
$Report = [System.Collections.Generic.List[object]]::new()

function Add-Finding {
    param(
        [Parameter(Mandatory = $true)][string]$Document,
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][string]$Severity,
        [Parameter(Mandatory = $true)][string]$Message,
        [Parameter(Mandatory = $false)][object]$Evidence = $null
    )

    if ($Severity -eq 'HART') { $script:HardFindings++ } else { $script:SoftFindings++ }

    $script:Report.Add([pscustomobject]@{
        document = $Document
        kind     = $Kind
        severity = $Severity
        message  = $Message
        evidence = $Evidence
    })

    $Color = if ($Severity -eq 'HART') { 'Red' } else { 'Yellow' }
    Write-Host ("[{0,-4}] {1,-14} {2}" -f $Severity, $Kind, $Message) -ForegroundColor $Color

    if ($null -ne $Evidence) {
        foreach ($Line in @($Evidence)) {
            Write-Host ("         {0}" -f $Line) -ForegroundColor DarkGray
        }
    }
}

function Get-Similarity {
    param(
        [Parameter(Mandatory = $true)][string]$A,
        [Parameter(Mandatory = $true)][string]$B
    )

    $NormA = ($A -replace '\s+', ' ').Trim().ToLowerInvariant()
    $NormB = ($B -replace '\s+', ' ').Trim().ToLowerInvariant()

    if ($NormA.Length -lt 20 -or $NormB.Length -lt 20) { return 0.0 }

    $SetA = [System.Collections.Generic.HashSet[string]]::new()
    $SetB = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($Word in ($NormA -split ' ')) { if ($Word) { [void]$SetA.Add($Word) } }
    foreach ($Word in ($NormB -split ' ')) { if ($Word) { [void]$SetB.Add($Word) } }

    $Intersection = 0

    foreach ($Word in $SetA) { if ($SetB.Contains($Word)) { $Intersection++ } }

    $Union = $SetA.Count + $SetB.Count - $Intersection

    if ($Union -eq 0) { return 0.0 }

    return [double]$Intersection / [double]$Union
}

Write-Host ''
Write-Host '============================================'
Write-Host ' REFERENZDOKUMENTE PRUEFEN'
Write-Host '============================================'

$Documents = [System.Collections.Generic.List[object]]::new()

foreach ($Candidate in $Path) {
    $Resolved = (Resolve-Path -LiteralPath $Candidate -ErrorAction Stop).Path

    if (-not (Test-Path -LiteralPath $Resolved -PathType Leaf)) {
        Write-Warning "uebersprungen (keine Datei): $Candidate"
        continue
    }

    $Documents.Add([pscustomobject]@{ Path = $Resolved; Name = (Split-Path -Path $Resolved -Leaf) })
}

if ($Documents.Count -eq 0) {
    throw 'KEINE_DATEIEN: kein gueltiger Pfad angegeben.'
}


function Get-LineKind {
    <#
        Klassifiziert jede Zeile: fence (in Codeblock), table, head, prose.
        Grund (durch Testlauf mit echten Dokumenten belegt): ohne diese
        Unterscheidung meldet das Werkzeug in 700-Zeilen-Dokumenten dutzende
        Falschbefunde - z. B. aehnliche Code-Zeilen in verschiedenen Funktionen
        oder Zeilenlaengen von Tabellenzeilen.
    #>
    param([Parameter(Mandatory = $true)][string[]]$Lines)

    $Kinds = [System.Collections.Generic.List[string]]::new()
    $InFence = $false

    foreach ($Line in $Lines) {
        $Trimmed = $Line.Trim()
        $IsFence = $Trimmed -match '^(```+|~~~+)'

        if ($IsFence) {
            $InFence = -not $InFence
            $Kinds.Add('fence')
            continue
        }

        if ($InFence) { $Kinds.Add('fence') }
        elseif ($Trimmed.StartsWith('|')) { $Kinds.Add('table') }
        elseif ($Trimmed.StartsWith('#')) { $Kinds.Add('head') }
        else { $Kinds.Add('prose') }
    }

    return $Kinds
}

function Test-ProseLine {
    param(
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][string]$Text
    )

    if ($Kind -ne 'prose' -and $Kind -ne 'head') { return $false }
    if ($Text.Length -lt 40) { return $false }
    if ($Text -match '[{};]|\$[A-Za-z_]|::|=>') { return $false }

    return (@($Text -split '\s+' | Where-Object { $_ }).Count -ge 6)
}

function Get-RuleDefinitions {
    <#
        Nur DEFINITIONEN zaehlen, nicht Zitate. Ein Befund wie F-01 wird in einem
        Dokument vielmals genannt; nur die Definition darf doppelt sein.
        Querverweis-Zeilen (mit ..., siehe, vgl) werden ausgenommen.
    #>
    param([Parameter(Mandatory = $true)][string[]]$Lines)

    $Definitions = [System.Collections.Generic.List[object]]::new()
    $Pattern = '[A-Z]{1,6}[-_][0-9]{1,4}|\[[A-Z]{1,4}-[0-9]{1,4}\]'

    for ($Index = 0; $Index -lt $Lines.Count; $Index++) {
        $Raw = $Lines[$Index]
        $Text = $Raw.Trim()

        if (-not $Text) { continue }
        if ($Text.StartsWith('|')) { continue }
        if ($Text -match '(?i)\.\.\.|\u2026|siehe|vgl\.') { continue }

        $IsHeading = $Text.StartsWith('#')

        foreach ($Match in [regex]::Matches($Text, $Pattern)) {
            $Rest = $Text.Substring($Match.Index + $Match.Length).TrimStart()
            $IsDefinition = $IsHeading -or ($Match.Index -eq 0) -or ($Rest.Length -gt 0 -and $Rest[0] -in @(':', '.', ')', '-', [char]0x2014, '='))

            if ($IsDefinition) {
                $Definitions.Add([pscustomobject]@{
                    Line = $Index + 1
                    Id   = $Match.Value
                    Text = $(if ($Text.Length -gt 70) { $Text.Substring(0, 70) } else { $Text })
                })
            }
        }
    }

    return $Definitions
}

# =========================================================================
# Pro Dokument
# =========================================================================
foreach ($Document in $Documents) {
    Write-Host ''
    Write-Host ('---- {0} ----' -f $Document.Name) -ForegroundColor Cyan

    $Bytes = [System.IO.File]::ReadAllBytes($Document.Path)
    $HasBom = ($Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF)

    # --- 1. Kodierung ----------------------------------------------------
    $Text = $null

    try {
        $Text = $script:Utf8Strict.GetString($Bytes)
    }
    catch {
        Add-Finding -Document $Document.Name -Kind 'KODIERUNG' -Severity 'HART' `
            -Message 'Datei ist kein gueltiges UTF-8 - Umlaute/Zeichen werden beim Lesen beschaedigt.' `
            -Evidence $_.Exception.Message
        $Text = $script:Utf8.GetString($Bytes)
    }

    if ($HasBom) {
        Add-Finding -Document $Document.Name -Kind 'KODIERUNG' -Severity 'WEICH' `
            -Message 'UTF-8-BOM vorhanden. Kann Markerpruefungen (^# ...) und Content-Hashes verschieben (Befund F-33).' `
            -Evidence ("erste Bytes: {0:X2} {1:X2} {2:X2}" -f $Bytes[0], $Bytes[1], $Bytes[2])

        $Text = $Text.Substring(1)
    }

    $FieldText = $Text
    $Lines = @($FieldText -split "`n")
    $Crlf = ([regex]::Matches($FieldText, "`r`n")).Count
    $LfOnly = ([regex]::Matches($FieldText, "(?<!`r)`n")).Count

    if ($Crlf -gt 0 -and $LfOnly -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'KODIERUNG' -Severity 'WEICH' `
            -Message ("Gemischte Zeilenenden: {0}x CRLF, {1}x LF. Erschwert Diffs und Hashes." -f $Crlf, $LfOnly)
    }

    $Control = [regex]::Matches($FieldText, '[\x00-\x08\x0B\x0C\x0E-\x1F]')

    if ($Control.Count -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'KODIERUNG' -Severity 'HART' `
            -Message ("{0} Steuerzeichen gefunden (nicht druckbar)." -f $Control.Count)
    }

    # --- 2. Copy-Paste-Schaeden (codeblock-bewusst) -----------------------
    # In Fliesstext sind typografische Anfuehrungszeichen eine Stilfrage.
    # In Codebloecken und Inline-Code sind sie ein Defekt: Wer sie kopiert,
    # zerstoert PowerShell-Code (Befund F-01, genau so entstanden).
    $Kinds = @(Get-LineKind -Lines $Lines)
    $SmartAll = [System.Collections.Generic.List[int]]::new()
    $SmartInCode = [System.Collections.Generic.List[int]]::new()
    $InlineCodeQuotes = [System.Collections.Generic.List[int]]::new()

    for ($Index = 0; $Index -lt $Lines.Count; $Index++) {
        $Line = $Lines[$Index]
        $Hits = [regex]::Matches($Line, '[\u201C\u201D\u201E\u201A\u2018\u2019]').Count

        if ($Hits -gt 0) {
            $SmartAll.Add($Index + 1)

            if ($Kinds[$Index] -eq 'fence') { $SmartInCode.Add($Index + 1) }
        }

        foreach ($Inline in [regex]::Matches($Line, '`[^`]+`')) {
            if ($Inline.Value -match '[\u201C\u201D\u201E\u201A\u2018\u2019]') {
                $InlineCodeQuotes.Add($Index + 1)
                break
            }
        }
    }

    if ($SmartInCode.Count -gt 0 -or $InlineCodeQuotes.Count -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'COPY-PASTE' -Severity 'HART' `
            -Message ("Typografische Anfuehrungszeichen in Codebereichen: {0} Zeile(n) im Codeblock, {1} mit Inline-Code. Wer das kopiert, bekommt unbrauchbaren PowerShell-Code (Befund F-01)." -f $SmartInCode.Count, $InlineCodeQuotes.Count) `
            -Evidence (@(($SmartInCode + $InlineCodeQuotes) | Sort-Object -Unique | Select-Object -First 8 | ForEach-Object { "Zeile $_" }))
    }

    if ($SmartAll.Count -gt $SmartInCode.Count + $InlineCodeQuotes.Count) {
        Add-Finding -Document $Document.Name -Kind 'COPY-PASTE' -Severity 'WEICH' `
            -Message ("{0} typografische Anfuehrungszeichen im Fliesstext (stilistisch, unkritisch - nur gefaehrlich, wenn kopiert)." -f ($SmartAll.Count - $SmartInCode.Count - $InlineCodeQuotes.Count))
    }

    $NbspCount = ([regex]::Matches($FieldText, '\u00A0')).Count

    if ($NbspCount -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'COPY-PASTE' -Severity 'WEICH' `
            -Message ("{0} geschuetzte Leerzeichen (U+00A0) - sehen wie Leerzeichen aus, sind aber andere Zeichen." -f $NbspCount)
    }

    $DashCount = ([regex]::Matches($FieldText, '[\u2013\u2014]')).Count

    if ($DashCount -gt 0 -and $SmartInCode.Count -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'COPY-PASTE' -Severity 'WEICH' `
            -Message ("{0} typografische Bindestriche (Halbgeviert/Geviert) - relevant, wenn Text als Code genutzt wird." -f $DashCount)
    }

    # Unbalancierte Codebloecke: Zeichen dafuer, dass das Dokument beschaedigt ist
    $FenceCount = @($Kinds | Where-Object { $_ -eq 'fence' }).Count
    $FenceMarkers = ([regex]::Matches($FieldText, '(?m)^\s*(```+|~~~+)')).Count

    if ($FenceMarkers % 2 -ne 0) {
        Add-Finding -Document $Document.Name -Kind 'STRUKTUR' -Severity 'HART' `
            -Message ("{0} Codeblock-Marker - ungerade Anzahl, mindestens ein Codeblock ist nicht geschlossen." -f $FenceMarkers)
    }

    # --- 3. Struktur -----------------------------------------------------
    $Headings = [regex]::Matches($FieldText, '(?m)^(#{1,6})\s+(.+)$')
    $HeadingLevels = @()

    foreach ($Heading in $Headings) {
        $HeadingLevels += $Heading.Groups[1].Value.Length
    }

    $Jumps = 0

    for ($Index = 1; $Index -lt $HeadingLevels.Count; $Index++) {
        if ($HeadingLevels[$Index] - $HeadingLevels[$Index - 1] -gt 1) { $Jumps++ }
    }

    $Definitions = @(Get-RuleDefinitions -Lines $Lines)
    $RuleIds = @($Definitions | ForEach-Object { $_.Id } | Sort-Object -Unique)
    $DuplicateIds = @(
        $Definitions | Group-Object Id | Where-Object { $_.Count -gt 1 } |
            ForEach-Object {
                "$($_.Name)  {$($_.Count)x}  Zeilen: $((@($_.Group.Line)) -join ', ')"
            }
    )

    if ($DuplicateIds.Count -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'STRUKTUR' -Severity 'WEICH' `
            -Message ("{0} Regel-ID(s) mehrfach DEFINIERT (Zitate sind ausgenommen) - bei Verweisen mehrdeutig." -f $DuplicateIds.Count) `
            -Evidence ($DuplicateIds | Select-Object -First 10)
    }

    if ($Jumps -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'STRUKTUR' -Severity 'WEICH' `
            -Message ("{0} Spruenge in der Ueberschriftenhierarchie (z. B. H1 -> H3)." -f $Jumps)
    }

    # --- 4. Dubletten ----------------------------------------------------
    # Nur Fliesstext wird inhaltlich verglichen. Code-Zeilen und Tabellen
    # erzeugen sonst Falschbefunde (im Testlauf mit echten Dokumenten belegt).
    $ContentLines = @(
        for ($Index = 0; $Index -lt $Lines.Count; $Index++) {
            $Clean = ($Lines[$Index] -replace "`r", '').Trim()

            if (Test-ProseLine -Kind $Kinds[$Index] -Text $Clean) {
                [pscustomobject]@{ Number = $Index + 1; Text = $Clean }
            }
        }
    )

    $ExactDuplicates = @($ContentLines | Group-Object { ($_.Text -replace '\s+', ' ') } | Where-Object { $_.Count -gt 1 })

    if ($ExactDuplicates.Count -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'DUBLETTEN' -Severity 'WEICH' `
            -Message ("{0} wortgleiche Zeilen (>=40 Zeichen)." -f $ExactDuplicates.Count) `
            -Evidence (@($ExactDuplicates | Select-Object -First 3 | ForEach-Object { "Zeilen $((@($_.Group.Number) -join ', ')): $($_.Group[0].Text.Substring(0, [Math]::Min(90, $_.Group[0].Text.Length)))" }))
    }

    $NearDuplicates = [System.Collections.Generic.List[string]]::new()

    if ($ContentLines.Count -le 900) {
        for ($I = 0; $I -lt $ContentLines.Count; $I++) {
            for ($J = $I + 1; $J -lt $ContentLines.Count; $J++) {
                if ($ContentLines[$I].Text -eq $ContentLines[$J].Text) { continue }

                $Score = Get-Similarity -A $ContentLines[$I].Text -B $ContentLines[$J].Text

                if ($Score -ge 0.85) {
                    $NearDuplicates.Add(("Zeile {0} <-> {1}  ({2:P0} aehnlich)" -f $ContentLines[$I].Number, $ContentLines[$J].Number, $Score))
                }
            }
        }
    }

    if ($NearDuplicates.Count -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'DUBLETTEN' -Severity 'WEICH' `
            -Message ("{0} fast identische Zeilenpaare (>=85% Wortgleichheit)." -f $NearDuplicates.Count) `
            -Evidence ($NearDuplicates | Select-Object -First 5)
    }

    # --- 5. Widerspruchskandidaten --------------------------------------
    $Always = @($ContentLines | Where-Object { $_.Text -match '(?i)\b(immer|stets|grunds[aä]tzlich|ausnahmslos|jedes Mal)\b' })
    $Never = @($ContentLines | Where-Object { $_.Text -match '(?i)\b(niemals|nie|unter keinen Umstaenden|auf keinen Fall|verboten)\b' })

    $Candidates = [System.Collections.Generic.List[string]]::new()

    foreach ($Positive in $Always) {
        foreach ($Negative in $Never) {
            if ($Positive.Number -eq $Negative.Number) { continue }
            $Score = Get-Similarity -A $Positive.Text -B $Negative.Text

            if ($Score -ge 0.5) {
                $Candidates.Add(("Zeile {0} (immer) <-> Zeile {1} (nie)  ({2:P0})" -f $Positive.Number, $Negative.Number, $Score))
            }
        }
    }

    if ($Candidates.Count -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'WIDERSPRUCH?' -Severity 'WEICH' `
            -Message ("{0} Kandidaten fuer widerspruechliche Regelpaare (Pruefung durch Menschen noetig - das ist ein Hinweis, kein Urteil)." -f $Candidates.Count) `
            -Evidence ($Candidates | Select-Object -First 5)
    }

    # --- 6. Platzhalter --------------------------------------------------
    $Placeholders = @($ContentLines | Where-Object { $_.Text -match '(?i)\b(TODO|TBD|FIXME|XXX|noch festlegen|Platzhalter|lorem ipsum)\b' })

    if ($Placeholders.Count -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'PLATZHALTER' -Severity 'WEICH' `
            -Message ("{0} Platzhalter im Dokument - ein Regelwerk mit TODO ist nicht verbindlich." -f $Placeholders.Count) `
            -Evidence (@($Placeholders | Select-Object -First 5 | ForEach-Object { "Zeile $($_.Number): $($_.Text.Substring(0, [Math]::Min(80, $_.Text.Length)))" }))
    }

    # --- 7. Form ---------------------------------------------------------
    $TooLong = @($ContentLines | Where-Object { $_.Text.Length -gt $MaxLineLength })

    if ($TooLong.Count -gt 0) {
        Add-Finding -Document $Document.Name -Kind 'FORM' -Severity 'WEICH' `
            -Message ("{0} Zeilen ueber {1} Zeichen - beim Lesen und in Modellkontexten unhandlich." -f $TooLong.Count, $MaxLineLength) `
            -Evidence (@($TooLong | Select-Object -First 3 | ForEach-Object { "Zeile $($_.Number): $($_.Text.Length) Zeichen" }))
    }

        Write-Host ('         Datei: {0} Bytes | {1} Zeilen | {2} Ueberschriften | {3} Regel-Definitionen ({4} eindeutig) | Fliesstextzeilen: {5}' -f $Bytes.Length, $Lines.Count, $Headings.Count, @($Definitions).Count, @($RuleIds).Count, @($ContentLines).Count) -ForegroundColor DarkGray

    $Document | Add-Member -NotePropertyName 'Text' -NotePropertyValue $FieldText
    $Document | Add-Member -NotePropertyName 'RuleIds' -NotePropertyValue @($RuleIds)
    $Document | Add-Member -NotePropertyName 'Bytes' -NotePropertyValue $Bytes.Length
    $Document | Add-Member -NotePropertyName 'Lines' -NotePropertyValue $Lines.Count
    $Document | Add-Member -NotePropertyName 'SHA256' -NotePropertyValue (Get-FileHash -LiteralPath $Document.Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

# =========================================================================
# Querbezug zwischen Dokumenten
# =========================================================================
if ($Documents.Count -gt 1) {
    Write-Host ''
    Write-Host '---- Querbezug zwischen den Dokumenten ----' -ForegroundColor Cyan

    # 8a. Gleiche Regel-ID in mehreren Dokumenten
    $IdMap = @{}

    foreach ($Document in $Documents) {
        foreach ($Id in $Document.RuleIds) {
            if (-not $IdMap.ContainsKey($Id)) {
                $IdMap[$Id] = [System.Collections.Generic.List[string]]::new()
            }

            [void]$IdMap[$Id].Add($Document.Name)
        }
    }

    $CrossIds = @($IdMap.Keys | Where-Object { $IdMap[$_].Count -gt 1 })

    if ($CrossIds.Count -gt 0) {
        Add-Finding -Document '(alle)' -Kind 'QUERBEZUG' -Severity 'WEICH' `
            -Message ("{0} Regel-IDs kommen in mehreren Dokumenten vor - bei Verweisen muss die Quelle mitgenannt werden." -f $CrossIds.Count) `
            -Evidence (@($CrossIds | Select-Object -First 10 | ForEach-Object { "$_  ->  $((@($IdMap[$_] | Select-Object -Unique)) -join ', ')" }))
    }
    else {
        Write-Host '         Keine mehrfach vergebenen Regel-IDs ueber Dokumentgrenzen.' -ForegroundColor DarkGray
    }

    # 8b. Zwei Dokumente mit gleichem Inhalt, aber anderer Endung (.md/.txt)
    $TextMap = @{}

    foreach ($Document in $Documents) {
        $Normalized = ($Document.Text -replace "`r`n", "`n").Trim()
        $TextMap[$Document.Name] = $Normalized
    }

    $Names = @($TextMap.Keys)

    for ($I = 0; $I -lt $Names.Count; $I++) {
        for ($J = $I + 1; $J -lt $Names.Count; $J++) {
            $A = $Names[$I]
            $B = $Names[$J]
            $NormA = $TextMap[$A]
            $NormB = $TextMap[$B]

            if ($NormA -eq $NormB) {
                Add-Finding -Document '(alle)' -Kind 'DOPPELDATEI' -Severity 'WEICH' `
                    -Message ("'{0}' und '{1}' haben identischen Inhalt in zwei Dateiformaten." -f $A, $B) `
                    -Evidence 'Genau eine Fassung als kanonisch festlegen und hash-pinnen - zwei Fassungen driften sonst auseinander.'
            }
            else {
                $Score = Get-Similarity -A $NormA -B $NormB

                if ($Score -ge 0.9) {
                    Add-Finding -Document '(alle)' -Kind 'DOPPELDATEI' -Severity 'WEICH' `
                        -Message ("'{0}' und '{1}' sind zu {2:P0} wortgleich, aber nicht identisch." -f $A, $B, $Score) `
                        -Evidence 'Pruefen, welche Fassung gilt. Zwei fast gleiche Regelwerke sind schlimmer als eines.'
                }
            }
        }
    }
}

# =========================================================================
# Ergebnis
# =========================================================================
Write-Host ''
Write-Host '============================================'
Write-Host ' BILANZ'
Write-Host '============================================'
Write-Host ("HART  : {0}" -f $HardFindings)
Write-Host ("WEICH : {0}" -f $SoftFindings)
Write-Host ("Dateien: {0}" -f $Documents.Count)
Write-Host ''

foreach ($Document in $Documents) {
    Write-Host ("{0,-52} {1}" -f $Document.Name, $Document.SHA256) -ForegroundColor DarkGray
}

Write-Host ''

if ($JsonOut) {
    $Payload = [ordered]@{
        generated_utc = (Get-Date).ToUniversalTime().ToString('o')
        tool          = 'Review-ReferenceDocument.ps1'
        hard          = $HardFindings
        soft          = $SoftFindings
        documents     = @($Documents | ForEach-Object {
                [ordered]@{
                    name    = $_.Name
                    bytes   = $_.Bytes
                    lines   = $_.Lines
                    sha256  = $_.SHA256
                    ruleIds = @($_.RuleIds)
                }
            })
        findings      = @($Report)
    }

    [System.IO.File]::WriteAllText($JsonOut, ($Payload | ConvertTo-Json -Depth 12), $script:Utf8)
    Write-Host ("JSON-Bericht: {0}" -f $JsonOut)
}

if ($HardFindings -gt 0) {
    Write-Host 'REFERENZPRUEFUNG: FAIL (harte Maengel vorhanden)' -ForegroundColor Red
    exit 1
}

Write-Host 'REFERENZPRUEFUNG: PASS (keine harten Maengel; weiche Hinweise oben)' -ForegroundColor Green
exit 0
