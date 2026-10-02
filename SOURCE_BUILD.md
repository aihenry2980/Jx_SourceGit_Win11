# Building the Full Source Package

Download the release asset named `sourcegit_<version>.source-with-submodules.zip`.
GitHub's automatic "Source code" ZIP and TAR downloads do not include submodule
contents and are not the full source package.

Extract the archive and open its `JxSourceGit-<version>` directory. All submodules
are included at the exact revisions used by the release, recorded in
`source-manifest.json`. There is no need to clone or run any Git commands.

Install .NET SDK 10. NuGet access is required for the initial package restore.
Build on Windows with:

```powershell
dotnet build SourceGit.slnx -c Debug
```

Alternatively, open `SourceGit.slnx` in Visual Studio 2026 with .NET 10 support.
The `SOURCE_VERSION` file supplies version metadata without requiring Git.

To publish a self-contained Windows build without native AOT build tools:

```powershell
dotnet publish src/SourceGit.csproj -c Release -r win-x64 --self-contained true -p:DisableAOT=true -o artifacts/SourceGit
```

The default native AOT release also requires the Visual C++ build tools and
Windows SDK. The repository's `build/scripts/release.win.cmd` is intended for
maintainers with a Git checkout; use the build commands above for extracted source.
