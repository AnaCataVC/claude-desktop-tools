using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;
using ClaudeDesktopTools.ViewModels;
using Xunit;

namespace ClaudeDesktopTools.Tests;

public class ContextDiscoveryViewModelTests
{
    private static ClaudeDiscoveryCandidate MakeCandidate(string category, bool tracked = false, bool selected = true)
        => new()
        {
            FilePath = $@"C:\repo\{category}\{Guid.NewGuid():N}.md",
            RelativePath = $@"{category}\file.md",
            Category = category,
            IsTrackedByGit = tracked,
            IsSelected = selected
        };

    private static ContextDiscoveryViewModel CreateViewModel(IEnumerable<ClaudeDiscoveryCandidate> candidates)
        => new(new FakeDiscoveryService(candidates), new FakeDriveSyncService());

    [Fact]
    public async Task Discover_GroupsCandidatesInDisplayOrder_SkippingEmptyCategories()
    {
        var candidates = new[]
        {
            MakeCandidate(ClaudeDiscoveryCategory.Hook),
            MakeCandidate(ClaudeDiscoveryCategory.Context),
            MakeCandidate(ClaudeDiscoveryCategory.Skill)
        };
        var vm = CreateViewModel(candidates);

        await vm.DiscoverAsync();

        var categoriesInOrder = vm.GroupedCandidates.Select(g => g.Category).ToArray();
        Assert.Equal(
            new[] { ClaudeDiscoveryCategory.Context, ClaudeDiscoveryCategory.Skill, ClaudeDiscoveryCategory.Hook },
            categoriesInOrder);
    }

    [Fact]
    public async Task ApplyCategoryFilter_HidingCategory_RemovesGroupButKeepsFullCandidateList()
    {
        var candidates = new[]
        {
            MakeCandidate(ClaudeDiscoveryCategory.Skill),
            MakeCandidate(ClaudeDiscoveryCategory.Agent)
        };
        var vm = CreateViewModel(candidates);
        await vm.DiscoverAsync();

        var agentFilter = vm.CategoryFilters.Single(f => f.Category == ClaudeDiscoveryCategory.Agent);
        agentFilter.IsChecked = false;

        Assert.DoesNotContain(vm.GroupedCandidates, g => g.Category == ClaudeDiscoveryCategory.Agent);
        Assert.Contains(vm.GroupedCandidates, g => g.Category == ClaudeDiscoveryCategory.Skill);
        Assert.Equal(2, vm.Candidates.Count);
    }

    [Fact]
    public async Task ApplyCategoryFilter_TogglingFilterOffAndOn_PreservesSameGroupInstanceAndExpandedState()
    {
        var candidates = new[]
        {
            MakeCandidate(ClaudeDiscoveryCategory.Skill),
            MakeCandidate(ClaudeDiscoveryCategory.Agent)
        };
        var vm = CreateViewModel(candidates);
        await vm.DiscoverAsync();

        var skillGroupBefore = vm.GroupedCandidates.Single(g => g.Category == ClaudeDiscoveryCategory.Skill);
        skillGroupBefore.IsExpanded = false;

        var skillFilter = vm.CategoryFilters.Single(f => f.Category == ClaudeDiscoveryCategory.Skill);
        skillFilter.IsChecked = false;
        Assert.DoesNotContain(vm.GroupedCandidates, g => g.Category == ClaudeDiscoveryCategory.Skill);

        skillFilter.IsChecked = true;
        var skillGroupAfter = vm.GroupedCandidates.Single(g => g.Category == ClaudeDiscoveryCategory.Skill);

        Assert.Same(skillGroupBefore, skillGroupAfter);
        Assert.False(skillGroupAfter.IsExpanded);
    }

    [Fact]
    public async Task ShowAllCategories_RestoresEveryHiddenGroup()
    {
        var candidates = new[]
        {
            MakeCandidate(ClaudeDiscoveryCategory.Skill),
            MakeCandidate(ClaudeDiscoveryCategory.Agent),
            MakeCandidate(ClaudeDiscoveryCategory.Hook)
        };
        var vm = CreateViewModel(candidates);
        await vm.DiscoverAsync();

        foreach (var filter in vm.CategoryFilters) filter.IsChecked = false;
        Assert.Empty(vm.GroupedCandidates);

        vm.ShowAllCategoriesCommand.Execute(null);

        Assert.Equal(3, vm.GroupedCandidates.Count);
    }

