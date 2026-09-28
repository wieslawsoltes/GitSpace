using GitSpace.Core;
using GitSpace.Controls.Uno;
using GitSpace.Hosting.GitHub;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace GitSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    private ContentDialog Dialog(string title, object content, string primary = "OK") => new()
    {
        XamlRoot = XamlRoot, Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Primary, RequestedTheme = RequestedTheme,
        Background = GitTheme.Brush(GitTheme.Current.Panel), Foreground = GitTheme.Brush(GitTheme.Current.Text),
        BorderBrush = GitTheme.Brush(GitTheme.Current.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6)
    };
    private async Task<bool> ShowAsync(ContentDialog dialog)
    {
        if (_dialogOpen) return false; _dialogOpen = true;
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary; }
        finally { _dialogOpen = false; }
    }
    private async Task<string?> PromptAsync(string title, string label, string value = "", string explanation = "", string primary = "Continue")
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 340, MaxWidth = 560 }; var input = GitTheme.Input(label); input.Text = value; AutomationProperties.SetName(input, label);
        panel.Children.Add(GitTheme.Label(label, 12, bold: true)); panel.Children.Add(input);
        if (explanation.Length != 0) { var note = GitTheme.Label(explanation, 12, true); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note); }
        var dialog = Dialog(title, panel, primary); dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        return await ShowAsync(dialog) ? input.Text.Trim() : null;
    }
    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var text = GitTheme.Label(message, 13); text.TextWrapping = TextWrapping.Wrap; text.MaxWidth = 500; text.MinWidth = 330;
        return await ShowAsync(Dialog(title, text, "Confirm"));
    }
    private async Task NoticeAsync(string title, string message)
    {
        var text = GitTheme.Label(message, 13); text.TextWrapping = TextWrapping.Wrap; text.IsTextSelectionEnabled = true; text.MaxWidth = 570;
        var dialog = Dialog(title, new ScrollViewer { Content = text, MaxHeight = 460 }, "Close"); dialog.CloseButtonText = ""; await ShowAsync(dialog);
    }
    private async Task<string?> ChooseAsync(string title, IEnumerable<string> choices, string primary, string explanation = "")
    {
        var values = choices.ToArray(); var panel = new StackPanel { Spacing = 10, MinWidth = 350, MaxWidth = 520 }; var filter = GitTheme.Input("Filter branches or entries"); panel.Children.Add(filter);
        var list = new ListView { ItemsSource = values, Height = Math.Clamp(values.Length * 36 + 8, 120, 300), SelectionMode = ListViewSelectionMode.Single };
        AutomationProperties.SetName(list, title + " choices"); panel.Children.Add(list); if (values.Length != 0) list.SelectedIndex = 0;
        filter.TextChanged += (_, _) => { list.ItemsSource = values.Where(v => v.Contains(filter.Text, StringComparison.OrdinalIgnoreCase)).ToArray(); list.SelectedIndex = 0; };
        if (explanation.Length != 0) { var text = GitTheme.Label(explanation, 12, true); text.TextWrapping = TextWrapping.Wrap; panel.Children.Add(text); }
        return await ShowAsync(Dialog(title, panel, primary)) ? list.SelectedItem as string : null;
    }
    private async Task RepositoryPickerAsync()
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 360 }; panel.Children.Add(GitTheme.Label(_platform.IsBrowser ? "Open a repository stored in this browser" : "Open a local Git repository", 13, bold: true));
        var root = GitTheme.Input(_platform.IsBrowser ? "Repository name" : "Full repository path"); root.Text = Snapshot.Root; AutomationProperties.SetName(root, "Repository path or name"); panel.Children.Add(root);
        var note = GitTheme.Label(_platform.IsBrowser ? "Browser repositories use isolated browser storage, not your computer's working folders. New and Clone are available from the File menu." : "Only open repositories you trust. Git filters and configured credential helpers may execute programs. Repository hooks are disabled by GitSpace.", 12, true); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        var dialog = Dialog("Current repository", panel, "Open repository");
        if (await ShowAsync(dialog)) await ExecuteAsync(new("open") { Root = root.Text.Trim() });
    }
    private async Task OpenRepositoryAsync(string operation)
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 390, MaxWidth = 560 }; var root = GitTheme.Input(_platform.IsBrowser ? "Repository name" : "New, empty destination directory"); AutomationProperties.SetName(root, "Repository destination"); panel.Children.Add(GitTheme.Label(_platform.IsBrowser ? "Repository name" : "Destination directory", 12, bold: true)); panel.Children.Add(root);
        TextBox? remote = null;
        if (operation == "clone") { remote = GitTheme.Input("https://github.com/owner/repository.git"); AutomationProperties.SetName(remote, "Clone URL"); panel.Children.Add(GitTheme.Label("HTTPS clone URL", 12, bold: true)); panel.Children.Add(remote); }
        var explanation = GitTheme.Label(_platform.IsBrowser ? "Stored in this browser. GitHub network operations need a trusted CORS proxy in Preferences. No third-party proxy is selected automatically. Your proxy can see repository contents and any token sent through it." : "Requires Git on PATH. Only clone repositories you trust; configured filters and credential helpers can execute programs. Existing non-empty directories are never overwritten.", 12, true); explanation.TextWrapping = TextWrapping.Wrap; panel.Children.Add(explanation);
        if (await ShowAsync(Dialog(operation == "clone" ? "Clone a repository" : "Create a new repository", panel, operation == "clone" ? "Clone" : "Create repository"))) await ExecuteAsync(new(operation) { Root = root.Text.Trim(), Value = remote?.Text.Trim() ?? "" });
    }
    private async Task BranchPickerAsync()
    {
        var selected = await ChooseAsync("Current branch", Snapshot.Branches, "Switch branch", _platform.IsBrowser ? "Browser branch switching requires a clean working directory. Commit or stash changes first." : "Git will refuse a switch that would overwrite local changes.");
        if (selected is not null && selected != Snapshot.Branch) await ExecuteAsync(new("checkout") { Value = selected });
    }
    private async Task NewBranchAsync()
    {
        var name = await PromptAsync("Create a branch", "Branch name", "feature/", "Create a branch from the current HEAD and switch to it.", "Create branch");
        if (name is not null) await ExecuteAsync(new("branch") { Value = name });
    }
    private async Task RenameBranchAsync()
    {
        var name = await PromptAsync("Rename current branch", "Branch name", Snapshot.Branch, "This renames only the local branch.", "Rename branch"); if (name is not null) await ExecuteAsync(new("renameBranch") { Value = name });
    }
    private async Task DeleteBranchAsync()
    {
        var name = await ChooseAsync("Delete local branch", Snapshot.Branches.Where(b => b != Snapshot.Branch), "Delete branch", "Only fully merged branches can be deleted. Remote branches are not deleted."); if (name is not null) await ExecuteAsync(new("deleteBranch") { Value = name, Confirm = true });
    }
    private async Task IntegrateAsync(string operation)
    {
        if (!_backend.Capabilities.Contains(operation)) throw new NotSupportedException("This operation requires the desktop backend.");
        var name = await ChooseAsync(operation == "merge" ? "Merge into " + Snapshot.Branch : "Rebase " + Snapshot.Branch, Snapshot.Branches.Where(b => b != Snapshot.Branch), operation == "merge" ? "Merge branch" : "Rebase branch", operation == "rebase" ? "Rebasing rewrites local commit IDs. Do not rebase shared commits without coordinating with collaborators." : _platform.IsBrowser ? "The browser supports fast-forward merges only. Diverged branches must be merged on desktop." : "Git will perform a fast-forward or merge commit. Conflicts are left for review, never silently resolved.");
        if (name is not null) await ExecuteAsync(new(operation) { Value = name, Confirm = true });
    }
    private async Task ConfirmCommandAsync(string operation, string message)
    {
        if (await ConfirmAsync(operation, message)) await ExecuteAsync(new(operation) { Confirm = true });
    }
    private async Task SynchronizeAsync() { if (Snapshot.Remotes.Length == 0) await AddRemoteAsync(); else await ExecuteAsync(new("fetch")); }
    private async Task AddRemoteAsync()
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 390 }; var name = GitTheme.Input("Remote name"); name.Text = "origin"; var url = GitTheme.Input("HTTPS repository URL");
        AutomationProperties.SetName(name, "Remote name"); AutomationProperties.SetName(url, "Remote URL"); panel.Children.Add(GitTheme.Label("Remote name", 12, bold: true)); panel.Children.Add(name); panel.Children.Add(GitTheme.Label("Repository URL", 12, bold: true)); panel.Children.Add(url);
        var note = GitTheme.Label("The remote must already exist. Adding a remote does not publish or push your commits. Embedded URL credentials are not allowed.", 12, true); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        if (await ShowAsync(Dialog("Add remote", panel, "Add remote"))) await ExecuteAsync(new("remote") { Path = name.Text.Trim(), Value = url.Text.Trim() });
    }
    private async Task NewFileAsync()
    {
        var name = await PromptAsync("Create a file", "Repository-relative path", "", "The file will appear in Changes. Binary files, .git metadata, symlinks and paths outside the repository are not editable.", "Create file");
        if (name is null) return;
        var existing = await _backend.ExecuteAsync(new("read") { Path = name }, _lifetime.Token);
        if (existing.Text.Length != 0 && !await ConfirmAsync("File already exists", "Open the existing file for editing? Its contents will not be replaced until you explicitly save.")) return;
        await EditTextAsync(name, existing.Text, true);
    }
    private async Task EditFileAsync()
    {
        if (_showHistory) throw new InvalidOperationException("Switch to Changes before editing the working file.");
        if (_activePath.Length == 0) return;
        var result = await _backend.ExecuteAsync(new("read") { Path = _activePath }, _lifetime.Token); await EditTextAsync(_activePath, result.Text, false);
    }
    private async Task EditTextAsync(string path, string contents, bool newFile)
    {
        var editor = GitTheme.Input("File contents", true); editor.Text = contents; editor.MinWidth = 500; editor.Height = 360; editor.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("monospace"); editor.FontSize = 13; editor.TextWrapping = TextWrapping.NoWrap;
        AutomationProperties.SetName(editor, "File editor"); var dialog = Dialog((newFile ? "Create: " : "Edit: ") + path, editor, "Save file");
        if (!await ShowAsync(dialog)) return;
        var current = await _backend.ExecuteAsync(new("read") { Path = path }, _lifetime.Token);
        if (current.Text != contents) throw new InvalidOperationException("This file changed while the editor was open. Reopen it to review the current content before saving.");
        await ExecuteAsync(new("write") { Path = path, Message = editor.Text }); _activePath = path; _showHistory = false; ApplyTabState(); _changes.SetFiles(Snapshot.Changes, path); await SelectFileAsync(path);
    }
    private async Task DiscardAsync()
    {
        var paths = _changes.SelectedPaths; if (paths.Length == 0) return;
        if (await ConfirmAsync("Discard selected changes", $"Restore {paths.Length} selected tracked files to HEAD, including their staged changes? This cannot be undone in GitSpace. Untracked files are not deleted.")) await ExecuteAsync(new("discard") { Paths = paths, Confirm = true });
    }
    private async Task StashAsync()
    {
        var message = await PromptAsync("Stash changes", "Stash description", "Saved in GitSpace", _platform.IsBrowser ? "Browser stash supports tracked files in loose-object repositories only. Untracked files must be committed first." : "Save tracked and untracked changes, leaving a clean working directory.", "Stash changes"); if (message is not null) await ExecuteAsync(new("stash") { Message = message });
    }
    private async Task StashesAsync()
    {
        if (Snapshot.Stashes.Length == 0) { await NoticeAsync("Stashed changes", "There are no stashes in this repository."); return; }
        var panel = new StackPanel { Spacing = 12, MinWidth = 370 }; var list = new ListView { ItemsSource = Snapshot.Stashes.Select(s => s.Id + "  " + s.Message).ToArray(), Height = Math.Clamp(Snapshot.Stashes.Length * 40, 100, 280), SelectedIndex = 0 }; panel.Children.Add(list);
        var note = GitTheme.Label("Apply keeps the stash as a backup. Drop permanently removes the selected stash entry.", 12, true); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        var dialog = Dialog("Stashed changes", panel, "Apply stash"); dialog.SecondaryButtonText = "Drop stash";
        if (_dialogOpen) return; _dialogOpen = true; ContentDialogResult result;
        try { result = await dialog.ShowAsync(); } finally { _dialogOpen = false; }
        if (list.SelectedIndex < 0 || result == ContentDialogResult.None) return; var stash = Snapshot.Stashes[list.SelectedIndex];
        if (result == ContentDialogResult.Primary) await ExecuteAsync(new("stashApply") { Value = stash.Id });
        else if (await ConfirmAsync("Drop stash", "Permanently remove " + stash.Id + "?")) await ExecuteAsync(new("stashDrop") { Value = stash.Id, Confirm = true });
    }
    private async Task TagAsync()
    {
        var name = await PromptAsync("Create lightweight tag", "Tag name", "", "Tag the current HEAD. Tags are local until explicitly pushed using Git.", "Create tag"); if (name is not null) await ExecuteAsync(new("tag") { Value = name });
    }
    private async Task AmendAsync()
    {
        if (Snapshot.Commits.Length == 0) return;
        var message = await PromptAsync("Amend latest commit", "Commit message", Snapshot.Commits[0].Message, "This rewrites the latest commit ID and includes selected working changes. Avoid amending commits already shared with others.", "Amend commit");
        if (message is not null) await ExecuteAsync(new("amend") { Paths = _changes.SelectedPaths, Message = message, Confirm = true });
    }
    private async Task HistoryActionAsync(string operation)
    {
        if (_selectedCommit is null) throw new InvalidOperationException("Choose a commit in History first.");
        if (await ConfirmAsync(operation == "revert" ? "Revert commit" : "Cherry-pick commit", _selectedCommit.Summary + "\n" + _selectedCommit.Id + "\n\nThis changes the current branch. Conflicts must be resolved before continuing.")) await ExecuteAsync(new(operation) { Value = _selectedCommit.Id, Confirm = true });
    }
    private async Task FindAsync()
    {
        var query = await PromptAsync("Find in diff", "Search text", "", "Searches the full diff and scrolls to the next matching line.", "Find next"); if (query is not null && !_diff.FindNext(query)) await NoticeAsync("Find in diff", "No matching line was found.");
    }
    private async Task ExportAsync()
    {
        if (!await ConfirmAsync("Export repository ZIP", "The ZIP includes working files and the complete .git directory. It may contain private history and configuration. Store it securely.")) return;
        var result = await _backend.ExecuteAsync(new("export"), _lifetime.Token); await _platform.DownloadAsync(Snapshot.Name + ".zip", result.Text);
    }
    private async Task PreferencesAsync()
    {
        var panel = new StackPanel { Spacing = 10, MinWidth = 410, MaxWidth = 540 };
        var author = GitTheme.Input("Author name"); author.Text = _preferences.Author; var email = GitTheme.Input("Author email"); email.Text = _preferences.Email;
        var token = new PasswordBox { Password = _token, MinHeight = 30, FontSize = 12, PlaceholderText = "Optional session-only GitHub access token" }; AutomationProperties.SetName(token, "Session GitHub access token");
        var proxy = GitTheme.Input("Optional trusted HTTPS CORS proxy"); proxy.Text = _proxy;
        foreach (var (label, input) in new (string, Control)[] { ("Git author name", author), ("Git author email", email), ("GitHub access token (this session only)", token), ("Browser Git CORS proxy (this session only)", proxy) }) { panel.Children.Add(GitTheme.Label(label, 12, bold: true)); panel.Children.Add(input); AutomationProperties.SetName(input, label); }
        var dark = new CheckBox { Content = "Dark appearance", IsChecked = _preferences.Dark, FontSize = 12 }; panel.Children.Add(dark);
        var warning = GitTheme.Label("Tokens are never saved in preferences or Git configuration. A configured proxy can see repository contents and any token sent through it; only use a proxy you control or trust. Desktop Git uses your installed credential helper, not this token. OAuth sign-in is not implemented.", 12, true); warning.TextWrapping = TextWrapping.Wrap; panel.Children.Add(warning);
        if (!await ShowAsync(Dialog("Preferences", new ScrollViewer { Content = panel, MaxHeight = 500 }, "Save preferences"))) return;
        if (proxy.Text.Trim().Length != 0) GitSafety.HttpsRemote(proxy.Text.Trim());
        if (string.IsNullOrWhiteSpace(author.Text) || string.IsNullOrWhiteSpace(email.Text)) throw new ArgumentException("An author name and email are required.");
        _preferences = _preferences with { Author = author.Text.Trim(), Email = email.Text.Trim(), Dark = dark.IsChecked == true }; _token = token.Password; _proxy = proxy.Text.Trim();
        GitTheme.Current = _preferences.Dark ? GitTheme.Dark : GitTheme.Light; Build(); await _platform.SavePreferencesAsync(_preferences);
    }
    private (string Owner, string Repository) GitHubRepository()
    {
        foreach (var remote in Snapshot.Remotes) if (GitHubClient.TryParseRemote(remote.Url, out var owner, out var repository)) return (owner, repository);
        throw new InvalidOperationException("Add an HTTPS github.com remote to use pull-request integration.");
    }
    private async Task ViewOnGitHubAsync() { var (owner, repository) = GitHubRepository(); await _platform.OpenExternalAsync("https://github.com/" + owner + "/" + repository); }
    private async Task PullRequestsAsync()
    {
        var (owner, repository) = GitHubRepository(); var requests = await _github.ListPullRequestsAsync(owner, repository, _token, _lifetime.Token);
        if (requests.Length == 0) { await NoticeAsync("Pull requests", "No open pull requests were returned."); return; }
        var choice = await ChooseAsync("Open pull requests (first 100)", requests.Select(p => "#" + p.Number + "  " + p.Title), "Open on GitHub");
        if (choice is null) return; var selected = requests.First(p => choice.StartsWith("#" + p.Number + "  ", StringComparison.Ordinal)); await _platform.OpenExternalAsync(selected.Url);
    }
    private async Task CreatePullRequestAsync()
    {
        var (owner, repository) = GitHubRepository(); var panel = new StackPanel { Spacing = 10, MinWidth = 410 }; var title = GitTheme.Input("Pull request title"); var target = GitTheme.Input("Base branch"); target.Text = "main"; var body = GitTheme.Input("Description", true); body.Height = 150;
        panel.Children.Add(GitTheme.Label("From " + Snapshot.Branch + " into", 12, true)); panel.Children.Add(target); panel.Children.Add(title); panel.Children.Add(body);
        var note = GitTheme.Label("Push the branch first. Creating a pull request sends the title and description to GitHub. A session token with pull-request write permission is required.", 12, true); note.TextWrapping = TextWrapping.Wrap; panel.Children.Add(note);
        if (!await ShowAsync(Dialog("Create pull request", panel, "Create pull request"))) return;
        var pr = await _github.CreatePullRequestAsync(owner, repository, title.Text, Snapshot.Branch, target.Text.Trim(), body.Text, _token, _lifetime.Token); await _platform.OpenExternalAsync(pr.Url);
    }
}
