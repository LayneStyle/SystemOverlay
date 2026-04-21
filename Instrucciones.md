# Development (requires admin — IDE should prompt UAC)
dotnet run --project SystemOverlay/SystemOverlay.csproj

# Publish standalone .exe
dotnet publish SystemOverlay/SystemOverlay.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
