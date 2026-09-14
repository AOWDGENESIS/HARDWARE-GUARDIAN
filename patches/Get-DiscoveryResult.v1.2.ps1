function Get-DiscoveryResult {
    # =========================================================================
    # Get-DiscoveryResult  (Schema 1.2)  — Ersatz fuer die Fassung 1.1
    #
    # Behebt:
    #   F-02  Parameter-Reflection entfernt; -Path wird explizit uebergeben
    #   F-03  Get-WorkspacePath wird mit -RequestedWorkspace aufgerufen
    #   F-04  klare Semantik: -WorkspaceName ist ein RELATIVER Name unter dem
    #         autorisierten Workspace-Root (" " = Root selbst)
    #   F-08  Evidence wird ordinal sortiert (kulturunabhaengig)
    #   F-09  Hash-Eingabe ist gueltiges JSON (ConvertTo-Json -AsArray)
    #   F-10  Contract-Werte werden berechnet, nicht behauptet
    #   F-13c Schema-Mismatch wird hart abgelehnt statt still zu degradieren
    #   F-14  Limits werden durchgereicht statt erfunden
    #
    # Aufruf:
    #   $Discovery = Get-DiscoveryResult -WorkspaceName "READONLY_TEST"
    #   $Discovery = Get-DiscoveryResult                    # Root
    #   $Discovery = Get-DiscoveryResult -IncludeCanonicalJson
    #
    # Voraussetzungen im Skript-Scope: $WorkspaceRoot
    # Voraussetzungen als Funktionen:  Get-WorkspacePath, Get-WorkspaceContent
    # =========================================================================
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$WorkspaceName = "",

        [Parameter(Mandatory = $false)]
        [int]$MaxFileBytes = 65536,

        [Parameter(Mandatory = $false)]
        [int]$MaxTotalChars = 24000,

        [Parameter(Mandatory = $false)]
        [int]$MaxFiles = 500,

        [Parameter(Mandatory = $false)]
        [switch]$IncludeCanonicalJson
    )

    $Warnings = [System.Collections.Generic.List[string]]::new()

    # ---- 0. Root bestimmen -------------------------------------------------
    $Root = [string]$script:WorkspaceRoot

    if ([string]::IsNullOrWhiteSpace($Root)) {
        throw 'DISCOVERY_ROOT_NOT_INITIALIZED: $script:WorkspaceRoot ist leer.'
    }

    if ([string]::IsNullOrWhiteSpace($WorkspaceName)) {
        $Target = $Root
    }
    else {
        if ([System.IO.Path]::IsPathRooted($WorkspaceName)) {
            throw "DISCOVERY_WORKSPACE_NAME_NOT_RELATIVE: $WorkspaceName"
        }

        $Target = Join-Path $Root $WorkspaceName
    }

    # ---- 1. Autorisierung (Containment, Reparse, Existenz) -----------------
    $WorkspacePath = Get-WorkspacePath -RequestedWorkspace $Target

    # ---- 2. Inhalte lesen (fester Vertrag, kein Reflection) ----------------
    $ContentCommand = Get-Command -Name Get-WorkspaceContent -CommandType Function -ErrorAction Stop

    $ContentParams = @{
        Path          = $WorkspacePath
        MaxFileBytes  = $MaxFileBytes
        MaxTotalChars = $MaxTotalChars
    }

    if ($ContentCommand.Parameters.ContainsKey('MaxFiles')) {
        $ContentParams['MaxFiles'] = $MaxFiles
    }
    elseif ($MaxFiles -gt 0) {
        $Warnings.Add("MAX_FILES_NOT_ENFORCED_BY_CONTENT: Get-WorkspaceContent kennt keinen -MaxFiles Parameter.")
    }

    if ($ContentCommand.Parameters.ContainsKey('AuthorizedRoot')) {
        $ContentParams['AuthorizedRoot'] = $Root
    }
    else {
        $Warnings.Add("AUTHORIZED_ROOT_NOT_PASSED: Get-WorkspaceContent kennt keinen -AuthorizedRoot Parameter.")
    }

    $RawContent = Get-WorkspaceContent @ContentParams

    if ($RawContent -is [string]) {
        try {
            $Content = $RawContent | ConvertFrom-Json -Depth 64
        }
        catch {
            throw "DISCOVERY_CONTENT_JSON_FAILED: $($_.Exception.Message)"
        }
    }
    else {
        $Content = $RawContent
    }

    if ($null -eq $Content) {
        throw 'DISCOVERY_CONTENT_EMPTY'
    }

    $ContentKeys = @($Content.PSObject.Properties.Name)

    # ---- 3. Schema pruefen (fail-closed statt still leer) ------------------
    $ReadKey = $null

    foreach ($Candidate in @('ReadFiles', 'ContentReadFiles', 'Files', 'Read')) {
        if ($ContentKeys -contains $Candidate) { $ReadKey = $Candidate; break }
    }

    if ($null -eq $ReadKey) {
        throw "CONTENT_SCHEMA_MISMATCH: keine Read-Dateiliste gefunden. Vorhandene Felder: $($ContentKeys -join ', ')"
    }

    if ($ReadKey -ne 'ReadFiles') {
        $Warnings.Add("LEGACY_SCHEMA_ALIAS: gelesen aus '$ReadKey' statt 'ReadFiles'.")
    }

    $BlockedKey = $null

    foreach ($Candidate in @('BlockedFiles', 'ContentBlockedFiles', 'Blocked')) {
        if ($ContentKeys -contains $Candidate) { $BlockedKey = $Candidate; break }
    }

    if ($null -eq $BlockedKey) {
        $Warnings.Add('BLOCKED_LIST_MISSING: keine Blocked-Liste im Content-Objekt.')
    }

    if ($ContentKeys -contains 'ReadOnly') {
        $ReadOnly = [bool]$Content.ReadOnly
    }
    else {
        throw 'CONTENT_SCHEMA_INCOMPLETE: Feld ReadOnly fehlt.'
    }

    if (-not $ReadOnly) {
        throw 'DISCOVERY_READONLY_CONTRACT_FAILED'
    }

    if ($ContentKeys -contains 'SchemaVersion') {
        $SchemaVersion = [string]$Content.SchemaVersion
    }
    else {
        $SchemaVersion = 'unversioned'
        $Warnings.Add('CONTENT_SCHEMA_UNVERSIONED: Content-Objekt hat kein SchemaVersion-Feld.')
    }

    # ---- 4. Projektion mit Pflichtfeldpruefung -----------------------------
    $ReadFiles = @($Content.$ReadKey)

    if (($null -eq $BlockedKey) -or (@($Content.$BlockedKey).Count -eq 0)) {
        $BlockedFiles = @()
    }
    else {
        $BlockedFiles = @($Content.$BlockedKey)
    }

    $NormalizedRead = [System.Collections.Generic.List[object]]::new()
    $Index = 0

    foreach ($File in $ReadFiles) {
        $Index++
        $Keys = @($File.PSObject.Properties.Name)

        foreach ($Required in @('Path', 'Size', 'SHA256')) {
            if ($Keys -notcontains $Required) {
                throw "READ_ENTRY_INCOMPLETE: Eintrag $Index fehlt das Feld '$Required'."
            }
        }

        $PathValue = [string]$File.Path

        if ([string]::IsNullOrWhiteSpace($PathValue)) {
            throw "READ_ENTRY_INVALID: Eintrag $Index hat einen leeren Path."
        }

        if ([string]::IsNullOrWhiteSpace([string]$File.SHA256)) {
            throw "READ_ENTRY_INVALID: Eintrag $Index hat keinen SHA256."
        }

        $NormalizedRead.Add(
            [pscustomobject][ordered]@{
                Path         = $PathValue
                Size         = [int64]$File.Size
                Extension    = [string]$File.Extension
                Encoding     = [string]$File.Encoding
                SHA256       = ([string]$File.SHA256).ToUpperInvariant()
                ContentChars = [int]$File.ContentChars
                Truncated    = [bool]$File.Truncated
            }
        )
    }

    $NormalizedBlocked = [System.Collections.Generic.List[object]]::new()

    foreach ($File in $BlockedFiles) {
        $NormalizedBlocked.Add(
            [pscustomobject][ordered]@{
                Path      = [string]$File.Path
                Size      = $(if ($null -ne $File.Size) { [int64]$File.Size } else { [int64]0 })
                Extension = [string]$File.Extension
                Reason    = [string]$File.Reason
            }
        )
    }

    # ---- 5. Evidence = Projektion der Read-Dateien (eine Quelle) -----------
    $Evidence = [System.Collections.Generic.List[object]]::new()

    foreach ($Item in $NormalizedRead) {
        $Evidence.Add(
            [pscustomobject][ordered]@{
                Path         = $Item.Path
                Size         = $Item.Size
                Extension    = $Item.Extension
                Encoding     = $Item.Encoding
                SHA256       = $Item.SHA256
                ContentChars = $Item.ContentChars
                Truncated    = $Item.Truncated
            }
        )
    }

    # Gegenprobe: liefert Get-WorkspaceContent eine eigene Evidence-Liste,
    # muss sie inhaltlich (Path + SHA256) identisch sein.
    $EvidenceIntegrityChecked = $false

    if ($ContentKeys -contains 'Evidence') {
        $ProvidedEvidence = @($Content.Evidence)

        if ($ProvidedEvidence.Count -ne $Evidence.Count) {
            throw "EVIDENCE_COUNT_MISMATCH: provided=$($ProvidedEvidence.Count) expected=$($Evidence.Count)"
        }

        $ProvidedMap = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
        $ProvidedDuplicates = $false

        foreach ($Item in $ProvidedEvidence) {
            $Key = [string]$Item.Path

            if ($ProvidedMap.ContainsKey($Key)) { $ProvidedDuplicates = $true }
            else { $ProvidedMap[$Key] = ([string]$Item.SHA256).ToUpperInvariant() }
        }

        if ($ProvidedDuplicates) {
            throw 'EVIDENCE_INTEGRITY_FAILED: doppelte Pfade in der gelieferten Evidence-Liste.'
        }

        foreach ($Item in $Evidence) {
            if (-not $ProvidedMap.ContainsKey($Item.Path)) {
                throw "EVIDENCE_INTEGRITY_FAILED: Pfad fehlt in gelieferter Evidence: $($Item.Path)"
            }

            if ($ProvidedMap[$Item.Path] -ne $Item.SHA256) {
                throw "EVIDENCE_INTEGRITY_FAILED: SHA256 weicht ab fuer $($Item.Path)"
            }
        }

        $EvidenceIntegrityChecked = $true
    }

    # ---- 6. Deterministische Serialisierung (ordinal, gueltiges JSON) ------
    $OrdinalPaths = [string[]]@($Evidence | ForEach-Object { [string]$_.Path })
    [System.Array]::Sort($OrdinalPaths, [System.StringComparer]::Ordinal)

    $ByPath = [System.Collections.Generic.Dictionary[string, object]]::new([System.StringComparer]::Ordinal)

    foreach ($Item in $Evidence) {
        $ByPath[$Item.Path] = $Item
    }

    $SortedEvidence = [object[]]@(
        foreach ($PathKey in $OrdinalPaths) {
            $ByPath[$PathKey]
        }
    )

    $EvidenceJson = ConvertTo-Json `
        -InputObject @($SortedEvidence) `
        -Depth 32 `
        -Compress `
        -AsArray

    if (-not (Test-Json -Json $EvidenceJson)) {
        throw 'EVIDENCE_JSON_INVALID: serialisierte Evidence ist kein gueltiges JSON.'
    }

    # Unabhaengiger Determinismus-Beweis: zweimal serialisieren muss identisch sein
    $EvidenceJsonSecond = ConvertTo-Json `
        -InputObject @($SortedEvidence) `
        -Depth 32 `
        -Compress `
        -AsArray

    if ($EvidenceJson -ne $EvidenceJsonSecond) {
        throw 'EVIDENCE_SERIALIZATION_UNSTABLE'
    }

    $EvidenceBytes = [System.Text.UTF8Encoding]::new($false).GetBytes($EvidenceJson)
    $EvidenceHashBytes = [System.Security.Cryptography.SHA256]::HashData($EvidenceBytes)
    $EvidenceHash = ([System.BitConverter]::ToString($EvidenceHashBytes) -replace '-', '').ToUpperInvariant()

    # ---- 7. Limits durchreichen, nicht erfinden ----------------------------
    if ($ContentKeys -contains 'Limits') {
        $Limits = $Content.Limits
        $LimitsSource = 'content'
    }
    else {
        $Limits = [pscustomobject][ordered]@{
            MaxFileBytes  = $MaxFileBytes
            MaxTotalChars = $MaxTotalChars
            MaxFiles      = $null
        }
        $LimitsSource = 'discovery-argument'
        $Warnings.Add('CONTENT_LIMITS_MISSING: Limits wurden nicht aus dem Content-Objekt uebernommen.')
    }

    # ---- 8. Security-Reasons und unbekannte Gruende ------------------------
    $KnownReasons = @(
        'REPARSE_POINT_BLOCKED'
        'EXTENSION_NOT_ALLOWED'
        'FILE_SIZE_LIMIT'
        'BINARY_CONTENT_DETECTED'
        'INVALID_TEXT_ENCODING'
        'TOTAL_CONTENT_LIMIT_REACHED'
        'MAX_FILES_REACHED'
        'READ_FAILED'
        'SECRET_REDACTED'
    )

    $SecurityReasons = @(
        $NormalizedBlocked |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_.Reason) } |
            Select-Object -ExpandProperty Reason -Unique |
            Sort-Object
    )

    $UnknownReasons = @(
        $SecurityReasons |
            Where-Object { $KnownReasons -notcontains $_ }
    )

    if ($UnknownReasons.Count -gt 0) {
        $Warnings.Add("UNKNOWN_BLOCK_REASONS: $($UnknownReasons -join ', ')")
    }

    # ---- 9. Ergebnis -------------------------------------------------------
    $InventoryCount = $NormalizedRead.Count + $NormalizedBlocked.Count

    $Result = [pscustomobject][ordered]@{
        DiscoveryVersion = '1.2'
        SchemaVersion    = $SchemaVersion
        Workspace        = $WorkspacePath
        Inventory        = [pscustomobject][ordered]@{
            Count        = $InventoryCount
            ReadCount    = $NormalizedRead.Count
            BlockedCount = $NormalizedBlocked.Count
        }
        ReadFiles        = @($NormalizedRead)
        BlockedFiles     = @($NormalizedBlocked)
        Evidence         = @($SortedEvidence)
        EvidenceHash     = $EvidenceHash
        EvidenceChars    = $EvidenceJson.Length
        Limits           = $Limits
        LimitsSource     = $LimitsSource
        Security         = [pscustomobject][ordered]@{
            ReadOnly = $ReadOnly
            Reasons  = @($SecurityReasons)
        }
        Unknown          = [pscustomobject][ordered]@{
            Reasons = @($UnknownReasons)
        }
        Contract         = [pscustomobject][ordered]@{
            ReadOnly                     = [bool]$ReadOnly
            EvidenceHashAlgorithm        = 'SHA256'
            EvidenceJsonValid            = $true
            EvidenceJsonIsArray          = $true
            EvidenceSort                 = 'Ordinal'
            EvidenceSerializationStable  = $true
            EvidenceIntegrityChecked     = $EvidenceIntegrityChecked
            EvidenceCountMatchesRead     = ($NormalizedRead.Count -eq $SortedEvidence.Count)
            AllPathsOrdinalUnique        = ($OrdinalPaths.Count -eq $ByPath.Count)
        }
        Warnings         = @($Warnings)
    }

    if ($IncludeCanonicalJson) {
        $Result | Add-Member -NotePropertyName EvidenceCanonicalJson -NotePropertyValue $EvidenceJson
    }

    return $Result
}
