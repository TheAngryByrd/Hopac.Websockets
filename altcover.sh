#!/bin/bash

pushd ./tests/Hopac.Websockets.Tests

rm -rf bin obj __Instrumented coverage.xml
dotnet build -c Debug

dotnet run --project ~/.nuget/packages/altcover/2.0.330/tools/netcoreapp2.0/AltCover/altcover.core.fsproj -- \
    -i bin/Debug/netcoreapp2.0 --opencover

cp -rf ./__Instrumented/* ./bin/Debug/netcoreapp2.0

dotnet run --project ~/.nuget/packages/altcover/2.0.330/tools/netcoreapp2.0/AltCover/altcover.core.fsproj --configuration Release -- \
    runner -x "dotnet" -w "." -r "bin/Debug/netcoreapp2.0" -- \
    run --no-build --configuration Debug -f netcoreapp2.0


popd

