# Regression Checks

Run on Windows with .NET 10 and Git installed:

```powershell
dotnet run --project tests/SourceGit.Regression -c Debug
```

The checks use Avalonia Headless and temporary repositories to cover missing HEAD branches, empty repositories, detached checkout, generated branch names, filter colors, submodule colors, graph rendering, tracking-pair badge zoom, and branch-name mismatch confirmation before push. Push tests use a local bare remote to verify cancellation, closing the dialog, explicit confirmation, force push, SHA pushes, and tag operations without network access. Application settings are isolated in a temporary directory; rendered `graph-preview.png`, `tracking-pair-zoom-preview.png`, and `push-warning-preview.png` files are saved there. The final output prints the directory path.
