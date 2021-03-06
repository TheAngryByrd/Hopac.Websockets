# Hopac.Websockets

A threadsafe [Hopac](https://github.com/Hopac/Hopac) wrapper around [dotnet websockets](https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket?view=netcore-2.0).


### Why? 

Dotnet websockets only allow for one receive and one send at a time. If multiple threads try to write to a websocket, it will throw a `System.InvalidOperationException` with the message `There is already one outstanding 'SendAsync' call for this WebSocket instance. ReceiveAsync and SendAsync can be called simultaneously, but at most one outstanding operation for each of them is allowed at the same time.`. This wraps a websocket in a hopac server-client model that allows for multiple threads to write or read at the same time. See https://docs.microsoft.com/en-us/dotnet/api/system.net.websockets.websocket.sendasync?view=netcore-2.0#Remarks

---

## Builds

MacOS/Linux | Windows
--- | ---
[![Travis Badge](https://travis-ci.org/TheAngryByrd/Hopac.Websockets.svg?branch=master)](https://travis-ci.org/TheAngryByrd/Hopac.Websockets) | [![Build status](https://ci.appveyor.com/api/projects/status/github/TheAngryByrd/Hopac.Websockets?svg=true)](https://ci.appveyor.com/project/TheAngryByrd/Hopac-Websockets)
[![Build History](https://buildstats.info/travisci/chart/TheAngryByrd/Hopac.Websockets)](https://travis-ci.org/TheAngryByrd/Hopac.Websockets/builds) | [![Build History](https://buildstats.info/appveyor/chart/TheAngryByrd/Hopac-Websockets)](https://ci.appveyor.com/project/TheAngryByrd/Hopac-Websockets)  


## Nuget 

Package | Stable | Prerelease
--- | --- | ---
Hopac.Websockets | [![NuGet Badge](https://buildstats.info/nuget/Hopac.Websockets)](https://www.nuget.org/packages/Hopac.Websockets/) | [![NuGet Badge](https://buildstats.info/nuget/Hopac.Websockets?includePreReleases=true)](https://www.nuget.org/packages/Hopac.Websockets/)
Hopac.Websockets.AspNetCore | [![NuGet Badge](https://buildstats.info/nuget/Hopac.Websockets.AspNetCore)](https://www.nuget.org/packages/Hopac.Websockets.AspNetCore/)  | [![NuGet Badge](https://buildstats.info/nuget/Hopac.Websockets.AspNetCore?includePreReleases=true)](https://www.nuget.org/packages/Hopac.Websockets.AspNetCore/)

---

### Building


Make sure the following **requirements** are installed in your system:

* [dotnet SDK](https://www.microsoft.com/net/download/core) 2.0 or higher
* [Mono](http://www.mono-project.com/) if you're on Linux or macOS.

```
> build.cmd // on windows
$ ./build.sh  // on unix
```


### Watch Tests

The `WatchTests` target will use [dotnet-watch](https://github.com/aspnet/Docs/blob/master/aspnetcore/tutorials/dotnet-watch.md) to watch for changes in your lib or tests and re-run your tests on all `TargetFrameworks`

```
./build.sh WatchTests
```

### Releasing

* [Add your nuget API key to paket](https://fsprojects.github.io/Paket/paket-config.html#Adding-a-NuGet-API-key)

```
paket config add-token "https://www.nuget.org" 4003d786-cc37-4004-bfdf-c4f3e8ef9b3a
```


* Then update the `RELEASE_NOTES.md` with a new version, date, and release notes [ReleaseNotesHelper](https://fsharp.github.io/FAKE/apidocs/fake-releasenoteshelper.html)

```
#### 0.2.0 - 2017-04-20
* FEATURE: Does cool stuff!
* BUGFIX: Fixes that silly oversight
```

* You can then use the `Release` target.  This will:
    * make a commit bumping the version:  `Bump version to 0.2.0` and add the release notes to the commit
    * publish the package to nuget
    * push a git tag

```
./build.sh Release
```
