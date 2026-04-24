#!/bin/bash
DOTNET_ROLL_FORWARD=Major dotnet test src/TavlaJules.Engine.Tests/TavlaJules.Engine.Tests.csproj --filter "FullyQualifiedName~GameState"
