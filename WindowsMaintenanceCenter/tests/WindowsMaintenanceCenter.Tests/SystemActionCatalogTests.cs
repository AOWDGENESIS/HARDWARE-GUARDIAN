using System.Text.RegularExpressions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Infrastructure.Platform;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The catalogue of actions this version knows (WMC specification, chapters 32, 38 and 96).
///
/// These tests protect the release decision itself: which actions are allowed to run, and why the
/// others are only visible. A tool cannot be released by accident, and a released action cannot
/// carry a path, an argument without a declaration or a missing program.
/// </summary>
public sealed class SystemActionCatalogTests
{
    private static readonly string[] ReadOnlyReleased =
    {
        "System.FsutilDeleteNotifyQuery",
        "Network.IpConfigAll",
        "System.SystemInfoSnapshot",
    };

    private static IReadOnlyList<RegisteredAction> Catalog => SystemActionCatalog.Create();

    [Fact]
    public void Every_action_has_a_unique_identifier_in_the_form_area_dot_name()
    {
        var ids = Catalog.Select(action => action.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.Matches(new Regex(@"^[A-Za-z][A-Za-z0-9]*\.[A-Za-z][A-Za-z0-9]*$"), id));
    }

    [Fact]
    public void The_released_actions_are_exactly_the_read_only_queries()
    {
        // Releasing a repair tool needs the approval and backup gate in front of it (chapters 30, 44)
        // and measured exit codes. This test fails as soon as somebody widens the list silently.
        var released = Catalog.Where(action => action.Allowed).Select(action => action.Id).ToList();

        Assert.Equal(ReadOnlyReleased, released);
    }

    [Fact]
    public void A_released_action_needs_no_administrator_rights_and_changes_nothing()
    {
        foreach (var action in Catalog.Where(a => a.Allowed))
        {
            Assert.False(action.RequiresAdmin, $"{action.Id} would ask for elevation although it is released as read only.");
            Assert.True(action.SelfValidating, $"{action.Id} is released without a verification of its own result.");

            foreach (var part in action.ArgumentTemplate)
            {
                Assert.DoesNotContain("scannow", part, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("restorehealth", part, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("/f", part, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void An_unreleased_action_is_marked_as_such_and_states_its_risk()
    {
        var unreleased = Catalog.Where(action => !action.Allowed).ToList();

        Assert.NotEmpty(unreleased);
        Assert.All(unreleased, action =>
        {
            Assert.False(action.Allowed);
            Assert.True(action.Risk != RiskLevel.Low, $"{action.Id} is registered but not released and must not be declared as harmless.");
            Assert.NotEmpty(action.ArgumentTemplate);
        });
    }

    [Fact]
    public void Every_action_names_a_program_without_a_path_and_a_timeout()
    {
        foreach (var action in Catalog)
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Executable), $"{action.Id} has no program.");
            Assert.DoesNotContain('\\', action.Executable);
            Assert.DoesNotContain('/', action.Executable);
            Assert.Contains(".exe", action.Executable, StringComparison.OrdinalIgnoreCase);
            Assert.True(action.Timeout > TimeSpan.Zero, $"{action.Id} has no timeout.");
            Assert.True(action.Timeout <= TimeSpan.FromHours(4), $"{action.Id} waits longer than four hours.");
        }
    }

    [Fact]
    public void Every_placeholder_in_a_template_belongs_to_a_declared_argument_and_the_other_way_round()
    {
        foreach (var action in Catalog)
        {
            var declared = action.Arguments.Select(argument => argument.Name).ToHashSet(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match match in Regex.Matches(string.Join(' ', action.ArgumentTemplate), @"\{([A-Za-z][A-Za-z0-9]*)\}"))
            {
                used.Add(match.Groups[1].Value);
            }

            Assert.Equal(declared.OrderBy(name => name, StringComparer.Ordinal), used.OrderBy(name => name, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void A_rollback_action_that_is_named_has_to_be_registered_as_well()
    {
        var ids = Catalog.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var action in Catalog.Where(a => a.RollbackActionId is not null))
        {
            Assert.Contains(action.RollbackActionId!, ids);
        }
    }

    [Fact]
    public void The_registry_takes_the_whole_catalogue_and_refuses_the_unreleased_ones()
    {
        var registry = new ActionRegistry(Catalog);

        Assert.Equal(Catalog.Count, registry.All.Count);

        var released = registry.Validate(new ActionRequest { ActionId = "System.FsutilDeleteNotifyQuery" });
        Assert.True(released.IsValid);

        var unreleased = registry.Validate(new ActionRequest { ActionId = "System.DismRestoreHealth" });
        Assert.False(unreleased.IsValid);
        Assert.Equal(BlockReasons.ActionNotAllowed, unreleased.ReasonCode);
    }

    [Fact]
    public void A_released_query_is_approved_by_the_registry_without_any_argument()
    {
        var registry = new ActionRegistry(Catalog);

        var result = registry.Validate(new ActionRequest { ActionId = "Network.IpConfigAll" });

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "/all" }, result.ResolvedArguments);
    }

    [Fact]
    public void A_volume_argument_of_the_disk_check_is_validated_before_it_could_be_used()
    {
        // The check itself is not released yet, but its argument shape is already the guarded one:
        // a drive letter passes, a path with a parent directory or with shell characters does not.
        var registry = new ActionRegistry(Catalog);

        var allowed = registry.Validate(new ActionRequest
        {
            ActionId = "Volume.ChkdskScan",
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["Volume"] = "C:" },
        });

        // Not released, so this is refused - but with the reason "not allowed", not "invalid arguments".
        Assert.False(allowed.IsValid);
        Assert.Equal(BlockReasons.ActionNotAllowed, allowed.ReasonCode);

        var catalogueWithoutRelease = new ActionRegistry(
            Catalog.Select(action => action.Id == "Volume.ChkdskScan" ? action with { Allowed = true } : action));

        var traversal = catalogueWithoutRelease.Validate(new ActionRequest
        {
            ActionId = "Volume.ChkdskScan",
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["Volume"] = @"..\..\Windows" },
        });

        Assert.False(traversal.IsValid);
        Assert.Equal(BlockReasons.ActionPathNotAllowed, traversal.ReasonCode);

        var letter = catalogueWithoutRelease.Validate(new ActionRequest
        {
            ActionId = "Volume.ChkdskScan",
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["Volume"] = "C:" },
        });

        Assert.True(letter.IsValid);
        Assert.Equal(new[] { "C:", "/scan" }, letter.ResolvedArguments);
    }
}
