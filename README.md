# GTiff2Tiles

![Icon](Resources/Icon.png)

**GTiff2Tiles** is an analogue of [gdal2tiles.py](https://github.com/OSGeo/gdal/blob/master/gdal/swig/python/scripts/gdal2tiles.py) and [MapTiler](https://www.maptiler.com/) on **C#** for creating tiles.

[![build-test-deploy](https://github.com/Gigas002/GTiff2Tiles/actions/workflows/build-test-deploy.yml/badge.svg)](https://github.com/Gigas002/GTiff2Tiles/actions/workflows/build-test-deploy.yml)
[![codecov](https://codecov.io/gh/Gigas002/GTiff2Tiles/branch/master/graph/badge.svg)](https://codecov.io/gh/Gigas002/GTiff2Tiles)
[![Maintainability](https://api.codeclimate.com/v1/badges/f01b570988c070e70cc9/maintainability)](https://codeclimate.com/github/Gigas002/GTiff2Tiles/maintainability)

[![Release](https://img.shields.io/github/v/release/Gigas002/GTiff2tiles?include_prereleases)](https://github.com/Gigas002/GTiff2Tiles/releases)
[![NuGet](https://img.shields.io/nuget/vpre/GTiff2Tiles)](https://www.nuget.org/packages/GTiff2Tiles/)
[![Docker](https://img.shields.io/docker/v/gigas002/gtiff2tiles-console
)](https://hub.docker.com/r/gigas002/gtiff2tiles-console)

## Project state

I'm not developing this project actively, so don't expect any new features. I'm keeping it alive, merging some dependencies bump stuff and will review any PRs that are suggested. There's a cross-platofrm GUI Avalonia app WIP, so you can contribute here, if you really want to

## Versions and .NET Framework support

Use version `1.4.1` if you need .NET Framework support, otherwise it's **strongly recommended** to build library from source code yourself, or use latest build from github [releases](https://github.com/Gigas002/GTiff2Tiles/releases) page, pre-release package on [nuget](https://www.nuget.org/packages/GTiff2Tiles/) and [docker hub](https://hub.docker.com/r/gigas002/gtiff2tiles-console)

## Documentation

Docs for latest version are available to browse on [GitHub Pages](https://gigas002.github.io/GTiff2Tiles/)

Outdated docs for release 1.4.x Core's API are available on [GitHub Wiki](https://github.com/Gigas002/GTiff2Tiles/wiki)

## Examples

In [Examples](https://github.com/Gigas002/GTiff2Tiles/tree/master/Examples) directory you can find **GeoTIFFs** for some tests

## Image server

This repository also contains `GTiff2Tiles.Server`, a pragmatic ASP.NET Core image server for local development.

> [!WARNING]
> The admin UI is intended for trusted local use only. It has no authentication/authorization, so do not expose it directly to untrusted users or networks.

It provides:
- a Razor Pages + HTMX admin UI for creating catalogs
- GeoTIFF upload and normalization to Web Mercator (`EPSG:3857`)
- one active GeoTIFF per catalog in v1
- on-demand XYZ tile rendering at routes like `/{catalogSlug}/{z}/{x}/{y}`
- SQLite metadata storage plus disk-backed raster/tile storage under `App_Data` by default
- configurable large-upload request limits for GeoTIFF workflows (10 GiB by default)

Run it with:

```bash
dotnet run --project GTiff2Tiles.Server
```

Then open the printed local URL, create a catalog, upload a GeoTIFF, and request tiles from the catalog slug route.

## Console viewer option

The CLI can also generate a simple slippy-map QA page in the tile output root:

```bash
GTiff2Tiles.Console -i input.tif -o output --minz 0 --maxz 12 --coordinates mercator --tms false --slippymap-html true
```

When enabled, the console writes `slippy-map.html` into the output directory. The page uses OpenStreetMap as the base layer, overlays the generated tiles via relative `./{z}/{x}/{y}` paths, and includes an opacity slider for alignment checks. The generated HTML also sets a referrer policy so OSM tiles are more likely to load correctly from a local preview server.

This viewer is only supported for Web Mercator / XYZ output (`--coordinates mercator --tms false`), because that is the addressing and projection expected by OpenStreetMap and Leaflet.

## Contributing

Feel free to contribute if you want to, I'll review everything

## License

Project is licensed under [WTFPL](LICENSE.txt) license

## 3rd party resources

Icon is provided by [Google’s material design](https://material.io/tools/icons/?icon=image&style=baseline) and is used in all of **GTiff2Tiles** projects.

Examples GeoTIFFs for Tokyo were downloaded using free & opensource [SAS Planet](http://www.sasgis.org/download/) from [bing maps](https://www.bing.com/maps).
