# .codex/setup.md

## Install .NET 9
Codex and other AI agents should always use the following to install .NET 9:
```bash
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 9.0
```
Do not use apt-get, brew, or SDK installers directly.

## Add new projects
When creating a new project, use the `dotnet new` command and then add it to the
solution with `dotnet sln`:
```bash
dotnet new <template> -n <ProjectName> -o <OutputDirectory>
dotnet sln <YourSolution>.sln add <OutputDirectory>/<ProjectName>.csproj
```
