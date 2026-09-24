<#
.SYNOPSIS
    Mutation check: breaks one rule of the code at a time and confirms that the test suite notices.

.DESCRIPTION
    Each mutant replaces one piece of source text (for example "round half away from zero" becomes
    "banker's rounding"), runs every test, then restores the file byte for byte. A mutant is "caught"
    when at least one test fails. A mutant that survives points at a rule no test really checks.

    The tests run once on the unchanged code first. If any of them fails there, a failure could not be
    blamed on a mutant, so the check stops with exit code 2.

    Runs on Windows PowerShell 5.1 and PowerShell 7 (pwsh) on any OS. Needs a clean git working tree.

.EXAMPLE
    pwsh tools/mutation-check.ps1
    pwsh tools/mutation-check.ps1 -Configuration Release
#>
param(
    [string] $Configuration = 'Debug'
)

# dotnet writes failing tests to stderr; 'Stop' would turn those lines into terminating errors.
$ErrorActionPreference = 'Continue'
Set-Location (Join-Path $PSScriptRoot '..')

if (git status --porcelain) {
    Write-Error 'Commit or stash your changes first: the check edits source files and restores them afterwards.'
    exit 2
}

# A mutant counts as caught only when a test fails because of it, so every test must pass without one.
Write-Output 'Running the tests on the unchanged code...'
$baseline = dotnet test -c $Configuration --nologo -v q -p:TreatWarningsAsErrors=false 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $baseline -match 'Failed!|error CS\d+') {
    Write-Output $baseline
    Write-Error 'The tests fail on the unchanged code, so no mutant can be judged. Make them pass first.'
    exit 2
}

