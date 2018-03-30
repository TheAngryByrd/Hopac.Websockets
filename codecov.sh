#!/bin/bash
# -*- coding: utf-8 -*-


./build.sh Rebuild

pushd tools

dotnet restore
# Instrument assemblies inside 'test' folder to detect hits for source files inside 'src' folder


dotnet minicover instrument --workdir ../ --assemblies tests/**/bin/**/*.exe --sources src/**/*.fs 
dotnet minicover instrument --workdir ../ --assemblies tests/**/bin/**/*.dll --sources src/**/*.fs 

# Reset hits count in case minicover was run for this project
dotnet minicover reset

popd

pushd tests/Hopac.Websockets.Tests

dotnet run -f netcoreapp2.0 --no-build

# Need libuv, ignore for now
dotnet mono -f net461 --no-build

popd

pushd tools

dotnet minicover uninstrument --workdir ../
dotnet minicover report --workdir ../ 
dotnet minicover htmlreport --workdir ../ 

popd