    [Fact]
    public async Task SelectOnlyCategory_SelectsOnlyThatCategorysCandidates()
    {
        var skillA = MakeCandidate(ClaudeDiscoveryCategory.Skill, selected: false);
        var skillB = MakeCandidate(ClaudeDiscoveryCategory.Skill, selected: false);
        var agentA = MakeCandidate(ClaudeDiscoveryCategory.Agent, selected: true);
        var vm = CreateViewModel(new[] { skillA, skillB, agentA });
        await vm.DiscoverAsync();

        vm.SelectOnlyCategory(ClaudeDiscoveryCategory.Skill);

        Assert.True(skillA.IsSelected);
        Assert.True(skillB.IsSelected);
        Assert.False(agentA.IsSelected);
    }

    [Fact]
    public async Task DeselectGitTrackedCommand_DeselectsOnlyTrackedCandidates_LeavesOthersUntouched()
    {
        var trackedSkill = MakeCandidate(ClaudeDiscoveryCategory.Skill, tracked: true, selected: true);
        var untrackedSkill = MakeCandidate(ClaudeDiscoveryCategory.Skill, tracked: false, selected: true);
        var untrackedAgentAlreadyOff = MakeCandidate(ClaudeDiscoveryCategory.Agent, tracked: false, selected: false);
        var vm = CreateViewModel(new[] { trackedSkill, untrackedSkill, untrackedAgentAlreadyOff });
        await vm.DiscoverAsync();

        vm.DeselectGitTrackedCommand.Execute(null);

        Assert.False(trackedSkill.IsSelected);
        Assert.True(untrackedSkill.IsSelected);
        Assert.False(untrackedAgentAlreadyOff.IsSelected);
    }

    [Fact]
    public void CandidateGroup_IsExpanded_RaisesPropertyChangedWhenValueChanges()
    {
        var group = new CandidateGroup(ClaudeDiscoveryCategory.Skill, Array.Empty<ClaudeDiscoveryCandidate>());
        var raised = false;
        ((INotifyPropertyChanged)group).PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(CandidateGroup.IsExpanded);

        group.IsExpanded = false;

        Assert.True(raised);
    }

    [Fact]
    public void CandidateGroup_IsExpanded_DoesNotRaisePropertyChangedWhenSetToSameValue()
    {
        var group = new CandidateGroup(ClaudeDiscoveryCategory.Skill, Array.Empty<ClaudeDiscoveryCandidate>());
        var raiseCount = 0;
        ((INotifyPropertyChanged)group).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CandidateGroup.IsExpanded)) raiseCount++;
        };

        group.IsExpanded = true; // already true by default

        Assert.Equal(0, raiseCount);
    }

    [Fact]
    public void CategoryFilterOption_IsChecked_RaisesPropertyChangedWhenValueChanges()
    {
        var filter = new CategoryFilterOption(ClaudeDiscoveryCategory.Skill);
        var raised = false;
        filter.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(CategoryFilterOption.IsChecked);

        filter.IsChecked = false;

        Assert.True(raised);
    }

    private sealed class FakeDiscoveryService : IClaudeConfigDiscoveryService
    {
        private readonly List<ClaudeDiscoveryCandidate> _candidates;

        public FakeDiscoveryService(IEnumerable<ClaudeDiscoveryCandidate> candidates)
        {
            _candidates = candidates.ToList();
        }

        public Task<ClaudeDiscoveryReport> DiscoverAsync(string rootPath, int maxDepth = 4, CancellationToken cancellationToken = default)
            => Task.FromResult(new ClaudeDiscoveryReport
            {
                Candidates = _candidates,
                RepositoriesScanned = 1,
                UntrackedCandidatesCount = _candidates.Count(c => !c.IsTrackedByGit)
            });

        public Task<string?> GetGitRepoRootAsync(string directory, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);

        public Task<HashSet<string>> GetTrackedFilesAsync(string repoRoot, IEnumerable<string> relativeFilePaths, CancellationToken cancellationToken = default)
            => Task.FromResult(new HashSet<string>());
    }

    private sealed class FakeDriveSyncService : IDriveSyncService
    {
        public DriveSyncSettings Settings { get; } = new();
        public bool IsConfigured => false;

        public void UpdateSettings(DriveSyncSettings settings)
        {
        }

        public Task<DriveSyncResult> TestConnectionAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DriveSyncResult { Message = "ok" });

        public Task<DriveSyncResult> SyncCandidatesAsync(IEnumerable<ClaudeDiscoveryCandidate> candidates, IProgress<DriveSyncProgress>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new DriveSyncResult { Uploaded = candidates.Count(), Message = "done" });
    }
}