$mutants = @(
    @{ Name = 'rounding: half away from zero -> banker''s'; File = 'src/DiscountModule/Money.cs'; Find = 'MidpointRounding.AwayFromZero'; Replace = 'MidpointRounding.ToEven' },
    @{ Name = 'order: campaigns not sorted by category'; File = 'src/DiscountModule/DiscountCalculator.cs'; Find = 'campaigns.OrderBy(campaign => campaign.Category)'; Replace = 'campaigns.AsEnumerable()' },
    @{ Name = 'points cap not rounded down'; File = 'src/DiscountModule/Campaigns/PointsOnTop.cs'; Find = 'Math.Floor(runningTotal * MaxShareOfTotal)'; Replace = '(runningTotal * MaxShareOfTotal)' },
    @{ Name = 'seasonal counts partial steps'; File = 'src/DiscountModule/Campaigns/SeasonalCampaign.cs'; Find = 'Math.Floor(runningTotal / EveryAmount)'; Replace = '(runningTotal / EveryAmount)' },
    @{ Name = 'discount not capped at the running total'; File = 'src/DiscountModule/DiscountCalculator.cs'; Find = 'Math.Clamp(campaign.ComputeDiscount(context), 0m, runningTotal)'; Replace = 'campaign.ComputeDiscount(context)' },
    @{ Name = 'two campaigns of one category allowed'; File = 'src/DiscountModule/Validation/CampaignListValidator.cs'; Find = '.Where(group => group.Count() > 1)'; Replace = '.Where(group => group.Count() > 2)' },
    @{ Name = 'category % taken from the running total'; File = 'src/DiscountModule/Campaigns/CategoryPercentageOnTop.cs'; Find = 'Math.Min(ListedPriceDiscount(context.Cart), ShareLeft(context))'; Replace = 'Math.Min(context.RunningTotal * (Percentage / 100m), ShareLeft(context))' },
    @{ Name = 'category discount not capped at the category share'; File = 'src/DiscountModule/Campaigns/CategoryPercentageOnTop.cs'; Find = 'Math.Min(ListedPriceDiscount(context.Cart), ShareLeft(context))'; Replace = 'ListedPriceDiscount(context.Cart)' },
    @{ Name = 'category share rounded down'; File = 'src/DiscountModule/Money.cs'; Find = '((2 * numerator) + denominator) / (2 * denominator)'; Replace = 'numerator / denominator' },
    @{ Name = 'category share divided in decimal'; File = 'src/DiscountModule/Campaigns/CategoryPercentageOnTop.cs'; Find = 'Money.Share(context.RunningTotal, CategoryTotal(context.Cart), subtotal)'; Replace = 'Money.Round(CategoryTotal(context.Cart) * (context.RunningTotal / subtotal))' },
    @{ Name = 'percentage above 100 accepted'; File = 'src/DiscountModule/Validation/PercentageRules.cs'; Find = 'percentage > 100'; Replace = 'percentage > 1000' },
    @{ Name = 'negative price accepted'; File = 'src/DiscountModule/Validation/AmountRules.cs'; Find = 'if (value < 0)'; Replace = 'if (value < -1)' },
    @{ Name = 'input not copied before validation'; File = 'src/DiscountModule/DiscountCalculator.cs'; Find = 'cart with { Items = cart.Items.ToArray() }'; Replace = 'cart' },
    @{ Name = 'subtotal limit not enforced'; File = 'src/DiscountModule/Validation/CartValidator.cs'; Find = 'if (everyLineIsValid && cart.Subtotal > InputLimits.MaxSubtotal)'; Replace = 'if (everyLineIsValid && cart.Subtotal < 0)' },
    @{ Name = 'JSON: duplicate property not reported'; File = 'src/DiscountModule.Json/JsonObjectReader.cs'; Find = 'Record(errors, $"{path}: property \"{propertyName}\" is given more than once");'; Replace = '' },
    @{ Name = 'JSON: unexpected property not reported'; File = 'src/DiscountModule.Json/JsonObjectReader.cs'; Find = 'if (!expectedNames.Contains(name, StringComparer.OrdinalIgnoreCase))'; Replace = 'if (name.Length < 0)' },
    @{ Name = 'JSON: lost digits not reported'; File = 'src/DiscountModule.Json/JsonObjectReader.cs'; Find = 'if (writtenValue != CanonicalNumber(number.ToString(CultureInfo.InvariantCulture)))'; Replace = 'if (writtenValue is null && number == -1)' },
    @{ Name = 'JSON: names match case-sensitively'; File = 'src/DiscountModule.Json/JsonObjectReader.cs'; Find = 'string.Equals(candidate, text, StringComparison.OrdinalIgnoreCase)'; Replace = 'string.Equals(candidate, text, StringComparison.Ordinal)' },
    @{ Name = 'JSON: half surrogate pair crashes the reader'; File = 'src/DiscountModule.Json/JsonObjectReader.cs'; Find = 'catch (InvalidOperationException)'; Replace = 'catch (FormatException)' },
    @{ Name = 'CLI: control characters printed as they are'; File = 'src/DiscountModule.Cli/Output/ConsoleText.cs'; Find = 'if (!text.Any(NeedsEscape))'; Replace = 'if (text.Length >= 0)' },
    @{ Name = 'CLI: direction overrides printed as they are'; File = 'src/DiscountModule.Cli/Output/ConsoleText.cs'; Find = 'char.IsControl(character) || IsBidirectionalControl(character)'; Replace = 'char.IsControl(character)' },
    @{ Name = 'CLI: line separators printed as they are'; File = 'src/DiscountModule.Cli/Output/ConsoleText.cs'; Find = 'IsLineOrParagraphSeparator(character) || char.IsControl(character)'; Replace = 'char.IsControl(character)' },
    @{ Name = 'CLI: Thai marks counted as columns'; File = 'src/DiscountModule.Cli/Output/ResultPrinter.cs'; Find = 'is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)'; Replace = 'is not UnicodeCategory.Control' },
    @{ Name = 'CLI: long item names not shortened'; File = 'src/DiscountModule.Cli/Output/ResultPrinter.cs'; Find = 'string name = Shorten(ConsoleText.Escape(item.Name), MaxNameWidth);'; Replace = 'string name = item.Name;' },
    @{ Name = 'CLI: -- does not end the options'; File = 'src/DiscountModule.Cli/CliApp.cs'; Find = 'optionsEnded = true;'; Replace = 'optionsEnded = false;' },
    @{ Name = 'API: body size not checked while reading'; File = 'src/DiscountModule.Api/DiscountEndpoint.cs'; Find = 'if (body.Length + read > MaxOrderBytes)'; Replace = 'if (body.Length < 0)' },
    @{ Name = 'API: declared size not checked'; File = 'src/DiscountModule.Api/DiscountEndpoint.cs'; Find = 'if (request.ContentLength > MaxOrderBytes)'; Replace = 'if (request.ContentLength < 0)' },
    @{ Name = 'API: broken rule answered as 400'; File = 'src/DiscountModule.Api/DiscountEndpoint.cs'; Find = 'StatusCodes.Status422UnprocessableEntity'; Replace = 'StatusCodes.Status400BadRequest' },
    @{ Name = 'API: a charset other than UTF-8 accepted'; File = 'src/DiscountModule.Api/DiscountEndpoint.cs'; Find = 'if (OtherCharset(request) is { } charset)'; Replace = 'if (OtherCharset(request) is { } charset && charset.Length < 0)' },
    @{ Name = 'problems: JSON reader never stops at the limit'; File = 'src/DiscountModule.Json/OrderRequestParser.cs'; Find = '=> errors.Count > ProblemList.MaxReportedProblems;'; Replace = '=> errors.Count < 0;' },
    @{ Name = 'problems: CLI lists every broken rule'; File = 'src/DiscountModule.Cli/CliApp.cs'; Find = 'ProblemList.ForDisplay(ex.Errors)'; Replace = 'ex.Errors' },
    @{ Name = 'problems: API lists every broken rule'; File = 'src/DiscountModule.Api/DiscountEndpoint.cs'; Find = 'ProblemList.ForDisplay(ex.Errors)'; Replace = 'ex.Errors' },
    @{ Name = 'problems: no stop inside one object'; File = 'src/DiscountModule.Json/JsonObjectReader.cs'; Find = 'private static bool IsFull(List<string> errors) => errors.Count > ProblemList.MaxReportedProblems;'; Replace = 'private static bool IsFull(List<string> errors) => errors.Count < 0;' },
    @{ Name = 'UTF-8: invalid bytes become U+FFFD'; File = 'src/DiscountModule.Json/OrderRequestParser.cs'; Find = 'json = StrictUtf8.GetString(utf8);'; Replace = 'json = Encoding.UTF8.GetString(utf8);' },
    @{ Name = 'UTF-8: reader follows a byte order mark'; File = 'src/DiscountModule.Json/OrderRequestParser.cs'; Find = 'StrictUtf8, detectEncodingFromByteOrderMarks: false);'; Replace = 'StrictUtf8, detectEncodingFromByteOrderMarks: true);' },
    @{ Name = 'UTF-8: UTF-16 byte order mark not named'; File = 'src/DiscountModule.Json/OrderRequestParser.cs'; Find = 'if (utf8.StartsWith(Utf16LittleEndianMark) || utf8.StartsWith(Utf16BigEndianMark))'; Replace = 'if (utf8.Length < 0)' },
    @{ Name = 'UTF-8: U+FEFF not skipped'; File = 'src/DiscountModule.Json/OrderRequestParser.cs'; Find = 'if (json.StartsWith(ByteOrderMark))'; Replace = 'if (json.Length < 0)' },
    @{ Name = 'UTF-8: file decoded by guesswork'; File = 'src/DiscountModule.Json/OrderRequestParser.cs'; Find = 'utf8 = File.ReadAllBytes(path);'; Replace = 'utf8 = Encoding.UTF8.GetBytes(File.ReadAllText(path));' },
    @{ Name = 'API: body decoded leniently'; File = 'src/DiscountModule.Api/DiscountEndpoint.cs'; Find = 'OrderRequest order = OrderRequestParser.ParseUtf8(body);'; Replace = 'OrderRequest order = OrderRequestParser.Parse(System.Text.Encoding.UTF8.GetString(body));' }
)

