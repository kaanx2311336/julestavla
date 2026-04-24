sed -i 's/<TargetFramework>net9.0<\/TargetFramework>/<TargetFrameworks>net8.0;net9.0<\/TargetFrameworks>/' src/TavlaJules.Engine/TavlaJules.Engine.csproj
dotnet add src/TavlaJules.Data/TavlaJules.Data.csproj reference src/TavlaJules.Engine/TavlaJules.Engine.csproj
dotnet build src/TavlaJules.Data/TavlaJules.Data.csproj
