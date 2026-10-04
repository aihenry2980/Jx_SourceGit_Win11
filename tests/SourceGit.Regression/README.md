# Regression Checks

Run on Windows with .NET 10 and Git installed:

```powershell
dotnet run --project tests/SourceGit.Regression -c Debug
```

To run just the custom action checks:

```powershell
dotnet run --project tests/SourceGit.Regression -c Debug -- --custom-actions
```

Custom action checks run a local BAT and child process with no network access. They verify that repository refreshes wait until the action ends, the UI continues processing events during heavy stdout/stderr output, Cancel task terminates the child, and a failed launch releases the watcher and permits another action.

The checks use Avalonia Headless and temporary repositories to cover missing HEAD branches, empty repositories, detached checkout, generated branch names, filter colors, submodule colors, graph rendering, tracking-pair badge zoom, and branch-name mismatch confirmation before push. Push tests use a local bare remote to verify cancellation, closing the dialog, explicit confirmation, force push, SHA pushes, and tag operations without network access. Application settings are isolated in a temporary directory; rendered `graph-preview.png`, `tracking-pair-zoom-preview.png`, and `push-warning-preview.png` files are saved there. The final output prints the directory path.