$survivors = 0
$number = 0
foreach ($mutant in $mutants) {
    $number++
    $label = '{0,2}. {1}' -f $number, $mutant.Name
    $path = Join-Path (Get-Location) $mutant.File
    $originalBytes = [IO.File]::ReadAllBytes($path)
    $original = [Text.Encoding]::UTF8.GetString($originalBytes)
    $count = ([regex]::Matches($original, [regex]::Escape($mutant.Find))).Count
    if ($count -ne 1) {
        Write-Output "$label : NOT APPLIED (the text to replace appears $count times; update this script)"
        $survivors++
        continue
    }

    try {
        [IO.File]::WriteAllText($path, $original.Replace($mutant.Find, $mutant.Replace), (New-Object Text.UTF8Encoding($false)))
        $output = dotnet test -c $Configuration --nologo -v q -p:TreatWarningsAsErrors=false 2>&1 | Out-String
        if ($output -match 'error CS\d+') {
            $status = 'NOT APPLIED (does not compile; update this script)'
            $survivors++
        }
        elseif ($output -match 'Failed!') {
            $failing = ([regex]::Matches($output, 'Failed:\s+(\d+)') | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Sum).Sum
            $status = "caught by $failing failing test(s)"
        }
        else {
            $status = 'SURVIVED'
            $survivors++
        }
    }
    finally {
        [IO.File]::WriteAllBytes($path, $originalBytes)
    }

    Write-Output "$label : $status"
}

Write-Output ''
Write-Output ("{0} of {1} mutants caught." -f ($mutants.Count - $survivors), $mutants.Count)
exit $survivors
